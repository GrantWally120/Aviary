// Aviary M2 test site. Two SITES: A = http://127.0.0.1:8766, B = http://127.0.0.2:8766 — different
// hosts, so to the browser every A<->B request is cross-site (IP hosts have no registrable domain
// beyond themselves). Both are loopback, so HTTPS-Only leaves them alone by design.
const http = require('http');
const url = require('url');
const PORT = 8766;
const A = 'http://127.0.0.1:' + PORT, B = 'http://127.0.0.2:' + PORT;
const hits = [];

const page = (title, body, head = '') => `<!doctype html><html><head><meta charset="utf-8"><title>${title}</title>${head}
<style>body{font:15px system-ui;margin:32px;max-width:900px} pre{background:#8881;padding:10px;white-space:pre-wrap}</style>
</head><body>${body}</body></html>`;

function headersOf(req) {
  const h = {};
  for (const k of Object.keys(req.headers)) h[k] = req.headers[k];
  return h;
}

function record(req, u) {
  hits.push({ t: Date.now(), host: req.headers.host, path: u.path, referer: req.headers.referer || null,
              cookie: req.headers.cookie || null, gpc: req.headers['sec-gpc'] || null,
              ua: req.headers['user-agent'] || null, chua: req.headers['sec-ch-ua'] || null, method: req.method });
  if (hits.length > 200) hits.shift();
}

const echoScript = `<script>
  var j = {};
  try { j.ua = navigator.userAgent; } catch(e){}
  try { j.uaDataIn = ('userAgentData' in navigator); j.brands = navigator.userAgentData ? navigator.userAgentData.brands : null; } catch(e){}
  try { j.gpc = navigator.globalPrivacyControl; } catch(e){}
  try { j.dnt = navigator.doNotTrack; } catch(e){}
  j.referrer = document.referrer; j.cookie = document.cookie; j.href = location.href;
  window.__echo = { headers: HEADERS, js: j };
  document.getElementById('j').textContent = JSON.stringify(j, null, 1);
  // worker view of GPC
  try {
    var w = new Worker(URL.createObjectURL(new Blob(["postMessage({gpc: self.navigator.globalPrivacyControl, uaDataIn: ('userAgentData' in self.navigator)})"])));
    w.onmessage = function(e){ window.__echo.worker = e.data; document.getElementById('w').textContent = JSON.stringify(e.data); };
  } catch(e) { window.__echo.worker = {error: String(e)}; }
</script>`;

