# Tab groups, end to end (Alpha Build 5 M5 stage 4). No desktop input: UI Automation presses the
# sidebar's Tab Groups pane, the group button and the name dialogs; DevTools opens pages and counts
# which tabs have an engine (a DORMANT tab has none - it is a title and an address until chosen).
#
# SAVED AS UTF-8 WITH BOM: it matches Aviary's own curly quotes and apostrophes.
#
#   groups-test.ps1 [-Port 9391]
param([int]$Port = 9391)

$kit = $PSScriptRoot
Set-Location $kit
$env:CDP_PORT = "$Port"
$pass = 0; $fail = 0
# PowerShell reads curly quotes as QUOTES, so the ones Aviary writes are spelled as characters.
$APOS = [char]0x2019; $LQ = [char]0x201C; $RQ = [char]0x201D; $ELL = [char]0x2026
$OWN = "This window$($APOS)s own tabs"
function Check($what, $ok, $saw) {
    if ($ok) { $script:pass++; "  PASS  $what" }
    else { $script:fail++; "  FAIL  $what  --  saw: $saw" }
}
foreach ($site in @(@('linksite.js', 'http://127.0.0.1:8795/links'), @('dlsite.js', 'http://127.0.0.1:8793/page'))) {
    try { Invoke-WebRequest -UseBasicParsing $site[1] -TimeoutSec 2 | Out-Null }
    catch { Start-Process node -ArgumentList $site[0] -WorkingDirectory $kit -WindowStyle Hidden | Out-Null; Start-Sleep 2 }
}

$script:pidv = 0; $script:profile = ""; $script:files = ""
function Launch([string]$name, [string]$extra = "", [switch]$Keep) {
    $script:profile = $name
    $script:files = Join-Path $kit "profiles\$name-files"
    if (-not $Keep) {
        if (Test-Path $script:files) { Remove-Item $script:files -Recurse -Force }
        New-Item -ItemType Directory -Force $script:files | Out-Null
        $seed = '{"improvementCollection":"true","startNews":"false","listsAutoUpdate":"Off","sidebarVisible":"true","sidebarPane":"Tab Groups","downloadDir":' + (ConvertTo-Json $script:files) + $extra + '}'
        & .\launch-quiet.ps1 -Profile $name -Fresh -Port $Port -SettingsJson $seed | Out-Null
    } else {
        & .\launch-quiet.ps1 -Profile $name -Port $Port | Out-Null
    }
    $script:pidv = [int](Get-Content pid.txt)
    Start-Sleep 4
}
function Quit { Stop-Process -Id $script:pidv -Force -ErrorAction SilentlyContinue; Start-Sleep 3 }
function U { param($Id = "", $Name = "", $Action = "Invoke", $Value = "", $Window = "") & .\uia.ps1 -ProcessId $script:pidv -Id $Id -Name $Name -Action $Action -Value $Value -Window $Window }
function Tree { (U -Action Dump) -join "`n" }
function Pages { @(node cdp.js list | Where-Object { $_ -match '^page' }) }
function Engines($n) { @(Pages | Where-Object { $_ -match "dest\?n=$n" }).Count }
# ForEach-Object unrolls: PowerShell 5.1's ConvertFrom-Json hands "[]" over as ONE object (an empty
# array), so @(...) of it counts 1 - which read as "a group is still there" after a delete.
function Groups { $f = "profiles\$script:profile\tabgroups.json"; if (Test-Path $f) { @(Get-Content $f -Raw | ConvertFrom-Json | ForEach-Object { $_ }) } else { @() } }
function Title { (Get-Process -Id $script:pidv).Refresh(); (Get-Process -Id $script:pidv).MainWindowTitle }
function NewTabAt([string]$n) {
    $before = (node cdp.js list) -join "`n"
    U -Id NewTabButton | Out-Null; Start-Sleep 1.5
    $id = (node cdp.js list | Where-Object { $_ -match '^page' } | ForEach-Object { $_.Substring(7, 8) } | Where-Object { $before -notmatch $_ } | Select-Object -First 1)
    node cdp.js nav $id "http://127.0.0.1:8795/dest?n=$n" | Out-Null; Start-Sleep 1.2
}
function NameIt([string]$name, [string]$verb) {
    Start-Sleep 1
    U -Name "Tab group name" -Action SetValue -Value $name | Out-Null
    U -Name $verb | Out-Null
    Start-Sleep 1.5
}
function Events($pattern) { $e = "profiles\$script:profile\user.data.improvementcollection\events.jsonl"; if (Test-Path $e) { @(Select-String -Path $e -Pattern $pattern) } else { @() } }

