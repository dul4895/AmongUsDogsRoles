param([switch]$SkipChecks)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $repoRoot '.tools/dotnet/dotnet.exe'
$dotnetCommand = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
if (-not $env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = Join-Path $repoRoot '.tools/cli' }
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $repoRoot '.nuget/packages' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $repoRoot
try {
    & $dotnetCommand build src/AmongUsDogsRoles/AmongUsDogsRoles.csproj -c Release -p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw 'Mod build failed.' }
    if (-not $SkipChecks) {
        & $dotnetCommand run --project tests/AmongUsDogsRoles.Checks -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Gameplay rule checks failed.' }
    }
} finally { Pop-Location }
