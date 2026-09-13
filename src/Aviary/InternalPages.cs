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
  /* MUST come after the rule above.
     The browser hides [hidden] elements with display:none from its own stylesheet, which
     any author rule setting display outranks. Without this line every popup is open from
     the moment the page loads and no click can ever close one — which is exactly what
     happened. */
  .sel__pop[hidden]{display:none;}
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
  .btns{display:flex;gap:8px;flex:0 0 auto;}
  /* A row's expandable detail — "What Aviary collects". */
  .more{margin-top:8px;}
  .more summary{
    display:inline-flex;align-items:center;gap:7px;cursor:default;list-style:none;
    color:var(--accent);font-size:12.5px;font-weight:500;
  }
  .more summary::-webkit-details-marker{display:none;}
  .more summary::before{
    content:"";width:5px;height:5px;margin-top:-2px;
    border-right:1.5px solid currentColor;border-bottom:1.5px solid currentColor;
    transform:rotate(-45deg);transition:transform 160ms cubic-bezier(.32,.72,0,1);
  }
  .more[open] summary::before{transform:rotate(45deg);margin-top:-4px;}
  .more__body{margin-top:10px;font-size:12.5px;line-height:1.55;color:var(--ink-2);
    -webkit-user-select:text;user-select:text;}
  .more__body ol{margin:0 0 6px;padding-left:22px;display:grid;gap:6px;}
  .more__body ul{margin:4px 0 8px;padding-left:18px;display:grid;gap:3px;}
  /* `.row__t b` makes a row's TITLE a block; bold words inside a description or the list
     must stay in their sentence. */
  .row__t span b,.more__body b{display:inline;color:var(--ink);font-weight:600;font-size:inherit;}
  .more__body p{margin:8px 0 0;}
  .more__h{font-weight:600;color:var(--ink);margin-top:12px !important;}
  /* "Restart Aviary to apply" under an engine switch whose saved value is not yet the one
     the running engine started with. */
  .note{display:block;margin-top:6px;font-size:12px;font-weight:600;color:#FF9500 !important;}
  /* Website data: one line per site, its sizes, and Remove. */
  .sitedata{margin-top:12px;display:grid;gap:2px;max-height:340px;overflow-y:auto;}
  .sitedata[hidden]{display:none;}
  .sd{display:flex;align-items:center;gap:12px;padding:7px 10px;border-radius:7px;background:var(--fill);}
  .sd b{flex:1;min-width:0;font-weight:500;font-size:13px;color:var(--ink);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;display:block;}
  .sd i{font-style:normal;font-size:12px;color:var(--ink-2);white-space:nowrap;}
  .sd button{border:1px solid var(--line);background:var(--panel);color:var(--ink);font:500 12px/1 inherit;
    padding:6px 10px;border-radius:6px;cursor:default;}
  .sd button:hover{background:var(--fill-hover);}
  .sd button[disabled]{opacity:.45;}
  .sd__empty{font-size:12.5px;color:var(--ink-2);padding:4px 0;}
  .note[hidden]{display:none;}
  .soon{
    font:600 10px/1 ui-monospace,Consolas,monospace;letter-spacing:.1em;text-transform:uppercase;
    color:var(--ink-3);border:1px solid var(--line);border-radius:5px;padding:4px 7px;flex:0 0 auto;
  }
  .about{display:grid;gap:10px;}
  .about div{display:flex;gap:16px;font-size:13.5px;}
  .about dt{width:150px;color:var(--ink-2);flex:0 0 auto;}
  .about dd{margin:0;font-weight:500;}
  code{font:400 12.5px/1.5 ui-monospace,Consolas,monospace;word-break:break-all;}
  /* A filesystem path in a description line. direction:rtl truncates from the FRONT, so a
     deep folder keeps the part you actually read — the folder's own name. */
  .path{
    display:block;font:400 12px/1.5 ui-monospace,Consolas,monospace;color:var(--ink-2);
    margin-top:3px;max-width:44ch;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;
    direction:rtl;text-align:left;
  }
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
/*__AVIARY_BUILD__*/
/*__AVIARY_PATHS__*/
/*__AVIARY_DIAG__*/
/*__AVIARY_SECTION__*/
var SAVED = (typeof window.__aviarySettings === 'object' && window.__aviarySettings) || {};
/* What the running engine STARTED with, for the switches that only apply at launch. */
var LAUNCH = (typeof window.__aviaryLaunch === 'object' && window.__aviaryLaunch) || {};
var PATHS = (typeof window.__aviaryPaths === 'object' && window.__aviaryPaths) || {};
var DIAG = (typeof window.__aviaryDiag === 'object' && window.__aviaryDiag) || {};
var RENDERED = {};
var BUILD = (typeof window.__aviaryBuild === 'object' && window.__aviaryBuild) ||
            {version:'?', channel:'?', phaseLabel:'?', phaseNumber:0};
/* Rows are gated on the last SHIPPED phase, not the one in progress — see BuildInfo. */
var PHASE = BUILD.phaseShipped | 0;

/* Every keyed control's LITERAL default, collected as the page renders and posted up once
   at the end. The host writes back only the keys it has never seen, which is what makes a
   control that saves-but-does-not-reload impossible: registration is a side effect of
   rendering, not a list somebody has to remember to edit. */
var SEED = {};

function saved(key, fallback){
  if (!(key in SAVED)) { return fallback; }
  var v = SAVED[key];
  if (v === 'true') { return true; }
  if (v === 'false') { return false; }
  return v;
}
/* Every message carries this page's secret (injected with the settings), which the host
   checks — its handler outlives this page if the tab is navigated to a website. */
function post(o){ try { o.token = window.__aviaryToken || ''; window.chrome.webview.postMessage(JSON.stringify(o)); } catch(e){} }
function sendSetting(k, v){ post({type:'setting', key:k, value:v}); }
function sendCommand(n){ post({type:'command', name:n}); }
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

/* Phase 5's Experimental menu is one more entry in this array.

   A row that is not ready yet carries `phase:N` — the number of the phase it arrives in.
   The badge text and the disabled state are BOTH derived from comparing that number
   against the injected build's phase, so nobody writes "Phase 3" by hand and no badge can
   outlive the phase it names. Something deliberately never coming carries `never:'…'`. */
var SECTIONS = [
  { id:'start', icon:'start', name:'Get started', title:'Get started',
    lede:'Aviary is a Safari-inspired browser for Windows 11, built one phase at a time. This is __AV_CHANNEL__.',
    rows:[
      {t:'On startup', d:'What Aviary opens with when you launch it.', phase:6, ctl:{type:'select', key:'startup', opts:['Start Page','Continue where you left off','A specific page'], value:'Start Page'}},
      {t:'Home button', d:'Show the home button in the toolbar.', ctl:{type:'toggle', key:'homeButton', value:true}},
      {t:'Start Page sections', d:'The same switches as the Customise button in the corner of the Start Page.', ctl:{type:'none'}},
      {t:'Show Favourites', d:'', ctl:{type:'toggle', key:'startFavourites', value:true}},
      {t:'Show Frequently Visited', d:'', ctl:{type:'toggle', key:'startFrequent', value:true}},
      {t:'Show Reading List', d:'', ctl:{type:'toggle', key:'startReading', value:true}},
      {t:'Show Privacy Report', d:'', ctl:{type:'toggle', key:'startPrivacy', value:true}},
      {t:'What works today', d:'Tabs you can drag to reorder, both Safari looks switching live, ad and tracker blocking, find in page, downloads, and a favourites bar. Arriving this phase: the real Start Page, Reader, the sidebar, history and real favicons.', ctl:{type:'none'}}
    ]},

  { id:'look', icon:'look', name:'Appearance', title:'Appearance',
    lede:'How Aviary looks. Both Safari looks switch while the browser is running, and whichever you leave it on is the one it opens with next time.',
    rows:[
      {t:'Theme', d:'Follow Windows, or pin Aviary to light or dark.', ctl:{type:'select', key:'theme', opts:['System','Light','Dark'], value:'System'}},
      {t:'Safari look', d:'Classic is the solid unified toolbar of Safari 15–18. Liquid Glass is the floating translucent chrome of Safari 26 — translucent enough to let Windows’ Mica show through.', ctl:{type:'select', key:'look', opts:['Classic','Liquid Glass'], value:'Classic'}},
      {t:'Tab layout', d:'A tab bar across the top, or a vertical sidebar down the side.', phase:4, ctl:{type:'select', key:'tabLayout', opts:['Top bar','Sidebar'], value:'Top bar'}},
      {t:'Show site icons', d:'Show each site’s own icon in tabs and the favourites bar. Turned off, Aviary shows a coloured letter instead and never fetches an icon.', ctl:{type:'toggle', key:'siteIcons', value:true}},
      {t:'Show full address', d:'Show the whole URL instead of just the site name.', ctl:{type:'toggle', key:'fullUrl', value:false}},
      {t:'Favourites bar', d:'A strip of saved sites under the toolbar. Ctrl+D adds the page you are on.', ctl:{type:'toggle', key:'bookmarksBar', value:false}},
      {t:'Full screen', d:'Press F11 to hide everything but the page, and F11 again to come back. A video you put in full screen fills the whole screen too, and Esc brings it back.', ctl:{type:'none'}}
    ]},

  { id:'content', icon:'content', name:'Content', title:'Content',
    lede:'How pages are rendered.',
    rows:[
      {t:'Default zoom', d:'Applied to every site that has no zoom of its own.', phase:4, ctl:{type:'select', key:'zoom', opts:['80%','90%','100%','110%','125%','150%'], value:'100%'}},
      {t:'Block pop-ups', d:'Stop sites opening windows you did not ask for. Links you click yourself always open.', ctl:{type:'toggle', key:'popups', value:true}},
      {t:'Autoplay video', d:'Let sites start video on their own.', phase:5, ctl:{type:'toggle', key:'autoplay', value:false}},
      {t:'Reader', d:'Strip an article back to its text. Open it from the “Aa” button inside the address field, or press Ctrl+Shift+R. Aviary offers it only on pages that actually have an article.', ctl:{type:'none'}},
      {t:'Reader font', d:'The typeface Reader sets articles in.', ctl:{type:'select', key:'readerFont', opts:['San Francisco','New York','Athelas','Charter','Georgia','Iowan','Palatino','Seravek','Times New Roman'], value:'New York'}},
      {t:'Reader background', d:'White, Sepia, Grey or Black — independent of the browser’s own theme, exactly as Safari keeps it.', ctl:{type:'select', key:'readerTheme', opts:['White','Sepia','Grey','Black'], value:'White'}}
    ]},

  { id:'privacy', icon:'privacy', name:'Privacy and security', title:'Privacy and security',
    lede:'What Aviary keeps, and what it tells sites about you. Every switch on this page does what it says — nothing here is for show.',
    rows:[
      {t:'Block ads and trackers', d:'__AV_BLOCKCOUNT__', ctl:{type:'toggle', key:'blockTrackers', value:true}},
      /* Phase 5 M2 — privacy that actually works. The three rows marked restart:true are
         switches in the browser engine itself, read once when Aviary starts; the page shows a
         note when the saved value differs from what the running engine started with. */
      {t:'Block third-party cookies', d:'Refuses cookies from sites other than the one you are on — the way ads follow you from one site to the next. Sign-in buttons from other sites may ask you to sign in again.', restart:true, ctl:{type:'toggle', key:'thirdPartyCookies', value:true}},
      {t:'Ask sites not to sell or share my data', d:'Sends Global Privacy Control with every request and tells every page’s scripts too. It replaced Do Not Track, and in some places the law requires sites to honour it.', restart:true, ctl:{type:'toggle', key:'gpc', value:true}},
      {t:'Remove tracking from links', d:'Takes click identifiers such as fbclid and gclid, and utm_ campaign tags, off any link that takes you to a different site — before that site ever sees them. “Copy Link” removes them too.', ctl:{type:'toggle', key:'stripTracking', value:true}},
      {t:'Always use a secure connection', d:'Asks every site for HTTPS first. If a site has no secure version, Aviary says so and you can choose to continue — a choice it then remembers for that site. Your own computer and local network are left alone.', ctl:{type:'toggle', key:'httpsOnly', value:true}},
      {t:'Send less about where you came from', d:'When a page links to or loads something from another site, that site learns only which site you came from — never the page you were reading.', ctl:{type:'toggle', key:'trimReferrer', value:true}},
      {t:'Hide my local network address', d:'Stops websites reading your computer’s address on your home or office network through video-calling features. Video calls still work.', restart:true, ctl:{type:'toggle', key:'webrtcProtect', value:true}},
      {t:'Identify as Safari', d:'Sends Safari’s user agent so sites serve their Safari code paths, and hides the extra details a Chromium browser would hand to page scripts. The engine underneath is still Chromium, so a determined site can tell. Applies to pages you load from now on.', ctl:{type:'toggle', key:'safariUa', value:true}},
      {t:'Privacy Report shows', d:'What the Start Page’s Privacy Report counts: the last seven days, or everything since Aviary started counting. Bandwidth and time saved are estimates — each blocked request is counted at a typical size for its kind and 50 ms of loading.', ctl:{type:'select', key:'privacyRange', opts:['Last 7 days','All time'], value:'Last 7 days'}},
      {t:'Private window', d:'A window that forgets: pages, searches, cookies and site data from it are gone when you close it, and nothing about it is recorded. Ctrl+Shift+N opens one.', ctl:{type:'button', cmd:'privateWindow', label:'Open'}},
      {t:'Website data', d:'Cookies and storage each site you have visited has kept in this profile, with a way to remove any one site\u2019s.', more:'sitedata', ctl:{type:'button', cmd:'siteData', label:'Show'}},
      {t:'Clear browsing data', d:'History, cookies and cached files kept in this profile.', phase:4, ctl:{type:'button', cmd:'clearData', label:'Clear\u2026'}},
      {t:'Cached site icons', d:'__AV_ICONCACHE__', ctl:{type:'button', cmd:'clearFavicons', label:'Clear'}},
      /* Diagnostics \u2014 PHASE5-PLAN.md M1. Deliberately at the BOTTOM of Privacy and deliberately
         blunt: it records every address, title and search, and the row has to say so in those
         words. Live, not phase-gated: it works now, and every later milestone is measured by it. */
      {t:'Allow data collection for improvements', d:'Records everything Aviary does \u2014 every web address you visit, every page title and every search, every request and blocked tracker, every button you press, every error and crash \u2014 into a folder on this computer, so problems can be found and fixed. <b>It never leaves this computer.</b> Off until you switch it on.', ctl:{type:'toggle', key:'improvementCollection', value:false}},
      {t:'What Aviary collects', d:'One switch, all or nothing. While it is on, all sixteen of these are recorded; passwords, what you type into websites, cookies, login tokens, card numbers and your clipboard never are.', more:'diag', ctl:{type:'none'}},
      {t:'Collected data', d:'<span id="diagstatus"></span><span class="path" id="diagpath"></span>', ctl:{type:'buttons', items:[{cmd:'diagOpen', label:'Open folder'}, {cmd:'diagDelete', label:'Delete\u2026', danger:true}]}}
    ]},

  { id:'search', icon:'search', name:'Search engine', title:'Search engine',
    lede:'Where the address bar sends anything that is not a web address.',
    rows:[
      {t:'Search engine', d:'Used when what you type is not a URL.', ctl:{type:'select', key:'searchEngine', opts:['DuckDuckGo','Bing','Google','Startpage'], value:'DuckDuckGo'}},
      {t:'Search suggestions from the engine', d:'Ask the search engine for suggestions as you type. Off by default, and worth leaving off: it sends every keystroke in the address bar to them. Aviary’s own suggestions — from your history and favourites — work either way and never leave the machine.', phase:5, ctl:{type:'toggle', key:'suggest', value:false}}
    ]},

  { id:'ext', icon:'ext', name:'Extensions', title:'Extensions',
    lede:'Not in version one, and that is a decision rather than an omission.',
    rows:[
      {t:'Extension support', d:'Extensions mean an update treadmill, a permissions model and a store. Aviary is about getting the browser itself right first. Revisit after beta.', never:'Not in v1', ctl:{type:'none'}}
    ]},

  { id:'autofill', icon:'autofill', name:'Autofill and passwords', title:'Autofill and passwords',
    lede:'Saved details for forms and sign-ins.',
    rows:[
      {t:'Offer to save passwords', d:'When you sign in to a site, Aviary offers to remember the password and fills it in next time. Passwords stay in this profile on this computer, encrypted with your Windows account, and a private window never saves one.', ctl:{type:'toggle', key:'savePasswords', value:false}},
      {t:'Saved passwords', d:'Delete every password Aviary has saved. Sites will ask you to sign in again.', ctl:{type:'button', cmd:'clearPasswords', label:'Remove\u2026', danger:true}},
      {t:'Suggest what you have typed before', d:'Remembers what you type into forms \u2014 names, addresses, search boxes \u2014 and offers it again next time. Kept in this profile on this computer. Off by default.', ctl:{type:'toggle', key:'formAutofill', value:false}},
      {t:'Saved form entries', d:'Forget everything the switch above has remembered.', ctl:{type:'button', cmd:'clearFormData', label:'Clear'}},
      {t:'Addresses and payment', d:'Card and address autofill.', never:'After beta', ctl:{type:'none'}}
    ]},

  { id:'lang', icon:'lang', name:'Languages', title:'Languages',
    lede:'What Aviary asks sites for.',
    rows:[
      {t:'Preferred language', d:'Sent to sites that offer more than one.', phase:5, ctl:{type:'select', key:'language', opts:['English (United States)','English (United Kingdom)','Filipino','Cebuano'], value:'English (United States)'}},
      {t:'Offer to translate pages', d:'', never:'After beta', ctl:{type:'toggle', key:'translate', value:false}}
    ]},

  { id:'down', icon:'down', name:'Downloads', title:'Downloads',
    lede:'Where files land.',
    rows:[
      /* The path is filled in by script, NOT substituted into this string. A Windows path
         dropped into a JS string literal loses its separators the moment the escape parser
         sees \F or \D or \t, and no amount of HTML-encoding helps — backslashes are not an
         HTML problem. Set as textContent below, from a JSON-encoded global. */
      {t:'Location', d:'<span class="path" id="dlpath"></span>', phase:5, ctl:{type:'button', cmd:'downloadDir', label:'Change…'}},
      {t:'Ask where to save each file', d:'Otherwise everything goes to the folder above.', phase:5, ctl:{type:'toggle', key:'askDownload', value:false}}
    ]},

  { id:'access', icon:'access', name:'Accessibility', title:'Accessibility',
    lede:'Aviary follows the Windows contrast themes; both Safari looks have a high-contrast palette built in.',
    rows:[
      {t:'Minimum font size', d:'Never render text smaller than this.', phase:5, ctl:{type:'select', key:'minFont', opts:['None','12px','14px','16px','18px'], value:'None'}},
      {t:'Always show scrollbars', d:'Instead of the thin overlay ones.', phase:5, ctl:{type:'toggle', key:'scrollbars', value:false}},
      {t:'Reduce motion', d:'Follow the Windows animation setting and keep chrome transitions short. Aviary reads your Windows setting the first time it runs, so this usually starts where you want it.', phase:5, ctl:{type:'toggle', key:'reduceMotion', value:false}}
    ]},

  { id:'system', icon:'system', name:'System', title:'System',
    lede:'How Aviary uses the machine.',
    rows:[
      {t:'Hardware acceleration', d:'Let the GPU draw pages. Turning it off is a troubleshooting step, not an optimisation.', phase:5, ctl:{type:'toggle', key:'gpu', value:true}},
      {t:'Continue running in the background', d:'Keep Aviary alive after the last window closes.', phase:5, ctl:{type:'toggle', key:'background', value:false}},
      {t:'Memory saver', d:'Let background tabs release caches and drop their memory priority. Music and timers in background tabs keep running — nothing is suspended.', ctl:{type:'toggle', key:'memorySaver', value:true}},
      {t:'Experimental features', d:'Opt-in switches that are not ready to be defaults — including the frame-rate ceiling.', phase:5, ctl:{type:'none'}}
    ]},

  { id:'reset', icon:'reset', name:'Reset settings', title:'Reset settings',
    lede:'Put everything on this page back the way it started.',
    rows:[
      {t:'Restore defaults', d:'Your history, cookies and downloads are not touched.', ctl:{type:'button', cmd:'reset', label:'Reset settings', danger:true}}
    ]},

  { id:'about', icon:'about', name:'About Aviary', title:'About Aviary', about:true,
    lede:'A Safari-inspired browser for Windows 11.', rows:[] }
];

function control(row){
  var c = row.ctl || {type:'none'};

  /* ONE number decides both the badge and whether the control is live. */
  var pending = row.never ? row.never
              : (row.phase && row.phase > PHASE)
                  ? ('Phase ' + (row.phase < 10 ? '0' : '') + row.phase)
                  : null;
  var badge = pending ? '<span class="soon">' + pending + '</span>' : '';

  if (c.key){
    /* Record the literal default BEFORE the saved value is applied over it. */
    if (!(c.key in SEED)){
      SEED[c.key] = (c.value === true) ? 'true'
                  : (c.value === false) ? 'false'
                  : String(c.value);
    }
    c.value = saved(c.key, c.value);

    /* …and what the control is about to DRAW, for diagnostics' round-trip check: a switch
       shows its truthiness, a dropdown shows its text. */
    RENDERED[c.key] = (c.type === 'toggle') ? (c.value ? 'true' : 'false') : String(c.value);
  }

  if (c.type === 'select'){
    var opts = c.opts.map(function(o){
      return '<button class="sel__opt" data-v="' + o + '" aria-selected="' +
             (o === c.value ? 'true' : 'false') + '"><i>&#10003;</i>' + o + '</button>';
    }).join('');
    return badge +
      '<div class="sel" data-key="' + c.key + '"' + (pending ? ' data-disabled="true"' : '') + '>' +
        '<button class="sel__btn" aria-expanded="false"><span>' + c.value + '</span>' +
          '<svg viewBox="0 0 10 7" fill="none"><path d="M1 1l4 4 4-4" stroke="currentColor" ' +
          'stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg></button>' +
        '<div class="sel__pop" hidden>' + opts + '</div>' +
      '</div>';
  }
  if (c.type === 'toggle'){
    return badge + '<button class="sw" data-key="' + c.key + '" role="switch" aria-checked="' +
           (c.value ? 'true' : 'false') + '"' + (pending ? ' disabled' : '') + '></button>';
  }
  if (c.type === 'button'){
    /* data-cmd, NOT data-key. A button is not a setting, and pretending it was is exactly
       how "Reset settings" ended up persisting a junk reset:true key and doing nothing. */
    return badge + '<button class="btn' + (c.danger ? ' btn--danger' : '') +
           '" data-cmd="' + c.cmd + '"' + (pending ? ' disabled' : '') + '>' + c.label + '</button>';
  }
  if (c.type === 'buttons'){
    return badge + '<div class="btns">' + c.items.map(function(b){
      return '<button class="btn' + (b.danger ? ' btn--danger' : '') + '" data-cmd="' + b.cmd + '"' +
             (pending ? ' disabled' : '') + '>' + b.label + '</button>';
    }).join('') + '</div>';
  }
  return badge;
}

/* A row's expandable detail. The body is filled by script from injected data, never pasted
   into this markup — see the diagnostics list below. */
var MORE = {
  diag: '<details class="more"><summary>Show all sixteen, and what is never collected</summary>' +
        '<div class="more__body" id="diaglist"></div></details>',
  /* Filled by the host when "Show" is pressed — site names are website-controlled, so every
     one goes in through textContent. */
  sitedata: '<div class="sitedata" id="sitedata" hidden></div>'
};

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
      '<div><dt>Version</dt><dd>__AV_VERSION__ &middot; __AV_CHANNEL__</dd></div>' +
      '<div><dt>Phase</dt><dd>__AV_PHASE__</dd></div>' +
      '<div><dt>Engine</dt><dd>WebView2 (Chromium, via Microsoft Edge)</dd></div>' +
      '<div><dt>Interface</dt><dd>WinUI 3 &middot; Windows App SDK &middot; .NET 9</dd></div>' +
      '<div><dt>Identifies as</dt><dd><code id="ua"></code></dd></div>' +
      '</div></div></div>' +
      '<p class="lede">Aviary is an independent project. It is not affiliated with Apple, and it is not ' +
      'Safari &mdash; Apple owns that mark. Windows, WinUI and WebView2 are Microsoft&rsquo;s.</p>';
  } else {
    body += '<div class="group">' + s.rows.map(function(r){
      var key = r.ctl && r.ctl.key;
      var note = (r.restart && key)
        ? '<span class="note" data-restart="' + key + '" hidden>Restart Aviary to apply this change.</span>'
        : '';
      return '<div class="row"><div class="row__t"><b>' + r.t + '</b>' +
             (r.d ? '<span>' + r.d + '</span>' : '') + note + (r.more ? (MORE[r.more] || '') : '') +
             '</div>' + control(r) + '</div>';
    }).join('') + '</div>';
  }
  pane.innerHTML = body;
  main.appendChild(pane);
});

