using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Settings → Privacy → Website data (Phase 5 M2): what each site has kept in this profile —
/// its cookies and its storage — with a Remove for each.
///
/// Cookies come from the profile's cookie manager, all of them at once. Storage has no "list
/// every origin" API in WebView2 or the DevTools protocol (the DevTools per-kind queries —
/// IndexedDB, DOMStorage, CacheStorage — only answer for an origin whose page is open in that
/// tab; measured). So storage is found two ways: the engine's IndexedDB folders, one per
/// origin, sized on disk; and Storage.getUsageAndQuota for the other origins Aviary knows
/// (cookie hosts, history) — which reports 0 for localStorage and for a database not opened
/// since launch, so it is only a fallback. A site that kept ONLY localStorage and set no
/// cookie is therefore not listed; Clear browsing data still clears everything, and Remove
/// clears every kind of storage for each origin it knows.
///
/// Remove deletes the site's cookies one by one through the cookie manager, and asks the engine
/// to clear every other kind of storage (Storage.clearDataForOrigin) for each of its origins —
/// only ever for an origin that passed <see cref="ClearableOrigin"/>, because the engine reads an
/// origin it cannot parse as no origin at all and clears EVERY site (bug 52, measured).
/// A private window's Settings shows and removes the private profile's data, and never looks
/// at history.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The origins found per site in the last scan, so Remove clears all of them.</summary>
    private readonly Dictionary<string, HashSet<string>> _siteOrigins = new(StringComparer.OrdinalIgnoreCase);

    private sealed class SiteData
    {
        public string Site = string.Empty;
        public int Cookies;
        public long Bytes;
    }

    /// <summary>Scans the profile and hands the Settings page its list.</summary>
    private async Task PushSiteDataAsync(CoreWebView2 page)
    {
        Dictionary<string, SiteData> sites = new(StringComparer.OrdinalIgnoreCase);
        _siteOrigins.Clear();

        SiteData Entry(string site)
        {
            if (!sites.TryGetValue(site, out SiteData? data))
            {
                data = new SiteData { Site = site };
                sites[site] = data;
            }

            return data;
        }

        void Origin(string site, string origin)
        {
            if (!_siteOrigins.TryGetValue(site, out HashSet<string>? set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _siteOrigins[site] = set;
            }

            set.Add(origin);
        }

        try
        {
            IReadOnlyList<CoreWebView2Cookie> cookies = await page.CookieManager.GetCookiesAsync(string.Empty);
            foreach (CoreWebView2Cookie cookie in cookies)
            {
                string host = (cookie.Domain ?? string.Empty).TrimStart('.');
                if (host.Length == 0)
                {
                    continue;
                }

                string site = PublicSuffix.RegistrableDomain(host);
                Entry(site).Cookies++;
                Origin(site, (cookie.IsSecure ? "https://" : "http://") + host);
                Origin(site, "https://" + host);
            }

            // The origins history knows — never in a private window, whose Settings describes
            // the private profile, not the places the normal window has been.
            if (!_private)
            {
                foreach (HistoryEntry item in History.Recent(400))
                {
                    if (Uri.TryCreate(item.Url, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https")
                    {
                        Origin(PublicSuffix.RegistrableDomain(uri.Host), uri.GetLeftPart(UriPartial.Authority));
                    }
                }
            }

            // Databases: the engine keeps one IndexedDB folder per origin, and its size on disk is
            // the honest figure. (Storage.getUsageAndQuota answered 0 for an origin holding a
            // 300 KB database and 200 KB of localStorage — it counts neither until they are
            // opened again — so it is only a fallback below.)
            if (!_private)
            {
                foreach ((string origin, long bytes) in IndexedDbOrigins())
                {
                    if (Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri))
                    {
                        string site = PublicSuffix.RegistrableDomain(uri.Host);
                        Entry(site).Bytes += bytes;
                        Origin(site, origin);
                    }
                }
            }

            foreach ((string site, HashSet<string> origins) in _siteOrigins)
            {
                if (sites.TryGetValue(site, out SiteData? known) && known.Bytes > 0)
                {
                    continue;
                }

                foreach (string origin in origins)
                {
                    long usage = await StorageUsageAsync(page, origin);
                    if (usage > 0)
                    {
                        Entry(site).Bytes += usage;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "website data: scan");
        }

        object[] list = sites.Values
            .Where(s => s.Cookies > 0 || s.Bytes > 0)
            .OrderByDescending(s => s.Bytes + s.Cookies * 1024L)
            .Select(s => (object)new { site = s.Site, cookies = s.Cookies, bytes = s.Bytes })
            .ToArray();

        try
        {
            _ = page.ExecuteScriptAsync(
                "window.__aviarySiteData && window.__aviarySiteData(" + JsonSerializer.Serialize(list) + ")");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "website data: hand list to page");
        }
    }

    /// <summary>
    /// The origins that have an IndexedDB database in the engine's profile, and its size on
    /// disk — read from the folder names (`https_www.example.com_0.indexeddb.leveldb`, port 0
    /// meaning the default one). This is the engine's own layout, not an API: if a future
    /// runtime changes it, the list simply loses its database sizes, and Remove, which asks
    /// the engine to clear every kind of storage, is unaffected.
    /// </summary>
    private static IEnumerable<(string Origin, long Bytes)> IndexedDbOrigins()
    {
        string dir = System.IO.Path.Combine(WebViewEnvironment.UserDataFolder, "EBWebView", "Default", "IndexedDB");
        if (!System.IO.Directory.Exists(dir))
        {
            yield break;
        }

        const string Suffix = ".indexeddb.leveldb";
        foreach (string folder in System.IO.Directory.EnumerateDirectories(dir, "*" + Suffix))
        {
            string name = System.IO.Path.GetFileName(folder);
            name = name.Substring(0, name.Length - Suffix.Length);

            int first = name.IndexOf('_');
            int last = name.LastIndexOf('_');
            if (first <= 0 || last <= first)
            {
                continue;
            }

            string scheme = name.Substring(0, first);
            string host = name.Substring(first + 1, last - first - 1);
            string port = name.Substring(last + 1);
            if (scheme is not ("http" or "https") || host.Length == 0)
            {
                continue;
            }

            bool defaultPort = port is "0" || (scheme == "http" && port == "80") || (scheme == "https" && port == "443");
            long bytes = 0;
            try
            {
                foreach (System.IO.FileInfo file in new System.IO.DirectoryInfo(folder).EnumerateFiles("*", System.IO.SearchOption.AllDirectories))
                {
                    bytes += file.Length;
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Swallowed(ex, "website data: size a database folder");
            }

            yield return (scheme + "://" + host + (defaultPort ? string.Empty : ":" + port), bytes);
        }
    }

    private static async Task<long> StorageUsageAsync(CoreWebView2 page, string origin)
    {
        try
        {
            string result = await page.CallDevToolsProtocolMethodAsync(
                "Storage.getUsageAndQuota", JsonSerializer.Serialize(new { origin }));

            using JsonDocument doc = JsonDocument.Parse(result);
            return doc.RootElement.TryGetProperty("usage", out JsonElement usage) && usage.TryGetDouble(out double bytes)
                ? (long)bytes
                : 0;
        }
        catch (Exception ex)
        {
            // An origin the engine will not measure (an opaque or odd one) is simply not counted.
            Diagnostics.Swallowed(ex, "website data: measure an origin");
            return 0;
        }
    }

    /// <summary>Removes one site's cookies and storage, then shows the list again.</summary>
    private async Task RemoveSiteDataAsync(CoreWebView2 page, string site)
    {
        if (string.IsNullOrWhiteSpace(site))
        {
            return;
        }

        // Only the site's name goes to diagnostics, and not from a private window.
        DiagUi("website-data-remove", new { site });

        await ClearSiteDataAsync(page, site);
        await PushSiteDataAsync(page);
    }

    /// <summary>
    /// One site's cookies and storage, gone (M4.9: pulled out of RemoveSiteDataAsync so Forgetful
    /// browsing can use it). <paramref name="page"/> can be any live tab: the cookie store and the
    /// Storage domain are the profile's, not the page's. It pushes NOTHING to <paramref name="page"/> -
    /// forgetting runs from ordinary tabs, and a host push must never reach a website (bug 62).
    /// Every origin still goes through ClearableOrigin first (bug 52).
    /// </summary>
    private async Task ClearSiteDataAsync(CoreWebView2 page, string site)
    {
        try
        {
            HashSet<string> cookieHosts = new(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<CoreWebView2Cookie> cookies = await page.CookieManager.GetCookiesAsync(string.Empty);
            foreach (CoreWebView2Cookie cookie in cookies)
            {
                string host = (cookie.Domain ?? string.Empty).TrimStart('.');
                if (host.Length > 0
                    && string.Equals(PublicSuffix.RegistrableDomain(host), site, StringComparison.OrdinalIgnoreCase))
                {
                    page.CookieManager.DeleteCookie(cookie);

                    // The site's own subdomains - accounts., m., shop. - are only known from its
                    // cookies; without this their storage would outlive the forgetting.
                    cookieHosts.Add(host);
                }
            }

            List<string> candidates = _siteOrigins.TryGetValue(site, out HashSet<string>? known)
                ? new List<string>(known)
                : new List<string>();
            // _siteOrigins is only filled while the Website Data pane is open, and forgetting runs
            // without anyone having opened it - so history is asked directly for the site's real
            // origins. Without this a forgotten site keeps whatever its subdomains stored.
            if (!_private)
            {
                candidates.AddRange(History.OriginsFor(site));
            }

            candidates.Add("https://" + site);
            candidates.Add("http://" + site);
            foreach (string host in cookieHosts)
            {
                candidates.Add("https://" + host);
            }

            // The www variants only for a domain name — "www." in front of an address is not a
            // host at all (and "www.127.0.0.1" is exactly the origin that emptied the profile).
            if (ClearableOrigin("https://" + site) is { } siteOrigin
                && Uri.TryCreate(siteOrigin, UriKind.Absolute, out Uri? siteUri)
                && siteUri.HostNameType == UriHostNameType.Dns)
            {
                candidates.Add("https://www." + site);
                candidates.Add("http://www." + site);
            }

            HashSet<string> origins = new(StringComparer.OrdinalIgnoreCase);
            int refused = 0;
            foreach (string candidate in candidates)
            {
                if (ClearableOrigin(candidate) is { } origin)
                {
                    origins.Add(origin);
                }
                else
                {
                    refused++;
                }
            }

            if (refused > 0)
            {
                Diagnostics.Anomaly("website-data-origin-refused",
                    refused + " origin(s) the engine could not read were not cleared", new { count = refused });
            }

            foreach (string origin in origins)
            {
                try
                {
                    await page.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin",
                        JsonSerializer.Serialize(new { origin, storageTypes = StorageTypesButCookies }));
                }
                catch (Exception ex)
                {
                    Diagnostics.Swallowed(ex, "website data: clear an origin");
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "website data: remove");
        }
    }

    /// <summary>
    /// Every kind of storage except cookies, which Remove has already deleted one by one — so
    /// that even a bad origin can never reach another site's cookies. A kind a later engine no
    /// longer knows is skipped by it, not an error (measured).
    /// </summary>
    private const string StorageTypesButCookies =
        "file_systems,indexeddb,local_storage,shader_cache,websql,service_workers,cache_storage,"
        + "interest_groups,shared_storage,storage_buckets";

    /// <summary>
    /// The origin exactly as the engine will read it, or null when it would not read it at all.
    ///
    /// This matters more than it looks: Storage.clearDataForOrigin treats an origin it cannot
    /// parse as NO origin and clears every site's cookies and storage (bug 52 — measured with
    /// "https://www.127.0.0.1", which emptied the profile, while "https://github.io" or
    /// "https://com" cleared nothing, as they should). The URL standard refuses a host whose last
    /// label is a number unless the whole host is an IPv4 address; .NET's Uri accepts it, so the
    /// rule is applied here: http(s) only, no path, query or user, and a host that is an IPv4
    /// address, an IPv6 one, or labels of letters, digits, '-' and '_' whose last is not a number.
    /// </summary>
    private static string? ClearableOrigin(string candidate)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length > 0
            || uri.PathAndQuery != "/"
            || uri.Fragment.Length > 0)
        {
            return null;
        }

        string host;
        switch (uri.HostNameType)
        {
            case UriHostNameType.IPv4:
            case UriHostNameType.IPv6:
                host = uri.Host;   // .NET's canonical form, which the engine's matches (brackets kept for IPv6)
                break;

            case UriHostNameType.Dns:
                host = uri.IdnHost.ToLowerInvariant();
                if (host.Length == 0 || host.Length > 253 || host.EndsWith('.'))
                {
                    return null;
                }

                string[] labels = host.Split('.');
                foreach (string label in labels)
                {
                    if (label.Length is 0 or > 63
                        || !label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_'))
                    {
                        return null;
                    }
                }

                string last = labels[^1];
                bool endsInNumber = last.All(char.IsAsciiDigit)
                    || (last.StartsWith("0x", StringComparison.Ordinal) && last.Skip(2).All(char.IsAsciiHexDigit));
                if (endsInNumber)
                {
                    return null;
                }

                break;

            default:
                return null;
        }

        return uri.Scheme + "://" + host + (uri.IsDefaultPort ? string.Empty : ":" + uri.Port);
    }
}
