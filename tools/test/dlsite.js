// Downloads, offline (Alpha Build 5 M5). Every kind of download the ring and the list have to
// handle, served slowly enough to watch:
//
//   /file?name=a.zip&size=5000000&rate=500000[&nolen=1]   an attachment, `rate` bytes a second;
//                                                         nolen = no Content-Length (the ring spins)
//   /drop?name=b.zip&size=4000000&at=1500000              sends `at` bytes, then cuts the connection
//                                                         (Accept-Ranges + ETag, so it can resume)
//   /program                                              a tiny "setup.exe" (MZ header) - the warning
//   /inline.pdf                                           NOT an attachment: opens as a page
//   /page                                                 links to all of the above, including a
//                                                         target=_blank one (a new tab that must close
//                                                         itself once it turns into a download)
//   /hits                                                 every request, with its Range header
//
//   node dlsite.js     ->  http://127.0.0.1:8793/
const http = require('http');
const url = require('url');
const PORT = 8793;
const hits = [];

function body(size, seed) {
  const b = Buffer.alloc(size);
  for (let i = 0; i < size; i++) { b[i] = (i * 31 + seed) & 0xff; }
  return b;
}

function stream(res, buf, start, rate, stopAt) {
  let pos = start;
  const chunk = Math.max(1000, Math.floor(rate / 10));
  const end = stopAt != null ? Math.min(stopAt, buf.length) : buf.length;
  const t = setInterval(() => {
    if (res.destroyed) { clearInterval(t); return; }
    const next = Math.min(end, pos + chunk);
    res.write(buf.subarray(pos, next));
    pos = next;
    if (pos >= end) {
      clearInterval(t);
      if (stopAt != null && stopAt < buf.length) { res.socket.destroy(); } else { res.end(); }
    }
  }, 100);
}

const PAGE = `<!doctype html><meta charset="utf-8"><title>dl probe</title>
<body style="font:15px system-ui;padding:2rem;line-height:2">
<h1>dl probe</h1>
<a id="slow" href="/file?name=slow.zip&size=6000000&rate=600000">slow.zip (6 MB, 10 s)</a><br>
<a id="quick" href="/file?name=quick.txt&size=20000&rate=2000000">quick.txt (20 KB)</a><br>
<a id="nolen" href="/file?name=unknown.bin&size=3000000&rate=500000&nolen=1">unknown.bin (no size)</a><br>
<a id="drop" href="/drop?name=dropped.zip&size=4000000&at=1500000">dropped.zip (cut at 1.5 MB)</a><br>
<a id="program" href="/program">setup.exe</a><br>
<a id="blank" href="/file?name=newtab.zip&size=3000000&rate=600000" target="_blank">newtab.zip in a NEW tab</a><br>
<a id="pdf" href="/inline.pdf">inline.pdf (a page, not a download)</a>
</body>`;

http.createServer((req, res) => {
  const u = url.parse(req.url, true);
  hits.push({ t: Date.now(), url: req.url, range: req.headers.range || '' });
  const q = u.query;

  if (u.pathname === '/hits') { res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify(hits)); return; }
  if (u.pathname === '/page' || u.pathname === '/') { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }); res.end(PAGE); return; }

  if (u.pathname === '/file') {
    const size = +(q.size || 1000000), rate = +(q.rate || 500000);
    const headers = { 'content-type': 'application/octet-stream', 'content-disposition': 'attachment; filename="' + (q.name || 'file.bin') + '"', 'cache-control': 'no-store' };
    if (!q.nolen) { headers['content-length'] = size; }
    res.writeHead(200, headers);
    stream(res, body(size, 7), 0, rate, null);
    return;
  }

  if (u.pathname === '/drop') {
    const size = +(q.size || 4000000), at = +(q.at || 1000000);
    const buf = body(size, 3);
    const m = /bytes=(\d+)-/.exec(req.headers.range || '');
    const headers = { 'content-type': 'application/octet-stream', 'content-disposition': 'attachment; filename="' + (q.name || 'dropped.zip') + '"', 'accept-ranges': 'bytes', 'etag': '"drop-v1"', 'last-modified': 'Wed, 23 Sep 2026 10:00:00 GMT', 'cache-control': 'no-store' };
    if (m) {
      const start = +m[1];
      headers['content-range'] = 'bytes ' + start + '-' + (size - 1) + '/' + size;
      headers['content-length'] = size - start;
      res.writeHead(206, headers);
      stream(res, buf, start, 800000, null);        // a resume is allowed to finish
    } else {
      headers['content-length'] = size;
      res.writeHead(200, headers);
      stream(res, buf, 0, 800000, at);              // the first attempt is cut short
    }
    return;
  }

  if (u.pathname === '/program') {
    const b = Buffer.concat([Buffer.from('MZ'), Buffer.alloc(510)]);
    res.writeHead(200, { 'content-type': 'application/x-msdownload', 'content-disposition': 'attachment; filename="setup.exe"', 'content-length': b.length });
    res.end(b);
    return;
  }

  if (u.pathname === '/inline.pdf') {
    res.writeHead(200, { 'content-type': 'text/html' });
    res.end('<!doctype html><title>inline</title><p>not a download');
    return;
  }

  res.writeHead(404).end();
}).listen(PORT, '127.0.0.1', () => console.log('dlsite on http://127.0.0.1:' + PORT));
