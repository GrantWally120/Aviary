// Does the per-tab DevTools override Emulation.setAutoDarkModeOverride darken a page on THIS engine,
// and does it last across navigations in that tab? (M6, 25 Sep - the engine-wide feature darkened
// Aviary's own pages too, and no page-level opt-out stopped it.)
//   CDP_PORT=9333 node autodark-probe.js
const PORT = process.env.CDP_PORT || 9222;
const sleep = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  const t = (await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json()).find(x => x.type === 'page');
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  let id = 0; const pending = new Map();
  ws.onmessage = ev => { const m = JSON.parse(ev.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } };
  await new Promise(r => ws.onopen = r);
  const send = (method, params = {}) => new Promise(r => { const my = ++id; pending.set(my, r); ws.send(JSON.stringify({ id: my, method, params })); });
  const pixel = async () => (await send('Page.captureScreenshot', { format: 'png', clip: { x: 300, y: 300, width: 2, height: 2, scale: 1 } })).result.data;
  const go = async p => { await send('Page.navigate', { url: 'http://127.0.0.1:8797' + p }); await sleep(1500); };
  const out = {};
  await go('/white'); out.before = await pixel();
  out.set = await send('Emulation.setAutoDarkModeOverride', { enabled: true }); await sleep(500);
  out.after = await pixel();
  await go('/scheme-light-dark'); out.afterNavigate = await pixel();
  out.off = await send('Emulation.setAutoDarkModeOverride', { enabled: false }); await sleep(500);
  out.offPixel = await pixel();
  out.clear = await send('Emulation.setAutoDarkModeOverride', {}); await sleep(500);
  out.cleared = await pixel();
  ws.close();
  console.log(JSON.stringify(out));
})();
