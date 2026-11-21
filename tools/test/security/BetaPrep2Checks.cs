using Aviary;

/// <summary>
/// Beta Prep 2 (21 Nov 2026) - the checks for what it added: skipping tracking redirects
/// (BounceTracking.cs), the legal documents and the record of agreeing to them (LegalTexts.cs), and
/// the rule that the Privacy Policy is read against every build (PrivacyPolicy.cs). All compiled from
/// the shipping sources; nothing here touches a real profile or the network.
/// </summary>
internal static class BetaPrep2Checks
{
    public static void Run(Action<string, bool> check)
    {
        Bounces(check);
        Legal(check);
        Agreement(check);
        News(check);
    }

    private static void Unwraps(Action<string, bool> check, string name, string url, string expected)
    {
        bool ok = BounceTracking.TryUnwrap(url, out string direct, out string via);
        check("Redirect skipped: " + name, ok && direct == expected && via.Length > 0);
    }

    private static void LeftAlone(Action<string, bool> check, string name, string url)
    {
        bool ok = BounceTracking.TryUnwrap(url, out string direct, out _);
        check("Redirect left alone: " + name, !ok && direct == url);
    }

    private static void Bounces(Action<string, bool> check)
    {
        Unwraps(check, "Google's click page", "https://www.google.com/url?q=https%3A%2F%2Fexample.com%2Fa%3Fb%3D1&sa=D", "https://example.com/a?b=1");
        Unwraps(check, "Google's click page, url=", "https://www.google.com/url?sa=t&url=https%3A%2F%2Fexample.org%2F", "https://example.org/");
        Unwraps(check, "a country's Google", "https://www.google.com.ph/url?q=https%3A%2F%2Fexample.com%2F", "https://example.com/");
        Unwraps(check, "Facebook's link page", "https://l.facebook.com/l.php?u=https%3A%2F%2Fexample.org%2Fstory&h=AT0abc", "https://example.org/story");
        Unwraps(check, "Instagram's link page", "https://l.instagram.com/?u=https%3A%2F%2Fexample.net%2F&e=x", "https://example.net/");
        Unwraps(check, "YouTube's redirect", "https://www.youtube.com/redirect?event=video_description&q=https%3A%2F%2Fexample.com%2Fshop", "https://example.com/shop");
        Unwraps(check, "DuckDuckGo's link page", "https://duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2F&rut=abc", "https://example.com/");
        Unwraps(check, "Reddit's out link", "https://out.reddit.com/t3_x?url=https%3A%2F%2Fexample.com%2F&token=t", "https://example.com/");

        // What must never be treated as a redirect, or never followed.
        LeftAlone(check, "an ordinary Google search", "https://www.google.com/search?q=https%3A%2F%2Fexample.com%2F");
        LeftAlone(check, "a click page pointing back into its own site", "https://www.google.com/url?q=https%3A%2F%2Fmaps.google.com%2F");
        LeftAlone(check, "a destination that is not a web address", "https://www.google.com/url?q=javascript%3Aalert(1)");
        LeftAlone(check, "a file destination", "https://l.facebook.com/l.php?u=file%3A%2F%2F%2FC%3A%2Fsecret.txt");
        LeftAlone(check, "a destination with a user name in it", "https://www.google.com/url?q=https%3A%2F%2Fuser%3Apass%40example.com%2F");
        LeftAlone(check, "a destination that is words, not an address", "https://www.google.com/url?q=cheap%20flights");
        LeftAlone(check, "no destination at all", "https://l.facebook.com/l.php?h=AT0abc");
        LeftAlone(check, "a link-scanning service (Microsoft Safe Links)", "https://nam01.safelinks.protection.outlook.com/?url=https%3A%2F%2Fexample.com%2F&data=x");
        LeftAlone(check, "a link-scanning service (urldefense)", "https://urldefense.com/v3/__https://example.com/__;!!abc");
        LeftAlone(check, "a look-alike host", "https://l.facebook.com.evil.example/l.php?u=https%3A%2F%2Fexample.com%2F");
        LeftAlone(check, "a site that is not on the list", "https://example.com/redirect?url=https%3A%2F%2Fexample.org%2F");
        LeftAlone(check, "not a web address", "about:blank");
    }

    private static void Legal(Action<string, bool> check)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
        bool Same(string file, string text) =>
            File.Exists(Path.Combine(root, file)) && File.ReadAllText(Path.Combine(root, file)).Replace("\r\n", "\n") == text;

        check("Published Terms of Use match Settings source", Same("TERMS.md", LegalTexts.Terms.Markdown()));
        check("Published Copyright notice matches Settings source", Same("COPYRIGHT.md", LegalTexts.Copyright.Markdown()));
        check("Terms carry their revision", LegalTexts.Terms.Markdown().Contains("Revision: " + LegalTexts.TermsRevision));
        check("Terms say Aviary comes as it is", LegalTexts.Terms.Markdown().Contains("\"as is\""));
        check("Terms name the Privacy Policy as part of them", LegalTexts.Terms.Markdown().Contains("The Aviary Privacy Policy is part of these terms."));
        check("Terms promise to ask again when they change", LegalTexts.Terms.Markdown().Contains("asks you to agree again"));
        check("Copyright notice reserves all rights", LegalTexts.Copyright.summary.Contains("All rights reserved."));
        check("Copyright notice does not claim other people's parts", LegalTexts.Copyright.Markdown().Contains("stay theirs"));
        check("The I agree screen has its three lines", LegalTexts.ShortVersion.Length == 3);

