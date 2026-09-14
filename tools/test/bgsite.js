// Page backgrounds: what a page that paints no background of its own looks like, and what
// shows between two pages while one replaces the other (bug 61, 13 Sep 2026).
//
//   node bgsite.js            -> http://127.0.0.1:8772/ and http://127.0.0.2:8772/ (two SITES, so a
//                                navigation between them changes renderer process)
//
// Pages (every one leaves the right half of the window free of text, so flash-watch.ps1 can
// sample the page's background there):
//   /plain           no styles at all — the bug: dark grey under black text in a dark window
//   /empty           nothing to paint, so no first-paint report (a new tab's fallback)
//   /text            text/plain — Chromium itself renders it light on #121212 when dark is preferred
//   /white           body{background:#fff}
//   /black           html{background:#000}
//   /scheme-dark     <meta name=color-scheme content=dark>, no background — the engine's own dark canvas
//   /scheme-auto     <meta name=color-scheme content="light dark">, no background
//   /prefers         white, or #202020 under @media (prefers-color-scheme: dark)
//   /slowcss?ms=N&bg=RRGGBB   a stylesheet that takes N ms and paints the page RRGGBB
//   /slowdoc?ms=N&bg=RRGGBB   the server waits N ms before sending a page painted RRGGBB
//   /links           links to all of the above, same-site, cross-site and target=_blank
const http = require('http');

const PORT = 8772;
const words = '<p>Plain words on a page that sets no colours. Chrome and Safari show this on white.</p>';
const layout = (title, head, body) => `<!doctype html><html><head><meta charset="utf-8"><title>${title}</title>${head}
<style>.w{max-width:40%;font:16px/1.5 serif}</style></head><body><div class="w"><h1>${title}</h1>${body}</div></body></html>`;
const hex = s => (/^[0-9a-fA-F]{6}$/.test(s || '') ? s : '000000');

function route(u) {
  const q = u.searchParams;
  switch (u.pathname) {
    case '/plain':
      return [200, 'text/html', `<!doctype html><title>plain</title><div style="max-width:40%"><h1>plain</h1>${words}</div>`];
    case '/empty':
      return [200, 'text/html', '<!doctype html><title>empty</title>'];
    case '/text':
      return [200, 'text/plain; charset=utf-8', 'Plain text (text/plain). Chromium shows this light on dark when dark is preferred.\n'];
    case '/white':
      return [200, 'text/html', layout('white', '<style>body{background:#fff;color:#000}</style>', words)];
    case '/black':
      return [200, 'text/html', layout('black', '<style>html{background:#000;color:#eee}</style>', words)];
    case '/scheme-dark':
      return [200, 'text/html', layout('scheme-dark', '<meta name="color-scheme" content="dark">', words)];
    case '/scheme-auto':
      return [200, 'text/html', layout('scheme-auto', '<meta name="color-scheme" content="light dark">', words)];
    case '/prefers':
      return [200, 'text/html', layout('prefers', '<style>html{background:#fff;color:#000}@media (prefers-color-scheme: dark){html{background:#202020;color:#eee}}</style>', words)];
    case '/slowcss':
      return [200, 'text/html', layout('slowcss', `<link rel="stylesheet" href="/css?ms=${+q.get('ms') || 1500}&bg=${hex(q.get('bg'))}">`, words)];
    case '/css':
      return [200, 'text/css', `html{background:#${hex(q.get('bg'))};color:#888}`, +q.get('ms') || 0];
    case '/slowdoc':
      return [200, 'text/html', layout('slowdoc', `<style>html{background:#${hex(q.get('bg'))};color:#888}</style>`, words), +q.get('ms') || 1500];
    case '/links': {
      const pages = ['plain', 'empty', 'text', 'white', 'black', 'scheme-dark', 'scheme-auto', 'prefers', 'slowcss?ms=1500&bg=000000', 'slowdoc?ms=1500&bg=000000'];
      const other = u.host.startsWith('127.0.0.2') ? '127.0.0.1' : '127.0.0.2';
      const row = p => `<li><a id="s-${p.split('?')[0]}" href="/${p}">${p}</a> · <a id="x-${p.split('?')[0]}" href="http://${other}:${PORT}/${p}">cross-site</a> · <a id="n-${p.split('?')[0]}" target="_blank" href="/${p}">new tab</a></li>`;
      return [200, 'text/html', layout('links', '', `<ul>${pages.map(row).join('')}</ul>`)];
    }
    default:
      return [404, 'text/plain', 'not found'];
  }
}

http.createServer((req, res) => {
  const u = new URL(req.url, `http://${req.headers.host}`);
  const [status, type, body, wait] = route(u);
  setTimeout(() => {
    res.writeHead(status, { 'Content-Type': type, 'Cache-Control': 'no-store' });
    res.end(body);
  }, wait || 0);
}).listen(PORT, '0.0.0.0', () => console.log(`bgsite on 127.0.0.1:${PORT} and 127.0.0.2:${PORT}`));
