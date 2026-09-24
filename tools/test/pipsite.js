// Will the engine really open a picture-in-picture window when Aviary is the host? (M4.9 stage 3.)
//
// document.pictureInPictureEnabled says the API is compiled in; it does NOT say a WebView2 host
// gets a window. This page makes a real playing video out of a canvas stream - no file, no network -
// and puts both kinds of picture-in-picture behind buttons, because both need a user gesture and
// cdp.js click sends a trusted one.
//
//   node pipsite.js          ->  http://localhost:8792/
const http = require('http');
const PORT = 8792;

const PAGE = `<!doctype html><meta charset="utf-8"><title>pip probe</title>
<body style="font:16px system-ui;padding:2rem;background:#111;color:#eee">
<h1>pip probe</h1>
<video id="v" autoplay muted playsinline style="width:320px;height:180px;background:#333"></video>
<p><button id="video">Video picture-in-picture</button>
   <button id="doc">Document picture-in-picture</button></p>
<pre id="o">…</pre>
<script>
const say = t => document.getElementById('o').textContent = t;
const c = document.createElement('canvas');
c.width = 320; c.height = 180;
const g = c.getContext('2d');
let n = 0;
setInterval(() => {
  g.fillStyle = 'hsl(' + (n++ % 360) + ' 70% 45%)';
  g.fillRect(0, 0, 320, 180);
  g.fillStyle = '#fff'; g.font = '28px system-ui'; g.fillText('frame ' + n, 20, 100);
}, 100);
const v = document.getElementById('v');
v.srcObject = c.captureStream(10);
v.play().catch(e => say('play refused: ' + e));

document.getElementById('video').onclick = async () => {
  try { await v.requestPictureInPicture(); say('VIDEO PIP OK: ' + !!document.pictureInPictureElement); }
  catch (e) { say('VIDEO PIP FAILED: ' + e.name + ' - ' + e.message); }
};
document.getElementById('doc').onclick = async () => {
  try {
    const w = await documentPictureInPicture.requestWindow({ width: 320, height: 180 });
    w.document.body.style.background = '#204020';
    w.document.body.textContent = 'document picture-in-picture';
    say('DOC PIP OK: ' + (w ? 'window' : 'none'));
  } catch (e) { say('DOC PIP FAILED: ' + e.name + ' - ' + e.message); }
};
window.pipState = () => JSON.stringify({
  playing: !v.paused, readyState: v.readyState,
  inPip: !!document.pictureInPictureElement,
  docPipWindow: !!(window.documentPictureInPicture && documentPictureInPicture.window),
  said: document.getElementById('o').textContent,
});
</script>`;

http.createServer((req, res) => {
  if (req.url === '/favicon.ico') { res.writeHead(404).end(); return; }
  res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
  res.end(PAGE);
}).listen(PORT, () => console.log('pip probe on http://localhost:' + PORT));
