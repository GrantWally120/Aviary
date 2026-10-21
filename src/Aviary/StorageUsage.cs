using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Aviary;

/// <summary>
/// A snapshot of files in the installation and persistent profile, measured off the UI thread.
/// Each file's length and allocation are sampled together where Windows permits it. Shared
/// website databases remain shared; unreadable entries and allocation estimates are reported.
/// This is a scan over time, not an atomic image: directory metadata, alternate streams, other
/// Aviary installations, the shared WebView runtime and external downloads are outside its scope.
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

    public sealed record Result(IReadOnlyList<Category> Categories, long Bytes, long OnDisk, int Files, double Ms,
        string InstallFolder, string ProfileFolder, DateTimeOffset At, IReadOnlyList<Item> Sites, long SitesShared,
        DateTimeOffset StartedAt, int SkippedEntries, int SkippedDirectories, int ReparsePointsSkipped,
        int DiskEstimatedFiles, int HardLinkDuplicates, int SiteCount, long SitesOtherBytes)
    {
        public bool Complete => SkippedEntries == 0 && SkippedDirectories == 0;
    }

    private static readonly object MeasureGate = new();
    private static Task<Result>? _measurement;

    // One scan for all requesting Settings tabs/windows. Each caller still receives the result.
    // No retained snapshot cache: a new request after completion measures the files again.
    public static Task<Result> MeasureAsync()
    {
        lock (MeasureGate)
        {
            if (_measurement is null || _measurement.IsCompleted)
            {
                _measurement = MeasureAsync(AppContext.BaseDirectory, AppPaths.Root);
            }

            return _measurement;
        }
    }

    // Used after clearing a cache: an already-running scan may predate the clear operation.
    public static async Task<Result> MeasureAfterCurrentAsync()
    {
        Task<Result>? current;
        lock (MeasureGate)
        {
            current = _measurement;
        }

        if (current is { IsCompleted: false })
        {
            try { await current; }
            catch (Exception) { /* A fresh request can recover from a failed previous scan. */ }
        }

        return await MeasureAsync();
    }

    internal static Task<Result> MeasureAsync(string installFolder, string profileFolder, CancellationToken cancellation = default) =>
        Task.Run(() => Measure(installFolder, profileFolder, cancellation), cancellation);

    private static Result Measure(string installFolder, string profileFolder, CancellationToken cancellation)
    {
        Stopwatch timer = Stopwatch.StartNew();
        DateTimeOffset startedAt = DateTimeOffset.Now;
        string install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installFolder));
        string profile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profileFolder));
        ScanState state = new();

        Dictionary<string, (long Bytes, long Disk, int Files, Dictionary<string, (long, int)> Parts)> tally = Categories
            .ToDictionary(c => c.Key, _ => (0L, 0L, 0, new Dictionary<string, (long, int)>(StringComparer.OrdinalIgnoreCase)));

        void Add(string key, string part, FileSample sample)
        {
            long bytes = sample.Bytes;
            (long b, long d, int n, Dictionary<string, (long, int)> parts) = tally[key];
            parts[part] = parts.TryGetValue(part, out (long B, int N) p) ? (p.B + bytes, p.N + 1) : (bytes, 1);
            tally[key] = (b + bytes, d + sample.OnDisk, n + 1, parts);
        }

        // M4.6: what each SITE keeps, where the engine's own layout says whose it is. Only some
        // of it can be told apart: a site's databases sit in a folder named after its address
        // ("https_example.com_0.indexeddb.leveldb"), while cookies and Local Storage are single
        // shared databases with every site inside them. Both facts are reported rather than
        // guessed at - the shared part is a figure of its own, not spread over the sites.
        Dictionary<string, (long Bytes, int Files)> sites = new(StringComparer.OrdinalIgnoreCase);
        long shared = 0;

        bool sameRoot = install.Equals(profile, StringComparison.OrdinalIgnoreCase);
        // Exclude overlapping subtrees instead of enumerating them a second time. If an
        // installation is inside a scratch profile, those files still belong to the app.
        foreach (string file in Walk(profile, !sameRoot && IsWithin(install, profile) ? install : null, state, cancellation))
        {
            if (Sample(file, state) is FileSample sample)
            {
                string rel = Relative(profile, file);
                (string key, string part) = ClassifyProfile(rel);
                Add(key, part, sample);

                if (key == "sites")
                {
                    long size = sample.Bytes;
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

        foreach (string file in sameRoot ? Array.Empty<string>() : Walk(install, IsWithin(profile, install) ? profile : null, state, cancellation))
        {
            if (Sample(file, state) is FileSample sample)
            {
                (string key, string part) = ClassifyInstall(Relative(install, file));
                Add(key, part, sample);
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
            Math.Round(timer.Elapsed.TotalMilliseconds, 1), install, profile, DateTimeOffset.Now, bySite, shared,
            startedAt, state.SkippedEntries, state.SkippedDirectories, state.ReparsePointsSkipped,
            state.DiskEstimatedFiles, state.HardLinkDuplicates, sites.Count, sites.Sum(s => s.Value.Bytes) - bySite.Sum(s => s.Bytes));
    }

    private sealed class ScanState
    {
        public int SkippedEntries, SkippedDirectories, ReparsePointsSkipped, DiskEstimatedFiles, HardLinkDuplicates;
        public readonly HashSet<(uint Volume, ulong Id)> HardLinks = new();
    }

    private static bool IsWithin(string path, string root) =>
        path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> Walk(string root, string? excluded, ScanState state, CancellationToken cancellation)
    {
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            IEnumerator<string>? entries = null;
            try
            {
                // Recheck directories when visiting them: the engine can remove one while a
                // scan is in progress. Never recurse through junctions or symbolic links.
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    state.ReparsePointsSkipped++;
                    continue;
                }

                entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator();
            }
            catch (IOException) { state.SkippedDirectories++; }
            catch (UnauthorizedAccessException) { state.SkippedDirectories++; }

            if (entries is null) continue;
            using (entries)
            {
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string entry;
                    try
                    {
                        if (!entries.MoveNext()) break;
                        entry = entries.Current;
                    }
                    catch (IOException) { state.SkippedDirectories++; break; }
                    catch (UnauthorizedAccessException) { state.SkippedDirectories++; break; }

                    if (entry.Equals(excluded, StringComparison.OrdinalIgnoreCase)) continue;
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(entry); }
                    catch (IOException) { state.SkippedEntries++; continue; }
                    catch (UnauthorizedAccessException) { state.SkippedEntries++; continue; }

                    if ((attributes & FileAttributes.ReparsePoint) != 0) state.ReparsePointsSkipped++;
                    else if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                    else yield return entry;
                }
            }
        }
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

            int first = folder.IndexOf('_');
            int last = folder.LastIndexOf('_');
            if (first > 0 && last > first && int.TryParse(folder.AsSpan(last + 1), out int port) && port >= 0 && port <= 65535
                && (folder[..first].Equals("https", StringComparison.OrdinalIgnoreCase) || folder[..first].Equals("http", StringComparison.OrdinalIgnoreCase)))
            {
                // Host names may contain underscores. Splitting on every underscore silently
                // attributed foo_bar.example to foo, and folders without a port were guessed.
                string host = folder[(first + 1)..last];
                if (host.Length > 0) return host;
            }
        }

        return null;
    }

    private static string Relative(string root, string full) => Path.GetRelativePath(root, full).Replace('/', '\\');

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
        if (root.ToLowerInvariant() is "shadercache" or "grshadercache" or "graphitedawncache" or "gpucache" or "dawnwebgpucache" or "dawngraphitecache")
        {
            return ("cache", "Compiled graphics shaders");
        }

        if (inProfile.Length > 0)
        {
            switch (top.ToLowerInvariant())
            {
                case "cache":
                    return ("cache", "Pages, scripts and images");
                case "code cache":
                    return ("cache", "Compiled scripts");
                case "gpucache":
                case "dawngraphitecache":
                case "dawnwebgpucache":
                    return ("cache", "Compiled graphics shaders");
                case "indexeddb":
                    return ("sites", "Site databases (IndexedDB)");
                case "local storage":
                case "session storage":
                case "webstorage":
                    return ("sites", "Local storage");
                case "service worker":
                    return ("sites", "Offline copies (service workers)");
                case "file system":
                case "blob_storage":
                case "databases":
                case "shared dictionary":
                    return ("sites", "Other site storage");
                case "network":
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

            if (top.StartsWith("History", StringComparison.OrdinalIgnoreCase) || top.ToLowerInvariant() is "visited links" or "top sites" or "top sites-journal"
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

    // ── One metadata sample per file ────────────────────────────────────────────

    private readonly record struct FileSample(long Bytes, long OnDisk);

    private static FileSample? Sample(string path, ScanState state)
    {
        // Attribute-only access does not read the file or block the engine's open databases.
        // FILE_FLAG_OPEN_REPARSE_POINT also avoids following a file changed into a link after
        // enumeration. Windows reports actual allocated bytes, including a resident file's 0.
        if (OperatingSystem.IsWindows())
        {
            using SafeFileHandle handle = CreateFileW(path, 0, FileShare.Read | FileShare.Write | FileShare.Delete,
                IntPtr.Zero, 3 /* OPEN_EXISTING */, 0x00200000 /* OPEN_REPARSE_POINT */, IntPtr.Zero);
            if (!handle.IsInvalid && GetFileInformationByHandleEx(handle, 1 /* FileStandardInfo */,
                out FileStandardInfo info, (uint)Marshal.SizeOf<FileStandardInfo>()) && !info.Directory)
            {
                long allocation = Math.Max(0, info.AllocationSize);
                if (info.NumberOfLinks > 1)
                {
                    if (GetFileInformationByHandle(handle, out ByHandleFileInformation identity))
                    {
                        ulong id = ((ulong)identity.FileIndexHigh << 32) | identity.FileIndexLow;
                        if (!state.HardLinks.Add((identity.VolumeSerialNumber, id)))
                        {
                            allocation = 0;
                            state.HardLinkDuplicates++;
                        }
                    }
                    else state.DiskEstimatedFiles++; // allocation known, duplicate-link identity unavailable
                }

                return new FileSample(Math.Max(0, info.EndOfFile), allocation);
            }
        }

        try
        {
            FileInfo file = new(path);
            file.Refresh();
            long bytes = file.Length;
            state.DiskEstimatedFiles++;
            // A metadata-only query can be forbidden even while FileInfo can read the length.
            // Preserve that measured length and explicitly mark allocation as estimated.
            return new FileSample(bytes, bytes);
        }
        catch (IOException) { state.SkippedEntries++; return null; }
        catch (UnauthorizedAccessException) { state.SkippedEntries++; return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandardInfo
    {
        public long AllocationSize, EndOfFile;
        public uint NumberOfLinks;
        [MarshalAs(UnmanagedType.U1)] public bool DeletePending;
        [MarshalAs(UnmanagedType.U1)] public bool Directory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint access, FileShare share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out FileStandardInfo info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation info);
}
