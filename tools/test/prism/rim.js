// Rim test, as the 2 Oct 2026 review ran it: put a hard black / white split behind a real glass capsule and measure how
// wide the edge's 10%-90% transition is (a) inside the capsule's outer rim band and (b) in its interior. A rim that
// lenses the page keeps the edge sharp in the band (a few px) while the interior blur smears it (tens of px); a rim
// nested in the blurred body smears both alike (the review measured 39 px for the nested rim and for no rim at all).
//
//   node tools/test/prism/rim.js <file|url> [--selector .nav__cap] [--scheme light|dark] [--max 6]
const path = require('node:path');
const { launch, decodePng, lum } = require('./lib');

async function measureRim(page, selector, dpr = 2) {
  await page.eval(`(function(){
    var s = document.createElement('style'); s.id = '__rimtest';
    s.textContent = '.field,.field *{display:none!important} html,body{background:linear-gradient(90deg,#000 0,#000 50%,#fff 50%,#fff 100%)!important} *,*::before,*::after{animation:none!important;transition:none!important}';
    document.head.appendChild(s); window.scrollTo(0, 0); })()`);
  await new Promise(r => setTimeout(r, 400));
  const r = await page.eval(`(function(){ var e = document.querySelector(${JSON.stringify(selector)}); var b = e.getBoundingClientRect(); return { left: b.left, top: b.top, width: b.width, height: b.height, vw: innerWidth }; })()`);
  const img = decodePng(await page.shot());
  const scale = img.w / r.vw, edgeX = Math.round(r.vw / 2 * scale);
  const row = yCss => {
    const y = Math.round(yCss * scale), vals = [];
    for (let x = edgeX - 160; x <= edgeX + 160; x++) { const i = (y * img.w + x) * 4; vals.push(lum(img.data[i], img.data[i + 1], img.data[i + 2])); }
    return vals;
  };
  const width = vals => { // 10%-90% transition, in CSS px, between the plateau levels at the row's two ends
    const lo = vals.slice(0, 12).reduce((a, b) => a + b) / 12, hi = vals.slice(-12).reduce((a, b) => a + b) / 12;
    const t10 = lo + 0.1 * (hi - lo), t90 = lo + 0.9 * (hi - lo);
    let a = vals.findIndex(v => v >= t10), b = vals.findIndex(v => v >= t90);
    return a < 0 || b < 0 ? NaN : (b - a) / scale;
  };
  const band = width(row(r.top + 3.5)), mid = width(row(r.top + r.height / 2));
  await page.eval(`(function(){ var s = document.getElementById('__rimtest'); if (s) s.remove(); })()`);
  return { band: +band.toFixed(1), interior: +mid.toFixed(1), capsule: r };
}
module.exports = { measureRim };

if (require.main === module) {
  const args = process.argv.slice(2), get = (k, d) => { const i = args.indexOf('--' + k); return i >= 0 ? args[i + 1] : d; };
  const target = args.find((a, i) => !a.startsWith('--') && !(args[i - 1] || '').startsWith('--'));
  (async () => {
    const url = /^(https?|file):/.test(target) ? target : 'file:///' + path.resolve(target).replace(/\\/g, '/');
    const b = await launch({ port: +get('port', 9555) });
    const p = await b.newPage({ width: 1280, height: 800, dpr: 2, scheme: get('scheme', 'light') });
    await p.goto(url, 1200);
    const r = await measureRim(p, get('selector', '.nav__cap'));
    console.log(`${path.basename(target)} ${get('scheme', 'light')}: edge transition in the rim band ${r.band} px, in the interior ${r.interior} px`);
    const max = +get('max', 0);
    await p.close(); await b.close();
    process.exit(max && !(r.band <= max) ? 1 : 0);
  })().catch(e => { console.error(e); process.exit(1); });
}
