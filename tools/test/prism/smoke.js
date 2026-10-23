// The page-level checks a Prism 2 page must pass: no console errors, no request to any other host, no horizontal scroll
// at 390 px, and still readable (text present) with each settings mode on.
//
//   node tools/test/prism/smoke.js <file|url>
const path = require('node:path');
const { launch } = require('./lib');

(async () => {
  const target = process.argv[2];
  const url = /^(https?|file):/.test(target) ? target : 'file:///' + path.resolve(target).replace(/\\/g, '/');
  const b = await launch({ port: 9555 });
  let bad = 0;
  const check = (name, ok, extra) => { console.log((ok ? 'PASS  ' : 'FAIL  ') + name + (extra ? '  -- ' + extra : '')); if (!ok) bad++; };
  for (const scheme of ['light', 'dark']) for (const [w, h] of [[1280, 800], [390, 844]]) {
    const p = await b.newPage({ width: w, height: h, scheme });
    await p.goto(url, 1500);
    const errors = p.console.filter(l => /^(error|exception|warning: .*(csp|refused))/i.test(l));
    check(`${scheme} ${w}px: no console errors`, errors.length === 0, errors[0]);
    const ext = await p.eval(`performance.getEntriesByType('resource').map(function(r){return r.name}).filter(function(n){return !/^(file|data|blob):/.test(n)})`);
    check(`${scheme} ${w}px: no request to another host`, ext.length === 0, ext.slice(0, 3).join(' '));
    const loose = await p.eval(`Array.prototype.filter.call(document.querySelectorAll('.gl,.gcap'),function(e){return Array.prototype.some.call(e.childNodes,function(n){return n.nodeType===3&&n.nodeValue.trim()}) }).map(function(e){return (e.className+'').split(' ').slice(0,2).join('.')+': '+Array.prototype.find.call(e.childNodes,function(n){return n.nodeType===3&&n.nodeValue.trim()}).nodeValue.trim().slice(0,24)})`);
    check(`${scheme} ${w}px: no loose text inside a glass capsule`, loose.length === 0, loose.slice(0, 3).join(' | '));
    const over = await p.eval(`document.documentElement.scrollWidth - document.documentElement.clientWidth`);
    check(`${scheme} ${w}px: no horizontal scroll`, over <= 1, over + 'px over');
    await p.close();
  }
  // settings modes: the page must stay legible and keep its text with each one forced on
  for (const [label, media, attr] of [
    ['reduced transparency', { 'prefers-reduced-transparency': 'reduce' }, 'reduced-transparency'],
    ['increased contrast', { 'prefers-contrast': 'more' }, 'contrast'],
    ['forced colours', { 'forced-colors': 'active' }, 'forced-colors'],
    ['reduced motion', { 'prefers-reduced-motion': 'reduce' }, null],
  ]) {
    const p = await b.newPage({ width: 1280, height: 800, scheme: 'light', media });
    await p.goto(url, 1200);
    const chars = await p.eval(`document.body.innerText.length`);
    const blurs = await p.eval(`Array.prototype.filter.call(document.querySelectorAll('.gl,.gcap'),function(e){var c=getComputedStyle(e,'::before');return c.display!=='none'&&c.backdropFilter&&c.backdropFilter!=='none'}).length`);
    const expectNone = attr !== null;
    check(`${label}: page keeps its text (${chars} chars)`, chars > 500);
    if (expectNone) check(`${label}: glass bodies drop their backdrop blur`, blurs === 0, blurs + ' still blur');
    await p.close();
  }
  await b.close();
  console.log(bad ? `${bad} check(s) failed` : 'all page checks passed');
  process.exit(bad ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
