# Drag a TEST Aviary by its toolbar for about a second - a real Windows move loop (bug 67, M4.5).
# DESKTOP INPUT: only when guard.ps1 says Grant is away. Fronts the test window, drags it with
# mouse_event (SetCursorPos does not drive WinUI's pointer pipeline), puts the cursor back where it
# was, parks the window at the bottom again and hands the foreground back to the window that had it.
param([int]$ProcessId, [int]$HoldMs = 1200, [int]$XOffset = 215, [int]$YOffset = 24)

& "$PSScriptRoot\guard.ps1" -QuietMs 30000; if ($LASTEXITCODE -ne 0) { "GUARD: someone is using the PC - not dragging"; exit 3 }

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class Drag {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int ht, uint f);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    public struct RC { public int L, T, R, B; }
    public struct PT { public int X, Y; }

    public static IntPtr Biggest(int processId) {
        IntPtr best = IntPtr.Zero; long bestArea = -1;
        EnumWindows(delegate (IntPtr h, IntPtr l) {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == processId && IsWindowVisible(h)) {
                RC r; GetWindowRect(h, out r);
                long area = (long)(r.R - r.L) * (r.B - r.T);
                if (area > bestArea) { bestArea = area; best = h; }
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    static void MoveAbs(int x, int y) {
        int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
        mouse_event(0x8001, x * 65535 / (w - 1), y * 65535 / (h - 1), 0, IntPtr.Zero);   // MOVE | ABSOLUTE
    }

    public static string Run(IntPtr h, int holdMs, int xo, int yo) {
        RC r; GetWindowRect(h, out r);
        int x = r.L + xo, y = r.T + yo;
        MoveAbs(x, y); Thread.Sleep(80);
        mouse_event(0x2, 0, 0, 0, IntPtr.Zero);                                           // LEFTDOWN
        int steps = holdMs / 16;
        for (int i = 1; i <= steps; i++) { MoveAbs(x + i, y + i / 3); Thread.Sleep(16); }
        mouse_event(0x4, 0, 0, 0, IntPtr.Zero);                                           // LEFTUP
        RC after; GetWindowRect(h, out after);
        return string.Format("dragged from {0},{1} for ~{2} ms; window moved {3},{4} px", x, y, holdMs, after.L - r.L, after.T - r.T);
    }
}
'@

$h = [Drag]::Biggest($ProcessId)
if ($h -eq [IntPtr]::Zero) { "no window for $ProcessId"; exit 1 }
$before = [Drag]::GetForegroundWindow()
$cursor = New-Object Drag+PT; [void][Drag]::GetCursorPos([ref]$cursor)
# Windows refuses SetForegroundWindow from a background process, so the window is lifted ABOVE
# everything without being activated (TOPMOST, NOACTIVATE); the press itself then activates it, as a
# real click on a title bar does.
[void][Drag]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x13)   # HWND_TOPMOST, no size/move/activate
Start-Sleep -Milliseconds 300

[Drag]::Run($h, $HoldMs, $XOffset, $YOffset)

# Stamp: this input was ours (guard.ps1's convention).
[uint32]([Environment]::TickCount -band 0xFFFFFFFF) | Set-Content "$PSScriptRoot\lastinput.txt"
[void][Drag]::SetCursorPos($cursor.X, $cursor.Y)
[void][Drag]::SetWindowPos($h, [IntPtr](-2), 0, 0, 0, 0, 0x13)  # HWND_NOTOPMOST
# Focus back to Grant: minimising the (now active) test window makes Windows activate the next one.
[void][Drag]::ShowWindow($h, 6); Start-Sleep -Milliseconds 400
[void][Drag]::ShowWindow($h, 4)                                   # SW_SHOWNOACTIVATE
[void][Drag]::SetWindowPos($h, [IntPtr]1, 0, 0, 0, 0, 0x13)       # bottom, no size/move/activate
"foreground restored: $([Drag]::GetForegroundWindow() -eq $before)"
