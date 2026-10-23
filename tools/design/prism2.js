// Prism 2: write design/prism2.tokens.json into every file that carries a copy of it, or check that none has drifted.
//
//   node tools/design/prism2.js build     rewrite the generated regions
//   node tools/design/prism2.js check     exit 1 if any region differs from what build would write
//   node tools/design/prism2.js css       print the web CSS (tokens + material) for a new page
//
// A generated region sits between  /* prism2:begin NAME */ ... /* prism2:end NAME */  (CSS and JS)  or
// <!-- prism2:begin NAME --> ... <!-- prism2:end NAME -->  (XAML). A target file lists the regions it has. A missing
// marker is an error, never a silent skip: that is how a fourth copy would start to drift.
const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.resolve(__dirname, '../..');
const T = JSON.parse(fs.readFileSync(path.join(ROOT, 'design/prism2.tokens.json'), 'utf8'));
const NL = '\n';

// ── formatting ──────────────────────────────────────────────────────────────
const a$ = a => { const s = String(Number(Number(a).toFixed(3))); return s === '0' ? '0' : s.replace(/^0\./, '.'); };
const rgba = c => `rgba(${c[0]},${c[1]},${c[2]},${a$(c[3])})`;
const pct = o => `${Number((o * 100).toFixed(2))}%`;
const grad = (angle, rgb, stops) => `linear-gradient(${angle}deg,${stops.map(([o, a]) => `rgba(${rgb[0]},${rgb[1]},${rgb[2]},${a$(a)}) ${pct(o)}`).join(',')})`;
const px = n => (n === 0 ? '0' : n + 'px');
const shadow = layers => layers.map(([x, y, b, a]) => `${px(x)} ${px(y)} ${px(b)} rgba(0,0,0,${a$(a)})`).join(',');
const hex2 = n => Math.round(n).toString(16).toUpperCase().padStart(2, '0');
const argb = c => '#' + hex2((c[3] ?? 1) * 255) + hex2(c[0]) + hex2(c[1]) + hex2(c[2]);

function glassVars(s) {
  const g = T.glass[s];
  return [
    ['--lg-backdrop', g.backdrop],
    ['--lg-fill', rgba(g.fill)],
    ['--lg-fill-read', rgba(g.fillRead)],
    ['--lg-warm', rgba(g.warm)],
    ['--lg-sheen', grad(180, g.sheen.rgb, g.sheen.stops)],
    ['--lg-ring', grad(g.ring.angle, g.ring.rgb, g.ring.stops)],
    ['--lg-edge-top', `inset 0 1px 0 ${rgba(g.edgeTop)}`],
    ['--lg-edge-bot', `inset 0 -1px 0 ${rgba(g.edgeBottom)}`],
    ['--lg-hairline', `0 0 0 .5px ${rgba(g.hairline)}`],
    ['--lg-shadow', shadow(g.shadow)],
    ['--lg-rim', g.rim],
    ['--lg-rim-w', T.rimWidth],
    ['--lg-opaque', rgba(g.opaque)],
    ['--lg-edge-strong', rgba(g.edgeStrong)],
  ];
}
const lines = (pairs, indent) => pairs.map(([k, v]) => `${indent}${k}:${v};`).join(NL);

// ── regions ─────────────────────────────────────────────────────────────────
function tokensRegion() {
  const sh = Object.entries(T.web.shared);
  const block = (s, extra) => [
    lines(Object.entries(T.web[s]), '  '),
    lines(glassVars(s), '  '),
    ...(extra ? [lines(extra, '  ')] : []),
    `  color-scheme:${s};`,
  ].join(NL);
  return [
    '/* Generated from design/prism2.tokens.json by tools/design/prism2.js. Edit the JSON, not this block. */',
    '',
    '/* ── Light: noon over a conservatory ─────────────────────────────────── */',
    ':root, .is-light{',
    block('light', sh),
    '}',
    '',
    '/* ── Dark: a glasshouse at dusk. A different recipe, not an inversion (Phase 0). ── */',
    '@media (prefers-color-scheme:dark){',
    '  :root:not([data-theme="light"]){',
    block('dark').split(NL).map(l => '  ' + l).join(NL),
    '  }',
    '}',
    ':root[data-theme="dark"], .is-dark{',
    block('dark'),
    '}',
    '/* A light window inside a dark page (and back) must win over the root\'s tokens: its own',
    '   declarations do, because custom properties inherit and an element\'s own always wins. */',
    '.is-light{ color-scheme:light; }',
    '.is-dark{ color-scheme:dark; }',
  ].join(NL);
}

