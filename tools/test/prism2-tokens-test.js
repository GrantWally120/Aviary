// Prism 2 token regression (no browser needed, so it runs in CI): every generated copy matches design/prism2.tokens.json, and the
// numbers in it still clear WCAG AA. A browser-based measurement of the real pages lives in tools/test/prism/ (README there).
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..');
const { T, rgba } = require(path.join(root, 'tools/design/prism2.js'));

let passed = 0;
function check(name, fn) { try { fn(); passed++; console.log('PASS ' + name); } catch (e) { console.error('FAIL ' + name + '\n  ' + e.message); process.exitCode = 1; } }

// ── WCAG ──
const lin = c => { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
const lum = c => 0.2126 * lin(c[0]) + 0.7152 * lin(c[1]) + 0.0722 * lin(c[2]);
const over = (fg, bg) => { const a = fg[3] ?? 1; return [0, 1, 2].map(i => fg[i] * a + bg[i] * (1 - a)); };
const ratio = (fg, bg) => { const l1 = lum(over(fg, bg)), l2 = lum(bg); return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05); };
function parse(s) {
  s = s.trim();
  let m = /^#([0-9a-f]{6})$/i.exec(s); if (m) return [parseInt(m[1].slice(0, 2), 16), parseInt(m[1].slice(2, 4), 16), parseInt(m[1].slice(4, 6), 16), 1];
  m = /^rgba?\(([^)]+)\)$/.exec(s); if (m) { const p = m[1].split(',').map(Number); return [p[0], p[1], p[2], p.length > 3 ? p[3] : 1]; }
  throw new Error('cannot read colour ' + s);
}
const WHITE = [255, 255, 255, 1];

check('every generated copy matches design/prism2.tokens.json', () => {
  const r = spawnSync(process.execPath, [path.join(root, 'tools/design/prism2.js'), 'check'], { encoding: 'utf8' });
  assert.equal(r.status, 0, (r.stderr || '') + (r.stdout || ''));
});

for (const scheme of ['light', 'dark']) {
  const w = T.web[scheme], g = T.glass[scheme], opaque = g.opaque.slice(0, 3);
  check(`${scheme}: text tokens clear 4.5:1 on the opaque glass`, () => {
    for (const k of ['--txt-1', '--txt-2', '--txt-3']) { const r = ratio(parse(w[k]), opaque); assert.ok(r >= 4.5, `${k} ${w[k]} is ${r.toFixed(2)}:1`); }
  });
  check(`${scheme}: text tokens clear 4.5:1 on the read surface over the page`, () => {
    const page = parse(w['--bg']).slice(0, 3), read = over(g.fillRead, page);
    for (const k of ['--txt-1', '--txt-2', '--txt-3']) { const r = ratio(parse(w[k]), read); assert.ok(r >= 4.5, `${k} on the read surface is ${r.toFixed(2)}:1`); }
  });
  check(`${scheme}: fills that carry white text (button, done, in progress) clear 4.5:1`, () => {
    for (const k of ['--accent-btn', '--ok-solid', '--warm-solid']) { const r = ratio(WHITE, parse(w[k]).slice(0, 3)); assert.ok(r >= 4.5, `white on ${k} ${w[k]} is ${r.toFixed(2)}:1`); }
  });
  check(`${scheme}: status inks clear 4.5:1 on their tinted tags`, () => {
    const page = opaque;
    for (const [ink, fill, a] of [['--ok-ink', '--ok', 0.14], ['--warm-ink', '--warm', 0.15]]) {
      const tint = over([...parse(w[fill]).slice(0, 3), a], page), r = ratio(parse(w[ink]), tint);
      assert.ok(r >= 4.5, `${ink} on its ${fill} tint is ${r.toFixed(2)}:1`);
    }
  });
  check(`${scheme}: the accent text ink clears 3:1 (large display text) on the field`, () => {
    const r = ratio(parse(w['--accent-ink']), parse(w['--bg']).slice(0, 3)); assert.ok(r >= 3, `--accent-ink is ${r.toFixed(2)}:1 on the page`);
  });
  check(`${scheme}: the Windows app's labels clear 4.5:1 on the glass colour the token file assumes`, () => {
    const n = T.native[scheme], under = n.glassUnder;
    for (const k of ['label', 'labelSecondary', 'labelTertiary']) { const r = ratio(n[k], under); assert.ok(r >= 4.5, `native ${k} is ${r.toFixed(2)}:1`); }
  });
  // The address box places its hint in the tertiary label on top of AvFieldBrush, which is darker than the bare glass: measured
  // in the running app on 23 Oct 2026 as 4.23:1 against 4.24 calculated here with the old 0.72 alpha, so this model is the real one.
  check(`${scheme}: the address box's hint (tertiary label on the field fill) clears 4.5:1`, () => {
    const n = T.native[scheme], field = over(n.fieldFill, n.glassUnder);
    for (const k of ['label', 'labelSecondary', 'labelTertiary']) { const r = ratio(n[k], field); assert.ok(r >= 4.5, `native ${k} on the field is ${r.toFixed(2)}:1`); }
  });
  check(`${scheme}: the token file's field fill is the one LiquidGlass.xaml draws`, () => {
    const xaml = fs.readFileSync(path.join(root, 'src/Aviary/Themes/LiquidGlass.xaml'), 'utf8');
    const brushes = [...xaml.matchAll(/x:Key="AvFieldBrush"\s+Color="#([0-9A-Fa-f]{8})"/g)].map(m => m[1]);
    const hex = brushes[scheme === 'light' ? 0 : 1], n = T.native[scheme].fieldFill;
    assert.ok(hex, 'no AvFieldBrush found');
    const a = parseInt(hex.slice(0, 2), 16) / 255, rgb = [2, 4, 6].map(i => parseInt(hex.slice(i, i + 2), 16));
    assert.ok(Math.abs(a - n[3]) < 0.01 && rgb.every((v, i) => v === n[i]), `xaml #${hex} vs tokens ${n.join(',')}`);
  });
}

