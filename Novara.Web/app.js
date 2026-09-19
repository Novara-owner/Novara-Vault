(function () {
  'use strict';

  var VIEWER = window.NovaraViewer;
  var API_PREFIX = '/api/v1';
  var MAC_INFO = 'novara-sync-mac';
  var SPACE_KEY_TEXT_LENGTH = 44;
  var KEYWRAP_TAG_LENGTH = 16;
  var utf8 = new TextEncoder();



  function bytesFromBase64(text) {
    var raw = atob(String(text || '').trim());
    var out = new Uint8Array(raw.length);
    for (var i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
    return out;
  }

  function toHex(buffer) {
    var bytes = new Uint8Array(buffer);
    var out = '';
    for (var i = 0; i < bytes.length; i++) out += (bytes[i] < 16 ? '0' : '') + bytes[i].toString(16);
    return out;
  }


  function failWith(key) {
    var error = new Error(key);
    error.i18nKey = key;
    return error;
  }

  function hasCryptoEnv() {
    return !!(window.crypto && crypto.subtle && typeof DecompressionStream !== 'undefined');
  }








  function parseFragment(hash) {
    var match = /[#&]s=([A-Za-z0-9_-]{1,128})/.exec(hash || '');
    return match ? { spaceId: match[1] } : null;
  }






  function normalizeToken(text) {
    return String(text || '')
      .toUpperCase()
      .replace(/[IL]/g, '1')
      .replace(/O/g, '0')
      .replace(/[^0-9A-Z]/g, '');
  }








  async function unwrapSpaceKey(record, passphrase) {
    if (!record || typeof record !== 'object') throw failWith('m3ErrServer');
    if (Number(record.wrap) !== 1 || record.kdf !== 'pbkdf2-sha256') throw failWith('m3ErrServer');

    var iterations = Number(record.iter);
    if (!(iterations >= 1000 && iterations <= 5000000)) throw failWith('m3ErrServer');
    if (!passphrase) throw failWith('m3ErrPassword');

    var salt = bytesFromBase64(record.salt);
    var nonce = bytesFromBase64(record.nonce);
    var blob = bytesFromBase64(record.ct);
    if (salt.length !== 32 || nonce.length !== 12 || blob.length !== SPACE_KEY_TEXT_LENGTH + KEYWRAP_TAG_LENGTH)
      throw failWith('m3ErrServer');

    var aad = utf8.encode('novara-keywrap-v' + Number(record.wrap) + '|' + record.kdf + '|' + iterations);
    var material = await crypto.subtle.importKey('raw', utf8.encode(passphrase), 'PBKDF2', false, ['deriveKey']);
    var kek = await crypto.subtle.deriveKey(
      { name: 'PBKDF2', salt: salt, iterations: iterations, hash: 'SHA-256' },
      material, { name: 'AES-GCM', length: 256 }, false, ['decrypt']);

    var plain;
    try {
      plain = await crypto.subtle.decrypt(
        { name: 'AES-GCM', iv: nonce, additionalData: aad, tagLength: 128 }, kek, blob);
    } catch (e) {
      throw failWith('m3ErrPassword');
    }

    var text = new TextDecoder().decode(plain);
    if (!/^[A-Za-z0-9+/]{43}=$/.test(text)) throw failWith('m3ErrServer');
    return text;
  }



  var CANONICAL_FIELDS = ['sync', 'crypto', 'space', 'version', 'base', 'device', 'ts', 'payloadSha256'];







  var ASCII_SAFE = /^[A-Za-z0-9._:\/-]*$/;


  function canonicalEnvelope(envelope) {
    var joined = String(envelope.space) + String(envelope.device) +
      String(envelope.ts) + String(envelope.payloadSha256);
    if (!ASCII_SAFE.test(joined)) throw failWith('m3ErrVerify');

    var ordered = {};
    CANONICAL_FIELDS.forEach(function (field) {
      ordered[field] = (field === 'sync' || field === 'crypto' || field === 'version' || field === 'base')
        ? Number(envelope[field])
        : String(envelope[field]);
    });
    return JSON.stringify(ordered);
  }


  async function deriveMacKey(spaceKeyText) {
    var material = await crypto.subtle.importKey('raw', utf8.encode(spaceKeyText), 'HKDF', false, ['deriveBits']);
    return crypto.subtle.deriveBits(
      { name: 'HKDF', hash: 'SHA-256', salt: new Uint8Array(0), info: utf8.encode(MAC_INFO) }, material, 256);
  }






  async function verifyEnvelope(envelope, spaceKeyText) {
    if (!envelope || Number(envelope.sync) !== 1 || Number(envelope.crypto) !== 4) return false;
    if (!envelope.mac || !envelope.payload || !envelope.payloadSha256) return false;

    var payload = bytesFromBase64(envelope.payload);
    var digest = await crypto.subtle.digest('SHA-256', payload);
    if (toHex(digest) !== String(envelope.payloadSha256).toLowerCase()) return false;

    try {
      var macKey = await crypto.subtle.importKey(
        'raw', await deriveMacKey(spaceKeyText), { name: 'HMAC', hash: 'SHA-256' }, false, ['verify']);
      return await crypto.subtle.verify(
        'HMAC', macKey, bytesFromBase64(envelope.mac), utf8.encode(canonicalEnvelope(envelope)));
    } catch (e) {
      return false;
    }
  }









  var lastLoadContext = null;

  function createRemoteLoader(config) {
    var base = String(config.baseUrl || '').replace(/\/+$/, '');



    var editorCredentialUsed = false;

    function fetchJson(path, useEditorCredential) {




      var headers = { 'X-Novara-Space': config.spaceId };
      if (useEditorCredential) {
        headers['Authorization'] = 'Bearer ' + config.getToken();
        headers['X-Novara-Device'] = 'web-editor';
      } else {
        headers['Authorization'] = 'Novara-Read ' + normalizeToken(config.getToken());
      }
      return fetch(base + API_PREFIX + path, {
        method: 'GET',
        headers: headers,
        cache: 'no-store',
        credentials: 'omit',
        redirect: 'error',
      }).then(function (response) {
        if (!response.ok) {





          var error = failWith(response.status === 401 ? 'm3ErrAuth'
            : response.status === 429 ? 'm3ErrRateLimited'
              : 'm3ErrServer');
          error.status = response.status;
          throw error;
        }
        if (useEditorCredential) editorCredentialUsed = true;
        return response.text().then(function (text) {
          var version = Number(response.headers.get('X-Novara-Version')) || 0;
          return { body: JSON.parse(text), version: version };
        });
      }, function () {
        throw failWith('m3ErrOffline');
      });
    }









    function looksLikeReadToken(raw) {
      return /^[0-9A-Z]{24}$/.test(normalizeToken(raw));
    }

    function readJson(path) {









      return fetchJson(path, !looksLikeReadToken(config.getToken()));
    }

    return async function load(passphrase) {
      if (!hasCryptoEnv()) throw failWith('lockErrorEnv');
      editorCredentialUsed = false;

      var keywrap = await readJson('/space/keywrap');
      var spaceKey = await unwrapSpaceKey(keywrap.body, passphrase);
      var envelope = await readJson('/space/data');

      if (!(await verifyEnvelope(envelope.body, spaceKey))) throw failWith('m3ErrVerify');
      lastLoadContext = { spaceKey: spaceKey, version: envelope.version, editorCredential: editorCredentialUsed };
      return VIEWER.decryptSnapshot(envelope.body.payload, spaceKey);
    };
  }



  var TRANSLATIONS = {
    'zh-CN': {
      m3Lead: '在线访问',
      m3TokenLabel: '只读令牌 / 编辑凭据',
      m3TokenPlaceholder: '24 位只读令牌 / 43 位编辑凭据',
      m3PasswordLabel: '访问口令（Novara 锁密码）',
      m3PasswordPlaceholder: '输入锁密码',
      m3Unlock: '连接并解锁',
      m3Note: '数据来自 {HOST}。本页不留存任何内容，关闭标签页即清除。',
      m3ErrNoSpace: '链接不完整：缺少空间标识（#s=…）',
      m3ErrNoViewer: '查看器脚本未能加载，请刷新重试',
      m3ErrAuth: '凭据无效或已作废（只读令牌 / 编辑凭据）',
      m3ErrServer: '服务器返回了无法识别的数据',
      m3ErrRateLimited: '尝试过于频繁，已被暂时限制，请等待约 1 分钟后重试',
      m3ErrOffline: '无法访问服务器，请检查地址与网络',
      m3ErrPassword: '锁密码不正确',
      m3ErrVerify: '数据校验未通过，已中止（可能被篡改）',
    },
    'en-US': {
      m3Lead: 'Online access',
      m3TokenLabel: 'Read-only token / Editor credential',
      m3TokenPlaceholder: '24-char read token / 43-char editor credential',
      m3PasswordLabel: 'Access passphrase (your Novara lock password)',
      m3PasswordPlaceholder: 'Your lock password',
      m3Unlock: 'Connect and unlock',
      m3Note: 'Data comes from {HOST}. Nothing is stored on this device - closing the tab clears it.',
      m3ErrNoSpace: 'Incomplete link: the space identifier (#s=…) is missing',
      m3ErrNoViewer: 'The viewer script could not be loaded; please reload',
      m3ErrAuth: 'The credential is invalid or has been revoked (read-only token / editor credential)',
      m3ErrServer: 'The server returned data this page cannot read',
      m3ErrRateLimited: 'Too many failed attempts, so the server is holding this device off for now. Please wait about a minute and try again.',
      m3ErrOffline: 'Cannot reach the server; check the address and your network',
      m3ErrPassword: 'The lock password is incorrect',
      m3ErrVerify: 'Verification failed; aborted (possible tampering)',
    },
    'zh-TW': {
      m3Lead: '線上存取',
      m3TokenLabel: '唯讀權杖 / 編輯憑證',
      m3TokenPlaceholder: '24 位唯讀權杖 / 43 位編輯憑證',
      m3PasswordLabel: '存取口令（Novara 鎖密碼）',
      m3PasswordPlaceholder: '輸入鎖密碼',
      m3Unlock: '連線並解鎖',
      m3Note: '資料來自 {HOST}。本頁不留存任何內容，關閉分頁即清除。',
      m3ErrNoSpace: '連結不完整：缺少空間識別碼（#s=…）',
      m3ErrNoViewer: '檢視器指令碼載入失敗，請重新整理',
      m3ErrAuth: '憑證無效或已作廢（唯讀權杖 / 編輯憑證）',
      m3ErrServer: '伺服器回傳了無法辨識的資料',
      m3ErrRateLimited: '嘗試過於頻繁，已被暫時限制，請等待約 1 分鐘後重試',
      m3ErrOffline: '無法連線伺服器，請檢查位址與網路',
      m3ErrPassword: '鎖密碼不正確',
      m3ErrVerify: '資料校驗未通過，已中止（可能遭竄改）',
    },
    'ko-KR': {
      m3Lead: '온라인 접속',
      m3TokenLabel: '읽기 전용 토큰 / 편집 자격 증명',
      m3TokenPlaceholder: '24자 읽기 전용 토큰 / 43자 편집 자격 증명',
      m3PasswordLabel: '접속 암호( Novara 잠금 비밀번호 )',
      m3PasswordPlaceholder: '잠금 비밀번호 입력',
      m3Unlock: '연결 후 잠금 해제',
      m3Note: '{HOST} 에서 데이터를 가져옵니다. 이 기기에는 아무것도 저장되지 않으며 탭을 닫으면 사라집니다.',
      m3ErrNoSpace: '링크가 불완전합니다: 공간 식별자(#s=…)가 없습니다',
      m3ErrNoViewer: '뷰어 스크립트를 불러오지 못했습니다. 새로고침해 주세요',
      m3ErrAuth: '자격 증명이 유효하지 않거나 폐기되었습니다(읽기 전용 토큰 / 편집 자격 증명)',
      m3ErrServer: '서버가 이 페이지에서 읽을 수 없는 데이터를 반환했습니다',
      m3ErrRateLimited: '시도가 너무 많아 서버가 잠시 차단했습니다. 약 1분 후 다시 시도해 주세요.',
      m3ErrOffline: '서버에 연결할 수 없습니다. 주소와 네트워크를 확인해 주세요',
      m3ErrPassword: '잠금 비밀번호가 올바르지 않습니다',
      m3ErrVerify: '데이터 검증에 실패하여 중단했습니다(변조 가능성)',
    },
    'ja-JP': {
      m3Lead: 'オンラインアクセス',
      m3TokenLabel: '読み取り専用トークン / 編集用資格情報',
      m3TokenPlaceholder: '24文字の読み取り専用トークン / 43文字の編集用資格情報',
      m3PasswordLabel: 'アクセスパスフレーズ（Novara ロックパスワード）',
      m3PasswordPlaceholder: 'ロックパスワードを入力',
      m3Unlock: '接続してロック解除',
      m3Note: 'データは {HOST} から取得します。この端末には何も保存されず、タブを閉じると消去されます。',
      m3ErrNoSpace: 'リンクが不完全です: スペース識別子(#s=…)がありません',
      m3ErrNoViewer: 'ビューアスクリプトを読み込めませんでした。再読み込みしてください',
      m3ErrAuth: '資格情報が無効か、失効しています（読み取り専用トークン / 編集用資格情報）',
      m3ErrServer: 'サーバーがこのページで読めないデータを返しました',
      m3ErrRateLimited: '試行が多すぎるため、サーバーがこの端末を一時的にブロックしています。約1分後にもう一度お試しください。',
      m3ErrOffline: 'サーバーに接続できません。アドレスとネットワークを確認してください',
      m3ErrPassword: 'ロックパスワードが正しくありません',
      m3ErrVerify: 'データ検証に失敗したため中止しました（改ざんの可能性）',
    },
  };



  function showUnlockError(key) {
    var error = document.getElementById('unlockError');
    if (error) error.textContent = VIEWER ? VIEWER.t(key) : key;
  }

  function boot() {
    if (!VIEWER) {
      showUnlockError('m3ErrNoViewer');
      return;
    }

    VIEWER.addTranslations(TRANSLATIONS);
    VIEWER.applyI18n();

    var config = parseFragment(location.hash);
    var note = document.getElementById('m3Note');
    if (note) note.textContent = VIEWER.t('m3Note').replace('{HOST}', location.host);

    if (!config) {
      showUnlockError('m3ErrNoSpace');
      var submit = document.querySelector('#unlockForm button[type="submit"]');
      if (submit) submit.disabled = true;
      return;
    }

    var remoteLoader = createRemoteLoader({
      baseUrl: location.origin,
      spaceId: config.spaceId,
      getToken: function () {
        var input = document.getElementById('readTokenInput');
        return input ? input.value : '';
      },
    });
    VIEWER.setSnapshotLoader(function (password) {
      return remoteLoader(password).then(function (data) {



        var context = WEBgetLastLoadContext();
        if (window.NovaraEditor && window.NovaraEditor.onLoaded && context) {
          window.NovaraEditor.onLoaded(data, context);
        }
        return data;
      });
    });

    if ('serviceWorker' in navigator) {
      window.addEventListener('load', function () {
        navigator.serviceWorker.register('sw.js').catch(function () {  });
      });
    }
  }


  window.NovaraWeb = {
    parseFragment: parseFragment,
    normalizeToken: normalizeToken,
    unwrapSpaceKey: unwrapSpaceKey,
    canonicalEnvelope: canonicalEnvelope,
    deriveMacKey: deriveMacKey,
    verifyEnvelope: verifyEnvelope,
    createRemoteLoader: createRemoteLoader,
    getLastLoadContext: function () { return lastLoadContext; },
  };

  function WEBgetLastLoadContext() { return lastLoadContext; }

  boot();
})();
