param(
    [Parameter(Mandatory=$true)][string]$Package,
    [string]$Version = '0.3.0',
    [string]$GameVersion = '2026.9.29',
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$packageFile = Get-Item -LiteralPath $Package
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $GameVersion -notmatch '^\d{4}\.\d{1,2}\.\d{1,2}$') {
    throw 'Use a numeric three-part mod version and a YYYY.M.D game version.'
}
if ($packageFile.Name -ne "AmongUsDogsRoles-$Version.zip") { throw 'Package filename and version do not match.' }
if (-not $OutputPath) { $OutputPath = Join-Path $packageFile.DirectoryName 'launcher-release.json' }
@{
    schemaVersion = 1
    version = $Version
    gameVersion = $GameVersion
    platform = 'steam'
    architecture = 'x64'
    assetName = $packageFile.Name
    sha256 = (Get-FileHash -LiteralPath $packageFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    size = $packageFile.Length
} | ConvertTo-Json | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Launcher manifest: $OutputPath"