// The capsule. The BODY (blur, fill, sheen) and the RIM are siblings that never overlap: the body is masked to the
// interior, the rim is masked to the outer band, so the rim's backdrop is the page, not the blurred body (the 2 Oct
// review measured the nested rim at 39 px of edge blur, the same as having none). Exactly one filter acts on any pixel.
function materialRegion() {
  return [
    '/* Generated from design/prism2.tokens.json by tools/design/prism2.js. Edit the JSON, not this block. */',
    '',
    '/* ── THE MATERIAL — Prism 2\'s capsule: body, rim, ring, edges, shadow ───────── */',
    '.gl{',
    '  position:relative;isolation:isolate;overflow:hidden;',
    '  --gl-fill:var(--lg-fill);',
    '  box-shadow:var(--lg-edge-top),var(--lg-edge-bot),var(--lg-hairline),var(--lg-shadow);',
    '}',
    '.gl--read{--gl-fill:var(--lg-fill-read);}',
    '.gl::before{                       /* the BODY: blur, fill and sheen, over the interior only. The outer rim band is',
    '                                      left out on purpose, so the rim below can see the page and not this layer. */',
    '  content:"";position:absolute;inset:0;border-radius:inherit;z-index:0;pointer-events:none;',
    '  padding:var(--lg-rim-w);',
    '  -webkit-backdrop-filter:var(--lg-backdrop);backdrop-filter:var(--lg-backdrop);',
    '  background:var(--lg-sheen),linear-gradient(var(--lg-warm),var(--lg-warm)),var(--gl-fill);',
    '  -webkit-mask:linear-gradient(#000 0 0) content-box;mask:linear-gradient(#000 0 0) content-box;',
    '}',
    '.gl::after{                        /* 1px ring: bright at the poles, near-invisible at the flanks.',
    '                                      A uniform border is the loudest tell of a fake. */',
    '  content:"";position:absolute;inset:0;border-radius:inherit;padding:1px;',
    '  background:var(--lg-ring);pointer-events:none;z-index:2;',
    '  -webkit-mask:linear-gradient(#000 0 0) content-box,linear-gradient(#000 0 0);',
    '  -webkit-mask-composite:xor;',
    '          mask:linear-gradient(#000 0 0) content-box exclude,linear-gradient(#000 0 0);',
    '}',
    '/* Content above the body, rim and ring. Only ELEMENTS can be lifted: loose text in a capsule would',
    '   paint under them, so every capsule\'s text sits in an element of its own. */',
    '.gl > *{position:relative;z-index:3;}',
    '.grim{                             /* the RIM: less blur, more brightness than the body, in the outer band only.',
    '                                      It is a sibling of the body and the two never overlap, so it lenses the page. */',
    '  position:absolute!important;inset:0;border-radius:inherit;pointer-events:none;z-index:1!important;',
    '  -webkit-backdrop-filter:var(--lg-rim);backdrop-filter:var(--lg-rim);',
    '  background:var(--gl-fill);',
    '  transform:scale(1.012);padding:var(--lg-rim-w);',
    '  -webkit-mask:linear-gradient(#000 0 0) content-box,linear-gradient(#000 0 0);',
    '  -webkit-mask-composite:xor;',
    '          mask:linear-gradient(#000 0 0) content-box exclude,linear-gradient(#000 0 0);',
    '}',
    '/* Anything INSIDE a glass capsule is raised, not glass again: one blur per pixel, or it goes to mud.',
    '   Same edges, a fill of its own, no backdrop filter. */',
    '.raised{',
    '  position:relative;background:var(--raised);',
    '  box-shadow:var(--lg-edge-top),var(--lg-edge-bot),var(--lg-hairline),0 1px 2px rgba(0,0,0,.05),0 8px 22px rgba(0,0,0,.06);',
    '}',
    '',
    modesCss('.gl', '.gl::before', '.grim'),
  ].join(NL);
}

