using System;
using System.Security.Cryptography;

namespace Aviary;

/// <summary>
/// The Content-Security-Policy of Aviary's own pages (Start Page, Settings). Both are built by
/// string substitution, with page data and the person's settings written into a script block,
/// and both receive messages the host acts on. A policy that lets only the page's own script
/// run means that if some string ever reaches innerHTML unescaped, it is text, not a script.
///
/// It is a &lt;meta&gt; tag because these pages are opened with NavigateToString, which has no
/// response headers. The tag must come before the first script and only the nonce'd script
/// runs; there are no inline event handlers (script-src without 'unsafe-inline' blocks them)
/// and no eval. Styles keep 'unsafe-inline' because the pages style generated markup with
/// style attributes, which is not a route to script.
///
/// A new nonce for every page build. <see cref="Apply"/> throws when a template lost its
/// marker, so a page can never ship without the policy because someone edited the head.
/// </summary>
internal static class PageCsp
{
    public enum Page { Settings, Start }

    public const string MetaMarker = "<!--__AVIARY_CSP__-->";
    public const string NonceMarker = "__AVIARY_NONCE__";

    // The virtual hosts the Start Page may draw pictures from: favicons and the person's own
    // background (Favicons.IconBase, StartBackground.Url), and the Collection's thumbnails.
    private const string StartImageHosts = "https://aviary.localhost https://collection.aviary https://menagerie.aviary";

    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

    public static string Policy(string nonce, Page page) =>
        "default-src 'none'; "
        + $"script-src 'nonce-{nonce}'; "
        + "style-src 'unsafe-inline'; "
        + "img-src data:" + (page == Page.Start ? " " + StartImageHosts : string.Empty) + "; "
        + "connect-src 'none'; font-src 'none'; media-src 'none'; object-src 'none'; "
        + "frame-src 'none'; worker-src 'none'; manifest-src 'none'; "
        + "base-uri 'none'; form-action 'none'";

    /// <summary>
    /// Writes the policy and a fresh nonce into a page template. Call this on the template
    /// BEFORE any page data is written into it, so data containing a marker cannot be mistaken
    /// for one.
    /// </summary>
    public static string Apply(string template, Page page)
    {
        if (CountOf(template, MetaMarker) != 1)
        {
            throw new InvalidOperationException("Page template must contain exactly one CSP marker.");
        }

        if (CountOf(template, "<script nonce=\"" + NonceMarker + "\">") != 1 || CountOf(template, "<script") != 1)
        {
            throw new InvalidOperationException("Page template must have exactly one script block, carrying the nonce marker.");
        }

        if (template.IndexOf(MetaMarker, StringComparison.Ordinal) > template.IndexOf("<script", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The CSP marker must come before the script.");
        }

        string nonce = NewNonce();
        string meta = "<meta http-equiv=\"Content-Security-Policy\" content=\"" + Policy(nonce, page) + "\">";
        return template.Replace(MetaMarker, meta).Replace(NonceMarker, nonce);
    }

    private static int CountOf(string text, string needle)
    {
        int count = 0, at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
        return count;
    }
}
