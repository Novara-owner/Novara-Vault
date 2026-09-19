import MarkdownIt from 'markdown-it';

const md = new MarkdownIt({
  html: false,
  linkify: true,
  breaks: true,
});


globalThis.NovaraMd = {
  render(text) {
    return md.render(text || '');
  },
};
