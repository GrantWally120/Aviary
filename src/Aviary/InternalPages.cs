namespace Aviary;

/// <summary>
/// Aviary's own pages. Real browsers render their settings as a web page too — chrome://,
/// edge:// and brave:// are all HTML — so this is the authentic shape rather than a
/// shortcut, and it means Settings can be redesigned without touching any XAML.
///
/// Both pages talk back to C# through window.chrome.webview.postMessage. The host trusts
/// a message only because these documents are navigated to by NavigateToString and never
/// from the network.
/// </summary>
internal static class InternalPages
{
    private const string SharedCss = """
  :root{
    color-scheme: light dark;
    --bg:#FFFFFF; --panel:#FFFFFF; --panel-2:#F6F6F7;
    --ink:rgba(0,0,0,.85); --ink-2:rgba(0,0,0,.50); --ink-3:rgba(0,0,0,.32);
    --line:rgba(0,0,0,.10); --line-soft:rgba(0,0,0,.06);
    --fill:rgba(0,0,0,.045); --fill-hover:rgba(0,0,0,.06);
    --accent:#007AFF; --sel:rgba(0,122,255,.12);
  }
  @media (prefers-color-scheme: dark){
    :root{
      --bg:#1E1E1E; --panel:#2C2C2E; --panel-2:#252527;
      --ink:rgba(255,255,255,.92); --ink-2:rgba(255,255,255,.55); --ink-3:rgba(255,255,255,.34);
      --line:rgba(255,255,255,.12); --line-soft:rgba(255,255,255,.07);
      --fill:rgba(255,255,255,.06); --fill-hover:rgba(255,255,255,.10);
      --accent:#0A84FF; --sel:rgba(10,132,255,.20);
    }
  }
  *{box-sizing:border-box;}
  html,body{height:100%;margin:0;}
  body{
    background:var(--bg); color:var(--ink);
    font:400 14px/1.6 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;
    letter-spacing:-.006em;
    -webkit-user-select:none; user-select:none;
  }
  ::selection{background:var(--sel);}
  ::-webkit-scrollbar{width:10px;height:10px;}
  ::-webkit-scrollbar-thumb{background:rgba(128,128,128,.32);border-radius:99px;border:3px solid transparent;background-clip:content-box;}
  ::-webkit-scrollbar-track{background:transparent;}
""";

    // ══ START PAGE ═════════════════════════════════════════════════════════
    // Phase 4 replaces this with the real Safari Start Page. Until then it stays
    // deliberately quiet — a wordy placeholder reads as an unfinished app.

