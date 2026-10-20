using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Aviary;

/// <summary>
/// Bird News (Alpha Build 5 M4.9) — Grant: <i>"add a news call it Bird News which gives live
/// accurate news from the internet always updates"</i>.
///
/// <para><b>The only part of Aviary that reaches the internet on its own.</b> Everything else
/// that goes online does so because a page asked or a person pressed something. So it is held to
/// rules the rest of the browser is not:</para>
/// <list type="bullet">
/// <item><b>Plain RSS and Atom</b> from the publishers themselves — no API key, no account, no
/// aggregator in the middle deciding what is news. The six Grant approved on 21 Sep 2026: BBC
/// World, Al Jazeera, The Guardian World, Inquirer, Rappler, GMA News. (AP was asked for and has
/// no public RSS feed any more; The Guardian took its place, at his word.)</item>
/// <item><b>Aviary asks as itself.</b> Not the Safari user agent the browser wears for pages — a
/// reader fetching for itself should not pretend to be a person's browser. No cookies are sent or
/// kept, so no site can tell one fetch from the next.</item>
/// <item><b>Headlines are never rewritten.</b> The publisher's own title, the publisher's own
/// link, when they published it. No summary, no ranking, nothing Aviary writes.</item>
/// <item><b>An unchanged feed costs one short request</b> — ETag and Last-Modified are kept and
/// sent back, and a 304 keeps the stories already held.</item>
/// <item><b>Off means off.</b> With Bird News switched off on the Start Page nothing is
/// fetched at all; nobody is reading it.</item>
/// <item>Feeds are fetched side by side with a short timeout each, so one slow or broken site
/// holds up none of the others and the Start Page never waits for any of them.</item>
/// </list>
/// <para>Modelled on FilterLists.UpdateAsync (the only other thing that downloads): atomic
/// temp-then-move writes into the profile, and a test hook — <c>AVIARY_TEST_NEWS_BASE</c> —
/// that points every shipped feed at a local server, so the whole feature is testable offline.</para>
/// </summary>
internal static class BirdsNews
{
    /// <summary>A news source. <c>SettingKey</c> is the switch in Settings → Bird News.</summary>
    public sealed record Feed(string Id, string Name, string Region, string Url, string? SettingKey);

    public sealed record Story(string Title, string Link, string Source, string Region, DateTimeOffset? Published);

    /// <summary>What happened the last time a feed was asked. <c>State</c>: ok, unchanged, failed.</summary>
    public sealed record Status(string Name, string Region, string State, string? Why, int Stories, DateTimeOffset? At, string? Note);

    public const string ShowKey = "startNews";
    public const string RefreshKey = "newsRefresh";
    public const string CustomKey = "newsCustom";

    /// <summary>The six Grant approved on 21 Sep 2026. Region decides the Start Page column.</summary>
    public static readonly Feed[] Shipped =
    {
        new("bbc", "BBC News", "world", "https://feeds.bbci.co.uk/news/world/rss.xml", "newsBbc"),
        new("aljazeera", "Al Jazeera", "world", "https://www.aljazeera.com/xml/rss/all.xml", "newsAljazeera"),
        new("guardian", "The Guardian", "world", "https://www.theguardian.com/world/rss", "newsGuardian"),
        new("inquirer", "Inquirer", "ph", "https://newsinfo.inquirer.net/feed", "newsInquirer"),
        new("rappler", "Rappler", "ph", "https://www.rappler.com/feed/", "newsRappler"),
        new("gma", "GMA News", "ph", "https://data.gmanetwork.com/gno/rss/news/feed.xml", "newsGma"),
    };

    /// <summary>How many stories each feed may hold. The Start Page shows a handful; the full page more.</summary>
    private const int PerFeed = 20;

    /// <summary>A feed bigger than this is not a headline list, and is not read.</summary>
    private const int MaxFeedBytes = 5 * 1024 * 1024;

    private static readonly TimeSpan FeedTimeout = TimeSpan.FromSeconds(15);

