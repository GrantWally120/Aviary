using System;
using System.Text;

namespace Aviary;

/// <summary>
/// Beta Prep 2 — Aviary's Terms of Use and Copyright notice (Grant, 21 Nov 2026: "add the legal
/// stuff ... I need approval, and I need you to ask me what stuff it is that will be in there, so
/// nothing goes wrong and no unwantedness that I did not read or allow goes through").
///
/// <para><b>EVERY WORD HERE WAS READ AND APPROVED BY GRANT BEFORE IT SHIPPED</b>
/// (he answered "Approved" on 21 November 2026; the draft he read is on his PC only). Do not change a
/// sentence without showing him the new wording first, and when the Terms change, change
/// <see cref="TermsRevision"/> too: the revision is what the "I agree" screen compares, so a new
/// one asks everybody to agree again (MainWindow.Terms.cs), which is what section 12 promises.</para>
///
/// <para>His choices: Terms of Use and a Copyright notice (no separate "not affiliated" or credits
/// page); shown as a first-launch screen that MUST be agreed to, plus Settings → Legal; the maker
/// is named as Grant, Wally Studios, with no email printed until he gives one; all rights
/// reserved, "so far".</para>
///
/// <para>Same shape as <see cref="PrivacyPolicy"/>: Settings draws it from this text as plain text
/// (never markup), and TERMS.md / COPYRIGHT.md at the repository root are the same words
/// (tools/test/release-state-test.js compares them).</para>
/// </summary>
internal static class LegalTexts
{
    public const string TermsRevision = "21 November 2026";
    public const string CopyrightRevision = "21 November 2026";

    internal sealed record Section(string title, string[] paragraphs);

    internal sealed record Document(string title, string revision, string summary, Section[] sections)
    {
        public string Markdown()
        {
            StringBuilder text = new("# " + title + "\n\nRevision: " + revision + "\n\n" + summary + "\n");
            foreach (Section section in sections)
            {
                text.Append("\n## ").Append(section.title).Append('\n');
                foreach (string paragraph in section.paragraphs) text.Append('\n').Append(paragraph).Append('\n');
            }

            return text.ToString();
        }
    }

    /// <summary>The three lines of the "I agree" screen. Each must stay true of the Terms below.</summary>
    public static readonly string[] ShortVersion =
    {
        "Aviary is free and comes as it is, with no guarantee.",
        "Your browsing data stays on this computer. Aviary has no account and sends nothing to its maker.",
        "You are responsible for what you browse and download.",
    };

    public static readonly Document Terms = new(
        "Aviary Terms of Use",
        TermsRevision,
        "Aviary is free software from Grant at Wally Studios. It comes as it is, with no guarantee. By pressing \"I agree\" or using Aviary, you accept these terms.",
        new Section[]
        {
            new("1. What these terms are", new[] {
                "These terms are the agreement between you and Grant (Wally Studios), who makes Aviary. If you do not agree with them, do not use Aviary."
            }),
            new("2. Who makes Aviary", new[] {
                "Aviary is an independent personal project made by Grant at Wally Studios. It is not a company product. For questions, use the Aviary page on GitHub: https://github.com/GrantWally120/Aviary. No email address is published in this build."
            }),
            new("3. What you may do", new[] {
                "You may install and use Aviary for free, for yourself. Aviary is still being built: this is a pre-release build, so parts of it may be unfinished, may change, or may be removed in a later build."
            }),
            new("4. What you are responsible for", new[] {
                "You are responsible for what you do with Aviary: the websites you visit, the files you download and open, and anything you post or send. Follow the law where you live and the rules of the websites you use. Do not use Aviary to harm other people, to break into computers or accounts, to spread harmful software, or to copy things you have no right to copy. Keep your Windows account and your computer protected; Aviary's data is stored there."
            }),
            new("5. If you are under 18", new[] {
                "If you are under 18, or not yet an adult where you live, use Aviary with the permission of a parent or guardian."
            }),
            new("6. Your privacy", new[] {
                "The Aviary Privacy Policy is part of these terms. In short: your browsing data stays on your computer, Aviary has no account, and Aviary sends nothing about you to its maker. Read the full policy in Settings, under Privacy Policy."
            }),
            new("7. Things Aviary does not control", new[] {
                "Aviary shows you websites, search results and news headlines that other people make. They belong to their owners, who have their own rules and privacy practices. Bird News shows headlines exactly as each news site wrote them and links to the site; the stories belong to their publishers. Aviary runs on Microsoft Edge WebView2, and its ad and tracker blocking uses lists written by their own authors. Grant does not control any of these and is not responsible for them."
            }),
            new("8. Protection has limits", new[] {
                "Shields, secure connections, private windows and Safety Check lower your risk. They cannot block every advertisement, every tracker or every harmful website or download. No browser can promise that."
            }),
            new("9. No guarantee", new[] {
                "Aviary is provided \"as is\" and \"as available\". There is no promise that it will work without errors, that it will always be available, or that it is right for any particular purpose."
            }),
            new("10. Limit of responsibility", new[] {
                "As far as the law allows, Grant and Wally Studios are not responsible for any loss or damage that comes from using Aviary or from not being able to use it. That includes lost data, lost time and problems caused by websites, downloads or other software. Nothing in these terms takes away rights the law gives you that cannot be taken away."
            }),
            new("11. Who owns Aviary", new[] {
                "Aviary, its name, its icons and its designs belong to Grant (Wally Studios). All rights reserved. The Copyright notice in Settings says what that means. Parts made by other people belong to them."
            }),
            new("12. If these terms change", new[] {
                "These terms can change as Aviary changes. When they do, the revision date at the top changes and Aviary asks you to agree again before you carry on."
            }),
            new("13. Stopping", new[] {
                "You can stop using Aviary at any time by closing it and deleting its folder. Your Aviary data is in %LOCALAPPDATA%\\Aviary and you can delete that too."
            }),
        });

