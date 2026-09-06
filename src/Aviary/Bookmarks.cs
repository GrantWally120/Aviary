using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aviary;

internal sealed class Bookmark
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    /// <summary>Stand-in for a favicon until Phase 4 fetches real ones.</summary>
    [JsonIgnore]
    public string Monogram
    {
        get
        {
            string host = BrowserTab.HostOf(Url);
            return host.Length > 0 ? host.Substring(0, 1).ToUpperInvariant() : "•";
        }
    }

    /// <summary>
    /// Colour derived from the host rather than assigned in order, so a given site keeps
    /// the same colour across launches instead of shuffling when the list is reordered.
    /// </summary>
    [JsonIgnore]
    public string Accent
    {
        get
        {
            string host = BrowserTab.HostOf(Url);
            int hash = 0;
            foreach (char c in host)
            {
                hash = (hash * 31 + c) & 0x7FFFFFFF;
            }

            string[] palette =
            {
                "#0A84FF", "#FF9F0A", "#30D158", "#FF375F", "#BF5AF2",
                "#64D2FF", "#FF6482", "#5E5CE6", "#66D4CF", "#FFD60A"
            };

            return palette[hash % palette.Length];
        }
    }
}

/// <summary>
/// The favourites bar's contents, kept in %LOCALAPPDATA%\Aviary\bookmarks.json.
///
/// Its own file rather than a key in settings.json: this is a list that grows, and mixing
/// a growing collection into a flat preferences map is how preference files turn into a
/// junk drawer.
/// </summary>
internal static class Bookmarks
{
    private static readonly object Gate = new();
    private static List<Bookmark>? _items;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Aviary",
        "bookmarks.json");

    public static event Action? Changed;

    public static List<Bookmark> Items
    {
        get
        {
            lock (Gate)
            {
                if (_items is not null)
                {
                    return _items;
                }

                _items = new List<Bookmark>();

                try
                {
                    if (File.Exists(FilePath))
                    {
                        _items = JsonSerializer.Deserialize<List<Bookmark>>(File.ReadAllText(FilePath))
                                 ?? new List<Bookmark>();
                    }
                }
                catch (Exception)
                {
                    // A corrupt bookmarks file costs you your bookmarks, not your browser.
                    _items = new List<Bookmark>();
                }

                return _items;
            }
        }
    }

    public static bool Contains(string url) =>
        Items.Exists(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));

    public static void Add(string title, string url)
    {
        if (string.IsNullOrWhiteSpace(url) || Contains(url))
        {
            return;
        }

        Items.Add(new Bookmark
        {
            Title = string.IsNullOrWhiteSpace(title) ? BrowserTab.HostOf(url) : title,
            Url = url
        });

        Save();
    }

    public static void Remove(Bookmark bookmark)
    {
        Items.Remove(bookmark);
        Save();
    }

    private static void Save()
    {
        try
        {
            string folder = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(folder);

            string json = JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true });

            // Temp file then move, so an interrupted write cannot truncate the list.
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // Losing a bookmark is survivable; taking the browser down is not.
        }

        Changed?.Invoke();
    }
}
