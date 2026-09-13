using System;
using System.Collections.Generic;
using System.Globalization;

namespace Aviary;

/// <summary>
/// Turns a host into the domain a person would call "the site", without dragging in the
/// Public Suffix List as a dependency.
///
/// The real PSL is ~10,000 lines and a NuGet package, which needs asking about first. This
/// is a hand-kept table of the multi-label suffixes that actually come up, and it fails in
/// a safe direction: an unknown suffix simply yields the last two labels, which is right
/// for the overwhelming majority of hosts.
///
/// The hosting suffixes at the bottom are deliberate and matter more than they look. Without
/// them every project on github.io shares one key, so one person's site icon would be shown
/// for everyone else's.
/// </summary>
internal static class PublicSuffix
{
    private static readonly HashSet<string> MultiLabel = new(StringComparer.OrdinalIgnoreCase)
    {
        // Country second-level domains
        "co.uk", "org.uk", "me.uk", "ac.uk", "gov.uk", "net.uk", "sch.uk",
        "com.au", "net.au", "org.au", "edu.au", "gov.au", "id.au",
        "co.nz", "net.nz", "org.nz", "govt.nz", "ac.nz",
        "co.za", "org.za", "web.za", "net.za",
        "com.br", "net.br", "org.br", "gov.br",
        "com.mx", "com.ar", "com.co", "com.pe", "com.ve",
        "co.jp", "ne.jp", "or.jp", "ac.jp", "go.jp",
        "co.kr", "or.kr", "ne.kr", "go.kr",
        "com.cn", "net.cn", "org.cn", "gov.cn", "edu.cn",
        "com.hk", "org.hk", "net.hk", "edu.hk",
        "com.tw", "org.tw", "net.tw",
        "com.sg", "com.my", "com.vn", "co.th", "co.id", "co.in", "net.in", "org.in",
        "co.il", "com.tr", "com.sa", "com.eg", "com.pk", "com.bd",
        // Cebu is home turf; .com.ph and friends are not an afterthought here.
        "com.ph", "net.ph", "org.ph", "gov.ph", "edu.ph", "ngo.ph",
        "com.es", "com.pl", "com.ua", "com.ru", "co.ke", "co.ng",
        "gov.uk", "nhs.uk", "police.uk",

        // Shared hosting. These MUST be treated as suffixes or every user site on the
        // platform collapses into one icon key and shows a stranger's mark.
        "github.io", "gitlab.io", "netlify.app", "vercel.app", "pages.dev",
        "workers.dev", "web.app", "firebaseapp.com", "azurewebsites.net",
        "herokuapp.com", "glitch.me", "repl.co", "replit.app", "surge.sh",
        "myshopify.com", "substack.com", "notion.site", "blogspot.com",
        "wordpress.com", "tumblr.com", "medium.com", "wixsite.com",
        "sharepoint.com", "amazonaws.com", "cloudfront.net", "fly.dev",
        "onrender.com", "readthedocs.io", "neocities.org", "itch.io",
    };

    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> MultiLabelBySpan =
        MultiLabel.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>
    /// True for a two-label public suffix ("co.uk", "github.io"). Span-based for the ad blocker,
    /// which asks once per request on the UI thread and must not allocate to do it.
    /// </summary>
    public static bool IsMultiLabel(ReadOnlySpan<char> lastTwoLabels) => MultiLabelBySpan.Contains(lastTwoLabels);

    /// <summary>The two-label suffixes, for the ad blocker's page script ("amazon.*" rules).</summary>
    public static IEnumerable<string> MultiLabelSuffixes => MultiLabel;

    /// <summary>
    /// The registrable domain: "docs.google.com" becomes "google.com",
    /// "grant.github.io" stays "grant.github.io", "bbc.co.uk" stays "bbc.co.uk".
    /// An IP address or a single label is returned unchanged.
    /// </summary>
    public static string RegistrableDomain(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return string.Empty;
        }

        host = host.Trim().TrimEnd('.').ToLowerInvariant();

        // An IPv4 literal or a bracketed IPv6 one has no registrable domain.
        if (host.Length == 0 || host[0] == '[' || IsIpv4(host))
        {
            return host;
        }

        string[] labels = host.Split('.');
        if (labels.Length <= 2)
        {
            return host;
        }

        string lastTwo = labels[^2] + "." + labels[^1];
        return MultiLabel.Contains(lastTwo) && labels.Length >= 3
            ? labels[^3] + "." + lastTwo
            : lastTwo;
    }

    private static bool IsIpv4(string host)
    {
        string[] parts = host.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        foreach (string part in parts)
        {
            if (!byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The letter shown when a site has no icon of its own.
    ///
    /// Taken from the registrable domain rather than the raw host, so "www.bbc.co.uk" and
    /// "news.bbc.co.uk" both read B rather than W and N. Digits and non-Latin scripts are
    /// passed through — a Japanese site gets its own first character, not a mangled "?".
    /// </summary>
    public static string Monogram(string host)
    {
        string domain = RegistrableDomain(host);
        if (domain.Length == 0)
        {
            return "•";
        }

        // Skip a leading "www." that survived, then take the first letter or digit.
        if (domain.StartsWith("www.", StringComparison.Ordinal))
        {
            domain = domain.Substring(4);
        }

        foreach (char c in domain)
        {
            if (char.IsLetterOrDigit(c))
            {
                return char.ToUpperInvariant(c).ToString();
            }
        }

        return "•";
    }

    /// <summary>
    /// A stable 0-7 index into the monogram palette, derived from the domain so a site
    /// keeps its colour across sessions instead of being assigned one in tab-open order.
    /// </summary>
    public static int AccentIndex(string host)
    {
        string domain = RegistrableDomain(host);
        if (domain.Length == 0)
        {
            return 0;
        }

        // FNV-1a. Deliberately not string.GetHashCode(), which is randomised per process
        // in .NET Core — the colour would change on every launch.
        uint hash = 2166136261;
        foreach (char c in domain)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return (int)(hash % 8);
    }
}
