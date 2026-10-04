param(
    [string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Among Us',
    [Parameter(Mandatory=$true)][string]$Destination
)
$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath $GamePath).Path.TrimEnd('\')
$installRoot = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
if ($installRoot.Equals($gameRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $installRoot.StartsWith($gameRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $gameRoot.StartsWith($installRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose a separate destination outside the Steam game directory.'
}
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'Among Us.exe'))) { throw 'Among Us.exe was not found.' }
$versionFile = Join-Path $gameRoot 'Among Us_Data/globalgamemanagers'
if (-not (Test-Path -LiteralPath $versionFile)) { throw 'Game version data was not found. Use a complete Steam installation.' }
# The executable's version is the Unity engine version, not the Among Us version.
$versionData = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($versionFile))
if ($versionData -notmatch '(?<![0-9.])2026\.8\.18(?![0-9.])') {
    throw 'Unsupported Among Us version. AmongUsDogsRoles 0.2.1 requires Steam 2026.8.18. No destination files have been changed.'
}
if (Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx')) { throw 'Use a clean, unmodded Steam installation as the source.' }
if ((Test-Path -LiteralPath $installRoot) -and (Get-ChildItem -LiteralPath $installRoot -Force | Select-Object -First 1)) {
    throw 'Destination must be empty. No existing files have been changed.'
}
$repoRoot = Split-Path $PSScriptRoot -Parent
$modDll = Join-Path $repoRoot 'AmongUsDogsRoles.dll'
if (-not (Test-Path -LiteralPath $modDll)) { $modDll = Join-Path $repoRoot 'src/AmongUsDogsRoles/bin/Release/net6.0/AmongUsDogsRoles.dll' }
if (-not (Test-Path -LiteralPath $modDll)) { throw 'Build AmongUsDogsRoles first or use the release zip.' }
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
$downloadRoot = Join-Path $installRoot '.setup'
New-Item -ItemType Directory -Path $downloadRoot | Out-Null
$loaderZip = Join-Path $downloadRoot 'loader.zip'
$loaderUrl = 'https://github.com/AU-Avengers/TOU-Mira/releases/download/1.7.3/TouMira.v1.7.3-x86-steam-itch.zip'
Write-Host 'Downloading the pinned Windows Steam loader...'
Invoke-WebRequest -Uri $loaderUrl -OutFile $loaderZip
$actualHash = (Get-FileHash -LiteralPath $loaderZip -Algorithm SHA256).Hash
$expectedHash = (Get-Content -LiteralPath (Join-Path $repoRoot 'loader.sha256')).Trim()
if ($actualHash -ne $expectedHash) { throw 'Loader checksum mismatch; installation stopped.' }
Expand-Archive -LiteralPath $loaderZip -DestinationPath (Join-Path $downloadRoot 'loader')
$loaderRoot = Join-Path $downloadRoot 'loader/TouMira v1.7.3-x86-steam-itch'
Write-Host 'Creating a separate modded game copy...'
Get-ChildItem -LiteralPath $gameRoot -Force | Copy-Item -Destination $installRoot -Recurse
$bepRoot = Join-Path $installRoot 'BepInEx'
$pluginsRoot = Join-Path $bepRoot 'plugins'
New-Item -ItemType Directory -Force -Path $pluginsRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $loaderRoot 'BepInEx/core') -Destination $bepRoot -Recurse
Copy-Item -LiteralPath (Join-Path $loaderRoot 'dotnet') -Destination $installRoot -Recurse
foreach ($file in @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version')) {
    Copy-Item -LiteralPath (Join-Path $loaderRoot $file) -Destination $installRoot
}
foreach ($file in @('MiraAPI.dll', 'Reactor.dll')) {
    Copy-Item -LiteralPath (Join-Path $loaderRoot "BepInEx/plugins/$file") -Destination $pluginsRoot
}
Copy-Item -LiteralPath $modDll -Destination $pluginsRoot
@{ mod='AmongUsDogsRoles'; version='0.2.1'; game='Steam 18.0.x / 2026.8.18'; loaderSha256=$actualHash } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $installRoot 'amongusdogsroles-install.json')
Write-Host "Installed. Keep Steam running and launch: $installRoot\Among Us.exe"
Write-Host 'Every player needs the same mod and game versions. First launch can take several minutes.'
