# Kill a TEST Aviary's WebView2 browser process - the one engine process every tab hangs off
# (M4.9, bug 83). It is the msedgewebview2.exe whose parent is Aviary and whose command line has
# no --type= (renderers, GPU and utility processes all carry one). Only ever the test instance's.
#
#   kill-engine.ps1 -ProcessId <test Aviary pid>
param([int]$ProcessId)
$kids = Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe' AND ParentProcessId=$ProcessId"
$browser = $kids | Where-Object { $_.CommandLine -notmatch '--type=' } | Select-Object -First 1
if (-not $browser) { "no WebView2 browser process under pid $ProcessId"; exit 1 }
Stop-Process -Id $browser.ProcessId -Force
"killed the engine (pid $($browser.ProcessId)) of test Aviary $ProcessId"
