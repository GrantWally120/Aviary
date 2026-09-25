// Bug 91 (M6 stage 2): a website's speculation rules make the engine fetch pages OUTSIDE Aviary's
// request filter - no Shields, no GPC, no tracking-parameter stripping (measured, preload-probe.js).
// This counts what the three kinds of rules make reach expsite.js, with and without a guard script.
//   CDP_PORT=9333 node spec-probe.js [guard-file.js]
// With a guard file, it is installed as a script that runs before any of the page's own
// (Page.addScriptToEvaluateOnNewDocument - what Aviary's own document-created scripts are).
const fs = require('fs');
const PORT = process.env.CDP_PORT || 9222;
const BASE = 'http://127.0.0.1:8797';
const guard = process.argv[2] ? fs.readFileSync(process.argv[2], 'utf8') : null;
const sleep = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  const t = (await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json()).find(x => x.type === 'page');
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  let id = 0; const pending = new Map();
  ws.onmessage = ev => { const m = JSON.parse(ev.data); if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } };
  await new Promise(r => ws.onopen = r);
  const send = (method, params = {}) => new Promise(r => { const my = ++id; pending.set(my, r); ws.send(JSON.stringify({ id: my, method, params })); });
  const ev = async e => (await send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise: true })).result?.result?.value;
  const hits = async () => (await (await fetch(BASE + '/hits')).json());
  const since = async () => (await hits()).length;
  const got = async (from, re) => (await hits()).slice(from).filter(h => re.test(h.url)).map(h => h.url + (h.purpose ? ' [' + h.purpose + ']' : '') + (h.dest ? ' dest=' + h.dest : ''));
  const hover = async sel => {
    const r = JSON.parse(await ev(`JSON.stringify(document.querySelector(${JSON.stringify(sel)}).getBoundingClientRect())`));
    await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: r.x + 5, y: r.y + r.height / 2 });
  };
  const out = { guard: !!guard };
  if (guard) { await send('Page.enable'); await send('Page.addScriptToEvaluateOnNewDocument', { source: guard }); }

  let from = await since();
  await send('Page.navigate', { url: BASE + '/spec-static' }); await sleep(2000);
  out.supports = await ev("HTMLScriptElement.supports('speculationrules')");
  out.rulesLeftInPage = await ev("document.querySelectorAll('script[type=speculationrules]').length");
  await hover('#doc'); await sleep(1500);
  out.staticHtml = await got(from, /^\/target/);

  from = await since();
  await send('Page.navigate', { url: BASE + '/spec-header' }); await sleep(2500);
  out.headerRules = await got(from, /^\/(target|rules\.json)/);

  from = await since();
  await send('Page.navigate', { url: BASE + '/hover' }); await sleep(1200);
  await ev(`(function(){var s=document.createElement('script');s.type='speculationrules';
    s.textContent=JSON.stringify({prefetch:[{source:'list',urls:['/target?n=dynamic'],eagerness:'immediate'},{where:{href_matches:'/*'},eagerness:'moderate'}]});document.head.appendChild(s);return true;})()`);
  await sleep(300); await hover('#same'); await sleep(1500);
  out.addedByScript = await got(from, /^\/target/);

  from = await since();
  await ev("(function(){var l=document.createElement('link');l.rel='prefetch';l.href='/target?n=link-control';document.head.appendChild(l);return true;})()");
  await sleep(1200);
  out.linkPrefetchControl = await got(from, /^\/target/);

  // The second look (25 Sep): the engine reads a script as rules when it is added OR when something
  // is put inside it later, and the rules then last while the element is in the page, whatever its
  // type says afterwards. Each way in gets its own /target?n=, so the hits say which one loaded.
  const rules = n => JSON.stringify(JSON.stringify({ prefetch: [{ source: 'list', urls: ['/target?n=' + n], eagerness: 'immediate' }] }));
  const script = (n, type) => `var s=document.createElement('script');${type ? "s.type='" + type + "';" : ''}s.textContent=${rules(n)};`;
  const LATE = {
    // added as text/plain; AFTER the page's observers have run, made rules and given a text node
    lateType: n => `new Promise(function(done){${script(n, 'text/plain')}document.head.appendChild(s);setTimeout(function(){s.type='speculationrules';s.appendChild(document.createTextNode(' '));done(true);},100);})`,
    // added EMPTY with no type; later made rules and filled in
    lateEmpty: n => `new Promise(function(done){var s=document.createElement('script');document.head.appendChild(s);setTimeout(function(){s.type='speculationrules';s.textContent=${rules(n)};done(true);},100);})`,
    // a real rules script, its type changed AWAY in the same task it was added in
    typeAway: n => `(function(){${script(n, 'speculationrules')}document.head.appendChild(s);s.type='text/plain';return true;})()`,
    // inside shadow roots made by script
    shadowOpen: n => `(function(){var h=document.createElement('div');document.body.appendChild(h);var r=h.attachShadow({mode:'open'});${script(n, 'speculationrules')}r.appendChild(s);return true;})()`,
    shadowClosed: n => `(function(){var h=document.createElement('div');document.body.appendChild(h);var r=h.attachShadow({mode:'closed'});${script(n, 'speculationrules')}r.appendChild(s);return true;})()`,
    // a closed shadow root made with ANOTHER frame's attachShadow (an about:blank iframe's)
    otherRealm: n => `(function(){var f=document.createElement('iframe');document.body.appendChild(f);var h=document.createElement('div');document.body.appendChild(h);var r=f.contentWindow.Element.prototype.attachShadow.call(h,{mode:'closed'});${script(n, 'speculationrules')}r.appendChild(s);return true;})()`,
    // shadow roots declared in the HTML itself (expsite.js /spec-dsd)
    dsdOpen: null,
    dsdClosed: null,
  };
  out.late = {};
  for (const [name, js] of Object.entries(LATE)) {
    const n = name + '-' + Date.now();
    if (js) { await send('Page.navigate', { url: BASE + '/hover?c=' + name }); await sleep(1500); }
    from = await since();
    if (js) { await ev(js(n)); }
    else { await send('Page.navigate', { url: BASE + '/spec-dsd?mode=' + (name === 'dsdOpen' ? 'open' : 'closed') + '&n=' + n }); }
    await sleep(1800);
    out.late[name] = (await got(from, new RegExp('^/target\\?n=' + n))).length;
  }

  ws.close();
  console.log(JSON.stringify(out, null, 1));
})();
