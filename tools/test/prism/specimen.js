// "Definition of done" for Prism2-Specimen.html (docs: Prism Review 2026-10-02/Claude-Code-Prompt-Prism2-Specimen.md).
//
//   node tools/test/prism/specimen.js [path-to-specimen] [--out <screenshot dir>] [--stamp]
//
// 1 screenshots of every section at 1280x800 and 390x844, light and dark   2 no glass nested in glass (this document)
// 3 pixel-sampled contrast, >= 99% of characters at AA on every screen      4 the background field never exposes an edge
// 5 the sibling-layer rim's edge transition in the 7 px band is <= 6 px     6 no console errors, no horizontal scroll at 390,
// every accessibility mode works                                            7 under 400 KB, no network request
// --stamp writes the measured rim numbers (A, B, C) into the page's Rim lab.
const fs = require('node:fs');
const path = require('node:path');
const { launch, decodePng, lum } = require('./lib');
const { measure, FREEZE } = require('./contrast');
const { inventory } = require('./inventory');

const args = process.argv.slice(2), get = (k, d) => { const i = args.indexOf('--' + k); return i >= 0 ? args[i + 1] : d; };
const file = path.resolve(args.find((a, i) => !a.startsWith('--') && !(args[i - 1] || '').startsWith('--')) || 'E:/Claude CoWork Enclosed/Prism2-Specimen.html');
const url = 'file:///' + file.replace(/\\/g, '/');
const out = get('out', path.join(path.dirname(file), 'Prism Review 2026-10-02', 'specimen-shots'));
fs.mkdirSync(out, { recursive: true });

const SECTIONS = [['hero', '.hero'], ['tokens', '#tokens'], ['lab', '#lab'], ['rules', '#rules'], ['rim', '#rim'], ['artemis', '#artemis'], ['modes', '#modes'], ['tiers', '#tiers'], ['experiment', '#experiment']];
const SCREENS = ['home', 'chat', 'settings', 'history'];
const results = []; let fails = 0;
function check(name, ok, detail) { results.push({ name, ok, detail }); if (!ok) fails++; console.log((ok ? 'PASS  ' : 'FAIL  ') + name + (detail ? '  -- ' + detail : '')); }

async function sectionTop(p, sel) { return p.eval(`(function(){ var e = document.querySelector(${JSON.stringify(sel)}); return Math.max(0, Math.round(e.getBoundingClientRect().top + scrollY - 84)); })()`); }
async function sectionHeight(p, sel) { return p.eval(`Math.round(document.querySelector(${JSON.stringify(sel)}).getBoundingClientRect().height)`); }
async function shot(p, name) { fs.writeFileSync(path.join(out, name + '.png'), await p.shot()); }

async function edgeWidth(page, selectorForCapsule, frameSel) {
  /* The review's rim test on a capsule on a hard black/white split, UNmagnified, at 2x density: the 10%-90% width of the
     step along a row 3.5 px inside the capsule's top edge (the middle of the 7 px rim band), in CSS px. */
  const info = await page.eval(`(function(){
    var host = document.querySelector(${JSON.stringify(frameSel)}); var doc = host.tagName === 'IFRAME' ? host.contentDocument : document;
    var s = doc.createElement('style'); s.id = '__unscale'; s.textContent = '.scene{transform:none!important;left:calc(50% - 150px)!important}'; (doc.head || doc.documentElement).appendChild(s);
    host.scrollIntoView({block:'center'});
    var cap = (host.tagName === 'IFRAME' ? doc : host).querySelector(${JSON.stringify(selectorForCapsule)}); var r = cap.getBoundingClientRect(), f = host.tagName === 'IFRAME' ? host.getBoundingClientRect() : {left:0,top:0};
    return { x: f.left + r.left, y: f.top + r.top, w: r.width, h: r.height, vw: innerWidth };
  })()`);
  await new Promise(r => setTimeout(r, 500));
  const info2 = await page.eval(`(function(){
    var host = document.querySelector(${JSON.stringify(frameSel)}); var doc = host.tagName === 'IFRAME' ? host.contentDocument : document;
    var cap = (host.tagName === 'IFRAME' ? doc : host).querySelector(${JSON.stringify(selectorForCapsule)}); var r = cap.getBoundingClientRect(), f = host.tagName === 'IFRAME' ? host.getBoundingClientRect() : {left:0,top:0};
    return { x: f.left + r.left, y: f.top + r.top, w: r.width, h: r.height, vw: innerWidth };
  })()`);
  const img = decodePng(await page.shot()), sc = img.w / info2.vw;
  const row = Math.round((info2.y + 3.5) * sc), cx = Math.round((info2.x + info2.w / 2) * sc), vals = [];
  for (let x = cx - Math.round(100 * sc); x <= cx + Math.round(100 * sc); x++) { const i = (row * img.w + x) * 4; vals.push(lum(img.data[i], img.data[i + 1], img.data[i + 2])); }
  const lo = vals.slice(0, 10).reduce((a, b) => a + b) / 10, hi = vals.slice(-10).reduce((a, b) => a + b) / 10;
  const t10 = lo + 0.1 * (hi - lo), t90 = lo + 0.9 * (hi - lo);
  const a = vals.findIndex(v => v >= t10), b = vals.findIndex(v => v >= t90);
  await page.eval(`(function(){ var host = document.querySelector(${JSON.stringify(frameSel)}); var doc = host.tagName === 'IFRAME' ? host.contentDocument : document; var s = doc.getElementById('__unscale'); if (s) s.remove(); })()`);
  return a < 0 || b < 0 ? NaN : +((b - a) / sc).toFixed(1);
}

