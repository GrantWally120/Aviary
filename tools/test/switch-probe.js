// What an engine switch really does on THIS WebView2 build (Alpha Build 5 M6), measured from
// inside a page over DevTools - no desktop input. Run against a test instance that was launched
// with (or without - the control) the switch under test:
//
//   CDP_PORT=9333 node switch-probe.js [fps|scroll|dark|video|all]
//
// Needs expsite.js on 8797. Prints one JSON object:
//   fps     animation frames a second on /raf (the display is 240 Hz on Grant's machine)
//   scroll  after ONE trusted wheel notch on /long: how many different positions the page passed
//           through in 700 ms (1-2 = it jumped; many = it glided), and where it ended
//   dark    the colour a screenshot of /white shows where the page has no colours of its own
//   video   what the engine says about decoding 1080p H.264 and VP9: supported, smooth, powerEfficient
//           (powerEfficient is true only when the graphics card decodes it)
const PORT = process.env.CDP_PORT || 9222;
const what = process.argv[2] || 'all';
const BASE = 'http://127.0.0.1:8797';

function session(wsUrl) {
  const ws = new WebSocket(wsUrl);
  let id = 0;
  const pending = new Map();
  ws.onmessage = ev => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) { const p = pending.get(m.id); pending.delete(m.id); m.error ? p.reject(new Error(JSON.stringify(m.error))) : p.resolve(m.result); }
  };
  const open = new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  return {
    async send(method, params = {}) { await open; const my = ++id; ws.send(JSON.stringify({ id: my, method, params })); return new Promise((resolve, reject) => pending.set(my, { resolve, reject })); },
    close() { try { ws.close(); } catch (e) {} },
  };
}
const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const list = await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json();
  const t = list.find(x => x.type === 'page');
  const s = session(t.webSocketDebuggerUrl);
  const ev = async expr => (await s.send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true })).result.value;
  const go = async path => { await s.send('Page.navigate', { url: BASE + path }); await sleep(1500); };
  const out = {};
  try {
    if (what === 'fps' || what === 'all') {
      await go('/raf'); await sleep(2300);
      out.fps = await ev('window.__fps');
    }
    if (what === 'scroll' || what === 'all') {
      // A DevTools wheel event is never animated (measured 25 Sep: the control jumped in one step
      // too), so the scroll is started by a trusted Page Down, which Chromium animates when smooth
      // scrolling is on. The page is clicked first so it has focus for the key.
      await go('/long');
      const summary = a => a ? { distinct: new Set(a).size, frames: a.length, end: a[a.length - 1], first: a.slice(0, 8) } : null;
      await s.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: 300, y: 300, button: 'left', clickCount: 1 });
      await s.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: 300, y: 300, button: 'left', clickCount: 1 });
      await ev('rearm()'); await sleep(200);
      for (const type of ['rawKeyDown', 'keyUp']) {
        await s.send('Input.dispatchKeyEvent', { type, key: 'PageDown', code: 'PageDown', windowsVirtualKeyCode: 34, nativeVirtualKeyCode: 34 });
      }
      await sleep(1100);
      out.scroll = summary(await ev('window.__scroll'));
      await ev('rearm()'); await sleep(200);
      await s.send('Input.dispatchMouseEvent', { type: 'mouseWheel', x: 300, y: 300, deltaX: 0, deltaY: 120 });
      await sleep(1100);
      out.wheel = summary(await ev('window.__scroll'));
    }
    if (what === 'dark' || what === 'all') {
      await go('/white'); await sleep(500);
      const shot = await s.send('Page.captureScreenshot', { format: 'png', clip: { x: 200, y: 300, width: 4, height: 4, scale: 1 } });
      out.darkPng = shot.data;   // decoded by the caller (PowerShell has System.Drawing)
      out.darkScheme = await ev("matchMedia('(prefers-color-scheme: dark)').matches");
    }
    if (what === 'video' || what === 'all') {
      await go('/white');
      out.video = await ev(`(async () => {
        const ask = c => navigator.mediaCapabilities.decodingInfo({ type: 'file', video: { contentType: c, width: 1920, height: 1080, bitrate: 8000000, framerate: 30 } })
          .then(r => ({ supported: r.supported, smooth: r.smooth, powerEfficient: r.powerEfficient }));
        return { h264: await ask('video/mp4; codecs="avc1.640028"'), vp9: await ask('video/webm; codecs="vp09.00.40.08"') };
      })()`);
    }
  } finally { s.close(); }
  console.log(JSON.stringify(out));
})().catch(e => { console.error('ERROR ' + e.message); process.exitCode = 1; });
