// MD editor entry: expose markdown-it to the WebView2 markdown document editor as a global.
// Bundled to a single IIFE (md.bundle.js) so the main app loads it offline (same pattern as
// tiptap.bundle.js). html:false keeps user/agent-authored markdown from injecting raw HTML (XSS).
import MarkdownIt from 'markdown-it';

const md = new MarkdownIt({
  html: false,
  linkify: true,
  breaks: true,
});

// Bridge contract used by the markdown HTML template in DiaryEditorPage.
globalThis.NovaraMd = {
  render(text) {
    return md.render(text || '');
  },
};
