using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>Where an icon came from. Higher wins; a lower rank never overwrites a higher one.</summary>
internal enum IconRank
{
    None = 0,

    /// <summary>Guessed at /favicon.ico because the page declared nothing.</summary>
    Guessed = 1,

    /// <summary>Chromium's own decoded icon. Already downloaded, already correct, free.</summary>
    Engine = 2,

    /// <summary>A &lt;link rel="icon"&gt; the page actually declared.</summary>
    Declared = 3,

    /// <summary>An apple-touch-icon: the big, crisp one, meant for tiles.</summary>
    Touch = 4,
}

/// <summary>One &lt;link&gt; the page declared, as harvested from the document.</summary>
internal sealed class IconCandidate
{
    public string Href { get; set; } = string.Empty;
    public string Rel { get; set; } = string.Empty;
    public int Px { get; set; }
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// Every site icon Aviary knows, and the only place that decides what a site looks like.
///
/// Two masters per site, not one. Safari genuinely keeps both: the tab icon comes from the
/// small purpose-drawn favicon, and the Start Page tile comes from the apple-touch-icon.
/// Storing one master and scaling it in both directions is what makes a browser look like a
/// knock-off — a 16px master blown up to a tile is mush, and a 180px master crushed to 16px
/// throws away the hinting the designer put there.
///
/// Keyed by HOST with a registrable-domain fallback, not by page URL. A bookmark to
/// developer.mozilla.org/en-US/docs/Web/CSS has to resolve to an icon with no I/O at all in
/// the instant the favourites bar draws, and mail.google.com should keep its own envelope
/// while an arbitrary docs.google.com URL still falls back to google.com.
/// </summary>
internal static class Favicons
{
    private const int MaxIconBytes = 256 * 1024;
    private const int MaxEntries = 400;

    /// <summary>Raised with the affected key when an icon becomes available. Always on the UI thread.</summary>
    public static event Action<string>? Updated;

    // ── ONE master image ────────────────────────────────────────────────────

    private sealed class Master
    {
        public byte[] Bytes = Array.Empty<byte>();
        public IconRank Rank;
        public bool IsSvg;
        public string Source = string.Empty;
        public string File = string.Empty;
    }

    private sealed class Entry
    {
        /// <summary>The tab-sized art.</summary>
        public Master? Small;

        /// <summary>The touch icon, when the site declared one.</summary>
        public Master? Large;

        public DateTimeOffset UsedUtc = DateTimeOffset.UtcNow;
        public bool Missing;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Pinned = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> InFlight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Decoded images, keyed by (icon key, PHYSICAL pixel width). RebuildTabStrip destroys
    /// and recreates every cell on every open, close and drag-end, so without this the app
    /// re-decodes every icon mid-drag. This is not an optimisation; it is why drag is smooth.
    /// </summary>
    private static readonly Dictionary<(string, int), ImageSource> Decoded = new();

    private static Microsoft.UI.Dispatching.DispatcherQueue? _ui;
    private static string? _token;
    private static bool _indexLoaded;
    private static bool _indexDirty;

    /// <summary>
    /// One client for the whole app. A per-request HttpClient exhausts sockets under
    /// TIME_WAIT. Cookies are OFF on purpose: with them on, an icon fetch would carry the
    /// user's session cookie to whatever URL the page happened to nominate.
    /// </summary>
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        UseCookies = false,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 3,
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    private static string Folder => AppPaths.Of("favicons");

    private static string IndexPath => Path.Combine(Folder, "index.json");

    // ── LIFECYCLE ───────────────────────────────────────────────────────────

    private static int _initialised;

    /// <summary>
    /// Once per process. A private window is a second MainWindow, and a second call used to
    /// add the Safari user agent to the fetcher twice and reload the index over the live one.
    /// </summary>
    public static void Initialise(Microsoft.UI.Dispatching.DispatcherQueue ui)
    {
        if (Interlocked.Exchange(ref _initialised, 1) == 1)
        {
            return;
        }

        _ui = ui;
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 "
          + "(KHTML, like Gecko) Version/18.0 Safari/605.1.15");

        _ = Task.Run(LoadIndex);
    }

    /// <summary>
    /// True while the engine's own favicon API is usable. GetFaviconAsync arrived in
    /// WebView2 102; below that it throws, and the harvest path carries the whole load.
    /// </summary>
    public static bool EngineApiAvailable { get; private set; } = true;

