using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Aviary;

/// <summary>
/// Everything the browser remembers between launches, in one small JSON file next to the
/// WebView2 profile: %LOCALAPPDATA%\Aviary\settings.json
///
/// Deliberately a flat string dictionary rather than a typed class. Settings is a
/// data-driven page whose rows change every phase; a typed model would mean editing three
/// files to add one switch, and a forgotten property means a setting that silently stops
/// being saved. Values are stored as strings because that is what the settings page sends
/// and what it needs back to render.
/// </summary>
internal static class AppSettings
{
    private static readonly object Gate = new();
    private static Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aviary");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>
    /// Defaults. "Classic" is the opening look for a fresh install; after that whatever
    /// was last chosen wins, because the saved value overwrites this.
    /// </summary>
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["look"] = "Classic",
        ["theme"] = "System",
        ["searchEngine"] = "DuckDuckGo",
        ["homeButton"] = "true",
        ["fullUrl"] = "false",
        ["blockTrackers"] = "true",
        ["memorySaver"] = "true",
        ["startup"] = "Start Page",
        ["zoom"] = "100%",
        ["dnt"] = "true",
        ["safariUa"] = "true",
        ["thirdPartyCookies"] = "true",
    };

    public static void Load()
    {
        lock (Gate)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _values = new Dictionary<string, string>(Defaults, StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!File.Exists(FilePath))
                {
                    return;
                }

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                {
                    _values[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        _ => property.Value.ToString()
                    };
                }
            }
            catch (Exception)
            {
                // A corrupt settings file should cost you your preferences, not your
                // browser. Defaults are already in place; carry on.
            }
        }
    }

    public static string Get(string key)
    {
        Load();
        lock (Gate)
        {
            return _values.TryGetValue(key, out string? value) ? value
                 : Defaults.TryGetValue(key, out string? fallback) ? fallback
                 : string.Empty;
        }
    }

    public static bool GetBool(string key) =>
        string.Equals(Get(key), "true", StringComparison.OrdinalIgnoreCase);

    public static void Set(string key, string value)
    {
        Load();
        lock (Gate)
        {
            _values[key] = value;
        }

        Save();
    }

    /// <summary>
    /// Written on every change rather than at shutdown. The file is tiny, and a browser
    /// that loses your preferences because it crashed is a browser that feels unreliable.
    /// </summary>
    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);

            Dictionary<string, string> snapshot;
            lock (Gate)
            {
                snapshot = new Dictionary<string, string>(_values);
            }

            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });

            // Write beside the target and move into place, so an interrupted write cannot
            // leave a half-written settings file behind.
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // Losing a preference is survivable. Taking the browser down over it is not.
        }
    }
}
