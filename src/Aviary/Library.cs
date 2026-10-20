using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// The Collection — Grant's own projects, bundled with Aviary and served from its install folder
/// at https://collection.aviary/ (Phase 5, Alpha Build 5 M2.5). Built as "the Menagerie" at
/// menagerie.aviary and renamed the same day at Grant's ask; the old address still works (see
/// <see cref="LegacyHost"/>).
///
/// <para>WebView2's virtual host mapping answers that name from disk inside the engine; the name is
/// never looked up on the network, so every exhibit runs with no internet at all. DenyCors: a page
/// in the collection loads its own files normally, and any other site can at most show one of its
/// images — never read a file through fetch.</para>
///
/// <para>Everything under <c>Library\files</c> is a COPY made by <c>tools/library/sync.ps1</c>, laid out
/// exactly as it sits under Documents so every relative link in Grant's pages still resolves (the
/// Wally Hub's "../Sliver a Wally2TTD Experience/…" among them). His originals are only ever read:
/// they are history, and none of them is changed to make it run here.</para>
/// </summary>
internal static class Library
{
    public const string Host = "collection.aviary";

    /// <summary>
    /// The address M2.5 first shipped under, when the Collection was called the Menagerie. Still
    /// mapped, so a history entry, a Frequently Visited tile or a bookmark made with it neither
    /// fails nor sends the name to a DNS server; a page opened there is moved to <see cref="Host"/>
    /// (<see cref="RedirectFor"/>).
    /// </summary>
    public const string LegacyHost = "menagerie.aviary";

    /// <summary>The library's root, which every exhibit address is relative to.</summary>
    public const string Root = "https://" + Host + "/";

    /// <summary>The Collection page itself. A file, not the folder: see <see cref="RedirectFor"/>.</summary>
    public const string Home = Root + "index.html";

    public const string Name = "The Collection";

    /// <summary>...\Aviary\Library, beside Aviary.exe.</summary>
    public static string Folder { get; } = Path.Combine(AppContext.BaseDirectory, "Library");

    /// <summary>False for a build that carries no library (a fresh clone that never ran the sync).</summary>
    public static bool Available => File.Exists(Path.Combine(Folder, "index.html"));

    /// <summary>
    /// Maps the host on one tab's engine. Every tab, before its first navigation — a mapping is
    /// per-CoreWebView2, private windows included.
    /// </summary>
    public static void Attach(CoreWebView2 core)
    {
        if (!Available)
        {
            return;
        }

        try
        {
            core.SetVirtualHostNameToFolderMapping(Host, Folder, CoreWebView2HostResourceAccessKind.DenyCors);
            core.SetVirtualHostNameToFolderMapping(LegacyHost, Folder, CoreWebView2HostResourceAccessKind.DenyCors);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "library: map host");
        }
    }

    public static bool IsLibraryHost(string host) =>
        string.Equals(host, Host, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, LegacyHost, StringComparison.OrdinalIgnoreCase);

    public static bool IsLibraryUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && IsLibraryHost(uri.Host);

    /// <summary>
    /// A library address as a path on disk, or null when it is not one — refusing anything that
    /// resolves outside the folder (a "..", an encoded one, a drive letter).
    /// </summary>
    private static string? DiskPath(Uri uri)
    {
        if (!IsLibraryHost(uri.Host))
        {
            return null;
        }

        string relative = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        string root = Path.GetFullPath(Folder) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || full + Path.DirectorySeparatorChar == root ? full : null;
    }

    /// <summary>
    /// Where a library address should really go, or null when it is fine as it is (or is not the
    /// library at all). Two cases: a FOLDER holding an index.html opens that page, as a web server
    /// would; and any address on <see cref="LegacyHost"/> moves to the same path on
    /// <see cref="Host"/>, so the address bar never shows the old name.
    /// </summary>
    public static string? RedirectFor(string url)
    {
        try
        {
            if (!Available || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || DiskPath(uri) is not { } full)
            {
                return null;
            }

            bool legacy = string.Equals(uri.Host, LegacyHost, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(full) && File.Exists(Path.Combine(full, "index.html")))
            {
                string path = uri.AbsolutePath.EndsWith('/') ? uri.AbsolutePath : uri.AbsolutePath + "/";
                return "https://" + Host + path + "index.html" + uri.Query + uri.Fragment;
            }

            return legacy ? "https://" + Host + uri.PathAndQuery + uri.Fragment : null;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "library: folder index");
            return null;
        }
    }

    /// <summary>
    /// A library file's bytes for a https://collection.aviary/… address, for the parts of Aviary that
    /// fetch outside the engine (site icons). Refuses any path that resolves outside the folder.
    /// </summary>
    public static bool TryRead(string url, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        try
        {
            if (DiskPath(uri) is not { } full || !File.Exists(full))
            {
                return false;
            }

            bytes = File.ReadAllBytes(full);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "library: read file");
            return false;
        }
    }

    /// <summary>One Start Page tile: an exhibit the catalog marks <c>featured</c>.</summary>
    public sealed record Featured(string Title, string Url, string Thumb, string Accent, string Note, string Mono);

    private static (DateTime Stamp, IReadOnlyList<Featured> Items, int Count)? _featured;

    /// <summary>
    /// The exhibits catalog.json marks featured, in its order, plus the total exhibit count —
    /// read from the same file the Collection page itself renders from, so the two cannot drift.
    /// Re-read only when the file changes.
    /// </summary>
    public static (IReadOnlyList<Featured> Items, int Count) FeaturedExhibits()
    {
        string path = Path.Combine(Folder, "catalog.json");
        try
        {
            if (!File.Exists(path))
            {
                return (Array.Empty<Featured>(), 0);
            }

            DateTime stamp = File.GetLastWriteTimeUtc(path);
            if (_featured is { } cached && cached.Stamp == stamp)
            {
                return (cached.Items, cached.Count);
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            List<Featured> items = new();
            int count = 0;
            foreach (JsonElement e in doc.RootElement.GetProperty("exhibits").EnumerateArray())
            {
                count++;
                if (!e.TryGetProperty("featured", out JsonElement f) || f.ValueKind != JsonValueKind.True)
                {
                    continue;
                }

                string id = e.GetProperty("id").GetString() ?? "";
                string open = e.TryGetProperty("open", out JsonElement o) ? o.GetString() ?? "" : "";
                string url = Root + EscapePath(open);
                // Source-only CI copies keep the catalog, but not personal payloads. Their
                // Start Page must not offer an exhibit that this copy cannot open.
                if (DiskPath(new Uri(url)) is not { } disk || !File.Exists(disk)) { continue; }
                items.Add(new Featured(
                    Title: e.GetProperty("title").GetString() ?? id,
                    Url: url,
                    Thumb: Root + "thumbs/" + id + ".jpg",
                    Accent: e.TryGetProperty("accent", out JsonElement a) ? a.GetString() ?? "#888" : "#888",
                    Note: e.TryGetProperty("short", out JsonElement s) ? s.GetString() ?? "" : "",
                    Mono: e.TryGetProperty("mono", out JsonElement m) ? m.GetString() ?? "" : ""));
            }

            _featured = (stamp, items, count);
            return (items, count);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "library: read catalog");
            return (Array.Empty<Featured>(), 0);
        }
    }

    /// <summary>"files/Claude CoWork Enclosed/Game Ledger.html" → each segment escaped, slashes kept.</summary>
    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
