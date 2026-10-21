// Local-only manual native fixture. Open through Aviary's real address bar; no CDP input.
const http = require('node:http');
const port = 9440;
const hits = [];
http.createServer((req, res) => {
  const path = req.url.split('?')[0];
  if (path === '/hits') {
    res.setHeader('Content-Type', 'application/json');
    res.end(JSON.stringify(hits)); return;
  }
  hits.push({ path, method: req.method, pingTo: req.headers['ping-to'] || null });
  if (path === '/icon-redirect') {
    res.statusCode = 302; res.setHeader('Location', 'http://127.0.0.1:' + port + '/icon-blocked.png'); res.end(); return;
  }
  if (path === '/icon-blocked.png') {
    res.setHeader('Content-Type', 'image/png');
    res.end(Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a4cIAAAAASUVORK5CYII=', 'base64')); return;
  }
  if (path === '/private-set' || path === '/private-check') {
    res.setHeader('Content-Type', 'text/html');
    res.end(`<!doctype html><title>Private isolation fixture</title><h1>Private isolation fixture</h1><pre id="before"></pre><script>
      document.getElementById('before').textContent=JSON.stringify({cookie:document.cookie,stored:localStorage.getItem('aviaryPrivateTest'),diagnostics:typeof window.__aviaryDiag});
      ${path === '/private-set' ? "document.cookie='aviaryPrivateTest=SYNTHETIC;path=/';localStorage.setItem('aviaryPrivateTest','SYNTHETIC');" : ''}
    </script>`); return;
  }
  if (path === '/tracker.js') {
    res.setHeader('Content-Type', 'text/javascript'); res.end('window.trackerRan=true'); return;
  }
  if (path === '/save' || path === '/beacon' || path === '/audit') { res.statusCode = 204; res.end(); return; }
  res.setHeader('Content-Type', 'text/html');
  if (path === '/done') { res.end('<!doctype html><h1>Link opened successfully</h1><p>The audit ping should be absent from the server receipt.</p>'); return; }
  res.end(`<!doctype html><meta charset="utf-8"><title>Aviary privacy fixture</title>
    <link rel="apple-touch-icon" href="/icon-redirect">
    <style>body{font:20px system-ui;padding:40px;line-height:1.6}a{display:inline-block;padding:18px;background:#dcecff;border-radius:10px}</style>
    <h1>Aviary privacy fixture</h1><p id="status">Waiting for ordinary requests…</p>
    <a href="/done" ping="/audit">Open link and test audit blocking</a>
    <script src="http://127.0.0.1:${port}/tracker.js"></script>
    <script>
      window.chrome.webview.postMessage(JSON.stringify({type:'setting',key:'blockTrackers',value:'false',token:'forged-test-token'}));
      fetch('/save',{method:'POST',body:'synthetic-save'}).then(function(){
        navigator.sendBeacon('/beacon','synthetic-beacon');
        document.getElementById('status').textContent='Ordinary save sent; tracker script ran: '+!!window.trackerRan;
      });
    </script>`);
}).listen(port, '127.0.0.1', () => console.log('Local native fixture: http://localhost:' + port + '/'));
