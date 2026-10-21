using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.IO;

namespace Aviary;

/// <summary>What a request is, in the words filter lists use for their $script, $image… options.</summary>
[Flags]
internal enum ReqType : ushort
{
    None = 0,
    /// <summary>The page itself — a tab's top-level document. Only $document rules, in Strict.</summary>
    Document = 1 << 0,
    /// <summary>A document loaded into a frame on the page.</summary>
    Subdocument = 1 << 1,
    Script = 1 << 2,
    Image = 1 << 3,
    Stylesheet = 1 << 4,
    Xhr = 1 << 5,
    Media = 1 << 6,
    Font = 1 << 7,
    Ping = 1 << 8,
    WebSocket = 1 << 9,
    Object = 1 << 10,
    Other = 1 << 11,
    Popup = 1 << 12,

    /// <summary>What a rule with no type options applies to: everything but the page and pop-ups.</summary>
    Resources = Subdocument | Script | Image | Stylesheet | Xhr | Media | Font | Ping | WebSocket | Object | Other,
}

/// <summary>
/// The ad blocker's rule engine (Phase 5, Alpha Build 5 M3): EasyList and EasyPrivacy, read
/// as they are published, matched against every request a page makes.
///
/// <para><b>What it reads.</b> The network rules that carry nearly all of the lists' value:
/// <c>||host^</c> domain anchors, <c>|</c> start and end anchors, <c>*</c> and <c>^</c>, plain
/// substrings, <c>@@</c> exceptions, and the options <c>$third-party</c>/<c>$first-party</c>,
/// the request types (<c>script image stylesheet xmlhttprequest subdocument ping media font
/// websocket object other document</c>, negated too), <c>$domain=</c> (with <c>~</c> and
/// <c>name.*</c>), <c>$important</c>, <c>$match-case</c>, and the page-level exceptions
/// <c>$document $elemhide $generichide</c>. Element hiding (<c>##</c>, <c>#@#</c>) is compiled by
/// <see cref="FilterEngine.Cosmetic"/>.</para>
///
/// <para><b>What it deliberately skips</b>, counted in <see cref="Stats"/> rather than guessed at:
/// regular-expression rules (31 of ~111,000 — they cannot be indexed and would run on every
/// request), <c>$popup</c> (a pop-up is not a request), <c>$csp</c>, <c>$redirect</c>,
/// <c>$rewrite</c>, <c>$removeparam</c>, and any option it does not know. A rule it does not
/// understand is never applied: a guess at a blocking rule is how a browser breaks a site.</para>
///
/// <para><b>How a request is matched.</b> 98,700 of the rules are just a host — <c>||ads.example^</c>
/// — so those sit in hash sets and a request is checked by walking its host's labels
/// (<c>a.b.example.com</c>, <c>b.example.com</c>, <c>example.com</c>): a handful of lookups,
/// no allocation. Every other rule is filed under ONE literal token that any URL it matches
/// must contain as a whole word — the rarest such token it has, so no bucket fills up with
/// rules — and a request looks up each of its own URL's words. Exceptions are only consulted
/// once something has matched. The engine is immutable once built; a list update builds a
/// new one and swaps it in (<see cref="FilterLists"/>).</para>
///
/// <para>The request side runs on the UI thread for every request a page makes, so it is
/// written to allocate as little as possible: span lookups into the indexes, the host walked
/// in place, the URL lowercased once.</para>
/// </summary>
internal sealed partial class FilterEngine
{
    [Flags]
    private enum RuleFlags : ushort
    {
        None = 0,
        Exception = 1 << 0,
        Important = 1 << 1,
        HostAnchor = 1 << 2,
        StartAnchor = 1 << 3,
        EndAnchor = 1 << 4,
        ThirdParty = 1 << 5,
        FirstParty = 1 << 6,
        MatchCase = 1 << 7,
        // Page-level exceptions: they switch something off for a whole page, and are never
        // matched against the page's requests.
        DocumentExc = 1 << 8,
        ElemHide = 1 << 9,
        GenericHide = 1 << 10,
    }

    /// <summary>One network rule, parsed.</summary>
    private sealed class NetRule
    {
        public required string Text;
        /// <summary>Anchors and redundant leading/trailing <c>*</c> removed; lowercase unless MatchCase.</summary>
        public required string Pattern;
        public RuleFlags Flags;
        public ReqType Types;
        public string[]? Include;
        public string[]? Exclude;
        public byte List;

        public bool Is(RuleFlags f) => (Flags & f) != 0;
    }

    /// <summary>The counts Settings shows and diagnostics records — what was read, and what was not.</summary>
    public sealed class ListStats
    {
        public int Lines;
        public int Network;
        public int NetworkExceptions;
        public int HostOnly;
        public int CosmeticGeneric;
        public int CosmeticSpecific;
        public int CosmeticExceptions;
        public int PageExceptions;
        public int Skipped;
        public readonly Dictionary<string, int> SkippedBy = new(StringComparer.Ordinal);

        /// <summary>Rules read from each list, by its name (M4.5: Settings shows what each list brought).</summary>
        public readonly Dictionary<string, int> RulesByList = new(StringComparer.Ordinal);

        /// <summary>Lines skipped in each list, by its name.</summary>
        public readonly Dictionary<string, int> SkippedByList = new(StringComparer.Ordinal);

        /// <summary>Up to three rules per reason, so "skipped" can always be checked against real lines.</summary>
        public readonly Dictionary<string, List<string>> SkippedExamples = new(StringComparer.Ordinal);

        /// <summary>The list being read, so a rule or a skip can be counted against it.</summary>
        public string Reading = string.Empty;

        public void Rule()
        {
            RulesByList[Reading] = RulesByList.TryGetValue(Reading, out int n) ? n + 1 : 1;
        }

        public void Skip(string why, string? rule = null)
        {
            SkippedByList[Reading] = SkippedByList.TryGetValue(Reading, out int sn) ? sn + 1 : 1;
            Skipped++;
            SkippedBy[why] = SkippedBy.TryGetValue(why, out int n) ? n + 1 : 1;
            if (rule is not null)
            {
                if (!SkippedExamples.TryGetValue(why, out List<string>? list))
                {
                    SkippedExamples[why] = list = new List<string>(3);
                }

                if (list.Count < 3)
                {
                    list.Add(rule);
                }
            }
        }
    }

    /// <summary>One filter list as it came in: its name (for diagnostics) and its lines.</summary>
    public sealed record Source(string Name, string Text);

    /// <summary>The name "My filters" carries in the engine, in diagnostics and in Settings.</summary>
    public const string MineName = "My filters";

    /// <summary>
    /// Why Aviary cannot use this line, or null when it can (M4.5, "My filters"). The line goes
    /// through the real parser - a one-line engine costs microseconds to build - so the answer
    /// is the same one a list gets, never a second opinion that could drift from it.
    /// </summary>
    public static string? Problem(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] == '!' || trimmed[0] == '[' || trimmed[0] == '#' && !trimmed.StartsWith("##", StringComparison.Ordinal))
        {
            // Blank, a comment, or "# note" the way hosts files write one: nothing to use, and
            // nothing wrong either.
            return null;
        }

        // The parser takes any text as a pattern to look for inside an address, exactly as Adblock
        // Plus does - so a sentence typed by mistake is a "rule" that can never match anything.
        // In a list that hardly matters; in a box a person types into, it has to be said.
        bool hides = CosmeticBuilder.TrySplit(trimmed, out string _, out string _, out string _);
        if (!hides)
        {
            if (trimmed.IndexOf(' ') >= 0)
            {
                return "a rule that blocks cannot contain spaces";
            }

            bool anything = false;
            foreach (char c in trimmed)
            {
                if (char.IsLetterOrDigit(c))
                {
                    anything = true;
                    break;
                }
            }

            if (!anything)
            {
                return "there is nothing here to match";
            }
        }

        ListStats s;
        try
        {
            s = Build(new[] { new Source("check", trimmed) }).Stats;
        }
        catch (Exception)
        {
            return "Aviary could not read this line";
        }

        if (s.SkippedBy.Count > 0)
        {
            return s.SkippedBy.Keys.First();
        }

        int kept = s.Network + s.NetworkExceptions + s.CosmeticGeneric + s.CosmeticSpecific
                 + s.CosmeticExceptions + s.PageExceptions;
        return kept > 0 ? null : "not a rule Aviary can use";
    }

    /// <summary>The result of matching one request.</summary>
    public readonly record struct Verdict(bool Blocked, string? Rule, string? List)
    {
        public static readonly Verdict None = new(false, null, null);
    }

    /// <summary>The page a request belongs to — computed once per navigation, not per request.</summary>
    public sealed class Page
    {
        public static readonly Page Empty = new(string.Empty);

        /// <summary>Lowercase host of the tab's top-level document.</summary>
        public string Host { get; }

        /// <summary>Its registrable domain — what "first party" means.</summary>
        public string Site { get; }

        /// <summary>"google.*" for www.google.co.uk: what a <c>$domain=google.*</c> matches.</summary>
        public string Entity { get; }

        /// <summary>
        /// Set when a <c>@@…$document</c> exception covers this page: the lists themselves say to
        /// leave it alone (local addresses, a handful of sites the list keepers have allowed).
        /// </summary>
        public bool Allowed { get; init; }

        public Page(string host)
        {
            Host = host.ToLowerInvariant();
            Site = Host.Length == 0 ? string.Empty : SiteOf(Host);
            Entity = EntityOf(Host);
        }
    }

    private readonly string[] _listNames;

    // Host-only rules: the host itself, nothing else. Plain ones need no object at all.
    private readonly HashSet<string> _plainHosts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte> _plainHostList = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<NetRule>> _hostRules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<NetRule>> _hostExceptions = new(StringComparer.Ordinal);

    // Everything else, filed under one token each.
    private readonly Dictionary<string, List<NetRule>> _tokenRules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<NetRule>> _tokenExceptions = new(StringComparer.Ordinal);
    private readonly List<NetRule> _anyRules = new();
    private readonly List<NetRule> _anyExceptions = new();
    private readonly List<NetRule> _important = new();
    private readonly List<NetRule> _pageExceptions = new();

    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _plainHostsBySpan;
    private readonly Dictionary<string, byte>.AlternateLookup<ReadOnlySpan<char>> _plainHostListBySpan;
    private readonly Dictionary<string, List<NetRule>>.AlternateLookup<ReadOnlySpan<char>> _hostRulesBySpan;
    private readonly Dictionary<string, List<NetRule>>.AlternateLookup<ReadOnlySpan<char>> _hostExceptionsBySpan;
    private readonly Dictionary<string, List<NetRule>>.AlternateLookup<ReadOnlySpan<char>> _tokenRulesBySpan;
    private readonly Dictionary<string, List<NetRule>>.AlternateLookup<ReadOnlySpan<char>> _tokenExceptionsBySpan;

    public ListStats Stats { get; } = new();

    /// <summary>How long building took, for diagnostics' performance log.</summary>
    public double BuildMs { get; private set; }

    private FilterEngine(IReadOnlyList<Source> sources)
    {
        _listNames = new string[sources.Count];
        _plainHostsBySpan = _plainHosts.GetAlternateLookup<ReadOnlySpan<char>>();
        _plainHostListBySpan = _plainHostList.GetAlternateLookup<ReadOnlySpan<char>>();
        _hostRulesBySpan = _hostRules.GetAlternateLookup<ReadOnlySpan<char>>();
        _hostExceptionsBySpan = _hostExceptions.GetAlternateLookup<ReadOnlySpan<char>>();
        _tokenRulesBySpan = _tokenRules.GetAlternateLookup<ReadOnlySpan<char>>();
        _tokenExceptionsBySpan = _tokenExceptions.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>Builds an engine from the lists' text. Slow (a few hundred ms) — never on the UI thread.</summary>
    public static FilterEngine Build(IReadOnlyList<Source> sources)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        FilterEngine engine = new(sources);
        List<NetRule> indexed = new();
        CosmeticBuilder cosmetic = new();

        for (int i = 0; i < sources.Count; i++)
        {
            engine._listNames[i] = sources[i].Name;
            engine.Stats.Reading = sources[i].Name;
            using StringReader reader = new(sources[i].Text);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                engine.Stats.Lines++;
                string trimmed = line.Trim();
                if (trimmed.Length > 0 && trimmed[0] != '!' && trimmed[0] != '[')
                {
                    engine.Stats.Rule();
                }

                engine.ParseLine(trimmed, (byte)i, indexed, cosmetic);
            }
        }

        engine.IndexByToken(indexed);
        engine.Cosmetic = cosmetic.Build(engine.Stats, engine._pageExceptions);
        engine.BuildMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return engine;
    }

    // ══ PARSING ══════════════════════════════════════════════════════════════

    private void ParseLine(string line, byte list, List<NetRule> indexed, CosmeticBuilder cosmetic)
    {
        if (line.Length == 0 || line[0] == '!' || line[0] == '[')
        {
            return;
        }

        if (CosmeticBuilder.TrySplit(line, out string domains, out string separator, out string body))
        {
            cosmetic.Add(line, domains, separator, body, Stats);
            return;
        }

        NetRule? rule = ParseNetwork(line, list);
        if (rule is null)
        {
            return;
        }

        if (rule.Is(RuleFlags.DocumentExc | RuleFlags.ElemHide | RuleFlags.GenericHide))
        {
            _pageExceptions.Add(rule);
            Stats.PageExceptions++;

            // "@@||site^$document" also lets every request on that page through, which the
            // page-level check handles; it is not also a request rule.
            return;
        }

        if (rule.Is(RuleFlags.Exception)) { Stats.NetworkExceptions++; } else { Stats.Network++; }

        if (rule.Is(RuleFlags.Important) && !rule.Is(RuleFlags.Exception))
        {
            _important.Add(rule);
            return;
        }

        // ||host^ and nothing else: the host table.
        if (IsHostOnly(rule, out string? host))
        {
            Stats.HostOnly++;
            bool plain = rule.Include is null && rule.Exclude is null
                         && (rule.Flags & ~(RuleFlags.HostAnchor | RuleFlags.Exception)) == 0
                         && rule.Types == ReqType.Resources;

            if (plain && !rule.Is(RuleFlags.Exception))
            {
                if (_plainHosts.Add(host!))
                {
                    _plainHostList[host!] = list;
                }

                return;
            }

            Dictionary<string, List<NetRule>> table = rule.Is(RuleFlags.Exception) ? _hostExceptions : _hostRules;
            if (!table.TryGetValue(host!, out List<NetRule>? bucket))
            {
                table[host!] = bucket = new List<NetRule>(1);
            }

            bucket.Add(rule);
            return;
        }

        indexed.Add(rule);
    }

    private static readonly char[] OptionChars = "abcdefghijklmnopqrstuvwxyz0123456789-_~=|.*,".ToCharArray();

    private NetRule? ParseNetwork(string line, byte list)
    {
        string text = line;
        RuleFlags flags = RuleFlags.None;

        if (line.StartsWith("@@", StringComparison.Ordinal))
        {
            flags |= RuleFlags.Exception;
            line = line.Substring(2);
        }

        // Options are a trailing "$a,b=c" — but '$' also turns up inside URL patterns, so it
        // only counts when everything after it looks like options.
        string pattern = line;
        string? options = null;
        int dollar = line.LastIndexOf('$');
        if (dollar >= 0)
        {
            string tail = line.Substring(dollar + 1);
            if (tail.Length > 0 && LooksLikeOptions(tail))
            {
                pattern = line.Substring(0, dollar);
                options = tail;
            }
        }

        // A regular expression: /…/ — not indexable, so not read (see the class notes).
        if (pattern.Length >= 2 && pattern[0] == '/' && pattern[^1] == '/')
        {
            Stats.Skip("regular expression", text);
            return null;
        }

        ReqType types = ReqType.None;
        ReqType notTypes = ReqType.None;
        string[]? include = null;
        string[]? exclude = null;

        if (options is not null)
        {
            foreach (string raw in options.Split(','))
            {
                string option = raw.Trim().ToLowerInvariant();
                if (option.Length == 0)
                {
                    continue;
                }

                bool negated = option[0] == '~';
                string name = negated ? option.Substring(1) : option;
                string? value = null;
                int eq = name.IndexOf('=');
                if (eq >= 0)
                {
                    value = name.Substring(eq + 1);
                    name = name.Substring(0, eq);
                }

                ReqType type = TypeOf(name);
                if (type != ReqType.None && value is null)
                {
                    if (negated) { notTypes |= type; } else { types |= type; }
                    continue;
                }

                switch (name)
                {
                    case "third-party" or "3p":
                        flags |= negated ? RuleFlags.FirstParty : RuleFlags.ThirdParty;
                        break;
                    case "first-party" or "1p":
                        flags |= negated ? RuleFlags.ThirdParty : RuleFlags.FirstParty;
                        break;
                    case "domain" or "from" when value is not null && !negated:
                        SplitDomains(value, out include, out exclude);
                        break;
                    case "important" when !negated:
                        flags |= RuleFlags.Important;
                        break;
                    case "match-case" when !negated:
                        flags |= RuleFlags.MatchCase;
                        break;
                    case "document" or "doc" when !negated && (flags & RuleFlags.Exception) != 0:
                        flags |= RuleFlags.DocumentExc;
                        break;
                    case "document" or "doc" when !negated:
                        // A blocking $document: the whole site, as a page (Strict) or a frame.
                        types |= ReqType.Document;
                        break;
                    case "document" or "doc":
                        // ~document: a page is never among what a plain rule covers anyway.
                        notTypes |= ReqType.Document;
                        break;
                    case "elemhide" or "ehide" when !negated && (flags & RuleFlags.Exception) != 0:
                        flags |= RuleFlags.ElemHide;
                        break;
                    case "generichide" or "ghide" when !negated && (flags & RuleFlags.Exception) != 0:
                        flags |= RuleFlags.GenericHide;
                        break;
                    case "all" when !negated:
                        types |= ReqType.Resources | ReqType.Document;
                        break;
                    case "popup":
                        // A pop-up is a window, not a request; a rule for pop-ups alone does
                        // nothing here. Combined with real types, the pop-up part is dropped.
                        types |= ReqType.Popup;
                        break;
                    default:
                        // $csp, $redirect, $rewrite, $removeparam, $badfilter, $denyallow,
                        // $method, $header… — anything not read is not applied.
                        Stats.Skip("$" + name, text);
                        return null;
                }
            }
        }

        // $document on a blocking rule: the page itself (Strict only) and frames.
        if ((types & ReqType.Document) != 0 && (flags & RuleFlags.Exception) == 0)
        {
            types |= ReqType.Subdocument;
        }

        if ((flags & (RuleFlags.DocumentExc | RuleFlags.ElemHide | RuleFlags.GenericHide)) != 0)
        {
            types = ReqType.Document;
        }
        else if (types == ReqType.None)
        {
            types = notTypes == ReqType.None ? ReqType.Resources : ReqType.Resources & ~notTypes;
        }
        else
        {
            types &= ~notTypes;
        }

        types &= ~ReqType.Popup;
        if (types == ReqType.None)
        {
            Stats.Skip("$popup", text);
            return null;
        }

        // ── the pattern ──
        if (pattern.StartsWith("||", StringComparison.Ordinal))
        {
            flags |= RuleFlags.HostAnchor;
            pattern = pattern.Substring(2);
        }
        else if (pattern.StartsWith('|'))
        {
            flags |= RuleFlags.StartAnchor;
            pattern = pattern.Substring(1);
        }

        if (pattern.EndsWith('|'))
        {
            flags |= RuleFlags.EndAnchor;
            pattern = pattern.Substring(0, pattern.Length - 1);
        }

        // A leading or trailing wildcard means the anchor on that side means nothing.
        if (pattern.StartsWith('*'))
        {
            flags &= ~(RuleFlags.HostAnchor | RuleFlags.StartAnchor);
            pattern = pattern.TrimStart('*');
        }

        if (pattern.EndsWith('*'))
        {
            flags &= ~RuleFlags.EndAnchor;
            pattern = pattern.TrimEnd('*');
        }

        if ((flags & RuleFlags.MatchCase) == 0)
        {
            pattern = pattern.ToLowerInvariant();
        }

        // A rule that would match every request of every kind on every site is a list error, not
        // a filter. With a type or a party it is deliberate: "*$ping,third-party" stops every
        // other site's pings.
        if (pattern.Length == 0 && include is null && types == ReqType.Resources
            && (flags & (RuleFlags.ThirdParty | RuleFlags.FirstParty)) == 0
            && (flags & (RuleFlags.DocumentExc | RuleFlags.ElemHide | RuleFlags.GenericHide)) == 0)
        {
            Stats.Skip("matches everything", text);
            return null;
        }

        return new NetRule
        {
            Text = text,
            Pattern = pattern,
            Flags = flags,
            Types = types,
            Include = include,
            Exclude = exclude,
            List = list,
        };
    }

    private static bool LooksLikeOptions(string tail)
    {
        foreach (char c in tail)
        {
            char l = char.ToLowerInvariant(c);
            if (Array.IndexOf(OptionChars, l) < 0 && c != '/' && c != ' ' && c != '\'' && c != ';' && c != ':')
            {
                return false;
            }
        }

        return true;
    }

    private static ReqType TypeOf(string name) => name switch
    {
        "script" => ReqType.Script,
        "image" => ReqType.Image,
        "stylesheet" or "css" => ReqType.Stylesheet,
        "xmlhttprequest" or "xhr" => ReqType.Xhr,
        "subdocument" or "frame" => ReqType.Subdocument,
        "media" => ReqType.Media,
        "font" => ReqType.Font,
        "ping" or "beacon" => ReqType.Ping,
        "websocket" => ReqType.WebSocket,
        "object" or "object-subrequest" => ReqType.Object,
        "other" => ReqType.Other,
        _ => ReqType.None,
    };

    /// <summary>"a.com|~b.a.com|c.*" into the domains a rule is for, and the ones it is not.</summary>
    private static void SplitDomains(string value, out string[]? include, out string[]? exclude)
    {
        List<string>? inc = null, exc = null;
        foreach (string raw in value.Split('|'))
        {
            string d = raw.Trim().ToLowerInvariant();
            if (d.Length == 0 || d == "~")
            {
                continue;
            }

            if (d[0] == '~') { (exc ??= new()).Add(d.Substring(1)); } else { (inc ??= new()).Add(d); }
        }

        include = inc?.ToArray();
        exclude = exc?.ToArray();
    }

    private static bool IsHostChar(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' or '_';

    /// <summary>True for "||ads.example.com^" — a host, the separator, nothing more.</summary>
    private static bool IsHostOnly(NetRule rule, out string? host)
    {
        host = null;
        if ((rule.Flags & (RuleFlags.HostAnchor | RuleFlags.StartAnchor | RuleFlags.EndAnchor)) != RuleFlags.HostAnchor)
        {
            return false;
        }

        string p = rule.Pattern;
        if (p.Length < 3 || p[^1] != '^')
        {
            return false;
        }

        for (int i = 0; i < p.Length - 1; i++)
        {
            if (!IsHostChar(p[i]))
            {
                return false;
            }
        }

        // "||example.com^" must start at a label, not in the middle of one — the table walk
        // only ever tries whole labels, which is exactly that.
        host = p.Substring(0, p.Length - 1).TrimEnd('.');
        return host.Length > 0 && host[0] != '.';
    }

    // ══ THE TOKEN INDEX ═════════════════════════════════════════════════════

    private static bool IsTokenChar(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '%';

    private const int MinToken = 2;
    private const int MaxToken = 48;

    /// <summary>
    /// The tokens a rule's pattern guarantees: runs of [a-z0-9%] that any matching URL must
    /// contain as a WHOLE word. A run touching a wildcard, or an unanchored end of the pattern,
    /// may be only part of a URL's word ("ad" at the start of "/ad-" could be the end of "bad"),
    /// so it cannot be used.
    /// </summary>
    private static List<string> TokensOf(NetRule rule)
    {
        List<string> tokens = new();
        string p = rule.Pattern.ToLowerInvariant();
        int i = 0;
        while (i < p.Length)
        {
            if (!IsTokenChar(p[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < p.Length && IsTokenChar(p[i]))
            {
                i++;
            }

            bool leftOk = start > 0
                ? p[start - 1] != '*'
                : rule.Is(RuleFlags.HostAnchor | RuleFlags.StartAnchor);
            bool rightOk = i < p.Length
                ? p[i] != '*'
                : rule.Is(RuleFlags.EndAnchor);

            int length = i - start;
            if (leftOk && rightOk && length is >= MinToken and <= MaxToken)
            {
                tokens.Add(p.Substring(start, length));
            }
        }

        return tokens;
    }

    /// <summary>
    /// Files each rule under its RAREST usable token, counted across all the rules — so a
    /// request's common words ("com", "https", "js") lead to small buckets, and no bucket ends
    /// up holding thousands of rules that every request would have to try.
    /// </summary>
    private void IndexByToken(List<NetRule> rules)
    {
        List<List<string>> tokens = new(rules.Count);
        Dictionary<string, int> frequency = new(StringComparer.Ordinal);
        foreach (NetRule rule in rules)
        {
            List<string> t = TokensOf(rule);
            tokens.Add(t);
            foreach (string token in t)
            {
                frequency[token] = frequency.TryGetValue(token, out int n) ? n + 1 : 1;
            }
        }

        for (int i = 0; i < rules.Count; i++)
        {
            NetRule rule = rules[i];
            string? best = null;
            int bestCount = int.MaxValue;
            foreach (string token in tokens[i])
            {
                int n = frequency[token] + (Common.Contains(token) ? 100_000 : 0);
                if (n < bestCount || (n == bestCount && best is not null && token.Length > best.Length))
                {
                    best = token;
                    bestCount = n;
                }
            }

            bool exception = rule.Is(RuleFlags.Exception);
            if (best is null)
            {
                (exception ? _anyExceptions : _anyRules).Add(rule);
                continue;
            }

            Dictionary<string, List<NetRule>> index = exception ? _tokenExceptions : _tokenRules;
            if (!index.TryGetValue(best, out List<NetRule>? bucket))
            {
                index[best] = bucket = new List<NetRule>(2);
            }

            bucket.Add(rule);
        }
    }

    /// <summary>Words nearly every URL has. A rule filed under one would be tried on every request.</summary>
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "http", "https", "www", "com", "net", "org", "js", "html", "htm", "php", "css", "png",
        "jpg", "gif", "static", "cdn", "assets", "img", "images", "api", "v1", "v2", "min",
    };

    // ══ MATCHING ════════════════════════════════════════════════════════════

    /// <summary>
    /// The verdict for one request. <paramref name="thirdPartyOnly"/> is Standard mode: a
    /// request to the page's own site is never blocked by the lists.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public Verdict Match(string url, ReqType type, Page page, bool thirdPartyOnly, string? fallbackHost = null)
    {
        if (page.Allowed || url.Length < 8)
        {
            return Verdict.None;
        }

        string lower = url.ToLowerInvariant();
        if (!TryHost(lower, out int hostStart, out int hostEnd))
        {
            return Verdict.None;
        }

        ReadOnlySpan<char> host = lower.AsSpan(hostStart, hostEnd - hostStart);

        // Standard: the lists only ever stop OTHER sites. (Brave's standard mode.)
        int party = 0; // 0 unknown, 1 first, 3 third
        if (thirdPartyOnly && type != ReqType.Document)
        {
            party = IsThirdParty(host, page) ? 3 : 1;
            if (party == 1)
            {
                return Verdict.None;
            }
        }

        Request r = new(url, lower, hostStart, hostEnd, type, page, party);

        // $important blocks even where an exception would let the request through. A handful
        // of rules, so tried first and outright.
        if (_important.Count > 0 && FindImportant(ref r) is { } important)
        {
            return Blocked(important);
        }

        NetRule? hit = FindBlock(ref r, out string? plainHost);
        if (hit is null && plainHost is null && fallbackHost is null)
        {
            return Verdict.None;
        }

        if (FindException(ref r) is not null)
        {
            return Verdict.None;
        }

        return hit is not null
            ? Blocked(hit)
            : plainHost is not null
                ? new Verdict(true, "||" + plainHost + "^", NameOf(_plainHostList.TryGetValue(plainHost, out byte l) ? l : (byte)0))
                : new Verdict(true, "||" + fallbackHost + "^", "Aviary");
    }

    private Verdict Blocked(NetRule rule) => new(true, rule.Text, NameOf(rule.List));

    private string NameOf(byte list) => list < _listNames.Length ? _listNames[list] : "?";

    /// <summary>Everything the options need about one request, worked out at most once.</summary>
    private ref struct Request
    {
        public readonly string Url;
        public readonly string Lower;
        public readonly int HostStart;
        public readonly int HostEnd;
        public readonly ReqType Type;
        public readonly Page Page;
        /// <summary>0 not yet known, 1 first-party, 3 third-party.</summary>
        public int Party;

        public Request(string url, string lower, int hostStart, int hostEnd, ReqType type, Page page, int party)
        {
            Url = url; Lower = lower; HostStart = hostStart; HostEnd = hostEnd; Type = type; Page = page; Party = party;
        }

        public readonly ReadOnlySpan<char> Host => Lower.AsSpan(HostStart, HostEnd - HostStart);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private NetRule? FindBlock(ref Request r, out string? plainHost)
    {
        plainHost = null;
        ReadOnlySpan<char> host = r.Host;

        // The host and each parent: a.b.example.com, b.example.com, example.com, com.
        int at = 0;
        while (at < host.Length)
        {
            ReadOnlySpan<char> suffix = host.Slice(at);

            if ((r.Type & ReqType.Resources) != 0 && _plainHostsBySpan.Contains(suffix))
            {
                plainHost = suffix.ToString();
                return null;
            }

            if (_hostRulesBySpan.TryGetValue(suffix, out List<NetRule>? rules))
            {
                foreach (NetRule rule in rules)
                {
                    if (Applies(rule, ref r))
                    {
                        return rule;
                    }
                }
            }

            int dot = IndexOfDot(suffix);
            if (dot < 0)
            {
                break;
            }

            at += dot + 1;
        }

        return FindIndexed(ref r, _tokenRulesBySpan, _anyRules);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private NetRule? FindException(ref Request r)
    {
        ReadOnlySpan<char> host = r.Host;
        int at = 0;
        while (at < host.Length)
        {
            ReadOnlySpan<char> suffix = host.Slice(at);
            if (_hostExceptionsBySpan.TryGetValue(suffix, out List<NetRule>? rules))
            {
                foreach (NetRule rule in rules)
                {
                    if (Applies(rule, ref r))
                    {
                        return rule;
                    }
                }
            }

            int dot = IndexOfDot(suffix);
            if (dot < 0)
            {
                break;
            }

            at += dot + 1;
        }

        return FindIndexed(ref r, _tokenExceptionsBySpan, _anyExceptions);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private NetRule? FindImportant(ref Request r)
    {
        foreach (NetRule rule in _important)
        {
            if (Applies(rule, ref r))
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>
    /// For the filters harness: every token of the address that has a bucket, and how many rules
    /// the bucket holds — how much work a Match on it does (M4.5, the slow YouTube checks).
    /// </summary>
    internal List<(string Token, int Rules)> BucketsFor(string url)
    {
        List<(string, int)> found = new();
        string lower = url.ToLowerInvariant();
        int i = 0;
        while (i < lower.Length)
        {
            if (!IsTokenChar(lower[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < lower.Length && IsTokenChar(lower[i]))
            {
                i++;
            }

            int length = i - start;
            if (length is >= MinToken and <= MaxToken
                && _tokenRulesBySpan.TryGetValue(lower.AsSpan(start, length), out List<NetRule>? bucket))
            {
                found.Add((lower.Substring(start, length), bucket.Count));
            }
        }

        return found;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private NetRule? FindIndexed(ref Request r, Dictionary<string, List<NetRule>>.AlternateLookup<ReadOnlySpan<char>> index, List<NetRule> any)
    {
        string url = r.Lower;
        int i = 0;
        while (i < url.Length)
        {
            if (!IsTokenChar(url[i]))
            {
                i++;
                continue;
            }

            int start = i;
            while (i < url.Length && IsTokenChar(url[i]))
            {
                i++;
            }

            int length = i - start;
            if (length is < MinToken or > MaxToken)
            {
                continue;
            }

            if (index.TryGetValue(url.AsSpan(start, length), out List<NetRule>? bucket))
            {
                foreach (NetRule rule in bucket)
                {
                    if (Applies(rule, ref r))
                    {
                        return rule;
                    }
                }
            }
        }

        foreach (NetRule rule in any)
        {
            if (Applies(rule, ref r))
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>Options first — they are cheap and usually decide it — then the pattern.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool Applies(NetRule rule, ref Request r)
    {
        if ((rule.Types & r.Type) == 0)
        {
            return false;
        }

        if (rule.Is(RuleFlags.ThirdParty | RuleFlags.FirstParty))
        {
            if (r.Party == 0)
            {
                r.Party = IsThirdParty(r.Host, r.Page) ? 3 : 1;
            }

            if (rule.Is(RuleFlags.ThirdParty) && r.Party != 3) { return false; }
            if (rule.Is(RuleFlags.FirstParty) && r.Party != 1) { return false; }
        }

        if ((rule.Include is not null || rule.Exclude is not null) && !DomainApplies(rule.Include, rule.Exclude, r.Page))
        {
            return false;
        }

        return PatternMatches(rule, rule.Is(RuleFlags.MatchCase) ? r.Url : r.Lower, r.HostStart, r.HostEnd);
    }

    /// <summary>$domain=: the page's host is one of (or under one of) the included, and none of the excluded.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool DomainApplies(string[]? include, string[]? exclude, Page page)
    {
        if (page.Host.Length == 0)
        {
            return include is null;
        }

        if (exclude is not null)
        {
            foreach (string d in exclude)
            {
                if (HostUnder(page, d))
                {
                    return false;
                }
            }
        }

        if (include is null)
        {
            return true;
        }

        foreach (string d in include)
        {
            if (HostUnder(page, d))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HostUnder(Page page, string domain)
    {
        if (domain.EndsWith(".*", StringComparison.Ordinal))
        {
            // "google.*" — Google on any country's domain.
            string entity = page.Entity;
            return entity.Length > 0 && (entity == domain || entity.EndsWith("." + domain, StringComparison.Ordinal));
        }

        string host = page.Host;
        return host == domain
               || (host.Length > domain.Length && host[host.Length - domain.Length - 1] == '.'
                   && host.EndsWith(domain, StringComparison.Ordinal));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool PatternMatches(NetRule rule, string url, int hostStart, int hostEnd)
    {
        string p = rule.Pattern;
        bool end = rule.Is(RuleFlags.EndAnchor);

        if (rule.Is(RuleFlags.HostAnchor))
        {
            // At the start of the host, or at the start of any of its labels.
            for (int at = hostStart; at < hostEnd; at++)
            {
                if ((at == hostStart || url[at - 1] == '.') && MatchAt(p, 0, url, at, end))
                {
                    return true;
                }
            }

            return false;
        }

        if (rule.Is(RuleFlags.StartAnchor))
        {
            return MatchAt(p, 0, url, 0, end);
        }

        if (p.Length == 0)
        {
            return true;
        }

        // Unanchored: try each place the pattern's first literal could begin.
        char first = p[0];
        if (first == '^')
        {
            for (int at = 0; at <= url.Length; at++)
            {
                if (MatchAt(p, 0, url, at, end)) { return true; }
            }

            return false;
        }

        int from = 0;
        while (from < url.Length)
        {
            int at = url.IndexOf(first, from);
            if (at < 0)
            {
                return false;
            }

            if (MatchAt(p, 0, url, at, end))
            {
                return true;
            }

            from = at + 1;
        }

        return false;
    }

    /// <summary>
    /// Whether the pattern from <paramref name="pi"/> matches the URL from <paramref name="ui"/>.
    /// <c>*</c> is any run of characters; <c>^</c> is one separator character, or the end.
    /// </summary>
    private static bool MatchAt(string p, int pi, string url, int ui, bool endAnchor)
    {
        while (pi < p.Length)
        {
            char c = p[pi];
            if (c == '*')
            {
                while (pi < p.Length && p[pi] == '*')
                {
                    pi++;
                }

                if (pi == p.Length)
                {
                    return true;
                }

                char next = p[pi];
                for (int k = ui; k <= url.Length; k++)
                {
                    if (next != '^' && (k == url.Length || url[k] != next))
                    {
                        int jump = k < url.Length ? url.IndexOf(next, k) : -1;
                        if (jump < 0)
                        {
                            return false;
                        }

                        k = jump;
                    }

                    if (MatchAt(p, pi, url, k, endAnchor))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (c == '^')
            {
                if (ui == url.Length)
                {
                    // The end of the address counts as a separator — once.
                    pi++;
                    continue;
                }

                if (!IsSeparator(url[ui]))
                {
                    return false;
                }

                pi++;
                ui++;
                continue;
            }

            if (ui >= url.Length || url[ui] != c)
            {
                return false;
            }

            pi++;
            ui++;
        }

        return !endAnchor || ui == url.Length;
    }

    /// <summary>ABP's separator: anything but a letter, a digit, or one of _ - . %</summary>
    private static bool IsSeparator(char c) =>
        !(c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-' or '.' or '%');

    /// <summary>Where the host sits in an absolute http(s)/ws(s) URL, without allocating.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool TryHost(string lower, out int start, out int end)
    {
        start = end = 0;
        int scheme = lower.IndexOf("://", StringComparison.Ordinal);
        if (scheme <= 0 || scheme > 6)
        {
            return false;
        }

        start = scheme + 3;
        if (start >= lower.Length)
        {
            return false;
        }

        // user:pass@ never reaches here in practice, but must not be mistaken for the host.
        int limit = lower.IndexOfAny(HostEnders, start);
        if (limit < 0) { limit = lower.Length; }
        int at = LastIndexOfChar(lower.AsSpan(start, limit - start), '@');
        if (at >= 0) { at += start; }
        if (at >= start) { start = at + 1; }

        end = start;
        while (end < lower.Length && lower[end] is not ('/' or ':' or '?' or '#'))
        {
            end++;
        }

        if (lower[start] == '[')
        {
            int close = lower.IndexOf(']', start);
            end = close > start ? close + 1 : end;
        }

        return end > start;
    }

    private static readonly char[] HostEnders = { '/', '?', '#' };

    // Plain loops for searching a HOST, not the framework's vectorised IndexOf/LastIndexOf.
    // A host is a few dozen characters, so a loop costs nothing — and the framework's search
    // switches to 512-bit vectors at 32 characters, code compiled at run time the first time a host
    // that long turns up. That was YouTube's video hosts (rr1---sn-q4flrnlz.googlevideo.com, 33
    // characters): 3.4 ms of compiling on the UI thread, in the middle of a page load, once a
    // session (M4.5, measured with JitInfo; the JIT log named SpanHelpers' Vector512 SimdImpl).

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int IndexOfDot(ReadOnlySpan<char> s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '.')
            {
                return i;
            }
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int LastIndexOfChar(ReadOnlySpan<char> s, char c)
    {
        for (int i = s.Length - 1; i >= 0; i--)
        {
            if (s[i] == c)
            {
                return i;
            }
        }

        return -1;
    }

    // ══ SITES ═══════════════════════════════════════════════════════════════

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool IsThirdParty(ReadOnlySpan<char> host, Page page)
    {
        if (page.Site.Length == 0)
        {
            return true;
        }

        ReadOnlySpan<char> site = SiteSpan(host);
        return !site.SequenceEqual(page.Site.AsSpan());
    }

    /// <summary>A host's registrable domain — the same answer PublicSuffix gives, without allocating.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static ReadOnlySpan<char> SiteSpan(ReadOnlySpan<char> host)
    {
        if (host.Length > 0 && host[^1] == '.')
        {
            host = host[..^1];
        }

        if (host.Length == 0 || host[0] == '[' || IsIpv4(host))
        {
            return host;
        }

        int last = LastIndexOfChar(host, '.');
        if (last < 0)
        {
            return host;
        }

        int second = LastIndexOfChar(host[..last], '.');
        if (second < 0)
        {
            return host;
        }

        ReadOnlySpan<char> lastTwo = host[(second + 1)..];
        if (PublicSuffix.IsMultiLabel(lastTwo))
        {
            int third = LastIndexOfChar(host[..second], '.');
            return third < 0 ? host : host[(third + 1)..];
        }

        return lastTwo;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool IsIpv4(ReadOnlySpan<char> host)
    {
        int dots = 0;
        foreach (char c in host)
        {
            if (c == '.') { dots++; }
            else if (c is < '0' or > '9') { return false; }
        }

        return dots == 3;
    }

    public static string SiteOf(string host) => SiteSpan(host.AsSpan()).ToString();

    /// <summary>True when the request goes to the page's own site (its registrable domain).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static bool IsSameSite(string url, Page page)
    {
        if (page.Site.Length == 0)
        {
            return false;
        }

        string lower = url.ToLowerInvariant();
        return TryHost(lower, out int start, out int end)
               && SiteSpan(lower.AsSpan(start, end - start)).SequenceEqual(page.Site.AsSpan());
    }

    /// <summary>"www.google.co.uk" → "www.google.*"; empty when there is no public suffix to drop.</summary>
    public static string EntityOf(string host)
    {
        if (host.Length == 0 || host[0] == '[' || IsIpv4(host.AsSpan()))
        {
            return string.Empty;
        }

        string site = SiteOf(host);
        int dot = site.IndexOf('.');
        if (dot < 0)
        {
            return string.Empty;
        }

        // What follows the site's first label is the public suffix ("com", "co.uk").
        string suffix = site.Substring(dot);
        return host.EndsWith(suffix, StringComparison.Ordinal)
            ? host.Substring(0, host.Length - suffix.Length) + ".*"
            : string.Empty;
    }

    // ══ PAGE-LEVEL ══════════════════════════════════════════════════════════

    /// <summary>
    /// For a page about to load: the page context its requests are judged against, with
    /// <see cref="Page.Allowed"/> set when a <c>@@…$document</c> exception covers it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public Page PageFor(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Host.Length == 0)
        {
            return Page.Empty;
        }

        Page plain = new(uri.Host);
        return PageExceptionFor(url, plain, RuleFlags.DocumentExc) is not null
            ? new Page(uri.Host) { Allowed = true }
            : plain;
    }

    private NetRule? PageExceptionFor(string url, Page page, RuleFlags kind)
    {
        string lower = url.ToLowerInvariant();
        if (!TryHost(lower, out int hostStart, out int hostEnd))
        {
            return null;
        }

        foreach (NetRule rule in _pageExceptions)
        {
            if ((rule.Flags & kind) == 0)
            {
                continue;
            }

            if ((rule.Include is not null || rule.Exclude is not null) && !DomainApplies(rule.Include, rule.Exclude, page))
            {
                continue;
            }

            if (PatternMatches(rule, rule.Is(RuleFlags.MatchCase) ? url : lower, hostStart, hostEnd))
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>
    /// Strict mode only: a rule that blocks this whole site as a page, for the warning Aviary
    /// shows instead of opening it — a <c>$document</c> rule (<c>||scam.example^$document</c>), or a
    /// plain <c>||host^</c> rule, which means the host exists to serve ads or track (uBlock
    /// Origin's "strict blocking" works the same way). None when nothing does, or the lists
    /// themselves make an exception for it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public Verdict MatchPage(string url)
    {
        Page page = PageFor(url);
        if (page.Allowed || page.Host.Length == 0)
        {
            return Verdict.None;
        }

        Verdict whole = Match(url, ReqType.Document, page, thirdPartyOnly: false);
        if (whole.Blocked)
        {
            return whole;
        }

        string lower = url.ToLowerInvariant();
        if (!TryHost(lower, out int hostStart, out int hostEnd))
        {
            return Verdict.None;
        }

        ReadOnlySpan<char> host = lower.AsSpan(hostStart, hostEnd - hostStart);
        int at = 0;
        while (at < host.Length)
        {
            ReadOnlySpan<char> suffix = host.Slice(at);
            if (_plainHostsBySpan.Contains(suffix))
            {
                // An exception that would let this host load in a frame lets it open as a page.
                Request r = new(url, lower, hostStart, hostEnd, ReqType.Subdocument, page, 0);
                if (FindException(ref r) is not null)
                {
                    return Verdict.None;
                }

                string blocked = suffix.ToString();
                return new Verdict(true, "||" + blocked + "^", NameOf(_plainHostList.TryGetValue(blocked, out byte l) ? l : (byte)0));
            }

            int dot = IndexOfDot(suffix);
            if (dot < 0)
            {
                break;
            }

            at += dot + 1;
        }

        return Verdict.None;
    }
}
