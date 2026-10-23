// usage: node keys.js <port> <target: #n | substring of url/title> <combo> [more combos...]
// Sends TRUSTED key events into one page target over CDP (no desktop input). combo e.g. ctrl+t, alt+left, f5, ctrl+shift+t
const [port, match, ...combos] = process.argv.slice(2);
const K = {
  t: ['t', 'KeyT', 84], w: ['w', 'KeyW', 87], n: ['n', 'KeyN', 78], p: ['p', 'KeyP', 80], g: ['g', 'KeyG', 71], l: ['l', 'KeyL', 76],
  f: ['f', 'KeyF', 70], f3: ['F3', 'F3', 114], f5: ['F5', 'F5', 116], left: ['ArrowLeft', 'ArrowLeft', 37], right: ['ArrowRight', 'ArrowRight', 39],
  escape: ['Escape', 'Escape', 27],
  ...Object.fromEntries([...'0123456789'].map(d => [d, [d, 'Digit' + d, 48 + Number(d)]])),
};
(async () => {
  const pages = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).filter(t => t.type === 'page');
  const t = match.startsWith('#') ? pages[+match.slice(1)] : pages.find(p => (p.url || '').includes(match) || (p.title || '').includes(match));
  if (!t) { console.error('no target', match, pages.map(p => p.title + ' | ' + p.url.slice(0, 60))); process.exit(2); }
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  await new Promise(r => ws.onopen = r);
  let n = 0; const pend = {};
  ws.onmessage = e => { const m = JSON.parse(e.data); if (pend[m.id]) pend[m.id](m); };
  const send = (method, params = {}) => new Promise(r => { const id = ++n; pend[id] = r; ws.send(JSON.stringify({ id, method, params })); });
  await send('Page.bringToFront').catch(() => {});
  for (const combo of combos) {
    const parts = combo.toLowerCase().split('+'); const k = parts.pop(); const def = K[k];
    if (!def) throw new Error('unknown key ' + k);
    const mods = (parts.includes('alt') ? 1 : 0) | (parts.includes('ctrl') ? 2 : 0) | (parts.includes('shift') ? 8 : 0);
    let key = def[0]; if (parts.includes('shift') && key.length === 1) key = key.toUpperCase();
    const base = { key, code: def[1], windowsVirtualKeyCode: def[2], nativeVirtualKeyCode: def[2], modifiers: mods };
    await send('Input.dispatchKeyEvent', { type: 'rawKeyDown', ...base });
    await send('Input.dispatchKeyEvent', { type: 'keyUp', ...base });
    console.log('sent', combo, '->', t.title.slice(0, 30));
    await new Promise(r => setTimeout(r, 400));
  }
  ws.close();
})().catch(e => { console.error('ERR', e.message); process.exit(1); });