    public const string StartPage = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Aviary</title>
<style>
""" + SharedCss + """
  body{display:grid;place-items:center;}
  main{width:min(600px,100% - 72px);text-align:center;}
  .dome{width:76px;height:auto;margin:0 auto 22px;display:block;opacity:.92;}
  h1{font-size:40px;font-weight:600;letter-spacing:-.028em;margin:0;}
  .sub{margin:10px 0 0;color:var(--ink-2);font-size:15px;}
  .links{display:flex;flex-wrap:wrap;gap:8px;justify-content:center;margin:34px 0 0;}
  .links a{
    text-decoration:none;padding:9px 15px;border-radius:9px;color:var(--ink);
    background:var(--fill);font-size:14px;font-weight:500;
    transition:background-color 140ms cubic-bezier(.25,.1,.25,1),transform 140ms cubic-bezier(.32,.72,0,1);
  }
  .links a:hover{background:var(--fill-hover);transform:translateY(-1px);}
  .foot{margin-top:44px;font-size:12px;color:var(--ink-3);letter-spacing:.02em;}
  .foot b{font-weight:600;color:var(--ink-2);}
</style>
</head>
<body>
  <main>
    <svg class="dome" viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <path d="M3 20V12.6A9 9 0 0 1 21 12.6V20" stroke="var(--accent)" stroke-width="1.5" stroke-linecap="round"/>
      <path d="M12 3.6V20M7.6 5.1V20M16.4 5.1V20M3 14.4h18" stroke="var(--accent)" stroke-width=".9" stroke-linecap="round" opacity=".5"/>
      <path d="M2.2 20h19.6" stroke="var(--accent)" stroke-width="1.5" stroke-linecap="round"/>
    </svg>
    <h1>Aviary</h1>
    <p class="sub">Start typing above.</p>
    <div class="links">
      <a href="https://en.wikipedia.org">Wikipedia</a>
      <a href="https://news.ycombinator.com">Hacker News</a>
      <a href="https://learn.microsoft.com">Microsoft Learn</a>
      <a href="https://developer.mozilla.org">MDN</a>
      <a href="https://github.com">GitHub</a>
    </div>
    <div class="foot" id="foot"></div>
  </main>
<script>
  var ua = navigator.userAgent;
  var safari = ua.indexOf('Safari') !== -1 && ua.indexOf('Chrome') === -1;
  document.getElementById('foot').innerHTML =
    'Alpha build 2 &middot; ' + (safari
      ? 'identifying as <b>Safari</b>'
      : '<b>not</b> identifying as Safari');
</script>
</body>
</html>
""";

    // ══ SETTINGS ═══════════════════════════════════════════════════════════
    // Data-driven on purpose. Adding Phase 5's Experimental menu means appending one
    // object to SECTIONS — no new markup, no new CSS.

    public const string Settings = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Settings</title>
<style>
""" + SharedCss + """
  body{display:grid;grid-template-columns:248px minmax(0,1fr);}
  nav{
    border-right:1px solid var(--line-soft);overflow-y:auto;padding:22px 12px 40px;
    background:var(--panel-2);
  }
  .brand{display:flex;align-items:center;gap:10px;padding:0 10px 18px;}
  .brand svg{width:20px;height:20px;}
  .brand b{font-size:15px;font-weight:600;letter-spacing:-.014em;}
  nav button{
    display:flex;align-items:center;gap:11px;width:100%;
    border:0;background:transparent;color:var(--ink);cursor:default;
    font:500 13.5px/1 inherit;letter-spacing:-.004em;
    padding:0 10px;height:34px;border-radius:7px;text-align:left;
    transition:background-color 120ms cubic-bezier(.25,.1,.25,1);
  }
  nav button:hover{background:var(--fill);}
  nav button[aria-current="true"]{background:var(--sel);color:var(--accent);}
  nav button svg{width:16px;height:16px;flex:0 0 auto;opacity:.85;}
  nav hr{border:0;border-top:1px solid var(--line-soft);margin:10px 10px;}

  main{overflow-y:auto;padding:34px 0 80px;}
  .pane{width:min(680px,100% - 88px);margin-inline:auto;display:none;}
  .pane.on{display:block;}
  h1{font-size:26px;font-weight:600;letter-spacing:-.022em;margin:0 0 6px;}
  .lede{color:var(--ink-2);margin:0 0 26px;font-size:14px;}
  .group{
    background:var(--panel);border:1px solid var(--line-soft);border-radius:12px;
    margin-bottom:18px;
    /* NOT overflow:hidden — a dropdown opening near the bottom row has to be able to
       escape the card, or its last options get clipped off. */
  }
  .row{
    display:flex;align-items:center;gap:18px;padding:14px 18px;min-height:60px;
    position:relative;
  }
  /* The open row floats above its neighbours so the popup is never covered. */
  .row:has(.sel__btn[aria-expanded="true"]){z-index:30;}
  .row + .row{border-top:1px solid var(--line-soft);}
  .row__t{flex:1;min-width:0;}
  .row__t b{display:block;font-weight:500;font-size:14px;}
  .row__t span{display:block;color:var(--ink-2);font-size:12.5px;line-height:1.5;margin-top:3px;}
  /* OUR OWN DROPDOWN, not a native <select>.
     A native select's popup is drawn by Windows, not by the page: in dark mode Chromium
     paints that list white while the text stays light, so you get a white box with only
     the hovered row legible. No CSS reaches inside it — and even if it did, an OS list
     would never look like Safari. So the popup is ours. */
  .sel{position:relative;flex:0 0 auto;}
  .sel__btn{
    display:flex;align-items:center;gap:10px;
    border:1px solid var(--line);background:var(--fill);color:var(--ink);
    font:500 13px/1 inherit;padding:9px 11px;border-radius:7px;cursor:default;
    min-width:150px;text-align:left;
    transition:background-color 120ms cubic-bezier(.25,.1,.25,1),border-color 120ms cubic-bezier(.25,.1,.25,1);
  }
  .sel__btn:hover{background:var(--fill-hover);}
  .sel__btn[aria-expanded="true"]{border-color:var(--accent);}
  .sel__btn span{flex:1;min-width:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
  .sel__btn svg{width:10px;height:7px;flex:0 0 auto;opacity:.55;
    transition:transform 160ms cubic-bezier(.32,.72,0,1);}
  .sel__btn[aria-expanded="true"] svg{transform:rotate(180deg);}
  .sel__pop{
    position:absolute;top:calc(100% + 6px);right:0;z-index:40;min-width:100%;
    background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:5px;
    box-shadow:0 1px 2px rgba(0,0,0,.16),0 10px 30px rgba(0,0,0,.28);
    display:grid;gap:1px;
    transform-origin:top right;
    animation:pop 150ms cubic-bezier(.32,.72,0,1);
  }
  @keyframes pop{from{opacity:0;transform:scale(.96) translateY(-4px);}to{opacity:1;transform:none;}}
  .sel__opt{
    display:flex;align-items:center;gap:9px;width:100%;
    border:0;background:transparent;color:var(--ink);cursor:default;
    font:450 13px/1 inherit;padding:0 10px;height:32px;border-radius:6px;text-align:left;
    white-space:nowrap;
  }
  .sel__opt:hover{background:var(--fill-hover);}
  .sel__opt[aria-selected="true"]{color:var(--accent);font-weight:600;}
  .sel__opt i{width:12px;flex:0 0 auto;font-style:normal;opacity:0;}
  .sel__opt[aria-selected="true"] i{opacity:1;}
  .sel[data-disabled="true"]{opacity:.4;pointer-events:none;}
  .sw{
    width:40px;height:24px;border-radius:99px;background:var(--fill-hover);position:relative;
    flex:0 0 auto;cursor:default;border:0;padding:0;
    transition:background-color 180ms cubic-bezier(.32,.72,0,1);
  }
  .sw::after{
    content:"";position:absolute;top:3px;left:3px;width:18px;height:18px;border-radius:50%;
    background:#fff;box-shadow:0 1px 2px rgba(0,0,0,.28);
    transition:transform 180ms cubic-bezier(.32,.72,0,1);
  }
  .sw[aria-checked="true"]{background:var(--accent);}
  .sw[aria-checked="true"]::after{transform:translateX(16px);}
  .sw[disabled]{opacity:.4;}
  .btn{
    border:1px solid var(--line);background:var(--fill);color:var(--ink);
    font:500 13px/1 inherit;padding:9px 14px;border-radius:7px;cursor:default;
  }
  .btn:hover{background:var(--fill-hover);}
  .btn--danger{color:#FF453A;border-color:rgba(255,69,58,.34);}
  .soon{
    font:600 10px/1 ui-monospace,Consolas,monospace;letter-spacing:.1em;text-transform:uppercase;
    color:var(--ink-3);border:1px solid var(--line);border-radius:5px;padding:4px 7px;flex:0 0 auto;
  }
  .about{display:grid;gap:10px;}
  .about div{display:flex;gap:16px;font-size:13.5px;}
  .about dt{width:150px;color:var(--ink-2);flex:0 0 auto;}
  .about dd{margin:0;font-weight:500;}
  code{font:400 12.5px/1.5 ui-monospace,Consolas,monospace;word-break:break-all;}
</style>
</head>
<body>
<nav id="nav">
  <div class="brand">
    <svg viewBox="0 0 24 24" fill="none">
      <path d="M3 20V12.6A9 9 0 0 1 21 12.6V20" stroke="var(--accent)" stroke-width="1.5" stroke-linecap="round"/>
      <path d="M12 3.6V20M7.6 5.1V20M16.4 5.1V20M3 14.4h18" stroke="var(--accent)" stroke-width=".9" stroke-linecap="round" opacity=".5"/>
      <path d="M2.2 20h19.6" stroke="var(--accent)" stroke-width="1.5" stroke-linecap="round"/>
    </svg>
    <b>Settings</b>
  </div>
</nav>
<main id="main"></main>

<script>
/*__AVIARY_SETTINGS__*/
var SAVED = (typeof window.__aviarySettings === 'object' && window.__aviarySettings) || {};
function saved(key, fallback){
  if (!(key in SAVED)) { return fallback; }
  var v = SAVED[key];
  if (v === 'true') { return true; }
  if (v === 'false') { return false; }
  return v;
}
var ICON = {
  start:'<svg viewBox="0 0 16 16" fill="none"><path d="M2.5 8.6 8 3.4l5.5 5.2M4 7.6V13h8V7.6" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  look:'<svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="5.8" stroke="currentColor" stroke-width="1.4"/><path d="M8 2.2v11.6" stroke="currentColor" stroke-width="1.4"/></svg>',
  content:'<svg viewBox="0 0 16 16" fill="none"><rect x="2.2" y="3" width="11.6" height="10" rx="2" stroke="currentColor" stroke-width="1.4"/><path d="M2.2 6.2h11.6" stroke="currentColor" stroke-width="1.4"/></svg>',
  privacy:'<svg viewBox="0 0 16 16" fill="none"><path d="M8 1.8 3.2 3.8v3.9c0 3 2 5.6 4.8 6.4 2.8-.8 4.8-3.4 4.8-6.4V3.8Z" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/></svg>',
  search:'<svg viewBox="0 0 16 16" fill="none"><circle cx="7.2" cy="7.2" r="4.4" stroke="currentColor" stroke-width="1.4"/><path d="m10.6 10.6 2.8 2.8" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/></svg>',
  ext:'<svg viewBox="0 0 16 16" fill="none"><path d="M6 2.6h4v2a1.4 1.4 0 0 0 2.8 0h.6v10.2H2.6V4.6h.6a1.4 1.4 0 0 0 2.8 0Z" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/></svg>',
  autofill:'<svg viewBox="0 0 16 16" fill="none"><rect x="2.2" y="4" width="11.6" height="8" rx="2" stroke="currentColor" stroke-width="1.4"/><path d="M5 8h6" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/></svg>',
  lang:'<svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="5.8" stroke="currentColor" stroke-width="1.4"/><path d="M2.4 8h11.2M8 2.2c3 3.4 3 8.2 0 11.6-3-3.4-3-8.2 0-11.6Z" stroke="currentColor" stroke-width="1.3"/></svg>',
  down:'<svg viewBox="0 0 16 16" fill="none"><path d="M8 2.6v7M5.4 7l2.6 2.6L10.6 7" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/><path d="M2.8 11.6v.8a1.4 1.4 0 0 0 1.4 1.4h7.6a1.4 1.4 0 0 0 1.4-1.4v-.8" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/></svg>',
  access:'<svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="5.8" stroke="currentColor" stroke-width="1.4"/><circle cx="8" cy="5.4" r="1" fill="currentColor"/><path d="M5.4 7.6h5.2M8 7.6v4M6.2 13l1.8-2.4L9.8 13" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>',
  system:'<svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="2.2" stroke="currentColor" stroke-width="1.4"/><path d="M8 1.6v2M8 12.4v2M1.6 8h2M12.4 8h2M3.5 3.5l1.4 1.4M11.1 11.1l1.4 1.4M12.5 3.5l-1.4 1.4M4.9 11.1l-1.4 1.4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>',
  reset:'<svg viewBox="0 0 16 16" fill="none"><path d="M13.4 8a5.4 5.4 0 1 1-1.6-3.8" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/><path d="M13 2.4v3.2H9.8" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  about:'<svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="5.8" stroke="currentColor" stroke-width="1.4"/><path d="M8 7.2v4" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/><circle cx="8" cy="4.9" r=".9" fill="currentColor"/></svg>'
};

function send(key, value){
  try { window.chrome.webview.postMessage(JSON.stringify({type:'setting', key:key, value:value})); } catch(e){}
}

/* Phase 5's Experimental menu is one more entry in this array. */
var SECTIONS = [
  { id:'start', icon:'start', name:'Get started', title:'Get started',
    lede:'Aviary is a Safari-inspired browser for Windows 11, built one phase at a time. This is alpha build 2.',
    rows:[
      {t:'On startup', d:'What Aviary opens with when you launch it.', ctl:{type:'select', key:'startup', opts:['Start Page','Continue where you left off','A specific page'], value:'Start Page'}},
      {t:'Home button', d:'Show the home button in the toolbar.', ctl:{type:'toggle', key:'homeButton', value:true}},
      {t:'What works today', d:'Tabs, browsing, the address bar, back and forward, and this settings page. Themes arrive in Phase 3; the real Start Page, Reader and bookmarks in Phase 4.', ctl:{type:'none'}}
    ]},

  { id:'look', icon:'look', name:'Appearance', title:'Appearance',
    lede:'How Aviary looks. The two Safari themes become switchable in Phase 3 — the tokens behind them already exist.',
    rows:[
      {t:'Theme', d:'Follow Windows, or pin Aviary to light or dark.', ctl:{type:'select', key:'theme', opts:['System','Light','Dark'], value:'System'}},
      {t:'Safari look', d:'Classic is the solid unified toolbar of Safari 15–18. Liquid Glass is the floating translucent chrome of Safari 26 — translucent enough to let Windows’ Mica show through. Whichever you pick is the one Aviary opens with next time.', ctl:{type:'select', key:'look', opts:['Classic','Liquid Glass'], value:'Classic'}},
      {t:'Tab layout', d:'A tab bar across the top, or a vertical sidebar down the side.', ctl:{type:'select', key:'tabLayout', opts:['Top bar','Sidebar'], value:'Top bar', soon:'Phase 3'}},
      {t:'Show full address', d:'Show the whole URL instead of just the site name.', ctl:{type:'toggle', key:'fullUrl', value:false}}
    ]},

  { id:'content', icon:'content', name:'Content', title:'Content',
    lede:'How pages are rendered.',
    rows:[
      {t:'Default zoom', d:'Applied to every site that has no zoom of its own.', ctl:{type:'select', key:'zoom', opts:['80%','90%','100%','110%','125%','150%'], value:'100%'}},
      {t:'Block pop-ups', d:'Stop sites opening windows you did not ask for.', ctl:{type:'toggle', key:'popups', value:true}},
      {t:'Autoplay video', d:'Let sites start video on their own.', ctl:{type:'toggle', key:'autoplay', value:false}},
      {t:'Reader mode', d:'Strip a page back to its text.', ctl:{type:'none', soon:'Phase 4'}}
    ]},

  { id:'privacy', icon:'privacy', name:'Privacy and security', title:'Privacy and security',
    lede:'What Aviary keeps, and what it tells sites about you.',
    rows:[
      {t:'Send a Do Not Track request', d:'Politely asked; widely ignored. Costs nothing.', ctl:{type:'toggle', key:'dnt', value:true}},
      {t:'Identify as Safari', d:'Aviary sends Safari\u2019s user agent so sites serve their Safari code paths. The engine underneath is still Chromium, so a determined site can tell.', ctl:{type:'toggle', key:'safariUa', value:true}},
      {t:'Block third-party cookies', d:'Cookies set by sites other than the one you are visiting.', ctl:{type:'toggle', key:'thirdPartyCookies', value:true}},
      {t:'Block ads and trackers', d:'BLOCKCOUNT', ctl:{type:'toggle', key:'blockTrackers', value:true}},
      {t:'Clear browsing data', d:'History, cookies and cached files kept in this profile.', ctl:{type:'button', key:'clearData', label:'Clear\u2026', soon:'Phase 4'}}
    ]},

  { id:'search', icon:'search', name:'Search engine', title:'Search engine',
    lede:'Where the address bar sends anything that is not a web address.',
    rows:[
      {t:'Search engine', d:'Used when what you type is not a URL.', ctl:{type:'select', key:'searchEngine', opts:['DuckDuckGo','Bing','Google','Startpage'], value:'DuckDuckGo'}},
      {t:'Search suggestions', d:'Show suggestions as you type.', ctl:{type:'toggle', key:'suggest', value:false, soon:'Phase 4'}}
    ]},

  { id:'ext', icon:'ext', name:'Extensions', title:'Extensions',
    lede:'Not in version one, and that is a decision rather than an omission.',
    rows:[
      {t:'Extension support', d:'Extensions mean an update treadmill, a permissions model and a store. Aviary is about getting the browser itself right first. Revisit after beta.', ctl:{type:'none', soon:'Not in v1'}}
    ]},

  { id:'autofill', icon:'autofill', name:'Autofill and passwords', title:'Autofill and passwords',
    lede:'Saved details for forms and sign-ins.',
    rows:[
      {t:'Offer to save passwords', d:'Aviary stores nothing yet — this is here so the shape is right.', ctl:{type:'toggle', key:'savePasswords', value:false, soon:'Phase 4'}},
      {t:'Addresses and payment', d:'Card and address autofill.', ctl:{type:'none', soon:'After beta'}}
    ]},

  { id:'lang', icon:'lang', name:'Languages', title:'Languages',
    lede:'What Aviary asks sites for.',
    rows:[
      {t:'Preferred language', d:'Sent to sites that offer more than one.', ctl:{type:'select', key:'language', opts:['English (United States)','English (United Kingdom)','Filipino','Cebuano'], value:'English (United States)'}},
      {t:'Offer to translate pages', d:'', ctl:{type:'toggle', key:'translate', value:false, soon:'After beta'}}
    ]},

  { id:'down', icon:'down', name:'Downloads', title:'Downloads',
    lede:'Where files land.',
    rows:[
      {t:'Location', d:'C:\\Users\\user\\Downloads', ctl:{type:'button', key:'downloadDir', label:'Change\u2026', soon:'Phase 4'}},
      {t:'Ask where to save each file', d:'Otherwise everything goes to the folder above.', ctl:{type:'toggle', key:'askDownload', value:false}}
    ]},

  { id:'access', icon:'access', name:'Accessibility', title:'Accessibility',
    lede:'Aviary follows the Windows contrast themes; both Safari looks have a high-contrast palette built in.',
    rows:[
      {t:'Minimum font size', d:'Never render text smaller than this.', ctl:{type:'select', key:'minFont', opts:['None','12px','14px','16px','18px'], value:'None'}},
      {t:'Always show scrollbars', d:'Instead of the thin overlay ones.', ctl:{type:'toggle', key:'scrollbars', value:false}},
      {t:'Reduce motion', d:'Follow the Windows animation setting and keep chrome transitions short.', ctl:{type:'toggle', key:'reduceMotion', value:true}}
    ]},

  { id:'system', icon:'system', name:'System', title:'System',
    lede:'How Aviary uses the machine.',
    rows:[
      {t:'Hardware acceleration', d:'Let the GPU draw pages. Turning it off is a troubleshooting step, not an optimisation.', ctl:{type:'toggle', key:'gpu', value:true}},
      {t:'Continue running in the background', d:'Keep Aviary alive after the last window closes.', ctl:{type:'toggle', key:'background', value:false}},
      {t:'Memory saver', d:'Let background tabs release caches and drop their memory priority. Music and timers in background tabs keep running — nothing is suspended.', ctl:{type:'toggle', key:'memorySaver', value:true}},
      {t:'Experimental features', d:'Opt-in switches that are not ready to be defaults \u2014 including the frame-rate ceiling.', ctl:{type:'none', soon:'Phase 5'}}
    ]},

  { id:'reset', icon:'reset', name:'Reset settings', title:'Reset settings',
    lede:'Put everything on this page back the way it started.',
    rows:[
      {t:'Restore defaults', d:'Your history, cookies and downloads are not touched.', ctl:{type:'button', key:'reset', label:'Reset settings', danger:true}}
    ]},

  { id:'about', icon:'about', name:'About Aviary', title:'About Aviary', about:true,
    lede:'A Safari-inspired browser for Windows 11.', rows:[] }
];

function control(row){
  var c = row.ctl || {type:'none'};
  if (c.key) { c.value = saved(c.key, c.value); }
  var soon = c.soon ? '<span class="soon">' + c.soon + '</span>' : '';
  if (c.type === 'select'){
    var opts = c.opts.map(function(o){
      return '<button class="sel__opt" data-v="' + o + '" aria-selected="' +
             (o === c.value ? 'true' : 'false') + '"><i>&#10003;</i>' + o + '</button>';
    }).join('');
    return soon +
      '<div class="sel" data-key="' + c.key + '"' + (c.soon ? ' data-disabled="true"' : '') + '>' +
        '<button class="sel__btn" aria-expanded="false"><span>' + c.value + '</span>' +
          '<svg viewBox="0 0 10 7" fill="none"><path d="M1 1l4 4 4-4" stroke="currentColor" ' +
          'stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg></button>' +
        '<div class="sel__pop" hidden>' + opts + '</div>' +
      '</div>';
  }
  if (c.type === 'toggle'){
    return soon + '<button class="sw" data-key="' + c.key + '" role="switch" aria-checked="' +
           (c.value ? 'true' : 'false') + '"' + (c.soon ? ' disabled' : '') + '></button>';
  }
  if (c.type === 'button'){
    return soon + '<button class="btn' + (c.danger ? ' btn--danger' : '') + '" data-key="' + c.key + '"' +
           (c.soon ? ' disabled' : '') + '>' + c.label + '</button>';
  }
  return soon;
}

var nav = document.getElementById('nav');
var main = document.getElementById('main');

SECTIONS.forEach(function(s, i){
  if (s.id === 'autofill' || s.id === 'about') nav.appendChild(document.createElement('hr'));
  var b = document.createElement('button');
  b.innerHTML = ICON[s.icon] + '<span>' + s.name + '</span>';
  b.setAttribute('data-target', s.id);
  if (i === 0) b.setAttribute('aria-current','true');
  nav.appendChild(b);

  var pane = document.createElement('section');
  pane.className = 'pane' + (i === 0 ? ' on' : '');
  pane.id = 'pane-' + s.id;

  var body = '<h1>' + s.title + '</h1><p class="lede">' + s.lede + '</p>';
  if (s.about){
    body += '<div class="group"><div class="row"><div class="about">' +
      '<div><dt>Version</dt><dd>0.2.0 &middot; alpha build 2</dd></div>' +
      '<div><dt>Phase</dt><dd>02 &mdash; Tabs and settings</dd></div>' +
      '<div><dt>Engine</dt><dd>WebView2 (Chromium, via Microsoft Edge)</dd></div>' +
      '<div><dt>Interface</dt><dd>WinUI 3 &middot; Windows App SDK &middot; .NET 9</dd></div>' +
      '<div><dt>Identifies as</dt><dd><code id="ua"></code></dd></div>' +
      '</div></div></div>' +
      '<p class="lede">Aviary is an independent project. It is not affiliated with Apple, and it is not ' +
      'Safari &mdash; Apple owns that mark. Windows, WinUI and WebView2 are Microsoft&rsquo;s.</p>';
  } else {
    body += '<div class="group">' + s.rows.map(function(r){
      return '<div class="row"><div class="row__t"><b>' + r.t + '</b>' +
             (r.d ? '<span>' + r.d + '</span>' : '') + '</div>' + control(r) + '</div>';
    }).join('') + '</div>';
  }
  pane.innerHTML = body;
  main.appendChild(pane);
});

var uaEl = document.getElementById('ua');
if (uaEl) uaEl.textContent = navigator.userAgent;

nav.addEventListener('click', function(e){
  var b = e.target.closest('button[data-target]');
  if (!b) return;
  nav.querySelectorAll('button[data-target]').forEach(function(x){ x.removeAttribute('aria-current'); });
  b.setAttribute('aria-current','true');
  document.querySelectorAll('.pane').forEach(function(p){ p.classList.remove('on'); });
  var pane = document.getElementById('pane-' + b.getAttribute('data-target'));
  if (pane) pane.classList.add('on');
  main.scrollTop = 0;
});

function closeAllPopups(except){
  document.querySelectorAll('.sel').forEach(function(s){
    if (s === except) { return; }
    s.querySelector('.sel__pop').hidden = true;
    s.querySelector('.sel__btn').setAttribute('aria-expanded','false');
  });
}

main.addEventListener('click', function(e){
  var sw = e.target.closest('.sw');
  if (sw && !sw.disabled){
    var on = sw.getAttribute('aria-checked') !== 'true';
    sw.setAttribute('aria-checked', on ? 'true' : 'false');
    send(sw.getAttribute('data-key'), on);
    closeAllPopups(null);
    return;
  }

  var opt = e.target.closest('.sel__opt');
  if (opt){
    var host = opt.closest('.sel');
    host.querySelectorAll('.sel__opt').forEach(function(o){ o.setAttribute('aria-selected','false'); });
    opt.setAttribute('aria-selected','true');
    host.querySelector('.sel__btn span').textContent = opt.getAttribute('data-v');
    host.querySelector('.sel__pop').hidden = true;
    host.querySelector('.sel__btn').setAttribute('aria-expanded','false');
    send(host.getAttribute('data-key'), opt.getAttribute('data-v'));
    return;
  }

  var selBtn = e.target.closest('.sel__btn');
  if (selBtn){
    var sel = selBtn.closest('.sel');
    var pop = sel.querySelector('.sel__pop');
    var opening = pop.hidden;
    closeAllPopups(sel);
    pop.hidden = !opening;
    selBtn.setAttribute('aria-expanded', opening ? 'true' : 'false');
    return;
  }

  closeAllPopups(null);

  var btn = e.target.closest('.btn');
  if (btn && !btn.disabled) { send(btn.getAttribute('data-key'), true); }
});

nav.addEventListener('click', function(){ closeAllPopups(null); });
main.addEventListener('scroll', function(){ closeAllPopups(null); }, true);
document.addEventListener('keydown', function(e){ if (e.key === 'Escape') { closeAllPopups(null); } });
</script>
</body>
</html>
""";
}
