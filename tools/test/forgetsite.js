// Forgetful browsing's test site (M4.9 stage 3). Two SITES on one server, because the whole
// question is whether forgetting one leaves the other alone:
//
//   http://localhost:8791/    -> registrable domain "localhost"
//   http://127.0.0.1:8791/    -> registrable domain "127.0.0.1" (an IPv4 literal, returned as-is)
//
// Chromium treats both as loopback, so both get storage on plain http. The page fills every kind
// of storage Aviary claims to clear - a cookie, localStorage, sessionStorage and an IndexedDB
// record - and `await report()` says what is still there. A forgotten site must answer with
// nothing; the other must answer with everything.
//
//   node forgetsite.js
const http = require('http');
const PORT = 8791;

const PAGE = `<!doctype html><meta charset="utf-8"><title>forget-me HOSTNAME</title>
<body style="font:16px system-ui;padding:2rem"><h1 id=h>…</h1><pre id=o></pre>
<script>
const MARK = location.host;
function idb(fn) {
  return new Promise((res, rej) => {
    const r = indexedDB.open('forgetdb', 1);
    r.onupgradeneeded = () => r.result.createObjectStore('kv');
    r.onerror = () => rej(r.error);
    r.onsuccess = () => { try { fn(r.result, res, rej); } catch (e) { rej(e); } };
  });
}
async function fill() {
  document.cookie = 'forgetme=' + MARK + '; path=/; max-age=86400';
  localStorage.setItem('forgetme', MARK);
  sessionStorage.setItem('forgetme', MARK);
  await idb((db, res, rej) => {
    const tx = db.transaction('kv', 'readwrite');
    tx.objectStore('kv').put(MARK, 'forgetme');
    tx.oncomplete = () => { db.close(); res(true); };
    tx.onerror = () => { db.close(); rej(tx.error); };
  });
  return true;
}
async function report() {
  let db = null;
  try {
    db = await idb((d, res) => {
      const tx = d.transaction('kv', 'readonly');
      const g = tx.objectStore('kv').get('forgetme');
      g.onsuccess = () => { d.close(); res(g.result ?? null); };
      g.onerror = () => { d.close(); res(null); };
    });
  } catch (e) { db = null; }
  return {
    host: MARK,
    cookie: /(?:^|; )forgetme=([^;]*)/.test(document.cookie) ? RegExp.$1 : null,
    local: localStorage.getItem('forgetme'),
    session: sessionStorage.getItem('forgetme'),
    idb: db,
  };
}
window.fill = fill; window.report = report;
(location.search.includes('read') ? report() : fill().then(report)).then(r => {
  document.getElementById('h').textContent = 'forget-me ' + MARK;
  document.getElementById('o').textContent = JSON.stringify(r, null, 2);
});
</script>`;

http.createServer((req, res) => {
  if (req.url === '/favicon.ico') { res.writeHead(404).end(); return; }
  res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
  res.end(PAGE.replace('HOSTNAME', (req.headers.host || '').split(':')[0]));
}).listen(PORT, () => console.log('forget site on http://localhost:' + PORT + ' and http://127.0.0.1:' + PORT));
