param([ValidateRange(1,2)][int]$Clients = 1)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
foreach ($name in @('playtest', 'playtest-client2') | Select-Object -First $Clients) {
    $gameRoot = Join-Path $repoRoot ".tools/$name"
    $exe = Join-Path $gameRoot 'Among Us.exe'
    if (-not (Test-Path (Join-Path $gameRoot 'BepInEx/plugins/AmongUsDogsRoles.Testing.dll'))) {
        throw 'Run scripts/setup-testing.ps1 first.'
    }
    $running = Get-Process 'Among Us' -ErrorAction SilentlyContinue | Where-Object Path -eq $exe
    if ($running) { Write-Host "$name is already running."; continue }
    # Retain file logs without a selectable console that can pause the game.
    $consoleConfig = Join-Path $gameRoot 'BepInEx/config/BepInEx.cfg'
    if (Test-Path -LiteralPath $consoleConfig) {
        $configText = [IO.File]::ReadAllText($consoleConfig)
        $configText = [regex]::Replace($configText, '(?ms)(\[Logging\.Console\][^\[]*?^Enabled = )true', '$1false')
        [IO.File]::WriteAllText($consoleConfig, $configText)
    }
    foreach ($dll in @('src/AmongUsDogsRoles/bin/Release/net6.0/AmongUsDogsRoles.dll',
        'tests/AmongUsDogsRoles.Testing/bin/Release/net6.0/AmongUsDogsRoles.Testing.dll',
        'tests/Reactor.Debugger.Build/bin/Release/net6.0/Reactor.Debugger.dll')) {
        $built = Join-Path $repoRoot $dll
        if (Test-Path $built) { Copy-Item -LiteralPath $built -Destination (Join-Path $gameRoot 'BepInEx/plugins') -Force }
    }
    $pluginLog = Join-Path $gameRoot 'BepInEx/LogOutput.log'
    if (Test-Path $pluginLog) { Remove-Item -LiteralPath $pluginLog }
    # These are the interactive game windows the user requested, not background helpers.
    Start-Process -FilePath $exe -WorkingDirectory $gameRoot -WindowStyle Normal -ArgumentList '-screen-fullscreen 0 -screen-width 1100 -screen-height 750 -logFile testing-unity.log'
    # MiraAPI presets share the Windows profile. Avoid simultaneous file loading.
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while (-not ((Test-Path $pluginLog) -and (Select-String -Path $pluginLog -SimpleMatch 'Chainloader startup complete' -Quiet))) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "$name did not finish plugin startup; inspect $pluginLog" }
        Start-Sleep -Milliseconds 500
    }
}
Write-Host 'F2: role tests. F1: Reactor debugger. Use Practice for solo or Local for multiplayer.'
