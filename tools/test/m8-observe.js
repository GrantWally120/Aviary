// Read-only CDP snapshot for isolated Aviary test instances. No navigation or input.
const port = process.env.CDP_PORT || 9417;
async function read(target, expression) {
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve,reject)=>{ws.onopen=resolve;ws.onerror=reject;});
  try {
    return await new Promise((resolve,reject)=>{
      const timer=setTimeout(()=>reject(Error('Timed out: '+target.id)),5000);
      ws.onmessage=e=>{const m=JSON.parse(e.data);if(m.id===1){clearTimeout(timer);resolve(m.result?.result?.value);}};
      ws.send(JSON.stringify({id:1,method:'Runtime.evaluate',params:{expression,returnByValue:true}}));
    });
  } finally { ws.close(); }
}
(async()=>{
  const pages=(await (await fetch('http://127.0.0.1:'+port+'/json/list')).json()).filter(p=>p.type==='page');
  const data=await Promise.all(pages.map(async p=>({id:p.id,...await read(p,
    '({title:document.title,origin:performance.timeOrigin,visibility:document.visibilityState,radius:getComputedStyle(document.documentElement).getPropertyValue("--tile-radius"),liveTheme:!!document.getElementById("aviary-live-theme"),loading:document.readyState})')})));
  console.log(JSON.stringify(data,null,2));
})().catch(e=>{console.error(e.message);process.exitCode=1;});
