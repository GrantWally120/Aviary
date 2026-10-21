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
    public static readonly string[] RestartKeys = { "thirdPartyCookies", "gpc", "webrtcProtect", "reduceMotion", "gpu" };

    /// <summary>
    /// M6 stage 5: the launch-time settings that are CHOICES, not switches — what the engine was started
    /// with, keyed like settings.json, so Settings can say "Restart Aviary to apply this change" for them too.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LaunchedText => _launchedText;

    private static Dictionary<string, string> _launchedText = new(StringComparer.OrdinalIgnoreCase);

    public static readonly string[] RestartChoices = { "autoplay", "language", "minFont" };

    /// <summary>Settings → Content → Auto-Play: Safari's three choices, in Safari's words.</summary>
    public const string AutoPlayAll = "Allow All Auto-Play";
    public const string AutoPlayWithSound = "Stop Media with Sound";
    public const string AutoPlayNever = "Never Auto-Play";

    /// <summary>Settings → Languages → Preferred language, when nothing is chosen: whatever Windows says.</summary>
    public const string LanguageOfWindows = "Same as Windows";

    /// <summary>
    /// The other choices, and the tag the engine is given for each (CoreWebView2EnvironmentOptions.Language:
    /// its own words on pages, and the Accept-Language header). Measured 26 Sep with the same tags on the
    /// command line: the engine takes only languages it has words of its own for — en-US, en-GB and fil
    /// (fil-PH and tl became fil) — and silently ignored Cebuano (ceb, ceb-PH) and English (Philippines),
    /// sending en-US instead. So those two are not offered: a choice that does nothing would be a lie.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Languages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["English (United States)"] = "en-US",
        ["English (United Kingdom)"] = "en-GB",
        ["Filipino"] = "fil",
    };

    /// <summary>
    /// The engine's browser process, once a tab has started (M6: "Restart Aviary" waits for it to
    /// go before the new Aviary starts, because an engine still running on this profile with the
    /// OLD switches would refuse the new ones). 0 until known.
    /// </summary>
    public static int BrowserProcessId { get; private set; }

    public static void NoteBrowserProcess(CoreWebView2 core)
    {
        try
        {
            if (BrowserProcessId == 0)
            {
                BrowserProcessId = (int)core.BrowserProcessId;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "engine: read the browser process");
        }
    }

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

                // M6 stage 5: the engine's own minimum font size lives in its Preferences file, which it
                // reads as it starts - so it is written now, before it does (EnginePreferences.cs).
                EnginePreferences.ApplyMinimumFontSize(_launchedText["minFont"]);

                // Keep engine crash dumps local rather than uploading them automatically.
                CoreWebView2EnvironmentOptions options = new()
                {
                    AdditionalBrowserArguments = Arguments,
                    IsCustomCrashReportingEnabled = true,
                };
                if (Languages.TryGetValue(_launchedText["language"], out string? tag))
                {
                    options.Language = tag;
                }

                _creation = CoreWebView2Environment.CreateWithOptionsAsync(
                    string.Empty,
                    UserDataFolder,
                    options).AsTask();
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

        // M7: Quik is not an engine switch, but it too is decided at launch - so Settings shows its
        // "Restart Aviary to apply this change" note the same way, from this map.
        _launchedWith[Quik.Key] = Quik.On;

        _launchedText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in RestartChoices)
        {
            _launchedText[key] = AppSettings.Get(key);
        }

        // M6: the experiments that change the engine are launch-time too - read once, here.
        foreach (Experiments.Experiment e in Experiments.All)
        {
            if (e.NeedsRestart)
            {
                _launchedWith[e.Key] = Experiments.IsOn(e);
            }
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

        // M6: into the SAME list. The engine keeps only the last --enable-features it is given, so
        // a second one for an experiment would silently switch the privacy features above off.
        bool LaunchedOn(Experiments.Experiment e) => _launchedWith.TryGetValue(e.Key, out bool on) ? on : Experiments.IsOn(e);
        features.AddRange(Experiments.EngineFeatures(LaunchedOn));

        List<string> args = new();
        if (features.Count > 0)
        {
            args.Add("--enable-features=" + string.Join(",", features));
        }

        // M6: each experiment's own switches, from the fixed list in Experiments.cs only - a value
        // in settings.json is never passed on, and anything that weakens security is dropped there.
        args.AddRange(Experiments.EngineSwitches(LaunchedOn));

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

        // M6 stage 5, Settings → System → Hardware acceleration, off: Windows' software renderer draws
        // instead. Measured 26 Sep: WebGL moved from the graphics card to "Microsoft Basic Render Driver",
        // WebGPU found no adapter, and pages still drew at the screen's 241 frames a second.
        if (!_launchedWith["gpu"])
        {
            args.Add("--disable-gpu");
        }

        // M6 stage 5, Settings → Content → Auto-Play. Measured 26 Sep (tools/test/expsite.js /autoplay):
        // given nothing, this engine already IS "Stop Media with Sound" — a muted video starts, anything
        // with sound waits for a click, and so does Web Audio. no-user-gesture-required lets everything
        // start. user-gesture-required stops every video and sound, muted ones too — but lets Web Audio in
        // a page's own frame start at once, which BrowserTab.AutoPlay.cs holds back.
        switch (_launchedText["autoplay"])
        {
            case AutoPlayAll:
                args.Add("--autoplay-policy=no-user-gesture-required");
                break;
            case AutoPlayNever:
                args.Add("--autoplay-policy=user-gesture-required");
                break;
        }

        string? test = Environment.GetEnvironmentVariable("AVIARY_TEST_BROWSER_ARGS");
        if (!string.IsNullOrWhiteSpace(test))
        {
            args.Add(test.Trim());
        }

        return string.Join(" ", args);
    }
}
