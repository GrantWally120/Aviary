// Drive Aviary's WebView2 pages over the DevTools protocol, WITHOUT touching the desktop.
//
// Aviary is launched for testing with WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222
// (test instances only; never shipped). Every tab is a CDP "page" target.
//
//   node cdp.js list
//   node cdp.js eval  <match> "<js expression>"        -> JSON result (promises awaited)
//   node cdp.js evalfile <match> <file.js>             -> the same, with the expression read from a file
//   node cdp.js nav   <match> <url>                     -> Page.navigate (NOT Aviary's address bar)
//   node cdp.js click <match> "<css selector>"          -> a TRUSTED click (Input.dispatchMouseEvent) at the element's centre
//   node cdp.js press <match> "<css selector>"          -> the same, without scrolling it into view first (dropdown options)
//   node cdp.js wait  <match> "<js condition>" [ms]     -> polls until truthy
//   node cdp.js shot  <match> <out.png> [WxH] [light|dark] -> screenshot at WxH, no window touched
//
// <match> is a substring of the target's url or title, or "#<n>" for the n-th page target.
const PORT = process.env.CDP_PORT || 9222;

async function targets() {
  const r = await fetch(`http://127.0.0.1:${PORT}/json/list`);
  return (await r.json()).filter(t => t.type === 'page' || t.type === 'iframe');
}

async function pick(match) {
  const all = await targets();
  const pages = all.filter(t => t.type === 'page');
  if (match.startsWith('#')) {
    const t = pages[parseInt(match.slice(1), 10)];
    if (!t) throw new Error('no target ' + match);
    return t;
  }
  const hit = pages.find(t => t.id.startsWith(match))
           || pages.find(t => (t.url || '').includes(match) || (t.title || '').includes(match));
  if (!hit) throw new Error('no page matching ' + JSON.stringify(match) + '\n' + pages.map(p => `  ${p.title} | ${p.url.slice(0, 100)}`).join('\n'));
  return hit;
}

function session(wsUrl) {
  const ws = new WebSocket(wsUrl);
  let id = 0;
  const pending = new Map();
  ws.onmessage = ev => {
    const msg = JSON.parse(ev.data);
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      msg.error ? reject(new Error(JSON.stringify(msg.error))) : resolve(msg.result);
    }
  };
  const open = new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  return {
    async send(method, params = {}) {
      await open;
      const my = ++id;
      ws.send(JSON.stringify({ id: my, method, params }));
      return new Promise((resolve, reject) => pending.set(my, { resolve, reject }));
    },
    close() { try { ws.close(); } catch (e) {} },
  };
}

async function evaluate(s, expression) {
  const r = await s.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true, userGesture: true });
  if (r.exceptionDetails) return { exception: r.exceptionDetails.exception?.description || r.exceptionDetails.text };
  return r.result.value;
}

