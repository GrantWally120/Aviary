// M3's test site: the ad blocker against REAL EasyList/EasyPrivacy rules, offline.
//
// Launch the test instance with every host name mapped to this server:
//   launch-m2.ps1 -ExtraArgs '--host-resolver-rules="MAP * 127.0.0.1:8771, EXCLUDE localhost"'
// (test instances only — AVIARY_TEST_BROWSER_ARGS). Every host AND port lands here, so the pages
// use real, port-less addresses: a rule like ||facebook.com/tr? cannot match "facebook.com:8771/tr?".
// http://news.test/ loads "ads" from pagead2.googlesyndication.com, www.google-analytics.com,
// securepubads.g.doubleclick.net… all answered here, so a request that shows up in /hits is one
// the blocker let through.
// Seed the profile with {"httpsOnly":"false"} — these are plain-http names that are not local.
//
// Pages (port-less; hosts on the HSTS preload list, like gmail.com, are https-only and unusable):
//   news.test/            third-party ads and trackers, a first-party ad image, generic
//                         element hiding (.ad-slot, .sponsored-post, [id^="div-gpt-ad"]), frames
//   yahoo.com/            a site's OWN hiding rule: yahoo.com###Horizon-ad
//   techymedies.com/      an exception: techymedies.com#@##ad-top (#ad_top is still hidden)
//   aetv.com/             @@||aetv.com^$generichide — generic hiding off, network blocking on
//   alpine-vpn.com/       ||alpine-vpn.com^$document — Strict's warning
//   ad.doubleclick.net/   a plain ||host^ rule — Strict's warning, Standard opens it
//   /lists/…              copies of the shipped lists, re-dated a day later, for "Update now":
//                         AVIARY_TEST_FILTER_BASE=http://127.0.0.1:8771/lists/ (the update uses .NET's
//                         HttpClient, which never sees Chromium's host mapping — so an IP, not a name)
//   any host /hits        every request this server saw; /reset clears it
const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = 8771;
const hits = [];
const GIF = Buffer.from('R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7', 'base64');
const FILTERS = path.join(__dirname, '..', '..', 'src', 'Aviary', 'Filters');

const probe = `
<script>
window.__frames = {};
addEventListener('message', function (e) { if (e.data && e.data.frame) { window.__frames[e.data.frame] = e.data; } });
window.__probe = function () {
  var out = { ran: Object.keys(window.__ran || {}).sort(), els: {}, sheets: document.adoptedStyleSheets.length, frames: window.__frames };
  document.querySelectorAll('[data-t]').forEach(function (el) { out.els[el.getAttribute('data-t')] = getComputedStyle(el).display; });
  return JSON.stringify(out);
};
</script>`;

const frameBody = (name) => `<!doctype html><title>${name}</title>
<div class="ad-slot" id="fad">frame ad</div><p id="fok">frame content</p>
<script>
setTimeout(function () {
  parent.postMessage({ frame: ${JSON.stringify(name)}, ad: getComputedStyle(document.getElementById('fad')).display,
                       ok: getComputedStyle(document.getElementById('fok')).display, sheets: document.adoptedStyleSheets.length }, '*');
}, 300);
</script>`;

