// Glass inventory, as the 2 Oct 2026 review counted it: every element (and its ::before / ::after) whose computed
// backdrop-filter is not "none", and whether an ANCESTOR ELEMENT also has one. A nested filter re-filters its parent's
// already blurred image, so blur and saturation stack. Prism 2's rule: no filter has a filtered ancestor.
//
//   node tools/test/prism/inventory.js <file|url> [--scheme light|dark] [--width 1280] [--height 800] [--scroll 0]
const path = require('node:path');
const { launch } = require('./lib');

const COLLECT = `(function(){
  function has(cs){ var f = cs.backdropFilter || cs.webkitBackdropFilter; return f && f !== 'none' ? f : ''; }
  var rows = [], all = document.querySelectorAll('*');
  all.forEach(function(el){
    var cs = getComputedStyle(el), spots = [['', cs]];
    ['::before', '::after'].forEach(function(p){ spots.push([p, getComputedStyle(el, p)]); });
    spots.forEach(function(s){
      var f = has(s[1]); if (!f) return;
      if (s[0] && s[1].content === 'none') return;
      var r = el.getBoundingClientRect(); if (!r.width || !r.height) return;
      var anc = []; for (var a = el.parentElement; a; a = a.parentElement) { var af = has(getComputedStyle(a)); if (af) anc.push(a.tagName.toLowerCase() + (a.className ? '.' + String(a.className).trim().split(/\\s+/)[0] : '')); }
      // for a pseudo, the element ITSELF is an ancestor-level box too if it carries a filter of its own
      if (s[0] && has(cs)) anc.push('(own element)');
      rows.push({ el: el.tagName.toLowerCase() + (el.className && typeof el.className === 'string' ? '.' + el.className.trim().split(/\\s+/).join('.') : '') + s[0], filter: f.slice(0, 60), ancestors: anc });
    });
  });
  return rows;
})()`;

async function inventory(page) {
  const rows = await page.eval(COLLECT);
  const nested = rows.filter(r => r.ancestors.length);
  return { total: rows.length, nested: nested.length, nestedRows: nested.slice(0, 12), rows };
}
module.exports = { inventory };

if (require.main === module) {
  const args = process.argv.slice(2), get = (k, d) => { const i = args.indexOf('--' + k); return i >= 0 ? args[i + 1] : d; };
  const target = args.find((a, i) => !a.startsWith('--') && !(args[i - 1] || '').startsWith('--'));
  (async () => {
    const url = /^(https?|file):/.test(target) ? target : 'file:///' + path.resolve(target).replace(/\\/g, '/');
    const b = await launch({ port: +get('port', 9555) });
    const p = await b.newPage({ width: +get('width', 1280), height: +get('height', 800), scheme: get('scheme', 'light') });
    await p.goto(url, 1000);
    await p.eval(`window.scrollTo(0, ${+get('scroll', 0)}); 0`);
    const r = await inventory(p);
    console.log(`${path.basename(target)}: ${r.total} glass surfaces (backdrop-filter), ${r.nested} nested inside another`);
    r.nestedRows.forEach(n => console.log('   NESTED', n.el, '<-', n.ancestors.join(' > ')));
    await p.close(); await b.close(); process.exit(r.nested ? 1 : 0);
  })().catch(e => { console.error(e); process.exit(1); });
}
