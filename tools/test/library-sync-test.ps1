# Regression for the moved-workspace data-loss bug. All writes stay in .artifact.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$fixture = Join-Path $repo ('.artifact\library-sync-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$dest = Join-Path $fixture 'copy'
New-Item -ItemType Directory -Path $source,$dest -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $source 'index.html'), 'new source')
[IO.File]::WriteAllText((Join-Path $dest 'history.html'), 'preserve this history')
$sync = Join-Path $repo 'tools\library\sync.ps1'
& $sync -SourceRoot $source -DestinationRoot $dest
if ((Get-Content (Join-Path $dest 'index.html') -Raw) -ne 'new source') { throw 'Copy failed' }
if (!(Test-Path (Join-Path $dest 'history.html'))) { throw 'Default sync removed history' }
& $sync -SourceRoot (Join-Path $fixture 'unplugged') -DestinationRoot $dest
if (@(Get-ChildItem $dest -File).Count -ne 2) { throw 'Missing source removed copies' }
[IO.File]::WriteAllText((Join-Path $source 'index.html'), 'must not copy')
$blocked = $false
try { & $sync -SourceRoot $source -DestinationRoot $dest -Prune } catch { $blocked = $_.Exception.Message -like 'Prune refused*' }
if (!$blocked -or (Get-Content (Join-Path $dest 'index.html') -Raw) -ne 'new source') { throw 'Incomplete prune changed the mirror' }
$blocked = $false
try { & $sync -SourceRoot $source -DestinationRoot $repo } catch { $blocked = $_.Exception.Message -like 'Destination must*' }
if (!$blocked) { throw 'Repository-root destination was accepted' }
& $sync -SourceRoot $source -DestinationRoot $dest -WhatIf
if ((Get-Content (Join-Path $dest 'index.html') -Raw) -ne 'new source') { throw 'WhatIf wrote a file' }
'PASS: copy, retain history, missing source, atomic prune preflight, path guard, dry run (6 checks)'