function page(host) {
  switch (host) {
    case 'news.test':
      return `<!doctype html><title>News Test</title>
<h1>Today's news</h1>
<p class="content" data-t="content">The real article text, which must stay.</p>
<div class="ad-slot" data-t="ad-slot">AD SLOT (generic rule ##.ad-slot)</div>
<div class="sponsored-post" data-t="sponsored-post">SPONSORED (##.sponsored-post)</div>
<div id="div-gpt-ad-1234-0" data-t="gpt">GPT SLOT (##[id^="div-gpt-ad"])</div>
<div id="ad-top" data-t="ad-top">#ad-top (hidden here)</div>
<script src="http://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=ca-pub-1"></script>
<script src="http://www.google-analytics.com/analytics.js"></script>
<script src="http://securepubads.g.doubleclick.net/tag/js/gpt.js"></script>
<script src="http://www.googletagmanager.com/gtm.js?id=GTM-TEST"></script>
<img src="http://www.facebook.com/tr?id=1&ev=PageView" width="1" height="1" data-t="pixel">
<script src="http://static.newscdn.test/npm/somelib@1/dist/lib.js"></script>
<script src="/app.js"></script>
<img src="/ads/banners/top.png" data-t="first-party-ad-img">
<iframe src="http://ad.doubleclick.net/ddm/adi/N1/B2" width="300" height="50" data-t="ad-frame"></iframe>
<iframe src="/frame.html" width="300" height="60"></iframe>
<iframe src="http://widgets.test/frame.html" width="300" height="60"></iframe>
<script>
  // A page that replaces the whole adopted list must not throw Aviary's sheet out.
  setTimeout(function () { document.adoptedStyleSheets = []; window.__replaced = true; }, 200);
</script>
${probe}`;
    case 'yahoo.com':
      return `<!doctype html><title>Yahoo Test</title>
<div id="Horizon-ad" data-t="horizon">yahoo.com###Horizon-ad</div>
<div class="ad-slot" data-t="ad-slot">generic</div>
<p data-t="content">content</p>${probe}`;
    case 'techymedies.com':
      return `<!doctype html><title>Exception Test</title>
<div id="ad-top" data-t="ad-top">techymedies.com#@##ad-top keeps this</div>
<div id="ad_top" data-t="ad_top">###ad_top still hides this</div>
<p data-t="content">content</p>${probe}`;
    // gmail.com is on Chromium's HSTS preload list, so plain http never reaches this server;
    // aetv.com carries the same kind of rule (@@||aetv.com^$generichide) and is not.
    case 'gmail.com':
    case 'aetv.com':
      return `<!doctype html><title>Generichide Test</title>
<div class="ad-slot" data-t="ad-slot">generic hiding is off here</div>
<script src="http://www.google-analytics.com/analytics.js"></script>
<p data-t="content">content</p>${probe}`;
    case 'alpine-vpn.com':
      return `<!doctype html><title>Alpine VPN</title><h1>A site on EasyList with $document</h1>${probe}`;
    case 'ad.doubleclick.net':
      return `<!doctype html><title>DoubleClick</title><h1>A plain ||host^ rule</h1>${probe}`;
    default:
      return `<!doctype html><title>${host}</title><p data-t="content">${host}</p>${probe}`;
  }
}

function listCopy(file) {
  // The shipped list, "modified" a day later — so the update path has something newer to take.
  let text = fs.readFileSync(path.join(FILTERS, file), 'utf8');
  const tomorrow = new Date(Date.now() + 86400000);
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const stamp = tomorrow.getUTCDate() + ' ' + months[tomorrow.getUTCMonth()] + ' ' + tomorrow.getUTCFullYear() + ' 00:00 UTC';
  text = text.replace(/^! Last modified: .*$/m, '! Last modified: ' + stamp)
             .replace(/^! Version: .*$/m, '! Version: 299912310000');
  return text;
}

http.createServer((req, res) => {
  const host = (req.headers.host || '').replace(/:\d+$/, '').toLowerCase();
  const url = new URL(req.url, 'http://' + (req.headers.host || 'x'));
  const p = url.pathname;

  if (p === '/hits') { res.writeHead(200, { 'content-type': 'application/json' }); return res.end(JSON.stringify(hits)); }
  if (p === '/reset') { hits.length = 0; res.writeHead(200); return res.end('ok'); }

  hits.push({ host, path: p + url.search, referer: req.headers.referer || null, at: Date.now() });

  if (p.startsWith('/lists/')) {
    const file = path.basename(p);
    if (!/^easy(list|privacy)\.txt$/.test(file)) { res.writeHead(404); return res.end(); }
    res.writeHead(200, { 'content-type': 'text/plain; charset=utf-8' });
    return res.end(listCopy(file));
  }

  if (p.endsWith('.js')) {
    res.writeHead(200, { 'content-type': 'text/javascript', 'cache-control': 'no-store' });
    return res.end(`(window.__ran = window.__ran || {})[${JSON.stringify(host + p)}] = 1;`);
  }
  if (/\.(png|gif|jpg)$/.test(p) || p.startsWith('/tr')) {
    res.writeHead(200, { 'content-type': 'image/gif', 'cache-control': 'no-store' });
    return res.end(GIF);
  }
  if (p === '/frame.html' || p.startsWith('/ddm/')) {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    return res.end(frameBody(host + p));
  }
  if (p === '/ping') { res.writeHead(204); return res.end(); }

  res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
  res.end(page(host));
}).listen(PORT, '127.0.0.1', () => console.log('m3site on http://127.0.0.1:' + PORT));
