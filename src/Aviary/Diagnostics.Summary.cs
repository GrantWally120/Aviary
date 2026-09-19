using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Aviary;

/// <summary>
/// summary.md — the one file small enough to read in full every turn.
///
/// Opens with the complete list of what is collected (the plan's promise that the folder
/// explains itself), then the digest: what needs attention, recurring issues, slowest
/// operations, the sites with the most page errors, and this session's totals. Built on the
/// writer thread from the last seven days of issues, pages and perf plus this session's
/// in-memory tallies. network.jsonl and browsing.jsonl are never read here — they are the
/// raw logs to dip into when chasing something specific.
/// </summary>
internal static partial class Diagnostics
{
    /// <summary>A file bigger than this is read from its tail only.</summary>
    private const long SummaryReadLimit = 32L * 1024 * 1024;

    private static readonly Regex DigitRuns = new(@"\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static void BuildSummary()
    {
        if (!Directory.Exists(Folder))
        {
            return;
        }

        FlushStreams();

        DateTimeOffset now = DateTimeOffset.Now;
        DateTimeOffset since = now.AddDays(-7);
        CultureInfo inv = CultureInfo.InvariantCulture;

        // ── read the last seven days ─────────────────────────────────────────
        Dictionary<string, (string Kind, string What, int Count, DateTimeOffset Last)> recurring = new();
        List<(DateTimeOffset T, string What)> engine = new();
        List<(DateTimeOffset T, string What)> unclean = new();
        List<(DateTimeOffset T, string File, string What)> crashes = new();
        int roundTrips = 0;
        int webViewFailures = 0;

        void Recur(string kind, string what, DateTimeOffset t, int add = 1)
        {
            string key = kind + "|" + what;
            recurring[key] = recurring.TryGetValue(key, out var r)
                ? (kind, what, r.Count + add, t > r.Last ? t : r.Last)
                : (kind, what, add, t);
        }

        ForEachRecent(LogFile.Issues, since, (t, k, root) =>
        {
            switch (k)
            {
                case "swallowed":
                {
                    string at = Str(root, "at");
                    string type = root.TryGetProperty("exception", out JsonElement ex) ? ShortType(Str(ex, "type")) : "";
                    string message = root.TryGetProperty("exception", out JsonElement ex2) ? Str(ex2, "message") : "";
                    Recur("hidden error", at + " — " + Str(root, "name") + " — " + type + ": " + Clip(message, 110), t);
                    break;
                }

                case "swallowed-repeats":
                    if (root.TryGetProperty("sinceLastMinute", out JsonElement reps) && reps.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement rep in reps.EnumerateArray())
                        {
                            int times = rep.TryGetProperty("times", out JsonElement n) && n.TryGetInt32(out int v) ? v : 1;
                            Recur("hidden error (repeats)", Str(rep, "at").Replace("|", " — "), t, times);
                        }
                    }

                    break;

                case "anomaly":
                {
                    string name = Str(root, "name");
                    string detail = Str(root, "detail");
                    if (name == "settings-roundtrip") { roundTrips++; }
                    if (name == "webview-start") { webViewFailures++; }
                    Recur("anomaly: " + name, Clip(DigitRuns.Replace(detail, "#"), 140), t);
                    break;
                }

                case "engine":
                {
                    string what = Str(root, "kind") + " · " + Str(root, "reason")
                        + (root.TryGetProperty("exitCode", out JsonElement code) ? " · exit " + code.ToString() : "")
                        + " · on " + NetRecord.SiteOf(Str(root, "page"));
                    engine.Add((t, what));
                    Recur("engine failure", Str(root, "kind") + " · " + Str(root, "reason"), t);
                    break;
                }

                case "crash":
                    crashes.Add((t, Str(root, "file"), ShortType(Str(root, "type")) + ": " + Clip(Str(root, "message"), 140)));
                    Recur("crash", ShortType(Str(root, "type")) + ": " + Clip(Str(root, "message"), 110), t);
                    break;

                case "unclean-exit":
                    unclean.Add((t, "session " + Str(root, "previousSession") + " started " + Str(root, "started") + ", last seen " + Str(root, "lastSeen")));
                    Recur("unclean exit", "a session ended without closing normally", t);
                    break;

                case "unobserved-task":
                {
                    string type = root.TryGetProperty("exception", out JsonElement ex) ? ShortType(Str(ex, "type")) + ": " + Clip(Str(ex, "message"), 110) : "";
                    Recur("unobserved task", type, t);
                    break;
                }
            }
        });

        Dictionary<string, (int Js, int Resource, int Blocked, DateTimeOffset Last)> sites = new(StringComparer.OrdinalIgnoreCase);
        ForEachRecent(LogFile.Pages, since, (t, k, root) =>
        {
            string site = NetRecord.SiteOf(Str(root, "page"));
            if (site.Length == 0)
            {
                return;
            }

            (int js, int res, int blocked, DateTimeOffset last) = sites.TryGetValue(site, out var s) ? s : (0, 0, 0, t);

            // Files Aviary's own blocker refused are counted apart: they are not the site
            // being broken, though a site that IS broken by the blocker shows up here first.
            if (k == "resource" && Str(root, "blockedBy").Length > 0) { blocked++; }
            else if (k == "resource") { res++; }
            else { js++; }

            sites[site] = (js, res, blocked, t > last ? t : last);
        });

        List<(double Ms, string Host, double Ttfb)> navs = new();
        List<double> tabCreates = new();
        List<double> startups = new();
        List<double> filterAvg = new();
        double filterMax = 0;
        List<double> uiP95 = new();

        ForEachRecent(LogFile.Perf, since, (t, k, root) =>
        {
            switch (k)
            {
                case "navigation":
                    navs.Add((Num(root, "ms"), Str(root, "site"), Num(root, "ttfbMs")));
                    break;
                case "tab-create":
                    tabCreates.Add(Num(root, "ms"));
                    break;
                case "startup":
                    startups.Add(Num(root, "totalMs"));
                    break;
                case "sample":
                    if (root.TryGetProperty("blocker", out JsonElement b) && b.ValueKind == JsonValueKind.Object)
                    {
                        filterAvg.Add(Num(b, "averageUs"));
                        filterMax = Math.Max(filterMax, Num(b, "maxUs"));
                    }

                    if (root.TryGetProperty("ui", out JsonElement u) && u.ValueKind == JsonValueKind.Object)
                    {
                        uiP95.Add(Num(u, "p95Ms"));
                    }

                    break;
            }
        });

        List<FileInfo> crashFiles = Directory.Exists(CrashFolder)
            ? new DirectoryInfo(CrashFolder).EnumerateFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).ToList()
            : new List<FileInfo>();

