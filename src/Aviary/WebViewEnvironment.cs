using System;
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
/// Hence: created once, awaited by everyone, never recreated.
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
    public static string UserDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Aviary",
        "WebView2");

    public static void EnsureUserDataFolder()
    {
        try
        {
            Directory.CreateDirectory(UserDataFolder);
        }
        catch (Exception)
        {
            // If LocalAppData is unwritable the machine has bigger problems, and the
            // environment call below will surface its own error with a real message.
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
            _creation ??= CoreWebView2Environment.CreateWithOptionsAsync(
                string.Empty,
                UserDataFolder,
                new CoreWebView2EnvironmentOptions()).AsTask();

            return _creation;
        }
    }
}
