// Pixel-sampled WCAG contrast, the way the 2 Oct 2026 Prism review measured it: hide every piece of text, take a
// screenshot, sample the REAL background behind each text run, and compare it with the run's computed colour.
//
//   node tools/test/prism/contrast.js <file|url> [--scheme light|dark] [--width 1280] [--height 800]
//        [--scrolls 0,700,1400] [--media prefers-contrast=more,forced-colors=active] [--fails 12] [--json out.json]
//        [--select ".css selector"] [--exclude ".site"] [--under ".scrim"] [--css ":root{--txt-2:...}"] [--live]   (--live leaves CSS animations running; the default freezes them at 0)
//
// Percentages count characters. "typical" uses the median background under a run, "worst" the 5th/95th percentile.
// AA is 4.5:1, or 3:1 for text of 24 px or more, or 18.66 px and bold. Runs whose colour is transparent (gradient text
// clipped to its letters) cannot be sampled and are listed separately.
const fs = require('node:fs');
const path = require('node:path');
const { launch, decodePng, contrast, parseColor } = require('./lib');

const COLLECT = `(function(sel, excl, under){
  var runs = [], skipped = [], covered = 0, excluded = 0;
  var topRoot = sel ? document.querySelector(sel) : document.body;
  /* Scrims and the like: content fades under them on purpose, as under glass, so text there is counted apart. */
  var unders = under ? Array.prototype.map.call(document.querySelectorAll(under), function(u){ return u.getBoundingClientRect(); }) : [];
  if (!topRoot) return { runs: runs, skipped: skipped, covered: 0, excluded: 0, error: 'no match for ' + sel };
  /* One pass over one document: the page, then each same-origin frame inside the root, positioned in page coordinates. */
  function pass(doc, win, ox, oy, root, frameEl){
    var w = win.innerWidth, h = win.innerHeight;
    function effAlpha(el){ var a = 1; for (var e = el; e && e.nodeType === 1; e = e.parentElement) { a *= parseFloat(win.getComputedStyle(e).opacity); } return a; }
    function visible(el){
      for (var e = el; e && e.nodeType === 1; e = e.parentElement) {
        var cs = win.getComputedStyle(e);
        if (cs.display === 'none' || cs.visibility === 'hidden' || cs.visibility === 'collapse') return false;
      }
      return true;
    }
    /* Text a scroll container or overflow:hidden box cuts off is not visible there: measure only what is shown. */
    function clipRect(el, r){
      var l = r.left, t = r.top, rr = r.right, b = r.bottom;
      for (var a = el; a && a.nodeType === 1 && a !== doc.documentElement && a !== doc.body; a = a.parentElement) {
        var cs = win.getComputedStyle(a);
        if (cs.overflowX === 'visible' && cs.overflowY === 'visible') continue;
        var ar = a.getBoundingClientRect();
        if (cs.overflowX !== 'visible') { l = Math.max(l, ar.left); rr = Math.min(rr, ar.right); }
        if (cs.overflowY !== 'visible') { t = Math.max(t, ar.top); b = Math.min(b, ar.bottom); }
      }
      if (rr - l < 1 || b - t < 1) return null;
      return { left: l, top: t, right: rr, bottom: b, width: rr - l, height: b - t };
    }
    var walker = doc.createTreeWalker(root, NodeFilter.SHOW_TEXT, null), n;
    while ((n = walker.nextNode())) {
      var text = n.nodeValue.replace(/\\s+/g, ' ').trim();
      if (!text) continue;
      var el = n.parentElement;
      if (!el || /^(SCRIPT|STYLE|NOSCRIPT|TEMPLATE)$/.test(el.tagName)) continue;
      if (!visible(el)) continue;
      var inExcl = excl && el.closest(excl);
      var cs = win.getComputedStyle(el), color = cs.color, fill = cs.webkitTextFillColor || color;
      var range = doc.createRange(); range.selectNodeContents(n);
      var rects = Array.prototype.filter.call(range.getClientRects(), function(r){ return r.width > 1 && r.height > 1; });
      if (!rects.length) continue;
      var totalW = rects.reduce(function(s, r){ return s + r.width; }, 0);
      var size = parseFloat(cs.fontSize), weight = parseInt(cs.fontWeight, 10) || 400, alpha = effAlpha(el);
      rects.forEach(function(r0){
        var r = clipRect(el, r0); if (!r) return;
        if (r.bottom <= 0 || r.top >= h || r.right <= 0 || r.left >= w) return;
        var gx = r.left + ox, gy = r.top + oy;
        if (gy + r.height <= 0 || gy >= innerHeight || gx + r.width <= 0 || gx >= innerWidth) return;
        var chars = Math.round(text.length * (r0.width / totalW) * (r.width / r0.width));
        if (!chars) return;
        /* Text that something else covers (clipped by an ancestor, or sliding under a glass capsule, where it is
           blurred out on purpose) is not legible text to a reader: counted apart, not measured. */
        var cx = Math.min(w - 1, Math.max(0, r.left + r.width / 2)), cy = Math.min(h - 1, Math.max(0, r.top + r.height / 2));
        var hit = doc.elementFromPoint(cx, cy), cover = hit && !(el.contains(hit) || hit.contains(el));
        if (frameEl) {
          var top = document.elementFromPoint(Math.min(innerWidth - 1, Math.max(0, gx + r.width / 2)), Math.min(innerHeight - 1, Math.max(0, gy + r.height / 2)));
          if (top !== frameEl) cover = true;
        }
        var mx = gx + r.width / 2, my = gy + r.height / 2;
        if (!cover) cover = unders.some(function(u){ return mx >= u.left && mx <= u.right && my >= u.top && my <= u.bottom; });
        if (inExcl) { excluded += chars; return; }
        if (cover) { covered += chars; return; }
        var rec = { text: text.slice(0, 48), chars: chars, x: gx, y: gy, w: r.width, h: r.height, color: fill, alpha: alpha, size: size, weight: weight,
                    tag: (frameEl ? 'frame:' : '') + el.tagName.toLowerCase() + (el.className && typeof el.className === 'string' ? '.' + el.className.trim().split(/\\s+/)[0] : '') };
        if (/rgba\\(.*,\\s*0\\)$/.test(fill) || fill === 'transparent') skipped.push(rec); else runs.push(rec);
      });
    }
  }
  pass(document, window, 0, 0, topRoot, null);
  topRoot.querySelectorAll('iframe').forEach(function(f){
    var d = null; try { d = f.contentDocument; } catch (e) {}
    if (!d || !d.body) return;
    var fr = f.getBoundingClientRect(); if (fr.bottom <= 0 || fr.top >= innerHeight) return;
    pass(d, f.contentWindow, fr.left, fr.top, d.body, f);
  });
  return { runs: runs, skipped: skipped, covered: covered, excluded: excluded };
})`;

