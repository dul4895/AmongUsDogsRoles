param([string]$LoaderArchive, [string]$ReactorPackage, [switch]$SkipBuild, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$artifactRoot = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repoRoot 'artifacts' }
$work = Join-Path $artifactRoot ('package-' + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $work 'AmongUsDogsRoles-0.3.0'
New-Item -ItemType Directory -Force -Path "$stage/BepInEx/plugins" | Out-Null
if (-not $LoaderArchive) {
    $LoaderArchive = Join-Path $work 'loader.zip'
    Invoke-WebRequest 'https://github.com/AU-Avengers/TOU-Mira/releases/download/1.7.3/TouMira.v1.7.3-x64-epic-msstore.zip' -OutFile $LoaderArchive
}
if ((Get-FileHash -LiteralPath $LoaderArchive).Hash -ne (Get-Content "$repoRoot/loader.sha256").Trim()) {
    throw 'Loader checksum mismatch.'
}
if (-not $ReactorPackage) {
    $ReactorPackage = Join-Path $work 'reactor.zip'
    Invoke-WebRequest 'https://nuget.reactor.gg/v3/package/reactor/2.5.1-ci.400/reactor.2.5.1-ci.400.nupkg' -OutFile $ReactorPackage
}
if ((Get-FileHash -LiteralPath $ReactorPackage).Hash -ne '5014FF1D296D5CAFEC8C447FC3491758DE98208EA8585E90B17931360BF533A6') {
    throw 'Reactor checksum mismatch.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($LoaderArchive), "$work/loader")
[IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($ReactorPackage), "$work/reactor")
$loader = Join-Path $work 'loader/TouMira v1.7.3-x64-epic-msstore'
Copy-Item "$loader/BepInEx/core" "$stage/BepInEx" -Recurse
Copy-Item "$loader/dotnet" $stage -Recurse
foreach ($name in @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version')) { Copy-Item "$loader/$name" $stage }
Copy-Item "$loader/BepInEx/plugins/MiraAPI.dll" "$stage/BepInEx/plugins"
Copy-Item "$work/reactor/lib/net6.0/Reactor.dll" "$stage/BepInEx/plugins"
Copy-Item "$repoRoot/src/AmongUsDogsRoles/bin/Release/net6.0/AmongUsDogsRoles.dll" "$stage/BepInEx/plugins"
Copy-Item "$repoRoot/LICENSE", "$repoRoot/NOTICE" $stage
Copy-Item "$repoRoot/docs/UPDATE_0.3.0.md" "$stage/START_HERE.md"

# Include reproducible mod source, never game binaries, bindings, test outputs,
# local tools, or Git history. All source files obey the repository ignore rules.
$sourceRoot = Join-Path $work 'source'
$files = @(& git -c "safe.directory=$repoRoot" -C $repoRoot ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate project source.' }
foreach ($relative in $files) {
    $source = Join-Path $repoRoot $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
    $target = Join-Path $sourceRoot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}
$licenses = Join-Path $stage 'licenses-and-source'
New-Item -ItemType Directory -Path $licenses | Out-Null
[IO.Compression.ZipFile]::CreateFromDirectory($sourceRoot, "$licenses/AmongUsDogsRoles-source.zip")
$sources = @{
    'BepInEx-source.zip' = 'https://github.com/BepInEx/BepInEx/archive/0d275a4df3b6bbcb990a9715d0f037789624c377.zip'
    'MiraAPI-source.zip' = 'https://github.com/All-Of-Us-Mods/MiraAPI/archive/refs/tags/0.5.0.zip'
    'Reactor-source.zip' = 'https://github.com/NuclearPowered/Reactor/archive/100b89125cf83bd054c30d50cbd7df059bb431d7.zip'
    'Il2CppInterop-upstream-reference.zip' = 'https://github.com/BepInEx/Il2CppInterop/archive/refs/tags/v1.5.3.zip'
    'Doorstop-upstream-reference.zip' = 'https://github.com/NeighTools/UnityDoorstop/archive/71c5e43f4a17bff6c1d84a769079b8ddfbbc7210.zip'
    'dotnet-LICENSE.txt' = 'https://raw.githubusercontent.com/dotnet/runtime/v6.0.7/LICENSE.TXT'
    'dotnet-THIRD-PARTY-NOTICES.txt' = 'https://raw.githubusercontent.com/dotnet/runtime/v6.0.7/THIRD-PARTY-NOTICES.TXT'
}
foreach ($entry in $sources.GetEnumerator()) { Invoke-WebRequest $entry.Value -OutFile (Join-Path $licenses $entry.Key) }
$zip = Join-Path $artifactRoot 'AmongUsDogsRoles-0.3.0.zip'
if (Test-Path -LiteralPath $zip) { throw 'Package already exists; choose a new version before replacing a distributed ZIP.' }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
& (Join-Path $PSScriptRoot 'new-launcher-manifest.ps1') -Package $zip -Version '0.3.0' -GameVersion '2026.9.29'
Get-FileHash -LiteralPath $zip
Write-Host "Package: $zip"
