using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aviary;

/// <summary>One page saved to read later.</summary>
internal sealed class ReadingItem
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset AddedUtc { get; set; }
    public bool Unread { get; set; } = true;

    [JsonIgnore]
    public string IconKey => Favicons.KeyFor(Url);

    /// <summary>
    /// "Added just now" / "14 minutes ago" / "yesterday" / "3 days ago" / a real date.
    /// Relative for the span where relative is genuinely more useful than absolute, and
    /// absolute after that — "added 143 days ago" is a number nobody can picture.
    /// </summary>
    [JsonIgnore]
    public string Age
    {
        get
        {
            TimeSpan since = DateTimeOffset.UtcNow - AddedUtc;

            if (since.TotalSeconds < 60) { return "Added just now"; }
            if (since.TotalMinutes < 60)
            {
                int m = (int)since.TotalMinutes;
                return "Added " + m + (m == 1 ? " minute ago" : " minutes ago");
            }

            if (since.TotalHours < 24)
            {
                int h = (int)since.TotalHours;
                return "Added " + h + (h == 1 ? " hour ago" : " hours ago");
            }

            if (since.TotalDays < 2) { return "Added yesterday"; }
            if (since.TotalDays < 7) { return "Added " + (int)since.TotalDays + " days ago"; }

            return "Added " + AddedUtc.ToLocalTime().ToString("d MMMM yyyy");
        }
    }
}

/// <summary>
/// Safari's Reading List, kept separate from bookmarks on purpose.
///
/// They look like the same feature and are not. A bookmark is a place you intend to return
/// to repeatedly; a reading-list item is a single article you mean to finish once and then
/// be done with. Merging them is what turns a bookmarks folder into a graveyard — which is
/// exactly why Apple split them in the first place.
///
/// Aviary does NOT save the page text for offline reading, which real Safari does. That
/// needs a fetch-and-rewrite pipeline for every subresource, and doing it badly gives you an
/// archive that renders wrong — worse than no archive. Reader mode covers the actual need
/// (strip it back so it is readable) and the article is fetched fresh. Revisit after beta.
/// </summary>
internal static class ReadingList
{
    private static readonly object Gate = new();
    private static List<ReadingItem>? _items;

    public static event Action? Changed;

    private static string FilePath => AppPaths.Of("readinglist.json");

    public static List<ReadingItem> Items
    {
        get
        {
            lock (Gate)
            {
                if (_items is not null)
                {
                    return _items;
                }

                _items = new List<ReadingItem>();

                try
                {
                    if (File.Exists(FilePath))
                    {
                        _items = JsonSerializer.Deserialize<List<ReadingItem>>(File.ReadAllText(FilePath))
                                 ?? new List<ReadingItem>();
                    }
                }
                catch (Exception ex)
                {
                    _items = new List<ReadingItem>();
                    Diagnostics.Swallowed(ex, "reading list: load readinglist.json");
                }

                return _items;
            }
        }
    }

    public static int UnreadCount
    {
        get { lock (Gate) { return Items.FindAll(i => i.Unread).Count; } }
    }

    public static bool Contains(string url) =>
        Items.Exists(i => string.Equals(i.Url, url, StringComparison.OrdinalIgnoreCase));

    public static void Add(string title, string url, bool record = true)
    {
        if (string.IsNullOrWhiteSpace(url) || Favicons.KeyFor(url).Length == 0)
        {
            return;
        }

        lock (Gate)
        {
            // Re-adding something already there marks it unread again rather than
            // duplicating it — you saved it a second time because you mean to read it.
            ReadingItem? existing = Items.Find(
                i => string.Equals(i.Url, url, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.Unread = true;
                existing.AddedUtc = DateTimeOffset.UtcNow;
            }
            else
            {
                // Newest first: the thing you just saved is the thing you are about to read.
                Items.Insert(0, new ReadingItem
                {
                    Url = url,
                    Title = string.IsNullOrWhiteSpace(title) ? BrowserTab.HostOf(url) : title,
                    AddedUtc = DateTimeOffset.UtcNow,
                    Unread = true,
                });
            }
        }

        if (record) { Diagnostics.Feature("reading-list-add", new { url, title }); }
        Save();
    }

    public static void Remove(string url)
    {
        int removed;
        lock (Gate)
        {
            removed = Items.RemoveAll(i => string.Equals(i.Url, url, StringComparison.OrdinalIgnoreCase));
        }

        Diagnostics.Feature("reading-list-remove", new { url, removed });
        Save();
    }

    public static void MarkRead(string url, bool read = true)
    {
        lock (Gate)
        {
            ReadingItem? item = Items.Find(
                i => string.Equals(i.Url, url, StringComparison.OrdinalIgnoreCase));

            if (item is null || item.Unread != read)
            {
                if (item is null) { return; }
            }

            item.Unread = !read;
        }

        Diagnostics.Feature(read ? "reading-list-mark-read" : "reading-list-mark-unread", new { url });
        Save();
    }

    public static void Clear()
    {
        lock (Gate) { Items.Clear(); }
        Save();
    }

    private static void Save()
    {
        try
        {
            string folder = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(folder);

            string json = JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true });
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "reading list: save readinglist.json");
        }

        Changed?.Invoke();
    }
}
