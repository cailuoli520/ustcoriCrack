param([string]$RuntimePath,[string]$FixturePath,[string]$ReportPath)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
foreach($name in @('Newtonsoft.Json.dll','USTCORi.WebLabClient.Model.dll','WebLabClient.exe')) { [void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $RuntimePath $name))) }
$asm=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $RuntimePath 'ustcori_crack.exe')))
$method=$asm.GetType('ustcori_crack.ExperimentQuery').GetMethod('ParseJson')
function Parse([string]$json) { return $method.Invoke($null,[object[]]@($json,'2')) }
function Check([bool]$condition,[string]$detail) { if(-not $condition) { throw $detail } }
function Reject([string]$json) { $threw=$false; try { [void](Parse $json) } catch { $threw=$true }; Check $threw 'expected field conflict rejection' }
$checks=New-Object System.Collections.Generic.List[object]
function Case([string]$name,[scriptblock]$body) { & $body; $checks.Add([pscustomobject]@{name=$name;passed=$true}) }
$json=[IO.File]::ReadAllText($FixturePath,[Text.Encoding]::UTF8)
$wire=$json | ConvertFrom-Json
$epoch=New-Object DateTime(1970,1,1,0,0,0,[DateTimeKind]::Utc)
Case 'current-response-name-id-file-and-time' {
    $lab=Parse $json
    Check ($lab.LabID -eq 2 -and $lab.LABNAME -eq $wire.LabName -and $lab.LabFileUrl -eq $wire.LabFileUrl) 'required fields not mapped'
    Check ($lab.UpTime.ToUniversalTime() -eq $epoch.AddMilliseconds(1585793981000)) 'UpdateTime alias did not reach UpTime'
}
Case 'new-unknown-object-field-is-ignored' {
    $data=$json | ConvertFrom-Json; $data | Add-Member -NotePropertyName FutureMetadata -NotePropertyValue @{nested=@(1,2)}
    $lab=Parse ($data | ConvertTo-Json -Depth 8); Check ($lab.LabID -eq 2) 'unknown field was not ignored'
}
Case 'uppercase-legacy-name' {
    $lab=Parse '{"LabID":2,"LABNAME":"fixture","LabFileUrl":"fixture.lab"}'
    Check ($lab.LABNAME -eq 'fixture') 'uppercase name mapping failed'
}
Case 'optional-download-and-report-fields-preserved' {
    $lab=Parse '{"LabID":2,"LabName":"fixture","LabFileUrl":"fixture.lab","LabUpTime":"A","SourceFileUrl":"source.zip","SourceUpTime":"B","ReportFileUrl":"report.zip","ReportUpTime":"C"}'
    Check ($lab.LabUpTime -eq 'A' -and $lab.SourceFileUrl -eq 'source.zip' -and $lab.SourceUpTime -eq 'B' -and $lab.ReportFileUrl -eq 'report.zip' -and $lab.ReportUpTime -eq 'C') 'optional fields not retained'
}
Case 'same-scalar-name-alias-accepted' {
    $lab=Parse '{"LabID":2,"LabName":"fixture","LABNAME":"fixture","LabFileUrl":"fixture.lab"}'
    Check ($lab.LABNAME -eq 'fixture') 'consistent aliases rejected'
}
Case 'conflicting-string-name-alias-rejected' { Reject '{"LabID":2,"LabName":"first","LABNAME":"second","LabFileUrl":"fixture.lab"}' }
Case 'review-counterexample-object-vs-string-alias-rejected' { Reject '{"LabID":2,"LABNAME":{},"LabName":"{}","LabFileUrl":"fixture.lab"}' }
Case 'same-text-different-scalar-type-alias-rejected' { Reject '{"LabID":2,"labid":"2","LabName":"fixture","LabFileUrl":"fixture.lab"}' }
Case 'conflicting-update-time-alias-rejected' { Reject '{"LabID":2,"LabName":"fixture","LabFileUrl":"fixture.lab","UpdateTime":"/Date(1585793981000)/","UpTime":"/Date(1585793982000)/"}' }
$hash=(Get-FileHash -LiteralPath (Join-Path $RuntimePath 'ustcori_crack.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
$report=[pscustomobject]@{passed=$true;caseCount=$checks.Count;exeSha256=$hash;checks=$checks.ToArray();networkContacted=$false;clientApplicationConstructed=$false;experimentStarted=$false;recordSubmitted=$false}
[IO.File]::WriteAllText($ReportPath,($report | ConvertTo-Json -Depth 8),(New-Object System.Text.UTF8Encoding($false)))
Write-Output ('LAB_MODEL_MAPPING_PASS '+$checks.Count)
