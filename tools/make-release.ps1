<#
    Packs the two ChatVoice data zips from the live data folder and prints the
    SHA-256 of each.

    Usage:
        powershell -ExecutionPolicy Bypass -File tools\make-release.ps1
        powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 -OutDir D:\release

    The hashes it prints are pinned in ChatVoice\AssetInstaller.cs. If you
    rebuild the zips, update those constants and rebuild the .tmod before
    publishing, or the mod will reject its own download.

    Entry names are written with forward slashes on purpose. Compress-Archive
    and ZipFile.CreateFromDirectory both emit backslashes under Windows
    PowerShell, which some extractors read as a filename rather than a path.
#>

param(
    [string]$DataDir = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) `
                        'My Games\Terraria\tModLoader\ChatVoice'),
    [string]$OutDir = (Join-Path $PSScriptRoot '..\release')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-DataZip {
    param([string]$SourceDir, [string]$Prefix, [string]$ZipPath)

    if (-not (Test-Path $SourceDir)) {
        throw "Missing $SourceDir - build libpiper and run get-voices.ps1 first."
    }

    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }

    $zip = [System.IO.Compression.ZipFile]::Open($ZipPath, 'Create')
    try {
        $root = (Resolve-Path $SourceDir).Path.TrimEnd('\')
        $count = 0

        foreach ($file in Get-ChildItem -Path $SourceDir -Recurse -File) {
            $relative = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
            $entryName = "$Prefix/$relative"
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $file.FullName, $entryName, 'Optimal') | Out-Null
            $count++
        }
    }
    finally {
        $zip.Dispose()
    }

    $item = Get-Item $ZipPath
    $hash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLower()

    Write-Host ("  {0}  ({1:N0} files, {2:N1} MB)" -f $item.Name, $count, ($item.Length / 1MB))
    Write-Host ("  sha256 = {0}`n" -f $hash)
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

Write-Host "Data folder: $DataDir"
Write-Host "Output:      $OutDir`n"

New-DataZip -SourceDir (Join-Path $DataDir 'native') -Prefix 'native' `
            -ZipPath  (Join-Path $OutDir 'ChatVoice-native-win-x64.zip')

New-DataZip -SourceDir (Join-Path $DataDir 'voices') -Prefix 'voices' `
            -ZipPath  (Join-Path $OutDir 'ChatVoice-voices.zip')

Write-Host "Pin these hashes in ChatVoice\AssetInstaller.cs, then rebuild the .tmod."
