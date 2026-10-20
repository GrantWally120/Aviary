using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Quik: the browser engine warmed while the window is still being built (Alpha Build 5 M7 "Super Quik").
///
/// <para><b>Measured first</b> (26 Sep, five launches each): a launch spends ~200 ms creating its first tab's
/// engine CONTROLLER — the browser, graphics and page processes starting — and it could only begin once the
/// window was ready. Creating the engine ENVIRONMENT earlier changed nothing (910.4 ms with it, 910.3 without):
/// that part is cheap. The window's handle, though, exists ~180 ms before the first tab is made — so Quik uses
/// it at once for a hidden controller of its own, which gets those processes going, and the first tab then
/// finds them already running. The warm-up controller is closed as soon as the first tab's own engine exists.</para>
/// </summary>
public sealed partial class MainWindow
{
    private CoreWebView2Controller? _quikWarm;

    /// <summary>Once per launch: the first ordinary window.</summary>
    private static bool _quikEngineWarmed;

    /// <summary>From the constructor of the first ordinary window, when Aviary runs Quik.</summary>
    private async Task QuikWarmEngineAsync(IntPtr hWnd)
    {
        try
        {
            CoreWebView2Environment environment = await WebViewEnvironment.GetAsync();
            CoreWebView2Controller warm = await environment.CreateCoreWebView2ControllerAsync(
                CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)hWnd));
            warm.IsVisible = false;
            Diagnostics.Mark("engine-warm");

            // The first tab may already have its own by now: then this one has done its job.
            if (_quikWarmDone)
            {
                warm.Close();
                return;
            }

            _quikWarm = warm;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "quik: warm the engine");
        }
    }

    private bool _quikWarmDone;

    /// <summary>The first tab's engine exists: the warm-up's work is done.</summary>
    private void QuikWarmDone()
    {
        _quikWarmDone = true;
        try
        {
            _quikWarm?.Close();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "quik: close the warm-up engine");
        }

        _quikWarm = null;
    }
}