const routes = {
  '/': () => page('M2 test site', `<h1>M2 test site</h1><ul>
    <li><a href="/echo">/echo</a> · <a href="/params">/params</a> · <a href="/3pc-top">/3pc-top</a> · <a href="/ref-top">/ref-top</a> · <a href="/webrtc">/webrtc</a> · <a href="/form">/form</a> · <a href="/spoof-shortcut">/spoof-shortcut</a></li></ul>`),

  '/echo': (req) => page('Echo', `<h1>Echo</h1><h3>Headers the server received</h3><pre id="h"></pre><h3>What scripts see</h3><pre id="j"></pre><h3>Worker</h3><pre id="w"></pre>
    <script>var HEADERS = ${JSON.stringify(headersOf(req))}; document.getElementById('h').textContent = JSON.stringify(HEADERS, null, 1);</script>${echoScript}`),

  '/set-cookie': () => page('Cookie set', '<h1>First-party cookies set on this site</h1>'),

  // Site A embeds site B in a frame. The frame reports what cookies IT received and can set.
  '/3pc-top': () => page('3PC top', `<h1>Third-party cookie probe (top = A)</h1><pre id="r">waiting</pre>
    <img src="${B}/pixel?from=3pc-top" width="1" height="1">
    <iframe id="f" src="${B}/3pc-frame" width="600" height="120"></iframe>
    <script>window.addEventListener('message', function(e){ window.__3pc = e.data; document.getElementById('r').textContent = JSON.stringify(e.data, null, 1); });</script>`),

  '/3pc-frame': (req) => page('3PC frame', `<p>frame on B</p><script>
    var got = ${JSON.stringify(req.headers.cookie || '')};
    var before = document.cookie;
    try { document.cookie = 'tp=2; Path=/; SameSite=None; Secure'; } catch(e) {}
    var after = document.cookie;
    parent.postMessage({ cookieHeaderReceived: got, docCookieBefore: before, docCookieAfterSet: after }, '*');
  </script>`),

  // A page that asks for the FULL referrer everywhere; the browser's trimming must win.
  '/ref-top': () => page('Ref top', `<h1>Referrer probe</h1><p id="x">x</p>
    <img src="${B}/pixel?from=ref-top-img" width="1" height="1">
    <a id="out" href="${B}/echo?from=ref-top-link">cross-site link</a>
    <script>fetch('${B}/pixel?from=ref-top-fetch', {mode:'no-cors'}).catch(function(){});</script>`,
    '<meta name="referrer" content="unsafe-url">'),

  '/params': () => page('Params', `<h1>Tracking links</h1>
    <p><a id="cross" href="${B}/echo?utm_source=news&fbclid=ABC123&id=keep#frag">cross-site, tracked</a></p>
    <p><a id="same" href="/echo?utm_source=same&id=keep">same-site, tracked</a></p>
    <p><a id="blank" target="_blank" href="${B}/echo?gclid=XYZ789&q=1">new tab, tracked</a></p>
    <p><a id="redir" href="/redirect-tracked">redirect that adds fbclid (cross-site)</a></p>`),

  '/form': () => page('Form', `<h1>POST form</h1>
    <form method="post" action="${B}/echo?utm_source=post&id=9"><input name="q" value="v"><button id="go">Send</button></form>`),

  '/webrtc': () => page('WebRTC', `<h1>WebRTC candidates</h1><pre id="c">gathering</pre><script>
    window.__cands = null;
    (async function(){
      var cands = [];
      try {
        var pc = new RTCPeerConnection({iceServers: []});
        pc.createDataChannel('x');
        pc.onicecandidate = function(e){ if (e.candidate) { cands.push(e.candidate.candidate); } else { window.__cands = cands; document.getElementById('c').textContent = cands.join('\\n') || '(none)'; } };
        await pc.setLocalDescription(await pc.createOffer());
        setTimeout(function(){ if (!window.__cands) { window.__cands = cands; document.getElementById('c').textContent = (cands.join('\\n') || '(none)') + '\\n(timeout)'; } }, 4000);
      } catch(e) { window.__cands = ['error ' + e]; }
    })();
  </script>`),

  // A website pretending to be Aviary's own shortcut script.
  '/spoof-shortcut': () => page('Spoof shortcut', `<h1>Spoof shortcut</h1><script>
    var send = function(o){ try { window.chrome.webview.postMessage(JSON.stringify(o)); } catch(e){} };
    send({type:'shortcut', action:'newtab'});
    send({type:'shortcut', action:'sidebar', token:'0123456789abcdef0123456789abcdef'});
    window.dispatchEvent(new KeyboardEvent('keydown', {key:'t', code:'KeyT', ctrlKey:true, bubbles:true}));
    document.title = 'Spoof shortcut sent';
  </script>`),

  '/fullscreen': () => page('Full screen', `<h1>Full screen</h1><div id="v" style="background:#123;color:#fff;height:200px;display:grid;place-items:center">a "video"</div>
    <button id="go" onclick="document.getElementById('v').requestFullscreen()">Full screen</button>`),

  '/favicon-proto': () => page('Favicon test', '<h1>Protocol-relative icon</h1>', '<link rel="icon" href="//127.0.0.2:8766/icon.png?proto-relative">'),
  '/trackers': () => page('Trackers', '<h1>Tracker page</h1><script src="https://www.google-analytics.com/analytics.js"></script><img src="https://ad.doubleclick.net/pixel?from=top" width="1" height="1"><iframe src="https://tpc.googlesyndication.com/safeframe/x.html" width="10" height="10"></iframe>'),
  '/tracker-frame-top': () => page('Frame top', '<h1>Cross-origin frame that loads a tracker</h1><iframe src="' + B + '/tracker-frame" width="400" height="80"></iframe>'),
  '/tracker-frame': () => page('Tracker frame', '<p>frame on B</p><img src="https://stats.g.doubleclick.net/pixel?from=cross-origin-frame" width="1" height="1">'),
  '/login': () => page('Sign in', '<h1>Sign in</h1><form method="post" action="/welcome" autocomplete="on"><label>Email <input id="u" name="email" type="email" autocomplete="username"></label><label>Password <input id="p" name="password" type="password" autocomplete="current-password"></label><button id="go">Sign in</button></form>'),
  '/welcome': () => page('Welcome', '<h1>Signed in (not really)</h1>'),
  '/download': () => page('Download', `<h1>Download</h1><a id="dl" href="/file.txt">get file</a>`),
};

function handler(req, res) {
  const u = url.parse(req.url, true);
  record(req, u);

  if (u.pathname === '/hits') {
    res.writeHead(200, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*' });
    return res.end(JSON.stringify(hits.slice(-60)));
  }
  if (u.pathname === '/icon.png') {
    res.writeHead(200, { 'Content-Type': 'image/png' });
    return res.end(Buffer.from('iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAIAAACQkWg2AAAAHUlEQVR4nGP4z8BAEmIYVTCqYFTBqIJRBUNFAQA3Lf8BJQ3i1gAAAABJRU5ErkJggg==', 'base64'));
  }
  if (u.pathname === '/pixel') {
    res.writeHead(200, { 'Content-Type': 'image/gif', 'Access-Control-Allow-Origin': '*' });
    return res.end(Buffer.from('R0lGODlhAQABAAAAACw=', 'base64'));
  }
  if (u.pathname === '/redirect-tracked') {
    res.writeHead(302, { Location: B + '/echo?fbclid=REDIR42&id=5' });
    return res.end();
  }
  if (u.pathname === '/file.txt') {
    res.writeHead(200, { 'Content-Type': 'text/plain', 'Content-Length': 38, 'Content-Disposition': 'attachment; filename="aviary-m2-download-test.txt"' });
    return res.end('Aviary M2 download test. Delete me.\n\n\n');
  }
  const route = routes[u.pathname];
  if (!route) { res.writeHead(404, { 'Content-Type': 'text/plain' }); return res.end('not found'); }

  const h = { 'Content-Type': 'text/html; charset=utf-8' };
  if (u.pathname === '/set-cookie') {
    h['Set-Cookie'] = ['fp=1; Path=/; SameSite=None; Secure', 'lax=1; Path=/; SameSite=Lax'];
  }
  if (req.method === 'POST') {
    let body = ''; req.on('data', c => body += c); req.on('end', () => { res.writeHead(200, h); res.end(route(req).replace('<h1>Echo</h1>', '<h1>Echo (POST body: ' + body.replace(/[<>&]/g, '') + ')</h1>')); });
    return;
  }
  res.writeHead(200, h);
  res.end(route(req));
}

for (const host of ['127.0.0.1', '127.0.0.2']) {
  http.createServer(handler).listen(PORT, host, () => console.log('m2 site on http://' + host + ':' + PORT));
}
