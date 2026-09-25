# A REAL pointer on one of the test window's controls (M6 stage 3) - DESKTOP INPUT: refuses unless
# guard.ps1 passes. Finds the control through UI Automation (by name), lifts the test window above
# everything WITHOUT activating it (TOPMOST - a background process cannot SetForegroundWindow), and:
#   -Action Hover        rests the cursor on it for -HoldMs, then PrintWindows the popup the process
#                        opened (a tooltip is a PopupWindowSiteBridge window of its own) to -Out
#   -Action DoubleClick  double-clicks -Below pixels under it (the empty space below a list)
# then puts the cursor back, drops the window to the bottom of the z-order, hands the foreground back
# if the click took it, and stamps lastinput.txt so guard.ps1 knows the input was ours.
# mouse_event, not SetCursorPos: SetCursorPos does not drive WinUI's pointer pipeline (HANDOFF 5).
#
#   pointer.ps1 -ProcessId <pid> -Name "exp white" -Action Hover -Out shots\tip.png
#   pointer.ps1 -ProcessId <pid> -Name "exp white" -Action DoubleClick -Below 80
param([int]$ProcessId, [string]$Name, [ValidateSet("Hover", "DoubleClick")][string]$Action = "Hover",
      [int]$HoldMs = 1600, [int]$Below = 0, [string]$Out = "")

& "$PSScriptRoot\guard.ps1" | Out-Null
if ($LASTEXITCODE -eq 3) { "REFUSED: someone is using the machine"; exit 3 }
if ($Out) { $Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out) }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class Ptr {
  public struct PT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct RC { public int L, T, R, B; }
  delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EP f, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int m);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint f);
  [DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
  public static IntPtr Biggest(uint pid) {
    IntPtr best = IntPtr.Zero; long area = -1;
    EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) { RC r; GetWindowRect(h, out r); long a = (long)(r.R - r.L) * (r.B - r.T); if (a > area) { area = a; best = h; } }
      return true; }, IntPtr.Zero);
    return best;
  }
  public static IntPtr Popup(uint pid) {
    IntPtr f = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) { var c = new StringBuilder(256); GetClassName(h, c, 256);
        if (c.ToString().Contains("PopupWindowSiteBridge")) f = h; } return true; }, IntPtr.Zero);
    return f;
  }
  public static void MoveTo(int x, int y) {
    int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
    mouse_event(0x8001, x * 65535 / (w - 1), y * 65535 / (h - 1), 0, IntPtr.Zero);   // MOVE | ABSOLUTE
  }
  public static void Click() { mouse_event(0x2, 0, 0, 0, IntPtr.Zero); mouse_event(0x4, 0, 0, 0, IntPtr.Zero); }
}
'@ -ErrorAction SilentlyContinue

$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
$roots = $A::RootElement.FindAll($T::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)))
$el = $null
foreach ($root in $roots) {
    $hit = $root.FindFirst($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $Name)))
    if ($hit) { $el = $hit; break }
}
if (-not $el) { "NO ELEMENT named '$Name'"; exit 2 }
$b = $el.Current.BoundingRectangle
$x = [int]($b.X + $b.Width / 2)
$y = if ($Below -gt 0) { [int]($b.Y + $b.Height + $Below) } else { [int]($b.Y + $b.Height / 2) }

$main = [Ptr]::Biggest([uint32]$ProcessId)
$fgBefore = [Ptr]::GetForegroundWindow()
$cursor = New-Object Ptr+PT; [void][Ptr]::GetCursorPos([ref]$cursor)
[void][Ptr]::SetWindowPos($main, [IntPtr](-1), 0, 0, 0, 0, 0x13)      # HWND_TOPMOST, no size/move/activate
Start-Sleep -Milliseconds 250
$result = ""
try {
    [Ptr]::MoveTo($x - 30, $y); Start-Sleep -Milliseconds 80; [Ptr]::MoveTo($x, $y)
    if ($Action -eq "Hover") {
        Start-Sleep -Milliseconds $HoldMs
        $pop = [Ptr]::Popup([uint32]$ProcessId)
        if ($pop -eq [IntPtr]::Zero) { $result = "NO POPUP after ${HoldMs} ms" }
        else {
            $r = New-Object Ptr+RC; [void][Ptr]::GetWindowRect($pop, [ref]$r)
            $w = $r.R - $r.L; $hh = $r.B - $r.T
            $bmp = New-Object System.Drawing.Bitmap $w, $hh
            $g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
            [void][Ptr]::PrintWindow($pop, $dc, 2); $g.ReleaseHdc($dc); $g.Dispose()
            if ($Out) { $bmp.Save($Out) }
            $bmp.Dispose()
            $result = "POPUP ${w}x${hh}" + $(if ($Out) { " SAVED $Out" } else { "" })
        }
    } else {
        [Ptr]::Click(); Start-Sleep -Milliseconds 90; [Ptr]::Click()
        Start-Sleep -Milliseconds 600
        $result = "DOUBLE-CLICKED at $x,$y"
    }
}
finally {
    [uint32]([Environment]::TickCount -band 0xFFFFFFFF) | Set-Content "$PSScriptRoot\lastinput.txt"
    [void][Ptr]::SetCursorPos($cursor.X, $cursor.Y)
    [void][Ptr]::SetWindowPos($main, [IntPtr](-2), 0, 0, 0, 0, 0x13)  # HWND_NOTOPMOST
    [void][Ptr]::SetWindowPos($main, [IntPtr](1), 0, 0, 0, 0, 0x13)   # HWND_BOTTOM
    if ([Ptr]::GetForegroundWindow() -eq $main) {
        # The click activated it: minimise (Windows hands the foreground back) and restore inactive.
        [void][Ptr]::ShowWindow($main, 6); Start-Sleep -Milliseconds 400; [void][Ptr]::ShowWindow($main, 4)
        [void][Ptr]::SetWindowPos($main, [IntPtr](1), 0, 0, 0, 0, 0x13)
    }
    [uint32]([Environment]::TickCount -band 0xFFFFFFFF) | Set-Content "$PSScriptRoot\lastinput.txt"
}
$result
