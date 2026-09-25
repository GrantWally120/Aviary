using System;
using System.Collections.Generic;

namespace Aviary;

/// <summary>
/// Settings → Experimental → "Remember each website’s zoom" (Alpha Build 5 M6 stage 3).
///
/// <para>Without it, Ctrl +/− sets ONE zoom — the "zoom" setting, Content → Default zoom — for every
/// site at once: zoom YouTube to 200% and every other site is 200% too. With it, zooming changes only
/// the site in front and is kept for that site in <see cref="SiteSettings"/> (sites.json), so Clear
/// Browsing Data forgets it with the rest of the per-site choices. Every site with no zoom of its own
/// follows the default. A zoom set back to the default is forgotten rather than kept.</para>
///
/// <para>A private window keeps nothing: its choices live here in memory, shared by the private
/// windows open together, and are dropped when the last of them closes. It does not read the zooms
/// kept from ordinary windows either — nothing of normal browsing leaks into a private window's look.</para>
///
/// <para>Switching the experiment off puts every site back on the default at once; what was kept
/// stays kept, so switching it on again brings each site's zoom back.</para>
/// </summary>
internal static class SiteZoom
{
    /// <summary>The per-site key in sites.json.</summary>
    public const string Key = "zoom";

    public static bool On => AppSettings.GetBool("exp.siteZoom");

    private static readonly Dictionary<string, string> PrivateZooms = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The zoom ("150%") a page at this address gets: its site's own while the experiment is on, else the default.</summary>
    public static string For(string url, bool isPrivate)
    {
        string fallback = AppSettings.Get("zoom");
        if (!On)
        {
            return fallback;
        }

        string host = BrowserTab.HostOf(url);
        if (host.Length == 0)
        {
            return fallback;
        }

        if (isPrivate)
        {
            return PrivateZooms.TryGetValue(host, out string? mine) ? mine : fallback;
        }

        string own = SiteSettings.Get(host, Key);
        return own.Length > 0 ? own : fallback;
    }

    /// <summary>Keeps a site's zoom — or, back at the default, forgets it.</summary>
    public static void Set(string host, bool isPrivate, string zoom)
    {
        bool isDefault = string.Equals(zoom, AppSettings.Get("zoom"), StringComparison.Ordinal);
        if (isPrivate)
        {
            if (isDefault)
            {
                PrivateZooms.Remove(host);
            }
            else
            {
                PrivateZooms[host] = zoom;
            }

            return;
        }

        if (isDefault)
        {
            SiteSettings.Remove(host, Key);
        }
        else
        {
            SiteSettings.Set(host, Key, zoom);
        }
    }

    /// <summary>The last private window has closed: what it zoomed is forgotten.</summary>
    public static void ForgetPrivate() => PrivateZooms.Clear();
}
