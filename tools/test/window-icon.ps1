# What icon is the window actually WEARING? (M4.8, bug 77.)
# Asks the window for its icon with WM_GETICON (the one the taskbar, the title bar and Alt-Tab use)
# and saves it as a PNG. No input, no focus change - safe while Grant is at the PC.
#
#   window-icon.ps1 -ProcessId <pid> -Out shots\icon-now.png
param([int]$ProcessId, [string]$Out = "shots\window-icon.png")

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public class WI {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int m);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetClassLongPtr(IntPtr h, int i);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public static IntPtr MainWin(int pid2) { IntPtr f = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid2 && IsWindowVisible(h)) { var c = new StringBuilder(256); GetClassName(h, c, 256);
        if (c.ToString().Contains("WinUIDesktopWin32WindowClass")) f = h; } return true; }, IntPtr.Zero);
    return f; }
  // WM_GETICON: 0 = small, 1 = big, 2 = small2. Falls back to the class icon.
  public static IntPtr Icon(IntPtr h, int which) {
    IntPtr i = SendMessage(h, 0x007F, (IntPtr)which, IntPtr.Zero);
    if (i == IntPtr.Zero) i = GetClassLongPtr(h, which == 1 ? -14 : -34);   // GCLP_HICON / GCLP_HICONSM
    return i; }
}
'@ -ErrorAction SilentlyContinue

$h = [WI]::MainWin($ProcessId)
if ($h -eq [IntPtr]::Zero) { "NO WINDOW for pid $ProcessId"; exit 1 }

foreach ($which in 1, 0) {
    $hIcon = [WI]::Icon($h, $which)
    $label = if ($which -eq 1) { "big" } else { "small" }
    if ($hIcon -eq [IntPtr]::Zero) { "$label icon: NONE"; continue }
    $ico = [System.Drawing.Icon]::FromHandle($hIcon)
    $bmp = $ico.ToBitmap()
    $file = $Out -replace '\.png$', "-$label.png"
    $bmp.Save((Join-Path $PWD $file), [System.Drawing.Imaging.ImageFormat]::Png)
    # Average colour tells the two tiles apart without eyes: blue tile is blue, glass tile is near-white.
    $r = 0; $g = 0; $b = 0; $n = 0
    for ($x = 2; $x -lt $bmp.Width - 2; $x += 2) { for ($y = 2; $y -lt $bmp.Height - 2; $y += 2) {
        $c = $bmp.GetPixel($x, $y); if ($c.A -gt 128) { $r += $c.R; $g += $c.G; $b += $c.B; $n++ } } }
    if ($n -gt 0) { "{0,-5} {1,3}x{2,-3} avg #{3:X2}{4:X2}{5:X2} -> {6}" -f $label, $bmp.Width, $bmp.Height, [int]($r/$n), [int]($g/$n), [int]($b/$n), $file }
    $bmp.Dispose(); $ico.Dispose()
}