/* Website data, from the host: [{site, cookies, bytes}]. */
function sizeText(b){
  if (b <= 0) { return ''; }
  var u = ['B','KB','MB','GB'], i = 0;
  while (b >= 1024 && i < u.length - 1) { b /= 1024; i++; }
  return (i === 0 ? String(Math.round(b)) : b.toFixed(1)) + ' ' + u[i];
}
window.__aviarySiteData = function(list){
  var box = document.getElementById('sitedata');
  if (!box) { return; }
  box.hidden = false;
  box.textContent = '';
  if (!list || !list.length){
    var none = document.createElement('div');
    none.className = 'sd__empty';
    none.textContent = 'No site has kept anything in this profile.';
    box.appendChild(none);
    return;
  }
  list.forEach(function(s){
    var row = document.createElement('div');
    row.className = 'sd';
    var name = document.createElement('b');
    name.textContent = s.site;
    var what = document.createElement('i');
    var parts = [];
    if (s.cookies) { parts.push(s.cookies + (s.cookies === 1 ? ' cookie' : ' cookies')); }
    if (s.bytes) { parts.push(sizeText(s.bytes) + ' stored'); }
    what.textContent = parts.join(' \u00b7 ');
    var remove = document.createElement('button');
    remove.textContent = 'Remove';
    remove.setAttribute('data-site', s.site);
    row.appendChild(name); row.appendChild(what); row.appendChild(remove);
    box.appendChild(row);
  });
};

