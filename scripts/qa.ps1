param([Parameter(Mandatory=$true)][int]$Client,[Parameter(Mandatory=$true)][string]$Command,[hashtable]$Data=@{})
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$gameRoot=Join-Path $repoRoot ".tools/hardening$Client"
$request=@{id=[guid]::NewGuid().ToString();command=$Command}
foreach($key in $Data.Keys){$request[$key]=$Data[$key]}
$request | ConvertTo-Json -Depth 10 | Set-Content "$gameRoot/qa-command.tmp" -Encoding utf8
$replaceDeadline=[DateTime]::UtcNow.AddSeconds(3)
while($true) {
    try { [IO.File]::Move("$gameRoot/qa-command.tmp", "$gameRoot/qa-command.json", $true); break }
    catch { if([DateTime]::UtcNow -gt $replaceDeadline){throw}; Start-Sleep -Milliseconds 50 }
}
$deadline=[DateTime]::UtcNow.AddSeconds(8)
do {
    Start-Sleep -Milliseconds 250
    if(Test-Path "$gameRoot/qa-response.json") {
        try {$response=Get-Content "$gameRoot/qa-response.json" -Raw | ConvertFrom-Json} catch {continue}
        if($response.id -eq $request.id) {
            if(-not $response.ok) {throw $response.error}
            Write-Host "Client $Client : $Command OK"
            return
        }
    }
} while([DateTime]::UtcNow -lt $deadline)
throw "Client $Client did not acknowledge $Command"
