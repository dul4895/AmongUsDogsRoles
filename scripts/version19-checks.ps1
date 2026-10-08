param([string]$EvidenceDirectory='artifacts/test-results/v19/influencer')
$env:DOGS_QA_EVIDENCE=$EvidenceDirectory
. "$PSScriptRoot/hardening-common.ps1"
foreach($n in 1..4){Cmd $n suspend @{value=$true}}
Await { @((1..4|ForEach-Object {State $_})|Where-Object {$_.connected -and $_.players.Count -eq 4}).Count -eq 4 } 'four players in lobby'
if((State 1).started){throw 'Start these checks in a fresh four-client lobby.'}
Cmd 1 role-settings @{roles=@('Penguin','Sheriff','Coroner','Veteran')}
Cmd 1 'v19-ghost-options'
Check 'v12-custom-role-settings-match-host-on-all-clients' {param($s)
    @($s|Where-Object {
        @($_.version19.roleRates|Where-Object {$_.count -ne $_.configuredCount -or $_.chance -ne $_.configuredChance}).Count -eq 0 -and
        @($_.version19.roleRates|Where-Object {$_.count -eq 1 -and $_.chance -eq 100}).Count -eq 4
    }).Count -eq 4
}
StartRound 2
Check 'natural-custom-allocation-works-with-v12-options' {param($s)
    @($s|Where-Object {@($_.players|Where-Object {$_.roleId -ge 100 -and -not $_.dead}).Count -eq 4}).Count -eq 4
}
Check 'influencer-remains-a-ghost-and-is-not-guessable' {param($s)
    @($s|Where-Object {$_.version19.process64 -and $_.version19.influencerRegistered -and $_.version19.influencerGhost -and -not $_.version19.influencerGuessable}).Count -eq 4
}
$state=State 1
$killer=($state.players|Where-Object isImpostor|Select-Object -First 1).id
$crew=@($state.players|Where-Object {-not $_.isImpostor}|Sort-Object id)
$victim=[int]$crew[0].id
$recipient=[int]$crew[1].id
Position $killer 16.6 -3.5
Position $victim 17.2 -3.5
Position $recipient 18 -3.5
Cmd 1 corpse @{actor=$killer;target=$victim}
Check 'killed-custom-crewmate-naturally-becomes-influencer' {param($s)
    @($s|Where-Object {@($_.players|Where-Object {$_.id -eq $victim -and $_.dead -and $_.roleId -eq 21}).Count -eq 1}).Count -eq 4
}
Cmd ($victim+1) 'v19-influencer-open' @{target=$recipient}
Check 'influencer-image-panel-and-native-ability-work' {param($s)
    $s[$victim].version19.panelOpen -and $s[$victim].version19.imageChoices -gt 0
}
Cmd ($victim+1) 'v19-influencer-send'
Check 'influencer-images-arrive-on-another-client' {param($s)
    $s[$recipient].version19.activeMessages + $s[$recipient].version19.queuedMessages -gt 0
}
Roles @('Faker','Impostor','Crewmate','Crewmate'); Nearby
Cmd 1 button @{name='Fake'}
Check 'fake-death-does-not-grant-influencer-role' {param($s)
    @($s|Where-Object {@($_.players|Where-Object {$_.id -eq 0 -and $_.dead -and $_.roleId -ge 100}).Count -eq 1 -and $_.fakers.Count -eq 1}).Count -eq 4
}
Cmd 1 button @{name='Unfake'}
Write-Host 'Version 19 and Influencer integration checks complete.'
