using System;
using System.IO;
using System.Text.Json.Nodes;

namespace Aviary;

/// <summary>
/// Settings → Accessibility → Minimum font size (Alpha Build 5 M6 stage 5), through the browser engine's
/// OWN setting: Chromium's <c>webkit.webprefs.minimum_font_size</c> — the one Chrome and Edge show as
/// "Minimum font size". WebView2 has no API for it, and CSS cannot say "at least this big" without
/// flattening every size on a page to one, so it is written into the engine's Preferences file before the
/// engine starts, which is where the engine reads it from.
///
/// <para><b>Measured first</b> (26 Sep, a scratch profile): with 16 written there, text of 8 and 12 px drew
/// at 16 and 20 px stayed 20 — getComputedStyle and the boxes agreeing — and the engine kept the value
/// through its own run and its own rewrite of the file.</para>
///
/// <para>Written only when it differs, only before the engine exists (WebViewEnvironment.GetAsync), and
/// through a temporary file, so the engine's file is never left half-written. Nothing else in the file is
/// touched. Aviary is the only thing that sets this key: WebView2 shows no settings of its own.</para>
/// </summary>
internal static class EnginePreferences
{
    /// <summary>The engine's settings for its default profile — every tab's, the private ones layered over it.</summary>
    private static string FilePath => Path.Combine(WebViewEnvironment.UserDataFolder, "EBWebView", "Default", "Preferences");

    /// <summary>"None" or "12px"…"18px", from Settings.</summary>
    public static void ApplyMinimumFontSize(string choice)
    {
        try
        {
            int size = choice.EndsWith("px", StringComparison.OrdinalIgnoreCase)
                       && int.TryParse(choice.AsSpan(0, choice.Length - 2), out int px) && px is > 0 and <= 72
                ? px
                : 0;

            string path = FilePath;
            JsonObject root;
            if (File.Exists(path))
            {
                root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("not an object");
            }
            else if (size == 0)
            {
                // A profile the engine has not started in yet, and nothing asked for: nothing to do.
                return;
            }
            else
            {
                root = new JsonObject();
            }

            if (root["webkit"] is not JsonObject webkit)
            {
                webkit = new JsonObject();
                root["webkit"] = webkit;
            }

            if (webkit["webprefs"] is not JsonObject prefs)
            {
                prefs = new JsonObject();
                webkit["webprefs"] = prefs;
            }

            int now = prefs["minimum_font_size"] is JsonValue value && value.TryGetValue(out int n) ? n : 0;
            if (now == size)
            {
                return;
            }

            if (size == 0)
            {
                prefs.Remove("minimum_font_size");
            }
            else
            {
                prefs["minimum_font_size"] = size;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".aviary-tmp";
            File.WriteAllText(temp, root.ToJsonString());
            File.Move(temp, path, overwrite: true);
            Diagnostics.Feature("engine-minimum-font-size", new { from = now, to = size });
        }
        catch (Exception ex)
        {
            // The worst case is the engine's own default - no minimum - for this run, with the reason in
            // diagnostics. Never a damaged file: the real one is only ever replaced whole.
            Diagnostics.Swallowed(ex, "engine: minimum font size");
        }
    }
}
