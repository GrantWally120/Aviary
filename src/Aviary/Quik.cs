using System;
using System.IO;

namespace Aviary;

/// <summary>
/// <b>Quik</b> — the hero of Alpha Build 5 M7 "Super Quik" (Grant, 26 Sep 2026): a mode, in Beta, that makes
/// Aviary quicker and more responsive. Settings → Quik has its switch and, once Quik is running, its options.
///
/// <para><b>Read once, at launch.</b> Quik changes how Aviary starts (the engine is started with the window,
/// launch work the first page does not need comes after it), so switching it on or off only takes effect
/// when Aviary restarts — Settings says so under the switch, with a Restart Aviary button. <see cref="On"/>
/// is what THIS launch is running as; the saved setting is what the next one will be.</para>
///
/// <para><b>The loading screen.</b> Switching Quik on owes Grant's loading screen to the next launch INTO
/// Quik — once (his choice, 26 Sep): a marker in the profile says so, and the launch that shows it clears
/// it (MainWindow.QuikIntro.cs). Switching Quik off clears it too.</para>
///
/// <para><b>It is Beta:</b> the crash protection that watches experiments watches Quik as well — if Aviary
/// stops soon after Quik is switched on, the next launch starts without it and says so (SafetyMarker).</para>
/// </summary>
internal static class Quik
{
    public const string Key = "quik";

    /// <summary>What this launch is running as. Set once, in App's constructor.</summary>
    public static bool On { get; private set; }

    private static string IntroMarkerPath => AppPaths.Of("quik-intro-pending");

    /// <summary>From App's constructor — after the crash protection, which may have switched Quik off.</summary>
    public static void AtLaunch()
    {
        On = AppSettings.GetBool(Key);
    }

    /// <summary>The switch moved in Settings (or crash protection's "Turn Them Back On").</summary>
    public static void Switched(bool on)
    {
        try
        {
            if (on)
            {
                File.WriteAllText(IntroMarkerPath, DateTimeOffset.Now.ToString("o"));
            }
            else if (File.Exists(IntroMarkerPath))
            {
                File.Delete(IntroMarkerPath);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "quik: loading screen marker");
        }

        // Beta: the runs after any change are watched, as for an experiment.
        SafetyMarker.ExperimentChanged();
        Diagnostics.Feature("quik-switched", new { on, running = On });
    }

    /// <summary>This launch is the first into Quik since it was switched on: the loading screen plays.</summary>
    public static bool IntroDue => On && File.Exists(IntroMarkerPath);

    /// <summary>
    /// AVIARY_QUIK_TEST=1: what Quik did, a line each, into <c>quik-test.txt</c> in the profile - so a test can
    /// see the loading screen play, end and stay muted. Environment only, like the other test hooks.
    /// </summary>
    public static void Test(string line)
    {
        if (Environment.GetEnvironmentVariable("AVIARY_QUIK_TEST") != "1")
        {
            return;
        }

        try
        {
            File.AppendAllText(AppPaths.Of("quik-test.txt"), DateTimeOffset.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "quik: test record");
        }
    }

    /// <summary>The loading screen has been shown (or could not be): nothing is owed any more.</summary>
    public static void IntroDone()
    {
        try
        {
            File.Delete(IntroMarkerPath);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "quik: clear the loading screen marker");
        }
    }
}
