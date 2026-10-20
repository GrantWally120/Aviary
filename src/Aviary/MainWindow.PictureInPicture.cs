using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Picture in picture (Alpha Build 5 M4.9) — Safari's, from Grant's pick of what to take next.
///
/// <para><b>What was measured first.</b> The engine already does both kinds: on this build
/// <c>document.pictureInPictureEnabled</c> is true, <c>window.documentPictureInPicture</c> is an
/// object, and a trusted click on a page's own button really does open a separate top-level
/// "Picture in picture" window — enumerated and confirmed, owned by the WebView2 browser process,
/// not by Aviary. So nothing needed enabling: no browser flag, no change to
/// <c>WebViewEnvironment.BuildArguments</c>. A site with its own button worked in Aviary already.</para>
///
/// <para><b>So what is this for?</b> The videos whose sites have no button — which is most of them.
/// Safari puts picture in picture in the right-click menu of any video, and that is what Aviary
/// does here, for the video that was actually right-clicked.</para>
///
/// <para><b>Why the DevTools protocol rather than ExecuteScriptAsync.</b> Two reasons, and the
/// second is the real one. Asking for picture in picture is meant to need a user gesture; a host
/// running a script has none, and while Chromium currently allows it for a playing video, that is a
/// carve-out and not a promise. <c>Runtime.evaluate</c> takes <c>userGesture</c>, which is the
/// documented way for a host to say the person asked. It also takes <c>awaitPromise</c>, and
/// <c>ExecuteScriptAsync</c> hands back <c>{}</c> for a promise — so it is the only one of the two
/// that can say whether picture in picture actually happened.</para>
///
/// <para><b>Why the menu item is not always there.</b> The engine says a video was right-clicked,
/// but not whether THAT video will accept picture in picture — a site can refuse with
/// <c>disablePictureInPicture</c>, and a video inside a cross-origin frame is not reachable from
/// the main frame at all. So the menu takes a deferral, asks the page, and offers the item only
/// when there is something it can really do. Page targets are checked too, so player overlays
/// work; the Aa menu can reach a playing video even when the player consumes right-clicks.</para>
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Finds the video that was right-clicked and either reports on it or toggles it.
    ///
    /// The point comes from the engine in the web view's own coordinates, which is what
    /// <c>elementsFromPoint</c> wants. It sees through player overlays without choosing an
    /// unrelated video elsewhere. Negative coordinates are the Aa menu: there, prefer the
    /// current PiP video, otherwise the largest playing video the main frame can reach.
    /// </summary>
    private const string PipScript = """
(function (x, y, act) {
  if (!document.pictureInPictureEnabled) { return 'off'; }
  function usable(v) {
    return !!v && v.tagName === 'VIDEO' && !v.disablePictureInPicture
        && v.getAttribute('disablepictureinpicture') === null
        && v.readyState >= 1 && v.videoWidth > 0;
  }
  var v = null, under = [];
  try { under = document.elementsFromPoint(x, y); } catch (e) {}
  for (var i = 0; i < under.length && !v; i++) {
    if (usable(under[i])) { v = under[i]; }
  }
  /* Negative coordinates mean the Aa menu. A right-click elsewhere on a page
     must never select an unrelated video. */
  if (!v && x < 0 && y < 0) {
    if (usable(document.pictureInPictureElement)) { v = document.pictureInPictureElement; }
    var found = [], all = document.querySelectorAll('video');
    for (var i = 0; i < all.length; i++) {
      if (usable(all[i]) && !all[i].paused && !all[i].ended) { found.push(all[i]); }
    }
    found.sort(function (a, b) {
      if (a.paused !== b.paused) { return a.paused ? 1 : -1; }
      return b.clientWidth * b.clientHeight - a.clientWidth * a.clientHeight;
    });
    v = v || found[0] || null;
  }
  if (!v) { return 'none'; }
  var inPip = document.pictureInPictureElement === v;
  if (act === 'ask') { return inPip ? 'in' : 'out'; }
  if (inPip) {
    return document.exitPictureInPicture().then(function () { return 'left'; },
                                               function (e) { return 'error ' + e.name; });
  }
  return v.requestPictureInPicture().then(function () { return 'entered'; },
                                         function (e) { return 'error ' + e.name; });
})(__X__, __Y__, '__ACT__')
""";

    /// <summary>
    /// "in" (this video is in picture in picture), "out" (it could be), "none" (no video this frame
    /// can reach), "off" (the engine will not), "entered", "left", "error &lt;name&gt;", or null when
    /// the page could not be asked at all.
    /// </summary>
    private static async Task<string?> AskPageAboutPipAsync(CoreWebView2 core, double x, double y, string act)
    {
        try
        {
            string expression = PipScript
                .Replace("__X__", x.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("__Y__", y.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("__ACT__", act);

            string json = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                JsonSerializer.Serialize(new
                {
                    expression,
                    awaitPromise = true,
                    returnByValue = true,
                    userGesture = true,
                }));

            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("result", out JsonElement result)
                   && result.TryGetProperty("value", out JsonElement value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "picture in picture: ask the page");
            return null;
        }
    }

    /// <summary>
    /// The right-clicked video's state, for deciding whether to offer the menu item and what to
    /// call it: "in", "out", or null for "do not offer it".
    /// </summary>
    private static async Task<string?> PipStateAsync(CoreWebView2 core, double x, double y)
    {
        string? answer = await AskPageAboutPipAsync(core, x, y, "ask");
        return answer is "in" or "out" ? answer : null;
    }

    /// <summary>In or out, whichever it is not. What the menu item does.</summary>
    private async Task TogglePictureInPictureAsync(CoreWebView2 core, double x, double y)
    {
        string? answer = await AskPageAboutPipAsync(core, x, y, "go");

        // Only the outcome, never the video's address: what is being watched is browsing, and
        // the navigation line already records it.
        DiagFeature("picture-in-picture", new { outcome = answer ?? "unreachable" });
    }
}
