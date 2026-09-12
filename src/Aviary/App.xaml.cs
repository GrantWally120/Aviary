using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace Aviary;

/// <summary>
/// Application entry point.
///
/// The project sets WindowsPackageType=None with WindowsAppSDKSelfContained=true, so the
/// Windows App SDK generates the Main() that initialises the bootstrapper for us. There is
/// deliberately no manual Bootstrap.Initialize call here — adding one alongside the
/// generated entry point is a known way to get a double-initialise failure.
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

        WebViewEnvironment.EnsureUserDataFolder();

        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Diagnostics.Mark("launched");
        _window = new MainWindow();
        _window.Activate();
        Diagnostics.Mark("window-activated");
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Keep the app alive so the user can at least close it deliberately, and leave a
        // trail next to the profile so a crash can be reported without a debugger.
        e.Handled = true;

        // The full report — breadcrumbs, open tabs, build — when collection is on. crash.log
        // below stays regardless: knowing that Aviary crashed must never depend on opting in.
        Diagnostics.Crash(e.Exception, "UI thread (Aviary kept running)", terminating: false);

        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Aviary");
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
