using System;
using System.Collections.Generic;
using System.Linq;

namespace Aviary;

/// <summary>
/// Settings → Experimental (Alpha Build 5 M6): unfinished features anyone can switch on, each
/// explained in plain words on its row — what it does and what might happen — under a badge:
/// <b>Safe to try</b>, <b>Some sites may look wrong</b>, or <b>Privacy trade-off</b>.
///
/// <para><b>Nothing here can make Aviary less safe, and that is enforced here, not promised.</b>
/// An experiment is a switch (on or off) whose EFFECT is fixed in code: engine switches in this
/// file, anything else in the one place that reads its key (e.g. BrowserTab.Dark.cs). A setting's
/// value is only ever compared with "true" — never passed to the engine — so editing settings.json
/// cannot add a switch, and a key this list does not know does nothing at all. Excluded outright, whatever
/// anyone asks, and refused by <see cref="IsForbidden"/> even if one were ever typed in below:
/// anything that weakens security — certificate or HTTPS errors, the sandbox, site isolation, web
/// security or same-origin rules, mixed content, file access from pages, or a debugging port.</para>
///
/// <para>The row text lives with the rest of Settings (InternalPages.cs, the <c>experimental</c>
/// section); the name is kept here too for the one place Aviary names an experiment outside that
/// page — the question after a problem (<see cref="SafetyMarker"/>). tools/test/settings-audit.js
/// checks that the two names agree.</para>
///
/// <para>Every experiment was measured on this engine before it was offered (HANDOFF §27,
/// tools/test/switch-probe.ps1): one that turns out to do nothing is cut, not shipped inert.
/// "Screen smoothness" was cut that way — the engine already draws at the screen's own rate (241
/// frames a second on a 240 Hz screen), and the only switch that changes it made pages draw
/// ~4,300 frames a second that no screen can show.</para>
/// </summary>
internal static class Experiments
{
    public const string Prefix = "exp.";

    internal enum Badge { Safe, Looks, Privacy }

    internal sealed record Experiment(
        string Key,
        string Name,
        Badge Kind,
        bool NeedsRestart,
        string[] Features,
        string[] Switches);

    public static readonly Experiment[] All =
    {
        // Measured 25 Sep: one Page Down glides through 37 positions without it, and jumps
        // 0 -> 667 in one frame with it.
        new("exp.smoothScrollOff", "Turn off smooth scrolling", Badge.Safe, NeedsRestart: true,
            Features: Array.Empty<string>(), Switches: new[] { "--disable-smooth-scrolling" }),

        // Not an engine switch either: it decides how a launch restores pinned tabs (MainWindow.Pinned.cs),
        // so it too only applies when Aviary starts - hence NeedsRestart, and the row's restart note.
        new("exp.pinnedOnClick", "Open pinned tabs only when clicked", Badge.Safe, NeedsRestart: true,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),

        // Stage 2, at once: every tab but the one in front of its window is muted (MainWindow.Experiments.cs).
        new("exp.muteBackground", "Mute tabs in the background", Badge.Safe, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),

        // Stage 2, at once: a small readout Aviary draws itself over the page (MainWindow.Overlay.cs).
        new("exp.perfOverlay", "Show speed and memory on top of pages", Badge.Safe, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),

        // NOT an engine switch - per tab, at once (BrowserTab.Dark.cs). The engine's WebContentsForceDark
        // was built first and measured: it darkened Aviary's own pages as well, and no page could opt
        // out. The per-tab DevTools override does the same darkening for websites only.
        new("exp.darkWebsites", "Make every website dark", Badge.Looks, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),

        // ── Stage 3: tabs and looks - none of them an engine switch, all at once. ──

        // Looks. The toolbar and tab strip take the page's theme-color (MainWindow.Tint.cs).
        new("exp.tintToolbar", "Tint the toolbar to match the page", Badge.Safe, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),

        // Looks. CSS in the stylesheet Aviary adopts into websites (ReadingAids.cs). The engine's
        // FluentScrollbar / FluentOverlayScrollbar features were measured first and change nothing on
        // this engine; OverlayScrollbar hides the bar until you scroll, which is not "thin".
        new("exp.thinScrollbars", "Thin scrollbars on websites", Badge.Looks, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),

        // Looks. Ctrl +/- and the Aa menu zoom the site in front, not every site (BrowserTab.ApplyZoom).
        new("exp.siteZoom", "Remember each website’s zoom", Badge.Safe, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),

        // Looks. The per-site "Use Reader Automatically" for every site (MainWindow.Reader.cs).
        new("exp.autoReader", "Use Reader on every article", Badge.Looks, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),

        // Tabs.
        new("exp.tabPreview", "Show a preview when you point at a tab", Badge.Safe, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),
        new("exp.switchToTab", "Switch to a tab that is already open", Badge.Safe, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),
        new("exp.videoPopOut", "Pop a playing video out when you switch tabs", Badge.Safe, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),
        new("exp.closeOldTabs", "Close tabs you have not looked at in a week", Badge.Safe, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),
        new("exp.dblClickNewTab", "Double-click below your tabs for a new tab", Badge.Safe, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),

        // ── Stage 4: privacy - all at once, none an engine switch. ──

        // Every website's cookies and stored data go when the last window closes; sites set to "Never"
        // in Forgetful browsing keep their sign-in cookies (MainWindow.ForgetEverything.cs).
        new("exp.forgetOnClose", "Forget everything when Aviary closes", Badge.Safe, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),

        // A second, longer list of tracking parameters than Brave's (LinkCleaner.cs). Measured first:
        // 21 of 23 well-known trackers went through the shipped list.
        new("exp.moreLinkCleaning", "Strip even more tracking from links", Badge.Looks, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),

        // Shields' verdict (BrowserTab.Shields.cs): a script from any site but the page's own is refused.
        new("exp.thirdPartyScripts", "Block scripts from other websites", Badge.Looks, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),

        // Per-site, per-session noise in canvas and audio reads, and plainer hardware (BrowserTab.Fingerprint.cs).
        new("exp.fingerprint", "Stronger fingerprint protection", Badge.Looks, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),

        // Replaces "Stop websites asking to show notifications", CUT measured: the engine never shows
        // web notifications or asks - every request is already refused. Every <a ping>, the site's own
        // included (EasyPrivacy already stops other sites'), in Shields' verdict. The engine's --no-pings
        // was measured too; Aviary's own filter works at once and shows what it stopped.
        new("exp.noPings", "Stop link-click pings", Badge.Safe, NeedsRestart: false,
            Features: Array.Empty<string>(), Switches: Array.Empty<string>()),

        // Measured 25 Sep: H.264 and VP9 at 1080p stop being "power efficient" - the graphics
        // card no longer decodes them; the processor does.
        new("exp.noVideoDecode", "Turn off video hardware decoding", Badge.Safe, NeedsRestart: true,
            Features: Array.Empty<string>(), Switches: new[] { "--disable-accelerated-video-decode" }),
    };