/* Shows the restart note while a launch-time switch differs from what the engine started with. */
function restartNote(key, on){
  var n = document.querySelector('.note[data-restart="' + key + '"]');
  if (!n || !(key in LAUNCH)) { return; }
  n.hidden = (LAUNCH[key] === on);
}
Object.keys(LAUNCH).forEach(function(k){ restartNote(k, saved(k, LAUNCH[k]) === true); });

var uaEl = document.getElementById('ua');
if (uaEl) uaEl.textContent = navigator.userAgent;

/* textContent, not innerHTML: a folder name is user data and can contain anything. */
var dlPath = document.getElementById('dlpath');
if (dlPath) dlPath.textContent = PATHS.downloads || '';

/* The collection row: its live status and folder, and the full list of what is collected —
   built from the same C# list as manifest.json and summary.md, so the three cannot drift. */
function diagStatus(d){
  var s = document.getElementById('diagstatus');
  if (s) s.textContent = (d && d.status) || '';
  var p = document.getElementById('diagpath');
  if (p) p.textContent = (d && d.folder) || '';
}
diagStatus(DIAG);

/* Host to page, one-way: the switch has moved itself; the words under it follow. */
window.__aviaryDiagStatus = function(d){ if (d) { DIAG = d; } diagStatus(DIAG); };