"`n1. nothing until there is a group; then a group made from the tabs in front - nothing reloads"
Launch "tg1"
Check "no group button while there are no groups" (-not ((Tree) -match 'Tab Groups:')) ""
Check "the sidebar's Tab Groups pane is there in the Top bar layout" ((Tree) -match "$OWN, 1 Tab, showing") ((U -Action Dump -Name 'own tabs') -join ' | ')
node cdp.js nav "#0" "http://127.0.0.1:8795/dest?n=a" | Out-Null; Start-Sleep 1.2
NewTabAt "b"
NewTabAt "c"
$ids = ((Pages) | ForEach-Object { $_.Substring(7, 8) }) -join ','
U -Name "New Tab Group with These 3 Tabs" | Out-Null
NameIt "Travel" "Create"
$g = @(Groups)
Check "tabgroups.json holds it, with its three pages in order" ($g.Count -eq 1 -and $g[0].Name -eq 'Travel' -and (($g[0].Tabs | ForEach-Object Url) -join ',') -match 'n=a,.*n=b,.*n=c') (($g | ConvertTo-Json -Depth 4 -Compress))
Check "and which was in front (the third)" ($g[0].Active -eq 2) $g[0].Active
Check "the same three pages are still open - nothing was reloaded" ((((Pages) | ForEach-Object { $_.Substring(7, 8) }) -join ',') -eq $ids) ""
Check "the toolbar now says which group is in front" ((Tree) -match 'Tab Groups: Travel') ((U -Action Dump -Name 'Tab Groups') -join ' | ')

"`n2. switching away puts the group away; switching back brings its tabs back DORMANT"
U -Name "$OWN, 0 Tabs" | Out-Null; Start-Sleep 2.5
Check "the group's tabs are closed" ((Engines 'a') + (Engines 'b') + (Engines 'c') -eq 0) ((Pages) -join ' | ')
Check "and the window shows a Start Page of its own" ((Title) -match '^Start Page') (Title)
Check "the group is still saved, whole" ((Groups)[0].Tabs.Count -eq 3) ""
U -Name "Tab group Travel, 3 tabs" | Out-Null; Start-Sleep 3
$t = Tree
Check "back: all three tabs are in the strip" ($t -match 'dest a' -and $t -match 'dest b' -and $t -match 'dest c') ""
Check "the one that was in front is in front again" ((Title) -match '^dest c') (Title)
Check "and ONLY it has an engine - the other two are dormant" ((Engines 'c') -eq 1 -and (Engines 'a') -eq 0 -and (Engines 'b') -eq 0) ((Pages) -join ' | ')
Check "the Start Page went with the window's own set" (@(Pages | Where-Object { $_ -match 'Aviary\s+about:blank' }).Count -eq 0) ((Pages) -join ' | ')
U -Name "dest a" -Action Toggle | Out-Null; Start-Sleep 2.5
Check "choosing a dormant tab starts it, and it loads where it was" ((Engines 'a') -eq 1 -and (Title) -match '^dest a') ("engines a=" + (Engines 'a') + ", " + (Title))
Check "the rouse is timed" ((Events '"k":"perf".*tab-rouse').Count + @(Select-String -Path "profiles\tg1\user.data.improvementcollection\perf.jsonl" -Pattern 'tab-rouse' -ErrorAction SilentlyContinue).Count -ge 1) ""

"`n3. the group in front keeps itself up to date - and survives Aviary being killed"
NewTabAt "d"
Start-Sleep 1.5
$g = @(Groups)
Check "a new tab in the group is in tabgroups.json a moment later" ((($g[0].Tabs | ForEach-Object Url) -join ',') -match 'n=d' -and $g[0].Tabs.Count -eq 4) (($g[0].Tabs | ForEach-Object Url) -join ',')
Quit   # a force-close: the close-time save never runs; the one-second save must already have happened
Launch "tg1" -Keep
Check "after a relaunch the group button is back" ((Tree) -match 'Tab Groups: 1 Tab') ((U -Action Dump -Name 'Tab Groups') -join ' | ')
U -Name "Tab group Travel, 4 tabs" | Out-Null; Start-Sleep 3
$t = Tree
Check "all four tabs come back" ($t -match 'dest a' -and $t -match 'dest b' -and $t -match 'dest c' -and $t -match 'dest d') ""
Check "with the last one in front" ((Title) -match '^dest d') (Title)

"`n4. rename and delete, from the group button's menu"
U -Id TabGroupButton | Out-Null; Start-Sleep 1
U -Name "Rename $($LQ)Travel$($RQ)$ELL" | Out-Null
NameIt "Trips" "Rename"
Check "renamed in the file" ((Groups)[0].Name -eq 'Trips') ((Groups)[0].Name)
Check "and on the button" ((Tree) -match 'Tab Groups: Trips') ""
U -Id TabGroupButton | Out-Null; Start-Sleep 1
U -Name "Delete $($LQ)Trips$($RQ)$ELL" | Out-Null; Start-Sleep 1
Check "deleting asks first" ((Tree) -match "Delete $($LQ)Trips$($RQ)\?") ""
U -Name "Delete" | Out-Null; Start-Sleep 2.5
Check "deleted: nothing left in the file" (@(Groups).Count -eq 0) (@(Groups).Count)
Check "the window is back on its own tabs" ((Title) -match '^Start Page') (Title)
Check "and the group button has gone" (-not ((Tree) -match 'Tab Groups:')) ""
Quit