(async () => {
  const size = fs.statSync(file).size;
  check('file under 400 KB', size < 400 * 1024, (size / 1024).toFixed(0) + ' KB');
  const browser = await launch({ port: 9555 });
  const rim = {};

  for (const scheme of ['light', 'dark']) for (const [w, h] of [[1280, 800], [390, 844]]) {
    const tag = `${scheme}-${w}`;
    const p = await browser.newPage({ width: w, height: h, scheme });
    await p.goto(url, 1800);
    await p.eval(FREEZE);
    check(`${tag}: no console errors`, !p.console.some(l => /^(error|exception)/i.test(l)), p.console.find(l => /^(error|exception)/i.test(l)));
    const ext = await p.eval(`performance.getEntriesByType('resource').map(function(r){return r.name}).filter(function(n){return !/^(file|data|blob):/.test(n)})`);
    check(`${tag}: no network request`, ext.length === 0, ext.slice(0, 2).join(' '));
    check(`${tag}: no horizontal scroll`, (await p.eval('document.documentElement.scrollWidth - document.documentElement.clientWidth')) <= 1);
    const inv = await inventory(p);
    check(`${tag}: no glass nested in glass (${inv.total} surfaces)`, inv.nested === 0, inv.nestedRows.map(r => r.el).join(', '));

    let chars = 0, pass = 0, worst = 100; const failing = [];
    for (const [name, sel] of SECTIONS) {
      if (name === 'experiment') await p.eval(`(function(){ var c = document.getElementById('expToggle'); c.checked = true; c.dispatchEvent(new Event('change')); })()`);
      const targets = name === 'artemis' ? SCREENS : [null];
      for (const screen of targets) {
        if (screen) await p.eval(`document.querySelector('#artSeg [data-screen=${screen}]').click()`);
        await new Promise(r => setTimeout(r, 300));
        const top = await sectionTop(p, sel), height = await sectionHeight(p, sel);
        const step = h - 140, scrolls = []; for (let y = top; y < top + height - 60 || y === top; y += step) scrolls.push(y);
        const res = await measure(p, { scrolls, select: sel, fails: 5, under: '.art__scrimT,.art__scrimB,.scrim' });
        let c = 0, ps = 0; res.forEach(r => { c += r.chars; ps += r.chars * r.typicalPct / 100; worst = Math.min(worst, r.typicalPct); r.fails.forEach(f => failing.push(`${name}${screen ? '/' + screen : ''}: ${f.ratio}:1 "${f.text}" (${f.tag})`)); });
        chars += c; pass += ps;
        const pct = c ? +(100 * ps / c).toFixed(1) : 100;
        check(`${tag} ${name}${screen ? '/' + screen : ''}: ${pct}% of ${c} characters pass AA`, pct >= 99, failing.filter(f => f.startsWith(name)).slice(0, 2).join(' | '));
        const shotTop = screen ? await sectionTop(p, '#art') : top;   // the Artemis screens are photographed at the stage, not its heading
        await p.eval(`window.scrollTo(0, ${shotTop}); 0`); await new Promise(r => setTimeout(r, 400));
        await shot(p, `${tag}-${name}${screen ? '-' + screen : ''}`);
      }
    }
    // each accessibility mode: still no console errors, nothing nested, no horizontal scroll
    for (const mode of ['reduced-transparency', 'contrast', 'forced-colors', 'reduce-motion', 'no-backdrop']) {
      await p.eval(`(function(){ var c = document.querySelector('[data-mode-toggle=${mode}]'); c.checked = true; c.dispatchEvent(new Event('change')); })()`);
      await new Promise(r => setTimeout(r, 600));
      const inv2 = await inventory(p), over = await p.eval('document.documentElement.scrollWidth - document.documentElement.clientWidth');
      const blur = await p.eval(`Array.prototype.filter.call(document.querySelectorAll('.gl'),function(e){var c=getComputedStyle(e,'::before');return c.display!=='none'&&c.backdropFilter&&c.backdropFilter!=='none'}).length`);
      check(`${tag} mode ${mode}: still sound`, inv2.nested === 0 && over <= 1 && (mode === 'reduce-motion' || blur === 0), `nested ${inv2.nested}, over ${over}px, still blurring ${blur}`);
      await p.eval(`(function(){ var c = document.querySelector('[data-mode-toggle=${mode}]'); c.checked = false; c.dispatchEvent(new Event('change')); })()`);
    }

    if (scheme === 'light' && w === 1280) {
      // field coverage: the fixed page field and the Artemis field never move, and cover what they sit behind
      const cover = await p.eval(`(function(){
        var out = []; var pairs = [['.field', null], ['.art__field', '.art']];
        pairs.forEach(function(pr){
          var f = document.querySelector(pr[0]), host = pr[1] ? document.querySelector(pr[1]) : null;
          var fr = f.getBoundingClientRect(), hr = host ? host.getBoundingClientRect() : { left: 0, top: 0, right: innerWidth, bottom: innerHeight };
          var tr = getComputedStyle(f).transform, anim = f.getAnimations ? f.getAnimations().length : 0;
          out.push({ sel: pr[0], covers: fr.left <= hr.left + .5 && fr.top <= hr.top + .5 && fr.right >= hr.right - .5 && fr.bottom >= hr.bottom - .5, transform: tr, animations: anim,
                     marginX: Math.round(hr.left - fr.left), marginY: Math.round(hr.top - fr.top) });
        });
        return out; })()`);
      cover.forEach(c => check(`field ${c.sel}: static and covers its stage (margin ${c.marginX}px, ${c.marginY}px)`, c.covers && (c.transform === 'none') && c.animations === 0, JSON.stringify(c)));
    }

    if (scheme === 'light' && w === 1280) {
      // rim lab: A nested (old), B no rim, C sibling layers (Prism 2)
      await p.eval(`document.querySelectorAll('.demo').forEach(function(d){}); 0`);
      rim.A = await edgeWidth(p, '.old', '#cropA iframe');
      rim.B = await edgeWidth(p, '.gl', '#cropB');
      rim.C = await edgeWidth(p, '.gl', '#cropC');
      check(`rim: C (sibling layers) edge transition in the 7 px band is ${rim.C} px (A nested ${rim.A} px, B none ${rim.B} px)`, rim.C <= 6 && rim.A > 6, '');
    }
    await p.close();
  }

  // field coverage at four sizes
  for (const [w, h] of [[1280, 800], [1920, 1080], [390, 844], [2560, 1080]]) {
    const p = await browser.newPage({ width: w, height: h, scheme: 'light' });
    await p.goto(url, 800);
    const r = await p.eval(`(function(){
      var f = document.querySelector('.field').getBoundingClientRect(), a = document.querySelector('.art').getBoundingClientRect(), af = document.querySelector('.art__field').getBoundingClientRect();
      return { page: f.left <= 0 && f.top <= 0 && f.right >= innerWidth && f.bottom >= innerHeight, art: af.left <= a.left && af.top <= a.top && af.right >= a.right && af.bottom >= a.bottom };
    })()`);
    check(`field covers the screen and the Artemis stage at ${w}x${h}, whatever its blobs do`, r.page && r.art, JSON.stringify(r));
    await p.close();
  }

  await browser.close();
  if (args.includes('--stamp') && !isNaN(rim.A + rim.B + rim.C)) {
    let t = fs.readFileSync(file, 'utf8');
    for (const k of ['A', 'B', 'C']) t = t.replace(new RegExp(`(id="rim${k}">)[^<]*(<small>)`), `$1${rim[k]} px$2`);
    fs.writeFileSync(file, t); console.log(`stamped rim numbers A ${rim.A}, B ${rim.B}, C ${rim.C} into the Rim lab`);
  }
  console.log(`\n${results.length - fails} of ${results.length} checks passed; screenshots in ${out}`);
  process.exit(fails ? 1 : 0);
})().catch(e => { console.error(e.stack || e); process.exit(1); });
