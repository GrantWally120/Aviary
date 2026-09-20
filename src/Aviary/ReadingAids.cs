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
