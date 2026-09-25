// Does THIS engine preload a page at all, and how? (M6 stage 2, measured before building.)
// Tries the three ways a page can ask - speculation rules on hover (document rules, "moderate"),
// speculation rules for a list (immediate), <link rel=prefetch> - and counts what reached expsite.js.
//   CDP_PORT=9333 node preload-probe.js
const PORT = process.env.CDP_PORT || 9222;
const BASE = 'http://127.0.0.1:8797';
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
  const targets = async since => (await hits()).slice(since).filter(h => h.url.startsWith('/target'));
  const out = {};

  await send('Page.navigate', { url: BASE + '/hover' }); await sleep(1500);
  out.supports = await ev("HTMLScriptElement.supports ? HTMLScriptElement.supports('speculationrules') : 'no supports()'");

  // 1. document rules, moderate eagerness: the engine itself watches for a hover (~200 ms).
  let since = (await hits()).length;
  await ev(`(function(){var s=document.createElement('script');s.type='speculationrules';
    s.textContent=JSON.stringify({prefetch:[{where:{href_matches:'/*'},eagerness:'moderate'}]});document.head.appendChild(s);return true;})()`);
  const box = await ev("JSON.stringify(document.getElementById('same').getBoundingClientRect())");
  const r = JSON.parse(box);
  await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: r.x + 10, y: r.y + r.height / 2 });
  await sleep(1500);
  out.hoverDocumentRules = await targets(since);

  // 2. a list rule, immediate.
  since = (await hits()).length;
  await ev(`(function(){var s=document.createElement('script');s.type='speculationrules';
    s.textContent=JSON.stringify({prefetch:[{source:'list',urls:['/target?n=list'],eagerness:'immediate'}]});document.head.appendChild(s);return true;})()`);
  await sleep(1500);
  out.listImmediate = await targets(since);

  // 3. <link rel=prefetch>.
  since = (await hits()).length;
  await ev("(function(){var l=document.createElement('link');l.rel='prefetch';l.href='/target?n=link';document.head.appendChild(l);return true;})()");
  await sleep(1500);
  out.linkPrefetch = await targets(since);

  // 4. the controls: an ordinary fetch() and an ordinary navigation, which DO pass Aviary's filter.
  since = (await hits()).length;
  await ev("fetch('/target?n=fetch').then(function(){return true})");
  await sleep(800);
  out.plainFetch = await targets(since);

  // 5. a cross-site list rule (127.0.0.2) - Chromium's rules differ for another site.
  since = (await hits()).length;
  await ev(`(function(){var s=document.createElement('script');s.type='speculationrules';
    s.textContent=JSON.stringify({prefetch:[{source:'list',urls:['http://127.0.0.2:8797/target?n=cross'],eagerness:'immediate'}]});document.head.appendChild(s);return true;})()`);
  await sleep(1500);
  out.crossSiteList = await targets(since);

  // 6. prerender (a whole hidden page), same site.
  since = (await hits()).length;
  await ev(`(function(){var s=document.createElement('script');s.type='speculationrules';
    s.textContent=JSON.stringify({prerender:[{source:'list',urls:['/target?n=prerender'],eagerness:'immediate'}]});document.head.appendChild(s);return true;})()`);
  await sleep(2000);
  out.prerender = await targets(since);

  // 7. can Aviary's filter STOP one? (only with AVIARY_GPC_PROBE=1 - the probe build answers 204
  //    for any URL with "probe-block" in it.) A prefetch and, as the control, a fetch().
  since = (await hits()).length;
  await ev(`(function(){var s=document.createElement('script');s.type='speculationrules';
    s.textContent=JSON.stringify({prefetch:[{source:'list',urls:['/target?n=probe-block-prefetch'],eagerness:'immediate'}]});document.head.appendChild(s);return true;})()`);
  await ev("fetch('/target?n=probe-block-fetch').then(function(r){return r.status}).catch(function(e){return String(e)})");
  await sleep(1500);
  out.blockAttempt = await targets(since);

  ws.close();
  console.log(JSON.stringify(out, null, 1));
})();
