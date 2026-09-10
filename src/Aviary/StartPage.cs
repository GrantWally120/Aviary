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

  .empty{color:var(--ink-3);font-size:13px;padding:2px 0 6px;}

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
var SHOW = D.sections || {favourites:true, frequent:true, reading:true, privacy:true};

function post(o){ try { window.chrome.webview.postMessage(JSON.stringify(o)); } catch(e){} }
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

  var favs = D.favourites || [];
  out += section('favourites', 'Favourites',
    favs.length ? '<div class="grid">' + favs.map(tile).join('') + '</div>'
                : '<p class="empty">Press Ctrl+D on a page to keep it here.</p>');

  var freq = D.frequent || [];
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
  out += section('privacy', 'Privacy Report',
    '<div class="privacy">' +
      '<div class="privacy__line">In the last seven days, Aviary has prevented ' +
        '<b id="pcount">0</b> ' + (p.total === 1 ? 'tracker' : 'trackers') + ' from profiling you.</div>' +
      (p.top ? '<div class="privacy__sub">Most often: ' + esc(p.top) + '</div>' : '') +
      '<div class="chart" id="chart"></div>' +
    '</div>');

  document.getElementById('panes').innerHTML = out;
  drawChart(p);
  countUp(p.total || 0);
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

/* Integer steps, so it never shows a fraction of a tracker. */
function countUp(target){
  var el = document.getElementById('pcount');
  if (!el) { return; }
  if (target <= 0){ el.textContent = '0'; return; }
  var start = performance.now(), dur = 520;
  function frame(t){
    var k = Math.min(1, (t - start) / dur);
    el.textContent = Math.round(target * (1 - Math.pow(1 - k, 3)));
    if (k < 1) { requestAnimationFrame(frame); }
  }
  requestAnimationFrame(frame);
}

/* ── customise ─────────────────────────────────────────────────────────── */
var LABELS = {favourites:'Favourites', frequent:'Frequently Visited',
              reading:'Reading List', privacy:'Privacy Report'};

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
