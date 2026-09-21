# Does memory saver really put an idle tab to sleep, and does it wake? (M4.8.)
#
# AVIARY_SLEEP_TEST=<seconds> replaces the setting's minutes so a five-minute feature can be seen
# in ten seconds, and makes the idle check tick every 2 s. Environment only - nothing inside the
# browser can set it (the AVIARY_CRASH_TEST rule). No desktop input: tabs are opened and switched
# through the DevTools driver and UI Automation.
#
#   sleep-test.ps1 [-Profile ms1] [-Seconds 6] [-Port 9380]
param([string]$Profile = "ms1", [int]$Seconds = 6, [int]$Port = 9380)

$kit = $PSScriptRoot
Set-Location $kit
$env:CDP_PORT = "$Port"
$env:AVIARY_SLEEP_TEST = "$Seconds"
& .\launch-quiet.ps1 -Profile $Profile -Fresh -Port $Port -Size 1280x860 `
    -SettingsJson '{"improvementCollection":"true","memorySaver":"true","sleepIdleTabs":"After 5 minutes"}' | Out-Null
$env:AVIARY_SLEEP_TEST = $null
$procId = [int](Get-Content pid.txt)
"test instance $procId, sleeping after ${Seconds}s of idleness"
Start-Sleep -Seconds 3

# Two real pages, so there is a background tab that is NOT one of Aviary's own.
node cdp.js nav "#0" "https://example.com/" | Out-Null
Start-Sleep -Seconds 3
& .\uia.ps1 -ProcessId $procId -Id NewTabButton -Action Invoke | Out-Null
Start-Sleep -Seconds 2
$second = (node cdp.js list | Select-String "Aviary \|" ) -ne $null
node cdp.js nav "Aviary" "https://example.org/" | Out-Null
Start-Sleep -Seconds 4
"tabs now:"
node cdp.js list

"`nwaiting for the idle check..."
Start-Sleep -Seconds ($Seconds + 8)

"`n--- what the diagnostics say ---"
$diag = Join-Path $kit "profiles\$Profile\user.data.improvementcollection\events.jsonl"
if (Test-Path $diag) {
    Get-Content $diag | Select-String "memory-saver-slept" | ForEach-Object { "  $_" }
    if (-not (Get-Content $diag | Select-String "memory-saver-slept")) { "  (nothing slept)" }
} else { "  no events.jsonl" }

"`n--- targets still alive? a slept tab keeps its target but stops answering ---"
node cdp.js list

"`n--- now switch back to the slept tab and see it wake ---"
& .\uia.ps1 -ProcessId $procId -Action Dump 2>&1 | Select-String -Pattern "Example" | Select-Object -First 3
