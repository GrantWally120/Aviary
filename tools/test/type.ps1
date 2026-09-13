# Type into Aviary, but ONLY while Aviary owns the foreground.
#
# This guard is the whole point of the script. Input sent while another window is in front
# has already leaked into the user's chat once in this project's history and posted two
# stray messages. If Aviary is not in front, this refuses and sends nothing.
param([int]$ProcessId, [string]$Text, [string]$Shot, [int]$WaitMs = 1500)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class T {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
    [StructLayout(LayoutKind.Sequential)] public struct RC { public int Left, Top, Right, Bottom; }
}
"@

$p = Get-Process -Id $ProcessId -ErrorAction Stop
$h = $p.MainWindowHandle
if ([T]::GetForegroundWindow() -ne $h) { Write-Output "ABORT: Aviary is not foreground - nothing sent"; exit 2 }

[System.Windows.Forms.SendKeys]::SendWait($Text)
Set-Content -Path (Join-Path $PSScriptRoot "lastinput.txt") -Value ([uint32]([Environment]::TickCount -band 0xFFFFFFFF))
Start-Sleep -Milliseconds $WaitMs

# Re-check before capturing: focus can move while typing.
if ([T]::GetForegroundWindow() -ne $h) { Write-Output "focus moved after typing; not capturing"; exit 0 }

if ($Shot) {
    $r = New-Object T+RC
    [void][T]::GetWindowRect($h, [ref]$r)
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
