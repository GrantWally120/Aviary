// Links, and what modified clicks on them do (Alpha Build 5 M5 stage 3, LinkScript).
//
// Two SITES on one server: 127.0.0.1:8795 and 127.0.0.2:8795 (different hosts, so cross-origin -
// what Alt+click's download has to cope with).
//
//   /links        every kind of link: plain, target=_blank, inside a shadow root, one the page
//                 handles itself (a web app's own Ctrl+click), javascript:, mailto:, a same-site file,
//                 a same-site attachment, a cross-site file, and a link with a download attribute
//   /dest?n=...   a page titled "dest <n>"
//   /file.txt     a plain text file (NOT an attachment: a click shows it; Alt+click must save it)
//   /att          an attachment
//   /hits         every request
//
//   node linksite.js   ->  http://127.0.0.1:8795/links
const http = require('http');
const url = require('url');
const PORT = 8795;
const hits = [];

const LINKS = `<!doctype html><meta charset="utf-8"><title>link probe</title>
<body style="font:15px system-ui;padding:2rem;line-height:2.1">
<h1>link probe</h1>
<a id="plain" href="/dest?n=plain">plain link</a><br>
<a id="blank" href="/dest?n=blank" target="_blank">target=_blank link</a><br>
<span id="host"></span><br>
<a id="app" href="/dest?n=app">a link the page handles itself</a><br>
<a id="js" href="javascript:document.title='js ran'">javascript: link</a><br>
<a id="mail" href="mailto:someone@example.com">mailto: link</a><br>
<a id="file" href="/file.txt">file.txt (shown, not an attachment)</a><br>
<a id="att" href="/att">an attachment</a><br>
<a id="cross" href="http://127.0.0.2:8795/file.txt">file.txt on ANOTHER site</a><br>
<a id="named" href="/file.txt" download="chosen-name.txt">a link with download="chosen-name.txt"</a><br>
<p id="log">-</p>
<script>
  // A link inside a shadow root: e.target.closest('a') cannot see it from outside.
  const host = document.getElementById('host');
  const root = host.attachShadow({ mode: 'open' });
  root.innerHTML = '<a id="shadow" href="/dest?n=shadow">link in a shadow root</a>';
  // A web app's own link handling (Gmail, Jira): it calls preventDefault and does its own thing,
  // and a browser must not take the click away from it.
  document.getElementById('app').addEventListener('click', e => {
    e.preventDefault();
    document.getElementById('log').textContent = 'the page handled ' + (e.ctrlKey ? 'ctrl+' : '') + 'click';
  });
</script>`;

function handle(req, res) {
  const u = url.parse(req.url, true);
  hits.push({ t: Date.now(), host: req.headers.host, url: req.url });
  if (u.pathname === '/hits') { res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify(hits)); return; }
  if (u.pathname === '/links' || u.pathname === '/') { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }); res.end(LINKS); return; }
  if (u.pathname === '/dest') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
    res.end('<!doctype html><title>dest ' + String(u.query.n || '').replace(/[<>&"]/g, '') + '</title><p>destination');
    return;
  }
  if (u.pathname === '/file.txt') { res.writeHead(200, { 'content-type': 'text/plain' }); res.end('a plain text file, ' + req.headers.host); return; }
  if (u.pathname === '/att') { res.writeHead(200, { 'content-type': 'application/octet-stream', 'content-disposition': 'attachment; filename="attached.bin"' }); res.end(Buffer.alloc(3000)); return; }
  res.writeHead(404).end();
}

// Loopback only (never ::, which brings up the firewall prompt): two hosts, two listeners.
for (const host of ['127.0.0.1', '127.0.0.2']) {
  http.createServer(handle).listen(PORT, host);
}
console.log('linksite on http://127.0.0.1:' + PORT + '/links (and 127.0.0.2)');
