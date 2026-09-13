// Which origins make Storage.clearDataForOrigin wipe cookies of OTHER sites?
// Plants cookies for several sites with Network.setCookie, clears one origin, reports survivors.
const PORT = 9222;
async function page() {
  const r = await fetch(`http://127.0.0.1:${PORT}/json/list`);
  return (await r.json()).find(t => t.type === 'page');
}
function session(wsUrl) {
  const ws = new WebSocket(wsUrl);
  let id = 0; const pending = new Map();
  ws.onmessage = ev => { const m = JSON.parse(ev.data); if (m.id && pending.has(m.id)) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.reject(new Error(JSON.stringify(m.error))) : p.resolve(m.result); } };
  const open = new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  return { async send(method, params = {}) { await open; const my = ++id; ws.send(JSON.stringify({ id: my, method, params })); return new Promise((resolve, reject) => pending.set(my, { resolve, reject })); }, close() { ws.close(); } };
}
const PLANT = [
  ['example.com', 'https://example.com/'],
  ['www.example.com', 'https://www.example.com/'],
  ['other.org', 'https://other.org/'],
  ['foo.github.io', 'https://foo.github.io/'],
  ['127.0.0.2', 'http://127.0.0.2/'],
  ['127.0.0.1', 'http://127.0.0.1/'],
  ['localhost', 'http://localhost/'],
];
(async () => {
  const t = await page();
  const s = session(t.webSocketDebuggerUrl);
  const origins = process.argv.slice(2);
  const types = process.env.TYPES || 'all';
  for (const origin of origins) {
    await s.send('Network.clearBrowserCookies');
    for (const [host, url] of PLANT) {
      await s.send('Network.setCookie', { name: 'c_' + host.replace(/\W/g, '_'), value: '1', url, expires: Date.now() / 1000 + 86400 });
    }
    let err = '';
    try { await s.send('Storage.clearDataForOrigin', { origin, storageTypes: types }); } catch (e) { err = ' ERROR ' + e.message; }
    const left = (await s.send('Network.getAllCookies')).cookies.map(c => c.domain);
    const gone = PLANT.map(p => p[0]).filter(h => !left.includes(h));
    console.log(`${origin.padEnd(28)} [${types}] removed: ${gone.join(', ') || '(nothing)'}${gone.length === PLANT.length ? '   <-- WIPED EVERYTHING' : ''}${err}`);
  }
  s.close();
})().catch(e => { console.error('ERROR ' + e.message); process.exitCode = 1; });