        // The policy describes what Beta Prep 2 added, and says which build it was read against.
        string policy = PrivacyPolicy.Markdown();
        check("Policy records agreeing to the Terms", policy.Contains("that you agreed, which revision of the Terms, and the date"));
        check("Policy covers the Start Page search bar", policy.Contains("search bar on the Start Page"));
        check("Policy covers Quik's timings", policy.Contains("the last startup time with and without Quik"));
        check("Policy covers site permission answers", policy.Contains("location, camera, microphone or notifications"));
        check("Policy covers skipped redirects and their limit", policy.Contains("Skip tracking redirects") && policy.Contains("can still reach the redirect page first"));
        check("Policy says which build it was read against", policy.Contains("read against Aviary " + PrivacyPolicy.ReviewedForVersion));
        check("Policy lists its changes, newest first, dated its revision", PrivacyPolicy.Changes.Length >= 2 && PrivacyPolicy.Changes[0].StartsWith(PrivacyPolicy.Revision));
    }

    private static void Agreement(Action<string, bool> check)
    {
        string? profile = Environment.GetEnvironmentVariable("AVIARY_PROFILE_DIR");
        string? hook = Environment.GetEnvironmentVariable("AVIARY_TEST_TERMS");
        try
        {
            Environment.SetEnvironmentVariable("AVIARY_PROFILE_DIR", null);
            Environment.SetEnvironmentVariable("AVIARY_TEST_TERMS", null);
            AppSettings.Set(TermsGate.AcceptedKey, "");
            AppSettings.Set(TermsGate.NoteSeenKey, "");

            check("Nobody has agreed until they agree", !TermsGate.Accepted);

            // The test hook answers the screen only for a scratch profile - never the normal one.
            Environment.SetEnvironmentVariable("AVIARY_TEST_TERMS", "accepted");
            check("The test hook alone does not skip the Terms", !TermsGate.Accepted);
            Environment.SetEnvironmentVariable("AVIARY_PROFILE_DIR", Path.GetTempPath());
            check("The test hook works with a scratch profile", TermsGate.Accepted);
            check("A test that skipped the screen is shown no note", TermsGate.NoteDue() == "");
            Environment.SetEnvironmentVariable("AVIARY_PROFILE_DIR", null);
            Environment.SetEnvironmentVariable("AVIARY_TEST_TERMS", null);

            AppSettings.Set(TermsGate.AcceptedKey, "1 January 2020");
            check("Agreeing to an older revision is not agreeing to this one", !TermsGate.Accepted);

            TermsGate.Accept();
            check("I agree records this revision", TermsGate.Accepted && TermsGate.AgreedRevision == LegalTexts.TermsRevision);
            check("I agree records a date", TermsGate.AgreedOn.Length > 0);
            check("The note after agreeing is due once", TermsGate.NoteDue() == "welcome");
            TermsGate.NoteSeen();
            check("...and not again", TermsGate.NoteDue() == "");
            AppSettings.Set(TermsGate.NoteSeenKey, "an older revision|an older revision");
            check("A changed legal page is said once more", TermsGate.NoteDue() == "changed");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AVIARY_PROFILE_DIR", profile);
            Environment.SetEnvironmentVariable("AVIARY_TEST_TERMS", hook);
        }
    }

    private static void News(Action<string, bool> check)
    {
        check("A summary is the publisher's text, without markup",
            BirdsNews.Summarise("<p>Officials said the <b>new bridge</b> will open to traffic next month after two years of work.</p>", "Bridge to open")
            == "Officials said the new bridge will open to traffic next month after two years of work.");
        check("A summary that only repeats the headline is dropped", BirdsNews.Summarise("Bridge to open", "Bridge to open") == "");
        check("A feed's read-on tail is not part of the summary",
            !BirdsNews.Summarise("The home side were held to a draw after a late equaliser in a tense derby. Continue reading...", "Derby drawn").Contains("Continue reading"));
        string longText = string.Join(" ", Enumerable.Repeat("words", 120));
        string cut = BirdsNews.Summarise(longText, "Title");
        check("A long summary is cut to a few lines, at a word", cut.Length <= 222 && cut.EndsWith("…") && !cut.EndsWith(" …"));
        check("Topics are off until switched on",
            BirdsNews.Shipped.Where(f => f.Region is "tech" or "science" or "sport").All(f => f.SettingKey is BirdsNews.TechKey or BirdsNews.ScienceKey or BirdsNews.SportKey)
            && !BirdsNews.Enabled().Any(f => f.Region is "tech" or "science" or "sport"));
        check("Every shipped feed is asked for over HTTPS", BirdsNews.Shipped.All(f => f.Url.StartsWith("https://", StringComparison.Ordinal)));
    }
}
