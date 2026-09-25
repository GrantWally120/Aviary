// The Experimental menu, offline (Alpha Build 5 M6). Pages that let each experiment be SEEN
// working - or not - from DevTools, with no desktop input:
//
//   /white    a page with no background or colours of its own: black text on the browser's white.
//             "Make every website dark" must paint it dark (read from a screenshot - force-dark
//             happens at paint, so no computed style changes).
//   /long     a very tall page. After any wheel event it records scrollY on every animation frame
//             for 700 ms into window.__scroll: smooth scrolling gives many in-between positions,
//             instant scrolling one jump.
//   /raf      counts animation frames for 2 s into window.__fps (how often the page is drawn).
//   /hits     every request the server saw.
//
//   node expsite.js     ->  http://127.0.0.1:8797/
const http = require('http');
const PORT = 8797;
const hits = [];

const WHITE = `<!doctype html><meta charset="utf-8"><title>exp white</title>
<body style="margin:0;padding:2rem;font:20px system-ui">
<h1>A page with no colours of its own</h1>
${'<p>Plain black text on whatever the browser paints behind it. '.repeat(3)}</p>
<div id="swatch" style="height:300px"></div>
</body>`;

const LONG = `<!doctype html><meta charset="utf-8"><title>exp long</title>
<body style="margin:0;font:16px system-ui">
<div style="height:20000px;background:linear-gradient(#fff,#ccc)">scroll me</div>
<script>
window.__scroll = null;
var started = false;
function sample() {
  if (started) return; started = true;
  var out = [], t0 = performance.now();
  (function tick() {
    out.push(Math.round(scrollY));
    if (performance.now() - t0 < 700) requestAnimationFrame(tick); else window.__scroll = out;
  })();
}
/* Whatever starts the scroll - a wheel, a key - starts the sampling, BEFORE the first move. */
window.rearm = function () { started = false; window.__scroll = null; scrollTo(0, 0); };
addEventListener('wheel', sample, { passive: true, capture: true });
addEventListener('keydown', sample, { capture: true });
</script></body>`;

const RAF = `<!doctype html><meta charset="utf-8"><title>exp raf</title>
<body style="margin:0;font:16px system-ui"><div id="box" style="width:40px;height:40px;background:#08f"></div>
<script>
window.__fps = null;
window.measure = function () {
  window.__fps = null;
  var n = 0, t0 = performance.now(), box = document.getElementById('box');
  (function tick(t) {
    n++; box.style.transform = 'translateX(' + (n % 200) + 'px)';
    if (t - t0 < 2000) requestAnimationFrame(tick); else window.__fps = Math.round(n * 1000 / (t - t0));
  })(t0);
};
measure();
</script></body>`;

// "Make every website dark" must leave Aviary's own pages alone. Three ways a page can declare its
// colours, each on a white background: as Aviary's pages did before M6 (light dark), and the two
// forms of the CSS opt-out, `only light`, as CSS and as a meta tag.
const scheme = (title, head, css) => `<!doctype html><meta charset="utf-8"><title>${title}</title>${head}
<style>:root{${css}} body{margin:0;padding:2rem;font:20px system-ui;background:#fff;color:#111}</style>
<body><h1>${title}</h1><div style="height:400px"></div></body>`;
const SCHEMES = {
  '/scheme-light-dark': scheme('light dark', '', 'color-scheme: light dark;'),
  '/scheme-only-light': scheme('only light (css)', '', 'color-scheme: light dark;} @media (prefers-color-scheme: light){:root{color-scheme: only light;}'),
  '/scheme-only-light-meta': scheme('only light (meta)', '<meta name="color-scheme" content="only light">', ''),
};

// An article Reader will take (more than 120 words in one block), on the browser's white.
const ARTICLE = `<!doctype html><meta charset="utf-8"><title>exp article</title>
<body style="margin:0;padding:2rem;font:18px Georgia,serif"><article><h1>A page Reader can read</h1>
${Array.from({ length: 8 }, (_, i) => `<p>Paragraph ${i + 1}. The glasshouse stood at the end of the garden, its panes catching the last of the afternoon light, and inside it the air was warm and still and smelled of soil and green things growing slowly toward the glass.</p>`).join('\n')}
</article></body>`;

// Two listeners, 127.0.0.1 and 127.0.0.2 (never ::), so a tab can move to a DIFFERENT SITE - a new
// renderer process - and "Make every website dark" can be checked to follow it there.
const handler = (req, res) => {
  hits.push({ t: Date.now(), url: req.url });
  const u = new URL(req.url, 'http://x');
  const page = { '/white': WHITE, '/long': LONG, '/raf': RAF, '/article': ARTICLE, ...SCHEMES }[u.pathname];
  if (u.pathname === '/hits') { res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify(hits)); return; }
  if (page) { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' }); res.end(page); return; }
  res.writeHead(404); res.end('not here');
};
for (const host of ['127.0.0.1', '127.0.0.2']) {
  http.createServer(handler).listen(PORT, host, () => console.log('expsite on http://' + host + ':' + PORT));
}
