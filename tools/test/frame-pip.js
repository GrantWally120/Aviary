// Picture in picture from INSIDE a frame - an embedded player (M6 stage 2, "Mute tabs in the
// background"): puts a same-site iframe of expsite.js /pipsound into the given tab and calls its pip()
// in the FRAME's own context, with a user gesture. Prints what the frame and the top page each say
// about picture in picture - the top page cannot see a frame's, which is why the mute cannot either.
//   CDP_PORT=9401 node frame-pip.js <tab id prefix>
const PORT = process.env.CDP_PORT || 9222;
const want = process.argv[2] || '';
const sleep = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  const t = (await (await fetch(`http://127.0.0.1:${PORT}/json/list`)).json())
    .find(x => x.type === 'page' && (want ? x.id.startsWith(want) : /8797/.test(x.url)));
  if (!t) { console.log(JSON.stringify({ error: 'no tab ' + want })); return; }
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  let id = 0; const pending = new Map(); const contexts = [];
  ws.onmessage = ev => {
    const m = JSON.parse(ev.data);
    if (m.method === 'Runtime.executionContextCreated') contexts.push(m.params.context);
    if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); }
  };
  await new Promise(r => ws.onopen = r);
  const send = (method, params = {}) => new Promise(r => { const my = ++id; pending.set(my, r); ws.send(JSON.stringify({ id: my, method, params })); });
  const ev = async (e, extra = {}) => {
    const m = await send('Runtime.evaluate', { expression: e, returnByValue: true, awaitPromise: true, userGesture: true, ...extra });
    return m.result?.result?.value ?? m.result?.exceptionDetails?.exception?.description;
  };
  const mainId = (await send('Page.getFrameTree')).result.frameTree.frame.id;
  await ev("(function(){var f=document.createElement('iframe');f.id='emb';f.src='/pipsound';f.width=400;f.height=260;f.allow='picture-in-picture; autoplay';document.body.appendChild(f);return true;})()");
  await sleep(1500);
  await send('Runtime.enable'); await sleep(500);
  const ctx = contexts.find(c => c.auxData && c.auxData.frameId !== mainId && c.auxData.isDefault && /8797/.test(c.origin));
  const out = { frameContext: !!ctx };
  if (ctx) {
    out.framePip = await ev('pip()', { contextId: ctx.id });
    out.frameSaysPip = await ev('!!document.pictureInPictureElement', { contextId: ctx.id });
  }
  out.topSaysPip = await ev('!!(document.pictureInPictureElement || (window.documentPictureInPicture && documentPictureInPicture.window))');
  ws.close();
  console.log(JSON.stringify(out));
})();
