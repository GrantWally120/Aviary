# Bug 51, the other ordering: launch FIRST, then close the running one $S ms later, so the handed-
# over launch lands before, during or after its shutdown. Outcomes, all acceptable:
#   none        - the launch arrived first and was shown; then the close closed Aviary (as asked)
#   relaunched  - the launch arrived while it was closing: it finished writing, then started one
#   new owner   - it had already let go of the profile; the launch became Aviary itself
# FAIL = two Aviarys on the profile at once, or an unclean-exit (a live session read as dead).
param([string]$Profile = "si", [int[]]$Sleeps = @(60, 90, 110, 130, 150, 170, 200, 250))

$sp = $PSScriptRoot
. "$PSScriptRoot\default-exe.ps1"
$diag = Join-Path $sp "profiles\$Profile\user.data.improvementcollection"
function Mine { @(Get-Process Aviary -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$sp*" }) }
function Start-Test {
    $env:AVIARY_PROFILE_DIR = Join-Path $sp "profiles\$Profile"
    $p = Start-Process -FilePath (Get-AviaryDevExe $sp) -PassThru
    $env:AVIARY_PROFILE_DIR = $null
    $p
}

foreach ($s in $Sleeps) {
    & "$sp\guard.ps1" | Out-Null; if ($LASTEXITCODE -ne 0) { "someone is at the keyboard - stopping"; exit 3 }

    $old = Mine
    if ($old.Count -eq 0) { $null = Start-Test; Start-Sleep -Milliseconds 4000; $old = Mine }
    if ($old.Count -ne 1) { "expected one running instance, found $($old.Count)"; exit 1 }
    $old = $old[0]

    $b = Start-Test
    Start-Sleep -Milliseconds $s
    [void]$old.CloseMainWindow()

    $maxAtOnce = 0
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt 5000) {
        $n = (Mine).Count
        if ($n -gt $maxAtOnce) { $maxAtOnce = $n }
        Start-Sleep -Milliseconds 50
    }
    $now = Mine
    $unclean = if (Test-Path "$diag\issues.jsonl") { @(Select-String -Path "$diag\issues.jsonl" -Pattern "unclean-exit").Count } else { 0 }

    $outcome = if ($now.Count -eq 0) { "none" }
               elseif ($now.Count -eq 1 -and $now[0].Id -eq $b.Id) { "new owner" }
               elseif ($now.Count -eq 1 -and $now[0].Id -ne $old.Id) { "relaunched" }
               else { "?" }
    # While the old one is still alive the handed-over launch process can exist for ~150 ms too
    # (it ends itself); two WINDOWS-owning Aviarys is what matters, so count only after 5 s.
    $ok = ($now.Count -le 1) -and ($unclean -eq 0) -and ($outcome -ne "?")
    "close after {0,3} ms: {1,-11} second launch {2}; now running: {3}; unclean-exit lines: {4} => {5}" -f `
        $s, $outcome, $(if ($b.HasExited) { "ended itself" } else { "is running" }), (($now | ForEach-Object Id) -join ","), $unclean, $(if ($ok) { "OK" } else { "FAIL" })
}
