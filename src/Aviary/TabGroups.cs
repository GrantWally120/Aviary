using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aviary;

/// <summary>One page in a tab group, as it is kept: where it was and what it was called.</summary>
internal sealed class GroupTab
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

/// <summary>A named set of tabs (Alpha Build 5 M5 stage 4).</summary>
internal sealed class TabGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<GroupTab> Tabs { get; set; } = new();

    /// <summary>Which of <see cref="Tabs"/> was in front when the group was last put away.</summary>
    public int Active { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The window showing this group right now, if one is. Never saved.</summary>
    [JsonIgnore]
    public MainWindow? OpenIn { get; set; }
}

/// <summary>
/// Safari's Tab Groups (Alpha Build 5 M5 stage 4, from PHASE5-PLAN: "named, persisted, switched from
/// the sidebar"). A group is a name and a list of pages; a window shows one group at a time, or its
/// own tabs. Kept in <c>tabgroups.json</c>, written temp-then-move, so a group is still there after
/// Aviary is closed — which is the whole difference between a group and a window.
///
/// <para><b>Pages, not tabs, are kept.</b> A group remembers each tab's address and title and which
/// one was in front; the tabs themselves are closed when the group is put away and come back
/// dormant when it is chosen (<c>MainWindow.TabGroups.cs</c>). A Start Page is kept as a Start Page (an
/// empty address); Aviary's other pages (Settings and the like) are not kept.</para>
///
/// <para>Private windows have no tab groups — Safari does not offer them there either: a group is a
/// thing that is kept, and a private window keeps nothing.</para>
/// </summary>
internal static class TabGroups
{
    private static readonly object Gate = new();
    private static List<TabGroup>? _groups;

    public static event Action? Changed;

    private static string FilePath => AppPaths.Of("tabgroups.json");

    public static List<TabGroup> All
    {
        get
        {
            lock (Gate)
            {
                if (_groups is not null)
                {
                    return _groups;
                }

                _groups = new List<TabGroup>();
                try
                {
                    if (File.Exists(FilePath))
                    {
                        _groups = JsonSerializer.Deserialize<List<TabGroup>>(File.ReadAllText(FilePath)) ?? new();
                    }
                }
                catch (Exception ex)
                {
                    _groups = new List<TabGroup>();
                    Diagnostics.Swallowed(ex, "tab groups: load tabgroups.json");
                }

                return _groups;
            }
        }
    }

    public static TabGroup? Find(string id)
    {
        lock (Gate)
        {
            return All.FirstOrDefault(g => g.Id == id);
        }
    }

    /// <summary>"Untitled Tab Group", then "Untitled Tab Group 2" and so on — Safari's names.</summary>
    public static string NextName()
    {
        lock (Gate)
        {
            const string basic = "Untitled Tab Group";
            if (!All.Any(g => g.Name == basic))
            {
                return basic;
            }

            for (int n = 2; ; n++)
            {
                if (!All.Any(g => g.Name == basic + " " + n))
                {
                    return basic + " " + n;
                }
            }
        }
    }

    public static TabGroup Create(string name, List<GroupTab> tabs, int active)
    {
        TabGroup group = new()
        {
            Name = Clean(name),
            Tabs = tabs,
            Active = Math.Clamp(active, 0, Math.Max(0, tabs.Count - 1)),
        };

        lock (Gate)
        {
            All.Add(group);
        }

        Save();
        return group;
    }

    public static void Rename(TabGroup group, string name)
    {
        group.Name = Clean(name);
        group.UpdatedUtc = DateTimeOffset.UtcNow;
        Save();
    }

    public static void Delete(TabGroup group)
    {
        lock (Gate)
        {
            All.Remove(group);
        }

        Save();
    }

    /// <summary>What a group holds now (see MainWindow.Snapshot for what is kept).</summary>
    public static void Update(TabGroup group, List<GroupTab> tabs, int active)
    {
        group.Tabs = tabs;
        group.Active = Math.Clamp(active, 0, Math.Max(0, tabs.Count - 1));
        group.UpdatedUtc = DateTimeOffset.UtcNow;
        Save();
    }

    /// <summary>A page put into a group from outside it ("Move to Tab Group").</summary>
    public static void Append(TabGroup group, GroupTab tab)
    {
        group.Tabs.Add(tab);
        group.UpdatedUtc = DateTimeOffset.UtcNow;
        Save();
    }

    /// <summary>A name as typed: trimmed, one line, not absurdly long; empty becomes the next free name.</summary>
    private static string Clean(string name)
    {
        string one = string.Join(' ', (name ?? string.Empty).Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (one.Length > 60)
        {
            one = one.Substring(0, 60).TrimEnd();
        }

        return one.Length > 0 ? one : NextName();
    }

    /// <summary>Written temp-then-move, and every list that shows groups hears about it.</summary>
    private static void Save()
    {
        try
        {
            string json;
            lock (Gate)
            {
                json = JsonSerializer.Serialize(All, new JsonSerializerOptions { WriteIndented = true });
            }

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab groups: save tabgroups.json");
        }

        Changed?.Invoke();
    }
}
