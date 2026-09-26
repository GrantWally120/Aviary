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

// What fingerprinting scripts read (M6 stage 4). Hashes of a canvas drawing and an audio rendering, the
// graphics card's names, cores, memory, screen - all as one JSON from window.fp().
const FP = `
function h(s){var x=2166136261>>>0;for(var i=0;i<s.length;i++){x^=s.charCodeAt(i);x=Math.imul(x,16777619)>>>0;}return x.toString(16);}
function draw(){
  var c=document.createElement('canvas');c.width=240;c.height=60;var g=c.getContext('2d');
  g.textBaseline='top';g.font='16px Arial';g.fillStyle='#f60';g.fillRect(100,1,62,20);g.fillStyle='#069';g.fillText('Aviary fp \u{1F426} 12345',2,15);
  g.fillStyle='rgba(102,204,0,0.7)';g.fillText('Aviary fp \u{1F426} 12345',4,17);
  return c;
}
window.px=function(){var d=draw().getContext('2d').getImageData(0,0,240,60).data,s='';for(var i=0;i<d.length;i+=8192){s+=String.fromCharCode.apply(null,d.subarray(i,i+8192));}return btoa(s);};
window.fp=async function(){
  var c=draw(),g=c.getContext('2d');
  var canvas=h(c.toDataURL());
  var px=g.getImageData(0,0,240,60).data,clear=0;for(var i=0;i<px.length;i+=4){if(px[i+3]===0&&(px[i]||px[i+1]||px[i+2])){clear++;}}
  var audio='none';
  try{var ac=new OfflineAudioContext(1,5000,44100),o=ac.createOscillator(),k=ac.createDynamicsCompressor();o.type='triangle';o.frequency.value=10000;o.connect(k);k.connect(ac.destination);o.start(0);
    var buf=await ac.startRendering(),d=buf.getChannelData(0),sum=0;for(var j=4500;j<5000;j++){sum+=Math.abs(d[j]);}audio=sum.toFixed(12);}catch(e){audio='error '+e.name;}
  var gl=document.createElement('canvas').getContext('webgl'),vendor='',renderer='';
  if(gl){var ext=gl.getExtension('WEBGL_debug_renderer_info');if(ext){vendor=gl.getParameter(ext.UNMASKED_VENDOR_WEBGL);renderer=gl.getParameter(ext.UNMASKED_RENDERER_WEBGL);}}
  return JSON.stringify({canvas:canvas,seeThrough:clear,audio:audio,vendor:vendor,renderer:renderer,cores:navigator.hardwareConcurrency,memory:navigator.deviceMemory,
    screen:screen.width+'x'+screen.height+'x'+screen.colorDepth,plugins:navigator.plugins.length,lang:navigator.languages.join(',')});
};`;
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
              probe: req.headers['x-aviary-probe'] || '', pingTo: req.headers['ping-to'] || '', lang: req.headers['accept-language'] || '' });
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
  // M6 stage 4, "Stronger fingerprint protection": /fp computes what fingerprinting scripts read - a canvas
  // drawing's pixels, an audio rendering's samples, the graphics card's names, cores, memory, screen - and
  // window.fp() resolves to them as JSON (hashes, so two runs compare at a glance).
  // M6 stage 5, Auto-Play: /tone.wav is a SILENT two-second WAV (all zero samples - the autoplay rules
  // look at whether an element is muted, never at what it plays, so nobody at the PC hears a test).
  // /autoplay tries every way a page starts media by itself as it loads; state() says what played.
  if (u.pathname === '/tone.wav') {
    const rate = 8000, data = Buffer.alloc(rate * 2 * 2), h = Buffer.alloc(44);
    h.write('RIFF', 0); h.writeUInt32LE(36 + data.length, 4); h.write('WAVE', 8); h.write('fmt ', 12);
    h.writeUInt32LE(16, 16); h.writeUInt16LE(1, 20); h.writeUInt16LE(1, 22); h.writeUInt32LE(rate, 24);
    h.writeUInt32LE(rate * 2, 28); h.writeUInt16LE(2, 32); h.writeUInt16LE(16, 34); h.write('data', 36); h.writeUInt32LE(data.length, 40);
    res.writeHead(200, { 'content-type': 'audio/wav', 'cache-control': 'no-store' });
    res.end(Buffer.concat([h, data]));
    return;
  }
  if (u.pathname === '/autoplay') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end(`<!doctype html><title>exp autoplay</title><body style="font:20px system-ui;padding:2rem">media that starts by itself
<video id="vm" src="/tone.wav" muted autoplay loop playsinline></video><audio id="au" src="/tone.wav" autoplay loop></audio>
<video id="vp" src="/tone.wav" muted loop playsinline></video><script>
var r = { scriptSound: 'pending', scriptMuted: 'pending' }, ac = new AudioContext(), a2 = new Audio('/tone.wav');
a2.loop = true; r.ctx0 = ac.state;
a2.play().then(function () { r.scriptSound = 'played'; }, function (e) { r.scriptSound = e.name; });
document.getElementById('vp').play().then(function () { r.scriptMuted = 'played'; }, function (e) { r.scriptMuted = e.name; });
function on(el) { return !el.paused && el.currentTime > 0; }
window.state = function () { return JSON.stringify({ attrMuted: on(document.getElementById('vm')), attrSound: on(document.getElementById('au')),
  scriptMuted: r.scriptMuted, scriptSound: r.scriptSound, ctxAtLoad: r.ctx0, ctx: ac.state }); };
</script></body>`);
    return;
  }
  if (u.pathname === '/fp') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end('<!doctype html><title>exp fp</title><body style="font:20px system-ui;padding:2rem">what a fingerprinting script reads<script>' + FP + '</script></body>');
    return;
  }
  // M6 stage 4, "Block scripts from other websites": a script from this site, one from a subdomain-less
  // OTHER site (127.0.0.2), each setting a window flag; state() reports which ran.
  if (u.pathname === '/scripts') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end('<!doctype html><title>exp scripts</title><body style="font:20px system-ui;padding:2rem">scripts from here and elsewhere' +
      '<script src="/s.js?who=own"></script><script src="http://127.0.0.2:8797/s.js?who=other"></script>' +
      '<img src="http://127.0.0.2:8797/pixel.png" alt=""><script>window.state=function(){return JSON.stringify({own:!!window.__own,other:!!window.__other,img:document.images[0].complete&&document.images[0].naturalWidth>0})};</script></body>');
    return;
  }
  if (u.pathname === '/s.js') {
    res.writeHead(200, { 'content-type': 'text/javascript', 'cache-control': 'no-store' });
    res.end('window.__' + (u.searchParams.get('who') === 'other' ? 'other' : 'own') + ' = true;');
    return;
  }
  if (u.pathname === '/pixel.png') {
    res.writeHead(200, { 'content-type': 'image/png', 'cache-control': 'no-store' });
    res.end(Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64'));
    return;
  }
  // M6 stage 4, "Strip even more tracking from links": /params links to the OTHER site with parameters -
  // /hits shows which of them arrived.
  if (u.pathname === '/params') {
    const q = u.search.slice(1);
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end('<!doctype html><title>exp params</title><body style="font:20px system-ui;padding:2rem"><a id="go" href="http://127.0.0.2:8797/target?' + q.replace(/"/g, '') + '">a link carrying parameters</a></body>');
    return;
  }
  // M6 stage 4, "Forget everything when Aviary closes": put() stores a cookie (a day long) and a local
  // storage value; read() says what this site still has.
  if (u.pathname === '/store') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end(`<!doctype html><title>exp store</title><body style="font:20px system-ui;padding:2rem">a site that remembers you<script>
window.put = function () { document.cookie = 'who=' + location.hostname + '; max-age=86400; path=/'; localStorage.setItem('who', location.hostname); return true; };
window.read = function () { return JSON.stringify({ cookie: /who=/.test(document.cookie), local: localStorage.getItem('who') !== null }); };
</script></body>`);
    return;
  }
  // M6 stage 4, link-click pings (<a ping>, "hyperlink auditing"): #same pings this site, #cross pings
  // 127.0.0.2 - a tracker told which link you clicked. /pinged answers 204; its hits carry Ping-To.
  if (u.pathname === '/ping') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end('<!doctype html><title>exp ping</title><body style="font:20px system-ui;padding:2rem">' +
      '<p><a id="same" href="/target?n=ping-same" ping="/pinged?n=same">a link that pings this site</a></p>' +
      '<p><a id="cross" href="/target?n=ping-cross" ping="http://127.0.0.2:8797/pinged?n=cross">a link that pings another site</a></p></body>');
    return;
  }
  if (u.pathname === '/pinged') { res.writeHead(204); res.end(); return; }
  // M6 stage 4, "Stop websites asking to show notifications": ask() requests the permission (a gesture -
  // cdp.js eval carries one) and records the answer in window.__perm; state() says where it stands.
  if (u.pathname === '/notify') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end(`<!doctype html><title>exp notify</title><body style="font:20px system-ui;padding:2rem">a site that wants to notify you<script>
window.__perm = null;
window.ask = function () { return Notification.requestPermission().then(function (p) { window.__perm = p; return p; }); };
window.state = function () { return JSON.stringify({ permission: Notification.permission, answer: window.__perm }); };
</script></body>`);
    return;
  }
  // M6 stage 3, "Tint the toolbar to match the page". /tint?c=<any CSS colour> declares one theme-color;
  // /tint-scheme declares one per colour scheme (light #e0f0ff, dark #102030); /tint-app is a web app
  // that, 1.5 s after loading, changes its colour AND its address without loading a page (pushState).
  if (u.pathname === '/tint' || u.pathname === '/tint-scheme' || u.pathname === '/tint-app') {
    const c = (u.searchParams.get('c') || '#0a7d4b').replace(/[<>"&]/g, '');
    const metas = u.pathname === '/tint-scheme'
      ? '<meta name="theme-color" media="(prefers-color-scheme: light)" content="#e0f0ff"><meta name="theme-color" media="(prefers-color-scheme: dark)" content="#102030">'
      : `<meta name="theme-color" content="${c}">`;
    const app = u.pathname === '/tint-app'
      ? `<script>setTimeout(function(){document.querySelector('meta[name=theme-color]').setAttribute('content','#aa2200');history.pushState({},'','/tint-app?moved=1');document.title='exp tint app moved';},1500);</script>` : '';
    // &article=1: /article itself (a page Reader is known to take) with the colour added - to prove
    // Reader drops the tint. (A home-made article was tried first: Reader's probe found 0 words in it.)
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    if (u.searchParams.get('article') === '1') { res.end(ARTICLE.replace('<meta charset="utf-8">', '<meta charset="utf-8">' + metas)); return; }
    res.end(`<!doctype html><meta charset="utf-8">${metas}<title>exp tint</title><body style="font:20px system-ui;padding:2rem;background:#fff">a page that chooses a colour${app}</body>`);
    return;
  }
  // M6 stage 3, "Pop a playing video out when you switch tabs": a VIDEO that plays with sound (a drawn
  // canvas + the quiet tone, as one stream) once start() is called with a gesture - NOT popped out.
  // ?muted=1 plays it muted (must not pop out); ?nopip=1 bars picture in picture (must not either).
  if (u.pathname === '/video') {
    const muted = u.searchParams.get('muted') === '1', nopip = u.searchParams.get('nopip') === '1';
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end(`<!doctype html><title>exp video</title><body style="font:20px system-ui;padding:2rem">
<canvas id="c" width="160" height="90"></canvas><video id="v" width="480" height="270" playsinline${nopip ? ' disablepictureinpicture' : ''}></video><script>
window.start = async function () {
  var cv = document.getElementById('c'), g2 = cv.getContext('2d'), n = 0;
  setInterval(function () { g2.fillStyle = 'hsl(' + (n++ * 7 % 360) + ',70%,50%)'; g2.fillRect(0, 0, 160, 90); }, 50);
  var ac = new AudioContext(), o = ac.createOscillator(), gain = ac.createGain(), dest = ac.createMediaStreamDestination();
  gain.gain.value = 0.02; o.frequency.value = 220; o.connect(gain); gain.connect(dest); o.start();
  var v = document.getElementById('v');
  v.srcObject = new MediaStream([cv.captureStream(20).getVideoTracks()[0], dest.stream.getAudioTracks()[0]]);
  v.muted = ${muted ? 'true' : 'false'};
  await v.play();
  return !v.paused;
};
window.state = function () { return JSON.stringify({ pip: !!document.pictureInPictureElement, playing: !document.getElementById('v').paused }); };
</script></body>`);
    return;
  }
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
