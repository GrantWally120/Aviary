using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace Aviary;

/// <summary>
/// What Aviary does when it is launched again while it runs (Phase 5 M2, bug 51): the new
/// launch has handed itself over and ended (Program.cs), and this is the answer — the normal
/// window comes to the front, restored if it was minimised. With only private windows open, a
/// normal window opens, as a fresh launch would have.
/// </summary>
public sealed partial class MainWindow
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    internal static void ShowForAnotherLaunch()
    {
        MainWindow? normal = OpenWindows.LastOrDefault(w => !w._private);
        if (normal is not null)
        {
            Diagnostics.Ui("another-launch", new { answer = "front" });
            normal.BringToFront();
        }
        else if (OpenWindows.Count > 0)
        {
            Diagnostics.Ui("another-launch", new { answer = "new-window" });
            new MainWindow().Activate();
        }

        // No window at all: Aviary is still starting, and its first window is on the way.
    }

    private void BringToFront()
    {
        if (_appWindow?.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();

        // Allowed: the launch that handed itself over passed its right to the foreground on.
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }
}
