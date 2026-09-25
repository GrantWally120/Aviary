# Capture Aviary's window WITHOUT bringing it to the front (PrintWindow, PW_RENDERFULLCONTENT).
# Non-intrusive: sends no input and does not steal focus, so it is safe while Grant is at the PC.
# Optional crop: -Crop "x,y,w,h" in window pixels.
param([int]$ProcessId, [string]$Out, [string]$Crop = "")
# .NET resolves a relative path against the PROCESS's directory, not PowerShell's location: run from
# Zoo\, experiments-test's "shots\xp14.png" failed to save (GDI+ "generic error") and the harness read
# an old capture instead (25 Sep). Resolve it the way PowerShell would.
$Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Pw {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
    [StructLayout(LayoutKind.Sequential)] public struct RC { public int Left, Top, Right, Bottom; }
}
"@

$p = Get-Process -Id $ProcessId -ErrorAction Stop
$h = $p.MainWindowHandle
$r = New-Object Pw+RC
[void][Pw]::GetWindowRect($h, [ref]$r)
$w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [Pw]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc); $g.Dispose()

if ($Crop) {
    $c = $Crop.Split(',') | ForEach-Object { [int]$_ }
    $part = $bmp.Clone((New-Object System.Drawing.Rectangle $c[0], $c[1], $c[2], $c[3]), $bmp.PixelFormat)
    $bmp.Dispose(); $bmp = $part
}

$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "PrintWindow=$ok  SAVED $Out"
