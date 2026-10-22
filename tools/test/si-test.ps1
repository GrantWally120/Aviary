# Bug 51: launch Aviary AGAIN on a profile a test instance already has open.
# Expected: the new process ends by itself within a few seconds, the running one stays the only
# one on that profile, and its window comes back (restored if minimised) to the front.
param(
    [string]$Profile = "si",
    [switch]$MinimiseFirst,
    [switch]$CloseFirst      # close the running one and launch at once: the shutdown race
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class SiWin {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
"@

$sp = $PSScriptRoot
. "$PSScriptRoot\default-exe.ps1"
& "$sp\guard.ps1"; if ($LASTEXITCODE -ne 0) { exit 3 }

function Mine { @(Get-Process Aviary -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$sp*" }) }

$before = Mine
"before: " + (($before | ForEach-Object { $_.Id }) -join ", ")
$a = $before | Select-Object -First 1
if (-not $a) { "no running test instance"; exit 1 }
$h = $a.MainWindowHandle

if ($MinimiseFirst) {
    [void][SiWin]::ShowWindow($h, 6)   # SW_MINIMIZE
    Start-Sleep -Milliseconds 800
    "minimised: " + [SiWin]::IsIconic($h)
}

if ($CloseFirst) {
    [void]$a.CloseMainWindow()
    "asked $($a.Id) to close"
}

$env:AVIARY_PROFILE_DIR = Join-Path $sp ("profiles\" + $Profile)
$env:AVIARY_TEST_BROWSER_ARGS = "--remote-debugging-port=9222"
$sw = [Diagnostics.Stopwatch]::StartNew()
$b = Start-Process -FilePath (Get-AviaryDevExe $sp) -PassThru
$env:AVIARY_PROFILE_DIR = $null; $env:AVIARY_TEST_BROWSER_ARGS = $null

$ended = $b.WaitForExit(15000)
"second launch pid $($b.Id): " + $(if ($ended) { "ENDED after $($sw.ElapsedMilliseconds) ms, exit code $($b.ExitCode)" } else { "STILL RUNNING after 15 s" })

Start-Sleep -Milliseconds 2500
$after = Mine
"after: " + (($after | ForEach-Object { "$($_.Id) (started $($_.StartTime.ToString('HH:mm:ss.fff')))" }) -join ", ")

if (-not $CloseFirst) {
    "still minimised: " + [SiWin]::IsIconic($h)
    $fg = [SiWin]::GetForegroundWindow()
    $fgPid = 0; [void][SiWin]::GetWindowThreadProcessId($fg, [ref]$fgPid)
    "foreground is the running Aviary: " + ($fgPid -eq $a.Id)
}
