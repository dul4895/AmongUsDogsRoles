param([string]$Stage='all',[string]$EvidenceDirectory='artifacts/test-results/playtest-fixes')
$env:DOGS_QA_EVIDENCE=$EvidenceDirectory
. "$PSScriptRoot/hardening-common.ps1"
function Player($state,[int]$id){$state.players|Where-Object id -eq $id}
function Vote($state,[int]$id){$state.votes|Where-Object id -eq $id}
foreach($c in 1..4){Cmd $c suspend @{value=$true}}
if(-not (State 1).started){
    Cmd 1 role-settings @{roles=@('Penguin','Bomber','Hacker','Faker','Sheriff','Coroner','Veteran','Jester')}
    StartRound 2
}
Cmd 1 meeting-options @{guesses=$true;cooldown=40}
if($Stage -in @('all','drag')){
    Roles @('Penguin','Crewmate','Crewmate','Crewmate'); Nearby
    Cmd 1 binding @{name='primary'}
    Check 'host-drag-key-does-not-immediately-release' {param($s) @($s|Where-Object {$_.captures.Count -eq 1}).Count -eq 4}
    Check 'kidnapper-native-wrapper-is-recognized' {param($s) $s[0].playtestFixes.wrappers.kidnapper}
    Cmd 1 button @{name='Release'}
    Check 'manual-release-clears-every-peer' {param($s) @($s|Where-Object {$_.captures.Count -eq 0}).Count -eq 4}
    Roles @('Crewmate','Penguin','Crewmate','Crewmate'); Nearby
    Cmd 2 button @{name='Drag'}
    Check 'remote-drag-is-accepted' {param($s) @($s|Where-Object {$_.captures.Count -eq 1 -and $_.captures[0].actor -eq 1}).Count -eq 4}
    Cmd 2 button @{name='Execute'}
    Check 'remote-execute-kills-and-cleans-up' {param($s) @($s|Where-Object {$_.captures.Count -eq 0 -and (Player $_ 0).dead}).Count -eq 4}
}
if($Stage -in @('all','coroner')){
    Roles @('Coroner','Impostor','Crewmate','Crewmate'); Nearby
    Cmd 1 corpse @{actor=1;target=2}
    Start-Sleep -Seconds 2
    Position 1 35 -12
    Cmd 1 ability @{ability='Examine';target=2}
    Check 'coroner-distant-trail-active' {param($s) $s[0].tracks.'0' -eq 1 -and $s[0].coronerArrow.arrowOnScreen}
    Start-Sleep -Seconds 10
    Check 'coroner-trail-expires' {param($s) -not $s[0].tracks.'0' -and -not $s[0].coronerArrow.arrowOnScreen}
    Cmd 1 ability @{ability='Examine';target=2}
    Check 'coroner-can-renew-expired-trail' {param($s) $s[0].tracks.'0' -eq 1}
    Position 1 20 -3.5
    Check 'coroner-trail-ends-near-killer' {param($s) -not $s[0].coronerArrow.arrowOnScreen}
}
if($Stage -in @('all','hacker','camera')){
    if($Stage -ne 'camera'){
    Roles @('Hacker','Crewmate','Crewmate','Scientist'); Nearby
    Cmd 2 open-vitals
    Cmd 4 scientist-vitals
    Cmd 1 button @{name='Hack'}
    Check 'hack-snapshot-and-pairs-replicate' {param($s) @($s|Where-Object {$_.playtestFixes.hack -and @($_.playtestFixes.pairs.PSObject.Properties).Count -eq 2}).Count -eq 4}
    Cmd 1 corpse @{actor=0;target=2}
    Check 'vitals-hide-kill-during-hack' {param($s) (Player $s[1] 2).dead -and -not @($s[1].playtestFixes.vitals|Where-Object id -eq 2)[0].dead}
    Cmd 2 close-info
    Cmd 2 open-vitals
    Check 'reopening-vitals-keeps-trigger-snapshot' {param($s) -not @($s[1].playtestFixes.vitals|Where-Object id -eq 2)[0].dead}
    Await {-not (State 2).playtestFixes.hack} 'hack expiry' 20
    Check 'vitals-restore-real-death-on-expiry' {param($s) @($s[1].playtestFixes.vitals|Where-Object id -eq 2)[0].dead}
    Cmd 2 close-info
    Cmd 4 close-info
    }
    Roles @('Hacker','Crewmate','Crewmate','Crewmate'); Nearby
    Cmd 2 open-cameras
    Cmd 1 button @{name='Hack'}
    Cmd 2 render-camera
    Check 'camera-render-hooks-run-during-hack' {param($s) $s[1].playtestFixes.cameraRenders -gt 0}
    Check 'camera-render-swaps-paired-names-and-colors-only' {param($s)
        $probe=$s[1].playtestFixes
        $valid=$true
        foreach($pair in $probe.pairs.PSObject.Properties){
            $shown=$probe.renderedIdentities|Where-Object id -eq ([int]$pair.Name)
            $partner=$probe.worldIdentities|Where-Object id -eq ([int]$pair.Value)
            $valid=$valid -and $shown.name -eq $partner.realName -and $shown.color -eq $partner.realColor
        }
        $valid -and @($probe.worldIdentities|Where-Object {$_.name -ne $_.realName -or $_.color -ne $_.realColor}).Length -eq 0
    }
    Cmd 2 close-info
    Check 'camera-view-closes-without-stuck-identity' {param($s) (Player $s[1] 0).name -eq 'QA Host'}
}
if($Stage -in @('all','admin')){
    Roles @('Hacker','Crewmate','Crewmate','Crewmate')
    Position 0 20 -17; Position 1 20.5 -17; Position 2 12 -23; Position 3 12.5 -23
    Cmd 2 open-admin; Cmd 1 button @{name='Hack'}
    Check 'admin-preserves-total-and-distorts-two-occupied-rooms' {param($s)
        $a=@($s[1].playtestFixes.admin|Where-Object count -gt 0); $numbers=@($a|ForEach-Object {$_.count})
        $a.Length -eq 2 -and ($numbers|Measure-Object -Sum).Sum -eq 4 -and ($numbers -contains 1) -and ($numbers -contains 3)
    }
    Await {-not (State 2).playtestFixes.hack} 'admin hack expiry' 20
    Check 'admin-restores-accurate-counts' {param($s)
        $a=@($s[1].playtestFixes.admin|Where-Object count -gt 0)
        $a.Length -eq 2 -and @($a|Where-Object count -ne 2).Length -eq 0
    }
    Cmd 2 close-admin
}
if($Stage -in @('all','faker')){
    Roles @('Faker','Impostor','Sheriff','Coroner'); Nearby
    Cmd 1 button @{name='Fake'}
    Cmd 2 meeting
    Await {(State 1).meeting} 'fake-death meeting'
    Check 'faking-faker-cannot-see-crew-roles-or-guess' {param($s) -not $s[0].playtestFixes.canGuess -and @($s[0].playtestFixes.visibleRoles|Where-Object {$_.id -in @(2,3) -and $_.visible}).Count -eq 0}
    Cmd 1 guess @{target=2;role='Sheriff'}
    Check 'host-rejects-faking-faker-guess' {param($s) @($s|Where-Object {-not (Player $_ 2).dead}).Count -eq 4}
    foreach($c in 2..4){Cmd $c vote @{target=253}}
    FinishMeeting
    Check 'meeting-cooldown-applies-to-impostor-and-sheriff' {param($s) $s[1].playtestFixes.cooldowns.Kill -gt 25 -and $s[2].playtestFixes.cooldowns.Shoot -gt 25}
    Cmd 1 button @{name='Unfake'}
    Check 'unfake-preserves-longer-meeting-kill-cooldown' {param($s) $s[0].playtestFixes.cooldowns.Kill -gt 30}
}
if($Stage -in @('all','guess')){
    Roles @('Impostor','Sheriff','Coroner','Impostor'); Nearby
    Cmd 2 meeting
    Await {(State 1).playtestFixes.canGuess} 'voting opens'
    Cmd 1 guess-picker @{target=1}
    Cmd 1 capture-picker
    Cmd 1 close-picker
    Cmd 3 vote @{target=1}
    Cmd 1 guess @{target=1;role='Sheriff'}
    Check 'correct-guess-kills-during-meeting-on-all-peers' {param($s) @($s|Where-Object {(Player $_ 1).dead -and -not (Player $_ 0).dead -and (Vote $_ 1).dead}).Count -eq 4}
    Check 'votes-for-guessed-player-are-cleared' {param($s) @($s|Where-Object {-not (Vote $_ 2).voted}).Count -eq 4}
    Cmd 1 guess @{target=2;role='Sheriff'}
    Check 'incorrect-guess-kills-only-guesser' {param($s) @($s|Where-Object {(Player $_ 0).dead -and -not (Player $_ 2).dead}).Count -eq 4}
    Cmd 1 guess @{target=2;role='Coroner'}
    Check 'dead-impostor-guess-rejected' {param($s) @($s|Where-Object {-not (Player $_ 2).dead}).Count -eq 4}
    Cmd 1 meeting-options @{guesses=$false}
    Cmd 4 guess @{target=2;role='Coroner'}
    Check 'disabled-remote-guess-rejected' {param($s) @($s|Where-Object {-not (Player $_ 2).dead}).Count -eq 4}
    Cmd 1 meeting-options @{guesses=$true}
    Cmd 4 guess @{target=2;role='Coroner'}
    Check 'remote-impostor-guess-kills-target-on-every-peer' {param($s) @($s|Where-Object {(Player $_ 2).dead -and -not (Player $_ 3).dead}).Count -eq 4}
    foreach($c in 3..4){Cmd $c vote @{target=253}}
    FinishMeeting
}
Write-Host 'Playtest fix integration checks complete.'
