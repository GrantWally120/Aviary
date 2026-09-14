# Bug 61's measurement: every way one page replaces another, each watched by flash-watch.ps1, so a
# build can be compared with another colour by colour. Needs bgsite.js running and a test instance
# (launch-m2.ps1). No mouse, no keyboard, no focus change: DevTools for pages and keys, UI
# Automation for the Home button — safe while Grant is at the PC.
#
#   bg-battery.ps1 -Label dark-before [-Only 1,5,9] [-Repeat 1]
#
# Each case prints the colour runs flash-watch saw at its three sample points (the right half of
# the page, where bgsite.js pages have no text) from just before the switch until well after it.
param([string]$Label = "run", [int]$ProcessId = 0, [int[]]$Only = @(), [int]$Repeat = 1)

$sp = $PSScriptRoot
if ($ProcessId -eq 0) { $ProcessId = [int](Get-Content (Join-Path $sp 'pid.txt')) }
$A = 'http://127.0.0.1:8772'; $B = 'http://127.0.0.2:8772'
$cdp = Join-Path $sp 'cdp.js'

function Pages { (& node $cdp list) | Where-Object { $_ -match '^page\s' } | ForEach-Object { ($_ -split '\s+')[1] } }
function TabOf([string]$substr) {
    foreach ($line in (& node $cdp list)) { if ($line -match '^page\s+(\S+)' -and $line.Contains($substr)) { return $Matches[1] } }
    return $null
}
function Go([string]$tab, [string]$url) {
    & node $cdp nav $tab $url | Out-Null
    Start-Sleep -Milliseconds 400
    & node $cdp wait $tab "document.readyState=='complete'" 8000 | Out-Null
    Start-Sleep -Milliseconds 700
}
function Home { & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $sp 'uia.ps1') -ProcessId $ProcessId -Id HomeButton -Action Invoke | Out-Null; Start-Sleep -Milliseconds 1200 }
function CloseNew([string[]]$before) {
    foreach ($id in (Pages)) {
        if ($before -notcontains $id) { & node $cdp key $id 'ctrl+KeyW' | Out-Null; Start-Sleep -Milliseconds 600 }
    }
}
function NewStart {
    & node $cdp key $tab 'ctrl+KeyT' | Out-Null; Start-Sleep -Milliseconds 1500
    $script:other = (Pages | Where-Object { $_ -ne $tab } | Select-Object -First 1)
}
function CloseStart {
    # Bring the Start Page tab to the front, then close it: keys reach only the tab in front.
    & node $cdp key $tab 'ctrl+Digit2' | Out-Null; Start-Sleep -Milliseconds 700
    & node $cdp key $script:other 'ctrl+KeyW' | Out-Null; Start-Sleep -Milliseconds 700
}
$uia = "powershell -NoProfile -ExecutionPolicy Bypass -File uia.ps1 -ProcessId $ProcessId -Id HomeButton -Action Invoke"

# The tab everything happens in: whichever page target is not one of Aviary's own.
$tab = (Pages | Select-Object -First 1)

