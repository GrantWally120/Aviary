# Does forgetful browsing forget the right site, at the right time, and only once? (M4.9 stage 3.)
#
# Two SITES on one local server - http://localhost:8791 and http://127.0.0.1:8791 - so every check
# has a control: whatever happens to one must not happen to the other. 127.0.0.1 is deliberately the
# forgetful one, because an IPv4 literal is bug 52's danger zone (the "www.127.0.0.1" that once
# emptied a whole profile).
#
# AVIARY_FORGET_TEST=<seconds> turns "5 minutes after" into <seconds>, "an hour" into twice that and
# "a day" into three times, and makes the sweep tick every 2 s instead of 15. Environment only.
# No desktop input except Ctrl+W, which needs the window in front - guard.ps1 is checked first.
#
#   forget-test.ps1 [-Profile fg] [-Port 9333] [-Seconds 4]
param([string]$Profile = "fg", [int]$Port = 9333, [int]$Seconds = 4)

$kit = $PSScriptRoot
Set-Location $kit
$env:CDP_PORT = "$Port"
$pass = 0; $fail = 0
function Check($what, $ok, $saw) {
    if ($ok) { $script:pass++; "  PASS  $what" }
    else { $script:fail++; "  FAIL  $what  --  saw: $saw" }
}
$diag = "profiles\$Profile\user.data.improvementcollection\events.jsonl"
function Forgets {
    if (Test-Path $diag) { (Select-String -Path $diag -Pattern 'site-forgotten').Count } else { 0 }
}
function Report($match) { node cdp.js eval $match "report()" | ConvertFrom-Json }
function NewTab($pidv, $url) {
    $before = (node cdp.js list) -join "`n"
    & .\uia.ps1 -ProcessId $pidv -Id NewTabButton -Action Invoke | Out-Null
    Start-Sleep -Seconds 2
    $fresh = $null
    foreach ($line in (node cdp.js list)) {
        if ($line -match "about:blank") {
            $id = $line.Substring(7, 8)
            if ($before -notmatch $id) { $fresh = $id }
        }
    }
    if (-not $fresh) { throw "no new tab appeared" }
    node cdp.js nav $fresh $url | Out-Null
    Start-Sleep -Seconds 3
}
function Sites { Get-Content "profiles\$Profile\sites.json" -Raw -ErrorAction SilentlyContinue }

# The test server, if it is not already up.
try { Invoke-WebRequest -UseBasicParsing "http://localhost:8791/?read" -TimeoutSec 2 | Out-Null }
catch {
    Start-Process node -ArgumentList "forgetsite.js" -WorkingDirectory $kit -WindowStyle Hidden | Out-Null
    Start-Sleep -Seconds 2
}

& .\guard.ps1 | Out-Null
if ($LASTEXITCODE -ne 0) { "ABORT: someone is using the machine - Ctrl+W would land in their window"; exit 3 }

$env:AVIARY_FORGET_TEST = "$Seconds"
& .\launch-quiet.ps1 -Profile $Profile -Fresh -Port $Port `
    -SettingsJson '{"improvementCollection":"true","startNews":"false","listsAutoUpdate":"Off"}' | Out-Null
$env:AVIARY_FORGET_TEST = $null
$pidv = [int](Get-Content pid.txt)
"test instance $pidv, waits of ${Seconds}s"
Start-Sleep -Seconds 3

# Tab 0 stays on the Start Page, so closing a site's tab never closes the window.
NewTab $pidv "http://localhost:8791/"
NewTab $pidv "http://127.0.0.1:8791/"
$a = Report "localhost:8791"; $b = Report "127.0.0.1:8791"
"`n1. both sites hold everything before anything is forgotten"
Check "localhost filled" ($a.cookie -and $a.local -and $a.idb) $a
Check "127.0.0.1 filled" ($b.cookie -and $b.local -and $b.idb) $b

"`n2. the Shields popover sets it, and writes one key"
& .\uia.ps1 -ProcessId $pidv -Id ShieldsButton -Action Invoke | Out-Null
Start-Sleep -Milliseconds 1200
& .\uia.ps1 -ProcessId $pidv -Name "Forget 127.0.0.1" -Action Select -Value "When I close it" | Out-Null
Start-Sleep -Milliseconds 800
& .\uia.ps1 -ProcessId $pidv -Id ShieldsButton -Action Invoke | Out-Null
Start-Sleep -Milliseconds 600
Check "sites.json says close" ((Sites) -match 'forget":\s+"close"') (Sites)

