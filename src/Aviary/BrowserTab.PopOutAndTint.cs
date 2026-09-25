using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Windows.UI;

namespace Aviary;

/// <summary>
/// Two of Settings → Experimental's stage 3 experiments (Alpha Build 5 M6), the parts that talk to
/// the page. The window decides when (MainWindow.TabsExperiments.cs).
///
/// <para><b>Pop a playing video out when you switch tabs.</b> Picture in picture needs a person's
/// gesture, and a page cannot start it for itself — so Aviary asks through the tab's own DevTools
/// session, as a gesture, which is how the browser starts it on a person's behalf (the same channel
/// "Make every website dark" uses). Only a video that is playing, with its sound on, at least 200
/// pixels wide, that the site has not barred from picture in picture; the largest if there are
/// several. The window takes it back when the tab returns — but only one Aviary popped out.</para>
///
/// <para><b>Tint the toolbar to match the page.</b> The page's <c>theme-color</c> — the colour a site
/// declares for the browser around it, the one Safari's tinted tab bar uses — for the colour scheme
/// in use, read after each page and each change of address. Never the page's pixels.</para>
/// </summary>
internal sealed partial class BrowserTab
{
    /// <summary>Aviary popped this tab's video out as the tab went behind; it takes it back on return.</summary>
    public bool PoppedOut { get; private set; }

    private const string PopOutScript = """
(async function () {
  if (document.pictureInPictureElement) { return 'already'; }
  var best = null, area = 0, all = document.querySelectorAll('video');
  for (var i = 0; i < all.length; i++) {
    var v = all[i];
    if (v.paused || v.ended || v.muted || v.volume === 0 || v.readyState < 2 || v.disablePictureInPicture) { continue; }
    var r = v.getBoundingClientRect(), a = r.width * r.height;
    if (r.width >= 200 && a > area) { best = v; area = a; }
  }
  if (!best) { return 'none'; }
  try { await best.requestPictureInPicture(); return 'popped'; } catch (e) { return 'refused ' + e.name; }
})()
""";

    /// <summary>The tab went behind: its playing video, if it has one, carries on in picture in picture.</summary>
    public async Task<string> PopOutVideoAsync()
    {
        try
        {
            if (View.CoreWebView2 is not CoreWebView2 core || IsInternalPage)
            {
                return "no page";
            }

            string args = JsonSerializer.Serialize(new
            {
                expression = PopOutScript,
                awaitPromise = true,
                returnByValue = true,
                userGesture = true,
            });
            string json = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", args);
            using JsonDocument doc = JsonDocument.Parse(json);
            string answer = doc.RootElement.TryGetProperty("result", out JsonElement result)
                            && result.TryGetProperty("value", out JsonElement value)
                            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

            PoppedOut = answer == "popped";
            if (PoppedOut)
            {
                // With "Mute tabs in the background" on, the tab was muted a moment ago as it went
                // behind; a popped-out video keeps its sound (BrowserTab.Mute.cs asks again).
                RefreshMute();
            }

            return answer;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: pop a video out");
            return "error";
        }
    }

    /// <summary>The tab is back in front: the video Aviary popped out goes back into the page.</summary>
    public async Task ReturnVideoAsync()
    {
        PoppedOut = false;
        try
        {
            if (View.CoreWebView2 is CoreWebView2 core)
            {
                await core.ExecuteScriptAsync(
                    "document.pictureInPictureElement ? document.exitPictureInPicture().then(function(){return true},function(){return false}) : false");
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: bring a video back");
        }
    }

    /// <summary>The colour this tab's page declares for the browser around it, if it declares one.</summary>
    public Color? TintColor { get; private set; }

    // A canvas normalises any CSS colour ("teal", "hsl(...)", "#abc") to "#rrggbb", or to
    // "rgba(r, g, b, a)" when it is see-through, without touching the page's own document.
    private const string TintScript = """
(function () {
  var metas = document.querySelectorAll('meta[name="theme-color" i]');
  for (var i = 0; i < metas.length; i++) {
    var m = metas[i], media = m.getAttribute('media'), c = (m.getAttribute('content') || '').trim();
    if (!c || (media && !matchMedia(media).matches)) { continue; }
    var x = document.createElement('canvas').getContext('2d');
    x.fillStyle = '#010203'; x.fillStyle = c;
    if (x.fillStyle === '#010203' && c.toLowerCase() !== '#010203') { continue; }
    return x.fillStyle;
  }
  return '';
})()
""";

    /// <summary>Asks the page for its colour again. True when the answer changed.</summary>
    public async Task<bool> ReadTintAsync()
    {
        Color? was = TintColor;
        try
        {
            if (View.CoreWebView2 is not CoreWebView2 core || IsInternalPage)
            {
                TintColor = null;
            }
            else
            {
                string json = await core.ExecuteScriptAsync(TintScript);
                TintColor = ParseCssColour(JsonSerializer.Deserialize<string>(json) ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: read the page's colour");
            TintColor = null;
        }

        return !Equals(was, TintColor);
    }

    /// <summary>Forgets the colour (a new page has none until it is asked).</summary>
    public void ForgetTint() => TintColor = null;

    /// <summary>"#rrggbb" or "rgba(r, g, b, a)" as a canvas writes them; see-through colours are taken as opaque.</summary>
    internal static Color? ParseCssColour(string css)
    {
        css = css.Trim();
        if (css.Length == 7 && css[0] == '#'
            && byte.TryParse(css.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
            && byte.TryParse(css.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
            && byte.TryParse(css.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return Color.FromArgb(255, r, g, b);
        }

        if (css.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && css.EndsWith(')'))
        {
            string[] parts = css.Substring(5, css.Length - 6).Split(',');
            if (parts.Length == 4
                && byte.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte rr)
                && byte.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte gg)
                && byte.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte bb)
                && double.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
                && a > 0.05)
            {
                return Color.FromArgb(255, rr, gg, bb);
            }
        }

        return null;
    }
}
