using System;
using System.IO;

namespace Aviary;

/// <summary>
/// Where WebView2 keeps its profile.
///
/// This is deliberately its own tiny class rather than a literal inside MainWindow,
/// because Phase 2 needs the same folder when it creates the shared environment for tabs,
/// and two copies of a path string is exactly how a browser ends up with two profiles.
/// </summary>
internal static class WebViewPaths
{
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

    /// <summary>
    /// Creates the folder if it is missing. Safe to call more than once. WebView2 will
    /// create it too, but doing it here means a permissions problem surfaces at a point
    /// where there is still a window to show a message in.
    /// </summary>
    public static void EnsureUserDataFolder()
    {
        try
        {
            Directory.CreateDirectory(UserDataFolder);
        }
        catch (Exception)
        {
            // If LocalAppData is unwritable the machine has bigger problems, and the
            // WebView2 environment call will surface its own error with a real message.
        }
    }
}