        // ── write it ─────────────────────────────────────────────────────────
        StringBuilder md = new();

        md.AppendLine("# Aviary — what the improvement data says");
        md.AppendLine();
        md.AppendLine("Regenerated " + now.ToString("yyyy-MM-dd HH:mm zzz", inv)
                      + " — every 10 minutes, and whenever Aviary closes. Build " + BuildInfo.Version + " · " + BuildInfo.Channel
                      + ". This collection session `" + _session + "` began " + _sessionStart.ToString("yyyy-MM-dd HH:mm", inv)
                      + " (" + _sessionReason + ").");
        md.AppendLine();

        md.AppendLine("## What Aviary collects");
        md.AppendLine();
        md.AppendLine(StaysNote);
        md.AppendLine();
        md.AppendLine("| # | Category | What it holds | File |");
        md.AppendLine("|---|---|---|---|");
        foreach (Category c in Categories)
        {
            md.AppendLine("| " + c.Number + " | " + c.Name + " | " + c.Plain + " | `" + c.File + "` |");
        }

        md.AppendLine();
        md.AppendLine("**Never collected:**");
        foreach (string never in NeverCollected)
        {
            md.AppendLine("- " + never);
        }

        md.AppendLine();
        md.AppendLine(MaskingNote);
        md.AppendLine();

        // ── needs attention ──
        md.AppendLine("## Needs attention");
        md.AppendLine();
        List<string> attention = new();

