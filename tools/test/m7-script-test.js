// Behavioral tests of the shipped PiP selection script, with a minimal DOM fixture.
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert/strict');
const source = fs.readFileSync(path.resolve(__dirname,'../../src/Aviary/MainWindow.PictureInPicture.cs'),'utf8');
const script = source.match(/private const string PipScript = """\r?\n([\s\S]*?)\r?\n""";/)[1];
let passed=0;
async function check(name, {under=[],videos=[],pip=null,x=50,y=50,act='ask',enabled=true}, expected) {
  const context={document:{
    pictureInPictureEnabled:enabled,pictureInPictureElement:pip,
    elementsFromPoint:()=>under,querySelectorAll:()=>videos,
    exitPictureInPicture:()=>Promise.resolve()
  }};
  const actual=await vm.runInNewContext(script.replace('__X__',x).replace('__Y__',y).replace('__ACT__',act),context);
  assert.equal(actual,expected,name);passed++;console.log('PASS '+name);
}
const video = (extra={}) => ({tagName:'VIDEO',disablePictureInPicture:false,
  getAttribute:()=>null,readyState:4,videoWidth:640,clientWidth:640,clientHeight:360,
  paused:false,ended:false,requestPictureInPicture:()=>Promise.resolve(),...extra});
(async()=>{
  const v=video(),overlay={tagName:'DIV'};
  await check('video under player overlay',{under:[overlay,v]},'out');
  await check('unrelated page area does not choose a remote video',{under:[overlay],videos:[v]},'none');
  await check('Aa finds the playing video',{videos:[v],x:-1,y:-1},'out');
  await check('Aa ignores paused video',{videos:[video({paused:true})],x:-1,y:-1},'none');
  await check('Aa can leave a paused PiP video',{pip:video({paused:true}),x:-1,y:-1},'in');
  await check('site opt-out respected',{under:[video({disablePictureInPicture:true})]},'none');
  await check('video without loaded metadata not offered',{under:[video({readyState:0})]},'none');
  await check('disabled engine not offered',{under:[v],enabled:false},'off');
  await check('enter PiP',{under:[overlay,v],act:'go'},'entered');
  await check('leave PiP',{under:[v],pip:v,act:'go'},'left');
  await check('request refusal reported',{under:[video({requestPictureInPicture:()=>Promise.reject({name:'NotAllowedError'})})],act:'go'},'error NotAllowedError');
  console.log(passed+' passed');
})().catch(e=>{console.error(e);process.exitCode=1});
