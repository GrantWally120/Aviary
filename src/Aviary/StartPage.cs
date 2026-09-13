namespace Aviary;

/// <summary>
/// Safari's real Start Page: a Favourites grid, Frequently Visited, a Reading List row, a
/// Privacy Report card, and a Customise popover in the bottom-right corner that turns each
/// section on and off.
///
/// The background is DRAWN, not photographed. Safari ships a photograph; shipping one here
/// means either a licensing question or several hundred kilobytes of base64 sitting inside
/// a .cs file. The glasshouse is generated parametrically from about forty lines of script
/// into inline SVG — it is Aviary's own dome mark at architectural scale, it weighs about
/// four kilobytes, and light and dark are a token swap rather than two images.
///
/// Every colour comes from CSS custom properties that C# fills in from the LIVE theme
/// dictionary, so this page follows Classic/Liquid Glass and light/dark exactly like the
/// XAML does, with no colour hardcoded here.
/// </summary>
internal static class StartPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Aviary</title>
<style>
  :root{
    color-scheme: light dark;
    --bg:#FFFFFF; --ink:rgba(0,0,0,.85); --ink-2:rgba(0,0,0,.5); --ink-3:rgba(0,0,0,.32);
    --well:rgba(0,0,0,.045); --well-hover:rgba(0,0,0,.075);
    --line:rgba(0,0,0,.1); --accent:#007AFF; --card:rgba(255,255,255,.72);
    --rib:rgba(0,0,0,.07); --sky:rgba(0,122,255,.05);
    --tile-radius:14px;
  }
  /*__AVIARY_THEME__*/

  *{box-sizing:border-box;}
  html,body{height:100%;margin:0;}
  body{
    background:var(--bg); color:var(--ink);
    font:400 14px/1.55 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;
    letter-spacing:-.006em; -webkit-user-select:none; user-select:none;
    overflow-x:hidden;
  }
  ::selection{background:color-mix(in srgb,var(--accent) 20%,transparent);}
  ::-webkit-scrollbar{width:10px;}
  ::-webkit-scrollbar-thumb{background:rgba(128,128,128,.3);border-radius:99px;border:3px solid transparent;background-clip:content-box;}
  ::-webkit-scrollbar-track{background:transparent;}

  /* The glasshouse. Fixed, behind everything, pointer-transparent. */
  #sky{position:fixed;inset:0;z-index:0;pointer-events:none;opacity:.85;}
  #sky svg{width:100%;height:100%;display:block;}

  main{position:relative;z-index:1;width:min(920px,100% - 80px);margin:0 auto;padding:64px 0 120px;}

  .mark{display:flex;align-items:center;gap:11px;margin-bottom:38px;}
  .mark svg{width:26px;height:26px;}
  .mark b{font-size:17px;font-weight:600;letter-spacing:-.018em;}

  h2{
    font-size:13px;font-weight:600;letter-spacing:-.002em;color:var(--ink-2);
    margin:34px 0 14px;
  }
  section:first-of-type h2{margin-top:0;}

  /* Favourites. A fixed square well so nothing reflows when an icon arrives late. */
  .grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(96px,1fr));gap:18px 8px;}
  .tile{
    display:flex;flex-direction:column;align-items:center;gap:8px;
    text-decoration:none;color:inherit;padding:0;border:0;background:none;
    cursor:default;font:inherit;
  }
  .tile__well{
    width:64px;height:64px;border-radius:var(--tile-radius);
    background:var(--well);border:1px solid var(--line);
    display:grid;place-items:center;
    transition:transform 150ms cubic-bezier(.32,.72,0,1),
               background-color 150ms cubic-bezier(.25,.1,.25,1),
               box-shadow 150ms cubic-bezier(.25,.1,.25,1);
  }
  .tile:hover .tile__well{transform:translateY(-2px);background:var(--well-hover);
    box-shadow:0 4px 14px rgba(0,0,0,.12);}
  .tile:active .tile__well{transform:scale(.97);}
  /* contain, never cover: a non-square icon is letterboxed rather than stretched. */
  .tile__well img{width:40px;height:40px;object-fit:contain;}
  .tile__mono{
    width:40px;height:40px;border-radius:10px;display:grid;place-items:center;
    color:#fff;font-weight:600;font-size:17px;
  }
  .tile__name{
    font-size:12px;line-height:1.28;text-align:center;color:var(--ink-2);
    max-width:88px;overflow:hidden;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;
  }

  /* The Collection: Grant's own projects, bundled with Aviary (Library.cs). A row of the
     exhibits catalog.json marks featured, each a window onto its thumbnail — its initials in
     its own colour until the image arrives, or if it never does — then the way into all of it. */
  .mg{display:grid;grid-auto-flow:column;grid-auto-columns:172px;gap:12px;overflow-x:auto;padding:2px 2px 8px;scrollbar-width:thin;}
  .mg__card{
    position:relative;display:flex;flex-direction:column;gap:2px;border:1px solid var(--line);border-radius:12px;
    background:var(--card);padding:0 0 10px;overflow:hidden;cursor:default;text-align:left;font:inherit;color:inherit;
    transition:transform 150ms cubic-bezier(.32,.72,0,1),box-shadow 150ms;
  }
  .mg__card:hover{transform:translateY(-2px);box-shadow:0 4px 14px rgba(0,0,0,.12);}
  .mg__win{position:relative;display:block;aspect-ratio:16/10;background:color-mix(in srgb,var(--c) 20%,var(--well));border-bottom:1px solid var(--line);}
  .mg__win img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;object-position:top center;}
  .mg__mono{position:absolute;inset:0;display:grid;place-items:center;font:700 26px/1 "Bahnschrift","Segoe UI",sans-serif;color:var(--c);}
  .mg__t{padding:0 11px;margin-top:7px;font-size:12.5px;font-weight:600;line-height:1.3;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
  .mg__n{padding:0 11px;font-size:11.5px;color:var(--ink-2);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
  .mg__all{
    display:grid;place-items:center;align-content:center;gap:4px;border:1px dashed var(--line);border-radius:12px;
    background:transparent;color:var(--ink-2);font:500 12.5px/1.35 inherit;cursor:default;text-align:center;padding:12px;
  }
  .mg__all:hover{background:var(--well);color:var(--ink);}
  .mg__all b{font:700 26px/1 "Bahnschrift","Segoe UI",sans-serif;color:var(--ink);}

  /* Reading list, a horizontal row of cards. */
  .rl{display:flex;gap:10px;overflow-x:auto;padding-bottom:6px;scrollbar-width:thin;}
  .rl__card{
    flex:0 0 210px;border:1px solid var(--line);border-radius:12px;background:var(--card);
    padding:12px 13px;display:flex;flex-direction:column;gap:6px;cursor:default;
    text-align:left;font:inherit;color:inherit;
    transition:transform 150ms cubic-bezier(.32,.72,0,1),box-shadow 150ms;
  }
  .rl__card:hover{transform:translateY(-2px);box-shadow:0 4px 14px rgba(0,0,0,.1);}
  .rl__head{display:flex;align-items:center;gap:7px;}
  .rl__head img{width:16px;height:16px;object-fit:contain;flex:0 0 auto;}
  .rl__dot{width:7px;height:7px;border-radius:50%;background:var(--accent);flex:0 0 auto;}
  .rl__host{font-size:11.5px;color:var(--ink-3);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
  .rl__title{font-size:13px;font-weight:500;line-height:1.35;
    display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;}
  .rl__age{font-size:11px;color:var(--ink-3);}

  /* Privacy report. */
  .privacy{
    border:1px solid var(--line);border-radius:14px;background:var(--card);
    padding:18px 20px;cursor:default;
  }
  .privacy__line{font-size:14.5px;line-height:1.5;}
  .privacy__line b{font-weight:600;}
  .privacy__sub{font-size:12.5px;color:var(--ink-2);margin-top:5px;}
  /* Columns are content-sized and bottom-aligned, so the weekday labels share a baseline
     and each bar grows upward from it by its own pixel height. */
  .chart{display:flex;align-items:flex-end;gap:8px;min-height:68px;margin-top:16px;}
  .chart div{flex:1;display:flex;flex-direction:column;align-items:center;gap:6px;}
  .chart i{
    display:block;width:100%;max-width:34px;border-radius:3px;background:var(--ink-3);
    min-height:3px; /* a zero day is a visible tick, not nothing */
    transition:height 420ms cubic-bezier(.32,.72,0,1);
  }
  .chart .today i{background:var(--accent);}
  .chart span{font-size:10.5px;color:var(--ink-3);}
  .privacy[hidden]{display:none;}

  /* The report's head: its sentence, and the 7 days / All time switch on the right. */
  .privacy__head{display:flex;align-items:flex-start;gap:16px;}
  .privacy__head .privacy__line{flex:1;min-width:0;}
  .range{display:inline-flex;flex:0 0 auto;padding:2px;border-radius:8px;background:var(--well);}
  .range button{
    border:0;background:transparent;color:var(--ink-2);font:500 12px/1 inherit;cursor:default;
    padding:6px 10px;border-radius:6px;transition:background-color 140ms,color 140ms;
  }
  .range button[aria-pressed="true"]{background:var(--card);color:var(--ink);box-shadow:0 1px 2px rgba(0,0,0,.12);}

  /* Brave's three figures, in the Start Page's own system hues: blocked in orange,
     bandwidth in indigo, time in the label ink. */
  .stats{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:14px;margin-top:16px;
    padding-top:14px;border-top:1px solid var(--line);}
  .stat b{display:block;font-size:24px;font-weight:600;letter-spacing:-.02em;line-height:1.15;
    font-variant-numeric:tabular-nums;white-space:nowrap;}
  .stat b small{font-size:13px;font-weight:600;margin-left:3px;letter-spacing:0;}
  .stat span{display:block;font-size:12px;color:var(--ink-2);margin-top:3px;}
  .stat--blocked b{color:#FF9500;}
  .stat--bw b{color:#5856D6;}
  @media (prefers-color-scheme:dark){ .stat--bw b{color:#8E8CF0;} }
  .privacy__note{font-size:11px;color:var(--ink-3);margin-top:10px;}
  .chart__label{font-size:10.5px;color:var(--ink-3);margin-top:14px;}

  .empty{color:var(--ink-3);font-size:13px;padding:2px 0 6px;}

  /* A private window's Start Page opens with what private browsing does and does not do. */
  .private{border:1px solid var(--line);border-radius:14px;background:var(--card);
    padding:16px 20px;margin:0 0 30px;display:flex;gap:14px;align-items:flex-start;}
  .private svg{flex:0 0 auto;width:22px;height:22px;margin-top:2px;}
  .private b{display:block;font-size:14.5px;font-weight:600;margin-bottom:3px;}
  .private span{display:block;font-size:12.5px;color:var(--ink-2);line-height:1.55;}

  /* Customise, bottom-right. */
  .cust{position:fixed;right:22px;bottom:22px;z-index:3;}
  .cust__btn{
    width:34px;height:34px;border-radius:9px;border:1px solid var(--line);
    background:var(--card);display:grid;place-items:center;cursor:default;
    transition:background-color 140ms;
  }
  .cust__btn:hover{background:var(--well-hover);}
  .cust__pop{
    position:absolute;right:0;bottom:42px;width:232px;
    border:1px solid var(--line);border-radius:12px;background:var(--bg);
    box-shadow:0 2px 6px rgba(0,0,0,.1),0 16px 44px rgba(0,0,0,.24);
    padding:7px;transform-origin:bottom right;
    animation:pop 160ms cubic-bezier(.32,.72,0,1);
  }
  /* AFTER the rule above: the browser hides [hidden] from its own stylesheet, which any
     author rule setting display outranks. Omit this and the popover is open on load and
     nothing can close it — the exact bug that shipped in the settings dropdowns. */
  .cust__pop[hidden]{display:none;}
  @keyframes pop{from{opacity:0;transform:scale(.96) translateY(4px);}to{opacity:1;transform:none;}}
  .cust__row{
    display:flex;align-items:center;gap:10px;width:100%;border:0;background:none;
    font:inherit;color:inherit;padding:0 9px;height:32px;border-radius:7px;cursor:default;
    text-align:left;font-size:13px;
  }
  .cust__row:hover{background:var(--well);}
  .cust__tick{width:15px;height:15px;flex:0 0 auto;}
  .cust__tick path{
    stroke:var(--accent);stroke-width:1.9;fill:none;stroke-linecap:round;stroke-linejoin:round;
    stroke-dasharray:14;stroke-dashoffset:14;transition:stroke-dashoffset 170ms cubic-bezier(.32,.72,0,1) 30ms;
  }
  .cust__row[aria-checked="true"] .cust__tick path{stroke-dashoffset:0;}

  @media (prefers-reduced-motion:reduce){
    *,*::before,*::after{transition-duration:1ms!important;animation-duration:1ms!important;}
  }
</style>
</head>
<body>
<div id="sky" aria-hidden="true"></div>

<main>
  <div class="mark">
    <svg viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <path d="M3 20V12.6A9 9 0 0 1 21 12.6V20" stroke="var(--accent)" stroke-width="1.5" stroke-linecap="round"/>
      <path d="M12 3.6V20M7.6 5.1V20M16.4 5.1V20M3 14.4h18" stroke="var(--accent)" stroke-width=".9" stroke-linecap="round" opacity=".5"/>
      <path d="M2.2 20h19.6" stroke="var(--accent)" stroke-width="1.5" stroke-linecap="round"/>
    </svg>
    <b>Aviary</b>
  </div>
  <div id="panes"></div>
</main>

<div class="cust">
  <button class="cust__btn" id="custBtn" aria-expanded="false" title="Customise this page">
    <svg width="17" height="17" viewBox="0 0 17 17" fill="none" aria-hidden="true">
      <path d="M2.5 4.6h12M2.5 8.5h12M2.5 12.4h12" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/>
      <circle cx="6" cy="4.6" r="2" fill="var(--bg)" stroke="currentColor" stroke-width="1.6"/>
      <circle cx="11" cy="8.5" r="2" fill="var(--bg)" stroke="currentColor" stroke-width="1.6"/>
      <circle cx="6.6" cy="12.4" r="2" fill="var(--bg)" stroke="currentColor" stroke-width="1.6"/>
    </svg>
  </button>
  <div class="cust__pop" id="custPop" hidden></div>
</div>

<script>
/*__AVIARY_START__*/
var D = (typeof window.__aviaryStart === 'object' && window.__aviaryStart) || {};
var ICONS = D.iconBase || '';
var SHOW = D.sections || {favourites:true, menagerie:true, frequent:true, reading:true, privacy:true};

/* Every message carries this page's secret, which the host checks: the host's handler stays
   attached after the tab moves on to a website, and only Aviary's own page knows the token. */
function post(o){ try { o.token = D.token || ''; window.chrome.webview.postMessage(JSON.stringify(o)); } catch(e){} }
function esc(s){ return String(s == null ? '' : s).replace(/[&<>"']/g,
  function(c){ return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]; }); }

/* ── the glasshouse ─────────────────────────────────────────────────────────
   Generated rather than shipped. A barrel vault seen from inside: ribs
   converging on a vanishing point, glazing bars crossing them, all in one
   near-invisible ink so it reads as texture and never competes with content. */
(function sky(){
  var W = 1600, H = 900, cx = W/2, cy = H*0.86, r = W*0.62, parts = [];
  parts.push('<defs><linearGradient id="g" x1="0" y1="0" x2="0" y2="1">' +
             '<stop offset="0" stop-color="var(--sky)"/>' +
             '<stop offset="1" stop-color="transparent"/></linearGradient></defs>');
  parts.push('<path d="M0,' + cy + ' A' + r + ',' + (r*0.82) + ' 0 0 1 ' + W + ',' + cy +
             ' L' + W + ',' + H + ' L0,' + H + 'Z" fill="url(#g)"/>');

  /* ribs */
  for (var i = -7; i <= 7; i++){
    var t = i / 7;
    var x = cx + t * r * 1.02;
    var top = cy - Math.cos(t * Math.PI/2) * r * 0.82;
    parts.push('<path d="M' + x.toFixed(1) + ',' + H + ' L' + x.toFixed(1) + ',' + top.toFixed(1) +
               '" stroke="var(--rib)" stroke-width="1.2" fill="none"/>');
  }
  /* glazing bars: arcs of decreasing radius */
  for (var k = 1; k <= 6; k++){
    var rr = r * (0.82 - k * 0.108);
    if (rr < 40) { break; }
    parts.push('<path d="M' + (cx - r*1.02) + ',' + cy + ' A' + (r*1.02) + ',' + rr +
               ' 0 0 1 ' + (cx + r*1.02) + ',' + cy + '" stroke="var(--rib)" stroke-width="1" fill="none"/>');
  }
  parts.push('<path d="M0,' + cy + ' L' + W + ',' + cy + '" stroke="var(--rib)" stroke-width="1.6"/>');

  document.getElementById('sky').innerHTML =
    '<svg viewBox="0 0 ' + W + ' ' + H + '" preserveAspectRatio="xMidYMax slice">' + parts.join('') + '</svg>';
})();

/* ── tiles ─────────────────────────────────────────────────────────────── */
function tile(item){
  var art = item.hasIcon
    ? '<img src="' + ICONS + encodeURIComponent(item.key) + '" alt="" loading="lazy" ' +
      'onerror="this.replaceWith(mono(' + JSON.stringify(item.mono).replace(/"/g,'&quot;') + ',' + item.hue + '))">'
    : '<span class="tile__mono" style="background:' + hue(item.hue) + '">' + esc(item.mono) + '</span>';

  return '<button class="tile" data-url="' + esc(item.url) + '" title="' + esc(item.url) + '">' +
           '<span class="tile__well">' + art + '</span>' +
           '<span class="tile__name">' + esc(item.title) + '</span>' +
         '</button>';
}

/* The same eight hues the XAML monogram chips use. Duplicated deliberately: an HTML
   surface cannot read the XAML dictionary. Themes/Classic.xaml is the source of truth. */
var HUES = ['#007AFF','#FF9500','#34C759','#FF2D55','#AF52DE','#30B0C7','#5856D6','#A2845E'];
function hue(i){ return HUES[(i|0) % HUES.length]; }
window.mono = function(text, h){
  var s = document.createElement('span');
  s.className = 'tile__mono'; s.style.background = hue(h); s.textContent = text;
  return s;
};

function section(id, heading, body){
  if (!SHOW[id]) { return ''; }
  return '<section data-sec="' + id + '"><h2>' + esc(heading) + '</h2>' + body + '</section>';
}

function render(){
  var out = '';

  if (D.isPrivate){
    out += '<div class="private"><svg viewBox="0 0 22 22" fill="none" aria-hidden="true">' +
      '<path d="M2.5 11c2.3-3.8 5.2-5.6 8.5-5.6s6.2 1.8 8.5 5.6c-2.3 3.8-5.2 5.6-8.5 5.6S4.8 14.8 2.5 11Z" stroke="var(--ink-2)" stroke-width="1.5" stroke-linejoin="round"/>' +
      '<path d="M4 18.5 18 3.5" stroke="var(--ink-2)" stroke-width="1.5" stroke-linecap="round"/></svg>' +
      '<div><b>Private Browsing</b><span>Aviary won’t remember the pages you visit, your searches or what you type ' +
      'after you close this window, and sites can’t read the cookies from your normal window. ' +
      'Files you download and bookmarks you add are kept. Websites, your network and your internet ' +
      'provider can still see what you do.</span></div></div>';
  }

  var favs = D.favourites || [];
  out += section('favourites', 'Favourites',
    favs.length ? '<div class="grid">' + favs.map(tile).join('') + '</div>'
                : '<p class="empty">Press Ctrl+D on a page to keep it here.</p>');

  /* Grant's projects, offline, from Aviary's own folder — absent in a build without them. */
  var mg = D.menagerie;
  if (mg && mg.items && mg.items.length){
    out += section('menagerie', 'The Collection', '<div class="mg">' + mg.items.map(function(it){
      var w = String(it.title).split(/\s+/);
      var initials = it.mono || ((w[0] || '?').charAt(0) + (w[1] ? w[1].charAt(0) : '')).toUpperCase();
      return '<button class="mg__card" data-url="' + esc(it.url) + '" title="' + esc(it.title) + '" style="--c:' + esc(it.accent) + '">' +
               '<span class="mg__win"><span class="mg__mono">' + esc(initials) + '</span>' +
               '<img src="' + esc(it.thumb) + '" alt="" loading="lazy" onerror="this.remove()"></span>' +
               '<span class="mg__t">' + esc(it.title) + '</span><span class="mg__n">' + esc(it.note) + '</span>' +
             '</button>';
    }).join('') +
      '<button class="mg__all" data-url="' + esc(mg.home) + '"><b>' + (mg.count | 0) + '</b>exhibits in the Collection &rarr;</button></div>');
  }

  var freq = D.isPrivate ? [] : (D.frequent || []);
  if (freq.length){
    out += section('frequent', 'Frequently Visited', '<div class="grid">' + freq.map(tile).join('') + '</div>');
  }

  var rl = D.reading || [];
  if (rl.length){
    out += section('reading', 'Reading List', '<div class="rl">' + rl.map(function(r){
      return '<button class="rl__card" data-url="' + esc(r.url) + '">' +
               '<span class="rl__head">' +
                 (r.unread ? '<i class="rl__dot"></i>' : '') +
                 (r.hasIcon ? '<img src="' + ICONS + encodeURIComponent(r.key) + '" alt="">' : '') +
                 '<span class="rl__host">' + esc(r.host) + '</span>' +
               '</span>' +
               '<span class="rl__title">' + esc(r.title) + '</span>' +
               '<span class="rl__age">' + esc(r.age) + '</span>' +
             '</button>';
    }).join('') + '</div>');
  }

  var p = D.privacy || {total:0, days:[], top:''};
  if (D.isPrivate) { SHOW.privacy = false; }
  var all = p.range === 'all';
  var blocked = all ? (p.blocked || 0) : (p.total || 0);
  var lead = !all ? 'In the last seven days, Aviary has prevented '
           : p.since ? 'Since ' + esc(longDate(p.since)) + ', Aviary has prevented '
           : 'Aviary has prevented ';
  out += section('privacy', 'Privacy Report',
    '<div class="privacy">' +
      '<div class="privacy__head">' +
        '<div class="privacy__line">' + lead +
          '<b id="pcount">0</b> ' + (blocked === 1 ? 'tracker' : 'trackers') + ' from profiling you.</div>' +
        '<div class="range" role="group" aria-label="Report range">' +
          '<button data-range="week" aria-pressed="' + (all ? 'false' : 'true') + '">7 days</button>' +
          '<button data-range="all" aria-pressed="' + (all ? 'true' : 'false') + '">All time</button>' +
        '</div>' +
      '</div>' +
      (p.top ? '<div class="privacy__sub">Most often: ' + esc(p.top) + '</div>' : '') +
      '<div class="stats">' +
        '<div class="stat stat--blocked"><b id="sblocked">0</b><span>Trackers &amp; ads blocked</span></div>' +
        '<div class="stat stat--bw"><b>' + sizeHtml(p.bytes || 0) + '</b><span>Bandwidth saved</span></div>' +
        '<div class="stat stat--time"><b>' + timeHtml(p.seconds || 0) + '</b><span>Time saved</span></div>' +
      '</div>' +
      (all ? '<div class="chart__label">Last 7 days</div>' : '') +
      '<div class="chart" id="chart"></div>' +
      '<div class="privacy__note">Bandwidth and time are estimates: a request that was never made has no real size.</div>' +
    '</div>');

  document.getElementById('panes').innerHTML = out;
  drawChart(p);
  countUp('pcount', blocked);
  countUp('sblocked', blocked);
}

/* "670.8 MB" — one decimal, the unit smaller beside it, as Brave sets it. */
function sizeHtml(bytes){
  var units = ['B','KB','MB','GB','TB'], v = bytes, u = 0;
  while (v >= 1024 && u < units.length - 1){ v /= 1024; u++; }
  var n = u === 0 ? String(Math.round(v)) : v.toFixed(1);
  return esc(n) + '<small>' + units[u] + '</small>';
}

/* "2.1 Hours". Seconds under a minute, minutes under an hour, days past two of them. */
function timeHtml(seconds){
  var s = Math.max(0, seconds || 0);
  /* One decimal under ten seconds: four blocks are 0.2 s, not "0 Seconds". */
  if (s < 10) { var t = s === 0 ? '0' : s.toFixed(1); return esc(t) + '<small>' + (t === '1.0' ? 'Second' : 'Seconds') + '</small>'; }
  if (s < 60) { return esc(String(Math.round(s))) + '<small>Seconds</small>'; }
  if (s < 3600) { return esc((s / 60).toFixed(1)) + '<small>Minutes</small>'; }
  if (s < 172800) { return esc((s / 3600).toFixed(1)) + '<small>Hours</small>'; }
  return esc((s / 86400).toFixed(1)) + '<small>Days</small>';
}

function longDate(iso){
  var d = new Date(iso + 'T00:00:00');
  if (isNaN(d.getTime())) { return iso; }
  return new Intl.DateTimeFormat(undefined, {day:'numeric', month:'long', year:'numeric'}).format(d);
}

function drawChart(p){
  var el = document.getElementById('chart');
  if (!el) { return; }
  var days = p.days || [];
  var max = Math.max(1, Math.max.apply(null, days.concat([1])));
  var names = [];
  var fmt = new Intl.DateTimeFormat(undefined, {weekday:'narrow'});
  var now = new Date(D.now || Date.now());
  for (var i = days.length - 1; i >= 0; i--){
    var d = new Date(now.getTime() - i * 86400000);
    names.push(fmt.format(d));
  }
  el.innerHTML = days.map(function(v, i){
    var last = i === days.length - 1;
    return '<div class="' + (last ? 'today' : '') + '">' +
             /* PIXELS, not percent. A percentage height resolves against the parent's
                height, and each column is content-sized, so every bar collapsed to its 3px
                minimum no matter the data — invisible for as long as the count was zero,
                which is the only state anyone had looked at. Found in Phase 5 M0 when a
                real 23 still drew a flat line. */
             '<i style="height:' + Math.max(3, Math.round((v / max) * 44)) + 'px"></i>' +
             '<span>' + esc(names[i]) + '</span>' +
           '</div>';
  }).join('');
}

/* Integer steps, so it never shows a fraction of a tracker; thousands grouped, "149,871". */
function countUp(id, target){
  var el = document.getElementById(id);
  if (!el) { return; }
  var fmt = new Intl.NumberFormat();
  if (target <= 0){ el.textContent = '0'; return; }
  var start = performance.now(), dur = 520;
  function frame(t){
    var k = Math.min(1, (t - start) / dur);
    el.textContent = fmt.format(Math.round(target * (1 - Math.pow(1 - k, 3))));
    if (k < 1) { requestAnimationFrame(frame); }
  }
  requestAnimationFrame(frame);
}

/* The 7 days / All time switch: the same setting as the row in Settings → Privacy. */
document.addEventListener('click', function(e){
  var b = e.target.closest('.range button[data-range]');
  if (!b) { return; }
  e.stopPropagation();
  var range = b.getAttribute('data-range');
  if ((D.privacy || {}).range === range) { return; }
  post({type:'privacyRange', value:range});
}, true);

/* ── customise ─────────────────────────────────────────────────────────── */
/* `menagerie` is the Collection's section id and settings key (startMenagerie) from before its
   rename — kept so a saved choice survives. Only the words changed. */
var LABELS = {favourites:'Favourites', menagerie:'The Collection', frequent:'Frequently Visited',
              reading:'Reading List', privacy:'Privacy Report'};
if (!D.menagerie) { delete LABELS.menagerie; }

function buildCust(){
  var pop = document.getElementById('custPop');
  pop.innerHTML = Object.keys(LABELS).map(function(k){
    return '<button class="cust__row" data-sec="' + k + '" role="switch" aria-checked="' +
           (SHOW[k] ? 'true' : 'false') + '">' +
             '<svg class="cust__tick" viewBox="0 0 15 15"><path d="M3 8l3 3 6-7"/></svg>' +
             esc(LABELS[k]) +
           '</button>';
  }).join('');
}

document.getElementById('custBtn').addEventListener('click', function(e){
  e.stopPropagation();
  var pop = document.getElementById('custPop');
  var opening = pop.hidden;
  pop.hidden = !opening;
  this.setAttribute('aria-expanded', opening ? 'true' : 'false');
});

document.getElementById('custPop').addEventListener('click', function(e){
  var row = e.target.closest('.cust__row');
  if (!row) { return; }
  /* Safari's popover does NOT close on each tick — you set several then dismiss. */
  e.stopPropagation();
  var k = row.getAttribute('data-sec');
  SHOW[k] = !SHOW[k];
  row.setAttribute('aria-checked', SHOW[k] ? 'true' : 'false');
  post({type:'startSection', key:k, value:SHOW[k]});
  render();
});

document.addEventListener('click', function(){
  var pop = document.getElementById('custPop');
  if (!pop.hidden){
    pop.hidden = true;
    document.getElementById('custBtn').setAttribute('aria-expanded','false');
  }
});

document.addEventListener('keydown', function(e){
  if (e.key === 'Escape'){
    document.getElementById('custPop').hidden = true;
    document.getElementById('custBtn').setAttribute('aria-expanded','false');
  }
});

/* ── navigation and context menu ───────────────────────────────────────── */
document.addEventListener('click', function(e){
  var t = e.target.closest('[data-url]');
  if (t) { post({type:'navigate', url:t.getAttribute('data-url')}); }
});

document.addEventListener('contextmenu', function(e){
  var t = e.target.closest('.tile[data-url]');
  if (!t) { return; }
  e.preventDefault();
  post({type:'tileMenu', url:t.getAttribute('data-url'),
        x:Math.round(e.clientX), y:Math.round(e.clientY)});
});

buildCust();
render();
</script>
</body>
</html>
""";
}
