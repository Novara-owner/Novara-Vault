(function () {
  'use strict';

  var VIEWER = window.NovaraViewer;
  var WEB = window.NovaraWeb;
  if (!VIEWER || !WEB) return;

  var utf8 = new TextEncoder();
  var EDIT_DEVICE = 'web-editor';



  var ctx = null;
  var dirty = false;
  var uploading = false;
  var editingEntryId = null;
  var editingNoteId = null;
  var editingDiaryId = null;
  var createKind = 'memo';
  var drafts = {};
  var conflictServerVersion = 0;
  var listenersWired = false;




  var editorCredential = false;

  function t(key) { return VIEWER.t(key); }

  function canEdit() { return !!ctx && editorCredential; }

  function failWith(key) { var e = new Error(key); e.i18nKey = key; return e; }



  function bytesToBase64(bytes) {
    var binary = '';
    for (var i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
    return btoa(binary);
  }

  function toHex(buffer) {
    var bytes = new Uint8Array(buffer); var out = '';
    for (var i = 0; i < bytes.length; i++) out += (bytes[i] < 16 ? '0' : '') + bytes[i].toString(16);
    return out;
  }

  function randomBytes(n) { var b = new Uint8Array(n); crypto.getRandomValues(b); return b; }

  function utcStamp() {
    return new Date().toISOString().replace(/\.\d{3}Z$/, 'Z');
  }

  async function gzipBytes(text) {
    var stream = new Blob([utf8.encode(text)]).stream().pipeThrough(new CompressionStream('gzip'));
    return new Uint8Array(await new Response(stream).arrayBuffer());
  }




  function buildHeader(salt) {
    var header = new Uint8Array(44);
    var view = new DataView(header.buffer);
    view.setUint32(0, 0x41564F4E, true);
    header[4] = 4; header[5] = 1; header[6] = 0; header[7] = 0;
    view.setInt32(8, 1000, true);
    header.set(salt, 12);
    return header;
  }

  async function deriveBlobKey(spaceKeyText, salt) {
    var material = await crypto.subtle.importKey('raw', utf8.encode(spaceKeyText), 'PBKDF2', false, ['deriveBits']);
    return new Uint8Array(await crypto.subtle.deriveBits(
      { name: 'PBKDF2', salt: salt, iterations: 1000, hash: 'SHA-256' }, material, 256));
  }







  async function sealGcmBody(body, spaceKeyText, salt, nonce) {
    var header = buildHeader(salt);
    var blobKey = await deriveBlobKey(spaceKeyText, salt);
    var key = await crypto.subtle.importKey('raw', blobKey, 'AES-GCM', false, ['encrypt']);
    var cipherTag = new Uint8Array(await crypto.subtle.encrypt(
      { name: 'AES-GCM', iv: nonce, additionalData: header, tagLength: 128 }, key, body));
    var tag = cipherTag.subarray(cipherTag.length - 16);
    var cipher = cipherTag.subarray(0, cipherTag.length - 16);
    var out = new Uint8Array(44 + 12 + 16 + cipher.length);
    out.set(header, 0); out.set(nonce, 44); out.set(tag, 56); out.set(cipher, 72);
    return out;
  }


  async function sealContainer(dataObject, spaceKeyText, opts) {
    var options = opts || {};
    var body = await gzipBytes(JSON.stringify(dataObject));
    var salt = options.salt || randomBytes(32);
    var nonce = options.nonce || randomBytes(12);
    return sealGcmBody(body, spaceKeyText, salt, nonce);
  }



  async function buildEnvelope(container, spaceKeyText, context, baseVersion) {
    var payload = bytesToBase64(container);
    var digest = toHex(await crypto.subtle.digest('SHA-256', container));
    var envelope = {
      sync: 1, crypto: 4,
      space: context.spaceId,
      version: baseVersion + 1, base: baseVersion,
      device: EDIT_DEVICE,
      ts: utcStamp(),
      payloadSha256: digest,
    };
    var canonical = WEB.canonicalEnvelope(envelope);
    var macKey = await WEB.deriveMacKey(spaceKeyText);
    var key = await crypto.subtle.importKey('raw', macKey, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
    envelope.mac = bytesToBase64(new Uint8Array(await crypto.subtle.sign('HMAC', key, utf8.encode(canonical))));
    envelope.payload = payload;
    return envelope;
  }



  function markDirty() {
    dirty = true;
    renderSyncBar();
  }


  function renderSyncBar() {
    var hint = document.getElementById('editSyncHint');
    var upload = document.getElementById('editUploadButton');
    var hasContext = !!ctx;


    if (hint) {
      hint.hidden = !hasContext;
      hint.textContent = !canEdit() ? t('editReadOnlyHint')
        : conflictServerVersion !== 0 ? t('editConflictBar')
        : dirty ? t('editDirtyHint') : t('editCleanHint');
    }
    if (upload) {

      upload.hidden = !canEdit() || !dirty || conflictServerVersion !== 0;
      upload.disabled = uploading;
    }
  }

  function draftKey(kind, id, index) { return kind + '|' + id + '|' + index; }



  function editButtonHtml(kind, id) {
    if (!canEdit()) return '';
    return '<div class="edit-actions"><button class="edit-button" type="button" data-editor-action="' + kind +
      '" data-editor-id="' + escapeAttr(id) + '">' + escape('editItem') + '</button>' +
      '<button class="edit-button is-danger" type="button" data-editor-action="delete-item" data-editor-kind="' + kind +
      '" data-editor-id="' + escapeAttr(id) + '">' + escape('deleteItem') + '</button></div>';
  }

  function entryFieldsHtml(entry, defaultHtml) {
    if (editingEntryId !== entry.id) return defaultHtml + editButtonHtml('edit-entry', entry.id);
    var rows = '';
    (entry.fields || []).forEach(function (field, index) {
      if (field.label === 'TOTP') return;
      var key = draftKey('field', entry.id, index);
      var value = Object.prototype.hasOwnProperty.call(drafts, key) ? drafts[key] : field.value;
      rows += '<label class="edit-field"><span class="edit-field-name">' + escapeText(field.label) + '</span>' +
        '<input class="edit-input" type="text" data-editor-input="field" data-editor-id="' + escapeAttr(entry.id) +
        '" data-editor-index="' + index + '" value="' + escapeAttr(value) + '" autocomplete="off" /></label>';
    });
    rows += '<div class="edit-actions">' +
      '<button class="edit-button is-primary" type="button" data-editor-action="save-entry" data-editor-id="' + escapeAttr(entry.id) + '">' + escape('saveItem') + '</button>' +
      '<button class="edit-button" type="button" data-editor-action="cancel-entry" data-editor-id="' + escapeAttr(entry.id) + '">' + escape('cancelEdit') + '</button>' +
      '</div>';
    return '<div class="edit-fields">' + rows + '</div>';
  }

  function todoHtml(item, expanded, defaultHtml) {
    if (editingNoteId || editingDiaryId) return defaultHtml;
    if (!canEdit()) return defaultHtml;
    var rows = '';
    rows += checkboxRow(item.id, -1, item.mainText, item.checkedStates.every(function (v) { return v; }) && item.subTexts.length > 0, 'subtask' + (item.subTexts.length > 0 && item.checkedStates.every(function (v) { return v; }) ? ' is-done' : ''));
    if (expanded) {
      item.subTexts.forEach(function (text, index) {
        rows += checkboxRow(item.id, index, text, item.checkedStates[index] === true, 'subtask sub-indent' + (item.checkedStates[index] ? ' is-done' : ''));
      });
    }
    return '<div class="subtasks">' + rows + '</div>';
  }

  function checkboxRow(id, index, label, checked, cls) {
    return '<div class="' + cls + '"><label class="edit-check"><input type="checkbox" data-editor-input="todo" data-editor-id="' + escapeAttr(id) +
      '" data-editor-index="' + index + '"' + (checked ? ' checked' : '') + ' /><span>' + escapeText(label) + '</span></label></div>';
  }

  function noteHtml(item, defaultHtml) {
    if (editingNoteId === item.id) {
      var key = draftKey('note', item.id, 0);
      var value = Object.prototype.hasOwnProperty.call(drafts, key) ? drafts[key] : item.content;
      return '<div class="edit-note"><textarea class="edit-input" rows="6" data-editor-input="note" data-editor-id="' + escapeAttr(item.id) + '">' +
        escapeText(value) + '</textarea>' +
        '<div class="edit-actions">' +
        '<button class="edit-button is-primary" type="button" data-editor-action="save-note" data-editor-id="' + escapeAttr(item.id) + '">' + escape('saveItem') + '</button>' +
        '<button class="edit-button" type="button" data-editor-action="cancel-note" data-editor-id="' + escapeAttr(item.id) + '">' + escape('cancelEdit') + '</button>' +
        '</div></div>';
    }
    return defaultHtml + editButtonHtml('edit-note', item.id);
  }

  function escape(key) { return escapeText(t(key)); }




  var escapeText = VIEWER.escapeHtml;
  var escapeAttr = VIEWER.escapeHtml;



  function data() { return VIEWER.getSnapshotData(); }

  function applyFieldEdit(id, index, value) {
    var entry = (data().memoEntries || []).find(function (e) { return e.id === id; });
    if (!entry || !entry.fields[Number(index)]) return false;
    entry.fields[Number(index)].value = String(value);
    return true;
  }

  function applyTodoToggle(id, index, checked) {
    var todo = (data().todoCards || []).find(function (e) { return e.id === id; });
    if (!todo) return false;
    if (Number(index) < 0) {


      var everyDone = todo.subTexts.length > 0 && todo.checkedStates.every(function (v) { return v; });
      if (checked && todo.subTexts.length > 0 && !everyDone) {
        todo.checkedStates = todo.subTexts.map(function () { return true; });
      } else {
        todo.checkedStates[0] = checked;
      }
      return true;
    }
    if (todo.checkedStates[Number(index)] === undefined) return false;
    todo.checkedStates[Number(index)] = checked === true;
    return true;
  }

  function applyNoteEdit(id, value) {
    var note = (data().noteCards || []).find(function (e) { return e.id === id; });
    if (!note) return false;
    note.content = String(value);
    return true;
  }


  function applyDelete(kind, id) {


    var listName = kind === 'memo' ? 'memoEntries'
      : kind === 'todo' ? 'todoCards'
      : kind === 'diary' ? 'diaryItems'
      : 'noteCards';
    var list = data()[listName] || [];
    var item = list.find(function (e) { return e.id === id; });
    if (!item || item.isDeleted === true) return false;
    item.isDeleted = true;
    item.deletedAt = new Date().toISOString();
    return true;
  }


  function applyCreate(kind, name, detail) {
    var now = new Date().toISOString();
    var id = (crypto.randomUUID ? crypto.randomUUID() : 'web-' + now + '-' + Math.floor(Math.random() * 1e6));
    if (kind === 'memo') {
      data().memoEntries.push({
        id: id, groupId: null, type: '自定义', name: String(name), iconKey: '',
        fields: [{ label: '信息', value: String(detail || ''), canCopy: true }],
        isPinned: false, isStarred: false, createdAt: now,
      });
    } else if (kind === 'todo') {
      data().todoCards.push({
        id: id, title: String(name), mainText: String(detail || name), subTexts: [], checkedStates: [],
        isPinned: false, isStarred: false, createdAt: now,
      });
    } else if (kind === 'diary') {



      data().diaryItems.push({
        id: id, title: String(name), content: '', format: 'markdown',
        isPinned: false, isStarred: false, createdAt: now, modifiedAt: now,
      });
    } else {
      data().noteCards.push({
        id: id, iconKey: 'Group48', title: String(name), content: String(detail || ''),
        isPinned: false, isStarred: false, createdAt: now,
      });
    }
    return id;
  }






  function diaryReaderHtml(item, defaultBody) {
    if (editingDiaryId === item.id) {
      var titleKey = 'diary|' + item.id + '|title', contentKey = 'diary|' + item.id + '|content';
      var title = Object.prototype.hasOwnProperty.call(drafts, titleKey) ? drafts[titleKey] : item.title;
      var content = Object.prototype.hasOwnProperty.call(drafts, contentKey) ? drafts[contentKey] : item.content;
      return '<div class="edit-doc">' +
        '<input class="edit-input" type="text" data-editor-input="diary-title" data-editor-id="' + escapeAttr(item.id) + '" value="' + escapeAttr(title) + '" autocomplete="off" />' +
        '<textarea class="edit-input edit-doc-body" rows="12" data-editor-input="diary-content" data-editor-id="' + escapeAttr(item.id) + '">' + escapeText(content) + '</textarea>' +
        '</div>';
    }



    if (!canEdit() || item.format !== 'markdown') return defaultBody;
    return defaultBody.replace(
      '<div class="reader-body markdown">',
      '<div class="reader-body markdown" role="button" tabindex="0" data-editor-action="edit-diary" data-editor-id="' + escapeAttr(item.id) + '">');
  }






  function readerNavExtra(item) {
    if (!canEdit() || item.format !== 'markdown') return '';
    return '<button class="edit-button is-danger reader-delete" type="button" data-editor-action="delete-item" data-editor-kind="diary" data-editor-id="' + escapeAttr(item.id) + '">' + escape('deleteItem') + '</button>';
  }











  function commitDiaryEdit() {
    if (!editingDiaryId) return true;
    var id = editingDiaryId;
    var outcome = applyDiaryEdit(id);
    if (outcome === false) return false;
    editingDiaryId = null;
    clearDrafts('diary', id);
    if (outcome === 'changed') markDirty();
    return true;
  }


  function beforeLeaveReader() { return commitDiaryEdit(); }









  function onLock() {
    dirty = false;
    uploading = false;
    editingEntryId = null;
    editingNoteId = null;
    editingDiaryId = null;
    drafts = {};
    conflictServerVersion = 0;
    ctx = null;
    closeConflictPanel();
    renderSyncBar();
  }


  function applyDiaryEdit(id) {
    var doc = (data().diaryItems || []).find(function (e) { return e.id === id; });
    if (!doc || doc.format !== 'markdown') return false;
    var titleKey = 'diary|' + id + '|title', contentKey = 'diary|' + id + '|content';
    var title = (drafts[titleKey] !== undefined ? drafts[titleKey] : doc.title).trim();
    if (!title) { showToast(t('newNameRequired')); return false; }
    var content = drafts[contentKey] !== undefined ? String(drafts[contentKey]) : doc.content;



    if (title === doc.title && content === doc.content) return 'unchanged';
    doc.title = title;
    doc.content = content;
    doc.modifiedAt = new Date().toISOString();
    return 'changed';
  }







  function growDiaryBody() {
    var field = document.querySelector('.edit-doc-body');
    if (!field || typeof field.scrollHeight !== 'number' || !field.style) return;
    field.style.height = 'auto';
    field.style.height = field.scrollHeight + 'px';
  }



  function onMainContentInput(event) {
    var target = event.target;
    if (!target || !target.dataset || target.dataset.editorInput === undefined) return;
    var key = target.dataset.editorInput === 'new-name' || target.dataset.editorInput === 'new-detail'
      ? 'new|' + target.dataset.editorKind + '|' + target.dataset.editorInput.slice(4)
      : target.dataset.editorInput === 'diary-title' || target.dataset.editorInput === 'diary-content'
        ? 'diary|' + target.dataset.editorId + '|' + target.dataset.editorInput.slice(6)
        : draftKey(target.dataset.editorInput, target.dataset.editorId, target.dataset.editorIndex);
    drafts[key] = target.value;
    if (target.dataset.editorInput === 'diary-content') growDiaryBody();
  }

  function onMainContentChange(event) {
    var target = event.target;
    if (!target || !target.dataset || target.dataset.editorInput !== 'todo') return;
    if (applyTodoToggle(target.dataset.editorId, target.dataset.editorIndex, target.checked)) {
      markDirty();
      VIEWER.render(data());
    } else {
      target.checked = !target.checked;
    }
  }

  function onMainContentClick(event) {
    var button = event.target.closest('[data-editor-action]');
    if (!button) return;
    var action = button.dataset.editorAction;
    var id = button.dataset.editorId;
    if (action === 'edit-entry') { editingEntryId = id; editingNoteId = null; VIEWER.render(data()); return; }
    if (action === 'edit-note') { editingNoteId = id; editingEntryId = null; VIEWER.render(data()); return; }
    if (action === 'cancel-entry') { editingEntryId = null; clearDrafts('field', id); VIEWER.render(data()); return; }
    if (action === 'cancel-note') { editingNoteId = null; clearDrafts('note', id); VIEWER.render(data()); return; }
    if (action === 'save-entry') { saveEntry(id); return; }
    if (action === 'save-note') { saveNote(id); return; }
    if (action === 'delete-item') {
      var delKind = button.dataset.editorKind;
      if (!window.confirm(t('deleteConfirm'))) return;
      if (applyDelete(delKind, id)) {
        if (editingEntryId === id) editingEntryId = null;
        if (editingNoteId === id) editingNoteId = null;




        if (editingDiaryId === id) { editingDiaryId = null; clearDrafts('diary', id); }
        markDirty();
        VIEWER.render(data());
      }
      return;
    }
    if (action === 'edit-diary') {
      editingDiaryId = id; editingEntryId = null; editingNoteId = null;
      if (uiStateReaderId() !== id) VIEWER.openReader(id); else VIEWER.render(data());
      growDiaryBody();
      return;
    }
  }






  function onDocumentClick(event) {
    if (!editingDiaryId) return;
    var target = event.target;
    if (!target || !target.closest) return;
    if (target.closest('.edit-doc') || target.closest('button') ||
        target.closest('[data-editor-action]') || target.closest('.edit-conflict')) return;
    if (commitDiaryEdit()) VIEWER.render(data());
  }





  function onMainContentKeyDown(event) {
    if (event.key !== 'Enter' && event.key !== ' ') return;
    var target = event.target;
    if (!target || !target.closest) return;
    var action = target.closest('[data-editor-action]');
    if (!action || action.tagName === 'BUTTON' || action.tagName === 'INPUT' || action.tagName === 'TEXTAREA') return;
    event.preventDefault();
    action.click();
  }

  function clearDrafts(kind, id) {
    var prefix = kind === 'new' ? 'new|' : kind + '|' + id + '|';
    Object.keys(drafts).forEach(function (key) {
      if (key.indexOf(prefix) === 0) delete drafts[key];
    });
  }

  function saveEntry(id) {
    var entry = (data().memoEntries || []).find(function (e) { return e.id === id; });
    if (!entry) return;
    var changed = false;
    (entry.fields || []).forEach(function (field, index) {
      if (field.label === 'TOTP') return;
      var key = draftKey('field', id, index);
      if (Object.prototype.hasOwnProperty.call(drafts, key) && drafts[key] !== field.value) {
        changed = applyFieldEdit(id, index, drafts[key]) || changed;
      }
    });
    editingEntryId = null;
    clearDrafts('field', id);
    if (changed) markDirty();
    VIEWER.render(data());
  }

  function saveNote(id) {
    var key = draftKey('note', id, 0);
    var value = drafts[key];
    editingNoteId = null;
    delete drafts[key];
    if (value !== undefined && applyNoteEdit(id, value)) markDirty();
    VIEWER.render(data());
  }



  async function upload() {
    if (!ctx || uploading || !dirty || conflictServerVersion !== 0) return;
    uploading = true;
    renderSyncBar();
    try {
      var container = await sealContainer(data(), ctx.spaceKey);
      var envelope = await buildEnvelope(container, ctx.spaceKey, ctx, ctx.baseVersion);
      var response = await fetch(ctx.baseUrl + '/api/v1/space/data', {
        method: 'PUT',
        headers: {
          'Authorization': 'Bearer ' + ctx.getToken(),
          'X-Novara-Space': ctx.spaceId,
          'X-Novara-Device': EDIT_DEVICE,
          'If-Match': String(ctx.baseVersion),
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(envelope),
      });
      if (response.status === 409) {
        var body = await response.json().catch(function () { return {}; });




        var serverVersion = Number(body && body.currentVersion);
        onConflict(serverVersion > 0 ? serverVersion : -1);
        return;
      }
      if (!response.ok) {

        showToast(response.status === 401 || response.status === 403
          ? t('editReadOnlyFail') : t('editUploadFail') + ' (' + response.status + ')');
        return;
      }
      ctx.baseVersion = ctx.baseVersion + 1;
      dirty = false;
      showToast(t('editUploaded'));
    } catch (e) {
      if (console && console.error) console.error('edit upload failed:', e);
      showToast(t('editOffline'));
    } finally {
      uploading = false;
      renderSyncBar();
    }
  }

  function onConflict(serverVersion) {
    conflictServerVersion = serverVersion;
    renderSyncBar();
    openConflictPanel();
  }

  function openConflictPanel() {
    var panel = document.getElementById('editConflictOverlay');
    if (!panel) return;
    var info = document.getElementById('editConflictInfo');

    if (info) info.textContent = t('editConflictInfo').replace('{V}', conflictServerVersion > 0 ? String(conflictServerVersion) : '?');



    var force = document.getElementById('editConflictForce');
    if (force) force.hidden = conflictServerVersion <= 0;
    panel.hidden = false;
  }

  function closeConflictPanel() {
    var panel = document.getElementById('editConflictOverlay');
    if (panel) panel.hidden = true;
  }



  function adoptCloud() {
    if (!window.confirm(t('adoptCloudConfirm'))) return;
    location.reload();
  }

  async function copyEdits() {
    try {
      await navigator.clipboard.writeText(JSON.stringify(data()));
      showToast(t('editCopied'));
    } catch (e) { showToast(t('editCopyFail')); }
  }

  function download(name, bytes, mime) {
    var url = URL.createObjectURL(new Blob([bytes], { type: mime }));
    var link = document.createElement('a');
    link.href = url; link.download = name;
    document.body.appendChild(link); link.click(); link.remove();
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
  }

  async function exportEncrypted() {





    try {
      var container = await sealContainer(data(), ctx.spaceKey);
      download('novara-conflict.novaenc', container, 'application/octet-stream');
    } catch (e) {
      if (console && console.error) console.error('edit export failed:', e);
      showToast(t('editExportFail'));
    }
  }

  function exportPlain() {
    if (!window.confirm(t('editExportPlainConfirm'))) return;
    download('novara-conflict.json', utf8.encode(JSON.stringify(data())), 'application/json');
  }

  async function forceOverwrite() {
    if (uploading || conflictServerVersion <= 0) return;
    if (!window.confirm(t('editForceConfirm'))) return;
    uploading = true;
    renderSyncBar();
    try {
      var container = await sealContainer(data(), ctx.spaceKey);
      var envelope = await buildEnvelope(container, ctx.spaceKey, ctx, conflictServerVersion);
      var response = await fetch(ctx.baseUrl + '/api/v1/space/data?force=1', {
        method: 'PUT',
        headers: {
          'Authorization': 'Bearer ' + ctx.getToken(),
          'X-Novara-Space': ctx.spaceId,
          'X-Novara-Device': EDIT_DEVICE,
          'If-Match': String(conflictServerVersion),
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(envelope),
      });
      if (!response.ok) {

        showToast(response.status === 401 || response.status === 403
          ? t('editReadOnlyFail') : t('editUploadFail') + ' (' + response.status + ')');
        return;
      }
      ctx.baseVersion = conflictServerVersion + 1;
      conflictServerVersion = 0;
      dirty = false;
      closeConflictPanel();
      showToast(t('editForceDone'));
    } catch (e) {


      if (console && console.error) console.error('edit force overwrite failed:', e);
      showToast(t('editOffline'));
    } finally {
      uploading = false;
      renderSyncBar();
    }
  }


  function uiStateReaderId() {
    var open = document.querySelector('[data-reader-id]');
    return open ? open.getAttribute('data-reader-id') : '';
  }





  var KIND_LABEL_KEYS = { memo: 'kindMemo', todo: 'kindTodo', note: 'kindNote', diary: 'kindDoc' };

  function openCreateDialog() {





    if (editingDiaryId && !commitDiaryEdit()) return;
    createKind = 'memo';
    document.getElementById('editCreateName').value = '';
    document.getElementById('editCreateDetail').value = '';
    document.getElementById('editCreateTitle').textContent = t('dlgTitle');
    Array.prototype.forEach.call(document.querySelectorAll('.edit-kind'), function (button) {
      button.textContent = t(KIND_LABEL_KEYS[button.dataset.editorKind] || ('kind' + button.dataset.editorKind.charAt(0).toUpperCase() + button.dataset.editorKind.slice(1)));
    });
    refreshCreateDialog();
    document.getElementById('editCreateOverlay').hidden = false;
    document.getElementById('editCreateName').focus();
  }

  function closeCreateDialog() {
    document.getElementById('editCreateOverlay').hidden = true;
  }

  function refreshCreateDialog() {
    var isDoc = createKind === 'diary';
    var detail = document.getElementById('editCreateDetail');
    detail.hidden = isDoc;
    detail.placeholder = createKind === 'note' ? t('detailContent') : t('detailInfo');
    document.getElementById('editCreateName').placeholder = t('newName');
    Array.prototype.forEach.call(document.querySelectorAll('.edit-kind'), function (button) {
      button.classList.toggle('is-selected', button.dataset.editorKind === createKind);
    });
  }

  function saveCreateDialog() {
    var name = document.getElementById('editCreateName').value.trim();
    if (!name) { showToast(t('newNameRequired')); return; }
    var detail = document.getElementById('editCreateDetail').value;
    var newId = applyCreate(createKind, name, detail);
    closeCreateDialog();
    markDirty();
    if (createKind === 'diary') {
      VIEWER.render(data());
      VIEWER.openReader(newId);
      editingDiaryId = newId;
      VIEWER.render(data());
    } else {
      VIEWER.render(data());
      showToast(t('editDirtyHint'));
    }
  }



  var showToast = function (message) { VIEWER.showToast(message); };



  var TRANSLATIONS = {
    'zh-CN': {
      editItem: '编辑', saveItem: '保存', cancelEdit: '取消',
      editCleanHint: '本页可直接编辑，改动仅在本机内存中', editDirtyHint: '有未上传的改动（刷新或关闭将丢失）',
      editConflictBar: '存在冲突，改动未上传', editUpload: '上传改动',
      editReadOnlyHint: '当前凭据仅可阅读（只读令牌），本页不能编辑', editReadOnlyFail: '当前凭据仅可阅读，无法上传改动',
      editConflictTitle: '同步冲突', editConflictInfo: '服务器已有更新版本（v{V}），你的改动仍保留在本页内存中。请选择处理方式：',
      editCopy: '复制改动（JSON）', editExportEnc: '导出加密文件（推荐）', editExportPlain: '导出明文 JSON',
      editForce: '以本机覆盖云端', editClose: '关闭',
      editExportPlainConfirm: '明文 JSON 不加密，任何人都能直接阅读。仍要导出吗？',
      editForceConfirm: '将用本机版本覆盖云端，服务器会把被覆盖的版本存为冲突副本。继续吗？',
      editUploaded: '改动已上传', editUploadFail: '上传失败', editOffline: '无法连接服务器，改动仍保留在本页',
      editExportFail: '导出失败，请重试',
      editCopied: '改动已复制到剪贴板', editCopyFail: '复制失败',
      newMemo: '新建备忘', newTodo: '新建待办', newNote: '新建便签',
      newName: '名称', newInfo: '信息（可留空）', newNoteContent: '内容', newNameRequired: '请先填写名称',
      deleteItem: '删除', deleteConfirm: '删除后进入回收站，PC 端可在回收站恢复。确定删除吗？',
      dlgTitle: '新建', kindMemo: '备忘', kindTodo: '待办', kindNote: '便签', kindDoc: '文档',
      detailInfo: '信息（可留空）', detailContent: '内容',
      newDoc: '新建文档', docTitle: '标题', docContent: '正文（Markdown 纯文本）',
      adoptCloud: '采用云端版本（放弃本机改动）', adoptCloudConfirm: '将放弃本页未上传的改动并重新载入云端版本。继续吗？',
    },
    'en-US': {
      editItem: 'Edit', saveItem: 'Save', cancelEdit: 'Cancel',
      editCleanHint: 'This page is editable; changes live in this tab\'s memory only', editDirtyHint: 'Unsaved changes (lost on refresh or close)',
      editConflictBar: 'Conflict - changes not uploaded', editUpload: 'Upload changes',
      editReadOnlyHint: 'Read-only credential - this page cannot be edited', editReadOnlyFail: 'This credential is read-only; the changes cannot be uploaded',
      editConflictTitle: 'Sync conflict', editConflictInfo: 'The server already has a newer version (v{V}). Your edits are kept in this page\'s memory. Choose how to proceed:',
      editCopy: 'Copy changes (JSON)', editExportEnc: 'Export encrypted file (recommended)', editExportPlain: 'Export plain JSON',
      editForce: 'Overwrite cloud with this device', editClose: 'Close',
      editExportPlainConfirm: 'Plain JSON is not encrypted and can be read by anyone. Export anyway?',
      editForceConfirm: 'This will overwrite the cloud with this device\'s version; the server keeps the replaced version as a conflict copy. Continue?',
      editUploaded: 'Changes uploaded', editUploadFail: 'Upload failed', editOffline: 'Cannot reach the server; your edits are kept on this page',
      editExportFail: 'Export failed - please try again',
      editCopied: 'Changes copied to the clipboard', editCopyFail: 'Copy failed',
      newMemo: 'New entry', newTodo: 'New todo', newNote: 'New note',
      newName: 'Name', newInfo: 'Info (optional)', newNoteContent: 'Content', newNameRequired: 'Enter a name first',
      deleteItem: 'Delete', deleteConfirm: 'The item moves to the desktop recycle bin, where it can be restored. Delete it?',
      dlgTitle: 'Create', kindMemo: 'Entry', kindTodo: 'Todo', kindNote: 'Note', kindDoc: 'Document',
      detailInfo: 'Info (optional)', detailContent: 'Content',
      newDoc: 'New document', docTitle: 'Title', docContent: 'Body (plain-text Markdown)',
      adoptCloud: 'Adopt cloud version (discard local edits)', adoptCloudConfirm: 'This discards the unsaved edits on this page and reloads the cloud version. Continue?',
    },
    'zh-TW': {
      editItem: '編輯', saveItem: '儲存', cancelEdit: '取消',
      editCleanHint: '本頁可直接編輯，改動僅在本機記憶體中', editDirtyHint: '有未上傳的改動（重新整理或關閉將遺失）',
      editConflictBar: '存在衝突，改動未上傳', editUpload: '上傳改動',
      editReadOnlyHint: '目前憑證僅可閱讀（唯讀權杖），本頁無法編輯', editReadOnlyFail: '目前憑證僅可閱讀，無法上傳改動',
      editConflictTitle: '同步衝突', editConflictInfo: '伺服器已有較新版本（v{V}），你的改動仍保留在本頁記憶體中。請選擇處理方式：',
      editCopy: '複製改動（JSON）', editExportEnc: '匯出加密檔案（建議）', editExportPlain: '匯出明文 JSON',
      editForce: '以本機覆蓋雲端', editClose: '關閉',
      editExportPlainConfirm: '明文 JSON 不加密，任何人都能直接閱讀。仍要匯出嗎？',
      editForceConfirm: '將用本機版本覆蓋雲端，伺服器會把被覆蓋的版本存為衝突副本。繼續嗎？',
      editUploaded: '改動已上傳', editUploadFail: '上傳失敗', editOffline: '無法連線伺服器，改動仍保留在本頁',
      editExportFail: '匯出失敗，請重試',
      editCopied: '改動已複製到剪貼簿', editCopyFail: '複製失敗',
      newMemo: '新增備忘', newTodo: '新增待辦', newNote: '新增便籤',
      newName: '名稱', newInfo: '資訊（可留空）', newNoteContent: '內容', newNameRequired: '請先填寫名稱',
      deleteItem: '刪除', deleteConfirm: '刪除後進入回收站，PC 端可在回收站復原。確定刪除嗎？',
      dlgTitle: '新增', kindMemo: '備忘', kindTodo: '待辦', kindNote: '便籤', kindDoc: '文件',
      detailInfo: '資訊（可留空）', detailContent: '內容',
      newDoc: '新增文件', docTitle: '標題', docContent: '內容（Markdown 純文字）',
      adoptCloud: '採用雲端版本（放棄本機改動）', adoptCloudConfirm: '將放棄本頁未上傳的改動並重新載入雲端版本。繼續嗎？',
    },
    'ko-KR': {
      editItem: '편집', saveItem: '저장', cancelEdit: '취소',
      editCleanHint: '이 페이지에서 직접 편집할 수 있으며 변경 사항은 이 탭의 메모리에만 있습니다', editDirtyHint: '업로드되지 않은 변경 사항이 있습니다(새로고침하거나 닫으면 사라짐)',
      editConflictBar: '충돌 발생 - 변경 사항이 업로드되지 않았습니다', editUpload: '변경 사항 업로드',
      editReadOnlyHint: '현재 자격 증명은 읽기 전용(읽기 전용 토큰)이라 이 페이지는 편집할 수 없습니다', editReadOnlyFail: '현재 자격 증명은 읽기 전용이라 변경 사항을 업로드할 수 없습니다',
      editConflictTitle: '동기화 충돌', editConflictInfo: '서버에 더 새 버전(v{V})이 있습니다. 변경 사항은 이 페이지의 메모리에 보존되어 있습니다. 처리 방법을 선택하세요:',
      editCopy: '변경 사항 복사(JSON)', editExportEnc: '암호화 파일 내보내기(권장)', editExportPlain: '평문 JSON 내보내기',
      editForce: '이 기기로 클라우드 덮어쓰기', editClose: '닫기',
      editExportPlainConfirm: '평문 JSON은 암호화되지 않아 누구나 읽을 수 있습니다. 그래도 내보내시겠습니까?',
      editForceConfirm: '이 기기의 버전으로 클라우드를 덮어씁니다. 서버는 교체된 버전을 충돌 복사본으로 보관합니다. 계속하시겠습니까?',
      editUploaded: '변경 사항이 업로드되었습니다', editUploadFail: '업로드 실패', editOffline: '서버에 연결할 수 없습니다. 변경 사항은 이 페이지에 보존됩니다',
      editExportFail: '내보내기에 실패했습니다. 다시 시도하세요',
      editCopied: '변경 사항이 클립보드에 복사되었습니다', editCopyFail: '복사 실패',
      newMemo: '새 메모', newTodo: '새 할 일', newNote: '새 노트',
      newName: '이름', newInfo: '정보(비워 둘 수 있음)', newNoteContent: '내용', newNameRequired: '이름을 먼저 입력하세요',
      deleteItem: '삭제', deleteConfirm: '삭제하면 데스크톱 휴지통으로 이동하며 복원할 수 있습니다. 삭제하시겠습니까?',
      dlgTitle: '만들기', kindMemo: '메모', kindTodo: '할 일', kindNote: '노트', kindDoc: '문서',
      detailInfo: '정보(비워 둘 수 있음)', detailContent: '내용',
      newDoc: '새 문서', docTitle: '제목', docContent: '본문(일반 텍스트 Markdown)',
      adoptCloud: '클라우드 버전 채택(로컬 변경 사항 버림)', adoptCloudConfirm: '이 페이지의 업로드되지 않은 변경 사항을 버리고 클라우드 버전을 다시 불러옵니다. 계속하시겠습니까?',
    },
    'ja-JP': {
      editItem: '編集', saveItem: '保存', cancelEdit: 'キャンセル',
      editCleanHint: 'このページは直接編集できます。変更はこのタブのメモリ内のみ', editDirtyHint: '未アップロードの変更があります（更新・終了で失われます）',
      editConflictBar: '競合が発生 - 変更は未アップロードです', editUpload: '変更をアップロード',
      editReadOnlyHint: '現在の資格情報は読み取り専用（読み取り専用トークン）のため、このページは編集できません', editReadOnlyFail: '現在の資格情報は読み取り専用のため、変更をアップロードできません',
      editConflictTitle: '同期の競合', editConflictInfo: 'サーバーにはより新しいバージョン（v{V}）があります。変更はこのページのメモリに保持されています。処理方法を選んでください：',
      editCopy: '変更をコピー（JSON）', editExportEnc: '暗号化ファイルを書き出す（推奨）', editExportPlain: '平文 JSON を書き出す',
      editForce: 'この端末でクラウドを上書き', editClose: '閉じる',
      editExportPlainConfirm: '平文 JSON は暗号化されておらず、誰でも読めます。それでも書き出しますか？',
      editForceConfirm: 'この端末のバージョンでクラウドを上書きします。サーバーは置き換えられたバージョンを競合コピーとして保持します。続行しますか？',
      editUploaded: '変更をアップロードしました', editUploadFail: 'アップロード失敗', editOffline: 'サーバーに接続できません。変更はこのページに保持されています',
      editExportFail: '書き出しに失敗しました。もう一度お試しください',
      editCopied: '変更をクリップボードにコピーしました', editCopyFail: 'コピー失敗',
      newMemo: '新規メモ', newTodo: '新規ToDo', newNote: '新規ノート',
      newName: '名前', newInfo: '情報（空欄可）', newNoteContent: '内容', newNameRequired: '先に名前を入力してください',
      deleteItem: '削除', deleteConfirm: '削除するとデスクトップのゴミ箱に入り、復元できます。削除しますか？',
      dlgTitle: '作成', kindMemo: 'メモ', kindTodo: 'ToDo', kindNote: 'ノート', kindDoc: 'ドキュメント',
      detailInfo: '情報（空欄可）', detailContent: '内容',
      newDoc: '新規ドキュメント', docTitle: 'タイトル', docContent: '本文（Markdown プレーンテキスト）',
      adoptCloud: 'クラウド版を採用（ローカルの変更を破棄）', adoptCloudConfirm: 'このページの未アップロードの変更を破棄し、クラウド版を再読み込みします。続行しますか？',
    },
  };



  function onLoaded(_data, context) {

    editorCredential = context.editorCredential === true;
    ctx = {
      baseUrl: location.origin,
      spaceId: (WEB.parseFragment(location.hash) || {}).spaceId || '',
      getToken: function () {
        var input = document.getElementById('readTokenInput');
        return input ? input.value : '';
      },
      spaceKey: context.spaceKey,
      baseVersion: context.version,
    };
    VIEWER.addTranslations(TRANSLATIONS);
    VIEWER.setEditHooks({ entryFieldsHtml: entryFieldsHtml, todoHtml: todoHtml, noteHtml: noteHtml, diaryReaderHtml: diaryReaderHtml, readerNavExtra: readerNavExtra, beforeLeaveReader: beforeLeaveReader, onLock: onLock });




    var dialogButtons = [
      ['editCreateSave', 'saveItem'], ['editCreateCancel', 'cancelEdit'],
      ['editConflictExportEnc', 'editExportEnc'], ['editConflictCopy', 'editCopy'],
      ['editConflictExportPlain', 'editExportPlain'], ['editConflictForce', 'editForce'],
      ['editConflictAdopt', 'adoptCloud'], ['editConflictClose', 'editClose'],
    ];
    dialogButtons.forEach(function (pair) {
      var el = document.getElementById(pair[0]);
      if (el) el.textContent = t(pair[1]);
    });




    var conflictTitle = document.getElementById('editConflictTitle');
    if (conflictTitle) conflictTitle.textContent = t('editConflictTitle');






    if (!listenersWired) {
      listenersWired = true;
    var main = document.getElementById('mainContent');
    if (main) {
      main.addEventListener('input', onMainContentInput);
      main.addEventListener('change', onMainContentChange);
      main.addEventListener('click', onMainContentClick);
      main.addEventListener('keydown', onMainContentKeyDown);
      document.addEventListener('click', onDocumentClick);


      window.addEventListener('resize', growDiaryBody);
    }
    var uploadButton = document.getElementById('editUploadButton');
    if (uploadButton) {
      uploadButton.addEventListener('click', upload);
      uploadButton.setAttribute('aria-label', t('editUpload'));
    }
    var createButton = document.getElementById('editCreateButton');
    if (createButton) {
      createButton.addEventListener('click', openCreateDialog);
      createButton.setAttribute('aria-label', t('dlgTitle'));
    }
    var createOverlay = document.getElementById('editCreateOverlay');
    if (createOverlay) createOverlay.addEventListener('click', function (event) {
      if (event.target === createOverlay) closeCreateDialog();
    });
    Array.prototype.forEach.call(
      document.querySelectorAll('.edit-kind'),
      function (button) { button.addEventListener('click', function () {
        createKind = button.dataset.editorKind;
        refreshCreateDialog();
      }); });
    var createName = document.getElementById('editCreateName');
    if (createName) createName.addEventListener('keydown', function (event) {
      if (event.key === 'Enter') { event.preventDefault(); saveCreateDialog(); }
    });
    var createSave = document.getElementById('editCreateSave');
    if (createSave) createSave.addEventListener('click', saveCreateDialog);
    var createCancel = document.getElementById('editCreateCancel');
    if (createCancel) createCancel.addEventListener('click', closeCreateDialog);
    var copyButton = document.getElementById('editConflictCopy');
    if (copyButton) copyButton.addEventListener('click', copyEdits);
    var exportEncButton = document.getElementById('editConflictExportEnc');
    if (exportEncButton) exportEncButton.addEventListener('click', exportEncrypted);
    var exportPlainButton = document.getElementById('editConflictExportPlain');
    if (exportPlainButton) exportPlainButton.addEventListener('click', exportPlain);
    var forceButton = document.getElementById('editConflictForce');
    if (forceButton) forceButton.addEventListener('click', forceOverwrite);
    var closeButton = document.getElementById('editConflictClose');
    if (closeButton) closeButton.addEventListener('click', closeConflictPanel);
    var adoptButton = document.getElementById('editConflictAdopt');
    if (adoptButton) adoptButton.addEventListener('click', adoptCloud);

    window.addEventListener('beforeunload', function (event) {
      if (!dirty && conflictServerVersion === 0) return;
      event.preventDefault();
      event.returnValue = '';
    });
    }




    var createVisibility = document.getElementById('editCreateButton');
    if (createVisibility) createVisibility.hidden = !canEdit();





    renderSyncBar();
  }


  window.NovaraEditor = {
    onLoaded: onLoaded,
    upload: upload,
    markDirty: markDirty,
    sealContainer: sealContainer,
    sealGcmBody: sealGcmBody,
    buildEnvelope: buildEnvelope,
    bytesToBase64: bytesToBase64,
    getContext: function () { return ctx; },
    isDirty: function () { return dirty; },
    getConflictVersion: function () { return conflictServerVersion; },
    getTranslations: function () { return TRANSLATIONS; },
  };
})();
