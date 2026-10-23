# Verify a prepared folder. Never launches the app or changes a profile.
param([Parameter(Mandatory)][string]$Folder, [switch]$SourceOnly)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Folder).TrimEnd('\')
$required = @(
    'Aviary.exe','Aviary.dll','Aviary.pri','Aviary.deps.json','Aviary.runtimeconfig.json',
    'hostfxr.dll','coreclr.dll','Microsoft.ui.xaml.dll','Microsoft.Web.WebView2.Core.dll','WebView2Loader.dll',
    'Assets\Aviary.ico','Assets\Aviary-Glass.ico','Assets\Quik\loading.mp4',
    'Filters\easylist.txt','Filters\easyprivacy.txt','Filters\fanboy-cookiemonster.txt','Filters\fanboy-social.txt','Filters\README.txt',
    'Library\index.html','Library\catalog.json','README.txt'
)
foreach ($relative in $required) {
    $path = Join-Path $root $relative
    if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw "Missing release file: $relative" }
}
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$buildInfo = Get-Content -LiteralPath (Join-Path $repo 'src\Aviary\BuildInfo.cs') -Raw
$expectedVersion = [regex]::Match($buildInfo, 'public const string Version = "([0-9]+\.[0-9]+\.[0-9]+)"').Groups[1].Value
$fileVersion = (Get-Item -LiteralPath (Join-Path $root 'Aviary.exe')).VersionInfo.FileVersion
if (!$expectedVersion -or $fileVersion -ne ($expectedVersion+'.0')) { throw "Executable version $fileVersion differs from BuildInfo.Version $expectedVersion." }
$files = @(Get-ChildItem -LiteralPath $root -Recurse -File -Force)
$forbidden = @($files | Where-Object {
    $relative = $_.FullName.Substring($root.Length + 1)
    $relative -match '(^|\\)(\.git|profiles|EBWebView|user\.data\.improvementcollection)(\\|$)' -or
    $_.Name -in @('settings.json','cookies','history.json','BCS Tracker.html','claude-linux.html','claude-model-matrix.html','parallax-loading.html','pam-database.html','steam-damage-report.html')
})
if ($forbidden.Count) { throw "Forbidden profile, repository or excluded files in release ($($forbidden.Count))." }
if ($SourceOnly) {
    foreach ($name in 'files','thumbs','builds') {
        if (Test-Path -LiteralPath (Join-Path $root ('Library\' + $name))) { throw "Source-only release contains private Collection $name." }
    }
}
$catalog = Get-Content -LiteralPath (Join-Path $root 'Library\catalog.json') -Raw | ConvertFrom-Json
$available = 0
foreach ($exhibit in $catalog.exhibits) {
    $path = [IO.Path]::GetFullPath((Join-Path (Join-Path $root 'Library') ([Uri]::UnescapeDataString($exhibit.open))))
    if (!$path.StartsWith($root + '\Library\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Catalog path escapes the Collection.' }
    if (Test-Path -LiteralPath $path -PathType Leaf) { $available++ }
    elseif (!$SourceOnly) { throw "Missing Collection exhibit: $($exhibit.id)" }
}
$rows = @($files | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path=$_.FullName.Substring($root.Length+1).Replace('\','/'); bytes=$_.Length; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
[pscustomobject]@{ version=$expectedVersion; fileVersion=$fileVersion; files=$rows.Count; bytes=($files|Measure-Object Length -Sum).Sum; sourceOnly=[bool]$SourceOnly; exhibitsAvailable=$available; exhibits=$catalog.exhibits.Count; entries=$rows }
