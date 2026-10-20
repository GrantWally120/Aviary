# Creates a new review package under .artifact. Never replaces publish/Aviary or syncs originals.
param([switch]$NoRestore, [switch]$SourceOnly, [string]$OutputRoot = '')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (!$OutputRoot) { $OutputRoot = Join-Path $repo ('.artifact\release-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$output = [IO.Path]::GetFullPath($OutputRoot).TrimEnd('\')
if (!$output.StartsWith((Join-Path $repo '.artifact')+'\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Release staging must stay under .artifact.' }
if (Test-Path -LiteralPath $output) { throw 'Use a new release staging folder; existing releases are preserved.' }
New-Item -ItemType Directory -Path $output | Out-Null
$app = Join-Path $output 'Aviary'
$project = Join-Path $repo 'src\Aviary\Aviary.csproj'
$buildInfo = Get-Content -LiteralPath (Join-Path $repo 'src\Aviary\BuildInfo.cs') -Raw
$phaseNumber = [regex]::Match($buildInfo, 'public const int PhaseNumber = (\d+);')
$phaseShipped = [regex]::Match($buildInfo, 'public const int PhaseShipped = (\d+);')
if (!$phaseNumber.Success -or !$phaseShipped.Success) { throw 'BuildInfo must declare the current and shipped phases.' }
$preRelease = [int]$phaseShipped.Groups[1].Value -lt [int]$phaseNumber.Groups[1].Value
if (!$NoRestore) {
    & dotnet restore $project --locked-mode -p:Platform=x64 -p:RuntimeIdentifier=win-x64
    if ($LASTEXITCODE) { throw 'Restore failed.' }
}
& dotnet publish $project --no-restore -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 "-p:IncludePrivateCollection=$((!$SourceOnly).ToString().ToLowerInvariant())" -o $app -v:minimal
if ($LASTEXITCODE) { throw 'Publish failed.' }
$releaseName = if ($preRelease) { 'release candidate' } else { 'The Long Run' }
$releaseNotice = if ($preRelease) { 'This is an unsigned pre-release. Final AB5 release approval is pending.' } else { 'This is an unsigned Alpha Build 5 app. Quik remains in Beta.' }
$notice = @"
Aviary — Alpha Build 5 $releaseName (Windows x64)

Extract this entire Aviary folder, then double-click Aviary.exe. Keep its files together.
.NET and Windows App SDK are included. Microsoft Edge WebView2 Runtime is also required
(normally present on Windows 11). macOS support is planned for later.

$releaseNotice
Your profile is stored separately in %LOCALAPPDATA%\Aviary.
Filters\README.txt contains the bundled filter lists' attribution and licences.
The Collection contains copies; it never edits the original projects.
"@
if ($SourceOnly) { $notice += "`r`nThis source-only build excludes personal Collection files, thumbnails and game builds.`r`nUnavailable exhibits say 'Not in this copy'.`r`n" }
[IO.File]::WriteAllText((Join-Path $app 'README.txt'), $notice)
$manifest = & (Join-Path $PSScriptRoot 'verify.ps1') -Folder $app -SourceOnly:$SourceOnly
$commit = & git -C $repo rev-parse HEAD
$dirty = [bool](& git -C $repo status --porcelain)
$record = [ordered]@{ created=(Get-Date -Format o); commit=$commit; workingTreeDirty=$dirty; sdk=(& dotnet --version); preRelease=$preRelease; verification=$manifest }
$record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding utf8
$stage = if ($preRelease) { 'RC-' } else { '' }
$distribution = if ($SourceOnly) { 'source' } else { 'local' }
$label = 'Aviary-AB5-'+$stage+$distribution+'-win-x64'
$zip = Join-Path $output ($label+'.zip')
Compress-Archive -LiteralPath $app -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($zip+'.sha256'), ($hash+'  '+[IO.Path]::GetFileName($zip)+"`r`n"))
& (Join-Path $PSScriptRoot 'verify-archive.ps1') -Zip $zip | Write-Host
[pscustomobject]@{ folder=$app; zip=$zip; sha256=$hash; files=$manifest.files; exhibitsAvailable=$manifest.exhibitsAvailable; sourceOnly=[bool]$SourceOnly } | ConvertTo-Json