    private static string? TestBase => Environment.GetEnvironmentVariable("AVIARY_TEST_NEWS_BASE");

    private static string StorePath => AppPaths.Of("news.json");

    private static readonly object Gate = new();
    private static Dictionary<string, Held>? _held;
    private static int _refreshing;
    private static Timer? _timer;
    private static DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

    /// <summary>Raised (on a worker thread) whenever the stories or any feed's status change.</summary>
    public static event Action? Changed;

    /// <summary>One feed's stories and the validators that make the next request conditional.</summary>
    private sealed class Held
    {
        public string Url { get; set; } = "";
        public string Name { get; set; } = "";
        public string Region { get; set; } = "";
        public string? ETag { get; set; }
        public string? LastModified { get; set; }
        public List<Story> Stories { get; set; } = new();
        public string State { get; set; } = "never";
        public string? Why { get; set; }

        /// <summary>Anything Aviary had to correct in this feed (its mislabelled times), for Settings.</summary>
        public string? Note { get; set; }
        public DateTimeOffset? At { get; set; }
    }

    // ══ WHAT IS ON ═════════════════════════════════════════════════════════

    public static bool On => AppSettings.Get(ShowKey) != "false";

    /// <summary>
    /// Every feed to fetch now: the shipped ones switched on, then the person's own, one address a
    /// line. A shipped feed's address is replaced by the test server's when AVIARY_TEST_NEWS_BASE
    /// is set; the person's own are left alone (a test points those at the server directly).
    /// </summary>
    public static List<Feed> Enabled()
    {
        List<Feed> feeds = new();
        foreach (Feed f in Shipped)
        {
            if (f.SettingKey is not null && AppSettings.Get(f.SettingKey) == "false")
            {
                continue;
            }

            string url = TestBase is { Length: > 0 } b ? b.TrimEnd('/') + "/" + f.Id + ".xml" : f.Url;
            feeds.Add(f with { Url = url });
        }

        foreach ((string line, string? _) in CustomLines())
        {
            if (Uri.TryCreate(line, UriKind.Absolute, out Uri? u) && IsWeb(u))
            {
                feeds.Add(new Feed("custom:" + line, u.Host, "more", line, null));
            }
        }

        return feeds;
    }

    /// <summary>
    /// The person's own feed addresses with, for each, why Aviary will not use it (or null). Blank
    /// lines and lines starting with ! are notes, as in My filters.
    /// </summary>
    public static List<(string Line, string? Problem)> CustomLines()
    {
        List<(string, string?)> lines = new();
        foreach (string raw in AppSettings.Get(CustomKey).Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('!'))
            {
                continue;
            }

            string? problem = !Uri.TryCreate(line, UriKind.Absolute, out Uri? u) ? "not a web address"
                            : !IsWeb(u) ? "only http and https addresses can be read"
                            : null;
            lines.Add((line, problem));
        }

