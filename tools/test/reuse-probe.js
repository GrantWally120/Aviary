// Preload on hover (M6 stage 2): is a page fetched by <link rel=prefetch as=document> USED when the
// link is then followed - or fetched again? For each cache rule real sites give their HTML, it
// prefetches expsite.js's 700 ms /slow page, then navigates to it, and reports how many times the
// server was asked and how long the navigation waited for its first byte.
//   CDP_PORT=9333 node reuse-probe.js
const PORT = process.env.CDP_PORT || 9222;
const BASE = 'http://127.0.0.1:8797';
const sleep = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  const t = (await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json()).find(x => x.type === 'page');
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  let id = 0; const pending = new Map();
  ws.onmessage = ev => { const m = JSON.parse(ev.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } };
  await new Promise(r => ws.onopen = r);
  const send = (method, params = {}) => new Promise(r => { const my = ++id; pending.set(my, r); ws.send(JSON.stringify({ id: my, method, params })); });
  const ev = async e => (await send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise: true })).result?.result?.value;
  const hits = async () => (await (await fetch(BASE + '/hits')).json());
  const out = {};
  for (const cc of ['no-store', 'no-cache', 'private, max-age=0', 'max-age=300']) {
    for (const pre of [false, true]) {
      const n = (pre ? 'pre-' : 'plain-') + cc.replace(/\W+/g, '');
      const url = BASE + '/slow?n=' + n + '&cc=' + encodeURIComponent(cc);
      await send('Page.navigate', { url: BASE + '/hover' }); await sleep(1000);
      const from = (await hits()).length;
      if (pre) {
        await ev(`(function(){var l=document.createElement('link');l.rel='prefetch';l.as='document';l.href=${JSON.stringify(url)};document.head.appendChild(l);return true;})()`);
        await sleep(1500);
      }
      await send('Page.navigate', { url }); await sleep(1500);
      const nav = JSON.parse(await ev("JSON.stringify((function(e){return {wait: Math.round(e.responseStart - e.requestStart), transfer: e.transferSize, delivery: e.deliveryType}})(performance.getEntriesByType('navigation')[0]))"));
      const asked = (await hits()).slice(from).filter(h => h.url.startsWith('/slow?n=' + n)).length;
      out[n] = { serverAsked: asked, ...nav };
    }
  }
  ws.close();
  console.log(JSON.stringify(out, null, 1));
})();
