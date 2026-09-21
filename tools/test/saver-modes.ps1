# Memory saver's four modes, and the migration into them (M4.9).
#
# AVIARY_SLEEP_TEST=<seconds> replaces the mode's minutes so each one can be seen in seconds.
# Off must STILL not sleep with the hook set - SleepAfter returns null for Off before it ever
# looks at the hook, and that ordering is the thing worth proving.
#
#   saver-modes.ps1 [-Seconds 6]
param([int]$Seconds = 6, [int]$Port = 9400)

$kit = $PSScriptRoot
Set-Location $kit

function Run([string]$label, [string]$settingsJson, [string]$profile, [int]$port) {
    $env:CDP_PORT = "$port"
    $env:AVIARY_SLEEP_TEST = "$Seconds"
    & .\launch-quiet.ps1 -Profile $profile -Fresh -Port $port -Size 1280x860 -SettingsJson $settingsJson | Out-Null
    $env:AVIARY_SLEEP_TEST = $null
    $procId = [int](Get-Content pid.txt)
    Start-Sleep -Seconds 3

    node cdp.js nav "#0" "https://example.com/" | Out-Null
    Start-Sleep -Seconds 3
    & .\uia.ps1 -ProcessId $procId -Id NewTabButton -Action Invoke | Out-Null
    Start-Sleep -Seconds 2
    node cdp.js nav "Aviary" "https://example.org/" | Out-Null
    Start-Sleep -Seconds ($Seconds + 9)

    $ev = Join-Path $kit "profiles\$profile\user.data.improvementcollection\events.jsonl"
    $slept = 0
    $mode = "?"
    if (Test-Path $ev) {
        $slept = @(Get-Content $ev | Select-String "memory-saver-slept").Count
        $line = Get-Content $ev | Select-String '"name":"start"' | Select-Object -First 1
        if ($line -match '"memorySaver":"([^"]*)"') { $mode = $Matches[1] }
    }
    "{0,-34} store says memorySaver={1,-10} slept={2}" -f $label, $mode, $slept
    Get-Process -Id $procId -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
}

Run "Off - must NOT sleep"        '{"improvementCollection":"true","memorySaver":"Off"}'        sm-off   $Port
Run "Chill"                       '{"improvementCollection":"true","memorySaver":"Chill"}'      sm-chill ($Port+1)
Run "Balanced (default)"          '{"improvementCollection":"true","memorySaver":"Balanced"}'   sm-bal   ($Port+2)
Run "Aggressive"                  '{"improvementCollection":"true","memorySaver":"Aggressive"}' sm-agg   ($Port+3)
Run "MIGRATION: old 'true'"       '{"improvementCollection":"true","memorySaver":"true"}'       sm-mtrue ($Port+4)
Run "MIGRATION: old 'false'"      '{"improvementCollection":"true","memorySaver":"false"}'      sm-mfals ($Port+5)
