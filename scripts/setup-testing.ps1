param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$sourceRoot = Join-Path $repoRoot '.reference/Reactor'
$reactorCommit = '04100970b66d57a2dd96f6f4eae0383362e9a321'
if (-not (Test-Path (Join-Path $sourceRoot 'Reactor.Debugger/DebuggerPlugin.cs'))) {
    & git clone --depth 1 --branch 2.5.1 https://github.com/NuclearPowered/Reactor.git $sourceRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not download official Reactor source.' }
}
$actualCommit = & git -c "safe.directory=$sourceRoot" -C $sourceRoot rev-parse HEAD
if ($actualCommit -ne $reactorCommit) { throw 'Unexpected Reactor source revision.' }
if (-not $env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = Join-Path $repoRoot '.tools/cli' }
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $repoRoot '.nuget/packages' }
$sdk = Join-Path $repoRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path $sdk)) { $sdk = 'dotnet' }
if (-not $SkipBuild) {
    foreach ($project in @('tests/AmongUsDogsRoles.Testing', 'tests/Reactor.Debugger.Build')) {
        & $sdk build (Join-Path $repoRoot $project) -c Release
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
    & $sdk run --project (Join-Path $repoRoot 'tests/AmongUsDogsRoles.Checks') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Gameplay rule checks failed.' }
}
$hostRoot = Join-Path $repoRoot '.tools/playtest'
$peerRoot = Join-Path $repoRoot '.tools/playtest-client2'
foreach ($running in Get-Process 'Among Us' -ErrorAction SilentlyContinue) {
    if ($running.Path -in @((Join-Path $hostRoot 'Among Us.exe'), (Join-Path $peerRoot 'Among Us.exe'))) {
        throw 'Close the testing game windows before updating their plugins.'
    }
}
if (-not (Test-Path (Join-Path $hostRoot 'Among Us.exe'))) {
    & (Join-Path $PSScriptRoot 'install.ps1') -Destination $hostRoot
}
if (-not (Test-Path $peerRoot)) {
    New-Item -ItemType Directory -Path $peerRoot | Out-Null
    Get-ChildItem -LiteralPath $hostRoot -Force | Where-Object Name -ne '.setup' | Copy-Item -Destination $peerRoot -Recurse
}
foreach ($gameRoot in @($hostRoot, $peerRoot)) {
    if (-not (Test-Path (Join-Path $gameRoot 'Among Us.exe'))) { throw "Incomplete testing installation: $gameRoot" }
    $pluginRoot = Join-Path $gameRoot 'BepInEx/plugins'
    foreach ($dll in @('src/AmongUsDogsRoles/bin/Release/net6.0/AmongUsDogsRoles.dll',
        'tests/AmongUsDogsRoles.Testing/bin/Release/net6.0/AmongUsDogsRoles.Testing.dll',
        'tests/Reactor.Debugger.Build/bin/Release/net6.0/Reactor.Debugger.dll')) {
        Copy-Item -LiteralPath (Join-Path $repoRoot $dll) -Destination $pluginRoot -Force
    }
    $configRoot = Join-Path $gameRoot 'BepInEx/config'
    New-Item -ItemType Directory -Path $configRoot -Force | Out-Null
    $debugConfig = Join-Path $configRoot 'gg.reactor.debugger.cfg'
    if (-not (Test-Path $debugConfig)) {
        @'
[Features]
DisableGameEnd = true
AutoPlayAgain = false
DisableTimeout = false
[AutoJoin]
JoinGameOnStart = false
'@ | Set-Content -LiteralPath $debugConfig
    }
}
Write-Host 'Testing ready. Run Test Roles.cmd or Test Multiplayer.cmd from the project folder.'
