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
  /* A list's credit sits under its counts, not run on after them (M4.5). */
  .more__body li small{display:block;margin-top:2px;}

  /* Shields, M4.5: what it stopped, each site's own setting, and your own rules. */
  .shx{display:grid;gap:16px;}
  .shx__n{display:flex;gap:22px;flex-wrap:wrap;}
  .shx__n > div{min-width:120px;}
  .shx__n b{display:block;font:600 21px/1.15 inherit;color:var(--ink);font-variant-numeric:tabular-nums;}
  .shx__n span{display:block;font-size:11.5px;color:var(--ink-2);}
  .shx__n span + span{color:var(--ink-3);}
  .shx__days{display:grid;grid-template-columns:repeat(7,1fr);gap:6px;align-items:end;height:56px;}
  .shx__days i{display:block;border-radius:3px 3px 1px 1px;background:var(--accent);opacity:.85;min-height:2px;
    transition:height .38s cubic-bezier(.32,.72,0,1);}
  [data-motion="off"] .shx__days i{transition:none;}
  .shx__lab{display:grid;grid-template-columns:repeat(7,1fr);gap:6px;font-size:10.5px;color:var(--ink-3);text-align:center;}
  .shx__two{display:grid;grid-template-columns:1fr 1fr;gap:20px;}
  .shx h4{margin:0 0 7px;font:600 12px/1 inherit;color:var(--ink-2);}
  .shx ol{margin:0;padding:0;list-style:none;display:grid;gap:4px;}
  .shx ol li{display:flex;gap:10px;align-items:baseline;font-size:12px;color:var(--ink);}
  .shx ol li span{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
  .shx ol li i{font-style:normal;color:var(--ink-2);font-variant-numeric:tabular-nums;}
  .shx__none{font-size:12.5px;color:var(--ink-2);}
  @media (max-width:760px){ .shx__two{grid-template-columns:1fr;} }

  .segsm{display:flex;gap:2px;background:var(--fill);border-radius:8px;padding:2px;flex:0 0 auto;}
  .segsm button{border:0;background:transparent;color:var(--ink-2);font:500 11.5px/1 inherit;
    padding:6px 9px;border-radius:6px;cursor:pointer;}
  .segsm button:hover{color:var(--ink);}
  .segsm button[aria-pressed="true"]{background:var(--panel);color:var(--ink);box-shadow:0 1px 2px rgba(0,0,0,.14);}

  .row:has(.ta){flex-wrap:wrap;}
  .ta{flex:1 1 100%;display:grid;gap:8px;}
  .ta textarea{width:100%;box-sizing:border-box;min-height:132px;resize:vertical;padding:10px 12px;
    border:1px solid var(--line);border-radius:9px;background:var(--panel-2);color:var(--ink);
    font:13px/1.55 ui-monospace,"Cascadia Mono",Consolas,monospace;}
  .ta textarea:focus{outline:none;border-color:var(--accent);box-shadow:0 0 0 3px var(--sel);}
  .ta__bar{display:flex;align-items:center;gap:12px;}
  .ta__said{flex:1;min-width:0;font-size:12px;color:var(--ink-2);}
  .mine{display:grid;gap:3px;}
  .mine div{font-size:12px;color:var(--ink-2);}
  .mine b{font-weight:600;color:var(--ink);font-family:ui-monospace,"Cascadia Mono",Consolas,monospace;}
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

  /* ── Updates ──────────────────────────────────────────────────────────────
     The build you are on, its milestones, what it brought, what is coming and every build
     before it. Orange and green only here: pre-release and latest. */
  :root{--warn:#B25000;--ok:#248A3D;}
  @media (prefers-color-scheme: dark){ :root{--warn:#FF9F0A;--ok:#30D158;} }
  .chip{
    font:600 10px/1 ui-monospace,Consolas,monospace;letter-spacing:.1em;text-transform:uppercase;
    color:var(--ink-2);border:1px solid var(--line);border-radius:5px;padding:4px 7px;white-space:nowrap;flex:0 0 auto;
  }
  .chip--code{color:var(--accent);border-color:color-mix(in srgb,var(--accent) 45%,transparent);}
  .chip--pre{color:var(--warn);border-color:color-mix(in srgb,var(--warn) 45%,transparent);}
  .chip--ok{color:var(--ok);border-color:color-mix(in srgb,var(--ok) 45%,transparent);}
  .hero{padding:22px 22px 8px;position:relative;overflow:hidden;}
  /* The dome, faint, behind the build name — the Settings mark at hero size. Sized so its
     base line ends above the milestone bar, never across it. */
  .hero__mark{position:absolute;right:-14px;top:-36px;width:168px;height:168px;opacity:.07;pointer-events:none;}
  .hero__kicker{font-size:11px;font-weight:600;letter-spacing:.07em;text-transform:uppercase;color:var(--ink-2);}
  .hero__title{display:flex;align-items:center;flex-wrap:wrap;gap:8px 10px;margin-top:8px;}
  .hero__title h2{margin:0;font-size:25px;line-height:1.15;font-weight:650;letter-spacing:-.024em;}
  .hero__name{margin-top:4px;font-size:15px;font-weight:500;}
  .hero__meta{margin-top:5px;font-size:12.5px;color:var(--ink-2);-webkit-user-select:text;user-select:text;}
  .bar{display:grid;gap:4px;margin-top:20px;}
  .bar i{height:6px;border-radius:99px;background:var(--fill-hover);}
  .bar i.done{background:var(--accent);}
  .bar i.next{background:color-mix(in srgb,var(--accent) 32%,transparent);}
  .bar__label{display:flex;justify-content:space-between;gap:16px;margin-top:9px;font-size:12px;color:var(--ink-2);}
  .bar__label b{color:var(--ink);font-weight:600;}
  .ms{margin:14px 0 0;padding:0;list-style:none;border-top:1px solid var(--line-soft);}
  .ms li{display:grid;grid-template-columns:22px 38px minmax(0,1fr) auto;align-items:start;gap:4px;padding:10px 0;font-size:13px;}
  .ms li > span{padding-top:1px;}
  .ms__t small{display:block;margin-top:2px;font-size:12px;line-height:1.45;color:var(--ink-2);font-weight:400;}
  .ms li.todo .ms__t small{color:var(--ink-3);}
  .ms li + li{border-top:1px solid var(--line-soft);}
  .ms__dot{width:14px;height:14px;border-radius:50%;border:1.5px solid var(--ink-3);display:grid;place-items:center;}
  .ms__dot svg{width:8px;height:8px;opacity:0;}
  .ms li.done .ms__dot{background:var(--accent);border-color:var(--accent);}
  .ms li.done .ms__dot svg{opacity:1;}
  .ms li.next .ms__dot{border-color:var(--accent);background:var(--accent);box-shadow:inset 0 0 0 2.5px var(--panel);}
  .ms__n{font:600 11px/1 ui-monospace,Consolas,monospace;color:var(--ink-2);}
  .ms li.todo .ms__t{color:var(--ink-2);}
  .ms__w{font-size:12px;color:var(--ink-2);white-space:nowrap;}
  .ms li.next .ms__w{color:var(--accent);font-weight:600;}
  h2.upd-h{font-size:13px;font-weight:600;color:var(--ink-2);margin:28px 2px 10px;letter-spacing:-.004em;}
  .notes{padding:2px 22px 20px;}
  .notes h3{margin:16px 0 8px;font-size:11px;font-weight:600;letter-spacing:.07em;text-transform:uppercase;color:var(--ink-2);}
  .notes h3.warn{color:var(--warn);}
  .notes ul{margin:0;padding:0;list-style:none;display:grid;gap:7px;}
  .notes li{position:relative;padding-left:15px;font-size:13.5px;line-height:1.5;-webkit-user-select:text;user-select:text;}
  .notes li::before{content:"";position:absolute;left:1px;top:.62em;width:5px;height:5px;border-radius:50%;background:var(--ink-3);}
  .notes li b{font-weight:600;}
  .notes kbd{
    font:500 11.5px/1 ui-monospace,Consolas,monospace;color:var(--ink);white-space:nowrap;
    padding:2px 5px;border:1px solid var(--line);border-bottom-width:2px;border-radius:5px;background:var(--fill);
  }
  .bld{margin-bottom:10px;}
  .bld__head{display:flex;align-items:center;gap:14px;padding:13px 18px;border-radius:12px;list-style:none;cursor:default;}
  .bld > summary::-webkit-details-marker{display:none;}
  .bld > summary:hover{background:var(--fill);}
  .bld[open] > summary{border-radius:12px 12px 0 0;}
  .bld__tile{
    width:42px;height:42px;border-radius:10px;flex:0 0 auto;display:grid;place-items:center;
    font:650 12px/1 ui-monospace,Consolas,monospace;color:var(--accent);background:var(--sel);
  }
  .bld--todo .bld__tile{background:transparent;border:1.5px dashed var(--line);color:var(--ink-2);}
  .bld__t{flex:1;min-width:0;}
  .bld__t b{display:block;font-size:14px;font-weight:600;}
  .bld__t span{display:block;font-size:12.5px;color:var(--ink-2);margin-top:1px;}
  .bld__when{font-size:12px;color:var(--ink-2);white-space:nowrap;}
  .bld__chev{width:10px;height:7px;opacity:.5;flex:0 0 auto;transition:transform 160ms cubic-bezier(.32,.72,0,1);}
  .bld[open] .bld__chev{transform:rotate(180deg);}
  .bld__body{padding:0 18px 16px 74px;}
  .bld__body > p{margin:0;font-size:13.5px;-webkit-user-select:text;user-select:text;}
  .bld__body .notes{padding:0;}
  .bits{display:flex;flex-wrap:wrap;gap:6px;margin-top:10px;}
  .bits span{font-size:12px;padding:4px 9px;border-radius:99px;background:var(--fill);color:var(--ink-2);}
  .upd-foot{margin:22px 2px 0;font-size:12.5px;}

  /* ── Storage Usage (M4.5) — one stacked bar, then a row per category ──────
     The eight colours are the data-viz reference palette's categorical slots in their fixed,
     CVD-validated order (validated against these panels: light #FFFFFF, dark #2C2C2E — all
     checks pass; three light slots are under 3:1, so every segment also has a visible label
     in the rows below). A colour belongs to its category, never to its rank. */
  :root{--st-app:#2a78d6;--st-collection:#eb6834;--st-sites:#1baf7a;--st-cache:#eda100;
        --st-yours:#e87ba4;--st-diagnostics:#008300;--st-secrets:#4a3aa7;--st-other:#e34948;}
  @media (prefers-color-scheme: dark){
    :root{--st-app:#3987e5;--st-collection:#d95926;--st-sites:#199e70;--st-cache:#c98500;
          --st-yours:#d55181;--st-diagnostics:#008300;--st-secrets:#9085e9;--st-other:#e66767;}
  }
  .st-hero{padding:18px 18px 16px;position:relative;}
  .st-total{font-size:28px;font-weight:650;letter-spacing:-.022em;font-variant-numeric:tabular-nums;}
  .st-total small{font-size:14px;font-weight:500;color:var(--ink-2);margin-left:4px;}
  .st-sub{font-size:12.5px;color:var(--ink-2);margin-top:2px;}
  .st-bar{display:flex;gap:2px;height:14px;margin-top:16px;}
  .st-bar i{display:block;min-width:3px;height:100%;border-radius:2px;transform-origin:left center;
    transition:filter 120ms,opacity 120ms;animation:bar-in 620ms var(--m-ease) backwards;}
  .st-bar i:first-child{border-top-left-radius:4px;border-bottom-left-radius:4px;}
  .st-bar i:last-child{border-top-right-radius:4px;border-bottom-right-radius:4px;}
  .st-bar:hover i{opacity:.55;}
  .st-bar i:hover{opacity:1;filter:saturate(1.15);}
  .st-tip{position:absolute;pointer-events:none;background:var(--panel);border:1px solid var(--line);border-radius:8px;
    padding:7px 10px;font-size:12px;box-shadow:0 6px 20px rgba(0,0,0,.25);white-space:nowrap;z-index:5;}
  .st-tip b{display:block;font-weight:600;color:var(--ink);}
  .st-tip span{color:var(--ink-2);font-variant-numeric:tabular-nums;}
  .st-tip[hidden]{display:none;}
  .st-cat > summary{display:grid;grid-template-columns:12px minmax(0,1fr) auto 52px 10px;align-items:center;gap:12px;
    padding:12px 18px;list-style:none;cursor:default;}
  .st-cat > summary::-webkit-details-marker{display:none;}
  .st-cat > summary:hover{background:var(--fill);}
  .st-cat + .st-cat{border-top:1px solid var(--line-soft);}
  .st-chip{width:12px;height:12px;border-radius:3px;}
  .st-name b{display:block;font-weight:500;font-size:14px;}
  .st-name span{display:block;font-size:12px;color:var(--ink-2);margin-top:1px;}
  .st-size{font-variant-numeric:tabular-nums;font-weight:600;text-align:right;white-space:nowrap;}
  .st-pct{font-variant-numeric:tabular-nums;color:var(--ink-2);font-size:12.5px;text-align:right;}
  .st-cat[open] .bld__chev{transform:rotate(180deg);}
  .st-body{padding:0 18px 14px 42px;font-size:12.5px;color:var(--ink-2);}
  .st-body p{margin:0 0 8px;}
  .st-parts{display:grid;gap:2px;margin-bottom:10px;}
  .st-parts div{display:flex;gap:12px;padding:5px 10px;border-radius:6px;background:var(--fill);}
  .st-parts b{flex:1;min-width:0;font-weight:500;color:var(--ink);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
  .st-parts i{font-style:normal;font-variant-numeric:tabular-nums;white-space:nowrap;}
  .st-actions{display:flex;gap:8px;flex-wrap:wrap;}
  .st-empty{padding:16px 18px;color:var(--ink-2);font-size:13px;}

  /* ── Performance (M4.5) — four live cards, then the details ─────────────── */
  .pf-cards{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px;margin-bottom:18px;}
  @media (max-width:980px){.pf-cards{grid-template-columns:repeat(2,minmax(0,1fr));}}
  .pf-card{background:var(--panel);border:1px solid var(--line-soft);border-radius:12px;padding:14px 14px 10px;min-width:0;}
  .pf-k{display:block;font-size:11px;font-weight:600;letter-spacing:.06em;text-transform:uppercase;color:var(--ink-2);}
  .pf-v{display:block;font-size:24px;font-weight:650;letter-spacing:-.02em;margin-top:6px;font-variant-numeric:tabular-nums;white-space:nowrap;}
  .pf-v small{font-size:13px;font-weight:500;color:var(--ink-2);margin-left:3px;}
  .pf-s{display:block;font-size:11.5px;color:var(--ink-2);margin-top:1px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
  .pf-spark{display:block;width:100%;height:34px;margin-top:8px;overflow:visible;}
  .pf-spark polyline{fill:none;stroke:var(--accent);stroke-width:1.6;stroke-linejoin:round;stroke-linecap:round;vector-effect:non-scaling-stroke;}
  .pf-spark polygon{fill:color-mix(in srgb,var(--accent) 14%,transparent);stroke:none;}
  .pf-table{padding:4px 0;}
  .pf-row{display:grid;grid-template-columns:minmax(0,1fr) 78px 62px 56px auto;align-items:center;gap:10px;padding:8px 18px;font-size:13px;}
  .pf-row + .pf-row{border-top:1px solid var(--line-soft);}
  .pf-row.pf-head{font-size:11px;font-weight:600;letter-spacing:.05em;text-transform:uppercase;color:var(--ink-2);padding-top:10px;padding-bottom:6px;}
  .pf-row .pf-n{min-width:0;}
  .pf-row .pf-n b{display:block;font-weight:500;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
  .pf-row .pf-n span{display:block;font-size:11.5px;color:var(--ink-2);margin-top:1px;}
  .pf-row .pf-num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap;}
  .pf-row .btns{justify-content:flex-end;min-width:122px;}
  .pf-row .btns button{border:1px solid var(--line);background:var(--fill);color:var(--ink);font:500 12px/1 inherit;padding:6px 10px;border-radius:6px;cursor:default;}
  .pf-row .btns button:hover{background:var(--fill-hover);}
  .pf-pill{display:inline-block;font-size:10.5px;font-weight:600;padding:2px 6px;border-radius:5px;background:var(--fill);color:var(--ink-2);margin-left:6px;vertical-align:1px;}
  .pf-pill--on{color:var(--accent);background:var(--sel);}
  .pf-empty{padding:12px 18px;font-size:12.5px;color:var(--ink-2);}

  /* ── Motion (Alpha Build 5 M4.5) ───────────────────────────────────────────
     <html data-motion> is "flight" (Smooth Flight — Classic's: quick ease-outs, no overshoot),
     "fluid" (Fluid — Liquid Glass's: a spring, drawn with linear()) or "off" (Reduce motion).
     The host sets it on load and again whenever the style changes (Motion.cs). */
  :root{--m-ease:cubic-bezier(.32,.72,0,1);--m-dur:200ms;--m-pane:240ms;--m-rise:8px;interpolate-size:allow-keywords;}
  html[data-motion="fluid"]{--m-ease:linear(0, .006, .025 2.8%, .101 6.1%, .539 18.9%, .721 25.3%, .849 31.5%, .937 38.1%, .968 41.8%, .991 45.7%, 1.006 50.1%, 1.015 55%, 1.017 63.9%, 1.001);--m-dur:360ms;--m-pane:420ms;--m-rise:14px;}
  html[data-motion="off"] *,html[data-motion="off"] *::before,html[data-motion="off"] *::after{transition:none!important;animation:none!important;}
  @media (prefers-reduced-motion:reduce){*,*::before,*::after{transition:none!important;animation:none!important;}}
  /* The selection in the side menu is ONE pill that travels to the item you pick. */
  nav{position:relative;}
  nav button{position:relative;z-index:1;}
  nav button[aria-current="true"]{background:transparent;}
  .nav-pill{position:absolute;left:12px;right:12px;top:0;height:34px;border-radius:7px;background:var(--sel);
    pointer-events:none;z-index:0;transition:transform var(--m-dur) var(--m-ease),height var(--m-dur) var(--m-ease);}
  /* A pane arrives; its rows follow one after another. backwards, not both: once finished an
     animation must leave nothing behind, or every row keeps a stacking context and an open
     dropdown can end up under the row below it. */
  .pane.on{animation:pane-in var(--m-pane) var(--m-ease) backwards;}
  .pane.on .row,.pane.on .bld,.pane.on .hero,.pane.on h2.upd-h{animation:row-in var(--m-pane) var(--m-ease) backwards;animation-delay:calc(var(--i,0) * 16ms);}
  @keyframes pane-in{from{opacity:0;}to{opacity:1;}}
  @keyframes row-in{from{opacity:0;transform:translateY(var(--m-rise));}to{opacity:1;transform:none;}}
  /* Expanding details open and close rather than jump. */
  details.more::details-content,details.bld::details-content{height:0;overflow:clip;
    transition:height var(--m-dur) var(--m-ease),content-visibility var(--m-dur) allow-discrete;}
  details.more[open]::details-content,details.bld[open]::details-content{height:auto;}
  html[data-motion="fluid"] .sel__pop{animation:pop-fluid 340ms var(--m-ease);}
  @keyframes pop-fluid{from{opacity:0;transform:scale(.86) translateY(-8px);}to{opacity:1;transform:none;}}
  .sw,.sw::after{transition-timing-function:var(--m-ease);transition-duration:var(--m-dur);}
  /* Fluid: the knob stretches like gel while it is held. */
  html[data-motion="fluid"] .sw::after{transition-property:transform,width;}
  html[data-motion="fluid"] .sw:active::after{width:24px;}
  html[data-motion="fluid"] .sw[aria-checked="true"]:active::after{transform:translateX(10px);}
  .btn{transition:background-color 120ms,transform var(--m-dur) var(--m-ease);}
  .btn:active{transform:scale(.97);}
  html[data-motion="fluid"] .btn:active{transform:scale(.93);}
  /* The Updates milestone bar fills in, one milestone after another. */
  .bar i{transform-origin:left center;animation:bar-in 560ms var(--m-ease) backwards;animation-delay:calc(var(--i,0) * 45ms + 140ms);}
  @keyframes bar-in{from{transform:scaleX(0);}to{transform:none;}}
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
/*__AVIARY_LISTS__*/
/*__AVIARY_ENGINES__*/
/*__AVIARY_SECTION__*/
var SAVED = (typeof window.__aviarySettings === 'object' && window.__aviarySettings) || {};
/* M4.5: "Keep the lists up to date" was a switch and is now Off/Daily/Weekly. A value saved
   by the switch would otherwise draw as "true", which is not one of the choices. */
if (SAVED.listsAutoUpdate === 'true') { SAVED.listsAutoUpdate = 'Weekly'; }
else if (SAVED.listsAutoUpdate === 'false') { SAVED.listsAutoUpdate = 'Off'; }
/* How this page moves (Motion.cs): the host re-sets it whenever the style changes. */
document.documentElement.dataset.motion = window.__aviaryMotion || 'flight';
/* What the running engine STARTED with, for the switches that only apply at launch. */
var LAUNCH = (typeof window.__aviaryLaunch === 'object' && window.__aviaryLaunch) || {};
var PATHS = (typeof window.__aviaryPaths === 'object' && window.__aviaryPaths) || {};
var DIAG = (typeof window.__aviaryDiag === 'object' && window.__aviaryDiag) || {};
/* The ad blocker's filter lists: versions, dates, counts, the last check (Shields, M3). */
var LISTS = (typeof window.__aviaryLists === 'object' && window.__aviaryLists) || {};
/* The search engines and which can suggest (M4) — SearchEngines.cs, the table the address bar uses. */
var ENGINES = (Array.isArray(window.__aviaryEngines) && window.__aviaryEngines.length)
  ? window.__aviaryEngines : [{name:'DuckDuckGo', suggests:true}];
var RENDERED = {};
var BUILD = (typeof window.__aviaryBuild === 'object' && window.__aviaryBuild) ||
            {version:'?', channel:'?', phaseLabel:'?', phaseNumber:0, names:{}, codes:{}};
/* Rows are gated on the last SHIPPED phase, not the one in progress — see BuildInfo. */
var PHASE = BUILD.phaseShipped | 0;
/* Phases ARE builds: 1–5 are Alpha Build 1–5, 6 and 7 Beta Build 1 and 2. The names come
   from BuildInfo, so this page never re-derives the numbering. */
function nameOf(p){ return (BUILD.names && BUILD.names[p]) || ('Build ' + p); }
function codeOf(p){ return (BUILD.codes && BUILD.codes[p]) || String(p); }
function esc(s){
  return String(s).replace(/[&<>"]/g, function(c){ return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]; });
}

/* Every keyed control's LITERAL default, collected as the page renders and posted up once
   at the end. The host writes back only the keys it has never seen, which is what makes a
   control that saves-but-does-not-reload impossible: registration is a side effect of
   rendering, not a list somebody has to remember to edit. */
var SEED = {};

function lineCount(v){
  var s = String(v == null ? '' : v);
  return s.trim() ? s.replace(/\r\n/g, '\n').split('\n').length : 0;
}
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
  privacy:'<svg viewBox="0 0 16 16" fill="none"><rect x="3.4" y="7" width="9.2" height="6.6" rx="1.6" stroke="currentColor" stroke-width="1.4"/><path d="M5.4 7V5.2a2.6 2.6 0 0 1 5.2 0V7" stroke="currentColor" stroke-width="1.4"/></svg>',
  shields:'<svg viewBox="0 0 16 16" fill="none"><path d="M8 1.8 3.2 3.8v3.9c0 3 2 5.6 4.8 6.4 2.8-.8 4.8-3.4 4.8-6.4V3.8Z" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/><path d="m5.9 8.1 1.5 1.5 2.8-3" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  search:'<svg viewBox="0 0 16 16" fill="none"><circle cx="7.2" cy="7.2" r="4.4" stroke="currentColor" stroke-width="1.4"/><path d="m10.6 10.6 2.8 2.8" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/></svg>',
  ext:'<svg viewBox="0 0 16 16" fill="none"><path d="M6 2.6h4v2a1.4 1.4 0 0 0 2.8 0h.6v10.2H2.6V4.6h.6a1.4 1.4 0 0 0 2.8 0Z" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/></svg>',
  autofill:'<svg viewBox="0 0 16 16" fill="none"><rect x="2.2" y="4" width="11.6" height="8" rx="2" stroke="currentColor" stroke-width="1.4"/><path d="M5 8h6" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/></svg>',
  lang:'<svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="5.8" stroke="currentColor" stroke-width="1.4"/><path d="M2.4 8h11.2M8 2.2c3 3.4 3 8.2 0 11.6-3-3.4-3-8.2 0-11.6Z" stroke="currentColor" stroke-width="1.3"/></svg>',
  down:'<svg viewBox="0 0 16 16" fill="none"><path d="M8 2.6v7M5.4 7l2.6 2.6L10.6 7" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/><path d="M2.8 11.6v.8a1.4 1.4 0 0 0 1.4 1.4h7.6a1.4 1.4 0 0 0 1.4-1.4v-.8" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/></svg>',
  access:'<svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="5.8" stroke="currentColor" stroke-width="1.4"/><circle cx="8" cy="5.4" r="1" fill="currentColor"/><path d="M5.4 7.6h5.2M8 7.6v4M6.2 13l1.8-2.4L9.8 13" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>',
  system:'<svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="2.2" stroke="currentColor" stroke-width="1.4"/><path d="M8 1.6v2M8 12.4v2M1.6 8h2M12.4 8h2M3.5 3.5l1.4 1.4M11.1 11.1l1.4 1.4M12.5 3.5l-1.4 1.4M4.9 11.1l-1.4 1.4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/></svg>',
  reset:'<svg viewBox="0 0 16 16" fill="none"><path d="M13.4 8a5.4 5.4 0 1 1-1.6-3.8" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/><path d="M13 2.4v3.2H9.8" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  updates:'<svg viewBox="0 0 16 16" fill="none"><path d="M7.2 2.4c.5 3 2 4.5 5 5-3 .5-4.5 2-5 5-.5-3-2-4.5-5-5 3-.5 4.5-2 5-5Z" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/><path d="M12.6 1.8v2.6M11.3 3.1h2.6" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/></svg>',
  storage:'<svg viewBox="0 0 16 16" fill="none"><ellipse cx="8" cy="4.4" rx="5.2" ry="1.9" stroke="currentColor" stroke-width="1.3"/><path d="M2.8 4.4v7.2c0 1.05 2.33 1.9 5.2 1.9s5.2-.85 5.2-1.9V4.4M2.8 8c0 1.05 2.33 1.9 5.2 1.9s5.2-.85 5.2-1.9" stroke="currentColor" stroke-width="1.3"/></svg>',
  perf:'<svg viewBox="0 0 16 16" fill="none"><path d="M2.6 11.4a5.6 5.6 0 1 1 10.8 0" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/><path d="m8 9.6 2.6-3.4" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/><circle cx="8" cy="9.8" r=".9" fill="currentColor"/></svg>',
  about:'<svg viewBox="0 0 16 16" fill="none"><circle cx="8" cy="8" r="5.8" stroke="currentColor" stroke-width="1.4"/><path d="M8 7.2v4" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/><circle cx="8" cy="4.9" r=".9" fill="currentColor"/></svg>'
};

/* ══ UPDATES — every build, oldest first ════════════════════════════════════
   What each build brought, in plain words. The STATE of a build — shipped, the one you are
   on, planned — is never written here: it comes from the injected BUILD, the same way the
   row badges do, so this page cannot go on calling a finished build "in progress". Only
   history is written by hand, and history does not go stale.

   Add a build's notes when it ships; bump BuildInfo.MilestonesDone when a milestone does. */
var UPDATES = [
  { phase:1, name:'The shell', when:'6 Sep 2026',
    summary:'A window that launches and browses — the frame everything since is built inside.',
    notes:[
      {h:'Window', items:[
        'Windows 11’s Mica backdrop and rounded corners, with an extended title bar and the real Windows caption buttons',
        'Back, forward, reload and home, and an address bar'
      ]},
      {h:'Browsing', items:[
        'Browses the real web on Microsoft’s WebView2 engine',
        'Introduces itself as Safari from the very first page, so sites serve their Safari versions'
      ]}
    ]},
  { phase:2, name:'Tabs and settings', when:'6 Sep 2026',
    summary:'Tabs the way Safari draws them, and a Settings page for every later feature to live in.',
    notes:[
      {h:'Tabs', items:[
        'Tabs share the whole width of the strip, as Safari’s do',
        'A tab’s icon turns into its close button when you point at it',
        'Every tab keeps its own history, without each one starting its own copy of the browser',
        'New tab, close tab, address bar and next tab: <kbd>Ctrl+T</kbd> <kbd>Ctrl+W</kbd> <kbd>Ctrl+L</kbd> <kbd>Ctrl+Tab</kbd>'
      ]},
      {h:'Settings', items:[
        'An app menu, and Settings in thirteen sections — every row either works or says which build it arrives in'
      ]}
    ]},
  { phase:3, name:'Theming and features', when:'6 Sep 2026',
    summary:'Both Safari looks, switching while you browse, and the first quarter of the features.',
    notes:[
      {h:'Looks', items:[
        '<b>Classic</b> (Safari 15 to 18) and <b>Liquid Glass</b> (Safari 26), switched live — Aviary opens with whichever you left it on',
        'Light and dark follow Windows, and both looks have a high-contrast palette',
        'Liquid Glass is see-through enough to let Windows’ Mica show',
        'Aviary’s own dropdowns — the Windows ones drew white boxes in dark mode'
      ]},
      {h:'Features', items:[
        'Ad and tracker blocking',
        'Find in page, downloads and a favourites bar',
        'Drag tabs to put them in a different order',
        'Memory saver for tabs in the background'
      ]}
    ]},
  { phase:4, name:'Safari features', when:'7 Sep 2026',
    summary:'The build that turned a window into a browser.',
    notes:[
      {h:'New', items:[
        '<b>The Start Page</b>: Favourites, Frequently Visited, your Reading List and a Privacy Report, with a Customise button',
        '<b>The sidebar</b>: tabs, bookmarks, Reading List and history in one panel — and tabs down the side if you like',
        '<b>Reader</b>, from the “Aa” button in the address field, with nine fonts and four backgrounds',
        'Every site’s real icon, everywhere, kept on disk',
        'The Smart Search Field: suggestions from your own history and favourites as you type',
        'History, the Reading List and pinned tabs',
        'Aviary’s own right-click menus, page zoom, and a reload button that becomes stop while a page loads',
        'Clear browsing data',
        'Shortcuts for tabs by number, history, downloads, adding to the Reading List, the sidebar and Reader: <kbd>Ctrl+1</kbd>–<kbd>9</kbd> <kbd>Ctrl+H</kbd> <kbd>Ctrl+J</kbd> <kbd>Ctrl+Shift+D</kbd> <kbd>Ctrl+Shift+L</kbd> <kbd>Ctrl+Shift+R</kbd>'
      ]},
      {h:'Moved to Alpha Build 5', items:[
        'The download progress ring and tab groups, rather than shipping either half-built'
      ]}
    ]},
  { phase:5, name:'Privacy that works',
    summary:'The last alpha — Brave-inspired, and built so that every privacy switch really does what it says.',
    short:'Shields, a real ad blocker you control site by site, joins privacy switches that really do what they say — and Grant’s own projects now live inside Aviary, in the Collection.',
    /* `n` is the milestone's own label: M2.5 was added between M2 and M3 on 13 Sep (Grant's
       "middle update"), so labels are written, not counted. `d` says what it is in plain words. */
    milestones:[
      {n:'M0', t:'Loose ends from Alpha Build 4', when:'10 Sep',
       d:'Everything Alpha Build 4 had only ever compiled was tried in the running app, and the six things that were broken got fixed.'},
      {n:'M1', t:'The diagnostics folder', when:'11 Sep',
       d:'An opt-in record of what Aviary does, kept on this computer, so problems get found and every later change can be measured.'},
      {n:'M2', t:'Every privacy switch made real', when:'13 Sep',
       d:'Each privacy setting genuinely does what it says — cookies, tracking, HTTPS, the private window — tested with the switch on and off.'},
      {n:'M2.5', t:'The Collection', when:'13 Sep',
       d:'The middle update: Grant’s own projects — the whole Wally series and the rest — brought inside Aviary to run offline, with landing pages for the ones a browser can’t run, and room for 500 MB of diagnostics.'},
      {n:'M3', t:'An ad blocker you control site by site', when:'13 Sep',
       d:'A real filter-list blocker built on EasyList and EasyPrivacy, with a shield for every site so you decide where it steps back.'},
      {n:'M4', t:'Brave Search and more safe search engines', when:'14 Sep',
       d:'Brave Search, Mojeek and Ecosia join the list, and suggestions from the engine stay off unless you want them.'},
      {n:'M5', t:'The download progress ring and tab groups',
       d:'Safari’s download ring with a downloads list that remembers, and named groups of tabs you can switch between.'},
      {n:'M6', t:'The Experimental menu, in plain English',
       d:'Twenty opt-in experiments, each explained in everyday words — and not one of them can make Aviary less safe.'},
      {n:'M7', t:'Faster and smoother — and measured',
       d:'Start-up, new tabs, memory and animation tuned, with every improvement proved by the diagnostics numbers rather than by feel.'},
      {n:'M8', t:'Getting ready for Beta Build 1',
       d:'An app icon, a home on GitHub with automatic builds, and everything tidied up for the first beta.'}
    ],
    notes:[
      {h:'Search', items:[
        '<b>Brave Search</b>, <b>Mojeek</b> and <b>Ecosia</b> join DuckDuckGo, Startpage, Bing and Google in Settings → Search engine. DuckDuckGo is still where Aviary starts',
        '<b>Suggestions from your search engine</b> as you type — off until you switch them on, and only ever sent to the engine you chose. Never from a private window, and never a web address',
        'Changing your search engine switches its suggestions off again, so a new engine hears nothing until you say so',
        'The right-click “Search … for” and the private window always use the engine you have chosen'
      ]},
      {h:'Shields — the ad blocker', items:[
        '<b>A real ad blocker.</b> EasyList and EasyPrivacy — the lists behind most ad blockers — stop ads and trackers before they load, so they never learn you were there',
        'The <b>shield in the toolbar</b> shows how many were stopped on the page you are on, and where from, and switches Shields off for a site that doesn’t work with them',
        '<b>Standard or Strict.</b> Standard stops what other companies load into a page. Strict also stops what a site runs itself, and warns you before opening a site that exists only to serve ads or track people',
        'The empty spaces ads leave are hidden too — inside frames as well',
        '“Update now” fetches the newest lists; a weekly refresh can do it for you, off until you switch it on',
        'A new <b>Shields</b> page in Settings, with every site you’ve switched Shields off for'
      ]},
      {h:'The Collection', items:[
        '<b>The Collection</b> — 49 of Grant’s projects inside Aviary, running with no internet: every Wally2TTD from v1.8 to 2.4.2, the Silver prequel, Jurassic Empire, EMBER, The Lost and the rest',
        '<b>Wally2TTD 2.4.2 and Jurassic Empire</b>, both built in Godot, now play right in a tab',
        'New landing pages for Wally2TTD 2.4.1 &amp; 2.4.2, the Wally2TTD Launcher, WallyCore1 and TinyCalc — the things a browser can’t run itself',
        'Open it from the app menu, or from its row on the Start Page',
        'Every exhibit is a copy; the originals in Documents are never touched',
        '<b>Five more exhibits</b> (14 Sep): the PARALLAX site, a Dev Log, Console Spectrum, FileMaker Beta and a battery that charges itself',
        'The Collection wears <b>Liquid Glass</b> now, and every exhibit has <b>a page of its own</b> — its story, its placard, its neighbours by date — with Play one press away'
      ]},
      {h:'Privacy', items:[
        '<b>Third-party cookies are really blocked</b> — the way ads follow you from one site to the next',
        '<b>Global Privacy Control</b> asks every site not to sell or share your data, replacing Do Not Track',
        '<b>Tracking is taken off links</b> — fbclid, gclid, utm_ tags and more — before the next site sees it',
        '<b>HTTPS first</b>, with a clear way through when a site has no secure version',
        'Sites learn which site you came from, never the page you were reading',
        'Your address on your home network is hidden from video-calling features',
        'The Safari identity now holds up: the extra Chromium details page scripts could read are gone',
        'The Privacy Report counts the last seven days or all time, with bandwidth and time saved'
      ]},
      {h:'Browsing', items:[
        'A <b>private window</b> that forgets everything when you close it — <kbd>Ctrl+Shift+N</kbd>',
        'Saved passwords, and suggestions for forms as their own switch, off until you want them',
        '<b>Website data</b>: see what each site has stored, and remove any one of them',
        'Full screen with <kbd>F11</kbd>, and videos that fill the whole screen'
      ]},
      {h:'Aviary', items:[
        '<b>Liquid Glass</b> wears the interface study’s capsule now: a sheen from the top, a rim that catches the light at two corners, soft shadows in three layers, and buttons that dip when you press them',
        '<b>Updates</b> — this page: the build you are on, what every build brought, and what is coming',
        'Builds have names: Alpha Build 1 to 5, then Beta Build 1 and 2',
        '<kbd>Ctrl+,</kbd> opens Settings, the way it does in Safari',
        'Only one Aviary runs at a time — opening it again brings the one already open to the front',
        '“Allow data collection for improvements” keeps a record of what Aviary does in a folder on this computer, so problems can be found. It never leaves the computer and never holds passwords, form contents, cookies or card numbers',
        'That folder can now keep up to 650 MB — it was 200 MB, then 500 MB — so it holds a longer history to learn from'
      ]},
      {h:'Safety fixes', items:[
        'A website could change Aviary’s settings — closed',
        'A page could pretend to press Aviary’s keyboard shortcuts — closed',
        'A website could be told when you changed some settings, such as the zoom level — closed',
        'Blocking now reaches trackers inside frames from other sites'
      ]},
      {h:'Fixes', items:[
        'In the dark theme, websites with no colours of their own came out as black text on dark grey. They now sit on white, as they do in Safari and Chrome, and a new tab shows nothing until its page has drawn',
        'A private window went on searching with the old engine after you changed it in Settings',
        'Visiting an ad or tracking company’s own website showed a blank page instead of the site',
        'Two site icons arriving at the same moment could leave the older one saved',
        'Pinning Aviary to light or dark left parts of it, and its menus, in the other theme’s colours',
        'Pinning a tab failed every time',
        'The Privacy Report’s chart never grew, and the report started again every launch',
        'A site’s own “page not found” page was hidden behind Aviary’s error panel',
        'Every download was followed by a false “page didn’t load” panel',
        'Google’s site icon never loaded',
        'A padlock showed beside a secure connection that had failed',
        'Minimising Aviary in the first moments after it opened could close it',
        'Shutting down or restarting Windows with Aviary open lost your last changes and was counted as a crash',
        'A message such as “can’t reach this page” belonged to the window rather than its tab, so it could appear over the wrong page — each tab now keeps its own',
        'A page that crashes now says so, with a Reload Page button',
        'A setting changed at the very moment Aviary was reading its settings back could fail to save',
        'The improvement data’s summary showed file sizes from the session before',
        'A second copy of Aviary from another folder now says Aviary is already open, instead of sharing its data'
      ]}
    ]},
  { phase:6, name:'Testing 1',
    summary:'Alpha Build 5 again, made properly: everything alpha built, finished and installable — the first build meant to be lived in rather than looked at.',
    bits:['All of alpha, finished','More polish','The features alpha left out','Session restore','Crash reporting','An installer, and updates','Daily-driver testing']},
  { phase:7, name:'Testing 2',
    summary:'The second round of living in it. What goes in is decided by using Beta Build 1 for a while, not by guessing now — smoother, quicker, quieter.',
    bits:['Planned when we get there','New features and ideas','Smoothed','Optimised']}
];
var CURRENT = UPDATES.filter(function(u){ return u.phase === (BUILD.phaseNumber | 0); })[0];
var PROLOGUE = 'Before Alpha Build 1 there was the interface study, on 4 September 2026: both Safari looks ' +
  'against both tab layouts, in light and dark, drawn in a Windows 11 frame before any of Aviary was written.';

/* The Experimental menu (Alpha Build 5, M6) is one more entry in this array.

   A row that is not ready yet carries `phase:N` — the number of the build it arrives in.
   The badge text and the disabled state are BOTH derived from comparing that number
   against the injected build, so nobody writes a build name by hand and no badge can
   outlive the build it names: "Later in AB5" while Alpha Build 5 is a pre-release, "Beta
   Build 1" beyond it. Something deliberately never coming carries `never:'…'`. */
var SECTIONS = [
  { id:'start', icon:'start', name:'Get started', title:'Get started',
    lede:'Aviary is a Safari-inspired browser for Windows 11, built one release at a time. You are on <span style="white-space:nowrap">__AV_CHANNEL__</span>.',
    rows:[
      {t:'On startup', d:'What Aviary opens with when you launch it.', phase:6, ctl:{type:'select', key:'startup', opts:['Start Page','Continue where you left off','A specific page'], value:'Start Page'}},
      {t:'Home button', d:'Show the home button in the toolbar.', ctl:{type:'toggle', key:'homeButton', value:true}},
      {t:'Start Page sections', d:'The same switches as the Customise button in the corner of the Start Page.', ctl:{type:'none'}},
      {t:'Show Favourites', d:'', ctl:{type:'toggle', key:'startFavourites', value:true}},
      {t:'Show The Collection', d:'A row of Grant’s own projects, which Aviary carries and runs offline. All of them are always in the app menu.', ctl:{type:'toggle', key:'startMenagerie', value:true}},
      {t:'Show Frequently Visited', d:'', ctl:{type:'toggle', key:'startFrequent', value:true}},
      {t:'Show Reading List', d:'', ctl:{type:'toggle', key:'startReading', value:true}},
      {t:'Show Privacy Report', d:'', ctl:{type:'toggle', key:'startPrivacy', value:true}},
      /* Was "What works today", hand-written in Alpha Build 3 and still promising Alpha Build
         4's features two builds later. Now it is the current build's own line from UPDATES. */
      {t: CURRENT ? 'What’s new in ' + nameOf(CURRENT.phase) : 'What’s new', d: CURRENT ? CURRENT.short : '', ctl:{type:'goto', target:'updates', label:'See updates'}}
    ]},

  { id:'look', icon:'look', name:'Appearance', title:'Appearance',
    lede:'How Aviary looks. Both Safari looks switch while the browser is running, and whichever you leave it on is the one it opens with next time.',
    rows:[
      {t:'Theme', d:'Follow Windows, or pin Aviary to light or dark.', ctl:{type:'select', key:'theme', opts:['System','Light','Dark'], value:'System'}},
      {t:'Safari look', d:'Classic is the solid unified toolbar of Safari 15–18. Liquid Glass is the floating translucent chrome of Safari 26 — translucent enough to let Windows’ Mica show through.', ctl:{type:'select', key:'look', opts:['Classic','Liquid Glass'], value:'Classic'}},
      {t:'Tab layout', d:'A tab bar across the top, or a vertical sidebar down the side.', phase:4, ctl:{type:'select', key:'tabLayout', opts:['Top bar','Sidebar'], value:'Top bar'}},
      /* Alpha Build 5 M4.5 — Motion.cs. Grant’s names: Smooth Flight is Classic’s way of moving,
         Fluid is Liquid Glass’s. Reduce motion (Accessibility) turns both off. */
      {t:'Animation style', d:'How Aviary moves. <b>Smooth Flight</b> is crisp and quick, like Safari on the Mac. <b>Fluid</b> is springy and gel-like, like Safari 26 — tabs stretch toward where they are going and buttons squish when pressed. Each look has its own, or choose one for both.', ctl:{type:'select', key:'animationStyle', opts:['Match the look','Smooth Flight','Fluid'], value:'Match the look'}},
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

  /* Phase 5 M3 — the ad blocker (FilterEngine.cs, FilterLists.cs, Shields.cs). Brave calls it
     Shields; so does Aviary. Every row here is live. */
  { id:'shields', icon:'shields', name:'Shields', title:'Shields',
    lede:'Aviary’s ad and tracker blocker. It stops ads and trackers before they load — so they never learn you were there — and hides the empty spaces they leave behind. The shield in the toolbar shows what it stopped on the page you are on, and turns it off for a site that doesn’t work with it.',
    rows:[
      {t:'Block ads and trackers', d:'__AV_BLOCKCOUNT__', ctl:{type:'toggle', key:'blockTrackers', value:true}},
      {t:'How much to block', d:'<b>Standard</b> stops ads and trackers that come from other companies — the ones that follow you from site to site — and leaves what a site serves itself alone. <b>Strict</b> also stops the ones a site runs itself, and warns you before opening a site the lists say exists only to serve ads or track people. Strict can break parts of some sites. A site you give a setting of its own keeps it whatever you choose here.', ctl:{type:'select', key:'blockMode', opts:['Standard','Strict'], value:'Standard'}},
      {t:'What Shields stopped', d:'<span id="shline">Counting…</span>', more:'shieldstats', ctl:{type:'button', cmd:'shieldsStatsReset', label:'Start again', danger:true}},
      {t:'Sites with their own Shields', d:'Sites you gave their own setting, from the shield in the toolbar. Everything else follows <b>How much to block</b>.', more:'shieldsites', ctl:{type:'button', cmd:'shieldsSitesReset', label:'Forget all', danger:true}},

      {t:'Ads', d:'EasyList: stops ad servers, and hides the boxes ads sit in.', ctl:{type:'toggle', key:'listAds', value:true}},
      {t:'Trackers', d:'EasyPrivacy: stops analytics, tracking pixels and the scripts that follow you from site to site.', ctl:{type:'toggle', key:'listTrackers', value:true}},
      {t:'Cookie notices', d:'EasyList Cookie List: hides the “we value your privacy” banners, and blocks the services that put them there. It hides the banner rather than agreeing to anything — some sites will keep asking. It is mostly hiding rules, so it is the more expensive of the two: with it and Social widgets both on, the hiding script every page runs grows from about 790 KB to 2 MB, and from about 2.5 ms to 9 ms.', ctl:{type:'toggle', key:'listCookies', value:false}},
      {t:'Social widgets', d:'Fanboy’s Social: stops Like, Share and comment widgets, which report the page you are on to their own company whether or not you touch them. Sharing buttons and embedded posts may disappear — which is the point, and sometimes a nuisance.', ctl:{type:'toggle', key:'listSocial', value:false}},
      {t:'Filter lists', d:'<span id="listsline"></span>', more:'lists', ctl:{type:'buttons', items:[{cmd:'listsUpdate', label:'Update now'},{cmd:'filtersRestore', label:'Use the shipped lists'}]}},
      {t:'Keep the lists up to date', d:'Aviary downloads the newest copies of the lists that are switched on, from the people who publish them. Nothing about you is sent — it is the same file for everyone. Off, they stay as they are until you press Update now.', ctl:{type:'select', key:'listsAutoUpdate', opts:['Off','Daily','Weekly'], value:'Off'}},
      {t:'My filters', d:'Your own rules, in the same syntax as the lists: <code>||ads.example.com^</code> to stop a host, <code>example.com##.banner</code> to hide something, <code>@@||example.com^</code> to let a site through. One per line; a line starting with <code>!</code> is a note. Aviary says which lines it cannot use, and what you write here is never recorded in improvement data.', ctl:{type:'text', key:'customFilters', value:''}},

      {t:'Hide the spaces ads leave', d:'After an ad is stopped, hide the empty box it sat in, so the page closes up around it. Off, the page keeps its gaps — which is sometimes the better way round, on a site whose layout depends on them.', ctl:{type:'toggle', key:'cosmeticHiding', value:true}},
      {t:'Show the number on the shield', d:'The small count on the toolbar shield, showing how many things were stopped on the page you are on. Off, the shield is still there and still stops them.', ctl:{type:'toggle', key:'shieldBadge', value:true}},
      {t:'Warn before ad and tracker sites', d:'In Strict only: when a site is on the lists as one that exists to serve ads or track people, Aviary shows a warning instead of opening it, with a way to open it anyway. Off, Strict blocks what those sites load but opens them.', ctl:{type:'toggle', key:'strictWarning', value:true}},

      {t:'Stronger fingerprint protection', d:'Make every Aviary look alike to the scripts that identify a browser by its fonts, canvas and hardware.', phase:5, ctl:{type:'none'}},
      {t:'Block scripts from other sites', d:'Refuse scripts a page loads from anywhere but its own site. It breaks a great many sites, which is why it is its own switch and not part of Strict.', phase:5, ctl:{type:'none'}},
      {t:'Block this element', d:'Point at anything on a page and hide it for good, the way uBlock Origin’s picker does.', phase:6, ctl:{type:'none'}}
    ]},

  { id:'privacy', icon:'privacy', name:'Privacy and security', title:'Privacy and security',
    lede:'What Aviary keeps, and what it tells sites about you. Every switch on this page does what it says — nothing here is for show. Blocking ads and trackers has its own page: Shields.',
    rows:[
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
      {t:'Search engine', d:'Used when what you type is not a web address. DuckDuckGo, Brave Search, Startpage, Mojeek and Ecosia say they keep no profile of you; Bing and Google do. Startpage and Mojeek sometimes check you are not a robot before the first search.', ctl:{type:'select', key:'searchEngine', opts:ENGINES.map(function(e){ return e.name; }), value:'DuckDuckGo'}},
      /* Live since M4. Its words name the engine chosen above and change with it (suggestRow). */
      {t:'Search suggestions from the engine', d:'<span id="sgtext"></span>', ctl:{type:'toggle', key:'suggest', value:false}}
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
      /* Live since M4.5: every animation Aviary draws checks it (Motion.cs, and the pages’ data-motion). */
      {t:'Reduce motion', d:'Turns off Aviary’s own animations — tabs, the sidebar, panels, Settings and the Start Page — so things appear in place instead of moving there. Aviary reads your Windows animation setting the first time it runs, so this usually starts where you want it.', ctl:{type:'toggle', key:'reduceMotion', value:false}}
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

  { id:'updates', icon:'updates', name:'Updates', title:'Updates', updates:true,
    lede:'The build you are on, what every build brought, and what is coming. All of it is part of Aviary itself — this page never goes online to check for anything.',
    rows:[
      {t:'Automatic updates', d:'For now, each new build replaces the copy of Aviary on your desktop. Updates arrive with the installer.', phase:6, ctl:{type:'none'}}
    ]},

  /* Alpha Build 5 M4.5 — StorageUsage.cs measures every file under Aviary's folder and its profile
     when this pane opens; the categories add up to the total exactly. */
  { id:'storage', icon:'storage', name:'Storage Usage', title:'Storage Usage', storage:true,
    lede:'Everything Aviary keeps on this computer, sorted into what it is — measured from the files themselves each time you open this page, so the parts add up to the whole, to the byte.',
    rows:[] },

  /* Alpha Build 5 M4.5 — live figures from LiveStats.cs, pushed once a second, only while this
     pane is on screen in the visible Settings tab. Nothing here is recorded. */
  { id:'perf', icon:'perf', name:'Performance', title:'Performance', perf:true,
    lede:'What Aviary is using right now, updated every second while this page is open: memory, processor and graphics card, for Aviary itself and every part of its browser engine. The same figures Windows’ Task Manager shows. Nothing here is recorded.',
    rows:[] },

  { id:'about', icon:'about', name:'About Aviary', title:'About Aviary', about:true,
    lede:'A Safari-inspired browser for Windows 11.', rows:[] }
];

function control(row){
  var c = row.ctl || {type:'none'};

  /* ONE number decides both the badge and whether the control is live. A row due in the
     build you are already on says "Later in AB5" — "Alpha Build 5" on a switch you cannot
     use, in Alpha Build 5, would read as broken. */
  var pending = row.never ? row.never
              : (row.phase && row.phase > PHASE)
                  ? (row.phase === (BUILD.phaseNumber | 0) ? 'Later in ' + codeOf(row.phase) : nameOf(row.phase))
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
    /* What a person writes themselves is reported as a count, never as the lines: it can
       name any site they visit (AppSettings.PrivateKeys says the same on the host's side). */
    RENDERED[c.key] = (c.type === 'toggle') ? (c.value ? 'true' : 'false')
                    : (c.type === 'text') ? lineCount(c.value) + ' lines (not recorded)'
                    : String(c.value);
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
  if (c.type === 'text'){
    /* The box is filled by script below, never by pasting the value into this markup. */
    return badge + '<div class="ta" data-key="' + c.key + '">' +
      '<textarea spellcheck="false" rows="7" aria-label="' + esc(row.t) + '"' + (pending ? ' disabled' : '') + '></textarea>' +
      '<div class="ta__bar"><span class="ta__said"></span>' +
      '<button class="btn" data-save="' + c.key + '"' + (pending ? ' disabled' : '') + '>Save</button></div>' +
      '<div class="mine"></div></div>';
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
  if (c.type === 'goto'){
    /* Moves to another pane of this page. NOT a command — nothing goes to the host, and the
       click handler takes it before the .btn branch that would post one. */
    return badge + '<button class="btn" data-goto="' + c.target + '"' +
           (pending ? ' disabled' : '') + '>' + c.label + '</button>';
  }
  return badge;
}

function rowHtml(r){
  var key = r.ctl && r.ctl.key;
  var note = (r.restart && key)
    ? '<span class="note" data-restart="' + key + '" hidden>Restart Aviary to apply this change.</span>'
    : '';
  return '<div class="row"><div class="row__t"><b>' + r.t + '</b>' +
         (r.d ? '<span>' + r.d + '</span>' : '') + note + (r.more ? (MORE[r.more] || '') : '') +
         '</div>' + control(r) + '</div>';
}

/* ── The Updates pane ─────────────────────────────────────────────────────── */
var DOME = '<svg class="hero__mark" viewBox="0 0 24 24" fill="none" aria-hidden="true">' +
  '<path d="M3 20V12.6A9 9 0 0 1 21 12.6V20" stroke="var(--accent)" stroke-width="1.1" stroke-linecap="round"/>' +
  '<path d="M12 3.6V20M7.6 5.1V20M16.4 5.1V20M3 14.4h18" stroke="var(--accent)" stroke-width=".6" stroke-linecap="round"/>' +
  '<path d="M2.2 20h19.6" stroke="var(--accent)" stroke-width="1.1" stroke-linecap="round"/></svg>';
var TICK = '<svg viewBox="0 0 8 8" fill="none"><path d="M1.5 4.2 3.2 5.9 6.5 2.3" stroke="#fff" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>';
var CHEV = '<svg class="bld__chev" viewBox="0 0 10 7" fill="none"><path d="M1 1l4 4 4-4" stroke="currentColor" ' +
  'stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/></svg>';

function notesHtml(notes){
  return (notes || []).map(function(n){
    return '<h3' + (n.warn ? ' class="warn"' : '') + '>' + n.h + '</h3><ul>' +
           n.items.map(function(i){ return '<li>' + i + '</li>'; }).join('') + '</ul>';
  }).join('');
}

function buildHead(u, tag){
  return '<span class="bld__tile">' + esc(codeOf(u.phase)) + '</span>' +
         '<span class="bld__t"><b>' + esc(nameOf(u.phase)) + '</b><span>' + u.name + '</span></span>' + tag;
}

function renderUpdates(s){
  var now = BUILD.phaseNumber | 0;
  var h = '';

  if (CURRENT){
    var ms = CURRENT.milestones || [];
    var done = Math.max(0, Math.min(BUILD.milestonesDone | 0, ms.length));
    var meta = ['Version ' + esc(BUILD.version)];
    if (BUILD.builtOn) { meta.push('Built ' + esc(BUILD.builtOn)); }
    if (BUILD.engine) { meta.push('Engine ' + esc(BUILD.engine)); }

    h += '<div class="group hero">' + DOME +
      '<div class="hero__kicker">You’re on</div>' +
      '<div class="hero__title"><h2>' + esc(nameOf(now)) + '</h2>' +
        '<span class="chip chip--code">' + esc(codeOf(now)) + '</span>' +
        (BUILD.preRelease ? '<span class="chip chip--pre">Pre-release</span>' : '<span class="chip chip--ok">Latest</span>') +
      '</div>' +
      '<div class="hero__name">' + CURRENT.name + '</div>' +
      '<div class="hero__meta">' + meta.join(' &middot; ') + '</div>';

    if (BUILD.preRelease && ms.length){
      h += '<div class="bar" style="grid-template-columns:repeat(' + ms.length + ',1fr)">' +
        ms.map(function(m, i){ return '<i style="--i:' + i + '" class="' + (i < done ? 'done' : (i === done ? 'next' : '')) + '"></i>'; }).join('') +
        '</div><div class="bar__label"><span><b>' + done + ' of ' + ms.length + '</b> milestones done</span>' +
        (done < ms.length ? '<span>Next: ' + esc(ms[done].n || '') + ' · ' + ms[done].t + '</span>' : '') + '</div>' +
        '<ol class="ms">' + ms.map(function(m, i){
          var st = i < done ? 'done' : (i === done ? 'next' : 'todo');
          return '<li class="' + st + '"><span class="ms__dot">' + TICK + '</span>' +
                 '<span class="ms__n">' + esc(m.n || ('M' + i)) + '</span>' +
                 '<span class="ms__t">' + m.t + (m.d ? '<small>' + m.d + '</small>' : '') + '</span>' +
                 '<span class="ms__w">' + (st === 'done' ? (m.when || '') : (st === 'next' ? 'Next' : '')) + '</span></li>';
        }).join('') + '</ol>';
    } else {
      h += '<div style="height:14px"></div>';
    }
    h += '</div>';
  }

  /* The pane's own rows go through the same renderer as every other pane's, so a gated row
     here is badged exactly like one anywhere else. */
  if (s.rows.length) { h += '<div class="group">' + s.rows.map(rowHtml).join('') + '</div>'; }

  if (CURRENT && CURRENT.notes){
    h += '<h2 class="upd-h">What’s new in ' + esc(nameOf(now)) + (BUILD.preRelease ? ' so far' : '') + '</h2>' +
         '<div class="group notes">' + notesHtml(CURRENT.notes) + '</div>';
  }

  var next = UPDATES.filter(function(u){ return u.phase > now; });
  if (next.length){
    h += '<h2 class="upd-h">Coming next</h2>' + next.map(function(u){
      return '<div class="group bld bld--todo"><div class="bld__head">' + buildHead(u, '<span class="chip">Planned</span>') + '</div>' +
             '<div class="bld__body"><p>' + u.summary + '</p>' +
             (u.bits ? '<div class="bits">' + u.bits.map(function(b){ return '<span>' + b + '</span>'; }).join('') + '</div>' : '') +
             '</div></div>';
    }).join('');
  }

  var past = UPDATES.filter(function(u){ return u.phase < now; }).reverse();
  if (past.length){
    h += '<h2 class="upd-h">Earlier builds</h2>' + past.map(function(u){
      return '<details class="group bld"><summary class="bld__head">' +
             buildHead(u, '<span class="bld__when">' + (u.when || '') + '</span>' + CHEV) + '</summary>' +
             '<div class="bld__body"><p>' + u.summary + '</p><div class="notes">' + notesHtml(u.notes) + '</div></div></details>';
    }).join('');
  }

  return h + '<p class="lede upd-foot">' + PROLOGUE + '</p>';
}

/* A row's expandable detail. The body is filled by script from injected data, never pasted
   into this markup — see the diagnostics list below. */
var MORE = {
  diag: '<details class="more"><summary>Show all sixteen, and what is never collected</summary>' +
        '<div class="more__body" id="diaglist"></div></details>',
  /* Filled by the host when "Show" is pressed — site names are website-controlled, so every
     one goes in through textContent. */
  sitedata: '<div class="sitedata" id="sitedata" hidden></div>',
  /* Shields: the lists and whose they are (filled from LISTS below), and the sites with
     Shields down (filled by the host on "Show", through textContent). */
  lists: '<details class="more"><summary>What the lists are, and whose they are</summary>' +
         '<div class="more__body" id="listsbox"></div></details>',
  shieldsites: '<div class="sitedata" id="shieldsites" hidden></div>',
  /* Shields' own figures (M4.5): the line is always there, the breakdown opens. */
  shieldstats: '<details class="more"><summary>The last seven days, and what was stopped most</summary>' +
               '<div class="more__body"><div class="shx" id="shstats"></div></div></details>'
};

var nav = document.getElementById('nav');
var main = document.getElementById('main');

SECTIONS.forEach(function(s, i){
  if (s.id === 'autofill' || s.id === 'updates') nav.appendChild(document.createElement('hr'));
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
    /* Filled from BUILD by textContent below — the engine string comes from WebView2. */
    body += '<div class="group"><div class="row"><div class="about">' +
      '<div><dt>Version</dt><dd id="ab-version"></dd></div>' +
      '<div><dt>Release</dt><dd id="ab-release"></dd></div>' +
      '<div><dt>Engine</dt><dd id="ab-engine"></dd></div>' +
      '<div><dt>Built</dt><dd id="ab-built"></dd></div>' +
      '<div><dt>Interface</dt><dd>WinUI 3 &middot; Windows App SDK &middot; .NET 9</dd></div>' +
      '<div><dt>Identifies as</dt><dd><code id="ua"></code></dd></div>' +
      '</div></div></div>' +
      '<p class="lede">Aviary is an independent project. It is not affiliated with Apple, and it is not ' +
      'Safari &mdash; Apple owns that mark. Windows, WinUI and WebView2 are Microsoft&rsquo;s.</p>';
  } else if (s.storage){
    body += renderStorage();
  } else if (s.perf){
    body += renderPerf();
  } else if (s.updates){
    body += renderUpdates(s);
  } else {
    body += '<div class="group">' + s.rows.map(rowHtml).join('') + '</div>';
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

function fillText(id, text){ var el = document.getElementById(id); if (el) { el.textContent = text; } }
fillText('ab-version', BUILD.version + ' · ' + nameOf(BUILD.phaseNumber) + ' (' + codeOf(BUILD.phaseNumber) + ')');
fillText('ab-release', BUILD.preRelease ? 'Pre-release · ' + (BUILD.progress || '') : 'Released');
fillText('ab-engine', 'WebView2 ' + (BUILD.engine ? BUILD.engine + ' ' : '') + '(Chromium, via Microsoft Edge)');
fillText('ab-built', BUILD.builtOn || '—');

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

/* ── Shields: the lists' line, their detail, and the sites with Shields down ── */
function num(n){ return (n | 0).toLocaleString('en-US'); }
function listsLine(d){
  var el = document.getElementById('listsline');
  var btn = document.querySelector('.btn[data-cmd="listsUpdate"]');
  if (btn) { btn.disabled = !!(d && d.updating); btn.textContent = (d && d.updating) ? 'Checking…' : 'Update now'; }
  var back = document.querySelector('.btn[data-cmd="filtersRestore"]');
  if (back) { back.disabled = !!(d && d.updating); }
  if (!el) { return; }
  if (!d || !d.lists) { el.textContent = ''; return; }
  if (d.updating) { el.textContent = 'Checking for newer lists…'; return; }
  var checked = d.checkedAt ? 'Last checked ' + d.checkedAt + (d.result ? ' — ' + d.result : '')
                            : (d.result ? d.result + ' ' : '') + 'Never checked for newer ones.';
  /* "My filters" is written here, not downloaded: it is not one of the lists this line is about. */
  var lists = d.lists.filter(function(l){ return l.setting !== 'customFilters'; });
  var on = lists.filter(function(l){ return l.on !== false; });
  if (!on.length) {
    el.textContent = 'Every list is switched off, so only Aviary’s own short list of trackers is blocking, ' +
                     'and nothing is hidden. ' + checked;
    return;
  }
  var have = on.map(function(l){
    return l.name + (l.modified ? ' of ' + l.modified : l.source === 'missing' ? ' (missing)' : '');
  }).join(', ');
  var off = lists.length - on.length;
  el.textContent = (d.ready ? 'Using ' : 'Loading ') + have + '. ' +
    (off ? off + (off === 1 ? ' other list is' : ' others are') + ' switched off. ' : '') + checked;
}
function listsBox(d){
  var box = document.getElementById('listsbox');
  if (!box || !d || !d.lists) { return; }
  box.textContent = '';
  var ul = document.createElement('ul');
  d.lists.forEach(function(l){
    var li = document.createElement('li');
    var b = document.createElement('b');
    b.textContent = l.name;
    li.appendChild(b);

    /* What this list is, then what it brought and where it came from (M4.5). Everything here
       is text: a list is a file from someone else, and its name is not markup. */
    /* "My filters" counts differently: what matters is how many of the lines Aviary can use. */
    var about = l.setting === 'customFilters'
      ? [num(Math.max(0, l.rules - ((d.mine || []).length))) + ' in use',
         (d.mine || []).length ? (d.mine.length + ' ' + (d.mine.length === 1 ? 'line' : 'lines') + ' Aviary cannot use') : '']
      : l.on === false
      ? ['switched off']
      : [l.version ? 'version ' + l.version : '',
         l.source,
         l.rules ? num(l.rules) + ' rules read' : (l.lines ? num(l.lines) + ' lines' : ''),
         l.skipped ? num(l.skipped) + ' skipped' : ''];
    about = about.filter(function(s){ return s; }).join(', ');
    li.appendChild(document.createTextNode(' — ' + String(l.what || '').toLowerCase() + '. ' +
      about.charAt(0).toUpperCase() + about.slice(1) + '.'));

    if (l.on !== false && l.by && l.licence){
      var small = document.createElement('small');
      small.textContent = 'By ' + l.by + ', used exactly as published, under the ' + l.licence + ' licence.';
      li.appendChild(small);
    }
    ul.appendChild(li);
  });
  box.appendChild(ul);

  if (d.ready) {
    var counts = document.createElement('p');
    counts.textContent = 'Aviary reads ' + num(d.network) + ' blocking rules and ' + num(d.cosmetic) +
      ' rules that hide what ads leave behind. ' + num(d.skipped) + ' more are kinds it does not use.';
    box.appendChild(counts);

    if (d.skippedBy && d.skippedBy.length){
      var why = document.createElement('p');
      why.textContent = 'What it skips, most first: ' + d.skippedBy.map(function(x){
        return x.why + ' (' + num(x.count) + ')';
      }).join(', ') + '.';
      box.appendChild(why);
    }
  }

  var credit = document.createElement('p');
  credit.textContent = 'The lists are other people’s work, bundled unchanged and credited above. ' +
    'Aviary adds a short list of its own, kept separately. "Use the shipped lists" deletes the copies ' +
    'downloaded since and goes back to the ones Aviary was built with.';
  box.appendChild(credit);
}
listsLine(LISTS);
listsBox(LISTS);
mineBox(LISTS);
window.__aviaryListsStatus = function(d){ if (d) { LISTS = d; } listsLine(LISTS); listsBox(LISTS); mineBox(LISTS); };

/* Each site with a setting of its own, and the three ways to change it (M4.5). Site names
   come from browsing, so every one of them goes in as text. */
window.__aviaryShieldsSites = function(d){
  var box = document.getElementById('shieldsites');
  if (!box) { return; }
  var sites = (d && d.sites) || [];
  var fallback = (d && d.fallback) || 'Standard';
  box.hidden = false;
  box.textContent = '';

  var forget = document.querySelector('.btn[data-cmd="shieldsSitesReset"]');
  if (forget) { forget.disabled = !sites.length; }

  if (!sites.length){
    var none = document.createElement('div');
    none.className = 'sd__empty';
    none.textContent = 'Every site follows the setting above (' + fallback + ').';
    box.appendChild(none);
    return;
  }

  sites.forEach(function(s){
    var row = document.createElement('div');
    row.className = 'sd';

    var name = document.createElement('b');
    name.textContent = s.site;
    row.appendChild(name);

    var seg = document.createElement('div');
    seg.className = 'segsm';
    ['Off','Standard','Strict'].forEach(function(mode){
      var b = document.createElement('button');
      b.textContent = mode;
      b.setAttribute('aria-pressed', s.mode === mode ? 'true' : 'false');
      b.setAttribute('data-site-mode', s.site + '|' + mode);
      b.setAttribute('aria-label', mode + ' for ' + s.site);
      seg.appendChild(b);
    });
    row.appendChild(seg);

    var undo = document.createElement('button');
    undo.textContent = 'Use the default';
    undo.setAttribute('data-site-reset', s.site);
    row.appendChild(undo);

    box.appendChild(row);
  });
};

/* ── What Shields stopped: this session, 7 days, all time (M4.5) ──────────── */
function shieldStats(d){
  var line = document.getElementById('shline');
  if (line){
    line.textContent = num(d.session) + ' since Aviary started · ' + num(d.week) + ' in the last 7 days · ' +
      num(d.all) + ' all time' + (d.since ? ', since ' + d.since : '') + '.';
  }

  var box = document.getElementById('shstats');
  if (!box) { return; }
  box.textContent = '';

  var figures = document.createElement('div');
  figures.className = 'shx__n';
  [['This session', num(d.session), 'requests stopped'],
   ['Last 7 days', num(d.week), stSize(d.weekBytes) + ' not downloaded'],
   ['All time', num(d.all), stSize(d.allBytes) + ' not downloaded'],
   ['Time saved', Math.round(d.allSeconds) + 's', 'at about 30 ms each']].forEach(function(f){
    var cell = document.createElement('div');
    var b = document.createElement('b');
    b.textContent = f[1];
    var top = document.createElement('span');
    top.textContent = f[0];
    var sub = document.createElement('span');
    sub.textContent = f[2];
    cell.appendChild(b); cell.appendChild(top); cell.appendChild(sub);
    figures.appendChild(cell);
  });
  box.appendChild(figures);

  var days = (d.days || []).slice(-7);
  if (days.length === 7){
    var most = Math.max.apply(null, days.concat([1]));
    var bars = document.createElement('div');
    bars.className = 'shx__days';
    days.forEach(function(n){
      var i = document.createElement('i');
      i.style.height = Math.max(2, Math.round(n / most * 100)) + '%';
      if (!n) { i.style.opacity = '.22'; }
      i.title = num(n) + ' blocked';
      bars.appendChild(i);
    });
    var labels = document.createElement('div');
    labels.className = 'shx__lab';
    var names = ['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
    var today = new Date().getDay();
    for (var k = 6; k >= 0; k--){
      var lab = document.createElement('span');
      lab.textContent = k === 0 ? 'Today' : names[(today - k + 7) % 7];
      labels.appendChild(lab);
    }
    box.appendChild(bars);
    box.appendChild(labels);
  }

  var two = document.createElement('div');
  two.className = 'shx__two';
  [['Stopped most often', d.trackers, 'Nothing has been stopped yet.'],
   ['On these sites', d.sites, 'No site has needed Shields yet.']].forEach(function(pair){
    var col = document.createElement('div');
    var h = document.createElement('h4');
    h.textContent = pair[0];
    col.appendChild(h);
    var rows = pair[1] || [];
    if (!rows.length){
      var none = document.createElement('div');
      none.className = 'shx__none';
      none.textContent = pair[2];
      col.appendChild(none);
    } else {
      var ol = document.createElement('ol');
      rows.forEach(function(r){
        var li = document.createElement('li');
        var s = document.createElement('span');
        s.textContent = r.name;
        var i = document.createElement('i');
        i.textContent = num(r.count);
        li.appendChild(s); li.appendChild(i);
        ol.appendChild(li);
      });
      col.appendChild(ol);
    }
    two.appendChild(col);
  });
  box.appendChild(two);

  var foot = document.createElement('p');
  foot.className = 'shx__none';
  foot.textContent = 'These are the Privacy Report’s own counts, kept on this computer. Private windows were ' +
    'never counted into them. "Start again" puts every figure here back to zero.';
  box.appendChild(foot);
}
window.__aviaryShieldStats = function(d){ if (d) { shieldStats(d); } };

/* ── My filters: the box, what it saved, and the lines it could not use ───── */
function mineBox(d){
  var wrap = document.querySelector('.ta[data-key="customFilters"]');
  if (!wrap) { return; }
  var said = wrap.querySelector('.ta__said');
  var box = wrap.querySelector('.mine');
  var problems = (d && d.mine) || [];
  var mine = ((d && d.lists) || []).filter(function(l){ return l.setting === 'customFilters'; })[0];

  if (said){
    var using_ = mine ? Math.max(0, mine.rules - problems.length) : 0;
    said.textContent = !mine ? 'No rules of your own yet.'
      : num(using_) + (using_ === 1 ? ' rule in use' : ' rules in use') +
        (problems.length ? ' · ' + problems.length + (problems.length === 1 ? ' line Aviary cannot use' : ' lines Aviary cannot use') : '');
  }

  if (!box) { return; }
  box.textContent = '';
  problems.forEach(function(p){
    var line = document.createElement('div');
    var b = document.createElement('b');
    b.textContent = 'Line ' + p.line + ': ' + (p.text.length > 60 ? p.text.slice(0, 59) + '…' : p.text);
    line.appendChild(b);
    line.appendChild(document.createTextNode(' — ' + p.why + '.'));
    box.appendChild(line);
  });
}

/* The boxes are filled from the saved value directly: what a person typed is their text,
   never markup, and never a boolean the way saved() would read "true". */
document.querySelectorAll('.ta').forEach(function(wrap){
  var key = wrap.getAttribute('data-key');
  var ta = wrap.querySelector('textarea');
  if (ta) { ta.value = (key in SAVED) ? String(SAVED[key]) : ''; }
});

/* ── The Shields pane asks for its own figures when it comes on screen ────── */
function shieldsWatch(){
  var pane = document.getElementById('pane-shields');
  if (!pane || !pane.classList.contains('on')) { return; }
  post({type:'command', name:'shieldsStats'});
  post({type:'command', name:'shieldsSites'});
}
nav.addEventListener('click', function(){ setTimeout(shieldsWatch, 0); });
setTimeout(shieldsWatch, 0);

/* ── Search: the suggestions row names the engine it would send to (M4) ── */
function engineNamed(name){
  for (var i = 0; i < ENGINES.length; i++) { if (ENGINES[i].name === name) { return ENGINES[i]; } }
  return ENGINES[0];
}
function suggestRow(){
  var sel = document.querySelector('.sel[data-key="searchEngine"] .sel__btn span');
  var engine = engineNamed(sel ? sel.textContent : saved('searchEngine', ENGINES[0].name));
  var sw = document.querySelector('.sw[data-key="suggest"]');
  var el = document.getElementById('sgtext');
  var own = 'Aviary’s own suggestions — from your history and favourites — work either way and never leave this computer.';
  if (sw) { sw.disabled = !engine.suggests; }
  if (!el) { return; }
  el.textContent = engine.suggests
    ? 'Ask ' + engine.name + ' for suggestions as you type. Off by default, and worth leaving off: what you type in the ' +
      'address bar goes to ' + engine.name + ' as you type it, not only what you search for. Never from a private window, ' +
      'never a web address, and changing the search engine switches this off again. ' + own
    : engine.name + ' doesn’t offer suggestions, so nothing you type is sent while it is your search engine. ' + own;
}
suggestRow();

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

/* The side menu's selection: one pill that travels to the item picked (M4.5). */
var navPill = document.createElement('div');
navPill.className = 'nav-pill';
nav.insertBefore(navPill, nav.firstChild);
function placeNavPill(b, instant){
  if (!b) { return; }
  if (instant) { navPill.style.transition = 'none'; }
  navPill.style.transform = 'translateY(' + b.offsetTop + 'px)';
  navPill.style.height = b.offsetHeight + 'px';
  if (instant) { void navPill.offsetWidth; navPill.style.transition = ''; }
}

/* Each pane's rows arrive one after another: their order, capped so a long pane is quick. */
function staggerRows(pane){
  pane.querySelectorAll('.row,.bld,.hero,h2.upd-h').forEach(function(r, i){ r.style.setProperty('--i', Math.min(i, 14)); });
}

function showPane(id){
  var b = nav.querySelector('button[data-target="' + id + '"]');
  var pane = document.getElementById('pane-' + id);
  if (!b || !pane) { return; }
  nav.querySelectorAll('button[data-target]').forEach(function(x){ x.removeAttribute('aria-current'); });
  b.setAttribute('aria-current','true');
  placeNavPill(b, false);
  staggerRows(pane);
  document.querySelectorAll('.pane').forEach(function(p){ p.classList.remove('on'); });
  pane.classList.add('on');
  main.scrollTop = 0;
}

staggerRows(document.querySelector('.pane.on') || document.body);
placeNavPill(nav.querySelector('button[aria-current="true"]'), true);
window.addEventListener('resize', function(){ placeNavPill(nav.querySelector('button[aria-current="true"]'), true); });

nav.addEventListener('click', function(e){
  var b = e.target.closest('button[data-target]');
  if (b) { showPane(b.getAttribute('data-target')); }
});

function closeAllPopups(except){
  document.querySelectorAll('.sel').forEach(function(s){
    if (s === except) { return; }
    s.querySelector('.sel__pop').hidden = true;
    s.querySelector('.sel__btn').setAttribute('aria-expanded','false');
  });
}

main.addEventListener('click', function(e){
  /* First: a goto button also wears .btn, and the .btn branch at the bottom posts a command. */
  var go = e.target.closest('button[data-goto]');
  if (go){
    closeAllPopups(null);
    if (!go.disabled) { showPane(go.getAttribute('data-goto')); }
    return;
  }

  var modeBtn = e.target.closest('button[data-site-mode]');
  if (modeBtn){
    if (modeBtn.getAttribute('aria-pressed') === 'true') { return; }
    var seg = modeBtn.closest('.segsm');
    if (seg) { seg.querySelectorAll('button').forEach(function(b){ b.setAttribute('aria-pressed','false'); }); }
    modeBtn.setAttribute('aria-pressed','true');
    post({type:'command', name:'shieldsSiteMode', arg: modeBtn.getAttribute('data-site-mode')});
    return;
  }

  var resetSite = e.target.closest('button[data-site-reset]');
  if (resetSite){
    resetSite.disabled = true;
    post({type:'command', name:'shieldsSiteReset', arg: resetSite.getAttribute('data-site-reset')});
    return;
  }

  var save = e.target.closest('button[data-save]');
  if (save && !save.disabled){
    var wrap = save.closest('.ta');
    var key = save.getAttribute('data-save');
    var text = wrap.querySelector('textarea').value;
    SAVED[key] = text;
    sendSetting(key, text);
    var said = wrap.querySelector('.ta__said');
    if (said) { said.textContent = 'Reading the rules…'; }
    return;
  }

  var shieldsBtn = e.target.closest('button[data-shields-site]');
  if (shieldsBtn){
    shieldsBtn.disabled = true;
    post({type:'command', name:'shieldsSiteUp', arg: shieldsBtn.getAttribute('data-shields-site')});
    return;
  }

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
    /* Picking what is already chosen changes nothing and sends nothing — it used to save it
       again, and choosing your own search engine again would have switched suggestions off. */
    var unchanged = opt.getAttribute('aria-selected') === 'true';
    host.querySelectorAll('.sel__opt').forEach(function(o){ o.setAttribute('aria-selected','false'); });
    opt.setAttribute('aria-selected','true');
    host.querySelector('.sel__btn span').textContent = opt.getAttribute('data-v');
    host.querySelector('.sel__pop').hidden = true;
    host.querySelector('.sel__btn').setAttribute('aria-expanded','false');
    if (unchanged) { return; }
    sendSetting(host.getAttribute('data-key'), opt.getAttribute('data-v'));
    if (host.getAttribute('data-key') === 'searchEngine') { suggestRow(); }
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
  if (key === 'searchEngine') { suggestRow(); }
};

/* ══ STORAGE USAGE (M4.5) ═══════════════════════════════════════════════════
   Measured by the host when this pane opens and on Refresh; drawn with textContent (paths hold
   backslashes, and nothing here should ever be read as markup). */
function renderStorage(){
  return '<div class="group st-hero" id="st-hero"><div class="st-total" id="st-total">Measuring…</div>' +
      '<div class="st-sub" id="st-sub">Counting every file Aviary keeps.</div>' +
      '<div class="st-bar" id="st-bar" role="img" aria-label="Storage by category"></div>' +
      '<div class="st-tip" id="st-tip" hidden></div></div>' +
    '<div class="group" id="st-cats"><div class="st-empty">Measuring…</div></div>' +
    '<div class="group"><div class="row"><div class="row__t"><b>Where it lives</b>' +
      '<span>Aviary itself<span class="path" id="st-install"></span></span>' +
      '<span>Your profile<span class="path" id="st-profile"></span></span></div>' +
      '<div class="btns"><button class="btn" id="st-refresh">Measure again</button></div></div></div>' +
    '<p class="lede upd-foot" id="st-foot"></p>';
}

function stSize(b){
  var u = ['bytes','KB','MB','GB','TB'], v = b, i = 0;
  while (v >= 1024 && i < u.length - 1) { v /= 1024; i++; }
  return (i === 0 ? String(v) : v >= 100 ? v.toFixed(0) : v >= 10 ? v.toFixed(1) : v.toFixed(2)) + ' ' + u[i];
}
var stRequested = false;
function storageMeasure(){
  var t = document.getElementById('st-total');
  if (t && stRequested) { t.textContent = 'Measuring…'; }
  stRequested = true;
  post({type:'command', name:'storageMeasure'});
}
function storageWatch(){
  var pane = document.getElementById('pane-storage');
  if (pane && pane.classList.contains('on') && !stRequested) { storageMeasure(); }
}
nav.addEventListener('click', function(){ setTimeout(storageWatch, 0); });

window.__aviaryStorage = function(d){
  window.__aviaryStorageLast = d;   /* for tools/test/storage-compare.ps1 */
  var total = document.getElementById('st-total');
  if (!total) { return; }
  total.textContent = stSize(d.bytes);
  var small = document.createElement('small');
  small.textContent = 'used by Aviary on this computer';
  total.appendChild(small);
  document.getElementById('st-sub').textContent = stSize(d.onDisk) + ' on disk · ' + d.files.toLocaleString() +
    ' files · measured at ' + d.at + ', in ' + Math.round(d.ms) + ' ms';

  var bar = document.getElementById('st-bar');
  var tip = document.getElementById('st-tip');
  bar.textContent = '';
  var shown = d.categories.filter(function(c){ return c.bytes > 0; });
  shown.forEach(function(c, i){
    var seg = document.createElement('i');
    seg.style.flex = String(Math.max(c.bytes / Math.max(d.bytes, 1), 0.002));
    seg.style.background = 'var(--st-' + c.key + ')';
    seg.style.animationDelay = (i * 40) + 'ms';
    seg.setAttribute('aria-label', c.name + ', ' + stSize(c.bytes));
    seg.addEventListener('mouseenter', function(){
      tip.textContent = '';
      var b = document.createElement('b'); b.textContent = c.name;
      var s = document.createElement('span');
      s.textContent = stSize(c.bytes) + ' · ' + (100 * c.bytes / Math.max(d.bytes, 1)).toFixed(1) + '% · ' + c.files.toLocaleString() + (c.files === 1 ? ' file' : ' files');
      tip.appendChild(b); tip.appendChild(s);
      tip.hidden = false;
      var host = document.getElementById('st-hero').getBoundingClientRect(), r = seg.getBoundingClientRect();
      tip.style.left = Math.max(8, Math.min(r.left - host.left + r.width / 2 - tip.offsetWidth / 2, host.width - tip.offsetWidth - 8)) + 'px';
      tip.style.top = (r.bottom - host.top + 8) + 'px';
    });
    seg.addEventListener('mouseleave', function(){ tip.hidden = true; });
    bar.appendChild(seg);
  });

  var cats = document.getElementById('st-cats');
  cats.textContent = '';
  d.categories.forEach(function(c){
    var det = document.createElement('details');
    det.className = 'st-cat';
    var sum = document.createElement('summary');
    var chip = document.createElement('span'); chip.className = 'st-chip'; chip.style.background = 'var(--st-' + c.key + ')';
    var name = document.createElement('span'); name.className = 'st-name';
    var nb = document.createElement('b'); nb.textContent = c.name;
    var ns = document.createElement('span'); ns.textContent = c.files.toLocaleString() + (c.files === 1 ? ' file' : ' files') + ' · ' + stSize(c.onDisk) + ' on disk';
    name.appendChild(nb); name.appendChild(ns);
    var size = document.createElement('span'); size.className = 'st-size'; size.textContent = stSize(c.bytes);
    var pct = document.createElement('span'); pct.className = 'st-pct'; pct.textContent = (100 * c.bytes / Math.max(d.bytes, 1)).toFixed(1) + '%';
    sum.appendChild(chip); sum.appendChild(name); sum.appendChild(size); sum.appendChild(pct);
    sum.insertAdjacentHTML('beforeend', CHEV);
    det.appendChild(sum);

    var body = document.createElement('div'); body.className = 'st-body';
    var p = document.createElement('p'); p.textContent = c.plain; body.appendChild(p);
    if (c.key === 'diagnostics') {
      var capP = document.createElement('p');
      capP.textContent = 'The improvement data is kept to ' + stSize(d.capBytes) + ' at most — the oldest goes first.';
      body.appendChild(capP);
    }
    if (c.biggest.length) {
      var parts = document.createElement('div'); parts.className = 'st-parts';
      c.biggest.forEach(function(it){
        var row = document.createElement('div');
        var lb = document.createElement('b'); lb.textContent = it.label;
        var sz = document.createElement('i'); sz.textContent = stSize(it.bytes) + ' · ' + it.files.toLocaleString() + (it.files === 1 ? ' file' : ' files');
        row.appendChild(lb); row.appendChild(sz); parts.appendChild(row);
      });
      body.appendChild(parts);
    }
    var acts = document.createElement('div'); acts.className = 'st-actions';
    function act(label, fn){ var b = document.createElement('button'); b.className = 'btn'; b.textContent = label; b.addEventListener('click', function(e){ e.stopPropagation(); fn(b); }); acts.appendChild(b); }
    if (c.key === 'cache' && c.bytes > 0) {
      act('Clear cached files', function(b){ b.disabled = true; b.textContent = 'Clearing…'; post({type:'command', name:'storageClearCache'}); });
    }
    if (c.key === 'sites' || c.key === 'secrets') { act('Manage in Privacy', function(){ showPane(c.key === 'sites' ? 'privacy' : 'autofill'); }); }
    if (c.key === 'diagnostics') { act('Manage in Privacy', function(){ showPane('privacy'); }); }
    if (acts.children.length) { body.appendChild(acts); }
    det.appendChild(body);
    cats.appendChild(det);
  });

  document.getElementById('st-install').textContent = d.install;
  document.getElementById('st-profile').textContent = d.profile;
  document.getElementById('st-foot').textContent = 'Every file is counted once, and its size is asked of the file itself rather than a folder listing, ' +
    'so the parts add up to the total exactly. “On disk” is the space the files really take up — a little more, because disks store files in whole blocks.';
};
main.addEventListener('click', function(e){ if (e.target.closest('#st-refresh')) { storageMeasure(); } });

/* ══ PERFORMANCE (M4.5) ═════════════════════════════════════════════════════
   The host (LiveStats.cs) measures once a second while this pane is on screen; perfWatch tells
   it when that is. Every tab title arrives from websites, so the tables are built with
   textContent, never markup. */
function renderPerf(){
  function card(id, k, s){
    return '<div class="pf-card" id="pf-' + id + '"><span class="pf-k">' + k + '</span><b class="pf-v">—</b>' +
      '<span class="pf-s">' + s + '</span><svg class="pf-spark" viewBox="0 0 60 30" preserveAspectRatio="none" aria-hidden="true">' +
      '<polygon points=""/><polyline points=""/></svg></div>';
  }
  return '<div class="pf-cards">' +
      card('mem', 'Memory', 'All of Aviary') +
      card('cpu', 'Processor', 'Of the whole processor') +
      card('gpu', 'Graphics', 'Its busiest part, for Aviary') +
      card('gmem', 'Graphics memory', 'The card’s own memory') +
    '</div>' +
    '<div class="group">' +
      '<div class="row"><div class="row__t"><b>How quickly Aviary answers</b><span id="pf-ui">Measuring…</span></div></div>' +
      '<div class="row"><div class="row__t"><b>Shields’ work</b><span id="pf-blocker">Measuring…</span></div></div>' +
      '<div class="row"><div class="row__t"><b>This session</b><span id="pf-session">Measuring…</span></div></div>' +
    '</div>' +
    '<h2 class="upd-h">Tabs</h2><div class="group pf-table" id="pf-tabs"><div class="pf-empty">Measuring…</div></div>' +
    '<h2 class="upd-h">Aviary’s processes</h2><div class="group pf-table" id="pf-procs"><div class="pf-empty">Measuring…</div></div>' +
    '<p class="lede upd-foot" id="pf-foot"></p>';
}

var PF = {mem:[], cpu:[], gpu:[], gmem:[]};
function pfSize(mb){
  if (mb == null) { return '—'; }
  return mb >= 1024 ? (mb / 1024).toFixed(2) + '<small>GB</small>' : Math.round(mb) + '<small>MB</small>';
}
function pfSizeText(mb){ return mb == null ? '—' : (mb >= 1024 ? (mb / 1024).toFixed(2) + ' GB' : Math.round(mb) + ' MB'); }
function pfPct(v){ return v == null ? '—' : (v < 10 ? v.toFixed(1) : String(Math.round(v))) + '<small>%</small>'; }
function pfPctText(v){ return v == null ? '—' : (v < 10 ? v.toFixed(1) : String(Math.round(v))) + '%'; }
function pfSpark(id, arr, v){
  arr.push(v == null ? 0 : v);
  if (arr.length > 60) { arr.shift(); }
  var card = document.getElementById('pf-' + id);
  if (!card) { return; }
  var top = Math.max(Math.max.apply(null, arr) * 1.15, 0.0001);
  var pts = arr.map(function(x, i){ return (i + 60 - arr.length) + ',' + (30 - (x / top) * 28).toFixed(2); }).join(' ');
  card.querySelector('polyline').setAttribute('points', pts);
  card.querySelector('polygon').setAttribute('points', (60 - arr.length) + ',30 ' + pts + ' 59,30');
}
function pfDuration(s){
  if (s < 60) { return s + ' s'; }
  if (s < 3600) { return Math.floor(s / 60) + ' min'; }
  return Math.floor(s / 3600) + ' h ' + Math.floor((s % 3600) / 60) + ' min';
}
function pfRow(name, sub, nums, buttons, head){
  var row = document.createElement('div');
  row.className = 'pf-row' + (head ? ' pf-head' : '');
  var n = document.createElement('div');
  n.className = 'pf-n';
  var b = document.createElement('b');
  b.textContent = name;
  n.appendChild(b);
  if (sub) { var s = document.createElement('span'); s.textContent = sub; n.appendChild(s); }
  row.appendChild(n);
  nums.forEach(function(v){ var c = document.createElement('div'); c.className = 'pf-num'; c.textContent = v; row.appendChild(c); });
  var btns = document.createElement('div');
  btns.className = 'btns';
  (buttons || []).forEach(function(bt){
    var x = document.createElement('button');
    x.textContent = bt.label;
    x.setAttribute('data-perf-tab', bt.cmd);
    btns.appendChild(x);
  });
  row.appendChild(btns);
  return row;
}

window.__aviaryPerf = function(d){
  function set(id, html, v, arr){
    var card = document.getElementById('pf-' + id);
    if (card) { card.querySelector('.pf-v').innerHTML = html; }
    pfSpark(id, arr, v);
  }
  set('mem', pfSize(d.memory), d.memory, PF.mem);
  set('cpu', pfPct(d.cpu), d.cpu, PF.cpu);
  set('gpu', d.gpu == null ? '—' : pfPct(d.gpu), d.gpu, PF.gpu);
  set('gmem', d.gpuMemory == null ? '—' : pfSize(d.gpuMemory), d.gpuMemory, PF.gmem);
  if (d.gpu == null) {
    var gs = document.querySelector('#pf-gpu .pf-s');
    if (gs) { gs.textContent = 'Windows is not reporting it'; }
  }

  var ui = document.getElementById('pf-ui');
  if (ui) {
    ui.textContent = 'Aviary answered in ' + d.ui.now.toFixed(1) + ' ms just now · slowest in the last minute: ' +
      d.ui.worst.toFixed(1) + ' ms. Anything under 50 ms feels instant.';
  }
  var bl = document.getElementById('pf-blocker');
  if (bl) {
    bl.textContent = d.blocker.checks
      ? 'Checked ' + d.blocker.checks + (d.blocker.checks === 1 ? ' request' : ' requests') + ' in the last second · ' +
        d.blocker.average + ' µs each on average, ' + d.blocker.worst + ' µs at most.'
      : 'Nothing to check in the last second — no page was loading anything.';
  }
  var se = document.getElementById('pf-session');
  if (se) {
    var t = d.tabs.length;
    se.textContent = 'Running for ' + pfDuration(d.session.uptimeSeconds) +
      (d.session.startupMs ? ' · opened in ' + (d.session.startupMs / 1000).toFixed(2) + ' s' : '') +
      ' · ' + t + (t === 1 ? ' tab' : ' tabs') + ' in ' + d.session.windows + (d.session.windows === 1 ? ' window' : ' windows') +
      ' · ' + (d.session.rules || 0).toLocaleString() + ' Shields rules loaded.';
  }

  var tabs = document.getElementById('pf-tabs');
  if (tabs) {
    tabs.textContent = '';
    tabs.appendChild(pfRow('Tab', '', ['Memory', 'Processor', 'Graphics'], [], true));
    var states = {active:'On screen', background:'In the background', saver:'In the background · memory saver', asleep:'Asleep — wakes when you click it'};
    d.tabs.forEach(function(tb){
      var name = tb.isPrivate ? 'Private tab' : (tb.title || 'Untitled');
      var note = PF_NOTES[tb.id];
      var sub = (note && note.until > Date.now()) ? note.text
        : (states[tb.state] || '') + (tb.own ? ' · Aviary’s own page' : '') +
          (tb.shared ? ' · shares its process with ' + (tb.shared - 1) + (tb.shared === 2 ? ' other tab' : ' other tabs') : '') +
          (tb.blocked ? ' · ' + tb.blocked + ' blocked' : '');
      var buttons = [];
      if (tb.canSleep) { buttons.push({label:'Sleep', cmd:'sleep:' + tb.id}); }
      buttons.push({label:'Close', cmd:'close:' + tb.id});
      tabs.appendChild(pfRow(name, sub,
        [tb.memory == null ? '—' : pfSizeText(tb.memory), tb.cpu == null ? '—' : pfPctText(tb.cpu), tb.gpu == null ? '—' : pfPctText(tb.gpu)],
        buttons));
    });
  }

  var procs = document.getElementById('pf-procs');
  if (procs) {
    procs.textContent = '';
    procs.appendChild(pfRow('Process', '', ['Memory', 'Processor', 'Graphics'], [], true));
    d.processes.forEach(function(p){
      var sub = 'Process ' + p.pid + (p.tabs ? ' · draws ' + p.tabs + (p.tabs === 1 ? ' tab' : ' tabs') : '') +
        ' · ' + p.handles + ' handles' + (p.gpuMemory ? ' · ' + pfSizeText(p.gpuMemory) + ' of graphics memory' : '');
      procs.appendChild(pfRow(p.kind, sub, [pfSizeText(p.memory), pfPctText(p.cpu), pfPctText(p.gpu)], []));
    });
  }

  var foot = document.getElementById('pf-foot');
  if (foot) {
    foot.textContent = 'Measuring takes Aviary ' + d.samplerMs.toFixed(1) + ' ms a second, and stops the moment you leave this page. ' +
      'Memory is each part’s private working set and processor is its share of the whole processor — the figures Task Manager shows.';
  }
};

/* The host's word on a tab it could not put to sleep; shown under that tab's row for a while. */
var PF_NOTES = {};
window.__aviaryPerfNote = function(id, text){ PF_NOTES[id] = {text: String(text), until: Date.now() + 8000}; };

main.addEventListener('click', function(e){
  var b = e.target.closest('button[data-perf-tab]');
  if (b) { b.disabled = true; post({type:'command', name:'perfTab', arg: b.getAttribute('data-perf-tab')}); }
});

/* The host measures only while this pane is actually on screen. */
var perfOn = false;
function perfWatch(){
  var pane = document.getElementById('pane-perf');
  var want = !!pane && pane.classList.contains('on') && document.visibilityState === 'visible';
  if (want !== perfOn) {
    perfOn = want;
    post({type:'command', name:'perfWatch', arg: want ? 'on' : 'off'});
  }
}
document.addEventListener('visibilitychange', perfWatch);
window.addEventListener('pagehide', function(){ if (perfOn) { perfOn = false; post({type:'command', name:'perfWatch', arg:'off'}); } });
nav.addEventListener('click', function(){ setTimeout(perfWatch, 0); });
main.addEventListener('click', function(e){ if (e.target.closest('button[data-goto]')) { setTimeout(perfWatch, 0); } });

/* Opened from the app menu's "About Aviary", which used to land you on Get started. */
if (window.__aviarySection){ showPane(window.__aviarySection); }
setTimeout(perfWatch, 0);
setTimeout(storageWatch, 0);
</script>
</body>
</html>
""";
}
