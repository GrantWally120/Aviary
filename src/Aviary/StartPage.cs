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
/// dictionary, so this page follows Classic/Prism 2 and light/dark exactly like the
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
<!--__AVIARY_CSP__-->
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

  /* The background. Fixed, behind everything, pointer-transparent (M4.9: it used to be only
     the glasshouse, which is still the default and still drawn rather than photographed). */
  #sky{position:fixed;inset:0;z-index:0;pointer-events:none;opacity:.85;}
  #sky svg{width:100%;height:100%;display:block;}

  /* The drawn choices are gradients of the theme's OWN tokens, so each one follows
     Classic/Prism 2 and light/dark without a second copy of anything. */
  #sky[data-bg="dawn"]{background:
    radial-gradient(120% 80% at 18% 108%, color-mix(in srgb,var(--accent) 16%,transparent), transparent 62%),
    radial-gradient(90% 70% at 88% 4%, color-mix(in srgb,var(--accent) 7%,transparent), transparent 58%);}
  #sky[data-bg="dusk"]{background:
    linear-gradient(200deg, color-mix(in srgb,var(--accent) 11%,transparent) 0%, transparent 46%),
    radial-gradient(110% 90% at 82% 104%, color-mix(in srgb,var(--ink) 9%,transparent), transparent 60%);}
  #sky[data-bg="moss"]{background:
    radial-gradient(130% 95% at 50% 106%, color-mix(in srgb,var(--accent) 9%,transparent), transparent 64%),
    repeating-linear-gradient(64deg, var(--rib) 0 1px, transparent 1px 34px);}

  /* The person's own picture. Its own layer so it can fade in when it arrives and never
     flash a half-drawn image over the content. */
  #pic{
    position:fixed;inset:0;z-index:0;pointer-events:none;
    background-position:center;background-size:cover;background-repeat:no-repeat;
    opacity:0;transition:opacity 400ms cubic-bezier(.25,.1,.25,1);
  }
  #pic[data-on="1"]{opacity:1;}
  /* Over a photograph the cards need to hold their own, and the dome would only fight it. */
  body[data-bg="picture"] #sky{display:none;}
  body[data-bg="picture"] .card, body[data-bg="picture"] .tile__well{backdrop-filter:blur(18px) saturate(160%);}
  [data-motion="off"] #pic{transition:none;}

  main{position:relative;z-index:1;width:min(920px,100% - 80px);margin:0 auto;padding:64px 0 120px;}

  .mark{display:flex;align-items:center;gap:11px;margin-bottom:38px;}
  .mark svg{width:26px;height:26px;}
  .mark b{font-size:17px;font-weight:600;letter-spacing:-.018em;}

  /* ── The search bar (Beta Prep 2) ─────────────────────────────────────────
     Grant, 21 Nov 2026, after Brave's new tab page: a bar in the middle of the page to search from
     without going up to the address bar, and whatever you type on this page lands in it. With it
     on, the mark is centred over it and the page starts a little lower, so the bar sits in the
     upper middle of the window the way Brave's does. The address bar is still there for addresses. */
  body[data-find="on"] main{padding-top:max(64px,11vh);}
  body[data-find="on"] .mark{justify-content:center;margin-bottom:22px;}
  body[data-find="on"] .mark svg{width:34px;height:34px;}
  body[data-find="on"] .mark b{font-size:24px;}
  .find{position:relative;max-width:640px;margin:0 auto 40px;}
  .find[hidden]{display:none;}
  .find__box{
    display:flex;align-items:center;gap:10px;height:50px;padding:0 8px 0 17px;border-radius:25px;
    border:1px solid var(--line);background:var(--card);
    box-shadow:0 1px 2px rgba(0,0,0,.05),0 10px 28px rgba(0,0,0,.07);
    backdrop-filter:blur(18px) saturate(160%);
    transition:border-color 150ms cubic-bezier(.25,.1,.25,1),box-shadow 150ms cubic-bezier(.25,.1,.25,1);
  }
  .find__box:focus-within{
    border-color:color-mix(in srgb,var(--accent) 62%,var(--line));
    box-shadow:0 0 0 3px color-mix(in srgb,var(--accent) 18%,transparent),0 10px 28px rgba(0,0,0,.09);
  }
  .find__box > svg{flex:0 0 auto;width:18px;height:18px;color:var(--ink-2);}
  .find__in{
    flex:1;min-width:0;border:0;outline:0;background:transparent;color:var(--ink);
    font:400 15.5px/1.2 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;letter-spacing:-.006em;
    -webkit-user-select:text;user-select:text;
  }
  .find__in::placeholder{color:var(--ink-3);}
  .find__eng{
    flex:0 0 auto;border:0;background:var(--well);color:var(--ink-2);font:500 12px/1 inherit;
    padding:9px 12px;border-radius:99px;cursor:default;white-space:nowrap;
    transition:background-color 140ms,color 140ms;
  }
  .find__eng:hover{background:var(--well-hover);color:var(--ink);}
  .find__list{
    position:absolute;left:0;right:0;top:56px;z-index:4;padding:6px;
    border:1px solid var(--line);border-radius:16px;background:var(--bg);
    box-shadow:0 2px 6px rgba(0,0,0,.1),0 18px 48px rgba(0,0,0,.22);
    animation:pop 160ms cubic-bezier(.32,.72,0,1);transform-origin:top center;
  }
  .find__list[hidden]{display:none;}
  .find__opt{
    display:flex;align-items:center;gap:11px;width:100%;height:36px;padding:0 11px;border:0;border-radius:9px;
    background:none;font:inherit;color:inherit;text-align:left;cursor:default;
  }
  .find__opt:hover,.find__opt[aria-selected="true"]{background:var(--well);}
  .find__opt svg{flex:0 0 auto;width:15px;height:15px;color:var(--ink-3);}
  .find__opt b{flex:1;min-width:0;font-weight:500;font-size:13.5px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
  .find__opt span{flex:0 0 auto;max-width:42%;font-size:12px;color:var(--ink-3);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
  .find__hint{margin:9px 4px 0;text-align:center;font-size:11.5px;color:var(--ink-3);}

  /* The one-time note after "I agree", and whenever the legal pages change (Beta Prep 2). */
  .lnote{
    display:flex;gap:14px;align-items:center;border:1px solid var(--line);border-radius:14px;background:var(--card);
    padding:14px 18px;margin:0 0 30px;
  }
  .lnote div{flex:1;min-width:0;}
  .lnote b{display:block;font-size:14px;font-weight:600;margin-bottom:2px;}
  .lnote span{display:block;font-size:12.5px;color:var(--ink-2);line-height:1.5;}
  .lnote button{
    flex:0 0 auto;border:1px solid var(--line);background:var(--well);color:var(--ink);font:500 12.5px/1 inherit;
    padding:9px 13px;border-radius:8px;cursor:default;
  }
  .lnote button:hover{background:var(--well-hover);}
  .lnote button[data-legal="seen"]{background:var(--accent);border-color:transparent;color:#fff;}

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

  /* ── Bird News (M4.9) ─────────────────────────────────────────────────────
     Columns by where the news is from; cards in the Reading List's own shape so the page reads
     as one design. The publisher's name leads each card, because whose words these are matters
     more than anything Aviary could add. */
  .nw__meta{font-size:12px;color:var(--ink-3);margin:-6px 0 12px;}
  .nw__meta b{font-weight:500;color:var(--ink-2);}
  .nw__cols{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:18px;}
  .nw__col h3{margin:0 0 8px;font-size:11px;font-weight:600;letter-spacing:.06em;text-transform:uppercase;color:var(--ink-2);}
  .nw__list{display:flex;flex-direction:column;gap:8px;}
  .nw__item{
    border:1px solid var(--line);border-radius:12px;background:var(--card);
    padding:10px 12px;display:flex;flex-direction:column;gap:4px;cursor:default;
    text-align:left;font:inherit;color:inherit;
    transition:transform 150ms cubic-bezier(.32,.72,0,1),box-shadow 150ms;
  }
  .nw__item:hover{transform:translateY(-1px);box-shadow:0 4px 14px rgba(0,0,0,.1);}
  .nw__src{display:flex;gap:8px;font-size:11.5px;}
  .nw__src b{font-weight:600;color:var(--accent);}
  .nw__src span{color:var(--ink-3);}
  .nw__title{font-size:13.5px;font-weight:500;line-height:1.35;
    display:-webkit-box;-webkit-line-clamp:3;-webkit-box-orient:vertical;overflow:hidden;}
  .nw__all{margin-top:12px;border:0;background:transparent;color:var(--accent);font:inherit;font-size:13px;cursor:default;padding:4px 0;}
  .nw__all:hover{text-decoration:underline;}
  .nw__down{color:var(--ink-3);font-size:12px;margin-top:10px;}
  /* Beta Prep 2 — a story you have already opened is drawn quieter (it is in your history). */
  .nw__item.is-seen .nw__title{color:var(--ink-2);font-weight:400;}
  .nw__item.is-seen .nw__src b{color:var(--ink-3);}

  /* ── The Bird News page (Beta Prep 2) ─────────────────────────────────────
     Its own page now, not the Start Page's section stretched: tabs by place and topic, a filter,
     a chip for each publisher, the newest story given room, and the rest grouped by when. Every
     headline and summary is still the publisher's own words. */
  .nwp__head{display:flex;align-items:flex-end;gap:16px;margin-bottom:18px;}
  .nwp__head > div{flex:1;min-width:0;}
  .nwp__h{margin:0 0 4px;font-size:28px;font-weight:600;letter-spacing:-.022em;color:var(--ink);}
  .nwp__head .nw__meta{margin:0;}
  .nwp__check{
    flex:0 0 auto;border:1px solid var(--line);background:var(--card);color:var(--ink);
    font:500 12.5px/1 inherit;padding:9px 13px;border-radius:8px;cursor:default;transition:background-color 140ms;
  }
  .nwp__check:hover{background:var(--well-hover);}
  .nwp__check:disabled{opacity:.55;}
  .nwp__bar{display:flex;align-items:center;gap:12px;margin-bottom:10px;flex-wrap:wrap;}
  .nwp__tabs{display:flex;gap:2px;padding:3px;border-radius:10px;background:var(--well);flex-wrap:wrap;}
  .nwp__tabs button{
    border:0;background:transparent;color:var(--ink-2);font:500 12.5px/1 inherit;cursor:default;
    padding:7px 11px;border-radius:7px;transition:background-color 140ms,color 140ms;
  }
  .nwp__tabs button[aria-selected="true"]{background:var(--card);color:var(--ink);box-shadow:0 1px 2px rgba(0,0,0,.12);}
  .nwp__tabs button small{margin-left:5px;color:var(--ink-3);font-size:11px;font-variant-numeric:tabular-nums;}
  .nwp__filter{
    flex:1;min-width:180px;height:32px;border:1px solid var(--line);border-radius:8px;background:var(--card);
    color:var(--ink);font:400 13px/1 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;padding:0 11px;outline:0;
    -webkit-user-select:text;user-select:text;
  }
  .nwp__filter::placeholder{color:var(--ink-3);}
  .nwp__filter:focus{border-color:var(--accent);}
  .nwp__chips{display:flex;gap:6px;flex-wrap:wrap;margin-bottom:6px;}
  .nwp__chips button{
    border:1px solid var(--line);background:transparent;color:var(--ink-3);font:500 11.5px/1 inherit;
    padding:6px 10px;border-radius:99px;cursor:default;transition:background-color 140ms,color 140ms,border-color 140ms;
  }
  .nwp__chips button[aria-pressed="true"]{
    background:color-mix(in srgb,var(--accent) 13%,transparent);
    border-color:color-mix(in srgb,var(--accent) 38%,var(--line));color:var(--ink);
  }
  .nwp__group{margin:22px 0 10px;font-size:11px;font-weight:600;letter-spacing:.06em;text-transform:uppercase;color:var(--ink-2);}
  .nwp__list{display:grid;grid-template-columns:repeat(auto-fill,minmax(330px,1fr));gap:10px;}
  .nws{position:relative;display:flex;min-width:0;}
  .nws .nw__item{flex:1;min-width:0;padding-right:44px;}
  .nws__sum{
    font-size:12.5px;line-height:1.45;color:var(--ink-2);
    display:-webkit-box;-webkit-line-clamp:3;-webkit-box-orient:vertical;overflow:hidden;
  }
  .nws__save{
    position:absolute;right:8px;top:8px;width:28px;height:28px;border-radius:8px;border:0;background:transparent;
    color:var(--ink-3);display:grid;place-items:center;cursor:default;transition:background-color 140ms,color 140ms;
  }
  .nws__save:hover{background:var(--well-hover);color:var(--ink);}
  .nws__save[aria-pressed="true"]{color:var(--accent);}
  .nws__save svg{width:15px;height:15px;}
  .nws--lead{grid-column:1 / -1;}
  .nws--lead .nw__item{padding:16px 52px 16px 18px;gap:7px;}
  .nws--lead .nw__title{font-size:20px;line-height:1.28;font-weight:600;-webkit-line-clamp:2;}
  .nws--lead .nws__sum{font-size:13.5px;}
  .nwp__none{color:var(--ink-3);font-size:13px;padding:18px 0;}

  /* A live update replaces the section in place and must not replay its arrival. */
  #panes > section[data-live]{animation:none;}

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
  /* M4.9 - the background choices, under the sections they share the popover with. */
  .cust__head{
    font-size:11px;font-weight:600;color:var(--ink-3);padding:9px 9px 4px;
    margin-top:5px;border-top:1px solid var(--line);
  }
  .cust__swatch{
    width:15px;height:15px;flex:0 0 auto;border-radius:4px;border:1px solid var(--line);
  }
  .cust__row[aria-checked="true"]{color:var(--accent);}
  .cust__note{font-size:11.5px;color:var(--ink-2);padding:2px 9px 7px;line-height:1.45;}

  /* ── Motion (Alpha Build 5 M4.5) — <html data-motion>: flight (Smooth Flight, Classic's),
     fluid (Fluid, Prism 2's spring) or off (Reduce motion). Set from the page's data and
     re-set by the host whenever the style changes (Motion.cs). */
  /* --m-x is Animation speed (M4.6) - see InternalPages for the same three lines. */
  :root{--m-ease:cubic-bezier(.32,.72,0,1);--m-x:1;--m-dur:calc(380ms * var(--m-x));--m-rise:10px;}
  html[data-speed="calm"]{--m-x:1.5;}
  html[data-speed="quick"]{--m-x:.68;}
  html[data-speed="quik"]{--m-x:.35;}
  html[data-motion="fluid"]{--m-ease:linear(0, .006, .025 2.8%, .101 6.1%, .539 18.9%, .721 25.3%, .849 31.5%, .937 38.1%, .968 41.8%, .991 45.7%, 1.006 50.1%, 1.015 55%, 1.017 63.9%, 1.001);--m-dur:calc(560ms * var(--m-x));--m-rise:18px;}
  html[data-motion="off"] *,html[data-motion="off"] *::before,html[data-motion="off"] *::after{transition:none!important;animation:none!important;}

  /* "Show keyboard focus clearly" (M4.5): the same ring as Settings’ own pages, and only
     for :focus-visible, so a click on a tile never draws it. */
  html[data-focus="clear"] :focus-visible{outline:3px solid var(--accent)!important;outline-offset:3px!important;}
  /* The sections arrive one after another. backwards: nothing is left behind once done. */
  #panes > section{animation:sec-in var(--m-dur) var(--m-ease) backwards;animation-delay:calc(var(--i,0) * 60ms + 40ms);}
  @keyframes sec-in{from{opacity:0;transform:translateY(var(--m-rise));}to{opacity:1;transform:none;}}
  .chart i{transition-timing-function:var(--m-ease);transition-duration:calc(var(--m-dur) + 180ms);}
  /* Fluid: tiles lift a little further on a spring, and squish when pressed. */
  html[data-motion="fluid"] .tile__well{transition-timing-function:var(--m-ease);transition-duration:420ms;}
  html[data-motion="fluid"] .tile:hover .tile__well{transform:translateY(-3px) scale(1.04);}
  html[data-motion="fluid"] .tile:active .tile__well{transform:scale(.92);}
  html[data-motion="fluid"] .cust__pop{animation:pop-fluid 380ms var(--m-ease);}
  @keyframes pop-fluid{from{opacity:0;transform:scale(.86) translateY(8px);}to{opacity:1;transform:none;}}
  @media (prefers-reduced-motion:reduce){
    *,*::before,*::after{transition-duration:1ms!important;animation-duration:1ms!important;}
  }
</style>
</head>
<body>
<div id="sky" aria-hidden="true"></div>
<div id="pic" aria-hidden="true"></div>

<main>
  <div class="mark">
    <svg viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <path d="M3 20V12.6A9 9 0 0 1 21 12.6V20" stroke="var(--accent)" stroke-width="1.5" stroke-linecap="round"/>
      <path d="M12 3.6V20M7.6 5.1V20M16.4 5.1V20M3 14.4h18" stroke="var(--accent)" stroke-width=".9" stroke-linecap="round" opacity=".5"/>
      <path d="M2.2 20h19.6" stroke="var(--accent)" stroke-width="1.5" stroke-linecap="round"/>
    </svg>
    <b>Aviary</b>
  </div>
  <div class="find" id="find" hidden>
    <div class="find__box">
      <svg viewBox="0 0 18 18" fill="none" aria-hidden="true"><circle cx="8" cy="8" r="5.2" stroke="currentColor" stroke-width="1.6"/><path d="m12 12 3.6 3.6" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg>
      <input class="find__in" id="findIn" type="text" autocomplete="off" autocapitalize="off" spellcheck="false"
             role="combobox" aria-expanded="false" aria-controls="findList" aria-autocomplete="list" aria-label="Search the web">
      <button class="find__eng" id="findEng" title="Change the search engine"></button>
    </div>
    <div class="find__list" id="findList" role="listbox" hidden></div>
    <div class="find__hint" id="findHint"></div>
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

<script nonce="__AVIARY_NONCE__">
/*__AVIARY_START__*/
var D = (typeof window.__aviaryStart === 'object' && window.__aviaryStart) || {};
/* How this page moves (Motion.cs, M4.5): the host re-sets it whenever the style changes. */
document.documentElement.dataset.motion = D.motion || 'flight';
document.documentElement.dataset.speed = D.speed || 'standard';
/* Show keyboard focus clearly (M4.5), the same switch as Settings' own pages. */
if (D.focusRing) { document.documentElement.dataset.focus = 'clear'; }
var ICONS = D.iconBase || '';
var SHOW = D.sections || {search:true, favourites:true, news:true, menagerie:true, frequent:true, reading:true, privacy:true};
/* Opened to show one section (the Shields popover's "Privacy Report"): shown on this page even
   if Customise hides it — the saved choice is untouched — and scrolled to below. */
if (D.focus && (D.focus in SHOW)) { SHOW[D.focus] = true; }

/* Every message carries this page's secret, which the host checks: the host's handler stays
   attached after the tab moves on to a website, and only Aviary's own page knows the token. */
function post(o){ try { o.token = D.token || ''; window.chrome.webview.postMessage(JSON.stringify(o)); } catch(e){} }
function esc(s){ return String(s == null ? '' : s).replace(/[&<>"']/g,
  function(c){ return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]; }); }

/* ── the glasshouse ─────────────────────────────────────────────────────────
   Generated rather than shipped. A barrel vault seen from inside: ribs
   converging on a vanishing point, glazing bars crossing them, all in one
   near-invisible ink so it reads as texture and never competes with content. */
/* M4.9 - which background. "glasshouse" draws the dome below; "plain" draws nothing at all;
   dawn/dusk/moss are CSS above; "picture" points the #pic layer at the profile's own copy,
   and falls back to the dome if the host had nothing to point it at. */
var BG = String(D.background || 'Glasshouse').toLowerCase();
if (BG === 'your picture' && !D.backgroundUrl) { BG = 'glasshouse'; }
if (BG === 'your picture') { BG = 'picture'; }
document.body.dataset.bg = BG;
document.getElementById('sky').dataset.bg = BG;
if (BG === 'picture') {
  var pic = document.getElementById('pic');
  var probe = new Image();
  probe.onload = function(){ pic.style.backgroundImage = 'url("' + D.backgroundUrl + '")'; pic.dataset.on = '1'; };
  probe.onerror = function(){ document.body.dataset.bg = 'glasshouse'; document.getElementById('sky').dataset.bg = 'glasshouse'; sky(); };
  probe.src = D.backgroundUrl;
}

function sky(){
  if (document.getElementById('sky').dataset.bg !== 'glasshouse') { document.getElementById('sky').innerHTML = ''; return; }
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
}
sky();

/* ── tiles ─────────────────────────────────────────────────────────────── */
function tile(item){
  var art = item.hasIcon
    ? '<img src="' + ICONS + encodeURIComponent(item.key) + '" alt="" loading="lazy" ' +
      'data-mono="' + esc(item.mono) + '" data-hue="' + (item.hue | 0) + '">'
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
/* A picture that fails to load falls back to its monogram (data-mono) or is dropped (data-drop).
   One listener instead of an onerror attribute on each <img>: this page's policy allows no inline
   handlers (PageCsp.cs). `error` does not bubble, so it is caught on the way down. */
document.addEventListener('error', function(e){
  var t = e.target;
  if (!t || t.tagName !== 'IMG') { return; }
  if (t.hasAttribute('data-mono')) { t.replaceWith(window.mono(t.getAttribute('data-mono'), Number(t.getAttribute('data-hue')))); }
  else if (t.hasAttribute('data-drop')) { t.remove(); }
}, true);

function section(id, heading, body){
  if (!SHOW[id]) { return ''; }
  return '<section data-sec="' + id + '"><h2>' + esc(heading) + '</h2>' + body + '</section>';
}

/* ── Bird News (M4.9) ───────────────────────────────────────────────────────
   Everything here came off the internet: every string goes through esc(), and every address was
   checked to be http or https by the host before it got here. */
var NEWS_FULL = D.focus === 'news';
if (NEWS_FULL) { document.title = 'Bird News'; }

function ago(ms){
  if (!ms) { return ''; }
  var s = Math.max(0, (Date.now() - ms) / 1000);
  if (s < 90) { return 'just now'; }
  if (s < 3600) { return Math.round(s / 60) + ' min ago'; }
  if (s < 86400) { var h = Math.round(s / 3600); return h + (h === 1 ? ' hour ago' : ' hours ago'); }
  var d = Math.round(s / 86400); return d + (d === 1 ? ' day ago' : ' days ago');
}

/* The Start Page's few slots are dealt round the publishers — each one's newest, then each one's
   next — and then shown newest first. Strictly newest-first let one busy desk take every slot:
   on the first real fetch (21 Sep) the World column was four stories from The Guardian while BBC
   and Al Jazeera had news an hour old. The Bird News page keeps the plain timeline. */
function mixSources(items, per){
  var bySource = {}, order = [];
  items.forEach(function(s){
    if (!bySource[s.source]) { bySource[s.source] = []; order.push(s.source); }
    bySource[s.source].push(s);
  });
  var picked = [];
  for (var round = 0; picked.length < per; round++) {
    var any = false;
    for (var i = 0; i < order.length && picked.length < per; i++) {
      var s = bySource[order[i]][round];
      if (s) { picked.push(s); any = true; }
    }
    if (!any) { break; }
  }
  return picked.sort(function(a, b){ return (b.at || 0) - (a.at || 0); });
}

/* Where a story can sit: by place, by topic (Beta Prep 2, each off until switched on), your own feeds. */
var NW_TABS = [['all', 'All'], ['world', 'The World'], ['ph', 'The Philippines'], ['tech', 'Technology'],
               ['science', 'Science'], ['sport', 'Sport'], ['more', 'Your feeds']];
/* The Bird News page's view: which tab, the filter's words, the publishers switched out of view. */
var NW = {tab:'all', q:'', off:{}};

function newsNewest(a, b){ return (b.at || 0) - (a.at || 0); }
/* One story once: a publisher's world desk and its technology desk often carry the same piece. */
function newsOnce(list){
  var had = {};
  return list.filter(function(s){ if (had[s.url]) { return false; } had[s.url] = true; return true; });
}
function newsOf(n, key){
  if (key !== 'all') { return (n[key] || []).slice(); }
  var all = [];
  NW_TABS.slice(1).forEach(function(t){ all = all.concat(n[t[0]] || []); });
  return newsOnce(all.sort(newsNewest));
}

function newsMeta(n){
  var meta = n.checkedAt ? 'Updated <b>' + esc(ago(n.checkedAt)) + '</b>' : '';
  if (n.every) { meta += (meta ? ' · ' : '') + 'checks every ' + (n.every >= 60 ? 'hour' : n.every + ' minutes'); }
  return meta + (meta ? ' · ' : '') + 'headlines exactly as each site wrote them';
}

function newsDown(n){
  var down = (n.feeds || []).filter(function(f){ return f.state === 'failed'; });
  if (!down.length) { return ''; }
  return '<div class="nw__down">Not updating right now — ' +
         down.map(function(f){ return esc(f.name) + ': ' + esc(f.why || 'no answer'); }).join(' · ') +
         '. Anything already here stays until ' + (down.length === 1 ? 'it answers' : 'they answer') + '.</div>';
}

/* One story. The Start Page's is the headline alone; the Bird News page adds the publisher's own
   summary and a + for the Reading List. A story already in your history is drawn quieter. */
function storyHtml(s, n, full, lead){
  var seen = s.seen && n.dimRead !== false ? ' is-seen' : '';
  var card = '<button class="nw__item' + seen + '" data-url="' + esc(s.url) + '" title="' + esc(s.title) + '">' +
               '<span class="nw__src"><b>' + esc(s.source) + '</b><span>' + esc(ago(s.at)) + '</span></span>' +
               '<span class="nw__title">' + esc(s.title) + '</span>' +
               (full && s.summary && n.summaries !== false ? '<span class="nws__sum">' + esc(s.summary) + '</span>' : '') +
             '</button>';
  if (!full) { return card; }
  return '<div class="nws' + (lead ? ' nws--lead' : '') + '">' + card +
           '<button class="nws__save" data-news-save="' + esc(s.url) + '" aria-pressed="' + (s.saved ? 'true' : 'false') + '" title="' +
             (s.saved ? 'On your Reading List — press to take it off' : 'Add to your Reading List') + '">' +
             '<svg viewBox="0 0 16 16" fill="none" aria-hidden="true"><path d="M4 2.4h8v11.4l-4-2.9-4 2.9Z" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"' +
             (s.saved ? ' fill="currentColor"' : '') + '/></svg></button>' +
         '</div>';
}

/* ── the Start Page's section ── */
function newsHtml(){
  var n = D.news;
  if (!n || !n.on) { return ''; }
  var per = 4;
  /* Topics and your own feeds share the third column here; the Bird News page gives each its own tab. */
  var extra = newsOnce([].concat(n.tech || [], n.science || [], n.sport || [], n.more || []).sort(newsNewest));
  var cols = [['The World', n.world || []], ['The Philippines', n.ph || []], ['More for you', extra]];
  var feeds = n.feeds || [];
  var total = 0;

  var body = cols.map(function(c){
    var items = mixSources(c[1], per);
    total += items.length;
    if (!items.length) { return ''; }
    return '<div class="nw__col"><h3>' + esc(c[0]) + '</h3><div class="nw__list">' +
      items.map(function(s){ return storyHtml(s, n, false, false); }).join('') + '</div></div>';
  }).join('');

  var failed = feeds.some(function(f){ return f.state === 'failed'; });
  var waiting = feeds.some(function(f){ return f.state === 'never'; });

  if (!total) {
    body = '<p class="empty">' + (waiting && !failed
      ? 'Fetching the latest stories…'
      : feeds.length
        ? 'Bird News couldn’t reach any news sites just now. It will try again on its own.'
        : 'Every feed is switched off. Settings → Bird News turns them on.') + '</p>';
  } else {
    body = '<div class="nw__cols">' + body + '</div>';
  }

  var foot = newsDown(n);
  if (total) { foot += '<button class="nw__all" data-news-all="1">All the stories in Bird News &rarr;</button>'; }

  return section('news', 'Bird News', '<div class="nw__meta">' + newsMeta(n) + '</div>' + body + foot);
}

/* ── the Bird News page ── */
function newsWhen(at){
  if (!at) { return 'Undated'; }
  var d = new Date(at), now = new Date();
  if (now.getTime() - at < 3600000) { return 'In the last hour'; }
  if (d.toDateString() === now.toDateString()) { return 'Earlier today'; }
  if (d.toDateString() === new Date(now.getTime() - 86400000).toDateString()) { return 'Yesterday'; }
  return 'Before that';
}

function newsFullShell(){
  return '<section data-sec="news" class="nwp" data-live="1">' +
    '<div class="nwp__head"><div><h1 class="nwp__h">Bird News</h1><div class="nw__meta" id="nwMeta"></div></div>' +
      '<button class="nwp__check" id="nwCheck" data-news-check="1">Check now</button></div>' +
    '<div class="nwp__bar"><div class="nwp__tabs" id="nwTabs" role="tablist"></div>' +
      '<input class="nwp__filter" id="nwFilter" type="text" autocomplete="off" spellcheck="false" ' +
        'placeholder="Filter headlines" aria-label="Filter headlines"></div>' +
    '<div class="nwp__chips" id="nwChips"></div>' +
    '<div id="nwBody"></div><div id="nwFoot"></div>' +
  '</section>';
}

function newsFullBody(n){
  var words = NW.q.toLowerCase().split(/\s+/).filter(Boolean);
  var list = newsOf(n, NW.tab).filter(function(s){
    if (NW.off[s.source]) { return false; }
    if (!words.length) { return true; }
    var hay = (s.title + ' ' + s.source + ' ' + (s.summary || '')).toLowerCase();
    return words.every(function(w){ return hay.indexOf(w) >= 0; });
  }).slice(0, 150);

  if (!list.length) {
    var feeds = n.feeds || [];
    return '<p class="nwp__none">' + (words.length || Object.keys(NW.off).length
      ? 'No headline here matches. Clear the filter, or switch a publisher back on above.'
      : feeds.some(function(f){ return f.state === 'never'; }) ? 'Fetching the latest stories…'
      : feeds.length ? 'Nothing here yet. Bird News will try again on its own.'
      : 'Every feed is switched off. Settings → Bird News turns them on.') + '</p>';
  }

  var out = '', group = null, open = false;
  list.forEach(function(s, i){
    var lead = i === 0 && !words.length;
    var when = newsWhen(s.at);
    if (when !== group) {
      if (open) { out += '</div>'; }
      out += '<div class="nwp__group">' + esc(when) + '</div><div class="nwp__list">';
      group = when; open = true;
    }
    out += storyHtml(s, n, true, lead);
  });
  return out + (open ? '</div>' : '');
}

/* Draws, or redraws in place, everything but the filter box - which keeps its words and its caret. */
function newsFullDraw(){
  var n = D.news, panes = document.getElementById('panes');
  if (!n || !n.on) {
    panes.innerHTML = '<p class="empty">Bird News is switched off. Settings → Bird News turns it on.</p>';
    return;
  }
  if (!document.getElementById('nwBody')) { panes.innerHTML = newsFullShell(); }

  var tabs = NW_TABS.filter(function(t){ return t[0] === 'all' || (n[t[0]] || []).length; });
  if (!tabs.some(function(t){ return t[0] === NW.tab; })) { NW.tab = 'all'; }
  document.getElementById('nwMeta').innerHTML = newsMeta(n);
  document.getElementById('nwTabs').innerHTML = tabs.map(function(t){
    return '<button role="tab" data-news-tab="' + t[0] + '" aria-selected="' + (t[0] === NW.tab ? 'true' : 'false') + '">' +
           esc(t[1]) + '<small>' + newsOf(n, t[0]).length + '</small></button>';
  }).join('');

  /* A chip for every publisher on this tab: pressed is shown, and pressing takes it out of view. */
  var sources = [];
  newsOf(n, NW.tab).forEach(function(s){ if (sources.indexOf(s.source) < 0) { sources.push(s.source); } });
  document.getElementById('nwChips').innerHTML = sources.length < 2 ? '' : sources.map(function(name){
    return '<button data-news-src="' + esc(name) + '" aria-pressed="' + (NW.off[name] ? 'false' : 'true') + '">' + esc(name) + '</button>';
  }).join('');

  document.getElementById('nwBody').innerHTML = newsFullBody(n);
  document.getElementById('nwFoot').innerHTML = newsDown(n);
  var check = document.getElementById('nwCheck');
  if (check) { check.disabled = false; check.textContent = 'Check now'; }
}

/* The host pushes fresh stories in; only this section is redrawn, so a page you are reading
   does not jump or replay its arrival every time the news is checked. */
window.__aviaryNews = function(n){
  D.news = n;
  if (NEWS_FULL) { newsFullDraw(); return; }

  var old = document.querySelector('#panes > section[data-sec="news"]');
  var html = newsHtml();
  if (old) {
    if (!html) { old.remove(); return; }
    var holder = document.createElement('div');
    holder.innerHTML = html;
    var fresh = holder.firstElementChild;
    fresh.setAttribute('data-live', '1');
    old.replaceWith(fresh);
  } else if (html) {
    render();
  }
};

/* The Bird News page's own controls (Beta Prep 2). */
document.addEventListener('click', function(e){
  var tab = e.target.closest('[data-news-tab]');
  if (tab) { e.stopPropagation(); NW.tab = tab.getAttribute('data-news-tab'); NW.off = {}; newsFullDraw(); return; }

  var src = e.target.closest('[data-news-src]');
  if (src) {
    e.stopPropagation();
    var name = src.getAttribute('data-news-src');
    if (NW.off[name]) { delete NW.off[name]; } else { NW.off[name] = true; }
    newsFullDraw();
    return;
  }

  var check = e.target.closest('[data-news-check]');
  if (check) {
    e.stopPropagation();
    check.disabled = true; check.textContent = 'Checking…';
    post({type:'newsCheck'});
    /* The answer redraws the page; if nothing changed there is no answer, so the button wakes by itself. */
    setTimeout(function(){ check.disabled = false; check.textContent = 'Check now'; }, 6000);
    return;
  }

  var save = e.target.closest('[data-news-save]');
  if (save) {
    e.stopPropagation();
    var url = save.getAttribute('data-news-save');
    var now = save.getAttribute('aria-pressed') !== 'true';
    NW_TABS.slice(1).forEach(function(t){
      ((D.news || {})[t[0]] || []).forEach(function(s){ if (s.url === url) { s.saved = now; } });
    });
    post({type:'newsSave', url:url});
    newsFullDraw();
  }
}, true);
document.addEventListener('input', function(e){
  if (e.target && e.target.id === 'nwFilter') {
    NW.q = e.target.value;
    document.getElementById('nwBody').innerHTML = newsFullBody(D.news || {});
  }
});

document.addEventListener('click', function(e){
  if (e.target.closest('[data-news-all]')) { e.stopPropagation(); post({type:'newsAll'}); }
}, true);

/* ── The search bar (Beta Prep 2) ───────────────────────────────────────────
   What is typed here goes nowhere until Enter - except, when "Search suggestions from the engine"
   is on, to the chosen engine as you type, under exactly the address bar's rules (the host decides;
   this page only asks). Everything the host sends back is shown as text, never as markup. */
var FIND = D.search || {};
var LEGAL_NOTE_DONE = false;
var findIn = document.getElementById('findIn');
var findList = document.getElementById('findList');
var findItems = [], findAt = -1, findAsked = '', findTimer = 0;
var FIND_ICON = {
  search:'<svg viewBox="0 0 16 16" fill="none" aria-hidden="true"><circle cx="7" cy="7" r="4.4" stroke="currentColor" stroke-width="1.5"/><path d="m10.4 10.4 3 3" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>',
  site:'<svg viewBox="0 0 16 16" fill="none" aria-hidden="true"><circle cx="8" cy="8" r="5.6" stroke="currentColor" stroke-width="1.4"/><path d="M2.6 8h10.8M8 2.4c2.6 3.2 2.6 8 0 11.2-2.6-3.2-2.6-8 0-11.2Z" stroke="currentColor" stroke-width="1.2"/></svg>',
  star:'<svg viewBox="0 0 16 16" fill="none" aria-hidden="true"><path d="m8 2.2 1.8 3.7 4 .6-2.9 2.8.7 4L8 11.4l-3.6 1.9.7-4-2.9-2.8 4-.6Z" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/></svg>'
};
function findOn(){ return !!SHOW.search && !NEWS_FULL; }
function findSetup(){
  var on = findOn();
  document.getElementById('find').hidden = !on;
  if (on) { document.body.dataset.find = 'on'; } else { delete document.body.dataset.find; }
  if (!on) { return; }
  var engine = FIND.engine || 'the web';
  findIn.placeholder = (D.isPrivate ? 'Search privately with ' : 'Search with ') + engine;
  document.getElementById('findEng').textContent = engine;
  document.getElementById('findHint').textContent =
    FIND.suggests ? 'Suggestions from ' + engine + ' are on, so what you type here is sent to it as you type.'
                  : 'Nothing you type here is sent anywhere until you press Enter.';
}
function findClose(){
  findItems = []; findAt = -1;
  findList.hidden = true; findList.textContent = '';
  findIn.setAttribute('aria-expanded', 'false');
}
function findDraw(){
  findList.textContent = '';
  if (!findItems.length) { findClose(); return; }
  findItems.forEach(function(it, i){
    var b = document.createElement('button');
    b.className = 'find__opt'; b.setAttribute('role', 'option'); b.setAttribute('data-find', i);
    b.setAttribute('aria-selected', i === findAt ? 'true' : 'false');
    b.innerHTML = FIND_ICON[it.kind === 'search' ? 'search' : it.kind === 'favourite' ? 'star' : 'site'];
    var t = document.createElement('b'); t.textContent = it.title || it.url || ''; b.appendChild(t);
    var s = document.createElement('span'); s.textContent = it.note || ''; b.appendChild(s);
    findList.appendChild(b);
  });
  findList.hidden = false;
  findIn.setAttribute('aria-expanded', 'true');
}
/* The host's answer: for these words, your own matches and (only if switched on) the engine's. */
window.__aviaryFind = function(q, items){
  if (!findOn() || String(q) !== findIn.value.trim()) { return; }
  /* Walking the list with the arrow keys: a late answer must not move the row you are on. */
  if (findAt >= 0) { return; }
  findItems = Array.isArray(items) ? items.slice(0, 9) : [];
  findDraw();
};
/* The host puts the keyboard here when a new tab opens (Settings → Get started). */
window.__aviaryFocusFind = function(){ if (findOn()) { findIn.focus(); } };
function findAsk(){
  var q = findIn.value.trim();
  if (q === findAsked) { return; }
  findAsked = q;
  if (!q) { findClose(); return; }
  post({type:'startSearchType', q:q});
}
function findGo(i){
  var it = findItems[i];
  var q = findIn.value.trim();
  findClose();
  if (it) { post({type:'startSearch', q:q, pick:it.kind, value:it.kind === 'search' ? it.title : it.url, index:i}); }
  else if (q) { post({type:'startSearch', q:q}); }
}
findIn.addEventListener('input', function(){
  findAt = -1;
  clearTimeout(findTimer);
  findTimer = setTimeout(findAsk, 60);
});
findIn.addEventListener('keydown', function(e){
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp'){
    if (!findItems.length) { return; }
    e.preventDefault();
    var n = findItems.length;
    findAt = e.key === 'ArrowDown' ? (findAt + 1 >= n ? -1 : findAt + 1) : (findAt < 0 ? n - 1 : findAt - 1);
    findDraw();
    return;
  }
  if (e.key === 'Enter'){ e.preventDefault(); findGo(findAt); return; }
  if (e.key === 'Escape'){
    e.stopPropagation();
    if (!findList.hidden) { findClose(); } else if (findIn.value) { findIn.value = ''; findAsked = ''; } else { findIn.blur(); }
  }
});
findIn.addEventListener('blur', function(){ setTimeout(function(){ if (document.activeElement !== findIn) { findClose(); } }, 140); });
findList.addEventListener('mousedown', function(e){ e.preventDefault(); });
findList.addEventListener('click', function(e){
  var o = e.target.closest('[data-find]');
  if (o) { e.stopPropagation(); findGo(Number(o.getAttribute('data-find'))); }
});
document.getElementById('findEng').addEventListener('click', function(e){ e.stopPropagation(); post({type:'startSearchEngine'}); });
/* Type anywhere on the page and it lands in the bar: the key that started it is typed there too,
   because focus has moved by the time the browser acts on it. Shortcuts and other fields are left alone. */
document.addEventListener('keydown', function(e){
  if (!findOn() || e.defaultPrevented || e.ctrlKey || e.altKey || e.metaKey) { return; }
  var t = e.target;
  if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) { return; }
  if (e.key.length !== 1 || e.key === ' ') { return; }
  findIn.focus();
});
document.addEventListener('click', function(e){
  var b = e.target.closest('[data-legal]');
  if (!b) { return; }
  e.stopPropagation();
  LEGAL_NOTE_DONE = true;
  post({type:'legalNote', action:b.getAttribute('data-legal')});
  var note = document.getElementById('lnote');
  if (note) { note.remove(); }
}, true);

function render(){
  var out = '';
  findSetup();

  /* The Bird News page is this same page opened on the news alone, every story shown. */
  if (NEWS_FULL) { newsFullDraw(); return; }

  /* Beta Prep 2: said once after "I agree", and once more whenever the legal pages change. */
  if (D.legalNote && !LEGAL_NOTE_DONE){
    var changed = D.legalNote === 'changed';
    out += '<div class="lnote" id="lnote"><div><b>' +
      (changed ? 'Aviary’s legal pages have changed' : 'Thank you. You’re all set') + '</b><span>' +
      (changed
        ? 'The Privacy Policy is now revision ' + esc(D.policyRevision || '') + ' and the Terms of Use revision ' + esc(D.termsRevision || '') + '. Settings → Legal has both, and says what changed.'
        : 'The Terms of Use, the Privacy Policy and the Copyright notice are always in Settings → Legal, and can be read offline.') +
      '</span></div><button data-legal="open">Open Legal</button><button data-legal="seen">OK</button></div>';
  }

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

  /* Bird News sits right under Favourites: the news is what changes between visits. */
  out += newsHtml();

  /* Grant's projects, offline, from Aviary's own folder — absent in a build without them. */
  var mg = D.menagerie;
  if (mg && mg.items && mg.items.length){
    out += section('menagerie', 'The Collection', '<div class="mg">' + mg.items.map(function(it){
      var w = String(it.title).split(/\s+/);
      var initials = it.mono || ((w[0] || '?').charAt(0) + (w[1] ? w[1].charAt(0) : '')).toUpperCase();
      return '<button class="mg__card" data-url="' + esc(it.url) + '" title="' + esc(it.title) + '" style="--c:' + esc(it.accent) + '">' +
               '<span class="mg__win"><span class="mg__mono">' + esc(initials) + '</span>' +
               '<img src="' + esc(it.thumb) + '" alt="" loading="lazy" data-drop="1"></span>' +
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
  /* Each section's place in the order it arrives in (M4.5). */
  document.querySelectorAll('#panes > section').forEach(function(sec, i){ sec.style.setProperty('--i', i); });
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
             '<i style="height:3px" data-h="' + Math.max(3, Math.round((v / max) * 44)) + '"></i>' +
             '<span>' + esc(names[i]) + '</span>' +
           '</div>';
  }).join('');

  /* The bars rise from the baseline to their day's height (M4.5) — drawn at 3px, then given
     their height two frames later, so the height transition runs. Instantly with motion off. */
  var bars = el.querySelectorAll('i[data-h]');
  var rise = function(){ bars.forEach(function(b){ b.style.height = b.getAttribute('data-h') + 'px'; }); };
  if (document.documentElement.dataset.motion === 'off') { rise(); }
  else { requestAnimationFrame(function(){ requestAnimationFrame(rise); }); }
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
var LABELS = {search:'Search bar', favourites:'Favourites', news:'Bird News', menagerie:'The Collection', frequent:'Frequently Visited',
              reading:'Reading List', privacy:'Privacy Report'};
if (!D.menagerie) { delete LABELS.menagerie; }

/* M4.9 - the backgrounds, in the order Settings offers them. The swatch is drawn from the same
   theme tokens the background itself uses, so the two can never disagree. */
var BACKGROUNDS = [
  ['Glasshouse', 'linear-gradient(160deg,var(--sky),transparent)'],
  ['Plain', 'var(--well)'],
  ['Dawn', 'radial-gradient(at 20% 100%,color-mix(in srgb,var(--accent) 45%,transparent),transparent)'],
  ['Dusk', 'linear-gradient(200deg,color-mix(in srgb,var(--accent) 35%,transparent),var(--well))'],
  ['Moss', 'repeating-linear-gradient(64deg,var(--rib) 0 2px,var(--well) 2px 6px)'],
  ['Your picture', 'linear-gradient(135deg,var(--ink-3),var(--well))'],
];

function buildCust(){
  var pop = document.getElementById('custPop');
  var rows = Object.keys(LABELS).map(function(k){
    return '<button class="cust__row" data-sec="' + k + '" role="switch" aria-checked="' +
           (SHOW[k] ? 'true' : 'false') + '">' +
             '<svg class="cust__tick" viewBox="0 0 15 15"><path d="M3 8l3 3 6-7"/></svg>' +
             esc(LABELS[k]) +
           '</button>';
  });
  rows.push('<div class="cust__head">Background</div>');
  if (D.backgroundProblem) { rows.push('<div class="cust__note">' + esc(D.backgroundProblem) + '</div>'); }
  var chosen = String(D.background || 'Glasshouse');
  BACKGROUNDS.forEach(function(b){
    rows.push('<button class="cust__row" data-bg="' + esc(b[0]) + '" role="radio" aria-checked="' +
              (b[0].toLowerCase() === chosen.toLowerCase() ? 'true' : 'false') + '">' +
                '<span class="cust__swatch" style="background:' + b[1] + '"></span>' +
                esc(b[0] === 'Your picture' ? 'Your picture…' : b[0]) +
              '</button>');
  });
  pop.innerHTML = rows.join('');
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

  /* M4.9 - a background. The host saves it and reloads the page: every background is drawn at
     load, and "Your picture…" opens a file picker whose answer only the host can have. */
  var bg = row.getAttribute('data-bg');
  if (bg) {
    [].slice.call(row.parentNode.querySelectorAll('[data-bg]')).forEach(function(r){
      r.setAttribute('aria-checked', r === row ? 'true' : 'false');
    });
    post({type:'startBackground', value:bg});
    return;
  }

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
/* A new tab's typing goes to the search bar when Settings says so; the address bar is one click away. */
if (findOn() && FIND.typing && !D.focus) { findIn.focus(); }
if (D.focus) {
  var focused = document.querySelector('[data-sec="' + D.focus + '"]');
  if (focused) { focused.scrollIntoView({block:'center'}); }
}
</script>
</body>
</html>
""";
}
