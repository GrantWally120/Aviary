# Click one point inside Aviary, but ONLY while Aviary owns the foreground.
#
# The foreground check is not optional. Input sent while another window is in front has
# already gone into the user's chat once in this project's history.
#
# mouse_event with MOUSEEVENTF_ABSOLUTE, not SetCursorPos + a synthetic click: SetCursorPos
# alone does not drive WinUI's pointer pipeline for anything beyond a plain click.
param([int]$ProcessId, [int]$X, [int]$Y, [string]$Shot, [int]$WaitMs = 1400, [switch]$Right)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class M {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [StructLayout(LayoutKind.Sequential)] public struct RC { public int Left, Top, Right, Bottom; }
    public const uint MOVE = 0x0001, ABS = 0x8000, LDOWN = 0x0002, LUP = 0x0004, RDOWN = 0x0008, RUP = 0x0010;
}
"@

$p = Get-Process -Id $ProcessId -ErrorAction Stop
$h = $p.MainWindowHandle
if ([M]::GetForegroundWindow() -ne $h) { Write-Output "ABORT: Aviary is not foreground"; exit 2 }

$sw = [M]::GetSystemMetrics(0); $sh = [M]::GetSystemMetrics(1)
$ax = [uint32](($X * 65535) / ($sw - 1))
$ay = [uint32](($Y * 65535) / ($sh - 1))

[M]::mouse_event([M]::MOVE -bor [M]::ABS, $ax, $ay, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 140
$dn = if ($Right) { [M]::RDOWN } else { [M]::LDOWN }
$up = if ($Right) { [M]::RUP } else { [M]::LUP }
[M]::mouse_event($dn -bor [M]::ABS, $ax, $ay, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 60
[M]::mouse_event($up -bor [M]::ABS, $ax, $ay, 0, [IntPtr]::Zero)
Set-Content -Path (Join-Path $PSScriptRoot "lastinput.txt") -Value ([uint32]([Environment]::TickCount -band 0xFFFFFFFF))
Write-Output "clicked screen ($X,$Y) on ${sw}x${sh}"

Start-Sleep -Milliseconds $WaitMs

if ($Shot) {
    if ([M]::GetForegroundWindow() -ne $h) { Write-Output "NOTE: focus moved after click; not capturing"; exit 0 }
    $r = New-Object M+RC
    [void][M]::GetWindowRect($h, [ref]$r)
    $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $bmp.Save($Shot, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Output "SAVED $Shot"
}
$p.Refresh()
Write-Output "title='$($p.MainWindowTitle)'"
