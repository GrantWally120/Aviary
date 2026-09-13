# Exports Wally2TTD 2.4.2 (Godot 4.7) as a web build, so the Collection can play it inside Aviary.
#
# It never touches Grant's project: the project is copied into tools\library\work\ (gitignored),
# a "Web" preset — the same settings Jurassic Empire's own web export uses, single-threaded so it
# needs no cross-origin isolation — is added to THE COPY, and the export lands in
# src\Aviary\Library\builds\wally2ttd-2.4.2-web\. Godot switches a web export to its Compatibility
# (WebGL 2) renderer by itself; 2.4.2 uses no threads, so nothing else changes.
#
#   powershell -NoProfile -File tools\library\export-godot-web.ps1
#
# Phase 5, Alpha Build 5 M2.5 — HANDOFF section 16.
$ErrorActionPreference = 'Stop'

# Grant's Godot lived on D:, which was gone on 13 Sep; the same official 4.7 build (signed by
# Prehensile Tales B.V.) was unpacked from his Downloads zip into work\godot. Web templates are
# the ones already installed under %APPDATA%\Godot\export_templates\4.7.stable.
$godot  = @('D:\Godot_v4.7-stable_win64_console.exe', (Join-Path $PSScriptRoot 'work\godot\Godot_v4.7-stable_win64_console.exe')) |
          Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $godot) { throw "Godot 4.7 not found (D:\ or tools\library\work\godot) - unpack Downloads\Godot_v4.7-stable_win64.exe.zip into work\godot" }
$source = Join-Path $env:USERPROFILE 'Documents\Claude CoWork Enclosed\Wally2TTD-Godot'
$repo   = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$work   = Join-Path $PSScriptRoot 'work\Wally2TTD-Godot'
$out    = Join-Path $repo 'src\Aviary\Library\builds\wally2ttd-2.4.2-web'

if (-not (Test-Path $godot))  { throw "Godot 4.7 not found at $godot" }
if (-not (Test-Path $source)) { throw "2.4.2 project not found at $source" }

# A fresh copy every time (its own import cache stays in the copy, never in Grant's folder).
robocopy $source $work /MIR /XD .godot build /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }

$preset = @'

[preset.1]

name="Web"
platform="Web"
runnable=true
advanced_options=false
dedicated_server=false
custom_features=""
export_filter="all_resources"
include_filter=""
exclude_filter="build/*"
export_path=""
encryption_include_filters=""
encryption_exclude_filters=""
encrypt_pck=false
encrypt_directory=false

[preset.1.options]

custom_template/debug=""
custom_template/release=""
variant/extensions_support=false
variant/thread_support=false
vram_texture_compression/for_desktop=true
vram_texture_compression/for_mobile=false
html/export_icon=true
html/custom_html_shell=""
html/head_include=""
html/canvas_resize_policy=2
html/focus_canvas_on_start=true
html/experimental_virtual_keyboard=false
progressive_web_app/enabled=false
'@
$presets = Join-Path $work 'export_presets.cfg'
if (-not (Select-String -Path $presets -Pattern 'platform="Web"' -Quiet)) { Add-Content -Path $presets -Value $preset -Encoding utf8 }

New-Item -ItemType Directory -Force -Path $out | Out-Null
Get-ChildItem $out -File | Remove-Item -Force

"importing..."
& $godot --headless --path $work --import 2>&1 | Select-Object -Last 5
"exporting..."
& $godot --headless --path $work --export-release 'Web' (Join-Path $out 'index.html') 2>&1 | Select-Object -Last 15
Get-ChildItem $out | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,2)}}
