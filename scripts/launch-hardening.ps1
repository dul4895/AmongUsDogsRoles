param([ValidateRange(1,9)][int]$Clients=4,[switch]$Dogs,[string]$ModDll,[string]$TestingDll)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
if(-not $ModDll){$ModDll=Join-Path $repoRoot 'src/AmongUsDogsRoles/bin/Release/net6.0/AmongUsDogsRoles.dll'}
if(-not $TestingDll){$TestingDll=Join-Path $repoRoot 'tests/AmongUsDogsRoles.Testing/bin/Release/net6.0/AmongUsDogsRoles.Testing.dll'}
if(-not(Test-Path -LiteralPath $ModDll) -or -not(Test-Path -LiteralPath $TestingDll)){throw 'Build the current mod and local testing helper before launching.'}
foreach($i in 1..$Clients) {
    $gameRoot=Join-Path $repoRoot ".tools/hardening$i"
    if(-not(Test-Path "$gameRoot/Among Us.exe")) {
        & robocopy (Join-Path $repoRoot '.tools/playtest') $gameRoot /E /XD .setup /XF Reactor.Debugger.dll qa-command.json qa-response.json qa-state.json /NFL /NDL /NJH /NJS /NP | Out-Null
        if($LASTEXITCODE -ge 8) {throw "Copy failed: $LASTEXITCODE"}
    }
    $exe=Join-Path $gameRoot 'Among Us.exe'
    if(Get-Process 'Among Us' -ErrorAction SilentlyContinue | Where-Object Path -eq $exe) {continue}
    # Selecting text in a Windows log console can block the game's log writes.
    # Keep file logging, but do not open a console alongside manual test windows.
    $consoleConfig=Join-Path $gameRoot 'BepInEx/config/BepInEx.cfg'
    if(Test-Path -LiteralPath $consoleConfig) {
        $configText=[IO.File]::ReadAllText($consoleConfig)
        $configText=[regex]::Replace($configText,'(?ms)(\[Logging\.Console\][^\[]*?^Enabled = )true','$1false')
        [IO.File]::WriteAllText($consoleConfig,$configText)
    }
    foreach($file in @('qa-command.json','qa-response.json','qa-state.json','qa-error.txt')) {
        $path=Join-Path $gameRoot $file
        if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}
    }
    Copy-Item -LiteralPath $ModDll -Destination "$gameRoot/BepInEx/plugins" -Force
    Copy-Item -LiteralPath $TestingDll -Destination "$gameRoot/BepInEx/plugins" -Force
    $launchArgs='--hardening -screen-fullscreen 0 -screen-width 960 -screen-height 600 -logFile hardening-unity.log'
    if($Dogs){$launchArgs+=' --dogs-test'}
    Start-Process -FilePath $exe -WorkingDirectory $gameRoot -WindowStyle Hidden -ArgumentList $launchArgs
    Write-Host "Started hardening$i"
    # MiraAPI presets live in the shared Windows profile. Concurrent startup can
    # fail with a file-sharing exception, so finish loading before the next copy.
    $deadline=[DateTime]::UtcNow.AddSeconds(90)
    while($true) {
        # The bridge rewrites this snapshot every 200 ms; retry partial reads.
        try {
            if ((Test-Path "$gameRoot/qa-state.json") -and ((Get-Content "$gameRoot/qa-state.json" -Raw | ConvertFrom-Json).scene -eq 'MainMenu')) { break }
        } catch { }
        if([DateTime]::UtcNow -gt $deadline){throw "hardening$i did not finish startup"}
        Start-Sleep -Milliseconds 500
    }
}