const HIDE = `(function(){
  var css = '*,*::before,*::after{color:transparent!important;-webkit-text-fill-color:transparent!important;text-shadow:none!important;caret-color:transparent!important;text-decoration-color:transparent!important}';
  function put(doc){ var s = doc.createElement('style'); s.id = '__hide_text'; s.textContent = css; (doc.head || doc.documentElement).appendChild(s); }
  put(document);
  document.querySelectorAll('iframe').forEach(function(f){ try { if (f.contentDocument) put(f.contentDocument); } catch (e) {} });
})()`;
const UNHIDE = `(function(){
  function drop(doc){ var s = doc.getElementById('__hide_text'); if (s) s.remove(); }
  drop(document);
  document.querySelectorAll('iframe').forEach(function(f){ try { if (f.contentDocument) drop(f.contentDocument); } catch (e) {} });
})()`;
const FREEZE = `(function(){ var s = document.createElement('style'); s.id = '__freeze';
  s.textContent = '*,*::before,*::after{animation:none!important;transition:none!important;scroll-behavior:auto!important}'; document.head.appendChild(s); })()`;

function sampleBg(img, run) {
  const x0 = Math.max(0, Math.floor(run.x)), y0 = Math.max(0, Math.floor(run.y));
  const x1 = Math.min(img.w, Math.ceil(run.x + run.w)), y1 = Math.min(img.h, Math.ceil(run.y + run.h));
  const px = [];
  const stepX = Math.max(1, Math.floor((x1 - x0) / 24)), stepY = Math.max(1, Math.floor((y1 - y0) / 6));
  for (let y = y0; y < y1; y += stepY) for (let x = x0; x < x1; x += stepX) {
    const i = (y * img.w + x) * 4; const r = img.data[i], g = img.data[i + 1], b = img.data[i + 2];
    px.push({ r, g, b, l: 0.2126 * r + 0.7152 * g + 0.0722 * b });
  }
  if (!px.length) return null;
  px.sort((a, b) => a.l - b.l);
  const at = q => px[Math.min(px.length - 1, Math.max(0, Math.floor(q * (px.length - 1))))];
  return { med: at(0.5), lo: at(0.05), hi: at(0.95) };
}

