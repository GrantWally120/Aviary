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

// Preload on hover (M6 stage 2): links to count. /target records nothing special - its hits ARE the
// measurement (a prefetch is a request the server sees without anyone clicking), with the headers
// that say what kind of request it was.
const HOVER = `<!doctype html><meta charset="utf-8"><title>exp hover</title>
<body style="margin:0;padding:2rem;font:20px system-ui;line-height:3">
<a id="same" href="/target?n=same">same site</a><br>
<a id="tracked" href="/target?n=tracked&utm_source=x&fbclid=y">same site, tracking parameters</a><br>
<a id="plain" href="http://127.0.0.2:8797/target?n=cross">another site, http</a><br>
</body>`;

// Bug 91: the three ways a site declares speculation rules - in its HTML (as WordPress does for every
// site since 6.8), added by script (the probe does that), and by an HTTP header pointing at a JSON file.
const SPEC_STATIC = `<!doctype html><meta charset="utf-8"><title>exp spec static</title>
<script type="speculationrules">{"prefetch":[{"source":"list","urls":["/target?n=static"],"eagerness":"immediate"}],
"prerender":[{"source":"list","urls":["/target?n=static-prerender"],"eagerness":"immediate"}]}</script>
<body style="font:20px system-ui;padding:2rem"><a id="doc" href="/target?n=static-hover">hover me</a>
<script type="speculationrules">{"prefetch":[{"where":{"href_matches":"/*"},"eagerness":"moderate"}]}</script></body>`;
const SPEC_HEADER = `<!doctype html><meta charset="utf-8"><title>exp spec header</title><body><p>rules by header</p></body>`;
const RULES_JSON = JSON.stringify({ prefetch: [{ source: 'list', urls: ['/target?n=header'], eagerness: 'immediate' }] });

