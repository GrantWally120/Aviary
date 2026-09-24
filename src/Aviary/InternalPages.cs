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

  /* M4.9 — Safety Check. The mark says the kind of line before the words do. */
  .sc-head{display:flex;align-items:center;justify-content:space-between;gap:12px;margin:4px 0 12px;}
  .sc-head b{font-size:15px;font-weight:600;}
  .sc-row{align-items:flex-start;}
  .sc-dot{flex:0 0 22px;height:22px;border-radius:50%;display:grid;place-items:center;font:700 12px/1 inherit;color:#fff;margin-top:1px;}
  .sc-ok{background:#2eb867;} .sc-warn{background:#f08c1a;} .sc-info{background:var(--accent);}
  /* M4.9 — Forgetful sites, one line each. `.row__t` for the same specificity reason as the tiles. */
  .row__t .fg{display:flex;flex-direction:column;gap:6px;margin-top:10px;}
  .row__t .fg__row{display:flex;align-items:center;gap:10px;margin:0;}
  .row__t .fg__row b{color:var(--ink);font-weight:600;}
  .row__t .fg__row em{font-style:normal;flex:1;}
  .row__t .fg i{font-style:normal;color:var(--ink-3);}

  /* M4.8 (bug 77) — the App Icon row shows both tiles, so picking one is visibly something.
     The taskbar and Alt-Tab are the only other places it shows, and neither is on this page.
     Selectors carry `.row__t` for the same reason as the rule above: `.row__t span` sets
     display:block and would otherwise win on specificity and stack these. */
  .row__t .icotiles{display:flex;gap:14px;margin-top:10px;}
  .row__t .icotile{display:flex;flex-direction:column;align-items:center;gap:5px;width:auto;}
  .row__t .icotile svg{
    width:44px;height:44px;border-radius:13px;
    box-shadow:0 1px 3px rgba(0,0,0,.28);
    outline:2px solid transparent;outline-offset:3px;
    transition:outline-color var(--m-std) var(--m-ease);
  }
  .row__t .icotile i{display:block;font-style:normal;font-size:11px;color:var(--ink-3);margin:0;}
  .row__t .icotile[data-on="true"] svg{outline-color:var(--accent);}
  .row__t .icotile[data-on="true"] i{color:var(--accent);font-weight:600;}
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

  /* The keyboard shortcuts list (M4.5). */
  .keys{display:grid;gap:14px;}
  .keys h4{margin:0 0 6px;font:600 12px/1 inherit;color:var(--ink-2);}
  .keys dl{margin:0;display:grid;grid-template-columns:auto 1fr;gap:5px 14px;align-items:baseline;}
  .keys dt{margin:0;white-space:nowrap;}
  .keys dd{margin:0;font-size:12.5px;color:var(--ink);}
  .keys kbd{display:inline-block;padding:2px 6px;border:1px solid var(--line);border-bottom-width:2px;
    border-radius:5px;background:var(--panel-2);color:var(--ink);font:500 11px/1.4 ui-monospace,"Cascadia Mono",Consolas,monospace;}

  /* "Show keyboard focus clearly" (M4.5). :focus-visible only, so a mouse click never draws
     it; !important because several controls here paint their own focus. */
  html[data-focus="clear"] :focus-visible{outline:3px solid var(--accent)!important;outline-offset:2px!important;}
  html[data-focus="clear"] .sw:focus-visible,html[data-focus="clear"] .btn:focus-visible{border-radius:8px;}
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
  .st-cap{height:8px;border-radius:5px;background:var(--fill);overflow:hidden;margin:8px 0 2px;}
  .st-cap i{display:block;height:100%;border-radius:5px;background:var(--st-diagnostics,var(--accent));}
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
  /* M4.8: the share column is wider than it was — it carries a bar under the number now. */
  .st-cat > summary{display:grid;grid-template-columns:12px minmax(0,1fr) auto 68px 10px;align-items:center;gap:12px;
    padding:12px 18px;list-style:none;cursor:default;}
  .st-cat > summary::-webkit-details-marker{display:none;}
  .st-cat > summary:hover{background:var(--fill);}
  .st-cat + .st-cat{border-top:1px solid var(--line-soft);}
  .st-chip{width:12px;height:12px;border-radius:3px;}
  .st-name b{display:block;font-weight:500;font-size:14px;}
  .st-name span{display:block;font-size:12px;color:var(--ink-2);margin-top:1px;}
  .st-size{font-variant-numeric:tabular-nums;font-weight:600;text-align:right;white-space:nowrap;}
  .st-pct{font-variant-numeric:tabular-nums;color:var(--ink-2);font-size:12.5px;text-align:right;}
  /* M4.8 — the share as a bar under its own number, in that category's colour. */
  .st-pct b{display:block;font-weight:inherit;}
  .st-share{display:block;height:3px;border-radius:2px;margin-top:4px;background:var(--line-soft);overflow:hidden;}
  .st-share::after{content:"";display:block;height:100%;width:var(--w,0);background:var(--c,var(--accent));border-radius:2px;}
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
  /* M4.6: the tabs table carries one more figure - what each tab has loaded. */
  #pf-tabs .pf-row{grid-template-columns:minmax(0,1fr) 78px 62px 56px 72px auto;}
  .pf-head .pf-num[data-sort]{cursor:pointer;}
  .pf-head .pf-num[data-sort]:hover{color:var(--ink);}
  .pf-head .pf-num[data-sort][data-on="true"]{color:var(--accent);}
  .pf-head .pf-num[data-sort][data-on="true"]::after{content:" ↓";font-size:10px;}

  /* ── M4.8: the processes table reads like Task Manager ───────────────────
     Every part of Aviary on its own line with its own figures, the tabs a page process is
     drawing nested under it, sortable by any column, and each number shaded by how busy it
     is - so the one that is working is the one that catches your eye. */
  #pf-procs .pf-row{grid-template-columns:minmax(0,1fr) 62px 78px 62px 56px 76px 64px;}
  #pf-procs .pf-row .btns{display:none;}
  /* The two extra columns go first when there is no room. The panel is much narrower than the
     window, so this breakpoint is deliberately low — at Grant's 1280 the full table fits. */
  @media (max-width:860px){
    #pf-procs .pf-row{grid-template-columns:minmax(0,1fr) 62px 78px 62px 56px;}
    #pf-procs .pf-row .pf-num.pf-opt{display:none;}
  }
  .pf-row.pf-kid{padding-left:40px;background:var(--fill);}
  .pf-row.pf-kid .pf-n b{font-weight:450;color:var(--ink-2);font-size:12.5px;}
  .pf-row.pf-kid .pf-n b::before{content:"└ ";color:var(--ink-3);}
  .pf-row.pf-tot{font-weight:650;}
  .pf-row.pf-tot .pf-n b{font-weight:650;}

  /* Task Manager's heat, in Aviary's palette: nothing until it is worth noticing. */
  .pf-num[data-heat="1"]{background:color-mix(in srgb,var(--accent) 10%,transparent);}
  .pf-num[data-heat="2"]{background:color-mix(in srgb,var(--accent) 20%,transparent);}
  .pf-num[data-heat="3"]{background:color-mix(in srgb,#e8833a 30%,transparent);}
  .pf-num[data-heat]{border-radius:5px;padding:2px 5px;margin:-2px -5px -2px 0;}
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
  /* --m-x is Animation speed (M4.6): Calm stretches, Quick shortens, and every duration below
     is written through it, so one attribute on <html> changes the whole page's pace. */
  :root{--m-ease:cubic-bezier(.32,.72,0,1);--m-x:1;--m-dur:calc(200ms * var(--m-x));--m-pane:calc(240ms * var(--m-x));--m-rise:8px;interpolate-size:allow-keywords;}
  html[data-speed="calm"]{--m-x:1.5;}
  html[data-speed="quick"]{--m-x:.68;}
  html[data-motion="fluid"]{--m-ease:linear(0, .006, .025 2.8%, .101 6.1%, .539 18.9%, .721 25.3%, .849 31.5%, .937 38.1%, .968 41.8%, .991 45.7%, 1.006 50.1%, 1.015 55%, 1.017 63.9%, 1.001);--m-dur:calc(360ms * var(--m-x));--m-pane:calc(420ms * var(--m-x));--m-rise:14px;}
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
document.documentElement.dataset.speed = window.__aviarySpeed || 'standard';
/* Show keyboard focus clearly (M4.5): the host sets this, and the ring below follows it. */
if (window.__aviaryFocusRing) { document.documentElement.dataset.focus = 'clear'; }
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
  safety:'<svg viewBox="0 0 16 16" fill="none"><path d="M8 1.8 13.2 3.8v3.9c0 3.1-2.1 5.4-5.2 6.5-3.1-1.1-5.2-3.4-5.2-6.5V3.8L8 1.8Z" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/><path d="m5.6 8 1.7 1.7 3.2-3.4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/></svg>',
  news:'<svg viewBox="0 0 16 16" fill="none"><rect x="2.2" y="3" width="11.6" height="10" rx="1.6" stroke="currentColor" stroke-width="1.3"/><path d="M4.6 8.6h6.8M4.6 10.8h6.8" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/><rect x="4.4" y="5" width="4.2" height="1.9" rx=".5" fill="currentColor"/></svg>',
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
  { phase:5, name:'The Long Run',
    summary:'The last alpha — Brave-inspired, and built so that every privacy switch really does what it says.',
    short:'Shields, a real ad blocker you control site by site, joins privacy switches that really do what they say — Grant’s own projects live inside Aviary, in the Collection — and with Smooth Flight the whole browser moves, measures itself, and hands you the controls.',
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
      {n:'M4.5', t:'Smooth Flight', when:'20 Sep',
       d:'The second middle update: Aviary learns to move. Classic gets Smooth Flight and Liquid Glass gets Fluid, each look with its own way of moving; the ad blocker gets four lists, a setting of its own for every site and rules you can write yourself; and Settings gains live Performance and Storage Usage pages, more accessibility, and the two idle freezes explained.'},
      {n:'M4.6', t:'Smooth Fix', when:'20 Sep',
       d:'The third middle update: full screen learns to move, every animation gets a speed of its own, Liquid Glass decides where the window buttons sit, Storage and Performance go deeper, tabs gain four choices of their own — and Aviary finally has an icon.'},
      {n:'M4.7', t:'Immediate Fix', when:'21 Sep',
       d:'The menus work again. The arrival M4.6 gave every menu was landing on the wrong part of it, so a menu looked right and answered nothing you clicked — the app menu, Settings and the Collection with it.'},
      {n:'M4.8', t:'A Iconic Fix', when:'21 Sep',
       d:'The app icon really changes the taskbar now, and the row shows you which tile you are on. Performance reads like Task Manager — every part of Aviary on its own line, with the tabs it draws under it. Memory saver stops hinting and starts handing memory back: an idle tab goes to sleep and wakes when you click it.'},
      {n:'M4.9', t:'Birds News', when:'24 Sep',
       d:'Live news from the world and the Philippines on the Start Page, and five things from Brave, Safari and Chrome. Ctrl+Shift+A searches your open tabs. Any site can be set to be forgotten — when you close its last tab, or five minutes, an hour or a day after. Safety Check gathers everything that could be safer into one page, with a button to fix each one. The Start Page gets six backgrounds, five of them drawn by Aviary in whichever look you are using, and the sixth your own picture. Right-click any video for picture in picture. Memory saver becomes four modes, and improvement data gets 700 MB and collects everything but secrets.'},
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
      {h:'Smooth Flight — the way Aviary moves', items:[
        '<b>Each look moves its own way.</b> Classic gets <b>Smooth Flight</b>: quick, straight and calm, in the manner of Safari 15–18. Liquid Glass gets <b>Fluid</b>: springy and gel-like, in the manner of Safari 26. Appearance → <b>Animation style</b> can pin either one to both looks',
        '<b>Tabs move now.</b> The selected tab’s background glides to the one you pick — or stretches toward it and settles, in Fluid. Opening and closing a tab grows and shrinks it, and the rest close the gap instead of jumping',
        'The sidebar glides open, panes cross-fade, menus and popovers arrive with a short fade and scale, and Aviary’s panels slide in',
        'A thin line runs along the address bar while a page loads, the reload icon turns into stop, and the shield’s number pops when something is stopped',
        'The bars dim a little when Aviary is not the window you are using, as Safari’s do',
        'Settings and the Start Page move too: panes slide, the selection pill glides down the menu, rows arrive one after another, sections unfold, and the Privacy Report’s numbers count up',
        '<b>Reduce motion</b> in Accessibility turns every one of these off — and now tells websites as well, so their own animations stop',
        'None of it is drawn by the part of Aviary that handles your clicks, so nothing here can make the browser slower to answer'
      ]},
      {h:'Shields — more of it, and more of it yours', items:[
        '<b>Two more lists</b>, off until you switch them on: <b>Cookie notices</b>, which hides the “we value your privacy” banners, and <b>Social widgets</b>, which stops Like and Share buttons reporting the page you are on',
        '<b>Every site can have its own setting</b> — Off, Standard or Strict — from the shield in the toolbar, with the whole list of them in Settings and a way back to the default',
        '<b>My filters</b>: write your own rules, in the same syntax as the lists. Aviary says which lines it cannot use, and — until M4.9 — what you wrote was never recorded in improvement data',
        '<b>What Shields stopped</b>: this session, the last seven days and all time, with the trackers and sites at the top of each, and a way to start the count again',
        'The shield’s popover now lists <b>what it stopped on this page</b>, address by address, with the rule that caught each one',
        'New switches: hide the spaces ads leave, show the number on the shield, and Strict’s warning page — each its own choice. Updating can be Off, Daily or Weekly, and “Use the shipped lists” puts the downloads back',
        'Each list now says how many rules it brought, how many lines Aviary skipped, whose list it is and under which licence'
      ]},
      {h:'Settings — two new pages', items:[
        '<b>Storage Usage</b>, under Updates: everything Aviary keeps on this computer, sorted into what it is — the program, the Collection, website data, cached files, your Aviary data, filter lists, improvement data and crash reports — measured from the files themselves, so the parts add up to the whole',
        '<b>Performance</b>: what Aviary is using right now — memory, processor and graphics card, for the browser and every part of its engine, with a line per tab, the same figures Windows’ Task Manager shows. It only measures while you are looking at it, and records nothing',
        'Both pages say what measuring itself costs, so watching cannot quietly hide its own weight'
      ]},
      {h:'Accessibility', items:[
        '<b>Text size for Aviary’s own pages</b> — Settings and the Start Page — apart from the zoom you set for websites',
        '<b>Underline links on websites</b>, for the many sites that mark links with colour alone',
        '<b>Show keyboard focus clearly</b>: a thick ring in the accent colour around whatever the keyboard is on, in Aviary and on its own pages, and never when you click',
        '<b>Every keyboard shortcut</b>, listed in one place — the same list the browser itself reads',
        'Named for later, with the build each is due in: a minimum font size, reading pages aloud, a larger toolbar, and colour filters'
      ]},
      {h:'Tabs and windows', items:[
        '<b>Aviary asks before closing a window with several tabs</b>, and says how many are open. Private windows never ask',
        '<b>When the last tab closes</b> you can keep the window and get the Start Page, instead of the window closing',
        '<b>Where a new tab goes</b> — right after the one you are on, as Safari does, or at the end',
        '<b>A new tab can start on the page you are already looking at</b>, which is the quick way to open two of something side by side',
        '<b>The middle mouse button closes a tab</b>',
        '<b>The close cross</b> can be always there, only when you point at a tab, or never'
      ]},
      {h:'Smooth Fix — the last things that did not move', items:[
        '<b>Full screen moves now.</b> Going in, the toolbar and tabs lift away and the window changes on the last frame of that; coming out, the page stays while the chrome settles back down. Windows’ own presenter still takes about a tenth of a second in the middle, and Aviary now spends that time where nothing is on screen to stutter',
        '<b>Animation speed</b> — Calm, Standard or Quick — one choice that changes the pace of the whole browser, its own pages included',
        '<b>Menus and popovers grow out of the button</b> that opened them',
        '<b>A gleam follows your pointer</b> along the glass, in Liquid Glass with Fluid motion',
        '<b>Window buttons</b>, for Liquid Glass: leave the bar floating, run it up into the corner so Windows’ three buttons sit on the glass, or line the bar up with them. Snap Layouts keep working either way — they stay Windows’ own buttons'
      ]},
      {h:'Storage Usage, deeper', items:[
        '<b>Which sites keep their own store</b>, and how much of what sites keep is in the databases they all share — said plainly rather than split by guesswork',
        '<b>The compiled graphics shaders can be cleared</b>, at the next launch: the engine holds them open from the moment it starts, so that is the only honest moment to do it',
        '<b>Improvement data against its 650 MB</b>, drawn as a bar',
        'The page keeps itself current while you are looking at it'
      ]},
      {h:'Performance, more exactly', items:[
        '<b>What each tab has loaded</b> — counted only while this page is open, and only what the sites declared',
        '<b>The graphics card by the kind of work</b>: drawing, video, moving data',
        '<b>Memory in full</b>: what is in use, what Aviary has asked Windows for, and the most it has ever held at once',
        '<b>Sort the tabs</b> by memory, processor or data, and <b>choose how often</b> Aviary measures — every 1, 2 or 5 seconds'
      ]},
      {h:'Birds News', items:[
        '<b>Live news on your Start Page</b>, right under Favourites: <b>The World</b> from BBC News, Al Jazeera and The Guardian, and <b>The Philippines</b> from Inquirer, Rappler and GMA News, side by side. Each publisher gets a turn, newest first',
        '<b>Every headline exactly as its site wrote it</b>, with who wrote it and when. Aviary never rewrites, summarises or ranks anything — press a story and you are on the publisher’s own page',
        '<b>It keeps itself up to date</b> — every 15 minutes by default, or every 30, every hour, or only when you open the Start Page. A site with nothing new answers in a few bytes',
        '<b>All the stories in Birds News</b> opens the full list; Settings → <b>Birds News</b> has a switch for every publisher, room for any other news feed you like, and a <b>Right now</b> line saying what each site answered last time',
        'Aviary asks each site as itself, sends no cookies, and never fetches from a private window. <b>Switch Birds News off and nothing is fetched at all</b>',
        'One of the six stamps its stories with the wrong time zone; Aviary corrects it from that site’s own clock and says so, rather than showing its news as eight hours in the future'
      ]},
      {h:'Birds News, part one — memory saver and improvement data', items:[
        '<b>Memory saver has four modes.</b> <b>Chill</b> sleeps a tab you have left alone for an hour; <b>Balanced</b>, the default, after half an hour; <b>Aggressive</b> after five minutes, and it lets go of every background tab the moment you minimise the window. <b>Off</b> sleeps nothing. The same rules keep every mode safe: never a tab playing sound or video, never the tab you are on',
        'If memory saver was on before, it is Balanced now; if it was off, it stays off',
        '<b>Improvement data can keep 700 MB</b> — it was 650',
        '<b>It collects everything but secrets now.</b> The rules you write in My filters are recorded as you wrote them, not just counted. Passwords, what you type into websites, cookie values, login tokens and card numbers are still never collected',
        '<b>Frame timing</b>, if you want it: how smoothly Aviary draws, once a minute. It is a separate switch and stays off — asking Windows for every frame keeps the graphics card redrawing, so it is the one measurement that costs what it measures',
        'A tab closing during a busy moment no longer shows up as a problem in the improvement data. Nothing was wrong; it was being reported as if something was'
      ]},
      {h:'A Iconic Fix — the icon, and what Aviary is doing', items:[
        '<b>The app icon really changes the taskbar now.</b> It always changed the window’s own icon — but Aviary had no identity of its own as far as Windows was concerned, so the taskbar button drew the icon baked into the program file and never looked at the window’s. Aviary introduces itself properly now',
        '<b>The row shows you which tile you are on</b>, drawn: choosing one is something you can see, on the page where you chose it',
        '<b>Performance reads like Task Manager.</b> Every part of Aviary on its own line with its own columns — which process, memory, processor, graphics, graphics memory and handles — <b>sortable by any of them</b>, with the tabs a page process is drawing listed underneath it and a total at the foot',
        '<b>The busiest is shaded</b>, against the busiest row rather than a fixed number, so the one doing the work is the one that catches your eye',
        '<b>Memory saver now actually saves memory.</b> It only ever asked background tabs to drop their priority — a hint the engine could ignore, which left a background tab sitting on hundreds of megabytes. Now <b>a tab you have not looked at for a while is put to sleep</b> and really does hand it back; it wakes on the page you left the moment you click it',
        'Nothing is slept that should not be: never a tab playing sound or video (the engine itself refuses), never the tab you are on, never one of Aviary’s own pages, and never anything in a private window. How soon is up to you — M4.9 turned it into four modes',
        '<b>Storage Usage draws every share</b>, so which of the eight is actually big is a glance rather than a hover'
      ]},
      {h:'Immediate Fix — the menus answer again', items:[
        '<b>Every menu works again.</b> The arrival M4.6 gave menus was applied to the part of a menu that decides where it is, not to the part that is drawn — so a menu appeared exactly where it should and then answered your clicks somewhere else entirely. Clicking anything in it counted as clicking outside: the menu simply closed and nothing happened',
        'That took the <b>app menu</b> with it — Settings, the Collection, Downloads, New Tab, Help — and the <b>Aa</b> menu, the tab menus and the sidebar’s',
        'The menus still grow out of the button that opened them; only what the growing is applied to has changed',
        '<b>Leaving full screen</b> no longer leaves a hidden bar behind: a row that was not on screen when the chrome lifted away is now put back properly instead of being skipped'
      ]},
      {h:'Aviary has an icon', items:[
        '<b>A birdcage dome</b>, the same mark that is on the Start Page and in Settings — on a blue tile, or on a Liquid Glass one',
        'Accessibility → <b>App Icon</b> switches the window, the taskbar and Alt-Tab between them while Aviary runs',
        'This build is now called <b>The Long Run</b> — Alpha Build 5 has run further than any build before it'
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
        'Full screen went from window to page in one jump, with a freeze in the middle. It moves now, and the freeze is spent where nothing is on screen',
        'Settings and the store disagreed about “Keep the lists up to date” — the page read a switch saved before M4.5 as Off while the store still held the old value. The value itself is brought forward now',
        'Aviary seemed to freeze for up to a second while you were doing nothing. It was Windows’ own move-and-resize hold — dragging or holding the window’s edge stops every app the same way. Aviary now knows the difference and says which it was',
        'With tabs set to the top bar, the sidebar still listed them, and at start-up it could say “0 Tabs” beside an open page',
        'The ad blocker’s slowest check was 3.5–7.4 ms on long video addresses, felt as a stutter. Now under 1 ms',
        'Reset settings, Clear browsing data and Clear site icons did their work but left the Settings page showing the old values',
        'Tabs could lose their titles and icons while the sidebar was open',
        'A tab opened by a screen reader had no name until you clicked it',
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
/* M4.8 (bug 77): the App Icon row draws both tiles and rings the one you are on.
   The setting always worked - the window's icon really did change - but the only places it
   shows are the taskbar button and Alt-Tab, so choosing one looked like it did nothing. The
   same dome as the real .ico files (tools/icon/build-icon.ps1), drawn rather than embedded so
   it costs no bytes and follows the accent. */
function icoTile(kind){
  var glass = kind === 'glass';
  var tile = glass ? 'url(#icoGlass' + kind + ')' : 'url(#icoBlue' + kind + ')';
  var ink = glass ? '#0a70e0' : '#ffffff';
  return '<span class="icotile" data-tile="' + (glass ? 'Glass tile' : 'Blue tile') + '">' +
    '<svg viewBox="0 0 48 48" aria-hidden="true">' +
      '<defs>' +
        '<linearGradient id="icoBlue' + kind + '" x1="0" y1="0" x2="0" y2="1">' +
          '<stop offset="0" stop-color="#4c9bff"/><stop offset="1" stop-color="#0a5fd6"/></linearGradient>' +
        '<linearGradient id="icoGlass' + kind + '" x1="0" y1="0" x2="0" y2="1">' +
          '<stop offset="0" stop-color="#fdfdfe"/><stop offset="1" stop-color="#dfe7f1"/></linearGradient>' +
      '</defs>' +
      '<rect x="2" y="2" width="44" height="44" rx="12" fill="' + tile + '"/>' +
      '<g stroke="' + ink + '" fill="none" stroke-linecap="round">' +
        '<path d="M12 36V23.4A12 12 0 0 1 36 23.4V36" stroke-width="2.6"/>' +
        '<path d="M24 12.6V36M18 14.4V36M30 14.4V36" stroke-width="1.5"/>' +
        '<path d="M11 36h26" stroke-width="2.6"/>' +
      '</g>' +
    '</svg><i>' + (glass ? 'Glass tile' : 'Blue tile') + '</i></span>';
}
var ICON_TILES = '<span class="icotiles">' + icoTile('blue') + icoTile('glass') + '</span>';

var SECTIONS = [
  { id:'start', icon:'start', name:'Get started', title:'Get started',
    lede:'Aviary is a Safari-inspired browser for Windows 11, built one release at a time. You are on <span style="white-space:nowrap">__AV_CHANNEL__</span>.',
    rows:[
      {t:'On startup', d:'What Aviary opens with when you launch it.', phase:6, ctl:{type:'select', key:'startup', opts:['Start Page','Continue where you left off','A specific page'], value:'Start Page'}},
      {t:'Home button', d:'Show the home button in the toolbar.', ctl:{type:'toggle', key:'homeButton', value:true}},
      {t:'Start Page background', d:'What is behind the Start Page. Five of the six are drawn by Aviary out of the look you are using, so each one follows Classic, Liquid Glass, light and dark with nothing to download. Your own picture is copied into Aviary’s own folder, so it keeps working if you move or delete the original.', ctl:{type:'select', key:'startBackground', opts:['Glasshouse','Plain','Dawn','Dusk','Moss','Your picture'], value:'Glasshouse'}},
      {t:'Start Page sections', d:'The same switches as the Customise button in the corner of the Start Page.', ctl:{type:'none'}},
      {t:'Show Favourites', d:'', ctl:{type:'toggle', key:'startFavourites', value:true}},
      {t:'Show The Collection', d:'A row of Grant’s own projects, which Aviary carries and runs offline. All of them are always in the app menu.', ctl:{type:'toggle', key:'startMenagerie', value:true}},
      {t:'Show Frequently Visited', d:'', ctl:{type:'toggle', key:'startFrequent', value:true}},
      {t:'Show Reading List', d:'', ctl:{type:'toggle', key:'startReading', value:true}},
      {t:'Show Privacy Report', d:'', ctl:{type:'toggle', key:'startPrivacy', value:true}},
      {t:'Birds News', d:'Live headlines from the world and the Philippines, right under Favourites. Its own page has every switch.', ctl:{type:'goto', target:'news', label:'Birds News settings'}},
      /* Was "What works today", hand-written in Alpha Build 3 and still promising Alpha Build
         4's features two builds later. Now it is the current build's own line from UPDATES. */
      {t: CURRENT ? 'What’s new in ' + nameOf(CURRENT.phase) : 'What’s new', d: CURRENT ? CURRENT.short : '', ctl:{type:'goto', target:'updates', label:'See updates'}}
    ]},

  /* M4.9 — Birds News (BirdsNews.cs). The one part of Aviary that goes online by itself, so this
     page says exactly what it fetches, from whom, how often, and what it sends. */
  { id:'news', icon:'news', name:'Birds News', title:'Birds News',
    lede:'Live headlines on your Start Page, straight from the people who publish them. Aviary asks each site for its own news feed — as Aviary, not dressed up as a person’s browser — sends no cookies, and shows every headline exactly as its site wrote it. Nothing about what you read is sent anywhere.',
    rows:[
      {t:'Birds News', d:'Show the latest stories on the Start Page. <b>While this is off Aviary fetches nothing at all</b> — nobody is reading it. The same switch as <b>Birds News</b> in the Start Page’s Customise button.', ctl:{type:'toggle', key:'startNews', value:true}},
      {t:'Check for new stories', d:'How often Aviary asks the news sites for anything new, while it is open. A site that has nothing new answers in a few bytes, so checking often costs almost nothing. <b>Only when I open the Start Page</b> never checks in the background.', ctl:{type:'select', key:'newsRefresh', opts:['Every 15 minutes','Every 30 minutes','Every hour','Only when I open the Start Page'], value:'Every 15 minutes'}},
      {t:'The World', d:'', ctl:{type:'none'}},
      {t:'BBC News', d:'World news from the BBC, London.', ctl:{type:'toggle', key:'newsBbc', value:true}},
      {t:'Al Jazeera', d:'Al Jazeera English, Doha.', ctl:{type:'toggle', key:'newsAljazeera', value:true}},
      {t:'The Guardian', d:'The Guardian’s world desk, London.', ctl:{type:'toggle', key:'newsGuardian', value:true}},
      {t:'The Philippines', d:'', ctl:{type:'none'}},
      {t:'Inquirer', d:'The Philippine Daily Inquirer’s news desk.', ctl:{type:'toggle', key:'newsInquirer', value:true}},
      {t:'Rappler', d:'Rappler, Manila.', ctl:{type:'toggle', key:'newsRappler', value:true}},
      {t:'GMA News', d:'GMA News Online.', ctl:{type:'toggle', key:'newsGma', value:true}},
      {t:'Your own feeds', d:'Any other news site’s RSS or Atom feed, one address per line — they appear as a column of their own. A line starting with <code>!</code> is a note. Aviary says below which lines it cannot use.', ctl:{type:'text', key:'newsCustom', value:''}},
      {t:'Right now', d:'<span id="newsstatus">Not checked yet.</span>', ctl:{type:'buttons', items:[{cmd:'newsCheck', label:'Check now'}]}}
    ] },

  { id:'look', icon:'look', name:'Appearance', title:'Appearance',
    lede:'How Aviary looks. Both Safari looks switch while the browser is running, and whichever you leave it on is the one it opens with next time.',
    rows:[
      {t:'Theme', d:'Follow Windows, or pin Aviary to light or dark.', ctl:{type:'select', key:'theme', opts:['System','Light','Dark'], value:'System'}},
      {t:'Safari look', d:'Classic is the solid unified toolbar of Safari 15–18. Liquid Glass is the floating translucent chrome of Safari 26 — translucent enough to let Windows’ Mica show through.', ctl:{type:'select', key:'look', opts:['Classic','Liquid Glass'], value:'Classic'}},
      {t:'Tab layout', d:'A tab bar across the top, or a vertical sidebar down the side.', phase:4, ctl:{type:'select', key:'tabLayout', opts:['Top bar','Sidebar'], value:'Top bar'}},
      /* Alpha Build 5 M4.5 — both live (MainWindow.CloseGuard.cs). A private window never
         asks and never stays open: its whole point is that it leaves nothing behind. */
      {t:'Ask before closing a window with several tabs', d:'One stray click on the × should not take six pages with it. Aviary asks first, and says how many are open. Private windows never ask.', ctl:{type:'toggle', key:'warnManyTabs', value:true}},
      {t:'When the last tab closes', d:'Close the window, the way every browser on Windows does — or keep it open and show the Start Page.', ctl:{type:'select', key:'lastTabCloses', opts:['Close the window','Show the Start Page'], value:'Close the window'}},
      /* Alpha Build 5 M4.6 — four small tab choices, all live. */
      {t:'New tab position', d:'<b>After the current tab</b> is Safari’s way — a tab you open from a page sits beside it. <b>At the end</b> is Chrome’s. Pinned tabs always keep the front of the strip.', ctl:{type:'select', key:'newTabPosition', opts:['After the current tab','At the end'], value:'After the current tab'}},
      {t:'New tab opens with', d:'A new tab starts on the Start Page, or on the page you are already looking at — which is the quick way to open two of something side by side. Aviary’s own pages are never carried over.', ctl:{type:'select', key:'newTabShowsPage', opts:['The Start Page','The page I’m on'], value:'The Start Page'}},
      {t:'Middle-click closes a tab', d:'Press the mouse wheel on a tab to close it, the way every browser on Windows does.', ctl:{type:'toggle', key:'middleClickCloses', value:true}},
      {t:'Tab close button', d:'<b>On hover</b> is Safari’s: a tab’s icon turns into a cross when you point at it. <b>Always</b> keeps the cross there, and <b>Never</b> leaves the icon alone — Ctrl+W, the middle button and the tab’s own menu still close it.', ctl:{type:'select', key:'tabCloseButton', opts:['On hover','Always','Never'], value:'On hover'}},
      /* Alpha Build 5 M4.5 — Motion.cs. Grant’s names: Smooth Flight is Classic’s way of moving,
         Fluid is Liquid Glass’s. Reduce motion (Accessibility) turns both off. */
      {t:'Animation style', d:'How Aviary moves. <b>Smooth Flight</b> is crisp and quick, like Safari on the Mac. <b>Fluid</b> is springy and gel-like, like Safari 26 — tabs stretch toward where they are going and buttons squish when pressed. Each look has its own, or choose one for both.', ctl:{type:'select', key:'animationStyle', opts:['Match the look','Smooth Flight','Fluid'], value:'Match the look'}},
      /* Alpha Build 5 M4.6 — one multiplier on every duration, in the window and in these pages. */
      {t:'Animation speed', d:'How fast all of that moves. <b>Calm</b> stretches every animation by half, <b>Quick</b> cuts it by a third. Springs keep their bounce either way — a spring’s feel is its give, not its length.', ctl:{type:'select', key:'animationSpeed', opts:['Calm','Standard','Quick'], value:'Standard'}},
      /* M4.6 — MainWindow.ApplyLookGeometry. Windows draws the three buttons itself and decides
         where they sit; the bar is what moves. Classic is already aligned, so this does nothing there. */
      {t:'Window buttons', d:'Where Liquid Glass puts its bar, and what that does about Windows’ three buttons. <b>Floating</b> leaves the bar 8&nbsp;px down, so the buttons ride a little above its middle. <b>Buttons on the glass</b> runs the bar up into the corner so they sit on it — they are still Windows’ own buttons, so Snap Layouts keep working. <b>Bar aligned</b> keeps the side gaps and drops the top one. Classic is already lined up, and ignores this.', ctl:{type:'select', key:'windowButtons', opts:['Floating','Buttons on the glass','Bar aligned'], value:'Floating'}},
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
      {t:'My filters', d:'Your own rules, in the same syntax as the lists: <code>||ads.example.com^</code> to stop a host, <code>example.com##.banner</code> to hide something, <code>@@||example.com^</code> to let a site through. One per line; a line starting with <code>!</code> is a note. Aviary says which lines it cannot use. If <b>Allow data collection for improvements</b> is on, what you write here is recorded as you wrote it — you chose that in M4.9; it never leaves this computer.', ctl:{type:'text', key:'customFilters', value:''}},

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
      {t:'Forgetful sites', d:'Sites Aviary forgets on its own — their cookies and everything they store — when you close their last tab, or a set time after. Stay signed in where you want to be; be a stranger everywhere else. Choose it for a site from the <b>shield</b> in the toolbar.<span class="fg" id="forgetlist"></span>', ctl:{type:'none'}},
      {t:'Clear browsing data', d:'History, cookies and cached files kept in this profile.', phase:4, ctl:{type:'button', cmd:'clearData', label:'Clear\u2026'}},
      {t:'Cached site icons', d:'__AV_ICONCACHE__', ctl:{type:'button', cmd:'clearFavicons', label:'Clear'}},
      /* Diagnostics \u2014 PHASE5-PLAN.md M1. Deliberately at the BOTTOM of Privacy and deliberately
         blunt: it records every address, title and search, and the row has to say so in those
         words. Live, not phase-gated: it works now, and every later milestone is measured by it. */
      {t:'Allow data collection for improvements', d:'Records everything Aviary does \u2014 every web address you visit, every page title and every search, every request and blocked tracker, every button you press, every error and crash \u2014 into a folder on this computer, so problems can be found and fixed. <b>It never leaves this computer.</b> Off until you switch it on.', ctl:{type:'toggle', key:'improvementCollection', value:false}},
      {t:'What Aviary collects', d:'One switch, all or nothing. While it is on, all sixteen of these are recorded; passwords, what you type into websites, cookies, login tokens, card numbers and your clipboard never are.', more:'diag', ctl:{type:'none'}},
      {t:'Also measure frame timing', d:'How smoothly Aviary draws, once a minute: how many frames, the usual gap between them, and how many were slow. <b>Off even while collection is on, and it should stay off unless you are chasing a stutter.</b> Windows only offers per-frame times to a program that asks the graphics card to redraw <i>continuously</i> — so measuring smoothness this way is the one measurement in Aviary that costs the very thing it is measuring. Everything else here is free. The figures are marked, so a session measured this way is never compared against a normal one.', ctl:{type:'toggle', key:'frameTiming', value:false}},
      {t:'Collected data', d:'<span id="diagstatus"></span><span class="path" id="diagpath"></span>', ctl:{type:'buttons', items:[{cmd:'diagOpen', label:'Open folder'}, {cmd:'diagDelete', label:'Delete\u2026', danger:true}]}}
    ]},

  /* M4.9 — Safety Check (SafetyCheck.cs): the settings Aviary already holds, read back as a list of
     what is safe, what could be safer, and what is a choice worth knowing about. */
  { id:'safety', icon:'safety', name:'Safety Check', title:'Safety Check', safety:true,
    lede:'Everything on this page is a setting you can already find elsewhere in Settings, or something Aviary already knows — gathered in one place, with a button to fix anything that could be safer. It runs each time you open this page; nothing is sent anywhere.',
    rows:[] },

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
    lede:'Aviary follows the Windows contrast themes; both Safari looks have a high-contrast palette built in. Windows’ own scrollbars are always visible in Aviary — the thin overlay kind is not used, so there is nothing here to switch.',
    rows:[
      /* Alpha Build 5 M4.5 — all four of these are live. Text size goes through the same CSS
         zoom the website zoom uses (BrowserTab.ApplyZoom), so Reader and the error pages get it
         too; underlined links are one adopted stylesheet (ReadingAids.cs); the focus ring is a
         data attribute on Aviary’s own pages and the focus-visual resources in its interface. */
      {t:'Text size for Aviary’s own pages', d:'Settings and the Start Page — the two pages Aviary writes itself. Reader has its own size in the <b>Aa</b> menu, and websites keep the zoom you set for them in Appearance.', ctl:{type:'select', key:'pageTextSize', opts:['Default','Large','Larger','Largest'], value:'Default'}},
      {t:'Underline links on websites', d:'A great many sites mark their links with colour alone, which does not show at all if you cannot tell those colours apart. This puts the line back, on every site.', ctl:{type:'toggle', key:'underlineLinks', value:false}},
      {t:'Show keyboard focus clearly', d:'A thick ring in the accent colour around whatever the keyboard is on — in Aviary’s toolbar and menus, and on its own pages. Only for the keyboard: clicking something never draws it.', ctl:{type:'toggle', key:'focusRing', value:false}},
      {t:'Reduce motion', d:'Turns off Aviary’s own animations — tabs, the sidebar, panels, Settings and the Start Page — so things appear in place instead of moving there. Aviary reads your Windows animation setting the first time it runs, so this usually starts where you want it. Since M4.5 it tells websites too, which stops their own animations where they honour it; that half needs a restart.', restart:true, ctl:{type:'toggle', key:'reduceMotion', value:false}},
      /* Alpha Build 5 M4.6 — Grant asked for this here, by this name. Both icons ship; the one
         inside the .exe is the blue tile, which is why Explorer keeps showing that one. */
      {t:'App Icon', d:'Which of Aviary’s two icons the <b>taskbar button</b> and <b>Alt-Tab</b> wear — the change is immediate, and the tile below shows which you are on. Aviary draws its own title bar, so there is no icon inside the window itself. The <b>Desktop shortcut and Explorer keep the blue one</b>: that icon lives inside the program file and cannot change while Aviary is running.' + ICON_TILES, ctl:{type:'select', key:'appIcon', opts:['Blue tile','Glass tile'], value:'Blue tile'}},
      {t:'Keyboard shortcuts', d:'Every shortcut Aviary knows. They work on a page as well as in the browser: Aviary reads them before a website can swallow them.', more:'keys', ctl:{type:'none'}},
      {t:'Minimum font size', d:'Never render text smaller than this.', phase:5, ctl:{type:'select', key:'minFont', opts:['None','12px','14px','16px','18px'], value:'None'}},
      {t:'Read pages aloud', d:'Have Aviary read an article out, with the words marked as it goes.', phase:6, ctl:{type:'none'}},
      {t:'Larger toolbar and tabs', d:'Bigger buttons and taller tabs, for touch and for anyone who finds them small.', phase:6, ctl:{type:'none'}},
      {t:'Colour filters for web pages', d:'Adjust the colours of every page for colour blindness.', phase:7, ctl:{type:'none'}}
    ]},

  { id:'system', icon:'system', name:'System', title:'System',
    lede:'How Aviary uses the machine.',
    rows:[
      {t:'Hardware acceleration', d:'Let the GPU draw pages. Turning it off is a troubleshooting step, not an optimisation.', phase:5, ctl:{type:'toggle', key:'gpu', value:true}},
      {t:'Continue running in the background', d:'Keep Aviary alive after the last window closes.', phase:5, ctl:{type:'toggle', key:'background', value:false}},
      {t:'Memory saver', d:'How hard Aviary works to give memory back. Background tabs always drop their memory priority, and a tab you have not looked at for a while is <b>put to sleep</b> — which really does hand its memory back, a heavy page can be holding hundreds of megabytes. A sleeping tab wakes the moment you click it, on the page you left.<br><b>Chill</b> sleeps a tab after an hour. <b>Balanced</b> after half an hour. <b>Aggressive</b> after five minutes, and lets go of every background tab as soon as you minimise the window. <b>Off</b> never sleeps anything.<br>A tab <b>playing sound or video is never put to sleep</b> — the engine itself refuses — and neither is the tab you are on, one of Aviary’s own pages, or anything in a private window. Performance lists which tabs are asleep.', ctl:{type:'select', key:'memorySaver', opts:['Off','Chill','Balanced','Aggressive'], value:'Balanced'}},
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
    lede:'What Aviary is using right now, while this page is open: memory, processor and graphics card, for Aviary itself and every part of its browser engine. The same figures Windows’ Task Manager shows. Nothing here is recorded.',
    rows:[
      /* M4.6 — LiveStats reads this when it starts, so the choice takes effect the next time
         you open this page. The foot of the page says which rate it is showing. */
      {t:'How often to measure', d:'Slower costs Aviary less and steadies the lines; faster catches a spike you would otherwise miss. The figures at the bottom of this page say what the measuring itself costs.', ctl:{type:'select', key:'perfInterval', opts:['Every second','Every 2 seconds','Every 5 seconds'], value:'Every second'}}
    ] },

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
    /* A key the host keeps private is reported as a count, never as its lines. M4.9 (bug 80):
       the page used to decide that for itself — every text box, whatever the host said — so
       when Grant chose to have "My filters" collected and the host's list went empty, the page
       still sent "0 lines (not recorded)", the host compared it with the real value, and the
       round-trip check flagged a mismatch on every visit. One list now, from the host. */
    var privateKeys = window.__aviaryPrivateKeys || [];
    RENDERED[c.key] = (c.type === 'toggle') ? (c.value ? 'true' : 'false')
                    : (privateKeys.indexOf(c.key) >= 0) ? lineCount(c.value) + ' lines (not recorded)'
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
  /* Every shortcut, from the one table the browser itself reads (M4.5). */
  keys: '<details class="more"><summary>Show every shortcut</summary>' +
        '<div class="more__body"><div class="keys" id="keylist"></div></div></details>',
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
  } else if (s.safety){
    body += '<div class="sc-head"><b id="sc-sum">Checking…</b><button class="btn" id="sc-again">Check again</button></div>' +
            '<div class="group" id="sc-list"></div>';
  } else if (s.storage){
    body += renderStorage();
  } else if (s.perf){
    body += renderPerf();
    /* M4.6: the Performance pane has a row of its own now - how often it measures. */
    if (s.rows.length) { body += '<div class="group">' + s.rows.map(rowHtml).join('') + '</div>'; }
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

/* ── Every keyboard shortcut (M4.5) ──────────────────────────────────── */
/* The same list the browser itself acts on: MainWindow's accelerators and the page-side MAP in
   BrowserTab.cs, which is why a shortcut works on a website too. Kept in this order on purpose -
   tabs, then the page, then windows - rather than alphabetically. */
var KEYS = [
  {h:'Tabs and windows', rows:[
    ['Ctrl T', 'New tab'],
    ['Ctrl W', 'Close this tab'],
    ['Ctrl Tab', 'Next tab'],
    ['Ctrl Shift Tab', 'Previous tab'],
    ['Ctrl 1 – 8', 'Go to that tab'],
    ['Ctrl 9', 'Go to the last tab'],
    ['Ctrl Shift N', 'New private window'],
    ['Ctrl Shift A', 'Search your open tabs']
  ]},
  {h:'Getting about', rows:[
    ['Ctrl L', 'Type an address or search'],
    ['Ctrl R', 'Reload — or stop, while a page is loading'],
    ['Ctrl Shift R', 'Reader'],
    ['Ctrl F', 'Find on this page'],
    ['Esc', 'Close the find bar'],
    ['F11', 'Full screen']
  ]},
  {h:'Keeping things', rows:[
    ['Ctrl D', 'Add to Favourites'],
    ['Ctrl Shift D', 'Add to Reading List'],
    ['Ctrl H', 'History'],
    ['Ctrl J', 'Downloads'],
    ['Ctrl Shift L', 'Show or hide the sidebar']
  ]},
  {h:'Size', rows:[
    ['Ctrl +', 'Zoom in'],
    ['Ctrl -', 'Zoom out'],
    ['Ctrl 0', 'Back to 100%']
  ]},
  {h:'Settings', rows:[
    ['Ctrl ,', 'Open Settings']
  ]}
];

function keyList(){
  var box = document.getElementById('keylist');
  if (!box) { return; }
  box.textContent = '';
  KEYS.forEach(function(group){
    var section = document.createElement('div');
    var h = document.createElement('h4');
    h.textContent = group.h;
    section.appendChild(h);
    var dl = document.createElement('dl');
    group.rows.forEach(function(row){
      var dt = document.createElement('dt');
      row[0].split(' ').forEach(function(key, i){
        if (i) { dt.appendChild(document.createTextNode(' ')); }
        var k = document.createElement('kbd');
        k.textContent = key;
        dt.appendChild(k);
      });
      var dd = document.createElement('dd');
      dd.textContent = row[1];
      dl.appendChild(dt);
      dl.appendChild(dd);
    });
    section.appendChild(dl);
    box.appendChild(section);
  });
}
keyList();

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

/* M4.8 (bug 77) — ring whichever tile is in force. Reads the row rather than a stored copy, so
   it is right whether the value came from the store, from a Reset, or from a click just now. */
/* M4.9 — Birds News, "Right now": what each feed answered the last time it was asked. Built
   with DOM text, never markup — a feed's name for one of your own addresses is its host name,
   which came off the internet. */
function nwAgo(ms){
  if (!ms) { return ''; }
  var s = Math.max(0, (Date.now() - ms) / 1000);
  if (s < 90) { return 'just now'; }
  if (s < 3600) { return Math.round(s / 60) + ' min ago'; }
  var h = Math.round(s / 3600); return h < 48 ? h + (h === 1 ? ' hour ago' : ' hours ago') : Math.round(h / 24) + ' days ago';
}
window.__aviaryNewsStatus = function(n){
  var el = document.getElementById('newsstatus');
  /* Bug 82: "Check now" is quiet from the press until this answer, so it cannot be pressed eight
     times in two seconds. */
  var chk = document.querySelector('.btn[data-cmd="newsCheck"]');
  if (chk) { chk.disabled = false; chk.textContent = 'Check now'; }
  if (!el || !n) { return; }
  el.textContent = '';
  function line(name, text){
    var s = document.createElement('span');
    if (name) { var b = document.createElement('b'); b.textContent = name; s.appendChild(b); s.appendChild(document.createTextNode(' — ')); }
    s.appendChild(document.createTextNode(text));
    el.appendChild(s);
  }
  if (!n.on) { line('', 'Birds News is switched off, so nothing is being fetched.'); return; }
  var feeds = n.feeds || [];
  if (!feeds.length) { line('', 'Every feed is switched off, so nothing is being fetched.'); }
  feeds.forEach(function(f){
    var when = f.at ? ' · ' + nwAgo(f.at) : '';
    line(f.name,
      f.state === 'ok' ? f.stories + (f.stories === 1 ? ' story' : ' stories') + when + (f.note ? ' · ' + f.note : '')
    : f.state === 'unchanged' ? 'nothing new, ' + f.stories + ' held' + when + (f.note ? ' · ' + f.note : '')
    : f.state === 'failed' ? (f.why || 'no answer') + when + (f.stories ? ' — ' + f.stories + ' stories kept from before' : '')
    : 'waiting for the first check');
  });
  (n.problems || []).forEach(function(p){ line('Not used', p.line + ' — ' + p.why); });
};
window.__aviaryNewsStatus(window.__aviaryNews);

/* ── Safety Check (M4.9) ─────────────────────────────────────────────────────
   The host works it out (SafetyCheck.cs) - it knows which sites have Shields lowered, how old the
   lists are, and what the engine was launched with - and the page draws it. Built with DOM text. */
function safetyRun(){
  var sum = document.getElementById('sc-sum');
  if (sum) { sum.textContent = 'Checking…'; }
  post({type:'command', name:'safetyCheck'});
}
window.__aviarySafety = function(list){
  var box = document.getElementById('sc-list'), sum = document.getElementById('sc-sum');
  if (!box || !sum) { return; }
  var warn = list.filter(function(f){ return f.level === 'warn'; }).length;
  sum.textContent = warn === 0 ? 'Everything here is as safe as it can be.'
                  : warn === 1 ? '1 thing could be safer.' : warn + ' things could be safer.';
  box.textContent = '';
  /* The ones to act on first, then the choices worth knowing, then what is already fine. */
  var rank = {warn:0, info:1, ok:2};
  list.slice().sort(function(a, b){ return rank[a.level] - rank[b.level]; }).forEach(function(f){
    var row = document.createElement('div'); row.className = 'row sc-row';
    var dot = document.createElement('span'); dot.className = 'sc-dot sc-' + f.level;
    dot.textContent = f.level === 'ok' ? '✓' : f.level === 'warn' ? '!' : 'i';
    var t = document.createElement('div'); t.className = 'row__t';
    var b = document.createElement('b'); b.textContent = f.title; t.appendChild(b);
    if (f.detail) { var d = document.createElement('span'); d.textContent = f.detail; t.appendChild(d); }
    row.appendChild(dot); row.appendChild(t);
    if (f.fixLabel) {
      var btn = document.createElement('button'); btn.className = 'btn'; btn.textContent = f.fixLabel;
      if (f.fixKey) { btn.setAttribute('data-sc-fix', f.fixKey); }
      if (f.goto) { btn.setAttribute('data-sc-goto', f.goto); }
      row.appendChild(btn);
    }
    box.appendChild(row);
  });
};
main.addEventListener('click', function(e){
  if (e.target.closest('#sc-again')) { safetyRun(); return; }
  var fix = e.target.closest('[data-sc-fix]');
  if (fix) { fix.disabled = true; post({type:'command', name:'safetyFix', arg: fix.getAttribute('data-sc-fix')}); return; }
  var go = e.target.closest('[data-sc-goto]');
  if (go) {
    var target = go.getAttribute('data-sc-goto');
    if (target.indexOf('cmd:') === 0) {
      go.disabled = true; go.textContent = 'Updating…';
      post({type:'command', name: target.substring(4)});
      setTimeout(safetyRun, 6000);
    } else { showPane(target); }
  }
});

/* M4.9 — Forgetful sites. Site names are shown as text, never markup. */
window.__aviaryForgetList = function(list){
  var box = document.getElementById('forgetlist');
  if (!box) { return; }
  box.textContent = '';
  if (!list || !list.length) { var none = document.createElement('i'); none.textContent = 'No site is set to be forgotten.'; box.appendChild(none); return; }
  list.forEach(function(f){
    var row = document.createElement('span'); row.className = 'fg__row';
    var b = document.createElement('b'); b.textContent = f.site; row.appendChild(b);
    var when = document.createElement('em');
    var dueText = '';
    if (f.done) { dueText = ' · closed, and forgotten'; }
    else if (f.due) { var mins = Math.max(0, Math.round((f.due - Date.now()) / 60000)); dueText = mins < 1 ? ' · forgetting now' : ' · closed, forgotten in ' + (mins < 90 ? mins + ' min' : Math.round(mins / 60) + ' h'); }
    when.textContent = f.label + dueText; row.appendChild(when);
    var stop = document.createElement('button'); stop.className = 'btn'; stop.textContent = 'Stop forgetting';
    stop.setAttribute('data-forget-stop', f.site); row.appendChild(stop);
    box.appendChild(row);
  });
};
window.__aviaryForgetList(window.__aviaryForget);
main.addEventListener('click', function(e){
  var stop = e.target.closest('[data-forget-stop]');
  if (stop) { stop.disabled = true; post({type:'command', name:'forgetStop', arg: stop.getAttribute('data-forget-stop')}); }
});

function iconTiles(){
  var sel = document.querySelector('.sel[data-key="appIcon"] .sel__btn span');
  var now = sel ? sel.textContent.trim() : saved('appIcon', 'Blue tile');
  [].forEach.call(document.querySelectorAll('.icotile'), function(t){
    t.setAttribute('data-on', t.getAttribute('data-tile') === now ? 'true' : 'false');
  });
}
iconTiles();

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
  if (id === 'safety') { safetyRun(); }
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
    if (host.getAttribute('data-key') === 'appIcon') { iconTiles(); }
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
  if (btn && !btn.disabled) {
    var cmd = btn.getAttribute('data-cmd');
    sendCommand(cmd);
    /* Bug 82: "Check now" goes quiet in the SAME step as it is sent, so the answer cannot arrive
       first. Deferred with setTimeout it did, on a fast machine: the answer woke the button,
       then the late disable put it back to sleep for good. __aviaryNewsStatus wakes it. */
    if (cmd === 'newsCheck') { btn.disabled = true; btn.textContent = 'Checking…'; }
  }
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
var stTimer = 0;
function storageWatch(){
  var pane = document.getElementById('pane-storage');
  var on = !!(pane && pane.classList.contains('on'));
  if (on && !stRequested) { storageMeasure(); }

  /* M4.6: while the page is the one on screen it measures again every ten seconds, so a
     download or a cleared cache shows up without being asked. It stops the moment you leave. */
  if (on && !stTimer) {
    stTimer = setInterval(function(){
      if (document.hidden) { return; }
      var p = document.getElementById('pane-storage');
      if (!p || !p.classList.contains('on')) { clearInterval(stTimer); stTimer = 0; return; }
      stRequested = false;
      storageMeasure();
    }, 10000);
  } else if (!on && stTimer) {
    clearInterval(stTimer); stTimer = 0;
  }
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

    /* M4.8: the share drawn as well as written. The stacked bar at the top of the page shows
       the split, but only on hover and only if you go looking for it — a bar on the row itself
       makes "which of these is actually big" answerable by running your eye down the list. */
    var share = 100 * c.bytes / Math.max(d.bytes, 1);
    var pct = document.createElement('span'); pct.className = 'st-pct';
    var pctBar = document.createElement('i');
    pctBar.className = 'st-share';
    pctBar.style.setProperty('--w', Math.max(share, share > 0 ? 1.5 : 0) + '%');
    pctBar.style.setProperty('--c', 'var(--st-' + c.key + ')');
    var pctNum = document.createElement('b');
    pctNum.textContent = share.toFixed(1) + '%';
    pct.appendChild(pctNum); pct.appendChild(pctBar);

    sum.appendChild(chip); sum.appendChild(name); sum.appendChild(size); sum.appendChild(pct);
    sum.insertAdjacentHTML('beforeend', CHEV);
    det.appendChild(sum);

    var body = document.createElement('div'); body.className = 'st-body';
    var p = document.createElement('p'); p.textContent = c.plain; body.appendChild(p);
    if (c.key === 'diagnostics') {
      var capP = document.createElement('p');
      var used = Math.min(1, c.bytes / Math.max(d.capBytes, 1));
      capP.textContent = stSize(c.bytes) + ' of ' + stSize(d.capBytes) + ' used — ' + (100 * used).toFixed(1) +
        '% of what improvement data may keep. The oldest goes first when it fills.';
      body.appendChild(capP);

      /* M4.6: the cap drawn, not just described. */
      var capBar = document.createElement('div'); capBar.className = 'st-cap';
      var capFill = document.createElement('i'); capFill.style.width = Math.max(0.6, 100 * used) + '%';
      capBar.appendChild(capFill);
      body.appendChild(capBar);
    }

    /* M4.6 — which sites keep something of their own, and how much is in the shared stores. */
    if (c.key === 'sites') {
      var siteP = document.createElement('p');
      siteP.textContent = (d.sites && d.sites.length)
        ? 'Sites with a store of their own, biggest first. The rest — ' + stSize(d.sitesShared || 0) +
          ' — is in the cookie and local-storage databases, which every site shares, so it cannot be split by site here.'
        : 'No site has a store of its own yet. What sites keep so far — ' + stSize(d.sitesShared || 0) +
          ' — is in the cookie and local-storage databases they all share.';
      body.appendChild(siteP);

      if (d.sites && d.sites.length) {
        var siteBox = document.createElement('div'); siteBox.className = 'st-parts';
        d.sites.forEach(function(it){
          var row = document.createElement('div');
          var lb = document.createElement('b'); lb.textContent = it.label;   /* a site's name: text, never markup */
          var sz = document.createElement('i'); sz.textContent = stSize(it.bytes) + ' · ' + it.files.toLocaleString() + (it.files === 1 ? ' file' : ' files');
          row.appendChild(lb); row.appendChild(sz); siteBox.appendChild(row);
        });
        body.appendChild(siteBox);
      }
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

      /* M4.6: the compiled-shader caches are open from the moment the engine starts, so they
         can only go before it does - at the next launch. The button says exactly that. */
      var pending = document.createElement('p');
      pending.textContent = d.gpuPending
        ? 'The graphics caches will be cleared the next time Aviary starts.'
        : 'The compiled graphics shaders cannot be cleared while Aviary is running — the engine holds them open from the moment it starts.';
      body.appendChild(pending);
      act(d.gpuPending ? 'Don’t clear them' : 'Clear graphics caches at the next launch', function(b){
        b.disabled = true;
        post({type:'command', name:'storageClearGpu', arg: d.gpuPending ? 'off' : 'on'});
      });
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
      /* M4.6 */
      '<div class="row"><div class="row__t"><b>Memory, in full</b><span id="pf-mem2">Measuring…</span></div></div>' +
      '<div class="row"><div class="row__t"><b>What the graphics card was doing</b><span id="pf-gpuwork">Measuring…</span></div></div>' +
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
  nums.forEach(function(v){
    var c = document.createElement('div');
    c.className = 'pf-num';
    /* A cell may be {v:'12.4 MB', heat:2} - the text, and how hard that part is working. */
    if (v && typeof v === 'object') {
      c.textContent = v.v;
      if (v.heat) { c.setAttribute('data-heat', v.heat); }
      if (v.opt) { c.className += ' pf-opt'; }
    } else {
      c.textContent = v;
    }
    row.appendChild(c);
  });
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

var PF_SORT = 'memory';
var PF_PSORT = 'none';
var PF_LAST = null;

/* How busy is this, on a scale a colour can show? (M4.8.)
   Shaded against the BUSIEST row rather than against fixed numbers: absolute thresholds meant a
   quiet browser had no shading at all (measured — every cell came back blank on an idle machine),
   which is exactly the "it seems to have no effect" that M4.8 exists to stop. `floor` keeps a
   genuinely idle table plain: nothing is shaded until it is worth looking at, and then the
   heaviest part always is. Three steps, not a gradient — a tint that is always slightly on reads
   as noise. */
function pfHeat(v, max, floor){
  if (v == null || max == null || max <= 0 || v < floor) { return 0; }
  var share = v / max;
  return share >= 0.66 ? 3 : share >= 0.33 ? 2 : share >= 0.12 ? 1 : 0;
}
function pfMax(list, key){
  return list.reduce(function(m, x){ return Math.max(m, x[key] || 0); }, 0);
}
window.__aviaryPerf = function(d){
  PF_LAST = d;
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

  /* M4.6 — memory Aviary has asked Windows for, and the most it has ever held at once. */
  var m2 = document.getElementById('pf-mem2');
  if (m2) {
    m2.textContent = 'In use now ' + pfSizeText(d.memory) + ' · asked for ' + pfSizeText(d.commit || 0) +
      ' · the most held at once ' + pfSizeText(d.peak || 0) +
      '. "Asked for" does not fall when Windows pages memory out, so a tab that really gave memory back shows it here.';
  }

  var gw = document.getElementById('pf-gpuwork');
  if (gw) {
    var eng = d.gpuEngines || [];
    gw.textContent = !eng.length
      ? 'Nothing measurable in the last second.'
      : eng.map(function(e){ return e.name + ' ' + pfPctText(e.percent); }).join(' · ') +
        '. Aviary\'s headline figure is its busiest part, not the sum — the card runs them at once.';
  }

  var tabs = document.getElementById('pf-tabs');
  if (tabs) {
    tabs.textContent = '';

    /* M4.6 — sorted by what you choose; the heading cells say so and can be clicked. */
    var order = PF_SORT;
    var list = d.tabs.slice().sort(function(a, b){
      if (order === 'cpu') { return (b.cpu || 0) - (a.cpu || 0); }
      if (order === 'data') { return (b.data || 0) - (a.data || 0); }
      if (order === 'memory') { return (b.memory || 0) - (a.memory || 0); }
      return 0;
    });

    var head = pfRow('Tab', '', ['Memory', 'Processor', 'Graphics', 'Data'], [], true);
    head.querySelectorAll('.pf-num').forEach(function(cell, i){
      var key = ['memory', 'cpu', null, 'data'][i];
      if (!key) { return; }
      cell.setAttribute('data-sort', key);
      /* M4.8: the column that is sorting says so with an arrow, as a table should, instead of
         the heading spelling it out in words beside "Tab". */
      cell.setAttribute('data-on', order === key ? 'true' : 'false');
      cell.title = 'Sort by this';
      cell.addEventListener('click', function(){
        PF_SORT = (PF_SORT === key) ? 'none' : key;
        if (PF_LAST) { window.__aviaryPerf(PF_LAST); }
      });
    });
    tabs.appendChild(head);
    var states = {active:'On screen', background:'In the background', saver:'In the background · memory saver', asleep:'Asleep — wakes when you click it'};
    var tMaxMem = pfMax(list, 'memory'), tMaxCpu = pfMax(list, 'cpu'), tMaxGpu = pfMax(list, 'gpu');
    list.forEach(function(tb){
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
        [tb.memory == null ? '—' : {v: pfSizeText(tb.memory), heat: pfHeat(tb.memory, tMaxMem, 20)},
         tb.cpu == null ? '—' : {v: pfPctText(tb.cpu), heat: pfHeat(tb.cpu, tMaxCpu, 0.5)},
         tb.gpu == null ? '—' : {v: pfPctText(tb.gpu), heat: pfHeat(tb.gpu, tMaxGpu, 0.5)},
         tb.data ? stSize(tb.data) : '—'],
        buttons));
    });
  }

  /* ── M4.8: Aviary's processes, the way Task Manager lists them ─────────────
     Every part on its own line with its own figures in their own columns, sortable by any of
     them, the tabs a page process draws nested underneath it, and a total at the foot. Before
     this the PID, the handles and the graphics memory were prose in a subtitle, which is the
     one thing you cannot scan down. */
  var procs = document.getElementById('pf-procs');
  if (procs) {
    procs.textContent = '';

    var keys = [null, 'memory', 'cpu', 'gpu', 'gpuMemory', 'handles'];
    var head = pfRow('Part of Aviary', '', ['Process', 'Memory', 'Processor', 'Graphics',
                                            {v:'GPU mem', opt:true}, {v:'Handles', opt:true}], [], true);
    head.querySelectorAll('.pf-num').forEach(function(cell, i){
      var key = keys[i];
      if (!key) { return; }
      cell.setAttribute('data-sort', key);
      cell.setAttribute('data-on', PF_PSORT === key ? 'true' : 'false');
      cell.title = 'Sort by this';
      cell.addEventListener('click', function(){
        PF_PSORT = (PF_PSORT === key) ? 'none' : key;
        if (PF_LAST) { window.__aviaryPerf(PF_LAST); }
      });
    });
    procs.appendChild(head);

    /* Sorting keeps Aviary's own order when nothing is chosen — the host already sends the
       parts in the order they matter (Aviary, engine, graphics, helpers, then pages). */
    var plist = d.processes.slice();
    if (PF_PSORT !== 'none') {
      plist.sort(function(a, b){ return (b[PF_PSORT] || 0) - (a[PF_PSORT] || 0); });
    }

    /* Which tabs each page process is drawing, so they can hang under it. */
    var kidsOf = {};
    d.tabs.forEach(function(tb){
      if (tb.pid == null) { return; }
      (kidsOf[tb.pid] = kidsOf[tb.pid] || []).push(tb);
    });

    var pMaxMem = pfMax(plist, 'memory'), pMaxCpu = pfMax(plist, 'cpu'), pMaxGpu = pfMax(plist, 'gpu');
    var totMem = 0, totCpu = 0, totGpu = 0;
    plist.forEach(function(p){
      totMem += p.memory || 0; totCpu += p.cpu || 0; totGpu += p.gpu || 0;
      var kids = kidsOf[p.pid] || [];
      var sub = kids.length
        ? (kids.length === 1 ? 'draws 1 tab' : 'draws ' + kids.length + ' tabs')
        : '';
      procs.appendChild(pfRow(p.kind, sub, [
        String(p.pid),
        {v: pfSizeText(p.memory), heat: pfHeat(p.memory, pMaxMem, 20)},
        {v: pfPctText(p.cpu),    heat: pfHeat(p.cpu, pMaxCpu, 0.5)},
        {v: pfPctText(p.gpu),    heat: pfHeat(p.gpu, pMaxGpu, 0.5)},
        {v: p.gpuMemory ? pfSizeText(p.gpuMemory) : '—', opt:true},
        {v: String(p.handles), opt:true}
      ], []));

      kids.forEach(function(tb){
        var kid = pfRow(tb.isPrivate ? 'Private tab' : (tb.title || 'Untitled'), '',
          ['', '', '', '', {v:'', opt:true}, {v:'', opt:true}], []);
        kid.className += ' pf-kid';
        procs.appendChild(kid);
      });
    });

    var tot = pfRow('All of Aviary', plist.length + (plist.length === 1 ? ' process' : ' processes'),
      ['', pfSizeText(totMem), pfPctText(totCpu), pfPctText(totGpu), {v:'', opt:true}, {v:'', opt:true}], []);
    tot.className += ' pf-tot';
    procs.appendChild(tot);
  }

  var every = d.everySeconds || 1;
  var foot = document.getElementById('pf-foot');
  if (foot) {
    foot.textContent = 'Measured ' + (every === 1 ? 'every second' : 'every ' + every + ' seconds') +
      ', which takes Aviary ' + d.samplerMs.toFixed(1) + ' ms each time, and stops the moment you leave this page. ' +
      'Memory is each part’s private working set and processor is its share of the whole processor — the figures Task Manager shows. ' +
      'Data is what the sites said they were sending, counted only while this page is open; anything streamed without a length is not counted.';
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
