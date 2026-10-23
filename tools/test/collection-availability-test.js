// Exercise the Collection's shipped probe and summary against full/partial file inventories.
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const folder = path.resolve(__dirname, '../../src/Aviary/Library');
const html = fs.readFileSync(path.join(folder, 'index.html'), 'utf8');
new vm.Script(html.match(/<script(?: nonce="[^"]*")?>([\s\S]*?)<\/script>/)[1]);
const summary = html.slice(html.indexOf('function renderStats('), html.indexOf('function render(){'));
const probe = html.slice(html.indexOf('var MISSING = {};'), html.indexOf('function bindTimeline(){'));
const catalog = JSON.parse(fs.readFileSync(path.join(folder, 'catalog.json'), 'utf8'));
let passed = 0;
async function check(name, available, response = 'normal', showing = 'all') {
  const nodes = {stats:{innerHTML:''}, availability:{textContent:''}, q:{value:''}};
  const cards = new Map(catalog.exhibits.map(e => [e.id, {
    missing:false, status:e.status, pill:{},
    classList:{add(){ cards.get(e.id).missing = true; }},
    setAttribute(key,value){ if(key === 'data-status') this.status = value; },
    querySelector(){ return this.pill; }
  }]));
  let filters = 0, gets = 0;
  const context = vm.createContext({CAT:catalog, openId:null, showing, STATUS:{missing:['missing','Not in this copy']},
    href: p => p, countUp(){}, filter(){ filters++; },
    document:{getElementById:id=>nodes[id], querySelectorAll:selector=>{
      const id = selector.match(/data-id="([^"]+)"/)[1]; return [cards.get(id)];
    }},
    fetch: async (url, options) => {
      const isHead = options.method === 'HEAD';
      if (!isHead) gets++;
      if (response === 'failure') throw Error('unavailable');
      if (isHead && response === 'head-throws') throw Error('HEAD not supported');
      if (isHead && response === 'head-405') return {ok:false,status:405};
      const e = catalog.exhibits.find(e=>e.open === url);
      const ok = available(e); return {ok,status:ok?200:404};
    }
  });
  vm.runInContext(summary + '\n' + probe, context);
  await vm.runInContext('probe()', context);
  const included = catalog.exhibits.filter(e=>response !== 'failure' && available(e));
  const missing = catalog.exhibits.length-included.length;
  assert.equal([...cards.values()].filter(c=>c.missing).length, missing, name);
  assert.equal(filters, showing === 'all' ? 0 : 1, name + ' refreshes an active filter and preserves initial card reveals');
  assert.match(nodes.availability.textContent, missing ? new RegExp('^'+included.length+' of 54') : /^All 54/);
  const runs = included.filter(e=>e.status !== 'landing').length;
  assert.match(nodes.stats.innerHTML, new RegExp('data-to="'+runs+'">0</b><span>Run right here'));
  if (response.startsWith('head-')) assert.equal(gets, catalog.exhibits.length, name + ' retries GET');
  assert.ok([...cards.values()].filter(c=>c.missing).every(c=>c.pill.innerHTML.includes('Not in this copy')));
  passed++; console.log('PASS ' + name);
}
(async()=>{
  await check('full Collection', ()=>true);
  await check('source-only Collection', e=>e.status === 'landing');
  await check('one absent file', e=>e.id !== catalog.exhibits[0].id);
  await check('HEAD 405 falls back to GET', ()=>true, 'head-405');
  await check('HEAD failure falls back to GET', ()=>true, 'head-throws');
  await check('both requests fail', ()=>true, 'failure');
  await check('active Plays here filter refreshes after probing', e=>e.status === 'landing', 'normal', 'runs');
  console.log(passed+' availability checks passed');
})().catch(error=>{console.error(error);process.exitCode=1;});
