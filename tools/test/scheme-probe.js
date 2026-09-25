// Which colour-scheme declarations "Make every website dark" (WebContentsForceDark) leaves alone,
// on THIS engine (M6, 25 Sep). Screenshots a pixel of each expsite.js /scheme-* page in the first
// tab of a test instance. Run once with the experiment on and once without (the control).
//   CDP_PORT=9333 node scheme-probe.js
const PORT = process.env.CDP_PORT || 9222;
const sleep = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  const t = (await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json()).find(x => x.type === 'page');
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  let id = 0; const pending = new Map();
  ws.onmessage = ev => { const m = JSON.parse(ev.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m.result); pending.delete(m.id); } };
  await new Promise(r => ws.onopen = r);
  const send = (method, params = {}) => new Promise(r => { const my = ++id; pending.set(my, r); ws.send(JSON.stringify({ id: my, method, params })); });
  const out = {};
  for (const p of ['/scheme-light-dark', '/scheme-only-light', '/scheme-only-light-meta', '/white']) {
    await send('Page.navigate', { url: 'http://127.0.0.1:8797' + p }); await sleep(1500);
    const shot = await send('Page.captureScreenshot', { format: 'png', clip: { x: 300, y: 300, width: 2, height: 2, scale: 1 } });
    const dark = (await send('Runtime.evaluate', { expression: "matchMedia('(prefers-color-scheme: dark)').matches", returnByValue: true })).result.value;
    out[p] = { png: shot.data, prefersDark: dark };
  }
  ws.close();
  console.log(JSON.stringify(out));
})();
