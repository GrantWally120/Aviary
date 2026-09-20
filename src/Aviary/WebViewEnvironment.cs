using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// The one browser environment every tab shares.
///
/// This is the single most important performance decision in the whole project. One
/// WebView2 per tab is correct — it is what gives each tab its own back/forward history
/// and its own crash isolation — but each ENVIRONMENT spins up a separate browser process,
/// GPU process and audio process. Share one environment and all the tabs share that fixed
/// overhead, leaving only the per-renderer cost. Create one per tab and the app is
/// unusable somewhere around fifteen tabs.
///
/// Hence: created once, awaited by everyone, never recreated — which is also why the engine
/// switches below are read ONCE, at creation, and a change to one of them takes effect the
/// next time Aviary starts. Settings says so on each of those rows.
/// </summary>
internal static class WebViewEnvironment
{
    private static Task<CoreWebView2Environment>? _creation;
    private static readonly object Gate = new();

    /// <summary>
    /// %LOCALAPPDATA%\Aviary\WebView2 — cookies, cache, local storage, service workers.
    ///
    /// Not the default (next to the .exe): Aviary ships unpackaged and may well be
    /// installed somewhere protected, where the default folder is unwritable and
    /// environment creation fails outright with "Access Denied".
    /// </summary>
    public static string UserDataFolder { get; } = AppPaths.Of("WebView2");

    /// <summary>
    /// The settings the running engine was STARTED with, keyed like settings.json. Settings
    /// compares the saved value against this to say "takes effect when Aviary restarts".
    /// </summary>
    public static IReadOnlyDictionary<string, bool> LaunchedWith => _launchedWith;

    private static Dictionary<string, bool> _launchedWith = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The exact switches passed to the engine, for diagnostics.</summary>
    public static string Arguments { get; private set; } = string.Empty;

    /// <summary>The settings that are engine switches — each needs a restart to change.</summary>
    public static readonly string[] RestartKeys = { "thirdPartyCookies", "gpc", "webrtcProtect", "reduceMotion" };

    /// <summary>The engine's compiled-shader and GPU caches, which it opens at launch and holds.</summary>
    private static readonly string[] GpuCaches =
    {
        "ShaderCache", "GrShaderCache", "GraphiteDawnCache", "GPUCache", "DawnWebGPUCache", "DawnGraphiteCache",
    };

    /// <summary>
    /// "Clear at the next launch" from Settings -> Storage Usage (M4.6). These folders cannot be
    /// cleared while Aviary runs - the engine keeps them open from the moment it starts - so the
    /// choice is remembered and honoured HERE, before the environment exists and therefore before
    /// anything has opened them.
    /// </summary>
    private static void ClearGpuCachesIfAsked()
    {
        if (!AppSettings.GetBool("clearGpuCaches"))
        {
            return;
        }

        int removed = 0;
        long bytes = 0;
        try
        {
            foreach (string folder in Directory.EnumerateDirectories(UserDataFolder, "*", SearchOption.AllDirectories))
            {
                if (Array.IndexOf(GpuCaches, Path.GetFileName(folder)) < 0)
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        bytes += new FileInfo(file).Length;
                    }
                    catch (Exception)
                    {
                        // A file that vanished between listing and measuring is one less to delete.
                    }
                }

                Directory.Delete(folder, recursive: true);
                removed++;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "engine: clear graphics caches");
        }

        AppSettings.Set("clearGpuCaches", "false");
        Diagnostics.Perf("gpu-caches-cleared", new { folders = removed, mb = Math.Round(bytes / 1048576.0, 1) });
    }

    public static void EnsureUserDataFolder()
    {
        try
        {
            Directory.CreateDirectory(UserDataFolder);
            ClearGpuCachesIfAsked();
        }
        catch (Exception ex)
        {
            // If LocalAppData is unwritable the machine has bigger problems, and the
            // environment call below will surface its own error with a real message.
            Diagnostics.Swallowed(ex, "engine: create user data folder");
        }
    }

    /// <summary>
    /// Returns the shared environment, creating it on first call. Concurrent callers all
    /// await the same task rather than racing to create a second one.
    /// </summary>
    public static Task<CoreWebView2Environment> GetAsync()
    {
        lock (Gate)
        {
            if (_creation is null)
            {
                Arguments = BuildArguments();
                _creation = CoreWebView2Environment.CreateWithOptionsAsync(
                    string.Empty,
                    UserDataFolder,
                    new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = Arguments }).AsTask();
            }

            return _creation;
        }
    }

    /// <summary>
    /// The Chromium switches Aviary's privacy settings need, from the settings as they are now.
    ///
    /// Each one was checked against the Chromium 152 source before it was used:
    ///  - ForceThirdPartyCookieBlockingEnabled (net/base/features.cc): third-party cookies are
    ///    refused everywhere — in requests and in cross-site frames' document.cookie. It only
    ///    acts together with ThirdPartyStoragePartitioning, which is on by default. Partitioned
    ///    ("CHIPS") cookies, which cannot track across sites, keep working.
    ///  - GlobalPrivacyControlForce (blink runtime features): sends Sec-GPC: 1 on every
    ///    request and sets navigator.globalPrivacyControl in pages AND workers — the whole GPC
    ///    signal, from the engine itself.
    ///  - force-webrtc-ip-handling-policy=default_public_interface_only (content switches):
    ///    WebRTC may only use the interface the internet sees, so a page cannot read the
    ///    computer's local network address through a video-call API.
    ///
    /// Never here, whatever anyone asks: anything that weakens security — certificate or
    /// HTTPS errors, the sandbox, site isolation, web security, a debugging port.
    /// <c>AVIARY_TEST_BROWSER_ARGS</c> appends to these for test instances only (a DevTools
    /// port for the test harness); like AVIARY_CRASH_TEST it lives in the process
    /// environment, which nothing inside the browser can set.
    /// </summary>
    internal static string BuildArguments()
    {
        _launchedWith = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in RestartKeys)
        {
            _launchedWith[key] = AppSettings.GetBool(key);
        }

        List<string> features = new();
        if (_launchedWith["thirdPartyCookies"])
        {
            features.Add("ForceThirdPartyCookieBlockingEnabled");
        }

        if (_launchedWith["gpc"])
        {
            features.Add("GlobalPrivacyControlForce");
        }

        List<string> args = new();
        if (features.Count > 0)
        {
            args.Add("--enable-features=" + string.Join(",", features));
        }

        if (_launchedWith["webrtcProtect"])
        {
            args.Add("--force-webrtc-ip-handling-policy=default_public_interface_only");
        }

        // M4.5: Reduce motion stops Aviary's own animations at once (Motion.cs); this is the
        // other half - every page sees "prefers-reduced-motion: reduce" and drops its own.
        // Checked on this engine before the row was written: matchMedia says reduce.
        if (_launchedWith["reduceMotion"])
        {
            args.Add("--force-prefers-reduced-motion");
        }

        string? test = Environment.GetEnvironmentVariable("AVIARY_TEST_BROWSER_ARGS");
        if (!string.IsNullOrWhiteSpace(test))
        {
            args.Add(test.Trim());
        }

        return string.Join(" ", args);
    }
}
