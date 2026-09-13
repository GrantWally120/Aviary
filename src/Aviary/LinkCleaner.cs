using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aviary;

/// <summary>
/// Removes tracking parameters from web addresses — Brave's "query string filter".
///
/// A click identifier (fbclid, gclid, msclkid…) is added to a link so that the site you land
/// on, and the ad network behind it, can tie THIS visit to THAT click and to you. It does
/// nothing for the page itself. Aviary takes them off before the request leaves, so they
/// never reach the server, the address bar, the Referer header or a script on the page.
///
/// The list is Brave's own (brave/adblock-lists, brave-lists/query-filter.json, fetched
/// 12 Sep 2026), plus the utm_* campaign family the Phase 5 plan names, plus Firefox's
/// gbraid. Brave's conditional rules are kept too: a few parameters are real identifiers on
/// unsubscribe and e-mail pages, where stripping them would break the one thing the link is
/// for, so there they stay.
///
/// Where it applies is decided in <see cref="BrowserTab"/>: top-level navigations that come
/// from another site, from the address bar or a bookmark, and cross-site redirects — never a
/// form submission. And on "Copy Link".
/// </summary>
internal static class LinkCleaner
{
    /// <summary>Stripped on every site.</summary>
    private static readonly HashSet<string> Global = new(StringComparer.OrdinalIgnoreCase)
    {
        "__hsfp", "__hssc", "__hstc", "__s", "_bhlid", "_branch_match_id", "_branch_referrer",
        "_gl", "_hsenc", "_hsmi", "_openstat", "at_recipient_id", "at_recipient_list", "bbeml",
        "bsft_clkid", "bsft_uid", "dclid", "et_rid", "fb_action_ids", "fb_comment_id", "fbclid",
        "gbraid", "gclid", "guce_referrer", "guce_referrer_sig", "hsCtaTracking", "irclickid",
        "mc_eid", "ml_subscriber", "ml_subscriber_hash", "msclkid", "mtm_cid", "oft_c", "oft_ck",
        "oft_d", "oft_id", "oft_ids", "oft_k", "oft_lk", "oft_sk", "oly_anon_id", "oly_enc_id",
        "pk_cid", "rb_clickid", "s_cid", "sc_customer", "sc_eh", "sc_uid", "sfmc_activityid",
        "sfmc_id", "sms_click", "sms_source", "sms_uph", "srsltid", "ss_email_id", "syclid",
        "ttclid", "twclid", "unicorn_click_id", "vero_conv", "vero_id", "vgo_ee", "wbraid",
        "wickedid", "yclid", "ymclid", "ysclid",
    };

    /// <summary>Every parameter starting with this — utm_source, utm_medium, utm_campaign…</summary>
    private const string CampaignPrefix = "utm_";

    /// <summary>Stripped only on these sites, where they identify who shared the link.</summary>
    private static readonly (string[] Hosts, string[] Params)[] PerSite =
    {
        (new[] { "instagram.com" }, new[] { "igsh", "igshid" }),
        (new[] { "twitter.com", "x.com" }, new[] { "ref_src", "ref_url" }),
        (new[] { "youtube.com", "youtu.be" }, new[] { "si" }),
    };

    /// <summary>Stripped everywhere EXCEPT a path that needs them (Brave's conditional rules).</summary>
    private static readonly (string Param, Regex KeepOnPath)[] Conditional =
    {
        ("ck_subscriber_id", new Regex("/unsubscribe", RegexOptions.IgnoreCase)),
        ("h_sid", new Regex("/email/", RegexOptions.IgnoreCase)),
        ("h_slt", new Regex("/email/", RegexOptions.IgnoreCase)),
        ("mkt_tok", new Regex("([uU]nsubscribe|emailWebview)")),
    };

    /// <summary>Never touched: link-protection services whose addresses are signed as a whole.</summary>
    private static readonly HashSet<string> ExemptHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "urldefense.com",
    };

    /// <summary>
    /// The address without its tracking parameters, and the names taken off. False — and the
    /// address untouched — when there was nothing to remove. Everything else in the query is
    /// kept exactly as written, in order: rebuilding it through a parser would re-encode
    /// values and could change what the site receives.
    /// </summary>
    public static bool TryClean(string url, out string cleaned, out List<string> removed)
    {
        cleaned = url;
        removed = new List<string>();

        if (string.IsNullOrEmpty(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https")
            || uri.Query.Length < 2)
        {
            return false;
        }

        string host = uri.Host;
        string site = PublicSuffix.RegistrableDomain(host);
        if (ExemptHosts.Contains(site))
        {
            return false;
        }

        // Split the raw address by hand so the fragment and every kept byte survive exactly.
        int queryStart = url.IndexOf('?');
        if (queryStart < 0)
        {
            return false;
        }

        int hashAt = url.IndexOf('#', queryStart);
        string head = url.Substring(0, queryStart);
        string query = hashAt < 0 ? url.Substring(queryStart + 1) : url.Substring(queryStart + 1, hashAt - queryStart - 1);
        string fragment = hashAt < 0 ? string.Empty : url.Substring(hashAt);

        StringBuilder kept = new();
        foreach (string token in query.Split('&'))
        {
            int eq = token.IndexOf('=');
            string rawName = eq < 0 ? token : token.Substring(0, eq);
            string name;
            try { name = Uri.UnescapeDataString(rawName.Replace('+', ' ')); }
            catch (Exception ex) { name = rawName; Diagnostics.Swallowed(ex, "link cleaner: decode parameter name"); }

            if (token.Length > 0 && IsTracking(name, site, uri.AbsolutePath))
            {
                removed.Add(name);
                continue;
            }

            if (kept.Length > 0)
            {
                kept.Append('&');
            }

            kept.Append(token);
        }

        if (removed.Count == 0)
        {
            return false;
        }

        cleaned = head + (kept.Length > 0 ? "?" + kept : string.Empty) + fragment;
        return true;
    }

    private static bool IsTracking(string name, string site, string path)
    {
        if (name.Length == 0)
        {
            return false;
        }

        if (Global.Contains(name) || name.StartsWith(CampaignPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach ((string[] hosts, string[] names) in PerSite)
        {
            if (Array.Exists(hosts, h => h.Equals(site, StringComparison.OrdinalIgnoreCase))
                && Array.Exists(names, n => n.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        foreach ((string param, Regex keepOnPath) in Conditional)
        {
            if (param.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return !keepOnPath.IsMatch(path);
            }
        }

        return false;
    }
}
