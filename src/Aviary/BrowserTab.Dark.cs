using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Settings → Experimental → "Make every website dark" (Alpha Build 5 M6), per tab.
///
/// <para><b>Why not the engine's switch.</b> The obvious way, the WebContentsForceDark engine
/// feature, was built first and measured: it darkened websites (a page with no colours of its own
/// painted rgb(18,18,18)) — and Aviary's OWN pages too. Settings in the light theme came out black,
/// and nothing a page can say stops it: <c>color-scheme: light dark</c>, <c>only light</c> in CSS
/// and the <c>only light</c> meta tag were all darkened alike on this engine (25 Sep,
/// tools/test/scheme-probe.js). So it is the per-tab DevTools override instead,
/// <c>Emulation.setAutoDarkModeOverride</c> — the same darkening, measured to reach the page, to
/// last across the tab's navigations, and to come off again — set ON for websites and OFF for
/// Aviary's own pages (Settings, the Start Page, Birds News) and while Reader is showing, whose
/// background is a choice the person already made. It also means the row needs no restart.</para>
/// </summary>
internal sealed partial class BrowserTab
{
    /// <summary>What this tab's engine was last told: null = never, true = darkened, false = not.</summary>
    private bool? _darkSent;

    /// <summary>Reader is showing in this tab (set by the window, cleared by any new navigation).</summary>
    private bool _readerShown;

    public static bool DarkWebsites => AppSettings.GetBool("exp.darkWebsites");

    /// <summary>Whether this tab should be darkened right now.</summary>
    private bool WantsDark => DarkWebsites && !IsInternalPage && !_readerShown;

    /// <summary>"Make every website dark" is darkening this page (M6 stage 3: the tint leaves such a page alone).</summary>
    public bool Darkened => WantsDark;

    /// <summary>Re-applies the switch to this tab: after the setting changes, or Reader comes or goes.</summary>
    public void RefreshDark() => _ = SendDarkAsync(WantsDark);

    public void SetReaderShown(bool shown)
    {
        _readerShown = shown;
        RefreshDark();
    }

    /// <summary>From OnNavigationStarting, once it is known whether the page is Aviary's own or a website.</summary>
    private void DarkOnNavigationStarting()
    {
        _readerShown = false;
        _ = SendDarkAsync(WantsDark);
    }

    private async Task SendDarkAsync(bool want)
    {
        // Nothing is ever sent to a tab that has never been darkened and is not to be: the
        // experiment off costs nothing at all.
        if (_darkSent == want || (_darkSent is null && !want))
        {
            return;
        }

        if (View.CoreWebView2 is not CoreWebView2 core)
        {
            return;
        }

        _darkSent = want;
        try
        {
            // {} clears the override: the engine's own behaviour (never darken) again.
            await core.CallDevToolsProtocolMethodAsync("Emulation.setAutoDarkModeOverride", want ? "{\"enabled\":true}" : "{}");
        }
        catch (Exception ex)
        {
            _darkSent = null;
            Diagnostics.Swallowed(ex, "tab: make websites dark");
        }
    }
}