        List<FileInfo> weekCrashes = crashFiles.Where(f => f.LastWriteTime >= since.LocalDateTime).ToList();
        if (weekCrashes.Count > 0)
        {
            string newest = crashes.Count > 0 ? " — newest: `" + crashes.OrderByDescending(c => c.T).First().File + "` " + crashes.OrderByDescending(c => c.T).First().What : "";
            attention.Add("**" + weekCrashes.Count + " crash" + (weekCrashes.Count == 1 ? "" : "es") + "** in the last 7 days" + Cell(newest));
        }

        if (engine.Count > 0)
        {
            (DateTimeOffset t, string what) = engine.OrderByDescending(e => e.T).First();
            attention.Add("**" + engine.Count + " engine failure" + (engine.Count == 1 ? "" : "s") + "** in the last 7 days — newest " + t.ToString("MM-dd HH:mm", inv) + ": " + Cell(what));
        }

        if (unclean.Count > 0)
        {
            attention.Add("**" + unclean.Count + " session" + (unclean.Count == 1 ? "" : "s") + " ended without a clean close** — newest: " + Cell(unclean.OrderByDescending(u => u.T).First().What));
        }

        if (roundTrips > 0)
        {
            attention.Add("**" + roundTrips + " settings round-trip mismatch" + (roundTrips == 1 ? "" : "es") + "** — a setting that will look broken.");
        }

        if (webViewFailures > 0)
        {
            attention.Add("**" + webViewFailures + " tab" + (webViewFailures == 1 ? "" : "s") + " failed to start their engine.**");
        }

        long dropped = Interlocked.Read(ref _dropped);
        long errors = Interlocked.Read(ref _writeErrors);
        if (dropped > 0 || errors > 0)
        {
            attention.Add("Collection itself: " + dropped + " record(s) dropped because the writer fell behind, " + errors + " write error(s), this session.");
        }

        if (Interlocked.Read(ref _stalls) > 0)
        {
            attention.Add(Interlocked.Read(ref _stalls) + " UI stall(s) over 50 ms this session; longest " + Math.Round(_longestStall) + " ms: " + Cell(Scrub(_longestStallDuring)));
        }

        if (attention.Count == 0)
        {
            md.AppendLine("Nothing. No crashes, engine failures, unclean exits or settings mismatches in the last 7 days.");
        }
        else
        {
            foreach (string line in attention)
            {
                md.AppendLine("- " + line);
            }
        }

        md.AppendLine();

        // ── recurring issues ──
        md.AppendLine("## Recurring issues — last 7 days");
        md.AppendLine();
        if (recurring.Count == 0)
        {
            md.AppendLine("None recorded.");
        }
        else
        {
            md.AppendLine("| Count | Kind | Where / what | Last seen |");
            md.AppendLine("|---:|---|---|---|");
            foreach (var r in recurring.Values.OrderByDescending(r => r.Count).ThenByDescending(r => r.Last).Take(20))
            {
                md.AppendLine("| " + r.Count + " | " + r.Kind + " | " + Cell(r.What) + " | " + r.Last.ToString("MM-dd HH:mm", inv) + " |");
            }

            if (recurring.Count > 20)
            {
                md.AppendLine();
                md.AppendLine("…and " + (recurring.Count - 20) + " more distinct issues in `issues.jsonl`.");
            }
        }

        md.AppendLine();

        // ── slowest ──
        md.AppendLine("## Slowest operations");
        md.AppendLine();
        List<Slow> slow;
        lock (Slowest)
        {
            slow = Slowest.ToList();
        }

        md.AppendLine("**This session**");
        md.AppendLine();
        AppendSlow(md, slow, "startup", "Start-up (process start to first page)", 1);
        AppendSlow(md, slow, "navigation", "Slowest page loads", 5);
        AppendSlow(md, slow, "tab-create", "Slowest new tabs (engine start)", 3);
        AppendSlow(md, slow, "look-swap", "Slowest look swaps", 2);
        AppendSlow(md, slow, "start-page-build", "Slowest Start Page builds", 2);
        AppendSlow(md, slow, "ui-stall", "Longest UI stalls", 5);
        if (slow.Count == 0)
        {
            md.AppendLine("- Nothing measured yet this session.");
        }