var diagList = document.getElementById('diaglist');
if (diagList && DIAG.categories){
  var ol = document.createElement('ol');
  DIAG.categories.forEach(function(c){
    var li = document.createElement('li');
    var b = document.createElement('b');
    b.textContent = c.name;
    li.appendChild(b);
    li.appendChild(document.createTextNode(' — ' + c.plain));
    ol.appendChild(li);
  });
  diagList.appendChild(ol);

  var never = document.createElement('p');
  never.className = 'more__h';
  never.textContent = 'Never collected';
  diagList.appendChild(never);

  var ul = document.createElement('ul');
  (DIAG.never || []).forEach(function(n){
    var li = document.createElement('li');
    li.textContent = n;
    ul.appendChild(li);
  });
  diagList.appendChild(ul);

  [DIAG.masking, DIAG.stays].forEach(function(t){
    if (!t) { return; }
    var p = document.createElement('p');
    p.textContent = t;
    diagList.appendChild(p);
  });
}

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
  var siteBtn = e.target.closest('button[data-site]');
  if (siteBtn){
    siteBtn.disabled = true;
    post({type:'command', name:'siteDataRemove', arg: siteBtn.getAttribute('data-site')});
    return;
  }

  var sw = e.target.closest('.sw');
  if (sw && !sw.disabled){
    var on = sw.getAttribute('aria-checked') !== 'true';
    sw.setAttribute('aria-checked', on ? 'true' : 'false');
    sendSetting(sw.getAttribute('data-key'), on);
    restartNote(sw.getAttribute('data-key'), on);
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
    sendSetting(host.getAttribute('data-key'), opt.getAttribute('data-v'));
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
  if (btn && !btn.disabled) { sendCommand(btn.getAttribute('data-cmd')); }
});

