using System;

namespace Aviary;

/// <summary>
/// Alpha Build 5 M4.5 — the accessibility switches that change how WEBSITES look, as one small
/// stylesheet Aviary adopts into every document before the page's own scripts run.
///
/// <list type="bullet">
/// <item><b>Underline links</b> (<c>underlineLinks</c>) — a great many sites colour their links
/// and nothing more, which is invisible to anyone who cannot tell the colours apart. This puts
/// the line back.</item>
/// </list>
///
/// It is deliberately tiny and deliberately blunt: one rule, marked important, adopted rather
/// than written into the page, so nothing of the page is edited and it comes off again the
/// moment the switch does. Aviary's own pages are not touched here — they have "Text size" and
/// "Show keyboard focus clearly", which go through the page itself.
///
/// <para><b>Bug 95 (found 25 Sep 2026, M6 stage 3):</b> that last promise depended on timing until
/// the script checked for itself. It is registered for every document in a tab as the tab starts,
/// without waiting, and a tab opened for Settings is already becoming Settings by then — with
/// diagnostics on, the registration won every time (measured on the published M5 build: every
/// Settings tab carried the underline stylesheet; with diagnostics off, none did). Found when M6's
/// "Thin scrollbars on websites" gave Settings thin scroll bars. Now the script leaves anything that
/// is not an http(s) page alone, as the element-hiding script always has.</para>
/// </summary>
internal static class ReadingAids
{
    /// <summary>The CSS for the switches that are on, or empty when none are.</summary>
    public static string Css()
    {
        string css = string.Empty;
        if (AppSettings.GetBool("underlineLinks"))
        {
            // :any-link so a link without href (an anchor target) is left alone.
            css += "a:any-link{text-decoration-line:underline!important;text-decoration-thickness:max(1px,.06em)!important;text-underline-offset:.15em!important;}";
        }

        if (AppSettings.GetBool("exp.thinScrollbars"))
        {
            // M6 stage 3, Settings -> Experimental. Measured 25 Sep: 15 px of every page is its scroll
            // bar; thin is 10. In a LAYER, and not important: an unlayered rule beats a layered one
            // whatever its specificity, so any site that sets its own scrollbar-width or -color keeps
            // it. (A site that only uses ::-webkit-scrollbar loses its styling - the engine ignores
            // those once scrollbar-width is set - which is why the row is labelled "may look wrong".)
            css += "@layer aviary-thin-scrollbars{*{scrollbar-width:thin}}";
        }

        return css;
    }

    /// <summary>
    /// The script that adopts that stylesheet, or takes the last one back off when nothing is
    /// switched on. Runs in every frame; safe to run twice.
    /// </summary>
    public static string Script()
    {
        string css = Css();
        return Template.Replace("__AVIARY_AIDS__", System.Text.Json.JsonSerializer.Serialize(css));
    }

    /// <summary>True when nothing is switched on, so the tab can skip the whole mechanism.</summary>
    public static bool Nothing => Css().Length == 0;

    private const string Template = """
(function () {
  'use strict';
  // Websites only. Aviary's own pages (Settings, the Start Page) are about:blank documents and keep
  // their own look. This must be decided HERE, by the page (as the element-hiding script does), not
  // by when the script was registered: a new tab registers it a moment after it starts to become
  // Settings, and with diagnostics on the registration won that race every time (bug 95).
  if (location.protocol !== 'http:' && location.protocol !== 'https:') { return; }
  var KEY = '__aviaryAidsSheet';
  try {
    var had = window[KEY];
    if (had && document.adoptedStyleSheets && document.adoptedStyleSheets.length) {
      document.adoptedStyleSheets = document.adoptedStyleSheets.filter(function (s) { return s !== had; });
    }
    window[KEY] = null;

    var css = __AVIARY_AIDS__;
    if (!css) { return; }

    var sheet = new CSSStyleSheet();
    sheet.replaceSync(css);
    window[KEY] = sheet;
    document.adoptedStyleSheets = (document.adoptedStyleSheets || []).concat([sheet]);
  } catch (e) { /* a document that refuses a stylesheet keeps its own look */ }
})();
""";
}
