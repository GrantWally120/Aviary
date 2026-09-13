// Aviary M1 test site. Every secret here is a unique marker string; after browsing, the
// diagnostics folder is grepped for each one and must contain ZERO of them.
const http = require('http');
const url = require('url');

const PORT = 8765;

// A JWT-shaped bearer token (header.payload.signature, base64url). Not a real credential.
const JWT = 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJKV1RTRUNSRVRQQVlMT0FEIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c';

const page = (title, body) => `<!doctype html><html><head><meta charset="utf-8"><title>${title}</title>
<style>body{font:16px system-ui;margin:40px;max-width:760px} input{font:inherit;padding:6px;margin:4px 0;display:block}</style>
</head><body>${body}</body></html>`;

const routes = {
  '/': () => page('Aviary test site', `<h1>Aviary M1 test site</h1>
    <ul>
      <li><a href="/errors">/errors — script errors, a failed image, a fetch, trackers</a></li>
      <li><a href="/login">/login — a sign-in form (GET, so the password rides in the URL)</a></li>
      <li><a href="/oauth">/oauth — redirect to a callback carrying a code and a token</a></li>
      <li><a href="/reset-password/7f3a9c2b8e1d4f6a9c0bRESETPATHSECRET">/reset-password/… — a credential in the PATH</a></li>
      <li><a href="/slow">/slow — 1.5 s to first byte</a></li>
      <li><a href="/status/404">/status/404</a> · <a href="/status/500">/status/500</a></li>
    </ul>`),

  '/errors': () => page('Errors test page', `<h1>Errors</h1>
    <label>Something you typed <input id="typed" value=""></label>
    <label>Password <input id="pw" type="password" value=""></label>
    <img src="/missing.png?token=IMGTOKENSECRET&size=large" alt="broken on purpose">
    <script src="https://www.google-analytics.com/analytics.js"></script>
    <img src="https://ad.doubleclick.net/pixel?id=TRACKERPIXEL" width="1" height="1">
    <script>
      // "Typed" values: the page-error script must cut them out of anything it reports.
      document.getElementById('typed').value = 'FORMSECRET-typed-7Q';
      document.getElementById('pw').value = 'PASSWORDFIELDSECRET';
      document.cookie = 'sid=COOKIESECRETVALUE; path=/';
      fetch('/api?access_token=FETCHTOKENSECRET&page=2').catch(function(){});
      setTimeout(function(){
        Promise.reject(new Error('rejected with bearer Bearer ' + '${JWT}'));
        throw new Error('Invalid value: ' + document.getElementById('typed').value
          + ' / ' + document.getElementById('pw').value + ' / card 4111 1111 1111 1111');
      }, 300);
    </script>`),

  '/login': () => page('Sign in', `<h1>Sign in</h1>
    <form action="/welcome" method="get">
      <input name="username" id="username" placeholder="username">
      <input name="password" id="password" type="password" placeholder="password">
      <button id="go">Sign in</button>
    </form>`),

  '/welcome': (q) => page('Welcome', `<h1>Welcome ${String(q.username || '').replace(/[<>&]/g, '')}</h1><p>Signed in (not really).</p>`),

  '/callback': () => page('Callback', `<h1>OAuth callback</h1><p>Got a code.</p>`),

  // A hostile page: pretends to be Aviary's Settings and Start Page and asks the host to
  // switch the blocker off, switch diagnostics on, and open a local file.
  '/spoof': () => page('Spoof', `<h1>Spoof</h1><script>
    var send = function(o){ try { window.chrome.webview.postMessage(JSON.stringify(o)); } catch(e){} };
    send({type:'setting', key:'blockTrackers', value:false});
    send({type:'setting', key:'improvementCollection', value:true});
    send({type:'command', name:'clearFavicons'});
    send({type:'navigate', url:'file:///C:/Windows/win.ini'});
    send({type:'startSection', key:'favourites', value:false});
    document.title = 'Spoof sent';
  </script>`),

  '/download': () => page('Download', `<h1>Download</h1><a id="dl" href="/file.txt">get file</a>`),
  '/api': () => JSON.stringify({ ok: true }),
};

http.createServer((req, res) => {
  const u = url.parse(req.url, true);

  // Scratch files (the extracted Settings page), for the browser pane.
  if (u.pathname.startsWith('/_file/')) {
    const name = require('path').basename(u.pathname);
    try {
      const body = require('fs').readFileSync(require('path').join(__dirname, name));
      res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
      return res.end(body);
    } catch (e) {
      res.writeHead(404); return res.end('no such file');
    }
  }

  if (u.pathname === '/oauth') {
    res.writeHead(302, { Location: '/callback?code=OAUTHCODESECRET&state=xyz#access_token=FRAGTOKENSECRET&token_type=bearer' });
    return res.end();
  }

  if (u.pathname.startsWith('/status/')) {
    const code = parseInt(u.pathname.split('/')[2], 10) || 500;
    res.writeHead(code, { 'Content-Type': 'text/html' });
    return res.end(page('Status ' + code, `<h1>${code}</h1>`));
  }

  if (u.pathname === '/file.txt') {
    res.writeHead(200, { 'Content-Type': 'text/plain', 'Content-Disposition': 'attachment; filename="aviary-m1-download-test.txt"' });
    return res.end('Aviary M1 download test. Safe to delete.\n');
  }

  if (u.pathname.startsWith('/reset-password/')) {
    res.writeHead(200, { 'Content-Type': 'text/html' });
    return res.end(page('Reset your password', '<h1>Choose a new password</h1>'));
  }

  const route = routes[u.pathname];
  const send = () => {
    if (!route) {
      res.writeHead(404, { 'Content-Type': 'text/plain' });
      return res.end('not found');
    }

    const body = route(u.query);
    const isJson = u.pathname === '/api';
    res.writeHead(200, {
      'Content-Type': isJson ? 'application/json' : 'text/html; charset=utf-8',
      // A cookie the SERVER sets — Set-Cookie must never reach the folder either.
      'Set-Cookie': 'session=SETCOOKIESECRET; Path=/; HttpOnly',
    });
    res.end(body);
  };

  if (u.pathname === '/slow') {
    setTimeout(() => { res.writeHead(200, { 'Content-Type': 'text/html' }); res.end(page('Slow', '<h1>Slow page</h1>')); }, 1500);
    return;
  }

  send();
}).listen(PORT, '127.0.0.1', () => console.log('test site on http://localhost:' + PORT));
