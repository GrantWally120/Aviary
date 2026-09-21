# Does a REAL mouse click on the app menu reach the item it is drawn on? (M4.7, 21 Sep 2026.)
#
# Written to settle one question: M4.6's Motion.Bloom leaves the flyout's visual-tree geometry
# ~860 px away from where the menu is actually drawn, and UIA Invoke / PostMessage both bypass
# hit-testing, so neither can tell whether a real click lands. This sends one.
#
# DESKTOP INPUT - guard.ps1 must pass. The popup is lifted TOPMOST without activating (a
# background process cannot call SetForegroundWindow), the cursor is put back where it was, and
# lastinput.txt is stamped so guard.ps1 knows the input was ours.
#
#   menu-click.ps1 -ProcessId <pid> -Row 400      # 400 = the Settings row, in popup client px
param([int]$ProcessId, [int]$Row = 400, [int]$Col = 137)

$sp = $PSScriptRoot
& (Join-Path $sp 'guard.ps1')
if ($LASTEXITCODE -ne 0) { "GUARD REFUSED - someone else is at the machine"; exit 3 }

Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public class MC {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int m);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int ht, uint f);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public struct RC { public int L,T,R,B; }
  public struct PT { public int X, Y; }
  public static IntPtr Popup(int pid2) { IntPtr f = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid2 && IsWindowVisible(h)) { var c = new StringBuilder(256); GetClassName(h, c, 256);
        if (c.ToString().Contains("PopupWindowSiteBridge")) f = h; } return true; }, IntPtr.Zero);
    return f; }
  public static IntPtr MainWin(int pid2) { IntPtr f = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid2 && IsWindowVisible(h)) { var c = new StringBuilder(256); GetClassName(h, c, 256);
        if (c.ToString().Contains("WinUIDesktopWin32WindowClass")) f = h; } return true; }, IntPtr.Zero);
    return f; }
  const uint NOSIZE = 0x1, NOMOVE = 0x2, NOACTIVATE = 0x10;
  public static void Top(IntPtr h)    { SetWindowPos(h, new IntPtr(-1), 0,0,0,0, NOSIZE|NOMOVE|NOACTIVATE); }
  public static void Bottom(IntPtr h) { SetWindowPos(h, new IntPtr(1),  0,0,0,0, NOSIZE|NOMOVE|NOACTIVATE); }
  public static void ClickAt(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(220);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(90);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); }
}
'@

$popup = [MC]::Popup($ProcessId)
if ($popup -eq [IntPtr]::Zero) { "NO POPUP OPEN for pid $ProcessId"; exit 1 }
$main = [MC]::MainWin($ProcessId)
$r = New-Object MC+RC; [void][MC]::GetWindowRect($popup, [ref]$r)
$x = $r.L + $Col; $y = $r.T + $Row
"popup at $($r.L),$($r.T) $($r.R-$r.L)x$($r.B-$r.T)  ->  clicking screen ($x,$y)"

$was = New-Object MC+PT; [void][MC]::GetCursorPos([ref]$was)
[MC]::Top($main); [MC]::Top($popup)
Start-Sleep -Milliseconds 250
[MC]::ClickAt($x, $y)
Start-Sleep -Milliseconds 400
[void][MC]::SetCursorPos($was.X, $was.Y)
[MC]::Bottom($popup); [MC]::Bottom($main)

[uint32]([Environment]::TickCount -band 0xFFFFFFFF) | Set-Content (Join-Path $sp 'lastinput.txt')
"clicked; cursor restored to $($was.X),$($was.Y); windows parked"
