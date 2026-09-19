# Bug 50's sweep: min-window.ps1 once per delay, but only when nobody is using the machine —
# guard.ps1 is asked before EVERY launch (each one takes the foreground for a moment), and a
# launch waits until the machine has been quiet for 15 s. Gives up after -MaxWaitMinutes.
param([int[]]$Delays = @(0, 50, 100, 150, 200, 250, 300, 400, 600), [int]$Repeat = 2, [int]$MaxWaitMinutes = 30, [int]$RestoreAfterMs = 0)

$sp = $PSScriptRoot
$deadline = (Get-Date).AddMinutes($MaxWaitMinutes)
$alive = 0; $dead = 0
for ($r = 0; $r -lt $Repeat; $r++) {
  foreach ($d in $Delays) {
    while ($true) {
      & "$sp\guard.ps1" | Out-Null
      if ($LASTEXITCODE -eq 0) { break }
      if ((Get-Date) -gt $deadline) { "gave up waiting for a quiet machine"; "alive=$alive dead=$dead"; exit 3 }
      Start-Sleep -Seconds 10
    }
    $line = & "$sp\min-window.ps1" -Delays $d -RestoreAfterMs $RestoreAfterMs 2>$null | Where-Object { $_ -match '^delay' }
    $line
    if ($line -match 'alive=True') { $alive++ } else { $dead++ }
  }
}
"alive=$alive dead=$dead"