$cases = @(
    @{ n = 1;  name = 'cross-site  plain -> black';            setup = { Go $tab "$A/plain" };  then = "node cdp.js nav $tab $B/black" },
    @{ n = 2;  name = 'cross-site  black -> plain';            setup = { Go $tab "$B/black" };  then = "node cdp.js nav $tab $A/plain" },
    @{ n = 3;  name = 'same-site   plain -> black';            setup = { Go $tab "$A/plain" };  then = "node cdp.js nav $tab $A/black" },
    @{ n = 4;  name = 'cross-site  white -> scheme-dark';      setup = { Go $tab "$A/white" };  then = "node cdp.js nav $tab $B/scheme-dark" },
    @{ n = 5;  name = 'cross-site  black -> slow CSS black';   setup = { Go $tab "$A/black" };  then = "node cdp.js nav $tab $B/slowcss?ms=1500&bg=000000"; ms = 3500 },
    @{ n = 6;  name = 'cross-site  black -> slow server black'; setup = { Go $tab "$A/black" }; then = "node cdp.js nav $tab $B/slowdoc?ms=1500&bg=000000"; ms = 3500 },
    @{ n = 7;  name = 'cross-site  plain -> slow CSS black';   setup = { Go $tab "$A/plain" };  then = "node cdp.js nav $tab $B/slowcss?ms=1500&bg=000000"; ms = 3500 },
    @{ n = 8;  name = 'link click  links -> black (cross)';    setup = { Go $tab "$A/links" };  then = "node cdp.js click $tab #x-black" },
    @{ n = 9;  name = 'Start Page -> black';                   setup = { Go $tab "$A/plain"; Home }; then = "node cdp.js nav $tab $B/black" },
    @{ n = 10; name = 'Start Page -> plain';                   setup = { Go $tab "$A/black"; Home }; then = "node cdp.js nav $tab $A/plain" },
    @{ n = 11; name = 'plain -> Start Page (Home)';            setup = { Go $tab "$A/plain" };  then = $uia; ms = 5000; at = 200 },
    @{ n = 12; name = 'black -> Start Page (Home)';            setup = { Go $tab "$B/black" };  then = $uia; ms = 5000; at = 200 },
    @{ n = 13; name = 'plain -> Settings (Ctrl+,)';            setup = { Go $tab "$A/plain" };  then = "node cdp.js key $tab ctrl+Comma"; newtab = $true },
    @{ n = 14; name = 'plain -> new tab (Ctrl+T)';             setup = { Go $tab "$A/plain" };  then = "node cdp.js key $tab ctrl+KeyT"; newtab = $true },
    @{ n = 15; name = 'link opens new tab -> black';           setup = { Go $tab "$A/links" };  then = "node cdp.js click $tab #n-black"; newtab = $true },
    @{ n = 16; name = 'link opens new tab -> plain';           setup = { Go $tab "$A/links" };  then = "node cdp.js click $tab #n-plain"; newtab = $true },
    @{ n = 17; name = 'link opens new tab -> slow CSS black';  setup = { Go $tab "$A/links" };  then = "node cdp.js click $tab #n-slowcss"; newtab = $true; ms = 3500 },
    @{ n = 18; name = 'link opens new tab -> empty page';      setup = { Go $tab "$A/links" };  then = "node cdp.js click $tab #n-empty"; newtab = $true },
    @{ n = 19; name = 'link opens new tab -> text/plain';      setup = { Go $tab "$A/links" };  then = "node cdp.js click $tab #n-text"; newtab = $true },
    @{ n = 20; name = 'cross-site  black -> text/plain';       setup = { Go $tab "$A/black" };  then = "node cdp.js nav $tab $B/text" },
    @{ n = 21; name = 'cross-site  black -> scheme-auto';      setup = { Go $tab "$A/black" };  then = "node cdp.js nav $tab $B/scheme-auto" },
    @{ n = 22; name = 'cross-site  black -> prefers';          setup = { Go $tab "$A/black" };  then = "node cdp.js nav $tab $B/prefers" },
    # Tab switches: a second tab (a Start Page) is opened in setup; the switch is a key pressed in
    # whichever tab is in front, and cleanup closes the Start Page tab again.
    @{ n = 23; name = 'tab switch  Start Page -> black tab';   setup = { Go $tab "$A/black"; NewStart };  then = { "node cdp.js key $script:other ctrl+Digit1" }; cleanup = { CloseStart } },
    @{ n = 24; name = 'tab switch  Start Page -> plain tab';   setup = { Go $tab "$A/plain"; NewStart };  then = { "node cdp.js key $script:other ctrl+Digit1" }; cleanup = { CloseStart } },
    @{ n = 25; name = 'tab switch  -> black tab loaded while hidden'; setup = { Go $tab "$A/plain"; NewStart; & node $cdp nav $tab "$B/slowdoc?ms=300&bg=000000" | Out-Null; Start-Sleep -Milliseconds 2500 }; then = { "node cdp.js key $script:other ctrl+Digit1" }; cleanup = { CloseStart } },
    @{ n = 26; name = 'tab switch  black tab -> Start Page';   setup = { Go $tab "$A/black"; NewStart; & node $cdp key $script:other 'ctrl+Digit1' | Out-Null; Start-Sleep -Milliseconds 900 }; then = { "node cdp.js key $tab ctrl+Digit2" }; cleanup = { CloseStart } }
)

$report = New-Object System.Collections.Generic.List[string]
$report.Add("== $Label  (pid $ProcessId, tab $tab) ==")
foreach ($c in $cases) {
    if ($Only.Count -gt 0 -and $Only -notcontains $c.n) { continue }
    for ($r = 1; $r -le $Repeat; $r++) {
        & $c.setup
        $before = @(Pages)
        $ms = if ($c.ms) { $c.ms } else { 2200 }
        $at = if ($c.at) { $c.at } else { 250 }
        $then = if ($c.then -is [scriptblock]) { & $c.then } else { $c.then }
        $out = & (Join-Path $sp 'flash-watch.ps1') -ProcessId $ProcessId -Ms $ms -At $at -Then $then
        $report.Add(("-- {0,2}. {1}{2}" -f $c.n, $c.name, $(if ($Repeat -gt 1) { "  (run $r)" } else { "" })))
        foreach ($line in (($out | Out-String) -split "`r?`n")) { if ($line -and $line -notmatch '^window ') { $report.Add("     " + ($line -replace '>> .*cdp\.js ', '>> ' -replace '>> .*uia\.ps1.*', '>> Home (UI Automation)')) } }
        if ($c.newtab) { CloseNew $before }
        if ($c.cleanup) { & $c.cleanup }
    }
}
$report | Tee-Object -FilePath (Join-Path $sp "shots\bg-$Label.txt")