    public static readonly Document Copyright = new(
        "Aviary Copyright Notice",
        CopyrightRevision,
        "Copyright © 2026 Grant (Wally Studios). All rights reserved.",
        new Section[]
        {
            new("What belongs to Grant", new[] {
                "Aviary's code, its name, its icons, its pages and its designs, including the Classic and Prism 2 looks as Aviary draws them, belong to Grant (Wally Studios). So do Grant's own projects shown in The Collection."
            }),
            new("What you may do", new[] {
                "You may download, install and use Aviary for yourself, for free. You may share the link to Aviary's official page."
            }),
            new("What you may not do without permission", new[] {
                "You may not copy, change, re-release or sell Aviary or parts of it, or use its name, icons or designs for something else, unless Grant says yes in writing. Aviary's code can be seen on GitHub so people can see how it works. Being able to see it does not make it free to reuse."
            }),
            new("What belongs to other people", new[] {
                "Some parts of Aviary were made by others and stay theirs, under their own licences: Microsoft's WebView2, Windows App SDK, Win2D and .NET, and the EasyList, EasyPrivacy and Fanboy blocking lists (Settings, Shields, Filter lists names each list's authors and licence). News headlines belong to their publishers. Names such as Safari, Windows, Brave and Google belong to their owners. Aviary is an independent project and is not connected to or approved by any of them."
            }),
            new("Asking for permission", new[] {
                "Ask through the Aviary page on GitHub: https://github.com/GrantWally120/Aviary."
            }),
        });

    /// <summary>What Settings → Legal is drawn from: both documents, and what this profile agreed to.</summary>
    public static object Describe() => new
    {
        terms = new { Terms.title, Terms.revision, Terms.summary, Terms.sections },
        copyright = new { Copyright.title, Copyright.revision, Copyright.summary, Copyright.sections },
        agreedRevision = TermsGate.AgreedRevision,
        agreedOn = TermsGate.AgreedOn,
        current = TermsGate.Accepted,
    };
}

/// <summary>
/// Whether this profile has agreed to the current Terms of Use — the one thing the "I agree" screen
/// writes down. Three settings, in settings.json on this computer and nowhere else:
/// <c>termsAccepted</c> (the revision agreed to), <c>termsAcceptedOn</c> (the day) and
/// <c>legalNoteSeen</c> (which revisions the Start Page's one-time note was last shown for).
/// PrivacyPolicy.cs says so in the policy itself.
///
/// <para><b>Nothing is created until it is accepted</b>: MainWindow.CreateTabAsync refuses while
/// <see cref="Accepted"/> is false, so there is no page, no engine request and no browsing behind
/// the screen.</para>
///
/// <para><b>The test hook.</b> A test instance cannot press a button on a native dialog through the
/// DevTools port, so <c>AVIARY_TEST_TERMS=accepted</c> answers for it — honoured ONLY together with
/// <c>AVIARY_PROFILE_DIR</c> (a scratch profile), the same rule as the filter-list and news test
/// hooks, so the normal profile can never skip the screen. It writes nothing: the scratch profile's
/// settings still say nobody agreed.</para>
/// </summary>
internal static class TermsGate
{
    public const string AcceptedKey = "termsAccepted";
    public const string AcceptedOnKey = "termsAcceptedOn";
    public const string NoteSeenKey = "legalNoteSeen";

    public static string AgreedRevision => AppSettings.Get(AcceptedKey);

    public static string AgreedOn => AppSettings.Get(AcceptedOnKey);

    public static bool Accepted =>
        string.Equals(AgreedRevision, LegalTexts.TermsRevision, StringComparison.Ordinal) || TestAccepted;

    private static bool TestAccepted =>
        string.Equals(Environment.GetEnvironmentVariable("AVIARY_TEST_TERMS"), "accepted", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AVIARY_PROFILE_DIR"));

    /// <summary>"I agree" was pressed: the revision and the day, and nothing else.</summary>
    public static void Accept()
    {
        AppSettings.Set(AcceptedKey, LegalTexts.TermsRevision);
        AppSettings.Set(AcceptedOnKey, DateTime.Now.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The Start Page's one-time note: shown once after agreeing, and once more whenever the Privacy
    /// Policy or the Terms get a new revision, so a change is never silent. Empty when there is
    /// nothing to say. A private window and a test instance that skipped the screen are never told.
    /// </summary>
    public static string NoteStamp => PrivacyPolicy.Revision + "|" + LegalTexts.TermsRevision;

    public static string NoteDue()
    {
        if (TestAccepted && AgreedRevision.Length == 0)
        {
            return string.Empty;
        }

        string seen = AppSettings.Get(NoteSeenKey);
        if (string.Equals(seen, NoteStamp, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // Never seen one: this is the note that follows "I agree". Seen an older one: something changed.
        return seen.Length == 0 ? "welcome" : "changed";
    }

    public static void NoteSeen() => AppSettings.Set(NoteSeenKey, NoteStamp);
}