nav.addEventListener('click', function(){ closeAllPopups(null); });
main.addEventListener('scroll', function(){ closeAllPopups(null); }, true);
document.addEventListener('keydown', function(e){ if (e.key === 'Escape') { closeAllPopups(null); } });

/* Every keyed control has now rendered, so SEED holds this page's literal defaults for all
   of them. Handing them up is what registers them with the store — after this, the host's
   snapshot covers everything the page can draw, and a control cannot save a value it then
   fails to read back. */
post({type:'schema', values:SEED});

/* After the schema, so the store has registered every key before the host compares. */
post({type:'rendered', values:RENDERED});

/* Host to page, deliberately one-way and silent: it must NOT post anything back, or Ctrl+D
   flipping the favourites bar on would ping-pong with the host forever. */
window.__aviaryApply = function(key, value){
  var k = (window.CSS && CSS.escape) ? CSS.escape(key) : key;

  var sw = document.querySelector('.sw[data-key="' + k + '"]');
  if (sw){ sw.setAttribute('aria-checked', value ? 'true' : 'false'); return; }

  var sel = document.querySelector('.sel[data-key="' + k + '"]');
  if (!sel) { return; }
  sel.querySelectorAll('.sel__opt').forEach(function(o){
    o.setAttribute('aria-selected', o.getAttribute('data-v') === String(value) ? 'true' : 'false');
  });
  sel.querySelector('.sel__btn span').textContent = String(value);
};

/* Opened from the app menu's "About Aviary", which used to land you on Get started. */
if (window.__aviarySection){
  var jump = nav.querySelector('button[data-target="' + window.__aviarySection + '"]');
  if (jump) { jump.click(); }
}
</script>
</body>
</html>
""";
}
