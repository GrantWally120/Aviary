// Bird News' offline test server (M4.9). Every shipped feed is pointed here with
// AVIARY_TEST_NEWS_BASE=http://127.0.0.1:8790, so the whole feature is tested without touching a
// real news site. Each feed exercises one thing:
//
//   /bbc.xml        good RSS 2.0 - and hostile content: a headline carrying HTML, a javascript:
//                   link (must be dropped), entities to decode, dates in GMT / +0800 / PHT
//   /aljazeera.xml  good Atom
//   /guardian.xml   ETag "v1": a 200 the first time, a 304 when asked with If-None-Match
//   /inquirer.xml   a "billion laughs" entity bomb - must fail fast and cheaply, never expand
//   /rappler.xml    403, the way a site refusing an unknown reader answers
//   /gma.xml        answers after 20 s - past Bird News' 15 s timeout
//   /broken.xml     not XML at all (for "Your own feeds")
//   /hits           every request seen, with the headers that matter (UA, Cookie, If-None-Match)
//
//   node newssite.js
const http = require('http');
const PORT = 8790;
const hits = [];

const now = Date.now();
const rfc = (minsAgo, zone) => {
  const d = new Date(now - minsAgo * 60000);
  const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
  const mons = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const p = n => String(n).padStart(2, '0');
  if (zone === '+0800' || zone === 'PHT') {
    const l = new Date(d.getTime() + 8 * 3600000);
    return `${days[l.getUTCDay()]}, ${p(l.getUTCDate())} ${mons[l.getUTCMonth()]} ${l.getUTCFullYear()} ${p(l.getUTCHours())}:${p(l.getUTCMinutes())}:${p(l.getUTCSeconds())} ${zone}`;
  }
  return `${days[d.getUTCDay()]}, ${p(d.getUTCDate())} ${mons[d.getUTCMonth()]} ${d.getUTCFullYear()} ${p(d.getUTCHours())}:${p(d.getUTCMinutes())}:${p(d.getUTCSeconds())} GMT`;
};

const feeds = {
  '/bbc.xml': () => `<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0"><channel><title>BBC News - World</title>
<item><title>World leaders meet in Geneva</title><link>https://news.test/bbc/1</link><pubDate>${rfc(5, 'GMT')}</pubDate></item>
<item><title><![CDATA[<img src=x onerror=alert(1)>HOSTILE headline with markup]]></title><link>https://news.test/bbc/2</link><pubDate>${rfc(12, '+0800')}</pubDate></item>
<item><title>Rice &amp; fish prices &#8220;steady&#8221;</title><link>https://news.test/bbc/3</link><pubDate>${rfc(30, 'PHT')}</pubDate></item>
<item><title>This one must never appear</title><link>javascript:alert(document.cookie)</link><pubDate>${rfc(1, 'GMT')}</pubDate></item>
<item><title>A story with no date</title><link>https://news.test/bbc/5</link></item>
</channel></rss>`,

  '/aljazeera.xml': () => `<?xml version="1.0" encoding="utf-8"?>
<feed xmlns="http://www.w3.org/2005/Atom"><title>Al Jazeera</title>
<entry><title>Ceasefire talks resume</title><link rel="alternate" href="https://news.test/aj/1"/><link rel="self" href="https://news.test/self"/><updated>${new Date(now - 8 * 60000).toISOString()}</updated></entry>
<entry><title>Markets open higher</title><link href="https://news.test/aj/2"/><published>${new Date(now - 40 * 60000).toISOString()}</published></entry>
</feed>`,

  '/guardian.xml': () => `<?xml version="1.0"?><rss version="2.0"><channel><title>The Guardian</title>
<item><title>Guardian story one</title><link>https://news.test/g/1</link><pubDate>${rfc(15, 'GMT')}</pubDate></item>
</channel></rss>`,

  '/inquirer.xml': () => `<?xml version="1.0"?>
<!DOCTYPE lolz [<!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
<!ENTITY lol3 "&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;"><!ENTITY lol4 "&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;">
<!ENTITY lol5 "&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;"><!ENTITY lol6 "&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;">]>
<rss version="2.0"><channel><item><title>&lol6;</title><link>https://news.test/inq/1</link></item></channel></rss>`,

  '/broken.xml': () => `this is <not really xml at all`,

  // Inquirer's real mistake (21 Sep): Manila local time labelled +0000, while the feed's OWN
  // lastBuildDate is honest (+0800). Read as written these are ~8 hours in the future.
  '/mislabel-ph.xml': () => {
    const local = minsAgo => rfc(minsAgo, '+0800').replace('+0800', '+0000');
    return `<?xml version="1.0"?><rss version="2.0"><channel><title>Mislabelled PH</title>
<lastBuildDate>${rfc(2, '+0800')}</lastBuildDate>
<item><title>Mislabelled story, really 20 min old</title><link>https://news.test/ml/1</link><pubDate>${local(20)}</pubDate></item>
<item><title>Mislabelled story, really 50 min old</title><link>https://news.test/ml/2</link><pubDate>${local(50)}</pubDate></item>
</channel></rss>`;
  },

  // The same mistake with nothing to correct it from: no build date, and not a Philippine feed.
  // Its times must be dropped, never shown as "just now".
  '/mislabel-noclue.xml': () => {
    const future = minsAhead => rfc(-minsAhead, 'GMT');
    return `<?xml version="1.0"?><rss version="2.0"><channel><title>No clue</title>
<item><title>Future-dated story with no way to fix it</title><link>https://news.test/nc/1</link><pubDate>${future(300)}</pubDate></item>
</channel></rss>`;
  },
};

http.createServer((req, res) => {
  const url = req.url.split('?')[0];
  hits.push({ t: Date.now(), url, ua: req.headers['user-agent'] || '', cookie: req.headers['cookie'] || '',
              inm: req.headers['if-none-match'] || '', ims: req.headers['if-modified-since'] || '' });

  if (url === '/hits') { res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify(hits, null, 1)); return; }
  if (url === '/rappler.xml') { res.writeHead(403); res.end('Forbidden'); return; }
  if (url === '/gma.xml') { setTimeout(() => { res.writeHead(200, { 'content-type': 'application/rss+xml' }); res.end(feeds['/bbc.xml']()); }, 20000); return; }

  if (url === '/guardian.xml' && req.headers['if-none-match'] === '"v1"') { res.writeHead(304); res.end(); return; }

  const make = feeds[url];
  if (!make) { res.writeHead(404); res.end('no'); return; }
  const headers = { 'content-type': 'application/rss+xml; charset=utf-8', 'set-cookie': 'tracker=should-never-come-back' };
  if (url === '/guardian.xml') { headers['etag'] = '"v1"'; }
  res.writeHead(200, headers);
  res.end(make());
}).listen(PORT, '127.0.0.1', () => console.log('newssite on http://127.0.0.1:' + PORT));
