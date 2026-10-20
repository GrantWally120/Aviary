# Copies Grant's projects into Aviary's library (src\Aviary\Library\files), laid out exactly as they
# sit under Documents so every relative link inside them still resolves — the Wally Hub's
# "../Sliver a Wally2TTD Experience/Silver_v1_4.html" among them.
#
# READ-ONLY on Documents. Grant's files are history (memory: keep-old-files-as-history): this script
# opens them only to copy, never to write, and nothing here edits a copy to make it "work" either.
# Run it before publishing Aviary; the MSBuild target warns when files\ is missing.
#
#   powershell -NoProfile -File tools\library\sync.ps1
#
# Phase 5, Alpha Build 5 M2.5 — HANDOFF section 16.
param(
    [string]$SourceRoot = (Join-Path $env:USERPROFILE 'Documents'),
    [string]$DestinationRoot = '',
    [switch]$Prune,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
$docs = [IO.Path]::GetFullPath($SourceRoot).TrimEnd('\')
$repo = [IO.Path]::GetFullPath((Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$library = Join-Path $repo 'src\Aviary\Library\files'
$dest = if ($DestinationRoot) { [IO.Path]::GetFullPath($DestinationRoot).TrimEnd('\') } else { $library }
# A test destination may live under .artifact, never at the repository root or an arbitrary folder.
$scratch = (Join-Path $repo '.artifact') + '\'
if ($dest -ne $library -and -not $dest.StartsWith($scratch, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Destination must be the Collection files folder or a scratch folder under $scratch"
}
$ancestor = $dest
while ($ancestor.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) {
    $existingPath = Get-Item -LiteralPath $ancestor -Force -ErrorAction SilentlyContinue
    if ($existingPath -and ($existingPath.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing linked destination: $ancestor"
    }
    $ancestor = Split-Path -Parent $ancestor
}
if (Test-Path -LiteralPath $dest) {
    $linked = @(Get-ChildItem -LiteralPath $dest -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
    if ($linked.Count) { throw 'Refusing Collection destination containing symbolic links or junctions.' }
}

# Grant, 13 Sep 2026: "do not include claude linux, bcs tracker, claude model, parallax loading screen,
# P.A.M and damage report (damage report is active)". Enforced below, not just left off the list.
$excluded = @('BCS Tracker.html', 'claude-linux.html', 'claude-model-matrix.html',
              'parallax-loading.html', 'pam-database.html', 'steam-damage-report.html')

# Paths relative to Documents. A wildcard copies every match in that one folder (not below it).
$items = @(
    # ── The Wally series: the Complete Edition exactly as Grant keeps it ──────────────────
    'Wally2TTD Complete Edition\*.html'
    'Wally2TTD Complete Edition\Archive\*.html'
    # ── …and the rest of the series, wherever it lives ───────────────────────────────────
    'Sliver a Wally2TTD Experience\Silver_v1_*.html'
    'Wally2TTD Radio\index.old.html'
    'Wally2TTDv2.5\Wally2TTDv2.4.html'                      # "Rail Ops Crisis", 1 May 21:35
    'Wally2TTDv2.5\wally2ttd_full_evolution.html'
    'index.html'                                            # the v1.8 desktop (Electron) edition's page
    'Claude CoWork Enclosed\Wally2TTDv2\Wally2TTDv2.4.html' # "Rail Ops Crisis", 1 May 21:58
    'Claude CoWork Enclosed\Wally2TTD-Godot\build\screenshots\*.png'  # 2.4.2's own screenshots (25 Jul)
    'Claude CoWork Enclosed\WallyCore1\build\screenshots\*.png'       # WallyCore1's render/demo captures
    # ── Everything else in the CoWork folder that a browser can open ─────────────────────
    'Claude CoWork Enclosed\artemis.html'
    'Claude CoWork Enclosed\console-index-2026.html'
    'Claude CoWork Enclosed\ember.html'
    'Claude CoWork Enclosed\Game Ledger.html'
    'Claude CoWork Enclosed\grants-birthday-gazette.html'
    'Claude CoWork Enclosed\happy-birthday-grant.html'
    'Claude CoWork Enclosed\jurassic-timeline.html'
    'Claude CoWork Enclosed\rog-experience.html'
    'Claude CoWork Enclosed\spec-showdown.html'
    'Claude CoWork Enclosed\the-ai-ledger.html'
    'Claude CoWork Enclosed\the-lost-part-1.html'
    'Claude CoWork Enclosed\work-log-2026-08.html'
    'Claude CoWork Enclosed\Filma\filma.html'
    'Claude CoWork Enclosed\jurassic-empire\JurassicEmpireLandingSite.HTML'
    'Claude CoWork Enclosed\jurassic-empire\JurassicEmpireRoadmap.html'
    'Claude CoWork Enclosed\jurassic-empire\README.md'
    'Claude CoWork Enclosed\jurassic-empire\notes\part2-design-briefs.md'
    'Claude CoWork Enclosed\jurassic-empire\build\screenshots\*.png'
    'Claude CoWork Enclosed\jurassic-empire\build\web\*'    # the playable web export
    'Claude CoWork Enclosed\Zoo\Aviary.html'
    'Claude CoWork Enclosed\Zoo\Aviary-Phase0.html'
    # ── The three experiment folders outside CoWork (Grant said yes, 14 Sep 2026) ────────
    # Named file by file: Codex EXP's parallax-loading.html is on the do-not-include list, and
    # AntiGravity Testing keeps each image twice (a timestamped copy the page never loads).
    'AntiGravity Testing\index.html'                        # PARALLAX — the game's site
    'AntiGravity Testing\ProjectDashboard.html'             # the Dev Log
    'AntiGravity Testing\assets\earth64-vanguard.jpg'
    'AntiGravity Testing\assets\hero-banner.jpg'
    'AntiGravity Testing\assets\megatonic-facility.jpg'
    'AntiGravity Testing\assets\xenar-invasion.jpg'
    'Codex EXP\console-spectrum.html'
    'vs testing\battery-animation.html'
    'vs testing\FileMakerBeta-App\index.html'               # the page and what it loads — not its
    'vs testing\FileMakerBeta-App\app.js'                   # Electron shell (main.js, preload.js),
    'vs testing\FileMakerBeta-App\styles.css'               # which a browser cannot run
)

$wanted = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$planned = New-Object 'System.Collections.Generic.List[object]'
$missing = New-Object 'System.Collections.Generic.List[string]'
$copied = 0; $same = 0; $bytes = 0L

foreach ($item in $items) {
    $folder = Split-Path -Parent $item
    $pattern = Split-Path -Leaf $item
    $source = if ($folder) { Join-Path $docs $folder } else { $docs }
    $found = @(Get-ChildItem -LiteralPath $source -File -Filter $pattern -ErrorAction SilentlyContinue)
    if ($found.Count -eq 0) { $missing.Add($item); continue }

    foreach ($file in $found) {
        if ($excluded -contains $file.Name) { throw "refusing: $($file.Name) is on Grant's do-not-include list" }
        $relative = $file.FullName.Substring($docs.Length + 1)
        $target = [IO.Path]::GetFullPath((Join-Path $dest $relative))
        if (-not $target.StartsWith($dest + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "refusing path outside Collection: $target" }
        [void]$wanted.Add($target)
        $bytes += $file.Length
        $planned.Add([pscustomobject]@{ File = $file; Relative = $relative; Target = $target })
    }
}

# Preflight before any writes. A moved workspace or absent USB must never empty the Collection.
if ($Prune -and $missing.Count) {
    throw "Prune refused before copying: $($missing.Count) source patterns are unavailable. Existing Collection files are preserved."
}
if ($missing.Count) { Write-Warning "$($missing.Count) source patterns unavailable under $docs; existing Collection copies will be retained." }
if (Test-Path -LiteralPath $dest) {
    $forbidden = @(Get-ChildItem -LiteralPath $dest -Recurse -File | Where-Object { $excluded -contains $_.Name })
    if ($forbidden.Count) { throw 'Collection contains an excluded file; review it before packaging.' }
}

foreach ($entry in $planned) {
        $file = $entry.File; $relative = $entry.Relative; $target = $entry.Target

        $existing = Get-Item -LiteralPath $target -ErrorAction SilentlyContinue
        if ($existing -and $existing.Length -eq $file.Length -and
            (Get-FileHash -LiteralPath $target).Hash -eq (Get-FileHash -LiteralPath $file.FullName).Hash) { $same++; continue }

        if ($WhatIf) { "would copy  $relative"; continue }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force   # keeps the original's timestamps
        $copied++
}

# Pruning is an explicit operation and requires every source pattern to be available.
$pruned = 0
if ($Prune -and (Test-Path -LiteralPath $dest)) {
    Get-ChildItem -LiteralPath $dest -Recurse -File | Where-Object { -not $wanted.Contains($_.FullName) } | ForEach-Object {
        if ($WhatIf) { "would remove $($_.FullName.Substring($dest.Length + 1))" } else { Remove-Item -LiteralPath $_.FullName -Force; $pruned++ }
    }
    Get-ChildItem -LiteralPath $dest -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending |
        Where-Object { -not (Get-ChildItem -LiteralPath $_.FullName -Force) } | ForEach-Object { if (-not $WhatIf) { Remove-Item -LiteralPath $_.FullName -Force } }
}

"library: {0} source files ({1:N1} MB) - {2} copied, {3} unchanged, {4} removed; {5} missing source patterns" -f $wanted.Count, ($bytes / 1MB), $copied, $same, $pruned, $missing.Count