    /// <summary>One-way latch. Once the API has thrown, stop asking.</summary>
    public static void MarkEngineApiUnavailable() => EngineApiAvailable = false;

    // ── KEYING ──────────────────────────────────────────────────────────────

    /// <summary>The cache key for a URL: its host. Empty for internal and non-web pages.</summary>
    public static string KeyFor(string url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return string.Empty;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return string.Empty;
        }

        string host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host.Substring(4) : host;
    }

    private static Entry? Find(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        lock (Gate)
        {
            if (Entries.TryGetValue(key, out Entry? exact))
            {
                exact.UsedUtc = DateTimeOffset.UtcNow;
                _indexDirty = true;
                return exact;
            }

            // mail.google.com has no icon of its own yet? Fall back to google.com rather
            // than showing a letter for a site the user plainly recognises.
            string domain = PublicSuffix.RegistrableDomain(key);
            if (!string.Equals(domain, key, StringComparison.OrdinalIgnoreCase)
                && Entries.TryGetValue(domain, out Entry? parent))
            {
                parent.UsedUtc = DateTimeOffset.UtcNow;
                _indexDirty = true;
                return parent;
            }
        }

        return null;
    }

    public static bool HasAnyMaster(string key)
    {
        Entry? e = Find(key);
        return e is not null && (e.Small is not null || e.Large is not null);
    }

