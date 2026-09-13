// Photographs every exhibit in the Menagerie for its placard: src/Aviary/Library/thumbs/<id>.jpg.
//
// Drives a TEST Aviary over DevTools (tools/test/launch-m2.ps1 starts one with port 9222), so the
// pictures are the pages exactly as Aviary renders them. Each is captured at 1280×800 and saved
// at half size. A page's own dialogs (alert, "leave this page?") are dismissed so none can stall
// the run. The thumbs are copies of Grant's own work, so they stay out of git with files/.
//
//   node tools/library/thumbs.js            every exhibit
//   node tools/library/thumbs.js w-242 ember   just these
//
// Phase 5, Alpha Build 5 M2.5 — HANDOFF section 16.
const fs = require('fs');
const path = require('path');

const PORT = process.env.CDP_PORT || 9222;
const LIB = path.join(__dirname, '..', '..', 'src', 'Aviary', 'Library');
const OUT = path.join(LIB, 'thumbs');
const ROOT = 'https://menagerie.aviary/';
const catalog = JSON.parse(fs.readFileSync(path.join(LIB, 'catalog.json'), 'utf8'));
const only = process.argv.slice(2);

function href(p) { return ROOT + p.split('/').map(encodeURIComponent).join('/'); }
const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const list = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
  const target = list.find(t => t.type === 'page');
  if (!target) { throw new Error('no page target — is a test Aviary running with --remote-debugging-port?'); }

  const ws = new WebSocket(target.webSocketDebuggerUrl);
  let id = 0; const pending = new Map();
  ws.onmessage = ev => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.reject(new Error(JSON.stringify(m.error))) : p.resolve(m.result); }
    else if (m.method === 'Page.javascriptDialogOpening') { send('Page.handleJavaScriptDialog', { accept: true }).catch(() => {}); }
  };
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  function send(method, params = {}) { const my = ++id; ws.send(JSON.stringify({ id: my, method, params })); return new Promise((resolve, reject) => pending.set(my, { resolve, reject })); }

  await send('Page.enable');
  await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });
  fs.mkdirSync(OUT, { recursive: true });

  let done = 0;
  try {
    for (const e of catalog.exhibits) {
      if (only.length && !only.includes(e.id)) { continue; }
      // Godot web builds need their engine to load and start; everything else is one file.
      const wait = /builds\/|build\/web\//.test(e.open) ? 11000 : e.kind === 'landing' ? 3500 : 2800;
      await send('Page.navigate', { url: href(e.open) });
      await sleep(wait);
      try { await send('Runtime.evaluate', { expression: 'window.scrollTo(0,0)' }); } catch (_) {}
      const shot = await send('Page.captureScreenshot', { format: 'jpeg', quality: 74, clip: { x: 0, y: 0, width: 1280, height: 800, scale: 0.5 } });
      fs.writeFileSync(path.join(OUT, e.id + '.jpg'), Buffer.from(shot.data, 'base64'));
      done++;
      console.log(`${String(done).padStart(2)}  ${e.id.padEnd(16)} ${e.title}`);
    }
  } finally {
    await send('Emulation.clearDeviceMetricsOverride').catch(() => {});
    await send('Page.navigate', { url: ROOT + 'index.html' }).catch(() => {});
    ws.close();
  }
  console.log(`thumbs: ${done} written to ${OUT}`);
})().catch(e => { console.error('ERROR ' + e.message); process.exitCode = 1; });