// Two listeners, 127.0.0.1 and 127.0.0.2 (never ::), so a tab can move to a DIFFERENT SITE - a new
// renderer process - and "Make every website dark" can be checked to follow it there.
const handler = (req, res) => {
  hits.push({ t: Date.now(), url: req.url, host: req.headers.host, purpose: req.headers['sec-purpose'] || req.headers['purpose'] || '',
              mode: req.headers['sec-fetch-mode'] || '', dest: req.headers['sec-fetch-dest'] || '', gpc: req.headers['sec-gpc'] || '',
              referer: req.headers['referer'] || '', chua: req.headers['sec-ch-ua'] || '', ua: (req.headers['user-agent'] || '').slice(0, 60),
              probe: req.headers['x-aviary-probe'] || '' });
  const u = new URL(req.url, 'http://x');
  // /slow?n=..&cc=no-store|no-cache|max-age=300 - a page that takes 700 ms, with the cache rule real
  // sites use for HTML: does a <link rel=prefetch> of it make the click fast, or is it fetched again?
  if (u.pathname === '/slow') {
    setTimeout(() => {
      res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': u.searchParams.get('cc') || 'no-store' });
      res.end('<!doctype html><title>slow ' + (u.searchParams.get('n') || '') + '</title><p>slow');
    }, 700);
    return;
  }
  // /churn - what bug 91's page guard (a MutationObserver on every page) costs a page that adds a lot of
  // DOM: 40 batches of 500 rows, each a div with spans and a nested list, timed with the observer's
  // work included (each batch awaits a microtask checkpoint). window.__churn = median ms per batch.
  if (u.pathname === '/churn') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end(`<!doctype html><title>exp churn</title><body><div id="box"></div><script>
window.__churn = null;
(async function () {
  var box = document.getElementById('box'), times = [];
  for (var b = 0; b < 40; b++) {
    var t = performance.now(), frag = document.createElement('div');
    for (var i = 0; i < 500; i++) {
      var row = document.createElement('div');
      row.innerHTML = '<span>a</span><span>b</span><ul><li>c</li><li>d</li></ul>';
      frag.appendChild(row);
    }
    box.appendChild(frag);
    await Promise.resolve();
    times.push(performance.now() - t);
    box.textContent = '';
  }
  times.sort(function (a, b) { return a - b; });
  window.__churn = Math.round(times[20] * 100) / 100;
})();
</script></body>`);
    return;
  }
  // /tone - a quiet (gain 0.02) 220 Hz tone once play() is called with a user gesture (cdp.js eval
  // carries one), for "Mute tabs in the background": audio-peak.ps1 reads whether it comes out.
  if (u.pathname === '/tone') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end(`<!doctype html><title>exp tone</title><body style="font:20px system-ui;padding:2rem">tone<script>
window.play = function () {
  var c = new AudioContext(), o = c.createOscillator(), g = c.createGain();
  g.gain.value = 0.02; o.frequency.value = 220; o.connect(g); g.connect(c.destination); o.start();
  window.__ctx = c; return c.resume().then(function () { return c.state; });
};
</script></body>`);
    return;
  }
  // /pipsound - a VIDEO with sound (a drawn canvas + the quiet tone, as one stream) that pip() pops out
  // into picture in picture: a tab doing that must keep its sound when "Mute tabs in the background"
  // sends it to the back. pip() needs a user gesture, which cdp.js eval carries.
  if (u.pathname === '/pipsound') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end(`<!doctype html><title>exp pipsound</title><body style="font:20px system-ui;padding:2rem">
<canvas id="c" width="160" height="90"></canvas><video id="v" width="320" height="180" playsinline></video><script>
window.pip = async function () {
  var cv = document.getElementById('c'), g2 = cv.getContext('2d'), n = 0;
  setInterval(function () { g2.fillStyle = 'hsl(' + (n++ * 7 % 360) + ',70%,50%)'; g2.fillRect(0, 0, 160, 90); }, 50);
  var ac = new AudioContext(), o = ac.createOscillator(), gain = ac.createGain(), dest = ac.createMediaStreamDestination();
  gain.gain.value = 0.02; o.frequency.value = 220; o.connect(gain); gain.connect(dest); o.start();
  var stream = new MediaStream([cv.captureStream(20).getVideoTracks()[0], dest.stream.getAudioTracks()[0]]);
  var v = document.getElementById('v'); v.srcObject = stream; await v.play();
  await v.requestPictureInPicture();
  return !!document.pictureInPictureElement;
};
</script></body>`);
    return;
  }
  if (u.pathname === '/spec-static') { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' }); res.end(SPEC_STATIC); return; }
  if (u.pathname === '/spec-header') { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store', 'speculation-rules': '"/rules.json"' }); res.end(SPEC_HEADER); return; }
  if (u.pathname === '/rules.json') { res.writeHead(200, { 'content-type': 'application/speculationrules+json', 'cache-control': 'no-store' }); res.end(RULES_JSON); return; }
  // Bug 91's second look: rules inside a shadow root the HTML itself declares (open or closed).
  if (u.pathname === '/spec-dsd') {
    const mode = u.searchParams.get('mode') === 'closed' ? 'closed' : 'open';
    const n = encodeURIComponent(u.searchParams.get('n') || 'dsd');
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end(`<!doctype html><meta charset="utf-8"><title>exp spec dsd</title><body><div id="host"><template shadowrootmode="${mode}">` +
      `<script type="speculationrules">{"prefetch":[{"source":"list","urls":["/target?n=${n}"],"eagerness":"immediate"}]}</script><p>inside</p>` +
      `</template></div><p>a shadow root declared in the HTML (${mode})</p></body>`);
    return;
  }
  if (u.pathname === '/hover') { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' }); res.end(HOVER); return; }
  if (u.pathname === '/target') { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'max-age=60' }); res.end('<!doctype html><title>target ' + (u.searchParams.get('n') || '') + '</title><p>target'); return; }
  const page = { '/white': WHITE, '/long': LONG, '/raf': RAF, '/article': ARTICLE, ...SCHEMES }[u.pathname];
  if (u.pathname === '/hits') { res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify(hits)); return; }
  if (page) { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' }); res.end(page); return; }
  res.writeHead(404); res.end('not here');
};
for (const host of ['127.0.0.1', '127.0.0.2']) {
  http.createServer(handler).listen(PORT, host, () => console.log('expsite on http://' + host + ':' + PORT));
}
