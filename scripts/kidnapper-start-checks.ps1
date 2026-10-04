param([ValidateRange(4,9)][int]$Clients=9,[string]$Stage='all')
$env:DOGS_QA_EVIDENCE='artifacts/test-results/kidnapper-0.2.1'
. "$PSScriptRoot/hardening-common.ps1"
function States { return ,@(1..$Clients | ForEach-Object {State $_}) }
function Player($s,[int]$id){$s.players|Where-Object id -eq $id}
function Button($s,[string]$name){$s.presentation.buttons|Where-Object type -eq ($name+'Button')}
function Verify([string]$label,[scriptblock]$test){
    $deadline=[DateTime]::UtcNow.AddSeconds(6)
    do {$states=States; $ok=[bool](& $test $states); if($ok){break}; Start-Sleep -Milliseconds 250}while([DateTime]::UtcNow -lt $deadline)
    $states|ConvertTo-Json -Depth 30|Set-Content "$evidenceRoot/$label.json"
    $line="$(if($ok){'PASS'}else{'FAIL'}): $label"; Add-Content "$evidenceRoot/results.txt" $line; Write-Host $line
    if(-not $ok){throw $line}
}
function Capture([int]$actor,[int]$target,[string]$label){
    Verify $label {param($s) @($s|Where-Object {$_.captures.Count -eq 1 -and $_.captures[0].actor -eq $actor -and $_.captures[0].target -eq $target}).Count -eq $Clients}
}
function Arrange([int]$actor,[int]$target){
    foreach($id in 0..($Clients-1)){Position $id (24+$id*.2) -3.5}
    Position $actor 16.6 -3.5; Position $target 17.2 -3.5
    Start-Sleep -Milliseconds 500
}
foreach($n in 1..$Clients){Cmd $n suspend @{value=$true}}
if(-not (State 1).started){
    Await { @((States)|Where-Object {$_.connected -and $_.players.Count -eq $Clients}).Count -eq $Clients } 'complete lobby replication' 45
    Cmd 1 role-settings @{roles=@('Penguin')}; Cmd 1 options @{map=2;impostors=2}; Cmd 1 start
    Await { @((States)|Where-Object {$_.started -and $_.canMove}).Count -eq $Clients } 'nine clients in round' 60
    $kid=(State 1).players|Where-Object role -eq 'PenguinRole'|Select-Object -First 1
    if(-not $kid){throw 'Natural allocator did not assign guaranteed Kidnapper'}
    $actor=[int]$kid.id; $target=[int]((State 1).players|Where-Object {-not $_.isImpostor}|Select-Object -First 1).id
    Arrange $actor $target
    Await {(Button (State ($actor+1)) 'Drag').canClick} 'natural intro Drag ready' 20
    Cmd ($actor+1) click-drag
    Capture $actor $target 'natural-nine-player-assignment-mouse-drag'
}
if($Stage -in @('all','activation')){
    foreach($client in @(1,2,$Clients)){
        $actor=$client-1; $target=if($actor -eq 0){1}else{0}
        Cmd 1 reset
        foreach($id in 0..($Clients-1)){Cmd 1 raw-role @{player=$id;role='Crewmate'}}
        Cmd 1 raw-role @{player=$actor;role='Bomber'}
        Cmd $client hud @{visible=$true}
        Start-Sleep -Milliseconds 500
        Cmd 1 raw-role @{player=$actor;role='Penguin'}
        Verify "client-$client-kidnapper-role-confirmed-on-all-peers" {param($s) @($s|Where-Object {(Player $_ $actor).roleId -eq 101 -and (Player $_ $actor).isImpostor}).Count -eq $Clients}
        Arrange $actor $target
        Verify "client-$client-role-transition-restores-drag-without-hud-refresh" {param($s) (Button $s[$client-1] 'Drag').canClick -and (Button $s[$client-1] 'Kill').canClick}
        Cmd $client button-role-disabled
        Verify "client-$client-role-filter-cannot-latch-drag-hidden" {param($s) (Button $s[$client-1] 'Drag').canClick}
        Cmd $client click-drag
        Capture $actor $target "client-$client-mouse-starts-capture-on-every-peer"
        Verify "client-$client-chain-and-movement-lock-start" {param($s) $s[$client-1].chainLinks -gt 0 -and -not $s[$target].canMove}
        Cmd 1 reset; Arrange $actor $target
        Cmd $client binding @{name='primary'}
        Capture $actor $target "client-$client-primary-key-starts-capture"
        Cmd 1 reset; Arrange $actor $target
        Cmd $client hud @{visible=$false}; Cmd $client click-drag
        Verify "client-$client-real-hidden-hud-rejects-capture" {param($s) @($s|Where-Object {$_.captures.Count -eq 0}).Count -eq $Clients -and -not (Button $s[$client-1] 'Drag').visible}
        Cmd $client hud @{visible=$true}; Cmd $client click-drag
        Capture $actor $target "client-$client-hud-return-restores-capture"
        Cmd 1 reset; Arrange $actor $target
        Cmd $client walk @{x=1;y=0;seconds=1.5}
        Cmd ($target+1) walk @{x=1;y=0;seconds=1.5}
        Cmd $client click-drag
        Capture $actor $target "client-$client-captures-while-players-move"
        Start-Sleep -Seconds 2
    }
}
if($Stage -in @('all','validation')){
    Cmd 1 reset
    foreach($id in 0..($Clients-1)){Cmd 1 raw-role @{player=$id;role='Crewmate'}}
    Cmd 1 raw-role @{player=1;role='Penguin'}; Cmd 2 hud @{visible=$true}; Arrange 1 0
    foreach($ability in @('Drag','Kill')){
        Cmd 1 capture-cooldown @{player=1;ability=$ability;seconds=3}
        Cmd 2 ability @{ability='Drag';target=0;count=5}
        Verify "host-rejects-$ability-cooldown-without-capture" {param($s) $s[0].playtestFixes.captureResult -eq ($ability+'Cooldown') -and @($s|Where-Object {$_.captures.Count -eq 0}).Count -eq $Clients}
        Await {(Button (State 2) 'Drag').canClick} 'cooldown expires' 10
        Cmd 2 click-drag; Capture 1 0 "$ability-cooldown-expiry-allows-initial-capture"
        Cmd 1 reset; Arrange 1 0
    }
    Position 0 24 -3.5
    Cmd 2 ability @{ability='Drag';target=0;count=5}
    Verify 'out-of-range-requests-rejected-without-consuming-cooldown' {param($s) $s[0].playtestFixes.captureResult -eq 'OutOfRange' -and $s[1].playtestFixes.cooldowns.Drag -le 0 -and @($s|Where-Object {$_.captures.Count -eq 0}).Count -eq $Clients}
    Position 0 17.2 -3.5; Cmd 2 click-drag; Capture 1 0 'rejected-request-can-immediately-retry-in-range'
    Cmd 1 reset; Arrange 1 0
    Cmd 1 raw-role @{player=0;role='Impostor'}
    Cmd 2 ability @{ability='Drag';target=0}
    Verify 'host-rejects-impostor-target' {param($s) $s[0].playtestFixes.captureResult -eq 'InvalidTarget' -and $s[0].captures.Count -eq 0}
    Cmd 1 raw-role @{player=0;role='Crewmate'}
    foreach($distance in 0..2){
        Cmd 1 settings-native @{killDistance=$distance}; Cmd 1 reset; Arrange 1 0
        Cmd 2 click-drag; Capture 1 0 "capture-with-kill-distance-setting-$distance"
    }
    Cmd 1 reset; Arrange 1 0
    Cmd 2 wall
    $wall=Get-Content "$qaRoot/.tools/hardening2/qa-wall.json" -Raw|ConvertFrom-Json
    Position 1 $wall.ax $wall.ay; Position 0 $wall.bx $wall.by
    Cmd 2 ability @{ability='Drag';target=0}
    Verify 'host-rejects-capture-through-wall' {param($s) $s[0].playtestFixes.captureResult -eq 'Wall' -and $s[0].captures.Count -eq 0}
    Arrange 1 0; Cmd 2 click-drag; Capture 1 0 'wall-rejection-does-not-break-subsequent-capture'
    Cmd 1 reset; Arrange 1 0
    Cmd 1 raw-role @{player=0;role='Veteran'}; Cmd 1 hud @{visible=$true}
    Cmd 1 button @{name='Alert'}; Cmd 2 click-drag
    Verify 'capture-still-respects-veteran-retaliation' {param($s) @($s|Where-Object {(Player $_ 1).dead -and -not (Player $_ 0).dead -and $_.captures.Count -eq 0}).Count -eq $Clients}
}
if($Stage -in @('all','meeting')){
    Cmd 1 reset
    foreach($id in 0..($Clients-1)){Cmd 1 raw-role @{player=$id;role='Crewmate'}}
    Cmd 1 raw-role @{player=1;role='Penguin'}; Arrange 1 0
    Cmd 1 meeting-options @{cooldown=12.5}
    Cmd 3 meeting; Await {(State 1).meeting} 'meeting'
    foreach($n in 1..$Clients){Cmd $n vote @{target=253}}
    FinishMeeting
    Verify 'meeting-kill-cooldown-also-gates-drag' {param($s) -not (Button $s[1] 'Drag').canClick -and $s[1].playtestFixes.cooldowns.Kill -gt 0}
    Arrange 1 0
    Await {(Button (State 2) 'Drag').canClick} 'post-meeting capture ready' 25
    Cmd 2 click-drag; Capture 1 0 'initial-drag-recovers-after-meeting'
    Cmd 1 meeting-options @{cooldown=30}
}
Write-Host "Initial Drag checks complete across $Clients actual clients."
