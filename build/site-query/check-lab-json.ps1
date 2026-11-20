param([string]$RuntimePath,[string]$JsonPath)
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$ErrorActionPreference = 'Stop'
foreach ($name in @('Newtonsoft.Json.dll','USTCORi.WebLabClient.Model.dll','WebLabClient.exe')) { [void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $RuntimePath $name))) }
$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $RuntimePath 'ustcori_crack.exe')))
$json=[IO.File]::ReadAllText($JsonPath,[Text.Encoding]::UTF8)
try { $v=$a.GetType('ustcori_crack.ExperimentQuery').GetMethod('ParseJson').Invoke($null,[object[]]@($json,'2')); $v | Select-Object LabID,LABNAME,LabFileUrl | Format-List; exit 0 }
catch { $e=$_.Exception; while($e) { Write-Output ($e.GetType().FullName+': '+$e.Message); $e=$e.InnerException }; exit 1 }
