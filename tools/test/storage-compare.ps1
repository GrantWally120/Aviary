# Settings -> Storage Usage against an independent count (M4.5). With the Storage Usage pane open in the
# test instance: asks the page to measure, reads its raw result, then counts the same two folders
# with Get-ChildItem. The install folder is static, so it must match to the byte. The profile is
# compared twice: live (Get-ChildItem reads folder listings, which lag for files the engine holds
# open - the page asks each file, so the page may be the more current) and after the instance
# closes, when every listing is exact.
#   storage-compare.ps1 -Profile storage [-Port 9333]
param([string]$Profile = "storage", [int]$Port = 9333)
$kit = $PSScriptRoot; Set-Location $kit; $env:CDP_PORT = "$Port"

node cdp.js eval Settings "window.__aviaryStorageLast = null; document.getElementById('st-refresh').click(); 'measuring'" | Out-Null
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 500
    $raw = node cdp.js eval Settings "JSON.stringify(window.__aviaryStorageLast)"
    if ($raw -and $raw -ne 'null' -and $raw -ne '"null"') { break }
}
$d = $raw | ConvertFrom-Json | ConvertFrom-Json
function Sum($path) { (Get-ChildItem -LiteralPath $path -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum) }

$installPage = ($d.categories | Where-Object { $_.key -in 'app', 'collection' } | Measure-Object bytes -Sum).Sum
$profilePage = ($d.categories | Where-Object { $_.key -notin 'app', 'collection' } | Measure-Object bytes -Sum).Sum
$inst = Sum $d.install
$prof = Sum $d.profile
"install folder: page {0:N0} bytes in {1} files | Get-ChildItem {2:N0} bytes in {3} files | difference {4:N0}" -f $installPage, (($d.categories | Where-Object { $_.key -in 'app','collection' } | Measure-Object files -Sum).Sum), $inst.Sum, $inst.Count, ($installPage - $inst.Sum)
"profile, live:  page {0:N0} bytes | Get-ChildItem {1:N0} bytes in {2} files | difference {3:N0}" -f $profilePage, $prof.Sum, $prof.Count, ($profilePage - $prof.Sum)
"categories add up: {0}" -f ((($d.categories | Measure-Object bytes -Sum).Sum) -eq $d.bytes)

$procId = [int](Get-Content pid.txt)
$p = Get-Process -Id $procId -ErrorAction SilentlyContinue
for ($n = 0; $p -and -not $p.HasExited -and $n -lt 6; $n++) { [void]$p.CloseMainWindow(); Start-Sleep 2 }
if ($p -and -not $p.HasExited) { Stop-Process -Id $procId -Force }
Start-Sleep 2
$closed = Sum $d.profile
"profile, after closing: Get-ChildItem {0:N0} bytes in {1} files (the page measured {2:N0} while it ran; closing writes history, the Privacy Report and the diagnostics summary)" -f $closed.Sum, $closed.Count, $profilePage
