# The 23 Sep 2026 playtest bug #6 (Prism review P2-7): in Liquid Glass with Fluid motion and "New tab position: after the
# current tab", a tab inserted mid-strip got no slot of its own and overlapped its neighbours.
#
# Launches a scratch instance, makes three tabs, selects the FIRST and opens a new one (it lands second), then again after
# switching away and closing it, and reads every tab's rectangle through UI Automation. No desktop input: key events go to
# the pages over DevTools (tools/test/prism/keys.js), the same route the page-level shortcuts use.
#
#   tabstrip-overlap.ps1 [-Look "Liquid Glass"|Classic] [-Exe <path>] [-Port 9444] [-Layout "Top bar"]
param([string]$Look = "Liquid Glass", [string]$Exe = "", [int]$Port = 9444, [string]$Layout = "Top bar", [string]$Profile = "tabstripOverlap")

$kit = $PSScriptRoot
Set-Location $kit
. "$kit\default-exe.ps1"
if (-not $Exe) { $Exe = Get-AviaryExe $kit }
$env:CDP_PORT = "$Port"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$fails = 0
function Check($name, $ok, $detail) { if ($ok) { "  PASS  $name" } else { "  FAIL  $name  --  $detail"; $script:fails++ } }

$settings = '{"look":"' + $Look + '","tabLayout":"' + $Layout + '"}'
$out = & .\launch-quiet.ps1 -Profile $Profile -Fresh -Port $Port -SettingsJson $settings -Exe $Exe 2>&1
$proc = [int](($out | Select-String 'TEST PID (\d+)').Matches[0].Groups[1].Value)
Start-Sleep 3

function Pages { @(Invoke-RestMethod "http://127.0.0.1:$Port/json/list" | Where-Object { $_.type -eq 'page' }) }
function Key($target, $combo) { node "$kit\prism\keys.js" $Port $target $combo | Out-Null; Start-Sleep -Milliseconds 700 }
# A tiny local site: /Alpha is a page titled "Alpha", and so on. (An Aviary page ignores document.title, so every tab
# would read "Start Page"; real pages from a real server give each tab its own name.)
$siteScript = Join-Path $env:TEMP "tabstrip-site.js"
[IO.File]::WriteAllText($siteScript, "require('http').createServer((q,s)=>{s.setHeader('content-type','text/html');s.end('<title>'+decodeURIComponent(q.url.slice(1))+'</title><body>'+q.url)}).listen(9457,'127.0.0.1')")
$site = Start-Process node -ArgumentList $siteScript -PassThru -WindowStyle Hidden
Start-Sleep -Milliseconds 600
function Title($index, $text) { node "$kit\cdp.js" nav "#$index" "http://127.0.0.1:9457/$text" | Out-Null; Start-Sleep -Milliseconds 1200 }

function TabRects {
    $A = [System.Windows.Automation.AutomationElement]
    $win = $A::RootElement.FindAll('Children', (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $proc)))
    $rects = @()
    foreach ($w in $win) {
        $buttons = $w.FindAll('Descendants', (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
        foreach ($b in $buttons) {
            $n = $b.Current.Name; $r = $b.Current.BoundingRectangle
            if ($n -match '^(Start Page|Alpha|Beta|Gamma|Inserted|Delta)$' -and -not $b.Current.AutomationId -and $r.Width -gt 10) {
                $rects += [pscustomobject]@{ Name = $n; Left = [math]::Round($r.Left); Right = [math]::Round($r.Right); Top = [math]::Round($r.Top); Bottom = [math]::Round($r.Bottom) }
            }
        }
    }
    $rects | Sort-Object Top, Left
}
function Overlaps($rects) {
    $found = @()
    $sorted = $rects | Sort-Object Left
    for ($i = 1; $i -lt $sorted.Count; $i++) {
        $prev = $sorted[$i - 1]; $cur = $sorted[$i]
        if ([math]::Abs($prev.Top - $cur.Top) -lt 12 -and $cur.Left -lt $prev.Right - 2) { $found += "$($prev.Name) / $($cur.Name) overlap by $($prev.Right - $cur.Left) px" }
    }
    $found
}

try {
    # three tabs: Start Page, Alpha, Beta
    Key "#0" "ctrl+t"; Title 1 "Alpha"
    Key "#1" "ctrl+t"; Title 2 "Beta"
    Check "three tabs open" ((Pages).Count -eq 3) "$((Pages).Count) pages"

    # select the first, open another: it must land SECOND, with a slot of its own
    Key "#0" "ctrl+1"
    Key "#0" "ctrl+t"
    $p = Pages
    Check "four tabs open" ($p.Count -eq 4) "$($p.Count) pages"
    $new = 0..($p.Count - 1) | Where-Object { $p[$_].title -notin @("Aviary", "Alpha", "Beta") } | Select-Object -First 1
    if ($null -eq $new) { $new = 3 }
    Title $new "Inserted"
    Start-Sleep -Milliseconds 900

    $r = @(TabRects)
    $r | ForEach-Object { "      tab '{0}'  x {1}..{2}  y {3}..{4}" -f $_.Name, $_.Left, $_.Right, $_.Top, $_.Bottom }
    Check "every tab has a rectangle" ($r.Count -ge 4) "$($r.Count) found"
    $o = @(Overlaps $r)
    Check "no tab overlaps its neighbour after a mid-strip insert" ($o.Count -eq 0) ($o -join '; ')
    $order = ($r | Sort-Object Left | ForEach-Object { $_.Name }) -join ' > '
    Check "the new tab sits second" ($order -match '^[^>]+> Inserted') $order

    # switch away and back: the layout must stay right (the report said it stayed broken)
    Key "#0" "ctrl+3"; Key "#0" "ctrl+1"; Start-Sleep -Milliseconds 700
    $o2 = @(Overlaps @(TabRects))
    Check "still no overlap after switching tabs" ($o2.Count -eq 0) ($o2 -join '; ')
}
finally {
    $pr = Get-Process -Id $proc -ErrorAction SilentlyContinue
    if ($pr) { $pr.Kill() }
    if ($site) { Stop-Process -Id $site.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep 1
    Remove-Item -Recurse -Force "$kit\profiles\$Profile" -ErrorAction SilentlyContinue
}
if ($fails) { "tabstrip-overlap ($Look): $fails check(s) failed"; exit 1 } else { "tabstrip-overlap ($Look): all checks passed" }
