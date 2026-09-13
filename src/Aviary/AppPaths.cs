using System;
using System.IO;

namespace Aviary;

/// <summary>
/// Where Aviary keeps everything it writes: %LOCALAPPDATA%\Aviary — settings, history,
/// bookmarks, the favicon cache, the engine's own store and the diagnostics folder.
///
/// One place, because ten files used to build the same path each for themselves, and a
/// store that quietly disagreed with the others about where the profile lives would lose
/// data without an error.
///
/// <c>AVIARY_PROFILE_DIR</c> moves the whole profile, for test instances only. Like
/// AVIARY_CRASH_TEST it lives in the process environment, which nothing inside the browser —
/// no page, setting, menu or message — can set. It exists because the tools that test Aviary
/// run inside the Claude desktop app's MSIX container, where %LOCALAPPDATA% is virtualized:
/// a test instance writing the real profile path lands in a private shadow copy that then
/// HIDES Grant's real files from the tests' own view (found 12 Sep 2026 — every "real app"
/// test since M0 had been reading and writing that shadow). A scratch profile keeps tests
/// and the real profile apart by construction.
///
/// Deliberately no Diagnostics call in here: Diagnostics reads its folder from this class
/// during its own start-up, and a failure path that called back into it would recurse.
/// </summary>
internal static class AppPaths
{
    public static string Root { get; } = ResolveRoot();

    /// <summary>True when a test instance has been pointed at a scratch profile.</summary>
    public static bool IsScratchProfile { get; private set; }

    public static string Of(string name) => Path.Combine(Root, name);

    private static string ResolveRoot()
    {
        string? overridden = Environment.GetEnvironmentVariable("AVIARY_PROFILE_DIR");

        // Fully qualified only. A relative path would resolve against whatever the working
        // directory happened to be, which is not a place a profile should end up by accident.
        if (!string.IsNullOrWhiteSpace(overridden) && Path.IsPathFullyQualified(overridden))
        {
            IsScratchProfile = true;
            return overridden.TrimEnd('\\', '/');
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aviary");
    }
}
