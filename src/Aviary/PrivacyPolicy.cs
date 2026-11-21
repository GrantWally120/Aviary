using System;
using System.Globalization;
using System.Text;

namespace Aviary;

/// <summary>
/// The offline Settings policy and the exported PRIVACY.md share this text.
///
/// <para><b>THE RULE (Grant, 21 Nov 2026: "update the privacy policy every time we do something
/// that will collect some stuff").</b> A build that starts to STORE or SEND anything it did not
/// before changes this policy IN THE SAME BUILD: the sentence that describes it, a new line at the
/// top of <see cref="Changes"/>, a new <see cref="Revision"/>, and <see cref="ReviewedForVersion"/>
/// set to that build's version. A build that changes nothing of the kind still sets
/// <see cref="ReviewedForVersion"/> — that is the proof somebody looked. tools/test/
/// release-state-test.js fails when the reviewed version is not the version being built, or when
/// the newest change is not dated the revision, so a build cannot ship on an unread policy.</para>
///
/// <para>Like the Terms (LegalTexts.cs), the wording is Grant's to approve: show him a changed
/// sentence before it ships. A new revision is said once on the Start Page (TermsGate.NoteDue).</para>
/// </summary>
internal static class PrivacyPolicy
{
    public const string Revision = "21 November 2026";

    /// <summary>The Aviary version this policy was last read against, line by line. See THE RULE above.</summary>
    public const string ReviewedForVersion = "0.5.2";

    public const string Summary = "Your browsing data and optional Aviary improvement diagnostics are stored in your local profile. This build has no Aviary account, cloud sync or automatic diagnostic email service.";
    internal sealed record Section(string title, string[] paragraphs);

    /// <summary>Newest first. Each line: the day, the build, and what about the policy changed.</summary>
    public static readonly string[] Changes =
    {
        "21 November 2026, Beta Prep 2 (0.5.2): the maker is named as Grant at Wally Studios. New: agreeing to the Terms of Use is recorded on this computer. New: the search bar on the Start Page, which follows the address bar's rules. Bird News now keeps each publisher's own short summary with its headline, has three topics that are off until switched on, and is described as fetching no pictures, which was always true. New: Quik keeps the last startup time with and without it on this computer. New: what websites may ask for can be blocked outright, and the answers you gave sites are described. New: skipping tracking redirects, and its limit.",
        "21 October 2026, Alpha Build 5 (0.5.0): the first policy.",
    };

