# ONE real mouse click at a point in a TEST Aviary's window, then a capture (M5).
#
# For the things only a real pointer can show: WinUI draws a keyboard focus ring after UI
# Automation or the keyboard, and not after a mouse click, so whether a popover opens WITH a focus
# ring can only be judged from a real click. DESKTOP INPUT - guard.ps1 must pass (30 s quiet).
# The window is lifted TOPMOST without activating (a background process cannot take the
# foreground), clicked, captured, then put back at the bottom with the cursor where it was and the
# foreground handed back - drag-window.ps1's routine.
#
#   real-click.ps1 -ProcessId <pid> -X 1074 -Y 25 -Out shots\x.png   (X/Y in window pixels)
param([int]$ProcessId, [int]$X, [int]$Y, [string]$Out, [int]$WaitMs = 1000)

& "$PSScriptRoot\guard.ps1" -QuietMs 30000; if ($LASTEXITCODE -ne 0) { "GUARD: someone is using the PC - not clicking"; exit 3 }
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Threading;
public static class RealClick {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int ht, uint f);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    public struct RC { public int L, T, R, B; }
    public struct PT { public int X, Y; }
    public static IntPtr Biggest(int processId) {
        IntPtr best = IntPtr.Zero; long bestArea = -1;
        EnumWindows(delegate (IntPtr h, IntPtr l) { uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == processId && IsWindowVisible(h)) { RC r; GetWindowRect(h, out r); long a = (long)(r.R - r.L) * (r.B - r.T); if (a > bestArea) { bestArea = a; best = h; } }
            return true; }, IntPtr.Zero);
        return best; }
    public static void Click(int x, int y) {
        int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
        mouse_event(0x8001, x * 65535 / (w - 1), y * 65535 / (h - 1), 0, IntPtr.Zero); Thread.Sleep(120);
        mouse_event(0x2, 0, 0, 0, IntPtr.Zero); Thread.Sleep(40); mouse_event(0x4, 0, 0, 0, IntPtr.Zero); }
}
'@
$h = [RealClick]::Biggest($ProcessId)
if ($h -eq [IntPtr]::Zero) { "no window for $ProcessId"; exit 1 }
$before = [RealClick]::GetForegroundWindow()
$cursor = New-Object RealClick+PT; [void][RealClick]::GetCursorPos([ref]$cursor)
[void][RealClick]::SetWindowPos($h, [IntPtr](-1), 0, 0, 0, 0, 0x13); Start-Sleep -Milliseconds 300
$r = New-Object RealClick+RC; [void][RealClick]::GetWindowRect($h, [ref]$r)
[RealClick]::Click($r.L + $X, $r.T + $Y)
[uint32]([Environment]::TickCount -band 0xFFFFFFFF) | Set-Content "$PSScriptRoot\lastinput.txt"
[void][RealClick]::SetCursorPos($cursor.X, $cursor.Y)
Start-Sleep -Milliseconds $WaitMs
$w = $r.R - $r.L; $hh = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $hh; $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
[void][RealClick]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc); $g.Dispose(); $bmp.Save($Out); $bmp.Dispose()
[void][RealClick]::SetWindowPos($h, [IntPtr](-2), 0, 0, 0, 0, 0x13)
[void][RealClick]::ShowWindow($h, 6); Start-Sleep -Milliseconds 400
[void][RealClick]::ShowWindow($h, 4)
[void][RealClick]::SetWindowPos($h, [IntPtr]1, 0, 0, 0, 0, 0x13)
"clicked $X,$Y; saved $Out; foreground restored: $([RealClick]::GetForegroundWindow() -eq $before)"
