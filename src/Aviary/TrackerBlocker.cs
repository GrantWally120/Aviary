using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace Aviary;

/// <summary>
/// Blocks ad and tracker requests before they leave the machine.
///
/// This is deliberately a domain blocklist rather than a filter-rule engine. Real
/// blockers parse EasyList-style rules with element hiding, regex patterns and per-site
/// exceptions; that is a large, ongoing project and it is not what Phase 3 is for. A
/// suffix match against known ad and telemetry hosts catches the large majority of
/// third-party tracking for a fraction of the complexity, and — crucially — it fails
/// safe: a domain that is not on the list simply loads.
///
/// It WILL occasionally break a site. That is why it has an off switch, and why the
/// counter is visible: if a page looks wrong, the first thing to try is turning this off.
/// </summary>
internal static class TrackerBlocker
{
    /// <summary>Requests dropped since launch. Shown in Settings.</summary>
    public static int BlockedCount { get; private set; }

    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Matched as domain suffixes, so "doubleclick.net" also covers
    /// "stats.g.doubleclick.net". Kept to well-known advertising, analytics and
    /// fingerprinting hosts — nothing that serves page content people came for.
    /// </summary>
    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        // Advertising exchanges and networks
        "doubleclick.net", "googlesyndication.com", "googleadservices.com",
        "adservice.google.com", "adnxs.com", "rubiconproject.com", "pubmatic.com",
        "openx.net", "criteo.com", "criteo.net", "taboola.com", "outbrain.com",
        "adcolony.com", "applovin.com", "unityads.unity3d.com", "smartadserver.com",
        "casalemedia.com", "33across.com", "sharethrough.com", "triplelift.com",
        "indexww.com", "adform.net", "teads.tv", "media.net", "revcontent.com",
        "zedo.com", "adroll.com", "bidswitch.net", "spotxchange.com", "sovrn.com",
        "yieldmo.com", "gumgud.com", "districtm.io", "themoneytizer.com",

        // Analytics and telemetry
        "google-analytics.com", "analytics.google.com", "googletagmanager.com",
        "googletagservices.com", "scorecardresearch.com", "quantserve.com",
        "quantcount.com", "chartbeat.com", "chartbeat.net", "newrelic.com",
        "nr-data.net", "segment.io", "segment.com", "amplitude.com", "mixpanel.com",
        "heapanalytics.com", "fullstory.com", "hotjar.com", "hotjar.io",
        "mouseflow.com", "crazyegg.com", "clarity.ms", "luckyorange.com",
        "statcounter.com", "histats.com", "kissmetrics.com", "parsely.com",
        "matomo.cloud", "branch.io", "adjust.com", "appsflyer.com", "kochava.com",

        // Social trackers (the widgets, not the sites themselves)
        "connect.facebook.net", "pixel.facebook.com", "analytics.twitter.com",
        "ads-twitter.com", "static.ads-twitter.com", "ads.linkedin.com",
        "px.ads.linkedin.com", "analytics.tiktok.com", "ads.tiktok.com",
        "business-api.tiktok.com", "ads.pinterest.com", "ct.pinterest.com",
        "sc-static.net", "ads.reddit.com", "events.redditmedia.com",

        // Fingerprinting, session replay and data brokers
        "fingerprintjs.com", "fpjs.io", "perimeterx.net", "px-cloud.net",
        "bluekai.com", "krxd.net", "demdex.net", "everesttech.net",
        "rlcdn.com", "crwdcntrl.net", "agkn.com", "exelator.com",
        "tapad.com", "id5-sync.com", "liadm.com", "smaato.net",

        // Miscellaneous beacons
        "onesignal.com", "pushcrew.com", "moatads.com", "adsafeprotected.com",
        "serving-sys.com", "flashtalking.com", "omtrdc.net", "2o7.net",
        "optimizely.com", "sitescout.com", "yieldlab.net", "zqtk.net",
    };

    /// <summary>
    /// True if this request should never leave the machine. Anything unparseable is
    /// allowed through — failing open is right here, since a false positive means a
    /// broken page and a false negative just means one advert.
    /// </summary>
    public static bool ShouldBlock(string url) => Match(url) is not null;

    /// <summary>
    /// The rule that blocks this request — the listed domain it fell under — or null. The
    /// rule is what diagnostics records beside each blocked address, so a site broken by the
    /// blocker can be traced to the one entry responsible.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static string? Match(string url)
    {
        if (!Enabled || string.IsNullOrEmpty(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        string host = uri.Host;
        if (host.Length == 0)
        {
            return null;
        }

        // Walk the labels: "stats.g.doubleclick.net" tests itself, then
        // "g.doubleclick.net", then "doubleclick.net" — which hits.
        int index = 0;
        while (index >= 0 && index < host.Length)
        {
            string suffix = host.Substring(index);
            if (Blocked.Contains(suffix))
            {
                return suffix;
            }

            int dot = host.IndexOf('.', index);
            index = dot < 0 ? -1 : dot + 1;
        }

        return null;
    }

    public static void CountBlocked() => BlockedCount++;

    public static void ResetCount() => BlockedCount = 0;
}
