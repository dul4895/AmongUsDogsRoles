param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $repoRoot '.tools/dotnet/dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
if (-not $env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = Join-Path $repoRoot '.tools/cli' }
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $repoRoot '.nuget/packages' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $repoRoot
try {
    & $dotnetCommand run --project tests/AmongUsDogsRoles.Launcher.Checks -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Launcher checks failed.' }
    & $dotnetCommand build src/AmongUsDogsRoles.Launcher -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
    & $dotnetCommand run --project tests/AmongUsDogsRoles.Launcher.UiChecks -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Launcher window checks failed.' }
    if (-not $SkipPublish) {
        & $dotnetCommand publish src/AmongUsDogsRoles.Launcher -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false `
            -o artifacts/launcher
        if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }
        Copy-Item LICENSE, NOTICE artifacts/launcher
        # Include the notices shipped in the restored runtime packages.
        foreach ($runtime in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
            $packageRoot = Join-Path $env:NUGET_PACKAGES $runtime
            Get-ChildItem -LiteralPath $packageRoot -Directory | ForEach-Object {
                $runtimeVersion = $_.Name
                Get-ChildItem -LiteralPath $_.FullName -File | Where-Object { $_.Name -match 'LICENSE|NOTICE' } | ForEach-Object {
                    Copy-Item -LiteralPath $_.FullName -Destination "artifacts/launcher/$runtime-$runtimeVersion-$($_.Name).txt"
                }
            }
        }
        # Ship the exact launcher source alongside the executable, including local changes.
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $sourceFiles = @('global.json', 'LICENSE', 'NOTICE', 'docs/LAUNCHER.md', 'scripts/build-launcher.ps1')
        foreach ($directory in @('src/AmongUsDogsRoles.Launcher', 'src/AmongUsDogsRoles.Launcher.Core',
                'tests/AmongUsDogsRoles.Launcher.Checks', 'tests/AmongUsDogsRoles.Launcher.UiChecks')) {
            $sourceFiles += Get-ChildItem -LiteralPath $directory -File -Recurse |
                Where-Object { $_.FullName -notmatch '[/\\](bin|obj)[/\\]' } |
                ForEach-Object { [IO.Path]::GetRelativePath($repoRoot, $_.FullName) }
        }
        $sourceStream = [IO.File]::Create((Join-Path $repoRoot 'artifacts/launcher/AmongUsDogsRoles-Launcher-source.zip'))
        $sourceZip = [IO.Compression.ZipArchive]::new($sourceStream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($file in $sourceFiles) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sourceZip, (Join-Path $repoRoot $file), $file.Replace('\', '/')) | Out-Null
            }
        } finally { $sourceZip.Dispose(); $sourceStream.Dispose() }
        Write-Host 'Share artifacts/launcher/AmongUsDogsRoles-Launcher.exe. Players do not need to install .NET.'
    }
} finally { Pop-Location }