(async () => {
  const [cmd, match, arg, extra] = process.argv.slice(2);
  if (cmd === 'list') {
    for (const t of await targets()) console.log(`${t.type.padEnd(6)} ${t.id.slice(0, 8)}  ${(t.title || '').slice(0, 40).padEnd(40)}  ${(t.url || '').slice(0, 110)}`);
    return;
  }

  const t = await pick(match);
  const s = session(t.webSocketDebuggerUrl);
  try {
    if (cmd === 'eval') {
      console.log(JSON.stringify(await evaluate(s, arg), null, 1));
    } else if (cmd === 'evalfile') {
      // The expression comes from a FILE: Windows caps a command line at ~32 KB, and some
      // things worth measuring (the element-hiding script) are megabytes (M4.5).
      console.log(JSON.stringify(await evaluate(s, require('fs').readFileSync(arg, 'utf8')), null, 1));
    } else if (cmd === 'nav') {
      console.log(JSON.stringify(await s.send('Page.navigate', { url: arg })));
    } else if (cmd === 'click' || cmd === 'press') {
      // press = click WITHOUT scrolling first: Settings closes an open dropdown on any scroll, so
      // an option must be pressed where it already is (M4.5, 19 Sep 2026).
      const scroll = cmd === 'click' ? "e.scrollIntoView({block:'center'});" : "";
      const rect = await evaluate(s, `(function(){var e=document.querySelector(${JSON.stringify(arg)}); if(!e) return null; ${scroll} var r=e.getBoundingClientRect(); return {x:r.left+r.width/2, y:r.top+r.height/2, w:r.width, h:r.height};})()`);
      if (!rect) { console.log('NO ELEMENT ' + arg); process.exitCode = 1; return; }
      for (const type of ['mouseMoved', 'mousePressed', 'mouseReleased']) {
        await s.send('Input.dispatchMouseEvent', { type, x: rect.x, y: rect.y, button: 'left', clickCount: 1 });
      }
      console.log('clicked ' + arg + ' at ' + Math.round(rect.x) + ',' + Math.round(rect.y));
    } else if (cmd === 'type') {
      // node cdp.js type <match> "<selector>" "<text>" — focus, then TRUSTED text input
      await evaluate(s, `(function(){var e=document.querySelector(${JSON.stringify(arg)}); if(e){e.focus();} return !!e;})()`);
      await s.send('Input.insertText', { text: extra || '' });
      console.log('typed into ' + arg);
    } else if (cmd === 'call') {
      // node cdp.js call <tab> <Domain.method> '<json params>' — any DevTools method, raw result
      console.log(JSON.stringify(await s.send(arg, extra ? JSON.parse(extra) : {}), null, 1));
    } else if (cmd === 'key') {
      // node cdp.js key <match> "ctrl+shift+KeyN" — a TRUSTED key press delivered into the page
      const parts = arg.split('+');
      const code = parts.pop();
      const mods = (parts.includes('alt') ? 1 : 0) | (parts.includes('ctrl') ? 2 : 0) | (parts.includes('shift') ? 8 : 0);
      const SPECIAL = { Comma: [',', 188], F11: ['F11', 122], Escape: ['Escape', 27], Tab: ['Tab', 9], F7: ['F7', 118] };
      const letter = code.replace(/^Key/, '');
      const key = SPECIAL[code] ? SPECIAL[code][0] : (parts.includes('shift') ? letter.toUpperCase() : letter.toLowerCase());
      const vk = SPECIAL[code] ? SPECIAL[code][1] : (letter.length === 1 ? letter.toUpperCase().charCodeAt(0) : 0);
      for (const type of ['rawKeyDown', 'keyUp']) {
        await s.send('Input.dispatchKeyEvent', { type, modifiers: mods, key, code, windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk });
      }
      console.log('pressed ' + arg);
    } else if (cmd === 'shot') {
      // node cdp.js shot <match> <out.png> [WxH] [light|dark] — the page alone, rendered at WxH
      // (default 1180x860) through device-metrics emulation: no window is moved or fronted.
      const [w, h] = (extra || '1180x860').split('x').map(Number);
      const scheme = process.argv[6];
      // The override is undone IN THIS SESSION before it closes: in WebView2 152 a size override
      // outlives the session that set it, and a later session's clear does not reach it — the
      // tab stays boxed at WxH until Aviary restarts (seen 13 Sep).
      await s.send('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 1, mobile: false });
      if (scheme) await s.send('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-color-scheme', value: scheme }] });
      try {
        await new Promise(r => setTimeout(r, 350));
        const shot = await s.send('Page.captureScreenshot', { format: 'png' });
        require('fs').writeFileSync(arg, Buffer.from(shot.data, 'base64'));
        console.log('saved ' + arg + ' (' + w + 'x' + h + (scheme ? ', ' + scheme : '') + ')');
      } finally {
        await s.send('Emulation.clearDeviceMetricsOverride');
        await s.send('Emulation.setEmulatedMedia', { features: [] });
      }
    } else if (cmd === 'wait') {
      const until = Date.now() + (parseInt(extra, 10) || 8000);
      let v;
      while (Date.now() < until) {
        v = await evaluate(s, arg);
        if (v && !v.exception) break;
        await new Promise(r => setTimeout(r, 250));
      }
      console.log(JSON.stringify(v));
    } else {
      console.log('unknown command ' + cmd);
    }
  } finally {
    s.close();
  }
})().catch(e => { console.error('ERROR ' + e.message); process.exitCode = 1; });
