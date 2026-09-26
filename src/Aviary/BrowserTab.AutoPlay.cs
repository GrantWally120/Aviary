using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Settings → Content → Auto-Play → <b>Never Auto-Play</b> (Alpha Build 5 M6 stage 5).
///
/// <para>The engine's own rule does most of it (<c>--autoplay-policy=user-gesture-required</c>,
/// WebViewEnvironment): every video and sound element stays still until you press play, muted ones too.
/// But under that rule the engine lets the Web Audio API in a page's own frame start at once — measured
/// 26 Sep, its AudioContext "running" as the page loaded, where with nothing chosen it waits "suspended"
/// — so "Never" would have let a page beep that the default stops.</para>
///
/// <para>This holds Web Audio back the way the default does: a context made before you have done anything
/// on the page starts suspended, and a <c>resume()</c> waits until it is asked during a click or a key.
/// Each replaced function is a <c>Proxy</c> of the real one, as in BrowserTab.Fingerprint.cs.</para>
/// </summary>
internal sealed partial class BrowserTab
{
    private const string NeverAutoPlayScript = """
(function () {
  'use strict';
  var AC = window.AudioContext;
  if (typeof AC !== 'function') { return; }
  var apply = Reflect.apply, construct = Reflect.construct, suspend = AC.prototype.suspend;
  // A click or a key on this page, just now: the moment a person has asked for something.
  function asked() { try { return navigator.userActivation.isActive; } catch (e) { return false; } }
  try {
    window.AudioContext = new Proxy(AC, { construct: function (t, args, target) {
      var ctx = construct(t, args, target);
      if (!asked()) { try { apply(suspend, ctx, []); } catch (e) {} }
      return ctx;
    } });
    AC.prototype.resume = new Proxy(AC.prototype.resume, { apply: function (t, self, args) {
      return asked() ? apply(t, self, args) : new Promise(function () {});
    } });
  } catch (e) {}
})();
""";

    /// <summary>Registered as the tab starts, when the engine was launched with Never Auto-Play.</summary>
    private static async Task AddAutoPlayScriptAsync(CoreWebView2 core)
    {
        try
        {
            if (WebViewEnvironment.LaunchedText.TryGetValue("autoplay", out string? how)
                && how == WebViewEnvironment.AutoPlayNever)
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(NeverAutoPlayScript);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: never auto-play");
        }
    }
}
