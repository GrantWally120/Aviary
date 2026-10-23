// Prism 2 measuring kit: a small Chrome DevTools Protocol client for headless Edge/Chrome, a PNG decoder and
// the WCAG helpers the 2 Oct 2026 Prism review measured with (tools/test/prism/README.md). No dependencies.
//
//   const { launch } = require('./lib');
//   const browser = await launch();                       // spawns headless Edge on a scratch profile
//   const page = await browser.newPage({ width: 1280, height: 800, scheme: 'dark' });
//   await page.goto('file:///E:/.../Aviary.html');
//   const png = await page.shot();                         // Buffer
//   await browser.close();
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const zlib = require('node:zlib');
const { spawn } = require('node:child_process');

const EDGE = [
  'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
].find(p => fs.existsSync(p));

const sleep = ms => new Promise(r => setTimeout(r, ms));

async function waitForPort(port, tries = 60) {
  for (let i = 0; i < tries; i++) {
    try { const r = await fetch(`http://127.0.0.1:${port}/json/version`); if (r.ok) return; } catch (e) { /* not yet */ }
    await sleep(250);
  }
  throw new Error('browser did not open its debugging port ' + port);
}

async function launch(opts = {}) {
  const port = opts.port || 9555;
  let child = null;
  try { await waitForPort(port, 1); }
  catch (e) {
    if (!EDGE) throw new Error('no Edge or Chrome found');
    const profile = opts.profile || fs.mkdtempSync(path.join(os.tmpdir(), 'prism-edge-'));
    child = spawn(EDGE, ['--headless=new', `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
      '--no-first-run', '--no-default-browser-check', '--force-device-scale-factor=1',
      '--disable-background-timer-throttling', '--disable-renderer-backgrounding', '--hide-scrollbars', 'about:blank'],
      { stdio: 'ignore', detached: false });
    await waitForPort(port);
  }
  const browser = {
    port, child,
    async newPage(o = {}) {
      const r = await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' });
      const target = await r.json();
      const page = new Page(target);
      page.port = port;
      await page.ready;
      await page.configure(o);
      return page;
    },
    async close() { if (child) { try { child.kill(); } catch (e) { /* gone */ } } },
  };
  return browser;
}

class Page {
  constructor(target) {
    this.target = target;
    this.ws = new WebSocket(target.webSocketDebuggerUrl);
    this.n = 0; this.pend = new Map(); this.listeners = []; this.console = [];
    this.ready = new Promise((res, rej) => { this.ws.onopen = res; this.ws.onerror = rej; });
    this.ws.onmessage = ev => {
      const m = JSON.parse(ev.data);
      if (m.id && this.pend.has(m.id)) {
        const { resolve, reject } = this.pend.get(m.id); this.pend.delete(m.id);
        m.error ? reject(new Error(m.error.message + ' (' + JSON.stringify(m.error.data || '') + ')')) : resolve(m.result);
      } else if (m.method) {
        this.listeners.forEach(f => f(m));
        if (m.method === 'Runtime.consoleAPICalled') this.console.push(m.params.type + ': ' + m.params.args.map(a => a.value ?? a.description).join(' '));
        if (m.method === 'Runtime.exceptionThrown') this.console.push('exception: ' + (m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text));
        if (m.method === 'Log.entryAdded') this.console.push(m.params.entry.level + ': ' + m.params.entry.text + (m.params.entry.url ? ' ' + m.params.entry.url : ''));
      }
    };
  }
  send(method, params = {}) {
    const id = ++this.n;
    this.ws.send(JSON.stringify({ id, method, params }));
    return new Promise((resolve, reject) => this.pend.set(id, { resolve, reject }));
  }
  /** width, height, dpr, scheme ('light'|'dark'), media: { 'prefers-contrast': 'more', ... } */
  async configure(o = {}) {
    await this.send('Page.enable'); await this.send('Runtime.enable'); await this.send('Log.enable');
    this.o = { width: 1280, height: 800, dpr: 1, scheme: 'light', media: {}, ...o };
    await this.send('Emulation.setDeviceMetricsOverride', { width: this.o.width, height: this.o.height, deviceScaleFactor: this.o.dpr, mobile: false });
    const features = [{ name: 'prefers-color-scheme', value: this.o.scheme }, ...Object.entries(this.o.media).map(([name, value]) => ({ name, value }))];
    await this.send('Emulation.setEmulatedMedia', { features });
  }
  async goto(url, settle = 800) {
    const loaded = new Promise(res => { const f = m => { if (m.method === 'Page.loadEventFired') { this.listeners = this.listeners.filter(x => x !== f); res(); } }; this.listeners.push(f); });
    await this.send('Page.navigate', { url });
    await Promise.race([loaded, sleep(15000)]);
    await sleep(settle);
  }
  async eval(expression) {
    const r = await this.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) throw new Error('page error: ' + (r.exceptionDetails.exception?.description || r.exceptionDetails.text));
    return r.result.value;
  }
  async shot(clip) {
    const r = await this.send('Page.captureScreenshot', { format: 'png', ...(clip ? { clip: { ...clip, scale: 1 } } : {}) });
    return Buffer.from(r.data, 'base64');
  }
  async close() {
    try { this.ws.close(); await fetch(`http://127.0.0.1:${this.port}/json/close/${this.target.id}`); } catch (e) { /* gone */ }
  }
}

// ── PNG decoder (8-bit, non-interlaced, colour types 2 and 6: what Chromium writes) ──
function decodePng(buf) {
  if (buf.readUInt32BE(0) !== 0x89504e47) throw new Error('not a PNG');
  let off = 8, w = 0, h = 0, ctype = 0, idat = [];
  while (off < buf.length) {
    const len = buf.readUInt32BE(off), type = buf.toString('ascii', off + 4, off + 8), data = buf.subarray(off + 8, off + 8 + len);
    if (type === 'IHDR') { w = data.readUInt32BE(0); h = data.readUInt32BE(4); ctype = data[9]; if (data[8] !== 8 || data[12] !== 0) throw new Error('unsupported PNG'); }
    else if (type === 'IDAT') idat.push(data);
    else if (type === 'IEND') break;
    off += 12 + len;
  }
  const bpp = ctype === 6 ? 4 : ctype === 2 ? 3 : 0;
  if (!bpp) throw new Error('unsupported PNG colour type ' + ctype);
  const raw = zlib.inflateSync(Buffer.concat(idat)), stride = w * bpp, out = Buffer.alloc(w * h * 4);
  let prev = Buffer.alloc(stride);
  for (let y = 0; y < h; y++) {
    const f = raw[y * (stride + 1)], line = Buffer.from(raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1)));
    for (let i = 0; i < stride; i++) {
      const a = i >= bpp ? line[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
      let v = line[i];
      if (f === 1) v += a; else if (f === 2) v += b; else if (f === 3) v += (a + b) >> 1;
      else if (f === 4) { const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c); v += (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c); }
      line[i] = v & 255;
    }
    for (let x = 0; x < w; x++) {
      out[(y * w + x) * 4] = line[x * bpp]; out[(y * w + x) * 4 + 1] = line[x * bpp + 1]; out[(y * w + x) * 4 + 2] = line[x * bpp + 2];
      out[(y * w + x) * 4 + 3] = bpp === 4 ? line[x * bpp + 3] : 255;
    }
    prev = line;
  }
  return { w, h, data: out };
}

// ── WCAG ──
const lin = c => { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
const lum = (r, g, b) => 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
const ratio = (l1, l2) => (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
function contrast(fg, bg) { // fg: {r,g,b,a}; bg: {r,g,b}
  const a = fg.a ?? 1, c = { r: fg.r * a + bg.r * (1 - a), g: fg.g * a + bg.g * (1 - a), b: fg.b * a + bg.b * (1 - a) };
  return ratio(lum(c.r, c.g, c.b), lum(bg.r, bg.g, bg.b));
}
function parseColor(s) {
  const m = /rgba?\(([^)]+)\)/.exec(s); if (!m) return null;
  const p = m[1].split(/[\s,\/]+/).filter(Boolean).map(Number);
  return { r: p[0], g: p[1], b: p[2], a: p.length > 3 ? p[3] : 1 };
}

module.exports = { launch, Page, decodePng, contrast, parseColor, lum, ratio, sleep };
