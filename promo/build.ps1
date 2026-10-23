# Builds the TouchWrite promo videos and covers.
#
#   powershell -ExecutionPolicy Bypass -File promo\build.ps1                 # everything
#   powershell -ExecutionPolicy Bypass -File promo\build.ps1 -Langs hans     # one language
#   powershell -ExecutionPolicy Bypass -File promo\build.ps1 -CoversOnly
#
# Needs: Node 22+, Python 3 with numpy, Microsoft Edge, .NET 9 SDK. No ffmpeg: Windows' own H.264/AAC encoder is used.
param(
    [string[]]$Langs = @('hant', 'hans'),
    [int]$Browsers = 6,
    [int]$Tabs = 1,
    [switch]$CoversOnly,
    # Reuse frames from the last run (in %TEMP%\touchwrite-promo): only redo the music and the encode.
    [switch]$AudioOnly
)
$ErrorActionPreference = 'Stop'
$promo = $PSScriptRoot
$tools = Join-Path $promo 'tools'
$page = 'file:///' + ((Join-Path $promo 'src\promo.html') -replace '\\', '/')
$out = Join-Path $promo 'out'
$work = Join-Path $env:TEMP 'touchwrite-promo'
New-Item -ItemType Directory -Force $out, (Join-Path $out 'covers'), $work | Out-Null

$names = @{
    hant = @{ video = 'TouchWrite 宣傳片（繁體中文）.mp4'; cover = 'TouchWrite 封面（繁體）' }
    hans = @{ video = 'TouchWrite 宣传片（简体中文）.mp4'; cover = 'TouchWrite 封面（简体）' }
}

$encoder = Join-Path $tools 'encoder\bin\Release\net9.0-windows10.0.19041.0\PromoEncoder.exe'
if (-not $CoversOnly -and -not (Test-Path $encoder)) {
    dotnet build (Join-Path $tools 'encoder') -c Release -nologo | Out-Null
}

foreach ($lang in $Langs) {
    $url = "$page`?lang=$lang"

    # ---- covers (16:9 and 4:3)
    if (-not $AudioOnly) {
        node (Join-Path $tools 'capture.mjs') --url "$url&mode=cover169" --still 0 --png (Join-Path $out "covers\$($names[$lang].cover) 16x9.png")
        node (Join-Path $tools 'capture.mjs') --url "$url&mode=cover43" --width 1440 --height 1080 --still 0 --png (Join-Path $out "covers\$($names[$lang].cover) 4x3.png")
        node (Join-Path $tools 'capture.mjs') --url "$url&mode=cover169" --still 0 --png (Join-Path $out "covers\$($names[$lang].cover) 16x9.jpg") --quality 93
        node (Join-Path $tools 'capture.mjs') --url "$url&mode=cover43" --width 1440 --height 1080 --still 0 --png (Join-Path $out "covers\$($names[$lang].cover) 4x3.jpg") --quality 93
    }
    if ($CoversOnly) { continue }

    # ---- soundtrack (sound effects follow the cue list the page computes)
    $events = Join-Path $work "events-$lang.json"
    $wav = Join-Path $work "music-$lang.wav"
    node (Join-Path $tools 'capture.mjs') --url $url --eval 'JSON.stringify(window.__events)' --save $events
    $duration = node (Join-Path $tools 'capture.mjs') --url $url --eval 'window.__duration'
    python (Join-Path $tools 'music.py') $events $wav $duration

    # ---- frames
    $frames = Join-Path $work "frames-$lang"
    if (-not $AudioOnly) {
        if (Test-Path $frames) { Remove-Item $frames -Recurse -Force }
        node (Join-Path $tools 'capture.mjs') --url $url --out $frames --fps 60 --duration $duration --workers $Tabs --browsers $Browsers
    }

    # ---- encode (frames are kept for -AudioOnly re-runs; delete %TEMP%\touchwrite-promo to free the space)
    & $encoder $frames 60 $wav (Join-Path $out $names[$lang].video) 24
}
Write-Host "done -> $out"
