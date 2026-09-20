using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Aviary;

/// <summary>
/// Settings → Storage Usage (Alpha Build 5 M4.5): every byte Aviary keeps on this computer, in
/// eight categories, measured from the files themselves.
///
/// <para><b>Accurate, not estimated.</b> Every file under Aviary's own folder and its profile is
/// counted exactly once, in the first category whose rule claims it — so the categories add up to
/// the total to the byte, and anything no rule claims lands in "Everything else" rather than being
/// dropped. Each file's size is asked of the file itself (a refreshed FileInfo), never read from
/// the folder listing: a listing holds what Windows noted when a file was last CLOSED, and the
/// engine keeps its databases open all session (bug 63's lesson). Size on disk is the space the
/// file really occupies — GetCompressedFileSize (compressed and sparse files) rounded up to the
/// volume's cluster.</para>
///
/// <para>Measured off the UI thread when the pane opens and on Refresh; nothing is recorded.</para>
/// </summary>
internal static class StorageUsage
{
    /// <summary>The categories, in their fixed order — the order of the chart's colours.</summary>
    public static readonly (string Key, string Name, string Plain)[] Categories =
    {
        ("app", "Aviary itself", "The program: Aviary, the .NET and Windows App SDK parts it carries, and the filter lists it ships with."),
        ("collection", "The Collection", "Grant’s projects, carried inside Aviary so they run offline."),
        ("sites", "Website data", "What websites have stored in this profile: cookies, local storage, databases and offline copies."),
        ("cache", "Cached files", "Copies of pages, scripts and images kept so sites open faster, and the graphics shaders the engine has compiled. Safe to clear — they come back as you browse."),
        ("yours", "Your Aviary data", "History, favourites, the Reading List, settings, site choices, the Privacy Report, site icons and updated filter lists."),
        ("diagnostics", "Improvement data", "The folder “Allow data collection for improvements” fills, and crash reports."),
        ("secrets", "Passwords and form entries", "Saved passwords and what forms remember, encrypted with your Windows account."),
        ("other", "Everything else", "The browser engine’s own records: its history index, crash handler, components and safe-browsing data."),
    };

    public sealed record Item(string Label, long Bytes, int Files);

    public sealed record Category(string Key, string Name, string Plain, long Bytes, long OnDisk, int Files, IReadOnlyList<Item> Biggest);

    public sealed record Result(IReadOnlyList<Category> Categories, long Bytes, long OnDisk, int Files, double Ms, string InstallFolder, string ProfileFolder, DateTimeOffset At, IReadOnlyList<Item> Sites, long SitesShared);

    public static Task<Result> MeasureAsync() => Task.Run(Measure);

    private static Result Measure()
    {
        Stopwatch timer = Stopwatch.StartNew();
        string install = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\', '/');
        string profile = Path.GetFullPath(AppPaths.Root).TrimEnd('\\', '/');

        Dictionary<string, (long Bytes, long Disk, int Files, Dictionary<string, (long, int)> Parts)> tally = Categories
            .ToDictionary(c => c.Key, _ => (0L, 0L, 0, new Dictionary<string, (long, int)>(StringComparer.OrdinalIgnoreCase)));

        void Add(string key, string part, FileInfo file)
        {
            long bytes;
            try
            {
                file.Refresh();                 // the file's own size, not the listing's
                bytes = file.Length;
            }
            catch (Exception)
            {
                return;                         // deleted between listing and measuring
            }

            (long b, long d, int n, Dictionary<string, (long, int)> parts) = tally[key];
            parts[part] = parts.TryGetValue(part, out (long B, int N) p) ? (p.B + bytes, p.N + 1) : (bytes, 1);
            tally[key] = (b + bytes, d + OnDisk(file.FullName, bytes), n + 1, parts);
        }

        // M4.6: what each SITE keeps, where the engine's own layout says whose it is. Only some
        // of it can be told apart: a site's databases sit in a folder named after its address
        // ("https_example.com_0.indexeddb.leveldb"), while cookies and Local Storage are single
        // shared databases with every site inside them. Both facts are reported rather than
        // guessed at - the shared part is a figure of its own, not spread over the sites.
        Dictionary<string, (long Bytes, int Files)> sites = new(StringComparer.OrdinalIgnoreCase);
        long shared = 0;

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        // The profile first: when the profile lives inside the install folder (a test instance can),
        // its files belong to the profile's categories, and "seen" stops the install walk counting them twice.
        foreach (FileInfo f in Walk(profile))
        {
            if (seen.Add(f.FullName))
            {
                string rel = Relative(profile, f.FullName);
                (string key, string part) = ClassifyProfile(rel);
                Add(key, part, f);

                if (key == "sites")
                {
                    long size;
                    try
                    {
                        f.Refresh();
                        size = f.Length;
                    }
                    catch (Exception)
                    {
                        size = 0;
                    }

                    if (SiteOfPath(rel) is { Length: > 0 } site)
                    {
                        sites[site] = sites.TryGetValue(site, out (long B, int N) had) ? (had.B + size, had.N + 1) : (size, 1);
                    }
                    else
                    {
                        shared += size;
                    }
                }
            }
        }

        foreach (FileInfo f in Walk(install))
        {
            if (seen.Add(f.FullName))
            {
                (string key, string part) = ClassifyInstall(Relative(install, f.FullName));
                Add(key, part, f);
            }
        }

        List<Category> categories = Categories.Select(c =>
        {
            (long bytes, long disk, int files, Dictionary<string, (long, int)> parts) = tally[c.Key];
            List<Item> biggest = parts
                .Select(p => new Item(p.Key, p.Value.Item1, p.Value.Item2))
                .OrderByDescending(i => i.Bytes)
                .Take(6)
                .ToList();
            return new Category(c.Key, c.Name, c.Plain, bytes, disk, files, biggest);
        }).ToList();

        List<Item> bySite = sites
            .Select(p => new Item(p.Key, p.Value.Bytes, p.Value.Files))
            .OrderByDescending(i => i.Bytes)
            .Take(12)
            .ToList();

        return new Result(categories, categories.Sum(c => c.Bytes), categories.Sum(c => c.OnDisk), categories.Sum(c => c.Files),
            Math.Round(timer.Elapsed.TotalMilliseconds, 1), install, profile, DateTimeOffset.Now, bySite, shared);
    }

