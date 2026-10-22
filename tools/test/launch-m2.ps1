# Launch a TEST instance of Aviary: the scratch build, a scratch profile, a DevTools port.
# Never touches Grant's real profile (AVIARY_PROFILE_DIR) and never his running copy (separate exe).
param(
    [string]$Profile = "p1",
    [switch]$Fresh,
    [string]$SettingsJson = "",
    [int]$Port = 9222,
    [string]$ExtraArgs = ""
)

$sp = $PSScriptRoot
. "$PSScriptRoot\default-exe.ps1"
$profileDir = Join-Path $sp ("profiles\" + $Profile)
if ($Fresh -and (Test-Path $profileDir)) { Remove-Item $profileDir -Recurse -Force }
New-Item -ItemType Directory -Force $profileDir | Out-Null
if ($SettingsJson) { [System.IO.File]::WriteAllText((Join-Path $profileDir "settings.json"), $SettingsJson) }

& "$sp\guard.ps1"; if ($LASTEXITCODE -ne 0) { exit 3 }

$env:AVIARY_PROFILE_DIR = $profileDir
# The last three keep a test window drawing while it sits behind Grant's windows: Chromium stops
# producing frames for an occluded window, and a DevTools screenshot then waits for ever (13 Sep).
# Test instances only — never part of Aviary's own switches.
$keepDrawing = "--disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows --disable-background-timer-throttling"
$env:AVIARY_TEST_BROWSER_ARGS = ("--remote-debugging-port=$Port $keepDrawing " + $ExtraArgs).Trim()
& "$sp\run-aviary.ps1" -Exe (Get-AviaryDevExe $sp) -Out "$sp\shots\m2-launch.png" -SettleMs 3500
$env:AVIARY_PROFILE_DIR = $null; $env:AVIARY_TEST_BROWSER_ARGS = $null

$p = Get-Process Aviary | Where-Object { $_.Path -like "$sp*" } | Select-Object -First 1
if ($p) { $p.Id | Set-Content "$sp\pid.txt"; "TEST PID $($p.Id) profile=$profileDir" }