        md.AppendLine();
        md.AppendLine("**Last 7 days** (from `perf.jsonl`)");
        md.AppendLine();
        if (startups.Count > 0)
        {
            md.AppendLine("- Start-up: last " + Ms(startups[^1]) + ", median " + Ms(Median(startups)) + ", slowest " + Ms(startups.Max()) + " over " + startups.Count + " launch(es).");
        }

        if (navs.Count > 0)
        {
            md.AppendLine("- Page loads: " + navs.Count + ", median " + Ms(Median(navs.Select(n => n.Ms).ToList())) + ", median time to first byte " + Ms(Median(navs.Select(n => n.Ttfb).Where(x => x > 0).ToList())) + ".");
            foreach (var n in navs.OrderByDescending(n => n.Ms).Take(5))
            {
                md.AppendLine("  - " + Ms(n.Ms) + " — " + Cell(n.Host) + (n.Ttfb > 0 ? " (first byte " + Ms(n.Ttfb) + ")" : ""));
            }
        }

        if (tabCreates.Count > 0)
        {
            md.AppendLine("- New tabs: " + tabCreates.Count + ", median " + Ms(Median(tabCreates)) + ", slowest " + Ms(tabCreates.Max()) + ".");
        }

        if (filterAvg.Count > 0)
        {
            md.AppendLine("- Blocker cost per request: average " + Math.Round(filterAvg.Average(), 1).ToString(inv) + " µs, worst single match " + Math.Round(filterMax, 1).ToString(inv) + " µs.");
        }

        if (uiP95.Count > 0)
        {
            md.AppendLine("- Window responsiveness: median minute's 95th percentile " + Ms(Median(uiP95)) + ", worst minute " + Ms(uiP95.Max()) + ".");
        }

        if (startups.Count + navs.Count + tabCreates.Count + filterAvg.Count + uiP95.Count == 0)
        {
            md.AppendLine("- Nothing measured yet.");
        }

        md.AppendLine();

        // ── page errors ──
        md.AppendLine("## Sites with the most page errors — last 7 days");
        md.AppendLine();
        if (sites.Count == 0)
        {
            md.AppendLine("None recorded.");
        }
        else
        {
            md.AppendLine("| Site | Script errors | Failed files | Files Aviary blocked | Last seen |");
            md.AppendLine("|---|---:|---:|---:|---|");
            foreach (var s in sites.OrderByDescending(s => s.Value.Js + s.Value.Resource).ThenByDescending(s => s.Value.Blocked).Take(10))
            {
                md.AppendLine("| " + Cell(s.Key) + " | " + s.Value.Js + " | " + s.Value.Resource + " | " + s.Value.Blocked
                              + " | " + s.Value.Last.ToString("MM-dd HH:mm", inv) + " |");
            }
        }

        md.AppendLine();

        // ── this session ──
        md.AppendLine("## This session");
        md.AppendLine();
        long requests = Interlocked.Read(ref _requests);
        long third = Interlocked.Read(ref _thirdParty);
        md.AppendLine("- Pages visited: " + Interlocked.Read(ref _navigations)
                      + " · requests: " + requests + " (" + (Interlocked.Read(ref _requestBytes) / 1048576.0).ToString("0.0", inv) + " MB declared"
                      + (requests > 0 ? ", " + Math.Round(third * 100.0 / requests) + "% third-party" : "") + ")"
                      + " · blocked: " + Interlocked.Read(ref _blocked) + ".");

        if (!RuleCounts.IsEmpty)
        {
            md.AppendLine("- Most-matched block rules: " + string.Join(", ",
                RuleCounts.OrderByDescending(p => p.Value).Take(6).Select(p => Cell(Scrub(p.Key)) + " ×" + p.Value)) + ".");
        }

        md.AppendLine("- Page errors: " + Interlocked.Read(ref _pageErrors) + " · engine failures: " + Interlocked.Read(ref _engineFailures)
                      + " · crashes: " + Interlocked.Read(ref _crashes) + ".");