// The settings Apple's Liquid Glass answers to, and the page's own fallbacks. Each rule is written twice, once under
// the real media query and once under :root[data-prism-mode~="name"], so a specimen can preview it with a switch.
function modesCss(cap, body, rim) {
  const rules = {
    'reduced-transparency': {
      query: '(prefers-reduced-transparency:reduce)',
      css: [
        `${body}{-webkit-backdrop-filter:none;backdrop-filter:none;background:var(--lg-opaque);}`,
        `${rim}{display:none!important;}`,
      ],
    },
    'contrast': {
      query: '(prefers-contrast:more)',
      css: [
        `:root{--txt-2:var(--txt-1);--txt-3:var(--txt-1);--accent-btn:#0050A8;--lg-ring:none;}`,
        `${body}{-webkit-backdrop-filter:none;backdrop-filter:none;background:var(--lg-opaque);}`,
        `${rim}{display:none!important;}`,
        `${cap}{box-shadow:0 0 0 1.5px var(--lg-edge-strong),var(--lg-shadow);}`,
        `${cap}::after{display:none;}`,
      ],
    },
    'forced-colors': {
      query: '(forced-colors:active)',
      css: [
        `${cap}{background:Canvas;border:1px solid CanvasText;box-shadow:none;}`,
        `${body},${cap}::after,${rim}{display:none!important;}`,
        `.field{display:none;}`,
      ],
    },
  };
  const out = ['/* ── Settings the material answers to (P1-8 / P2-4): reduced transparency, increased contrast, forced colours,',
    '   and a browser without backdrop-filter. Glass becomes an opaque surface with a visible edge. ── */'];
  for (const [name, r] of Object.entries(rules)) {
    out.push(`@media ${r.query}{`, ...r.css.map(l => '  ' + l), '}');
    out.push(...r.css.map(l => scoped(l, name)));
  }
  out.push('@supports not ((backdrop-filter:blur(1px)) or (-webkit-backdrop-filter:blur(1px))){',
    `  ${body}{background:var(--lg-opaque);}`, `  ${rim}{display:none!important;}`, '}');
  out.push(`:root[data-prism-mode~="no-backdrop"] ${body}{background:var(--lg-opaque);-webkit-backdrop-filter:none;backdrop-filter:none;}`,
    `:root[data-prism-mode~="no-backdrop"] ${rim}{display:none!important;}`);
  return out.join(NL);
}
function scoped(rule, name) {
  // `sel{decls}` -> `:root[data-prism-mode~="name"] sel{decls}`; a rule that starts with :root edits the root itself.
  const m = /^([^{]+)\{(.*)\}$/.exec(rule);
  const sels = m[1].split(',').map(s => s.trim()).map(s => s.startsWith(':root') ? s.replace(':root', `:root[data-prism-mode~="${name}"]`) : `:root[data-prism-mode~="${name}"] ${s}`);
  return `${sels.join(',')}{${m[2]}}`;
}

// Phase 0 keeps its own class names; it takes the numbers and the settings, and writes its own capsule rules by hand.
function phase0Region(s) { return lines(glassVars(s), '  '); }
function phase0Modes() { return modesCss('.gcap', '.gcap::before', '.grim'); }

// ── XAML ────────────────────────────────────────────────────────────────────
function xamlLabels(s) {
  const n = T.native[s];
  const row = (key, c) => `            <SolidColorBrush x:Key="${key}"${' '.repeat(Math.max(1, 22 - key.length))}Color="${argb(c)}" />`;
  return [
    '            <!-- Generated from design/prism2.tokens.json (tools/design/prism2.js). The chrome is the glass fill; the three labels',
    '                 are what text on it is drawn with: all three pass 4.5:1 on the glass colour assumed in the token file (P2-6). -->',
    row('AvChromeBrush', n.chrome),
    row('AvLabelBrush', n.label),
    row('AvLabelSecondaryBrush', n.labelSecondary),
    row('AvLabelTertiaryBrush', n.labelTertiary),
  ].join(NL);
}
function xamlGlass(s) {
  const g = T.glass[s];
  const stops = (list, rgb) => list.map(([o, a]) => `                <GradientStop Offset="${Number(o.toFixed(3))}"${' '.repeat(Math.max(1, 6 - String(Number(o.toFixed(3))).length))}Color="${argb([rgb[0], rgb[1], rgb[2], a])}" />`).join(NL);
  return [
    '            <!-- Generated from design/prism2.tokens.json (tools/design/prism2.js). Sheen, ring and shadow are the web recipe\'s numbers. -->',
    '            <LinearGradientBrush x:Key="AvGlassSheenBrush" StartPoint="0,0" EndPoint="0,1">',
    stops(g.sheen.stops, g.sheen.rgb),
    '            </LinearGradientBrush>',
    '            <LinearGradientBrush x:Key="AvGlassRingBrush">',
    stops(g.ring.stops, g.ring.rgb),
    '            </LinearGradientBrush>',
    `            <x:String x:Key="AvGlassShadowSpec">${g.shadow.map(([x, y, b, a]) => `${x},${y},${b},${a$(a).replace(/^\./, '0.')}`).join(';')}</x:String>`,
  ].join(NL);
}

// ── targets ─────────────────────────────────────────────────────────────────
const TARGETS = [
  { file: 'Aviary.html', kind: 'css', regions: { tokens: tokensRegion, material: materialRegion } },
  { file: 'Aviary-Phase0.html', kind: 'css', regions: { 'glass-light': () => phase0Region('light'), 'glass-dark': () => phase0Region('dark'), modes: phase0Modes } },
  { file: 'src/Aviary/Themes/LiquidGlass.xaml', kind: 'xaml', regions: { 'labels-light': () => xamlLabels('light'), 'labels-dark': () => xamlLabels('dark'), 'glass-light': () => xamlGlass('light'), 'glass-dark': () => xamlGlass('dark') } },
  { file: '../Prism2-Specimen.html', kind: 'css', optional: true, regions: { tokens: tokensRegion, material: materialRegion } },
];

function region(kind, name) {
  return kind === 'css'
    ? { re: new RegExp(`(/\\* prism2:begin ${name} \\*/)[\\s\\S]*?(/\\* prism2:end ${name} \\*/)`), wrap: (b, e, body) => `${b}${NL}${body}${NL}${e}` }
    : { re: new RegExp(`(<!-- prism2:begin ${name} -->)[\\s\\S]*?(<!-- prism2:end ${name} -->)`), wrap: (b, e, body) => `${b}${NL}${body}${NL}            ${e}` };
}

function run(mode) {
  let drift = 0;
  for (const t of TARGETS) {
    const p = path.resolve(ROOT, t.file);
    if (!fs.existsSync(p)) { if (t.optional) continue; console.error('MISSING FILE ' + t.file); drift++; continue; }
    let text = fs.readFileSync(p, 'utf8'); const crlf = text.includes('\r\n'); if (crlf) text = text.replace(/\r\n/g, '\n');
    let changed = false;
    for (const [name, make] of Object.entries(t.regions)) {
      const r = region(t.kind, name), m = r.re.exec(text);
      if (!m) { console.error(`NO MARKER  ${t.file}  prism2:begin ${name}`); drift++; continue; }
      const next = r.wrap(m[1], m[2], make());
      if (m[0] !== next) { if (mode === 'build') { text = text.replace(r.re, () => next); changed = true; console.log(`wrote   ${t.file}  ${name}`); } else { drift++; console.error(`DRIFT      ${t.file}  ${name}`); } }
    }
    if (changed) fs.writeFileSync(p, crlf ? text.replace(/\n/g, '\r\n') : text);
  }
  return drift;
}

if (require.main === module) {
  const mode = process.argv[2];
  if (mode === 'css') { console.log(tokensRegion() + NL + NL + materialRegion()); }
  else if (mode === 'build' || mode === 'check') {
    const d = run(mode);
    if (mode === 'check') { console.log(d ? `prism2 tokens: ${d} problem(s)` : 'prism2 tokens: every copy matches design/prism2.tokens.json'); process.exit(d ? 1 : 0); }
    else process.exit(d ? 1 : 0);
  } else { console.error('usage: node tools/design/prism2.js build|check|css'); process.exit(2); }
}
module.exports = { T, glassVars, tokensRegion, materialRegion, modesCss, xamlLabels, xamlGlass, argb, rgba };