check('the rim is narrower than the smallest capsule it sits on, and the body and rim use different filters', () => {
  assert.ok(parseFloat(T.rimWidth) <= 8);
  for (const s of ['light', 'dark']) assert.notEqual(T.glass[s].backdrop, T.glass[s].rim);
});

check('the landing page and the Prism 2 specimen request nothing from another host', () => {
  for (const f of ['Aviary.html', 'Aviary-Phase0.html', '../Prism2-Specimen.html']) {
    const p = path.join(root, f); if (!fs.existsSync(p)) continue;
    const t = fs.readFileSync(p, 'utf8');
    const bad = [...t.matchAll(/(?:<link[^>]+href|<script[^>]+src|<img[^>]+src|<iframe[^>]+src|url\()\s*=?\s*["']?(https?:)?\/\/[^"')\s>]+/gi)].map(m => m[0]).filter(s => !/w3\.org/.test(s));
    assert.deepEqual(bad, [], `${f} loads: ${bad.slice(0, 3).join(' | ')}`);
  }
});

check('no capsule in the three Prism pages carries loose text (it would paint under the body layer)', () => {
  // static approximation of tools/test/prism/smoke.js: an element with class gl whose first content is a bare text node
  for (const f of ['Aviary.html', 'Aviary-Phase0.html', '../Prism2-Specimen.html']) {
    const p = path.join(root, f); if (!fs.existsSync(p)) continue;
    const t = fs.readFileSync(p, 'utf8');
    const bad = [...t.matchAll(/<(?:div|span|a|article|section|aside|figure)\b[^>]*\bclass="[^"]*\b(?:gl|gcap)\b[^"]*"[^>]*>(?:\s*<span class="grim"><\/span>)?\s*([^<\s][^<]{2,})</g)].map(m => m[1].trim()).filter(s => /^[A-Za-z0-9]/.test(s));
    assert.deepEqual(bad, [], `${f}: ${bad.slice(0, 3).join(' | ')}`);
  }
});

console.log(`\n${passed} Prism 2 token checks passed`);
