using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace Aviary;

/// <summary>
/// The application. Main is in Program.cs (since Phase 5 M2, bug 51): it makes sure only one
/// Aviary runs per profile before this class — or anything that writes the profile — exists.
///
/// The project sets WindowsPackageType=None with WindowsAppSDKSelfContained=true, so the
/// Windows App SDK initialises itself through its own module initializer. There is
/// deliberately no manual Bootstrap.Initialize call anywhere — adding one alongside the
/// SDK's own is a known way to get a double-initialise failure.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        // Nothing else catches an unhandled UI-thread exception, and the owner of this
        // project cannot read a debugger. Without this, any failure closes Aviary
        // instantly with no window, no message and nothing to report back.
        UnhandledException += App_UnhandledException;

        // Before anything else can fail: if "Allow data collection for improvements" is on,
        // everything from here on is recorded — including a settings file that will not load.
        Diagnostics.Initialise();

        if (Program.InstanceFailure is { } instanceFailure)
        {
            // Aviary is running without its one-per-profile guard; a second launch would be a
            // second writer again (bug 51).
            Diagnostics.Swallowed(instanceFailure, "single instance: register the profile");
        }

        if (NativeCrash.InstallFailure is { } nativeFailure)
        {
            // A native crash would leave no trace again, as before bug 50's recorder.
            Diagnostics.Swallowed(nativeFailure, "native crash: install the abort handler");
        }

        // M6: before the engine's switches are built (the first tab). If the last run ended badly
        // soon after an experiment changed, every experiment is switched off HERE, so none of them
        // reaches the engine. Not part of diagnostics: safety must not depend on opting in.
        SafetyMarker.AtLaunch();

        // M7 "Super Quik": what this launch runs as - read once, after the crash protection above,
        // which may just have switched Quik off.
        Quik.AtLaunch();

        WebViewEnvironment.EnsureUserDataFolder();

        // The ad blocker's lists are read on a background thread while the window builds, so
        // they are ready before the first website asks for anything (FilterLists.cs, M3).
        FilterLists.Start();

        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Diagnostics.Mark("launched");
        _window = new MainWindow();
        _window.Activate();
        Diagnostics.Mark("window-activated");

        // Aviary launched again while this one runs: that launch has ended itself and handed
        // over here (Program.cs).
        Program.ListenForOtherLaunches(
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(), MainWindow.ShowForAnotherLaunch);
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Keep the app alive so the user can at least close it deliberately, and leave a
        // trail next to the profile so a crash can be reported without a debugger.
        e.Handled = true;

        // The full report — breadcrumbs, open tabs, build — when collection is on. crash.log
        // below stays regardless: knowing that Aviary crashed must never depend on opting in.
        Diagnostics.Crash(e.Exception, "UI thread (Aviary kept running)", terminating: false);

        // M6: a crash Aviary survived still counts against experiments changed just before it.
        SafetyMarker.Problem("part of it failed and recovered");

        try
        {
            string folder = AppPaths.Root;
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "crash.log"),
                DateTimeOffset.Now.ToString("u") + Environment.NewLine +
                e.Exception + Environment.NewLine + Environment.NewLine);
        }
        catch (Exception ex)
        {
            // crash.log itself would not write. Only diagnostics is left to say so.
            Diagnostics.Swallowed(ex, "crash.log write");
        }
    }
}