    /// <summary>
    /// Switches (and the start of switches) that weaken security. Never passed, from here or
    /// anywhere an experiment could reach — <see cref="EngineSwitches"/> drops any that match.
    /// </summary>
    private static readonly string[] Forbidden =
    {
        "--ignore-certificate-errors", "--ignore-ssl-errors", "--ignore-urlfetcher-cert-requests",
        "--allow-insecure-localhost", "--allow-running-insecure-content",
        "--unsafely-treat-insecure-origin-as-secure", "--test-type",
        "--no-sandbox", "--disable-gpu-sandbox", "--disable-setuid-sandbox", "--no-zygote",
        "--disable-site-isolation-trials", "--disable-web-security", "--reduce-security-for-testing",
        "--allow-file-access-from-files", "--allow-file-access", "--disable-xss-auditor",
        "--remote-debugging-port", "--remote-debugging-pipe", "--remote-allow-origins",
        "--user-data-dir",
        "--disable-client-side-phishing-detection", "--safebrowsing-disable",
        "--disable-popup-blocking", "--disable-extensions-file-access-check",
        // Switching engine features OFF is how site isolation and its relatives are removed, so no
        // experiment may use it. Experiments only ever ADD features (to the one --enable-features).
        "--disable-features", "--disable-blink-features",
    };

    public static bool IsExperiment(string key) =>
        key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && All.Any(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static bool IsOn(Experiment e) => AppSettings.GetBool(e.Key);

    public static List<Experiment> On() => All.Where(IsOn).ToList();

    public static Experiment? Find(string key) =>
        All.FirstOrDefault(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static bool IsForbidden(string engineSwitch) =>
        Forbidden.Any(f => engineSwitch.StartsWith(f, StringComparison.OrdinalIgnoreCase));

    /// <summary>The engine features the experiments that are on add — for the ONE --enable-features.</summary>
    public static IEnumerable<string> EngineFeatures(Func<Experiment, bool> on) =>
        All.Where(on).SelectMany(e => e.Features);

    /// <summary>The switches the experiments that are on add, with anything forbidden dropped.</summary>
    public static IEnumerable<string> EngineSwitches(Func<Experiment, bool> on) =>
        All.Where(on).SelectMany(e => e.Switches).Where(s => !IsForbidden(s));

    /// <summary>
    /// "Turn every experiment off", and the switch-off after a problem. Returns the keys that were
    /// on, so they can be turned back on. Writes each through <see cref="AppSettings.Set"/>, so
    /// diagnostics records every one as an experiment change (category 7). <paramref name="quikToo"/>: the
    /// switch-off after a problem also covers Quik (M7, Beta) - the Experimental pane's button does not.
    /// </summary>
    public static List<string> TurnAllOff(bool quikToo = false)
    {
        List<string> were = new();
        foreach (Experiment e in All)
        {
            if (IsOn(e))
            {
                AppSettings.Set(e.Key, "false");
                were.Add(e.Key);
            }
        }

        // M7: Quik is Beta, and the crash protection watches it the same way.
        if (quikToo && AppSettings.GetBool(Quik.Key))
        {
            AppSettings.Set(Quik.Key, "false");
            were.Add(Quik.Key);

            // Nothing is owed to a launch that will not run Quik: switched back on, it is owed again.
            Quik.IntroDone();
        }

        return were;
    }
}