        return lines;
    }

    private static bool IsWeb(Uri u) => u.Scheme is "http" or "https";

    /// <summary>How long between checks, or null for "only when I open the Start Page".</summary>
    public static TimeSpan? Every => AppSettings.Get(RefreshKey) switch
    {
        "Every 30 minutes" => TimeSpan.FromMinutes(30),
        "Every hour" => TimeSpan.FromHours(1),
        "Only when I open the Start Page" => null,
        _ => TimeSpan.FromMinutes(15),
    };

    // ══ THE SCHEDULE ═══════════════════════════════════════════════════════

    /// <summary>
    /// Starts, restarts or stops the timer to match the settings. Called at start-up and whenever
    /// Bird News or its refresh rate changes. A few seconds' delay before the first fetch, so
    /// Aviary's own start-up is never sharing the network with six news sites.
    /// </summary>
    public static void Reschedule()
    {
        lock (Gate)
        {
            _timer?.Dispose();
            _timer = null;

            if (!On || Every is not TimeSpan every)
            {
                return;
            }

            _timer = new Timer(_ => _ = RefreshAsync(force: false), null, StartupGrace, every);
        }
    }

    private static Timer? _settle;

    /// <summary>
    /// A Bird News setting changed. The timer follows the new rate at once and every open page is
    /// told straight away — a feed switched off takes its stories with it — and a fetch follows
    /// a moment later, once the person has stopped flicking switches: six switches turned on in a
    /// row are one fetch, not six (and not five skipped because one was already running).
    /// </summary>
    public static void SettingsChanged()
    {
        Reschedule();
        Changed?.Invoke();

        lock (Gate)
        {
            _settle?.Dispose();
            _settle = On
                ? new Timer(_ => _ = RefreshAsync(force: true), null, TimeSpan.FromSeconds(1.5), Timeout.InfiniteTimeSpan)
                : null;
        }
    }

    private static int _started;

    /// <summary>Once per run, from the first window: the schedule starts. Later windows share it.</summary>
    public static void EnsureStarted()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            Reschedule();
        }
    }

    /// <summary>
    /// A Start Page was just drawn: fetch if what is held is older than the refresh rate (ten
    /// minutes when refreshing only on open). Never from a private window — the page shows what is
    /// already held, and opening a private window does not make Aviary go and fetch anything.
    /// </summary>
    public static void StartPageOpened(bool isPrivate)
    {
        if (isPrivate || !On)
        {
            return;
        }

        TimeSpan stale = Every ?? TimeSpan.FromMinutes(10);
        if (DateTimeOffset.Now - _lastRefresh < stale)
        {
            return;
        }

        // The first Start Page of a run is drawn during start-up, and a new process has never
        // fetched, so it always looks stale. Measured 21 Sep: it went straight past the six-second
        // grace Reschedule gives start-up. Inside the grace the page leaves it to the timer's first
        // tick; with no timer ("only when I open the Start Page") it waits out the grace itself.
        TimeSpan grace = StartupGrace - Diagnostics.Uptime;
        if (grace > TimeSpan.Zero)
        {
            if (Every is null)
            {
                _ = Task.Delay(grace).ContinueWith(_ => RefreshAsync(force: false), TaskScheduler.Default);
            }

            return;
        }

        _ = RefreshAsync(force: false);
    }

    /// <summary>How long after launch before Bird News may use the network.</summary>
    private static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(6);

    // ══ FETCHING ═══════════════════════════════════════════════════════════

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        HttpClient http = new(new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = FeedTimeout,
        };

        // The conventional form for a reader fetching for itself: honest about what it is, and
        // shaped like the user agents sites already expect from feed readers.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Aviary/" + BuildInfo.Version + "; Bird News)");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml;q=0.9, text/xml;q=0.8, */*;q=0.5");
        return http;
    }

    /// <summary>
    /// Fetches every enabled feed, side by side. One refresh at a time — a second call while one is
    /// running returns at once. <paramref name="force"/> is "Refresh now": the stories are fetched
    /// even if they were fetched a minute ago (still conditionally — an unchanged feed stays cheap).
    /// </summary>
    /// <summary>
    /// How soon after a check "Check now" may go back to the sites (M4.9, bug 82). On 21 Sep Grant
    /// pressed it eight times in two seconds and every press was a full round: 48 requests to six
    /// real publishers in two seconds, which is how a reader gets itself rate-limited or blocked.
    /// A press inside this window shows what was just fetched and asks nobody anything.
    /// </summary>
    private static readonly TimeSpan ManualGap = TimeSpan.FromSeconds(30);

    /// <summary>"Check now" in Settings: a forced refresh, but never twice inside <see cref="ManualGap"/>.</summary>
    public static Task CheckNowAsync()
    {
        if (DateTimeOffset.Now - _lastRefresh < ManualGap)
        {
            // Nothing new to fetch that was not fetched moments ago - but the page is waiting for an
            // answer, so it gets one.
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        return RefreshAsync(force: true);
    }

    public static async Task RefreshAsync(bool force)
    {
        if (!On || Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        try
        {
            if (!force && DateTimeOffset.Now - _lastRefresh < TimeSpan.FromMinutes(2))
            {
                return;
            }

            _lastRefresh = DateTimeOffset.Now;
            Dictionary<string, Held> held = Load();
            List<Feed> feeds = Enabled();

            await Task.WhenAll(feeds.Select(f => FetchOneAsync(f, held))).ConfigureAwait(false);

            // A feed switched off, or an address taken out of the list, stops being held.
            HashSet<string> wanted = feeds.Select(f => f.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
            lock (Gate)
            {
                foreach (string gone in held.Keys.Where(k => !wanted.Contains(k)).ToList())
                {
                    held.Remove(gone);
                }
            }

            Save(held);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "birds news: refresh");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private static async Task FetchOneAsync(Feed feed, Dictionary<string, Held> heldByUrl)
    {
        Held held;
        // Gate, not the dictionary: Snapshot reads this same dictionary under Gate while a
        // refresh runs, and two different locks would let a read meet a write.
        lock (Gate)
        {
            if (!heldByUrl.TryGetValue(feed.Url, out Held? existing))
            {
                existing = new Held { Url = feed.Url };
                heldByUrl[feed.Url] = existing;
            }

            held = existing;
        }

        held.Name = feed.Name;
        held.Region = feed.Region;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        int status = 0;
        long bytes = 0;

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, feed.Url);
            if (held.ETag is { Length: > 0 } etag)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            if (held.LastModified is { Length: > 0 } modified)
            {
                request.Headers.TryAddWithoutValidation("If-Modified-Since", modified);
            }

            using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            status = (int)response.StatusCode;

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                held.State = "unchanged";
                held.Why = null;
                held.At = DateTimeOffset.Now;
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                Fail(held, status == 403 ? "the site turned Aviary away (403)"
                         : status == 404 ? "the feed is not there any more (404)"
                         : "the site answered with an error (" + status + ")");
                return;
            }

            if (response.Content.Headers.ContentLength is long declared && declared > MaxFeedBytes)
            {
                Fail(held, "too large to be a headline feed");
                return;
            }

            byte[] body = await ReadCappedAsync(response).ConfigureAwait(false);
            bytes = body.Length;
            if (body.Length == 0)
            {
                Fail(held, "the site sent an empty feed");
                return;
            }

            List<Story> stories = Parse(body, feed, out string? note);
            if (stories.Count == 0)
            {
                Fail(held, "that address isn’t a news feed");
                return;
            }

            held.Stories = stories;
            held.ETag = response.Headers.ETag?.ToString();
            held.LastModified = response.Content.Headers.LastModified?.ToString("R");
            held.State = "ok";
            held.Why = null;
            held.Note = note;
            held.At = DateTimeOffset.Now;
        }
        catch (XmlException)
        {
            // Bad XML is the SITE's fault, and it is already in Bird News' own status and in the
            // news-fetch perf line. Reported as a hidden error it sat under "Needs attention" in
            // the summary as though Aviary were broken - bug 79's lesson, the same day.
            Fail(held, "its feed couldn’t be read");
        }
        catch (InvalidDataException)
        {
            Fail(held, "too large to be a headline feed");
        }
        catch (TaskCanceledException)
        {
            Fail(held, "no answer within " + (int)FeedTimeout.TotalSeconds + " seconds");
        }
        catch (HttpRequestException ex)
        {
            Fail(held, "couldn’t reach the site" + (ex.StatusCode is { } code ? " (" + (int)code + ")" : ""));
        }
        catch (Exception ex)
        {
            Fail(held, "its feed couldn’t be read");
            Diagnostics.Swallowed(ex, "birds news: fetch " + feed.Name);
        }
        finally
        {
            Diagnostics.Perf("news-fetch", new
            {
                feed = feed.Name,
                state = held.State,
                why = held.Why,
                status,
                bytes,
                stories = held.Stories.Count,
                ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1),
            });
        }
    }

    /// <summary>A failure keeps the stories already held: yesterday's headlines beat an empty column.</summary>
    private static void Fail(Held held, string why)
    {
        held.State = "failed";
        held.Why = why;
        held.At = DateTimeOffset.Now;
    }

    private static async Task<byte[]> ReadCappedAsync(HttpResponseMessage response)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using MemoryStream copy = new();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            if (copy.Length + read > MaxFeedBytes)
            {
                throw new InvalidDataException("feed larger than " + MaxFeedBytes + " bytes");
            }

            copy.Write(buffer, 0, read);
        }

        return copy.ToArray();
    }

    // ══ READING A FEED ═════════════════════════════════════════════════════

    /// <summary>
    /// RSS 2.0, RSS 1.0 (RDF) and Atom, matched on element LOCAL names so a namespace a publisher
    /// happens to use changes nothing. DTDs are ignored, never processed: a feed is text from the
    /// internet, and an entity expansion in one must not be able to take Aviary's memory with it.
    /// </summary>
    internal static List<Story> Parse(byte[] body, Feed feed, out string? note)
    {
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024,
            MaxCharactersInDocument = MaxFeedBytes * 2L,
            IgnoreComments = true,
        };

        XDocument doc;
        using (MemoryStream ms = new(body))
        using (XmlReader reader = XmlReader.Create(ms, settings))
        {
            doc = XDocument.Load(reader);
        }

        List<Story> stories = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (XElement item in doc.Descendants().Where(e => e.Name.LocalName is "item" or "entry"))
        {
            string title = Clean(Child(item, "title")?.Value);
            string? link = LinkOf(item);
            if (title.Length == 0 || link is null || !seen.Add(link))
            {
                continue;
            }

            DateTimeOffset? published = ParseDate(
                Child(item, "pubDate")?.Value ?? Child(item, "published")?.Value
                ?? Child(item, "updated")?.Value ?? Child(item, "date")?.Value);

            stories.Add(new Story(title, link, feed.Name, feed.Region, published));
        }

        stories = CorrectMislabelledTimes(doc, feed, stories, out note);

        return stories
            .OrderByDescending(s => s.Published ?? DateTimeOffset.MinValue)
            .Take(PerFeed)
            .ToList();
    }

    /// <summary>
    /// A story cannot have been published after its feed was fetched (M4.9, found on the first
    /// real fetch, 21 Sep 2026). Inquirer's feed stamps Philippine local time with <c>+0000</c> —
    /// "Mon, 21 Sep 2026 13:20:00 +0000" for a story published at 13:20 in Manila — while its own
    /// <c>lastBuildDate</c> in the same file says "13:34:18 +0800". Read as written, every story was
    /// eight hours in the future: all "just now", always first, pushing Rappler and GMA out of the
    /// Philippines column entirely.
    ///
    /// So: when a feed's stories come out more than ten minutes ahead of now and are labelled UTC,
    /// their clock time is read again in the zone the feed's OWN build date uses. That is not a
    /// guess — it is the publisher's own clock, from the same file. Without a usable build date, a
    /// Philippine feed is read in Philippine time; anything else keeps no time at all rather than a
    /// wrong one. Headlines are never touched. The correction is recorded and shown in Settings.
    /// </summary>
    private static List<Story> CorrectMislabelledTimes(XDocument doc, Feed feed, List<Story> stories, out string? note)
    {
        note = null;
        DateTimeOffset soon = DateTimeOffset.UtcNow.AddMinutes(10);
        if (!stories.Any(s => s.Published > soon))
        {
            return stories;
        }

        // The feed-level date: RSS <lastBuildDate>/<pubDate> on the channel, Atom <updated> on the feed.
        XElement? channel = doc.Root?.Name.LocalName == "rss"
            ? doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "channel")
            : doc.Root;
        string? built = channel is null ? null
            : (Child(channel, "lastBuildDate") ?? Child(channel, "pubDate") ?? Child(channel, "updated"))?.Value;

        TimeSpan? zone = ParseDate(built) is DateTimeOffset b && b.Offset != TimeSpan.Zero ? b.Offset
                       : feed.Region == "ph" ? TimeSpan.FromHours(8)
                       : null;

        List<Story> fixedUp = stories.Select(s =>
        {
            if (s.Published is not DateTimeOffset p || p.Offset != TimeSpan.Zero)
            {
                return s;
            }

            DateTimeOffset? corrected = zone is TimeSpan z ? new DateTimeOffset(p.DateTime, z) : null;

            // Whatever the correction, a time still in the future is not shown at all.
            return s with { Published = corrected is DateTimeOffset c && c <= soon ? c : null };
        }).ToList();

        note = zone is TimeSpan used
            ? "its times say GMT but are " + (used == TimeSpan.FromHours(8) ? "Philippine time" : "UTC" + (used < TimeSpan.Zero ? "−" : "+") + used.ToString(@"hh\:mm")) + "; shown corrected"
            : "its times are in the future, so they are not shown";
        return fixedUp;
    }

    private static XElement? Child(XElement e, string local) =>
        e.Elements().FirstOrDefault(c => c.Name.LocalName == local);

    /// <summary>The story's own web address — only ever http or https; anything else is dropped.</summary>
    private static string? LinkOf(XElement item)
    {
        // Atom: <link rel="alternate" href="…"/> (or a link with no rel at all).
        foreach (XElement l in item.Elements().Where(c => c.Name.LocalName == "link"))
        {
            string? href = (string?)l.Attribute("href");
            string rel = (string?)l.Attribute("rel") ?? "alternate";
            string candidate = href ?? l.Value.Trim();
            if (candidate.Length > 0 && rel == "alternate" && Safe(candidate) is string ok)
            {
                return ok;
            }
        }

        // RSS: a guid that is a permalink will do if there is no link.
        XElement? guid = Child(item, "guid");
        if (guid is not null && (string?)guid.Attribute("isPermaLink") != "false")
        {
            return Safe(guid.Value.Trim());
        }

        return null;
    }

    private static string? Safe(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? u) && IsWeb(u) ? u.AbsoluteUri : null;

    private static readonly Regex Tags = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex Space = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// A title as plain text. Some publishers put markup or entities in their titles; those are
    /// stripped and decoded here, and the page escapes the result again when it draws it.
    /// </summary>
    private static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string text = WebUtility.HtmlDecode(Tags.Replace(raw, " "));
        text = Space.Replace(text, " ").Trim();
        return text.Length > 300 ? text.Substring(0, 297).TrimEnd() + "…" : text;
    }

    /// <summary>The time zone names RSS dates actually carry; RFC 822 allows several.</summary>
    private static readonly Dictionary<string, string> Zones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GMT"] = "+0000", ["UT"] = "+0000", ["UTC"] = "+0000", ["Z"] = "+0000",
        ["EST"] = "-0500", ["EDT"] = "-0400", ["CST"] = "-0600", ["CDT"] = "-0500",
        ["MST"] = "-0700", ["MDT"] = "-0600", ["PST"] = "-0800", ["PDT"] = "-0700",
        ["PHT"] = "+0800", ["PHST"] = "+0800", ["SGT"] = "+0800", ["HKT"] = "+0800",
        ["BST"] = "+0100", ["CET"] = "+0100", ["CEST"] = "+0200", ["AST"] = "+0300",
    };

    private static readonly string[] Rfc822 =
    {
        "d MMM yyyy HH:mm:ss zzz", "d MMM yyyy HH:mm zzz", "d MMM yy HH:mm:ss zzz",
        "d MMM yyyy HH:mm:ss", "dd MMM yyyy HH:mm:ss zzz",
    };

    /// <summary>RFC 822 (RSS) or ISO 8601 (Atom). Null when a date cannot be read — the story is
    /// still shown, just without a time, and sorts after the ones that have one.</summary>
    internal static DateTimeOffset? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string s = raw.Trim();
        if (DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset iso)
            && !char.IsLetter(s[0]))
        {
            return iso;
        }

        // RFC 822: drop the weekday, turn a zone name into an offset, and put a colon in the offset
        // so the "zzz" pattern accepts it.
        int comma = s.IndexOf(',');
        if (comma >= 0)
        {
            s = s.Substring(comma + 1).Trim();
        }

        string[] parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 5)
        {
            string zone = parts[^1];
            if (Zones.TryGetValue(zone, out string? offset))
            {
                zone = offset;
            }

            if (zone.Length == 5 && (zone[0] == '+' || zone[0] == '-'))
            {
                zone = zone.Substring(0, 3) + ":" + zone.Substring(3);
            }

            parts[^1] = zone;
            s = string.Join(' ', parts);
        }

        return DateTimeOffset.TryParseExact(s, Rfc822, System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset rfc)
            ? rfc
            : null;
    }

    // ══ WHAT IS HELD ═══════════════════════════════════════════════════════

    private static Dictionary<string, Held> Load()
    {
        lock (Gate)
        {
            if (_held is not null)
            {
                return _held;
            }

            _held = new Dictionary<string, Held>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(StorePath))
                {
                    _held = JsonSerializer.Deserialize<Dictionary<string, Held>>(File.ReadAllText(StorePath))
                            is { } saved
                        ? new Dictionary<string, Held>(saved, StringComparer.OrdinalIgnoreCase)
                        : _held;
                }
            }
            catch (Exception ex)
            {
                // A damaged news.json costs the headlines, never the browser: the next fetch refills it.
                Diagnostics.Swallowed(ex, "birds news: load news.json");
            }

            return _held;
        }
    }

    private static void Save(Dictionary<string, Held> held)
    {
        try
        {
            string temp = StorePath + ".tmp";
            string json;
            lock (Gate)
            {
                json = JsonSerializer.Serialize(held);
            }

            File.WriteAllText(temp, json);
            File.Move(temp, StorePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "birds news: save news.json");
        }
    }

    /// <summary>
    /// Everything the Start Page and Settings draw: each column's stories newest first, and every
    /// enabled feed's last result. Only feeds still enabled are shown — a feed switched off takes
    /// its stories with it at once, not at the next fetch.
    /// </summary>
    public static object Snapshot()
    {
        Dictionary<string, Held> held = Load();
        List<Feed> enabled = Enabled();

        List<Status> statuses = new();
        List<Story> all = new();
        lock (Gate)
        {
            foreach (Feed f in enabled)
            {
                if (held.TryGetValue(f.Url, out Held? h))
                {
                    all.AddRange(h.Stories);
                    statuses.Add(new Status(f.Name, f.Region, h.State, h.Why, h.Stories.Count, h.At, h.Note));
                }
                else
                {
                    statuses.Add(new Status(f.Name, f.Region, "never", null, 0, null, null));
                }
            }
        }

        object Column(string region) => all
            .Where(s => s.Region == region)
            .OrderByDescending(s => s.Published ?? DateTimeOffset.MinValue)
            // Every story a column holds: the Bird News page shows them all, and the Start Page picks
            // a mix from them itself. Six feeds of twenty is 60 a column; 100 leaves room for your own.
            .Take(100)
            .Select(s => new
            {
                title = s.Title,
                url = s.Link,
                source = s.Source,
                at = s.Published?.ToUnixTimeMilliseconds(),
            })
            .ToList();

        DateTimeOffset? newest = statuses.Where(s => s.At is not null).Select(s => s.At).DefaultIfEmpty(null).Max();

        return new
        {
            on = On,
            world = Column("world"),
            ph = Column("ph"),
            more = Column("more"),
            feeds = statuses.Select(s => new
            {
                name = s.Name,
                region = s.Region,
                state = s.State,
                why = s.Why,
                note = s.Note,
                stories = s.Stories,
                at = s.At?.ToUnixTimeMilliseconds(),
            }),
            checkedAt = newest?.ToUnixTimeMilliseconds(),
            every = Every is TimeSpan e ? (int)e.TotalMinutes : 0,

            // Lines in "Your own feeds" Aviary will not use, and why - the My filters rule.
            problems = CustomLines()
                .Where(l => l.Problem is not null)
                .Select(l => new { line = l.Line, why = l.Problem }),
        };
    }
}