        if (!FeatureCounts.IsEmpty)
        {
            md.AppendLine("- Features used: " + string.Join(", ",
                FeatureCounts.OrderByDescending(p => p.Value).Select(p => Cell(Scrub(p.Key)) + " ×" + p.Value)) + ".");
        }

        md.AppendLine("- Collection: " + Interlocked.Read(ref _linesWritten) + " lines written, " + dropped + " dropped, " + errors + " write errors.");
        md.AppendLine();

        // ── files ──
        md.AppendLine("## Files");
        md.AppendLine();
        md.AppendLine("| File | Size | Last written |");
        md.AppendLine("|---|---:|---|");
        foreach (FileInfo f in FilesNow(Folder)
                     .Where(f => !f.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase))
        {
            string relative = Path.GetRelativePath(Folder, f.FullName).Replace('\\', '/');
            md.AppendLine("| `" + relative + "` | " + Size(f.Length) + " | " + f.LastWriteTime.ToString("yyyy-MM-dd HH:mm", inv) + " |");
        }

        WriteFileAtomically(Path.Combine(Folder, "summary.md"), new UTF8Encoding(false).GetBytes(md.ToString()));
    }

    /// <summary>
    /// Calls <paramref name="visit"/> for each line of a category file newer than
    /// <paramref name="since"/>. Reads only the tail of a very large file.
    /// </summary>
    private static void ForEachRecent(LogFile file, DateTimeOffset since, Action<DateTimeOffset, string, JsonElement> visit)
    {
        string path = PathOf((int)file);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bool skipPartial = false;
            if (stream.Length > SummaryReadLimit)
            {
                stream.Seek(-SummaryReadLimit, SeekOrigin.End);
                skipPartial = true;
            }

            using StreamReader reader = new(stream, Encoding.UTF8);
            if (skipPartial)
            {
                reader.ReadLine();
            }

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (!TryLineTime(line, out DateTimeOffset t) || t < since)
                {
                    continue;
                }

                try
                {
                    using JsonDocument doc = JsonDocument.Parse(line);
                    visit(t, Str(doc.RootElement, "k"), doc.RootElement);
                }
                catch (JsonException)
                {
                    // A line cut short by a crash mid-write. Skip it; the rest still count.
                }
            }
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _writeErrors);
        }
    }

    private static void AppendSlow(StringBuilder md, List<Slow> slow, string op, string label, int take)
    {
        List<Slow> mine = slow.Where(s => s.Op == op).OrderByDescending(s => s.Ms).Take(take).ToList();
        if (mine.Count == 0)
        {
            return;
        }

        md.AppendLine("- " + label + ": " + string.Join("; ", mine.Select(s => Ms(s.Ms) + (s.Detail.Length > 0 ? " — " + Cell(Scrub(s.Detail)) : ""))));
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : v.ToString()
            : string.Empty;

    private static double Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : 0;

    private static string ShortType(string type)
    {
        int dot = type.LastIndexOf('.');
        return dot >= 0 ? type.Substring(dot + 1) : type;
    }

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string Clip(string text, int max)
    {
        text = Whitespace.Replace(text, " ").Trim();
        return text.Length <= max ? text : text.Substring(0, max) + "…";
    }

    /// <summary>Safe inside a Markdown table cell: no pipes, no line breaks, no runs of spaces.</summary>
    private static string Cell(string text) =>
        Whitespace.Replace(text, " ").Trim().Replace("|", "\\|");

    /// <summary>
    /// Whole milliseconds above ten, one decimal below — the window's responsiveness lives
    /// well under a millisecond, and "0 ms" said nothing.
    /// </summary>
    private static string Ms(double ms) =>
        ms >= 10_000 ? (ms / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " s"
        : ms >= 10 ? Math.Round(ms).ToString("N0", CultureInfo.InvariantCulture) + " ms"
        : ms.ToString("0.0", CultureInfo.InvariantCulture) + " ms";

    private static string Size(long bytes) =>
        bytes >= 1024 * 1024
            ? (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : Math.Max(bytes > 0 ? 1 : 0, bytes / 1024) + " KB";

    private static double Median(List<double> values) => Percentile(values, 0.5);
}
