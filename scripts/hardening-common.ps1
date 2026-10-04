$ErrorActionPreference='Stop'
$qaRoot=Split-Path $PSScriptRoot -Parent
$evidenceRoot=Join-Path $qaRoot $(if($env:DOGS_QA_EVIDENCE){$env:DOGS_QA_EVIDENCE}else{'artifacts/test-results/hardening'})
New-Item -ItemType Directory -Force $evidenceRoot | Out-Null
function Cmd([int]$c,[string]$op,[hashtable]$data=@{}) { & "$PSScriptRoot/qa.ps1" $c $op $data }
function State([int]$c) {
    for($retry=0;$retry -lt 10;$retry++) {
        try {return (Get-Content "$qaRoot/.tools/hardening$c/qa-state.json" -Raw | ConvertFrom-Json)}
        catch {Start-Sleep -Milliseconds 100}
    }
    throw "Cannot read client $c"
}
function Await([scriptblock]$condition,[string]$label,[int]$seconds=35) {
    $deadline=[DateTime]::UtcNow.AddSeconds($seconds)
    do {if(& $condition){return};Start-Sleep -Milliseconds 250} while([DateTime]::UtcNow -lt $deadline)
    throw "Timed out: $label"
}
function Evidence([string]$label) {
    $states=@(1..4 | ForEach-Object {State $_})
    $states | ConvertTo-Json -Depth 30 | Set-Content "$evidenceRoot/$label.json" -Encoding utf8
    return ,$states
}
function Check([string]$label,[scriptblock]$condition) {
    # A command acknowledgement precedes delivery to peers and their 200 ms
    # snapshots. Allow bounded network convergence before evaluating a result.
    Start-Sleep -Milliseconds 500
    $deadline=[DateTime]::UtcNow.AddSeconds(3)
    do {
        $states=@(1..4|ForEach-Object {State $_})
        $ok=[bool](& $condition $states)
        if($ok){break}
        Start-Sleep -Milliseconds 250
    } while([DateTime]::UtcNow -lt $deadline)
    $states | ConvertTo-Json -Depth 30 | Set-Content "$evidenceRoot/$label.json" -Encoding utf8
    $line="$(if($ok){'PASS'}else{'FAIL'}): $label"
    Add-Content "$evidenceRoot/results.txt" $line
    Write-Host $line
    if(-not $ok){throw $line}
}
function Roles([string[]]$roles) {
    Cmd 1 suspend @{value=$true}
    Cmd 1 reset
    for($i=0;$i -lt 4;$i++){Cmd 1 role @{player=$i;role=$roles[$i]}}
    Start-Sleep -Milliseconds 500
}
function Position([int]$player,[double]$x,[double]$y){Cmd 1 move @{player=$player;x=$x;y=$y}}
function FinishMeeting {
    # The base game's results animation can outlast the vote RPCs. Proceed is
    # ignored until enabled, so retry the normal action with a bounded deadline.
    $deadline=[DateTime]::UtcNow.AddSeconds(90)
    while($true) {
        $state=State 1
        $fakingHost=$state.started -and @($state.fakers|Where-Object id -eq $state.player).Count -eq 1
        if(-not $state.meeting -and -not $state.exile -and ($state.canMove -or $fakingHost)){return}
        if($state.meeting){Cmd 1 proceed}
        Start-Sleep -Seconds 4
        if([DateTime]::UtcNow -gt $deadline){throw 'Meeting did not finish'}
    }
}
function Nearby {
    Position 0 16.6 -3.5; Position 1 17.2 -3.5; Position 2 16.6 -2.7; Position 3 22 -3.5
}
function StartRound([int]$map=2) {
    Cmd 1 name @{value='QA Host'}; Cmd 2 name @{value='QA Two'}; Cmd 3 name @{value='QA Three'}; Cmd 4 name @{value='QA Four'}
    Cmd 1 options @{map=$map}
    Cmd 1 start
    Await { @((1..4|ForEach-Object {State $_}) | Where-Object {$_.started -and $_.canMove}).Count -eq 4 } 'all four in round' 45
    Start-Sleep -Milliseconds 800
}
function NextRound([int]$map=2) {
    foreach($i in 1..4){Cmd $i suspend @{value=$true}; Cmd $i navigate @{method='NextGame'}}
    Await {(State 1).players.Count -eq 4 -and -not(State 1).started} 'four players returned to lobby'
    StartRound $map
}
