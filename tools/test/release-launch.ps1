# Explicit scratch launch only; no UI input or real-profile access.
param([Parameter(Mandatory)][string]$Exe, [Parameter(Mandatory)][string]$Profile, [int]$Port = 9417)
$ErrorActionPreference = 'Stop'
if ($Profile -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Use a simple scratch profile name.' }
$folder = Join-Path $PSScriptRoot ('profiles\'+$Profile)
if (Test-Path -LiteralPath $folder) { throw 'Use a new profile for a release smoke test.' }
New-Item -ItemType Directory -Path $folder | Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'settings.json'), (@{improvementCollection='true';startNews='false';listsAutoUpdate='Off';quik='true';warnManyTabs='false'}|ConvertTo-Json))
$launch = [Diagnostics.ProcessStartInfo]::new()
$launch.FileName = (Resolve-Path -LiteralPath $Exe).Path
$launch.WorkingDirectory = Split-Path -Parent $launch.FileName
$launch.UseShellExecute = $false
$launch.Environment['AVIARY_PROFILE_DIR'] = $folder
$launch.Environment['AVIARY_TEST_BROWSER_ARGS'] = '--remote-debugging-port='+$Port
$launch.Environment['AVIARY_QUIK_TEST'] = '1'
$app = [Diagnostics.Process]::Start($launch)
[pscustomobject]@{processId=$app.Id;profile=$folder;exe=$launch.FileName;port=$Port}|ConvertTo-Json
