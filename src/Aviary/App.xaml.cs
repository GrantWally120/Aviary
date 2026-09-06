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

        WebViewEnvironment.EnsureUserDataFolder();

        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Keep the app alive so the user can at least close it deliberately, and leave a
        // trail next to the profile so a crash can be reported without a debugger.
        e.Handled = true;

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
        catch (Exception)
        {
            // Logging the failure to log a failure helps nobody.
        }
    }
}
