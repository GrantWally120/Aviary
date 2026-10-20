# Launch the current local build with an isolated profile. This script sends no UI input.
param(
    [ValidateSet('regular','quik','labs')][string]$Mode = 'quik',
    [string]$Profile = '',
    [int]$Port = 9417,
    [switch]$Timers
)
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (!$Profile) { $Profile = 'm7-' + $Mode + '-' + [guid]::NewGuid().ToString('N').Substring(0,8) }
if ($Profile -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Use a simple scratch profile name.' }
$profilePath = Join-Path $PSScriptRoot ('profiles\' + $Profile)
New-Item -ItemType Directory -Path $profilePath -Force | Out-Null
$settingsPath = Join-Path $profilePath 'settings.json'
if (!(Test-Path -LiteralPath $settingsPath)) {
    $seed = @{
        improvementCollection='true'; startNews='false'; listsAutoUpdate='Off'
        startup='Start Page'; quik=([string]($Mode -ne 'regular')).ToLowerInvariant()
        'exp.quikTwoReady'=([string]($Mode -eq 'labs')).ToLowerInvariant()
        warnManyTabs='false'
    }
    [System.IO.File]::WriteAllText($settingsPath, ($seed | ConvertTo-Json))
}
$launch = [System.Diagnostics.ProcessStartInfo]::new()
$launch.FileName = Join-Path $root 'src\Aviary\bin\x64\Release\net9.0-windows10.0.19041.0\win-x64\Aviary.exe'
$launch.WorkingDirectory = Split-Path -Parent $launch.FileName
$launch.UseShellExecute = $false
$launch.Environment['AVIARY_PROFILE_DIR'] = $profilePath
$launch.Environment['AVIARY_TEST_BROWSER_ARGS'] = '--remote-debugging-port=' + $Port
$launch.Environment['AVIARY_QUIK_TEST'] = '1'
if ($Timers) {
    $launch.Environment['AVIARY_FORGET_TEST'] = '4'
    $launch.Environment['AVIARY_SLEEP_TEST'] = '5'
}
$app = [System.Diagnostics.Process]::Start($launch)
[pscustomobject]@{ProcessId=$app.Id;Profile=$profilePath;Port=$Port;Mode=$Mode} | ConvertTo-Json