    public static readonly Section[] Sections =
    {
        new("Who makes Aviary and what this policy covers", new[] {
            "Aviary is an independent browser project made by Grant at Wally Studios. This policy covers this Windows build and Aviary's own handling of information. Websites, search providers, news publishers, Microsoft services and optional Collection services have their own practices.",
            "For privacy questions, contact the person who supplied your copy or the maker through the Aviary repository: https://github.com/GrantWally120/Aviary. No dedicated privacy email address is configured in this build. Never put browsing logs, passwords or tokens in a public issue."
        }),
        new("What this computer keeps", new[] {
            "The normal profile is in %LOCALAPPDATA%\\Aviary. It can contain preferences, history, bookmarks, pinned tabs and groups, site exceptions, the answers you gave websites that asked to use your location, camera, microphone or notifications, cached icons, news headlines with their publishers' summaries, download records, the last startup time with and without Quik, and WebView2 cookies, cache and website storage. Downloads and pictures you choose can also be kept outside this folder.",
            "These records support the browser features you choose. They are not an Aviary advertising profile. Aviary has no code to sell these records or send them to an Aviary server in this build. Local profile files are not all encrypted by Aviary; protect your Windows account, device and backups. Optional saved passwords are handled by WebView2."
        }),
        new("Agreeing to the Terms of Use", new[] {
            "Aviary asks you to agree to its Terms of Use before you browse, and again if the Terms change. When you press I agree, Aviary saves three things in its settings file on this computer: that you agreed, which revision of the Terms, and the date. It is not sent anywhere. Resetting settings keeps this record; deleting the profile removes it."
        }),
        new("Optional improvement diagnostics", new[] {
            "Allow data collection for improvements is off by default. If you enable it, Aviary records detailed local information to find bugs and measure performance: full addresses and titles, searches, request and blocking details, feature use, settings (including your own filter rules), downloads, page errors, crashes, timings and device information. The complete sixteen-category list is in Privacy and security → What Aviary collects.",
            "Diagnostics does not collect website form submissions, cookie values or clipboard contents as separate data sources. To mask page errors, a filter checks current form field text locally inside the page, including password inputs; these field values are not separately sent to Aviary or saved as diagnostic fields. Secret masking attempts to remove login codes, tokens, credentials and card numbers from logged text. It cannot guarantee removal of every secret or personal detail that a website puts in an address, title or error. Treat this folder as sensitive.",
            "The diagnostic feature has no upload or email sender. Turning it on permits local recording only. Turning it off stops new improvement recording; existing records remain until retention removes them or you use Collected data → Delete. A separate minimal crash.log may be written even when improvement recording is off; it also uses secret masking."
        }),
        new("Retention and deletion", new[] {
            "Improvement records are retained for up to " + Diagnostics.RetentionDays + " days while Aviary performs its cleanup. Their folder is capped at " + Diagnostics.CapBytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes (about " + (Diagnostics.CapBytes / 1_000_000.0).ToString("0", CultureInfo.InvariantCulture) + " decimal MB); oldest records are removed first. Cleanup runs while Aviary is running, so a closed app cannot remove files on a calendar deadline.",
            "History, bookmarks, preferences, crash.log and site data have separate lifetimes and can remain until you clear or remove them. Privacy and security has controls for website data, browsing data and collected diagnostics; Autofill has separate password and form-entry removal; Content can forget every answer you gave a site that asked to use your location, camera, microphone or notifications. Clearing browsing data does not delete downloaded files, every setting, or external backup copies. Review each confirmation for its scope."
        }),
        new("Private windows", new[] {
            "Private pages are excluded from Aviary improvement browsing diagnostics and the saved Privacy Report. Their WebView2 session is off the record. Private Shields choices and insecure-connection exceptions expire when the last private window closes and do not change saved site choices. A private window's Start Page does not show which news stories you have opened, and search suggestions are never requested from one. Files you download, saved bookmarks and records kept by a website, your network or Windows can remain.",
            "Private browsing does not hide your IP address from websites or make you anonymous to a network provider. General app or engine failures can still leave a local crash report; private page addresses are excluded from Aviary's tab snapshots."
        }),
        new("Connections to other services", new[] {
            "Browsing sends your IP address and requests to the websites you open. Searches go to your chosen search provider, whether you type them in the address bar or in the search bar on the Start Page; nothing typed in either is sent until you press Enter, unless you switch on suggestions. Optional suggestions send partial search text as you type, from either bar; they are off by default. Bird News fetches enabled publishers' feeds, which carry headlines and short summaries; it does not fetch pictures. Its three topics (Technology, Science and Sport) are off by default and, once switched on, ask further addresses at the same publishers. Opening a story contacts its website. Site icons and services used inside Collection exhibits can also make connections.",
            "Filter updates contact only the enabled lists' approved publishers (easylist.to and secure.fanboy.co.nz), without browser cookies or browsing history. Updates are off by default and may be requested manually. Those publishers still see ordinary connection information such as an IP address and Aviary's update user agent. Invalid, redirected or oversized filter responses are rejected; the previous lists keep working."
        }),
        new("Microsoft WebView2 and SmartScreen", new[] {
            "Aviary uses Microsoft Edge WebView2. Microsoft Defender SmartScreen stays enabled to help detect phishing and harmful downloads. It can collect and send information to Microsoft under Microsoft's privacy terms. WebView2 also has required runtime diagnostics; optional runtime diagnostics follow the Windows diagnostic-data setting. These are separate from Aviary's improvement switch.",
            "Aviary configures WebView2 crash reporting to keep its engine crash dumps local instead of automatically submitting those dumps to Microsoft. This does not disable SmartScreen, required runtime diagnostics, Windows reporting or runtime updates. Microsoft describes its practices at https://learn.microsoft.com/microsoft-edge/webview2/concepts/data-privacy and https://privacy.microsoft.com/privacystatement."
        }),
        new("Protection and its limits", new[] {
            "Shields blocks matching ad and tracker requests and can hide matching elements. Link tracking protection blocks hyperlink audit pings while Shields is up. Skip tracking redirects goes straight to the address written in a link for a short list of known redirect pages, so the redirect page is not asked for; a link the browser engine has already started to open before Aviary sees it, such as one a website opens in a new tab, can still reach the redirect page first, and services that scan a link for danger are deliberately left alone. Standard prioritises compatibility; Strict also checks a site's own requests. Site exceptions, disabled lists, unsupported rule syntax and engine limitations can reduce coverage. Blocking cannot remove every advertisement or stop every form of tracking.",
            "HTTPS-Only prefers encrypted connections. Invalid or revoked certificates are cancelled and never offered as a reason to downgrade to HTTP. Requests to use your location, camera, microphone or notifications can each be set to ask, site by site, or to be blocked for every site; other website permission decisions remain subject to WebView2's controls; insecure non-loopback origins are denied and private decisions are not saved by Aviary's handler. No browser can guarantee safety from every malicious site, download or compromised device."
        }),
        new("Future Google sign-in, Bird or Birds, and reporting", new[] {
            "Google sign-in and a possible Bird or Birds account service are planned and are not implemented here. No Google account details are collected by an Aviary account system in this build. The service name is undecided.",
            "Signing in must not automatically authorise uploading local diagnostics. Before any account, email or cloud reporting feature is enabled, its operator, recipient, exact data, purpose, retention, deletion process and security design must be defined and this policy updated. Sharing diagnostic reports must require separate, informed opt-in and a way to review what will be sent. Local recording consent is not consent to send data."
        }),
        new("How this policy is kept up to date", new[] {
            "Every build of Aviary is read against this policy before it is released. When a build starts to store or send something it did not before, the policy is changed in the same build, its revision date changes, and the Start Page says so once. This revision was read against Aviary " + ReviewedForVersion + ".",
            "What changed, newest first: " + string.Join(" ", Changes)
        }),
        new("Policy changes", new[] {
            "This policy is available offline in Settings → Privacy Policy and Settings → Legal, on the screen where the Terms of Use are agreed to, and from Privacy and security and About Aviary. Its revision is " + Revision + ". A future change that sends local information elsewhere requires a revised notice and separate consent before sending."
        }),
    };

    public static object Describe() => new { revision = Revision, summary = Summary, sections = Sections };

    public static string Markdown()
    {
        StringBuilder text = new("# Aviary Privacy Policy\n\nRevision: " + Revision + "\n\n" + Summary + "\n");
        foreach (Section section in Sections)
        {
            text.Append("\n## ").Append(section.title).Append('\n');
            foreach (string paragraph in section.paragraphs) text.Append('\n').Append(paragraph).Append('\n');
        }
        return text.ToString();
    }
}