async function measure(page, opts = {}) {
  const scrolls = opts.scrolls || [0];
  const results = [];
  for (const y of scrolls) {
    await page.eval(`window.scrollTo(0, ${y}); 0`);
    await new Promise(r => setTimeout(r, 900));
    const collected = await page.eval(COLLECT + '(' + JSON.stringify(opts.select || '') + ',' + JSON.stringify(opts.exclude || '') + ',' + JSON.stringify(opts.under || '') + ')');
    await page.eval(HIDE);
    await new Promise(r => setTimeout(r, 200));
    const img = decodePng(await page.shot());
    await page.eval(UNHIDE);
    const scale = img.w / page.o.width;
    let chars = 0, passT = 0, passW = 0; const fails = [];
    for (const run of collected.runs) {
      const scaled = { ...run, x: run.x * scale, y: run.y * scale, w: run.w * scale, h: run.h * scale };
      const s = sampleBg(img, scaled); if (!s) continue;
      const fg = parseColor(run.color); if (!fg) continue;
      fg.a = (fg.a ?? 1) * run.alpha;
      const large = run.size >= 24 || (run.size >= 18.66 && run.weight >= 700), need = large ? 3 : 4.5;
      const t = contrast(fg, s.med), wst = Math.min(contrast(fg, s.lo), contrast(fg, s.hi));
      chars += run.chars; if (t >= need) passT += run.chars; if (wst >= need) passW += run.chars;
      if (t < need) fails.push({ text: run.text, chars: run.chars, ratio: +t.toFixed(2), need, size: run.size, weight: run.weight, tag: run.tag, y: Math.round(run.y) });
    }
    fails.sort((a, b) => b.chars - a.chars);
    results.push({ scroll: y, chars, typicalPct: chars ? +(100 * passT / chars).toFixed(1) : 100, worstPct: chars ? +(100 * passW / chars).toFixed(1) : 100,
                   fails: fails.slice(0, opts.fails || 12), failCount: fails.length, unsampled: collected.skipped.map(s => s.text), coveredChars: collected.covered, excludedChars: collected.excluded });
  }
  return results;
}

module.exports = { measure, FREEZE };

if (require.main === module) {
  const args = process.argv.slice(2);
  const get = (k, d) => { const i = args.indexOf('--' + k); return i >= 0 ? args[i + 1] : d; };
  const positional = [];
  for (let i = 0; i < args.length; i++) { if (args[i].startsWith('--')) { if (args[i] !== '--live') i++; } else positional.push(args[i]); }
  const target = positional[0];
  (async () => {
    const url = /^(https?|file):/.test(target) ? target : 'file:///' + path.resolve(target).replace(/\\/g, '/');
    const media = {}; (get('media', '') || '').split(',').filter(Boolean).forEach(p => { const [k, v] = p.split('='); media[k] = v; });
    const browser = await launch({ port: +get('port', 9555) });
    const page = await browser.newPage({ width: +get('width', 1280), height: +get('height', 800), scheme: get('scheme', 'light'), media });
    await page.goto(url, 1200);
    if (!args.includes('--live')) await page.eval(FREEZE);
    const css = get('css', ''); if (css) await page.eval('(function(){var s=document.createElement(\'style\');s.textContent=' + JSON.stringify(css) + ';document.head.appendChild(s)})()');
    const res = await measure(page, { scrolls: get('scrolls', '0').split(',').map(Number), fails: +get('fails', 12), select: get('select', ''), exclude: get('exclude', ''), under: get('under', '') });
    console.log(`${path.basename(target)}  ${get('scheme', 'light')}  ${get('width', 1280)}x${get('height', 800)}`);
    for (const r of res) {
      console.log(`  scroll ${String(r.scroll).padStart(5)}: ${String(r.chars).padStart(5)} chars  typical ${String(r.typicalPct).padStart(5)}%  worst ${String(r.worstPct).padStart(5)}%  failing runs ${r.failCount}` + (r.coveredChars ? `  (${r.coveredChars} covered chars not measured)` : '') + (r.unsampled.length ? `  (gradient text not sampled: ${r.unsampled.length})` : ''));
      r.fails.slice(0, +get('fails', 12)).forEach(f => console.log(`        ${String(f.ratio).padStart(5)}:1 (need ${f.need})  ${f.chars}ch ${f.size}px/${f.weight}  ${f.tag}  "${f.text}"`));
    }
    const out = get('json', ''); if (out) fs.writeFileSync(out, JSON.stringify(res, null, 1));
    await page.close(); await browser.close();
    process.exit(0);
  })().catch(e => { console.error('ERR', e.stack || e.message); process.exit(1); });
}
