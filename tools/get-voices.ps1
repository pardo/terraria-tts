<#
    Downloads the Piper voice models ChatVoice uses.

    Usage:
        powershell -ExecutionPolicy Bypass -File tools\get-voices.ps1
        powershell -ExecutionPolicy Bypass -File tools\get-voices.ps1 -OutDir "D:\release\voices"

    Default output is the live data folder, so you can run this once and play:
        Documents\My Games\Terraria\tModLoader\ChatVoice\voices
#>

param(
    [string]$OutDir = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) `
                       'My Games\Terraria\tModLoader\ChatVoice\voices')
)

$ErrorActionPreference = 'Stop'
$base = 'https://huggingface.co/rhasspy/piper-voices/resolve/main'

$voices = @(
    @{ Path = 'en/en_US/libritts_r/medium'; Stem = 'en_US-libritts_r-medium'; Mb = 79 },
    @{ Path = 'es/es_ES/sharvard/medium';   Stem = 'es_ES-sharvard-medium';   Mb = 77 }
)

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Write-Host "Voice models -> $OutDir`n"

foreach ($v in $voices) {
    Write-Host "$($v.Stem) (~$($v.Mb) MB)"

    foreach ($suffix in @('.onnx', '.onnx.json')) {
        $dest = Join-Path $OutDir "$($v.Stem)$suffix"
        if ((Test-Path $dest) -and (Get-Item $dest).Length -gt 0) {
            Write-Host "  already have $($v.Stem)$suffix"
            continue
        }
        Write-Host "  downloading $($v.Stem)$suffix ..."
        Invoke-WebRequest -Uri "$base/$($v.Path)/$($v.Stem)$suffix" -OutFile $dest
    }

    # Each voice has its own license. Keep the model card next to the model.
    $card = Join-Path $OutDir "$($v.Stem).MODEL_CARD.txt"
    if (-not (Test-Path $card)) {
        try { Invoke-WebRequest -Uri "$base/$($v.Path)/MODEL_CARD" -OutFile $card }
        catch { Write-Warning "  could not fetch MODEL_CARD for $($v.Stem)" }
    }
    Write-Host ""
}

Write-Host "Done. Read the MODEL_CARD files before redistributing the models."