"`n5. two groups, straight from one to the other; pinned tabs stay put through it all"
Launch "tg2" ',"pinnedTabs":"[\"http://127.0.0.1:8795/dest?n=pin\"]"'
node cdp.js nav "about:blank" "http://127.0.0.1:8795/dest?n=w1" | Out-Null; Start-Sleep 1.2
U -Name "New Tab Group with This Tab" | Out-Null
NameIt "Work" "Create"
U -Name "New Tab Group" | Out-Null
NameIt "Home" "Create"
Check "a new empty group shows a Start Page" ((Title) -match '^Start Page') (Title)
node cdp.js nav "about:blank" "http://127.0.0.1:8795/dest?n=h1" | Out-Null; Start-Sleep 2.5
U -Name "Tab group Work, 1 tab" | Out-Null; Start-Sleep 2.5
Check "Home -> Work: Work's tab is in front" ((Title) -match '^dest w1') (Title)
Check "and Home's is put away" ((Engines 'h1') -eq 0) ((Pages) -join ' | ')
Check "the pinned tab never left" ((Engines 'pin') -eq 1) ((Pages) -join ' | ')
$g = @(Groups)
Check "neither group swallowed the pinned tab" (-not ((($g | ForEach-Object { $_.Tabs } | ForEach-Object Url) -join ',') -match 'n=pin')) (($g | ForEach-Object { $_.Tabs } | ForEach-Object Url) -join ',')
Check "and each kept its own page" ((($g | Where-Object Name -eq 'Home').Tabs.Url -match 'h1') -and (($g | Where-Object Name -eq 'Work').Tabs.Url -match 'w1')) (($g | ConvertTo-Json -Depth 4 -Compress))
Quit

"`n6. putting a group away never kills a download (the tab is parked until it finishes)"
Launch "tg3"
node cdp.js nav "#0" "http://127.0.0.1:8793/page" | Out-Null; Start-Sleep 1.5
U -Name "New Tab Group with This Tab" | Out-Null
NameIt "Files" "Create"
node cdp.js click "dl probe" "#slow" | Out-Null; Start-Sleep 2
U -Name "$OWN, 0 Tabs" | Out-Null; Start-Sleep 2
Check "the tab went from the strip" (-not ((Tree) -match 'dl probe')) ""
$landed = $false
for ($i = 0; $i -lt 24 -and -not $landed; $i++) { Start-Sleep -Milliseconds 500; $landed = (Get-Item (Join-Path $script:files "slow.zip") -ErrorAction SilentlyContinue).Length -eq 6000000 }
Check "and the download finished anyway" $landed ((Get-ChildItem $script:files | ForEach-Object { "$($_.Name)=$($_.Length)" }) -join ', ')
Quit

"`n7. a private window has no tab groups"
Launch "tg4"
U -Name "New Tab Group" | Out-Null
NameIt "Kept" "Create"
U -Id MenuButton | Out-Null; Start-Sleep 1
U -Name "New Private Window" | Out-Null; Start-Sleep 3
$priv = (U -Action Dump -Window "Private") -join "`n"
Check "no group button in the private window" (-not ($priv -match 'Tab Groups:')) ""
Check "no Tab Groups pane either" (-not ($priv -match 'Tab group Kept')) ""
Check "while the normal window still has both (control)" (((U -Action Dump -Window "Start Page") -join "`n") -match 'Tab Groups: Kept') ""
Quit

"`n8. a Start Page is part of a set too (Safari counts it as a tab)"
Launch "tg5"
Check "a fresh window: 1 Tab, the Start Page" ((Tree) -match "$OWN, 1 Tab, showing") ((U -Action Dump -Name "own tabs") -join " | ")
U -Name "New Tab Group" | Out-Null
NameIt "Empty" "Create"
Check "the window's own Start Page is counted while it is put away" ((Tree) -match "$OWN, 1 Tab") ((U -Action Dump -Name "own tabs") -join " | ")
U -Name "$OWN, 1 Tab" | Out-Null; Start-Sleep 3
Check "and comes back AS a Start Page" ((Title) -match "^Start Page") (Title)
Check "and the empty group now holds ITS Start Page - 1 tab, as a new Safari group does" ((Tree) -match "Tab group Empty, 1 tab") ((U -Action Dump -Name "Tab group") -join " | ")
Quit

"`n--- nothing should have gone wrong ---"
foreach ($p in @('tg1', 'tg2', 'tg3', 'tg4', 'tg5')) {
    $iss = "profiles\$p\user.data.improvementcollection\issues.jsonl"
    if (Test-Path $iss) {
        foreach ($line in Get-Content $iss -Encoding UTF8) {
            if ($line -match 'unclean-exit') { continue }
            elseif ($line -match '"ui-stall"' -and $line -match 'ui menu') { "  (known, not new) $p opening a window stalled: " + ($line -replace '.*"ms":([\d.]+).*', '$1 ms') }
            else { "  UNEXPECTED $p : $($line.Substring(0, [Math]::Min(230, $line.Length)))" }
        }
    }
}
"(every profile is force-closed on purpose, so each one's unclean-exit line is expected and not shown)"
"`n$pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }
