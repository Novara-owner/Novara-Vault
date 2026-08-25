// POC entry: expose Tiptap to the WebView2 diary editor as a global object.
// This is bundled to a single IIFE (tiptap.bundle.js) so the main app can load it
// offline from ms-appx:// without any bundler/CDN at runtime.
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import TextStyle from '@tiptap/extension-text-style';
import Color from '@tiptap/extension-color';
import Image from '@tiptap/extension-image';
import Underline from '@tiptap/extension-underline';
import Link from '@tiptap/extension-link';
import TextAlign from '@tiptap/extension-text-align';

// Custom image node:

// 2. persist width/height so resizing is saved with the document.
// 3. NodeView enables 8-direction edge dragging (like a window). Not aspect-locked.
// 4. TextAlign is configured to include 'image', so setTextAlign writes a textAlign attr onto this
//    node; the NodeView wraps the image in a full-width outer div whose text-align does the layout.
const ResizableImage = Image.extend({
  addOptions() {
    return { inline: false, allowBase64: true, HTMLAttributes: {} };
  },
  draggable() {
    return false;
  },
  addAttributes() {
    return {
      src: { default: null },
      alt: { default: null },
      title: { default: null },
      width: { default: null },
      height: { default: null },
    };
  },
  addNodeView() {
    return ({ node, editor, getPos }) => {
      const outer = document.createElement('div');
      outer.className = 'resizable-image-outer';
      outer.style.display = 'block';
      outer.style.width = '100%';
      outer.style.textAlign = node.attrs.textAlign || 'left';

      const wrapper = document.createElement('div');
      wrapper.className = 'resizable-image-wrapper';
      wrapper.style.position = 'relative';
      wrapper.style.display = 'inline-block';
      wrapper.style.lineHeight = '0';
      wrapper.style.maxWidth = '100%';
      wrapper.style.verticalAlign = 'top';

      const img = document.createElement('img');
      img.draggable = false;
      img.className = 'resizable-image';
      img.src = node.attrs.src || '';
      if (node.attrs.alt) img.alt = node.attrs.alt;
      img.style.display = 'block';
      img.style.margin = '0';
      applySize(node.attrs.width, node.attrs.height);
      wrapper.appendChild(img);
      outer.appendChild(wrapper);

      let selected = false;
      let resizing = false;
      let dir = '';
      let startX = 0, startY = 0, startW = 0, startH = 0;

      const EDGE = 8;
      const CURSORS = {
        n: 'ns-resize', s: 'ns-resize', e: 'ew-resize', w: 'ew-resize',
        ne: 'nesw-resize', sw: 'nesw-resize', nw: 'nwse-resize', se: 'nwse-resize',
      };

      function applySize(w, h) {
        if (w) { wrapper.style.width = w + 'px'; img.style.width = '100%'; img.style.maxWidth = 'none'; }
        else { wrapper.style.width = ''; img.style.width = ''; img.style.maxWidth = '400px'; }
        if (h) { wrapper.style.height = h + 'px'; img.style.height = '100%'; }
        else { wrapper.style.height = ''; img.style.height = ''; }
      }

      function getDir(e) {
        const r = img.getBoundingClientRect();
        const x = e.clientX - r.left;
        const y = e.clientY - r.top;
        const L = x <= EDGE, R = x >= r.width - EDGE, T = y <= EDGE, B = y >= r.height - EDGE;
        if (L && T) return 'nw';
        if (L && B) return 'sw';
        if (R && T) return 'ne';
        if (R && B) return 'se';
        if (L) return 'w';
        if (R) return 'e';
        if (T) return 'n';
        if (B) return 's';
        return '';
      }

      function setSelected(v) {
        selected = v;
        img.style.outline = v ? '2px solid #8C93FF' : 'none';
        img.style.outlineOffset = v ? '1px' : '0px';
      }

      wrapper.addEventListener('pointermove', (e) => {
        if (resizing) {
          e.preventDefault();
          const dx = e.clientX - startX;
          const dy = e.clientY - startY;
          let nw = startW, nh = startH;
          if (dir.indexOf('e') >= 0) nw = startW + dx;
          if (dir.indexOf('w') >= 0) nw = startW - dx;
          if (dir.indexOf('s') >= 0) nh = startH + dy;
          if (dir.indexOf('n') >= 0) nh = startH - dy;
          nw = Math.max(20, nw);
          nh = Math.max(20, nh);
          wrapper.style.width = nw + 'px';
          wrapper.style.height = nh + 'px';
          const tx = dir.indexOf('w') >= 0 ? (startW - nw) : 0;
          const ty = dir.indexOf('n') >= 0 ? (startH - nh) : 0;
          wrapper.style.transform = 'translate(' + tx + 'px,' + ty + 'px)';
          return;
        }
        if (!selected) return;
        const d = getDir(e);
        wrapper.style.cursor = d ? CURSORS[d] : 'default';
      });

      wrapper.addEventListener('pointerleave', () => {
        if (!resizing) wrapper.style.cursor = 'default';
      });

      wrapper.addEventListener('pointerdown', (e) => {
        if (!selected) return;
        const d = getDir(e);
        if (!d) return;
        e.preventDefault();
        e.stopPropagation();
        resizing = true;
        dir = d;
        startX = e.clientX;
        startY = e.clientY;
        startW = wrapper.getBoundingClientRect().width;
        startH = wrapper.getBoundingClientRect().height;
        wrapper.style.transform = 'none';
        try { wrapper.setPointerCapture(e.pointerId); } catch (_) {}
      });

      wrapper.addEventListener('pointerup', (e) => {
        if (!resizing) return;
        resizing = false;
        const w = Math.round(wrapper.getBoundingClientRect().width);
        const h = Math.round(wrapper.getBoundingClientRect().height);
        wrapper.style.transform = 'none';
        wrapper.style.width = w + 'px';
        wrapper.style.height = h + 'px';
        img.style.width = '100%';
        img.style.height = '100%';
        if (typeof getPos === 'function') {
          try { editor.commands.updateAttributes('image', { width: w, height: h }); } catch (_) {}
        }
        try { wrapper.releasePointerCapture(e.pointerId); } catch (_) {}
      });

      return {
        dom: outer,
        update(updatedNode) {
          if (updatedNode.type.name !== 'image') return false;
          img.src = updatedNode.attrs.src || '';
          if (updatedNode.attrs.alt) img.alt = updatedNode.attrs.alt; else img.removeAttribute('alt');
          applySize(updatedNode.attrs.width, updatedNode.attrs.height);
          outer.style.textAlign = updatedNode.attrs.textAlign || 'left';
          return true;
        },
        selectNode() { setSelected(true); },
        deselectNode() { setSelected(false); },
        stopEvent() { return false; },
        ignoreMutation() { return true; },
      };
    };
  },
});

// Expose the pieces the C# diary editor needs. `window.NovaraTiptap` is the bridge
// contract used by the POC HTML page (and later by DiaryEditorPage).
globalThis.NovaraTiptap = {
  Editor,
  StarterKit,
  TextStyle,
  Color,
  Image: ResizableImage,
  Underline,
  Link,
  TextAlign,
};
