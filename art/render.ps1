# art フォルダーの SVG / HTML を Edge（headless）で PNG にする
# 使い方: ./art/render.ps1 <入力ファイル> <出力PNG> <幅> <高さ>
param([string]$In, [string]$Out, [int]$W = 512, [int]$H = 512)
$edge = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
$inFull = (Resolve-Path $In).Path
$outFull = [System.IO.Path]::GetFullPath($Out)
$uri = ([System.Uri]$inFull).AbsoluteUri
$profile = Join-Path $env:TEMP "cursorchan-edge-profile"
& $edge --headless=new --disable-gpu --hide-scrollbars --user-data-dir="$profile" `
    --default-background-color=00000000 --force-device-scale-factor=1 `
    --virtual-time-budget=4000 --window-size="$W,$H" --screenshot="$outFull" $uri 2>$null | Out-Null
Start-Sleep -Milliseconds 300
if (Test-Path $outFull) { "rendered $outFull" } else { "failed"; exit 1 }
