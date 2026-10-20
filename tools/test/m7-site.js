// Local M7 regression fixtures. No external files, requests, or accounts.
const http = require('http');
const port = 8877;
const video = `<!doctype html><meta charset="utf-8"><title>M7 video controls</title>
<style>body{font:18px system-ui;margin:32px;background:#eee;color:#222}
button{font:inherit;padding:12px;margin:6px}#player{position:relative;width:480px;height:270px}
video{width:480px;height:270px}#overlay{position:absolute;inset:0;background:transparent}
#away{margin-top:32px;padding:25px;background:white;width:430px}</style>
<h1>M7 video controls</h1><div id="player"><video id="v" autoplay muted playsinline></video>
<div id="overlay" aria-label="Video control overlay"></div></div>
<p><button id="cover">Remove overlay</button><button id="pause">Pause</button>
<button id="disable">Disallow PiP</button></p><p id="state"></p>
<div id="away">Unrelated page area: its menu should have no video action.</div>
<script>
const v=document.querySelector('video'),canvas=document.createElement('canvas');
canvas.width=480;canvas.height=270;
const ctx=canvas.getContext('2d');let frame=0;
setInterval(()=>{ctx.fillStyle='hsl('+frame+' 65% 38%)';ctx.fillRect(0,0,480,270);
ctx.fillStyle='white';ctx.font='32px system-ui';ctx.fillText('M7 frame '+frame++,30,140)},100);
v.srcObject=canvas.captureStream(10);v.play();
cover.onclick=()=>{overlay.hidden=!overlay.hidden;cover.textContent=overlay.hidden?'Add overlay':'Remove overlay'};
pause.onclick=()=>{if(v.paused)v.play();else v.pause();pause.textContent=v.paused?'Play':'Pause'};
disable.onclick=()=>{v.disablePictureInPicture=!v.disablePictureInPicture;
disable.textContent=v.disablePictureInPicture?'Allow PiP':'Disallow PiP'};
setInterval(()=>{state.textContent='Playing: '+!v.paused+' | PiP: '+!!document.pictureInPictureElement+
' | Overlay: '+!overlay.hidden+' | Allowed: '+!v.disablePictureInPicture},200);
</script>`;
http.createServer((req,res)=>{
  res.setHeader('Cache-Control','no-store');
  if(req.url==='/favicon.ico'){res.writeHead(404).end();return}
  res.setHeader('Content-Type','text/html; charset=utf-8');
  if(req.url==='/video'){res.end(video);return}
  if(req.url.startsWith('/data')){
    res.setHeader('Set-Cookie','m7_cookie=present; SameSite=Lax');
    res.end('<!doctype html><title>M7 stored data</title><h1>M7 stored data</h1><p id="state"></p><script>localStorage.setItem("m7_data","present");state.textContent="Cookie: "+document.cookie+"; local data: "+localStorage.m7_data</script>');
    return;
  }
  res.end('<!doctype html><title>M7 plain page</title><h1>M7 plain page</h1><p>A local page for tab and memory tests.</p><a href="/video">Video test</a> <a href="/data">Stored data test</a>');
}).listen(port,'127.0.0.1',()=>console.log('M7 fixtures on http://127.0.0.1:'+port));