    public static void Pin(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            lock (Gate) { Pinned.Add(key); }
        }
    }

    public static void Unpin(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            lock (Gate) { Pinned.Remove(key); }
        }
    }

    public static void NoteMissing(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        lock (Gate)
        {
            if (!Entries.TryGetValue(key, out Entry? e))
            {
                Entries[key] = e = new Entry();
            }

            if (e.Small is null && e.Large is null)
            {
                e.Missing = true;
            }
        }
    }

    // ── TAKING AN ICON IN ───────────────────────────────────────────────────

    /// <summary>
    /// Offers raw bytes for a key. A lower rank never displaces a higher one, so the
    /// engine's decoded icon cannot overwrite the apple-touch-icon we went and fetched.
    /// </summary>
    public static void Offer(string key, byte[] raw, string sourceUri, IconRank rank)
    {
        if (string.IsNullOrEmpty(key) || raw is null || raw.Length == 0 || raw.Length > MaxIconBytes)
        {
            return;
        }

        bool svg = LooksLikeSvg(raw);
        bool large = rank == IconRank.Touch;
        bool changed = false;

        lock (Gate)
        {
            if (!Entries.TryGetValue(key, out Entry? e))
            {
                Entries[key] = e = new Entry();
            }

            e.Missing = false;
            e.UsedUtc = DateTimeOffset.UtcNow;

            Master? slot = large ? e.Large : e.Small;
            if (slot is null || rank >= slot.Rank)
            {
                Master m = new()
                {
                    Bytes = raw,
                    Rank = rank,
                    IsSvg = svg,
                    Source = sourceUri,
                    File = FileNameFor(key, large, svg),
                };

                if (large) { e.Large = m; } else { e.Small = m; }

                // An SVG is resolution-independent, so it serves both roles.
                if (svg && !large && e.Large is null) { e.Large = m; }

                changed = true;
                _indexDirty = true;
            }

            if (changed)
            {
                // Every decode of this key is now stale.
                foreach ((string, int) k in Decoded.Keys.Where(k => k.Item1 == key).ToList())
                {
                    Decoded.Remove(k);
                }
            }
        }

        if (!changed)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            WriteToDisk(key);
            ScheduleIndexSave();
        });

        RaiseUpdated(key);
    }

    private static int _saveScheduled;

    /// <summary>
    /// Writes index.json a couple of seconds after the last change, rather than only at
    /// shutdown.
    ///
    /// The image files land immediately but the index is what makes them findable, so an
    /// index written only on a clean exit means a crash — or a force-kill — leaves a folder
    /// of orphaned icons that no later launch can ever read. Debounced because a page with
    /// several declared icons offers them in a burst.
    /// </summary>
    private static void ScheduleIndexSave()
    {
        if (Interlocked.Exchange(ref _saveScheduled, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            Interlocked.Exchange(ref _saveScheduled, 0);
            SaveIndex();
        });
    }

    /// <summary>
    /// The links a page declared. Anything better than what we already hold is fetched;
    /// everything else is ignored, so a page reload costs no network at all.
    /// </summary>
    public static void OfferCandidates(string key, string pageOrigin, IReadOnlyList<IconCandidate> links)
    {
        if (string.IsNullOrEmpty(key) || links is null || links.Count == 0)
        {
            return;
        }

        if (!AppSettings.GetBool("siteIcons"))
        {
            return;
        }

        // The best declared touch icon, and the best declared regular icon.
        IconCandidate? touch = links
            .Where(l => l.Rel.Contains("apple-touch", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.Px)
            .FirstOrDefault();

        IconCandidate? icon = links
            .Where(l => !l.Rel.Contains("apple-touch", StringComparison.OrdinalIgnoreCase)
                     && !l.Rel.Contains("mask-icon", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.Px)
            .FirstOrDefault();

        Entry? existing = Find(key);

        if (touch is not null && (existing?.Large is null || existing.Large.Rank < IconRank.Touch))
        {
            _ = FetchAsync(key, Absolute(pageOrigin, touch.Href), IconRank.Touch);
        }

        if (icon is not null && (existing?.Small is null || existing.Small.Rank < IconRank.Declared))
        {
            _ = FetchAsync(key, Absolute(pageOrigin, icon.Href), IconRank.Declared);
        }
    }

    /// <summary>
    /// A declared icon's address, resolved the way a browser resolves it — against the page —
    /// and only if it is a web address at all.
    ///
    /// Google declares its icon as "//www.gstatic.com/…/favicon.ico": scheme-relative, which on
    /// the web means "same scheme as the page". .NET reads a string starting with // as a
    /// Windows network path instead, so trying it as an absolute URI first produced
    /// file://www.gstatic.com/… and every fetch failed with "The 'file' scheme is not
    /// supported" (found in Grant's own diagnostics, 12 Sep — bug 41). Hence the explicit
    /// scheme, and the refusal of anything that does not come out as http or https.
    /// </summary>
    internal static string Absolute(string origin, string href)
    {
        if (string.IsNullOrWhiteSpace(href)
            || !Uri.TryCreate(origin, UriKind.Absolute, out Uri? page)
            || page.Scheme is not ("http" or "https"))
        {
            return string.Empty;
        }

        href = href.Trim();
        if (href.StartsWith("//", StringComparison.Ordinal))
        {
            href = page.Scheme + ":" + href;
        }

        if (!Uri.TryCreate(page, href, out Uri? resolved))
        {
            return string.Empty;
        }

        return resolved.Scheme is "http" or "https" ? resolved.AbsoluteUri : string.Empty;
    }

    private static async Task FetchAsync(string key, string url, IconRank rank)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        // The Collection's host exists only inside the engine — the network has never heard of
        // it, so a fetch would fail every time and fill diagnostics with favicon errors. Its
        // icons are files in Aviary's own folder: read them from there.
        if (Library.IsLibraryUrl(url))
        {
            if (Library.TryRead(url, out byte[] local) && local.Length > 0)
            {
                Offer(key, local, url, rank);
            }

            return;
        }

        // Never fetch through a host the blocker would have stopped, and never fetch the
        // same URL twice concurrently.
        if (TrackerBlocker.ShouldBlock(url))
        {
            return;
        }

        string guard = key + "|" + url;
        lock (Gate)
        {
            if (!InFlight.Add(guard))
            {
                return;
            }
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        int status = 0;
        long? length = null;
        string? type = null;

        try
        {
            using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            status = (int)response.StatusCode;
            length = response.Content.Headers.ContentLength;
            type = response.Content.Headers.ContentType?.MediaType;

            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            if (response.Content.Headers.ContentLength is > MaxIconBytes)
            {
                return;
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            length = bytes.Length;
            Offer(key, bytes, url, rank);
        }
        catch (Exception ex)
        {
            // An icon that will not download is not worth interrupting anyone over — but it
            // is exactly what diagnostics' "site icons that will not load" is for.
            Diagnostics.Anomaly("favicon-fetch", key + ": " + ex.GetType().Name + " — " + ex.Message, new { url, rank = rank.ToString() });
        }
        finally
        {
            lock (Gate) { InFlight.Remove(guard); }

            // Aviary's own request, not a page's — still one of "every request the browser
            // makes". Tab 0 is Aviary itself.
            if (Diagnostics.RecordsBrowsing)
            {
                Diagnostics.Request(new NetRecord
                {
                    Tab = 0,
                    Url = url,
                    Method = "GET",
                    Context = "Favicon",
                    Status = status,
                    Type = type,
                    Bytes = length,
                    Ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1),
                    Initiator = "aviary:site-icon",
                    PageUrl = "https://" + key + "/",
                });
            }
        }
    }

    private static bool LooksLikeSvg(byte[] raw)
    {
        int n = Math.Min(raw.Length, 256);
        string head = Encoding.UTF8.GetString(raw, 0, n).TrimStart();
        return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
            || (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                && head.Contains("<svg", StringComparison.OrdinalIgnoreCase));
    }

    // ── DRAWING ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A decoded image for this key at this size, or null if we have nothing.
    ///
    /// MUST be called on the UI thread: BitmapImage and SvgImageSource are
    /// DependencyObjects. The size arithmetic is done entirely in PHYSICAL pixels, because
    /// 16 logical pixels is 32 real ones at 200% and the difference between a crisp icon
    /// and a blurry one is exactly that.
    /// </summary>
    public static ImageSource? GetImage(string key, double logicalSize, double rasterizationScale)
    {
        Entry? entry = Find(key);
        if (entry is null)
        {
            return null;
        }

        if (rasterizationScale <= 0)
        {
            rasterizationScale = 1;
        }

        int physical = Math.Max(8, (int)Math.Ceiling(logicalSize * rasterizationScale));

        lock (Gate)
        {
            if (Decoded.TryGetValue((key, physical), out ImageSource? cached))
            {
                return cached;
            }
        }

        // The smallest master that is still big enough. Falling back the other way is
        // better than nothing — an upscaled small icon beats a letter.
        Master? master = ChooseMaster(entry, physical);
        if (master is null)
        {
            return null;
        }

        ImageSource? source = Decode(master, physical);
        if (source is null)
        {
            return null;
        }

        lock (Gate)
        {
            Decoded[(key, physical)] = source;
        }

        return source;
    }

    private static Master? ChooseMaster(Entry entry, int physical)
    {
        // An SVG serves any size perfectly, so prefer it outright.
        if (entry.Small is { IsSvg: true }) { return entry.Small; }
        if (entry.Large is { IsSvg: true }) { return entry.Large; }

        // A touch icon is typically 180px. Above roughly 32 physical pixels it is the
        // better source; at tab size the purpose-drawn small icon wins every time.
        if (physical > 32 && entry.Large is not null) { return entry.Large; }
        return entry.Small ?? entry.Large;
    }

    private static ImageSource? Decode(Master master, int physical)
    {
        try
        {
            if (master.IsSvg)
            {
                // RasterizePixelWidth is ALWAYS physical pixels.
                SvgImageSource svg = new() { RasterizePixelWidth = physical, RasterizePixelHeight = physical };
                _ = svg.SetSourceAsync(new MemoryStream(master.Bytes).AsRandomAccessStream());
                return svg;
            }

            BitmapImage bitmap = new();

            // Both of these MUST be assigned BEFORE SetSourceAsync. Set afterwards they
            // are silently ignored and you get a full-size decode with no error at all.
            // Physical, not the Logical default, so this and the SVG path share one unit.
            bitmap.DecodePixelType = DecodePixelType.Physical;
            bitmap.DecodePixelWidth = physical;

            _ = bitmap.SetSourceAsync(new MemoryStream(master.Bytes).AsRandomAccessStream());
            return bitmap;
        }
        catch (Exception ex)
        {
            Diagnostics.Anomaly("favicon-decode", ex.GetType().Name + " — " + ex.Message,
                new { source = master.Source, svg = master.IsSvg, bytes = master.Bytes.Length });
            return null;
        }
    }

    /// <summary>Dropped when the rasterization scale changes — every decode is now wrong.</summary>
    public static void InvalidateDecoded()
    {
        lock (Gate) { Decoded.Clear(); }
    }

    private static void RaiseUpdated(string key)
    {
        Microsoft.UI.Dispatching.DispatcherQueue? ui = _ui;
        if (ui is null)
        {
            return;
        }

        ui.TryEnqueue(() =>
        {
            try { Updated?.Invoke(key); }
            catch (Exception ex) { Diagnostics.Swallowed(ex, "favicons: Updated listener"); /* a listener's failure is not the store's problem */ }
        });
    }

    // ── DISK ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hashed, never derived from the domain. A filename taken straight from a host gives
    /// base names like "nul" for nul.com and "con" for con.org — Windows reserved device
    /// names, where File.Create either throws or, worse, opens the device. The readable
    /// domain lives in index.json instead.
    /// </summary>
    private static string FileNameFor(string key, bool large, bool svg)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string stem = Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
        return stem + (large ? "-l" : "-s") + (svg ? ".svg" : ".img");
    }

    private sealed class IndexRow
    {
        public string Key { get; set; } = string.Empty;
        public string? SmallFile { get; set; }
        public int SmallRank { get; set; }
        public bool SmallSvg { get; set; }
        public string? LargeFile { get; set; }
        public int LargeRank { get; set; }
        public bool LargeSvg { get; set; }
        public DateTimeOffset UsedUtc { get; set; }
    }

    private static void LoadIndex()
    {
        try
        {
            if (!File.Exists(IndexPath))
            {
                _indexLoaded = true;
                return;
            }

            List<IndexRow>? rows = JsonSerializer.Deserialize<List<IndexRow>>(File.ReadAllText(IndexPath));
            if (rows is null)
            {
                _indexLoaded = true;
                return;
            }

            foreach (IndexRow row in rows)
            {
                if (string.IsNullOrEmpty(row.Key))
                {
                    continue;
                }

                Entry e = new() { UsedUtc = row.UsedUtc };
                e.Small = ReadMaster(row.SmallFile, (IconRank)row.SmallRank, row.SmallSvg);
                e.Large = ReadMaster(row.LargeFile, (IconRank)row.LargeRank, row.LargeSvg);

                if (e.Small is null && e.Large is null)
                {
                    continue;
                }

                lock (Gate) { Entries[row.Key] = e; }
                RaiseUpdated(row.Key);
            }
        }
        catch (Exception ex)
        {
            // A corrupt cache costs you icons for one session, not your browser.
            Diagnostics.Swallowed(ex, "favicons: load index.json");
        }
        finally
        {
            _indexLoaded = true;
        }
    }

    private static Master? ReadMaster(string? file, IconRank rank, bool svg)
    {
        if (string.IsNullOrEmpty(file))
        {
            return null;
        }

        try
        {
            string path = Path.Combine(Folder, file);
            if (!File.Exists(path))
            {
                return null;
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0 || bytes.Length > MaxIconBytes)
            {
                return null;
            }

            return new Master { Bytes = bytes, Rank = rank, IsSvg = svg, File = file };
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "favicons: read cached icon");
            return null;
        }
    }

    private static void WriteToDisk(string key)
    {
        try
        {
            Directory.CreateDirectory(Folder);

            Master? small, large;
            lock (Gate)
            {
                if (!Entries.TryGetValue(key, out Entry? e))
                {
                    return;
                }

                small = e.Small;
                large = e.Large;
            }

            WriteMaster(small);
            WriteMaster(large);
        }
        catch (Exception ex)
        {
            // The in-memory copy still works for this session.
            Diagnostics.Swallowed(ex, "favicons: write icon to disk");
        }
    }

    private static void WriteMaster(Master? m)
    {
        if (m is null || m.File.Length == 0)
        {
            return;
        }

        try
        {
            string path = Path.Combine(Folder, m.File);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, m.Bytes);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "favicons: write master");
        }
    }

    /// <summary>Called on shutdown so the index and the LRU timestamps are not lost.</summary>
    public static Task FlushAsync() => Task.Run(SaveIndex);

    private static void SaveIndex()
    {
        try
        {
            if (!_indexLoaded)
            {
                return;
            }

            Evict();
            Directory.CreateDirectory(Folder);

            List<IndexRow> rows = new();
            lock (Gate)
            {
                foreach (KeyValuePair<string, Entry> pair in Entries)
                {
                    Entry e = pair.Value;
                    if (e.Small is null && e.Large is null)
                    {
                        continue;
                    }

                    rows.Add(new IndexRow
                    {
                        Key = pair.Key,
                        SmallFile = e.Small?.File,
                        SmallRank = (int)(e.Small?.Rank ?? IconRank.None),
                        SmallSvg = e.Small?.IsSvg ?? false,
                        LargeFile = e.Large?.File,
                        LargeRank = (int)(e.Large?.Rank ?? IconRank.None),
                        LargeSvg = e.Large?.IsSvg ?? false,
                        UsedUtc = e.UsedUtc,
                    });
                }

                _indexDirty = false;
            }

            string json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = false });
            string temp = IndexPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, IndexPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "favicons: save index.json");
        }
    }

    /// <summary>Oldest-used first, but never a site that is currently open in a tab.</summary>
    private static void Evict()
    {
        List<string> doomed = new();

        lock (Gate)
        {
            if (Entries.Count <= MaxEntries)
            {
                return;
            }

            foreach (KeyValuePair<string, Entry> pair in Entries
                         .Where(p => !Pinned.Contains(p.Key))
                         .OrderBy(p => p.Value.UsedUtc)
                         .Take(Entries.Count - MaxEntries))
            {
                doomed.Add(pair.Key);
            }

            foreach (string key in doomed)
            {
                if (Entries.Remove(key, out Entry? e))
                {
                    DeleteFile(e.Small);
                    DeleteFile(e.Large);
                }
            }
        }
    }

    private static void DeleteFile(Master? m)
    {
        if (m is null || m.File.Length == 0)
        {
            return;
        }

        try { File.Delete(Path.Combine(Folder, m.File)); } catch (Exception ex) { Diagnostics.Swallowed(ex, "favicons: delete evicted icon"); }
    }

    public static (int count, long bytes) Stats()
    {
        lock (Gate)
        {
            long bytes = 0;
            int count = 0;
            foreach (Entry e in Entries.Values)
            {
                if (e.Small is null && e.Large is null) { continue; }
                count++;
                bytes += (e.Small?.Bytes.Length ?? 0) + (e.Large?.Bytes.Length ?? 0);
            }

            return (count, bytes);
        }
    }

    public static void ClearAll()
    {
        lock (Gate)
        {
            Entries.Clear();
            Decoded.Clear();
        }

        try
        {
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "favicons: clear cache folder");
        }

        RaiseUpdated(string.Empty);
    }

    // ── SERVING ICONS TO OUR OWN HTML PAGES ─────────────────────────────────

    /// <summary>
    /// A per-launch random origin the internal pages fetch icons from.
    ///
    /// A NavigateToString document has an opaque origin and cannot load file:// at all, so
    /// the Start Page cannot point an img at the cache folder. Base64 in the markup would
    /// work but bloats the document. This synthetic host is answered inside
    /// WebResourceRequested and never reaches the network.
    /// </summary>
    public static string IconBase
    {
        get
        {
            if (_token is null)
            {
                byte[] rnd = RandomNumberGenerator.GetBytes(16);
                _token = Convert.ToHexString(rnd).ToLowerInvariant();
            }

            return "https://aviary.localhost/icon/" + _token + "/";
        }
    }

    /// <summary>
    /// Answers a synthetic-origin request from memory.
    ///
    /// Returns true for ANY request to aviary.localhost, including one with a bad token,
    /// which gets a 404. It must never fall through: if it did, Chromium would perform a
    /// real DNS lookup for aviary.localhost and a watching network would learn something.
    /// </summary>
    public static bool TryServe(string requestUri, CoreWebView2Environment env, out CoreWebView2WebResourceResponse? response)
    {
        response = null;

        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out Uri? uri)
            || !uri.Host.Equals("aviary.localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            string prefix = "/icon/" + (_token ?? string.Empty) + "/";
            if (_token is null || !uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal))
            {
                response = env.CreateWebResourceResponse(null, 404, "Not Found", string.Empty);
                return true;
            }

            string key = Uri.UnescapeDataString(uri.AbsolutePath.Substring(prefix.Length)).Trim('/');
            Entry? entry = Find(key);
            Master? master = entry is null ? null : (entry.Large ?? entry.Small);

            if (master is null)
            {
                response = env.CreateWebResourceResponse(null, 404, "Not Found", string.Empty);
                return true;
            }

            string mime = master.IsSvg ? "image/svg+xml" : "image/png";

            // NOT inside a using. WebView2 reads the stream after this method returns; the
            // response object keeps it alive. Disposing it here yields an empty image and
            // says nothing in the console.
            MemoryStream stream = new(master.Bytes);
            response = env.CreateWebResourceResponse(
                stream.AsRandomAccessStream(), 200, "OK",
                "Content-Type: " + mime + "\r\nCache-Control: no-store");
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "favicons: serve icon to internal page");

            try { response = env.CreateWebResourceResponse(null, 404, "Not Found", string.Empty); }
            catch (Exception inner) { response = null; Diagnostics.Swallowed(inner, "favicons: 404 for internal page"); }

            return true;
        }
    }

    /// <summary>
    /// Waits for a set of keys to be readable from memory, so TryServe can stay synchronous
    /// when the page it is serving loads.
    /// </summary>
    public static async Task PreloadAsync(IEnumerable<string> keys)
    {
        for (int i = 0; i < 20 && !_indexLoaded; i++)
        {
            await Task.Delay(25);
        }
    }
}
