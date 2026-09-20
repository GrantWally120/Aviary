# What the element-hiding script costs a page (M4.5). The engine harness (tools\test\filters)
# writes the compiled script for the two standard lists and for all four into shots\; this wraps
# each one in a timing loop and runs it in a real page of the test instance, eleven times, and
# reports the median. That is the whole cost of "Hide the spaces ads leave": the script runs once
# per frame, before the page's own scripts.
#
# Open a website in the test instance first (node cdp.js nav Aviary https://example.com), then:
#   cosmetic-cost.ps1 [-Port 9333] [-Match Example]
param([int]$Port = 9333, [string]$Match = "Example")
$kit = $PSScriptRoot; Set-Location $kit; $env:CDP_PORT = "$Port"

foreach ($which in @("two", "four")) {
    $path = Join-Path $kit ("shots\cosmetic-" + $which + ".js")
    if (-not (Test-Path $path)) { "missing $path - run the filters harness first (dotnet run -c Release in filters\)"; continue }

    # The wrapper is written as a file because a Windows command line stops at about 32 KB and
    # these scripts are 0.8-2 MB; cdp.js evalfile reads it and sends it over CDP.
    $wrapper = Join-Path $kit ("shots\cosmetic-run-" + $which + ".js")
    node -e "const fs=require('fs');const s=fs.readFileSync(process.argv[1],'utf8');fs.writeFileSync(process.argv[2],'(function(){var s='+JSON.stringify(s)+';var t=[];for(var i=0;i<11;i++){var a=performance.now();(0,eval)(s);t.push(performance.now()-a);}t.sort(function(x,y){return x-y;});return JSON.stringify({kb:Math.round(s.length/1024),first:Math.round(t[10]*100)/100,median:Math.round(t[5]*100)/100});})()')" $path $wrapper

    $out = node cdp.js evalfile $Match $wrapper
    "$which lists: $out"
}