"`n3. closing its last tab forgets it - and nothing else"
$before = Forgets
$h = (Get-Process -Id $pidv).MainWindowHandle
& .\type-hwnd.ps1 -Hwnd ([long]$h) -Text "^w" -WaitMs 1500 | Out-Null
Start-Sleep -Seconds 4
$still = (node cdp.js list) -join "`n"
Check "the 127.0.0.1 tab is gone" ($still -notmatch "127\.0\.0\.1:8791") $still
$a = Report "localhost:8791"
Check "the control site kept everything" ($a.cookie -and $a.local -and $a.idb) $a
NewTab $pidv "http://127.0.0.1:8791/?read"
$b = Report "127.0.0.1:8791"
Check "cookie forgotten" ($null -eq $b.cookie) $b.cookie
Check "local storage forgotten" ($null -eq $b.local) $b.local
Check "database forgotten" ($null -eq $b.idb) $b.idb

"`n4. it is forgotten ONCE, not on every sweep (bug 84)"
$after = Forgets
Check "exactly one forget" (($after - $before) -eq 1) ("$before -> $after")
Start-Sleep -Seconds 7
Check "still one after 7 more seconds" ((Forgets) -eq $after) (Forgets)

"`n5. being open again cancels, and re-arms"
Check "the forgotten mark is cleared while it is open" ((Sites) -notmatch 'forgot') (Sites)
$was = Forgets
& .\type-hwnd.ps1 -Hwnd ([long]$h) -Text "^w" -WaitMs 1500 | Out-Null
Start-Sleep -Seconds 5
Check "closing it again forgets it again" ((Forgets) -eq ($was + 1)) ("$was -> " + (Forgets))

"`n6. a timed mode waits"
& .\uia.ps1 -ProcessId $pidv -Id ShieldsButton -Action Invoke | Out-Null
Start-Sleep -Milliseconds 1200
& .\uia.ps1 -ProcessId $pidv -Name "Forget localhost" -Action Select -Value "5 minutes after" | Out-Null
Start-Sleep -Milliseconds 800
& .\uia.ps1 -ProcessId $pidv -Id ShieldsButton -Action Invoke | Out-Null
Start-Sleep -Milliseconds 600
$was = Forgets
& .\type-hwnd.ps1 -Hwnd ([long]$h) -Text "^w" -WaitMs 1200 | Out-Null
Start-Sleep -Milliseconds 2000
Check "not forgotten while its time runs" ((Forgets) -eq $was) ("$was -> " + (Forgets))
Check "a due time was written" ((Sites) -match 'forgetAt') (Sites)
Start-Sleep -Seconds ($Seconds + 5)
Check "forgotten once its time is up" ((Forgets) -eq ($was + 1)) ("$was -> " + (Forgets))
NewTab $pidv "http://localhost:8791/?read"
$a = Report "localhost:8791"
Check "localhost's data really went" ($null -eq $a.cookie -and $null -eq $a.local -and $null -eq $a.idb) $a

"`n7. a time that passed while Aviary was shut is still kept"
# Killing the process skips WM_ENDSESSION on purpose: this half asks only whether the first sweep
# of a new launch honours a due time written before it. (MarkForgetfulOnExit is covered by
# MainWindow_Closed and the session-end hook, which shutdown-test.ps1 drives.)
Stop-Process -Id $pidv -Force
Start-Sleep -Seconds 3
$json = (Sites) | ConvertFrom-Json
$map = @{}
foreach ($p in $json.PSObject.Properties) { $map[$p.Name] = $p.Value }
$map["localhost|forget"] = "1d"
$map["localhost|forgetAt"] = "1"
$map.Remove("localhost|forgot")
[System.IO.File]::WriteAllText((Join-Path $kit "profiles\$Profile\sites.json"), ($map | ConvertTo-Json))
$was = Forgets
$env:AVIARY_FORGET_TEST = "$Seconds"
& .\launch-quiet.ps1 -Profile $Profile -Port $Port | Out-Null
$env:AVIARY_FORGET_TEST = $null
$pidv = [int](Get-Content pid.txt)
Start-Sleep -Seconds 9
Check "the overdue site is forgotten on the first sweep after a launch" ((Forgets) -gt $was) ("$was -> " + (Forgets))

"`n--- nothing should have gone wrong ---"
$iss = "profiles\$Profile\user.data.improvementcollection\issues.jsonl"
# Step 7 kills the process on purpose, so the next launch rightly reports an unclean exit.
if (Test-Path $iss) {
    foreach ($line in Get-Content $iss) {
        if ($line -match "unclean-exit") { "  (expected) unclean exit - step 7 force-closed Aviary" }
        else { "  UNEXPECTED: $line" }
    }
} else { "  no issues.jsonl" }
$crashDir = "profiles\$Profile\user.data.improvementcollection\crashes"
if (Test-Path $crashDir) {
    $c = Get-ChildItem $crashDir -ErrorAction SilentlyContinue
    if ($c) { "  CRASHES: " + $c.Count } else { "  no crashes" }
}
"`n$pass passed, $fail failed"
Stop-Process -Id $pidv -Force -ErrorAction SilentlyContinue
if ($fail -gt 0) { exit 1 }
