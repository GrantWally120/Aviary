# Bug 51's shutdown race: ask the running test instance to close, launch again after $Delays ms.
# Every trial must end with exactly ONE Aviary on the profile, not the old one (the launch was not
# lost), and no unclean-exit in issues.jsonl (the new one never read a live session).
param([string]$Profile = "si", [int[]]$Delays = @(0, 20, 50, 100, 200, 400))

$sp = $PSScriptRoot
. "$PSScriptRoot\default-exe.ps1"
$diag = Join-Path $sp "profiles\$Profile\user.data.improvementcollection"
function Mine { @(Get-Process Aviary -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$sp*" }) }

foreach ($d in $Delays) {
    & "$sp\guard.ps1" | Out-Null; if ($LASTEXITCODE -ne 0) { "someone is at the keyboard - stopping"; exit 3 }

    $old = Mine
    if ($old.Count -ne 1) { "expected one running instance, found $($old.Count)"; exit 1 }
    $old = $old[0]

    [void]$old.CloseMainWindow()
    if ($d -gt 0) { Start-Sleep -Milliseconds $d }

    $env:AVIARY_PROFILE_DIR = Join-Path $sp "profiles\$Profile"
    $b = Start-Process -FilePath (Get-AviaryDevExe $sp) -PassThru
    $env:AVIARY_PROFILE_DIR = $null

    $ended = $b.WaitForExit(6000)
    Start-Sleep -Milliseconds 3000
    $now = Mine
    $unclean = if (Test-Path "$diag\issues.jsonl") { @(Select-String -Path "$diag\issues.jsonl" -Pattern "unclean-exit").Count } else { 0 }

    $how = if ($ended) { "launch handed over (exit $($b.ExitCode)), relaunched by the closing one" } else { "launch became the new Aviary" }
    $ok = ($now.Count -eq 1) -and ($now[0].Id -ne $old.Id) -and ($unclean -eq 0)
    "delay {0,3} ms: {1}; running now: {2}; unclean-exit lines: {3}  => {4}" -f $d, $how, (($now | ForEach-Object Id) -join ","), $unclean, $(if ($ok) { "OK" } else { "FAIL" })
}
