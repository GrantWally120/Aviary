// Loopback-only fixtures for loading and per-tab audio tests.
const http = require('node:http');
const tone = `<!doctype html><meta charset="utf-8"><title>M8 audio test</title>
<style>body{font:20px system-ui;margin:40px}button{font:inherit;padding:20px}</style>
<h1>M8 audio test</h1><button id="tone">Start quiet tone</button><p id="state">Stopped</p>
<script>let context,oscillator,gain,on=false;
tone.onclick=async()=>{if(!context){context=new AudioContext();oscillator=context.createOscillator();
gain=context.createGain();gain.gain.value=.02;oscillator.frequency.value=220;
oscillator.connect(gain).connect(context.destination);oscillator.start();}
on=!on;if(on)await context.resume();else await context.suspend();
tone.textContent=on?'Stop quiet tone':'Start quiet tone';state.textContent=on?'Playing':'Stopped';};</script>`;
http.createServer((req,res)=>{
  res.setHeader('Cache-Control','no-store');
  if(req.url==='/favicon.ico'){res.writeHead(404).end();return;}
  res.setHeader('Content-Type','text/html; charset=utf-8');
  if(req.url==='/slow'){setTimeout(()=>res.end('<!doctype html><title>M8 slow page</title><h1>Loaded</h1><a href="/tone">Audio test</a>'),6000);return;}
  if(req.url==='/memory'){res.end('<!doctype html><title>M8 memory test</title><h1>Memory fixture</h1><script>window.buffers=Array.from({length:64},()=>{const a=new Uint8Array(1024*1024);a.fill(123);return a});setInterval(()=>document.title="M8 memory test",1000)</script>');return;}
  if(req.url==='/tone'){res.end(tone);return;}
  res.end('<!doctype html><title>M8 fixture</title><h1>M8 fixture</h1><a href="/slow">Slow page</a> <a href="/tone">Audio test</a>');
}).listen(8879,'127.0.0.1',()=>console.log('M8 fixtures: http://127.0.0.1:8879'));
