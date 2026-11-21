using System;

namespace Aviary;

/// <summary>
/// Beta Prep 2 — "Skip tracking redirects" (Settings → Privacy and security), what Brave calls
/// debouncing.
///
/// <para>Many links do not lead where they say. A link on Facebook, Instagram, Google's results,
/// YouTube's descriptions or Reddit goes first to that company's own redirect page — which writes
/// down that you clicked, and often who you are — and only then on to the page. Where the real
/// destination is written in the link itself, Aviary goes straight to it and the redirect page is
/// never asked for.</para>
///
/// <para><b>Only a short list of redirect pages that are known to do nothing else</b>, each
/// matched by site, host, path and the parameter that carries the destination. Deliberately NOT
/// here: link-protection services that scan a link before letting you through (Microsoft Safe
/// Links, Proofpoint's urldefense, Steam's link filter) — skipping those would remove a safety
/// check — and shorteners like t.co and bit.ly, whose links do not contain the destination at all.</para>
///
/// <para>The destination must be a complete http(s) address on a DIFFERENT site from the redirect
/// page; anything else is left exactly as it was. It is then handled like any other address the
/// tab is about to open — tracking tags taken off, HTTPS first, Shields — by the caller
/// (<c>BrowserTab.MaybeRewriteNavigation</c>). Pure, so the security harness tests the real
/// thing.</para>
/// </summary>
internal static class BounceTracking
{
    public const string SettingKey = "skipBounces";

    /// <param name="Site">The registrable domain the redirect page is on.</param>
    /// <param name="HostPrefix">What the host must start with ("l." for l.facebook.com), or null for any host of the site.</param>
    /// <param name="Path">What the path must start with.</param>
    /// <param name="Params">The parameters that can carry the destination, in the order tried.</param>
    private sealed record Wrapper(string Site, string? HostPrefix, string Path, string[] Params);

    private static readonly Wrapper[] Known =
    {
        new("facebook.com", "l.", "/l.php", new[] { "u" }),
        new("facebook.com", "lm.", "/l.php", new[] { "u" }),
        new("messenger.com", "l.", "/l.php", new[] { "u" }),
        new("instagram.com", "l.", "/", new[] { "u" }),
        new("threads.net", "l.", "/", new[] { "u" }),
        new("threads.com", "l.", "/", new[] { "u" }),
        new("youtube.com", null, "/redirect", new[] { "q" }),
        new("duckduckgo.com", null, "/l/", new[] { "uddg" }),
        new("reddit.com", "out.", "/", new[] { "url" }),
        new("umblr.com", "t.", "/redirect", new[] { "z" }),
        new("vk.com", null, "/away.php", new[] { "to" }),
        new("slack-redir.net", null, "/link", new[] { "url" }),
        new("medium.com", null, "/r/", new[] { "url" }),
        new("linkedin.com", null, "/redir/redirect", new[] { "url" }),
        new("exit.sc", null, "/", new[] { "url" }),
        new("gate.sc", null, "/", new[] { "url" }),
        new("pixiv.net", null, "/jump.php", new[] { "url" }),
    };

    /// <summary>Google's click page is the same on every country's Google: google.com, google.com.ph, google.co.uk…</summary>
    private static readonly Wrapper Google = new("google.", null, "/url", new[] { "q", "url" });

    /// <summary>
    /// True when <paramref name="url"/> is a known redirect page whose destination is written in it:
    /// <paramref name="direct"/> is that destination and <paramref name="via"/> the host skipped.
    /// </summary>
    public static bool TryUnwrap(string url, out string direct, out string via)
    {
        direct = url;
        via = string.Empty;

        if (string.IsNullOrEmpty(url) || url.Length > 8192
            || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https")
            || uri.Query.Length < 4)
        {
            return false;
        }

        string host = uri.Host;
        string site = PublicSuffix.RegistrableDomain(host);
        Wrapper? wrapper = null;

        if (site.StartsWith(Google.Site, StringComparison.OrdinalIgnoreCase)
            && (host.Equals(site, StringComparison.OrdinalIgnoreCase) || host.Equals("www." + site, StringComparison.OrdinalIgnoreCase))
            && uri.AbsolutePath.Equals(Google.Path, StringComparison.Ordinal))
        {
            wrapper = Google;
        }
        else
        {
            foreach (Wrapper known in Known)
            {
                if (known.Site.Equals(site, StringComparison.OrdinalIgnoreCase)
                    && (known.HostPrefix is null || host.StartsWith(known.HostPrefix, StringComparison.OrdinalIgnoreCase))
                    && uri.AbsolutePath.StartsWith(known.Path, StringComparison.Ordinal))
                {
                    wrapper = known;
                    break;
                }
            }
        }

        if (wrapper is null)
        {
            return false;
        }

        foreach (string name in wrapper.Params)
        {
            if (Parameter(uri.Query, name) is not { Length: > 0 } raw)
            {
                continue;
            }

            string value;
            try { value = Uri.UnescapeDataString(raw); }
            catch (UriFormatException) { continue; }

            if (value.Length > 4096
                || value.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"', '<', '>' }) >= 0
                || !Uri.TryCreate(value, UriKind.Absolute, out Uri? target)
                || target.Scheme is not ("http" or "https")
                || target.Host.Length == 0
                || target.UserInfo.Length > 0)
            {
                continue;
            }

            // A redirect page pointing back into its own site is not a bounce to another site;
            // it is that site's own business, and is left alone.
            if (PublicSuffix.RegistrableDomain(target.Host).Equals(site, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            direct = value;
            via = host;
            return true;
        }

        return false;
    }

    /// <summary>The raw (still encoded) value of the first parameter of that name, or null.</summary>
    private static string? Parameter(string query, string name)
    {
        string q = query.StartsWith('?') ? query.Substring(1) : query;
        foreach (string token in q.Split('&'))
        {
            int eq = token.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            if (token.AsSpan(0, eq).Equals(name, StringComparison.Ordinal))
            {
                return token.Substring(eq + 1);
            }
        }

        return null;
    }
}