    private static IEnumerable<FileInfo> Walk(string root)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<FileInfo>();
        }

        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,   // never follow a link out of the folder
            ReturnSpecialDirectories = false,
        };

        return new DirectoryInfo(root).EnumerateFiles("*", options);
    }

    /// <summary>
    /// The site a website-data file belongs to, when the engine's own folder name says so, or null
    /// when the file is one of the shared stores. Chromium names an origin's folder
    /// "https_example.com_0.indexeddb.leveldb" - scheme, host, port - and uses the same shape for
    /// a few other per-origin stores.
    /// </summary>
    private static string? SiteOfPath(string rel)
    {
        foreach (string mark in new[] { "\\IndexedDB\\", "\\File System\\", "\\WebStorage\\" })
        {
            int at = rel.IndexOf(mark, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                continue;
            }

            string rest = rel.Substring(at + mark.Length);
            int slash = rest.IndexOf('\\');
            string folder = slash > 0 ? rest.Substring(0, slash) : rest;

            // Strip what the engine adds after the origin, then read the origin itself.
            int dot = folder.IndexOf(".indexeddb", StringComparison.OrdinalIgnoreCase);
            if (dot > 0)
            {
                folder = folder.Substring(0, dot);
            }

            string[] parts = folder.Split('_');
            if (parts.Length >= 2 && (parts[0].Equals("https", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("http", StringComparison.OrdinalIgnoreCase)))
            {
                return parts[1];
            }
        }

        return null;
    }

    private static string Relative(string root, string full) =>
        full.Length > root.Length ? full.Substring(root.Length + 1).Replace('/', '\\') : string.Empty;

    // ── Which category a file is ────────────────────────────────────────────────

    private static (string Key, string Part) ClassifyInstall(string rel)
    {
        if (StartsWith(rel, "Library\\"))
        {
            return ("collection", "The Collection");
        }

        if (StartsWith(rel, "Filters\\"))
        {
            return ("app", "Filter lists shipped with Aviary");
        }

        if (!rel.Contains('\\'))
        {
            return ("app", "Program files");
        }

        // The Windows App SDK carries a folder of translations per language (en-US, ru-RU, ml-IN…):
        // dozens of tiny folders, one line between them.
        string top = First(rel);
        return ("app", System.Text.RegularExpressions.Regex.IsMatch(top, "^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$")
            ? "Translations of Windows’ own parts"
            : "Program files — " + top);
    }

    private static (string Key, string Part) ClassifyProfile(string rel)
    {
        if (StartsWith(rel, "user.data.improvementcollection\\"))
        {
            string inner = rel.Substring("user.data.improvementcollection\\".Length);
            return ("diagnostics", StartsWith(inner, "archive\\") ? "Older, archived records"
                : StartsWith(inner, "crashes\\") ? "Crash reports (collected)"
                : "Improvement data — " + inner);
        }

        if (rel.StartsWith("crash", StringComparison.OrdinalIgnoreCase) && !rel.Contains('\\'))
        {
            return ("diagnostics", "Crash reports");
        }

        if (StartsWith(rel, "favicons\\"))
        {
            return ("yours", "Site icons");
        }

        if (StartsWith(rel, "filters\\"))
        {
            return ("yours", "Updated filter lists");
        }

        if (!rel.Contains('\\'))
        {
            string name = Path.GetFileNameWithoutExtension(rel).ToLowerInvariant();
            return ("yours", name switch
            {
                "history" => "History",
                "bookmarks" => "Favourites",
                "readinglist" => "Reading List",
                "settings" => "Settings",
                "sites" => "Site choices",
                "privacy" => "Privacy Report",
                "pinned" => "Pinned tabs",
                _ => "Other Aviary files",
            });
        }

        if (StartsWith(rel, "WebView2\\"))
        {
            return ClassifyEngine(rel.Substring("WebView2\\".Length));
        }

        return ("other", "Other — " + First(rel));
    }

    /// <summary>The engine's profile folder (Chromium's layout, under EBWebView\).</summary>
    private static (string Key, string Part) ClassifyEngine(string rel)
    {
        // EBWebView\Default\… (the profile) and EBWebView\… (shared across profiles).
        string inProfile = string.Empty;
        string[] segments = rel.Split('\\');
        if (segments.Length >= 3 && segments[0].Equals("EBWebView", StringComparison.OrdinalIgnoreCase))
        {
            inProfile = string.Join('\\', segments.Skip(2));
        }

        string top = segments.Length >= 3 ? segments[2] : string.Empty;
        string root = segments.Length >= 2 ? segments[1] : string.Empty;

        // Shader caches live beside the profiles.
        if (root is "ShaderCache" or "GrShaderCache" or "GraphiteDawnCache")
        {
            return ("cache", "Compiled graphics shaders");
        }

        if (inProfile.Length > 0)
        {
            switch (top)
            {
                case "Cache":
                    return ("cache", "Pages, scripts and images");
                case "Code Cache":
                    return ("cache", "Compiled scripts");
                case "GPUCache":
                case "DawnGraphiteCache":
                case "DawnWebGPUCache":
                    return ("cache", "Compiled graphics shaders");
                case "IndexedDB":
                    return ("sites", "Site databases (IndexedDB)");
                case "Local Storage":
                case "Session Storage":
                case "WebStorage":
                    return ("sites", "Local storage");
                case "Service Worker":
                    return ("sites", "Offline copies (service workers)");
                case "File System":
                case "blob_storage":
                case "databases":
                case "Shared Dictionary":
                    return ("sites", "Other site storage");
                case "Network":
                    return segments.Length >= 4 && segments[3].StartsWith("Cookies", StringComparison.OrdinalIgnoreCase)
                        ? ("sites", "Cookies")
                        : ("other", "Engine — network state");
            }

            if (top.StartsWith("Login Data", StringComparison.OrdinalIgnoreCase))
            {
                return ("secrets", "Saved passwords");
            }

            if (top.StartsWith("Web Data", StringComparison.OrdinalIgnoreCase) || top.StartsWith("Account Web Data", StringComparison.OrdinalIgnoreCase))
            {
                return ("secrets", "Form entries");
            }

            if (top.StartsWith("History", StringComparison.OrdinalIgnoreCase) || top is "Visited Links" or "Top Sites" or "Top Sites-journal"
                || top.StartsWith("Favicons", StringComparison.OrdinalIgnoreCase))
            {
                return ("other", "Engine’s own history and icons");
            }

            return ("other", "Engine — " + top);
        }

        return ("other", "Engine — " + (root.Length > 0 ? root : segments[0]));
    }

    private static bool StartsWith(string rel, string prefix) => rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static string First(string rel)
    {
        int slash = rel.IndexOf('\\');
        return slash < 0 ? rel : rel.Substring(0, slash);
    }

    // ── Size on disk ────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, long> ClusterByVolume = new(StringComparer.OrdinalIgnoreCase);

    private static long OnDisk(string path, long bytes)
    {
        if (bytes == 0)
        {
            return 0;
        }

        uint high;
        uint low = GetCompressedFileSizeW(path, out high);
        long allocated = low == uint.MaxValue && Marshal.GetLastWin32Error() != 0 ? bytes : ((long)high << 32) | low;

        long cluster = ClusterOf(path);
        return cluster <= 0 ? allocated : (allocated + cluster - 1) / cluster * cluster;
    }

    private static long ClusterOf(string path)
    {
        string root = Path.GetPathRoot(path) ?? string.Empty;
        lock (ClusterByVolume)
        {
            if (ClusterByVolume.TryGetValue(root, out long cached))
            {
                return cached;
            }

            long cluster = GetDiskFreeSpaceW(root, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _)
                ? (long)sectorsPerCluster * bytesPerSector
                : 4096;
            ClusterByVolume[root] = cluster;
            return cluster;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string fileName, out uint fileSizeHigh);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceW(string rootPath, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);
}
