param([string]$RuntimePath,[string]$CasesPath,[string]$ReportPath)
$ErrorActionPreference = 'Stop'
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $RuntimePath 'Newtonsoft.Json.dll')))
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $RuntimePath 'ustcori_crack.exe')))
$type = $assembly.GetType('ustcori_crack.LaunchArguments', $true)
$parse = $type.GetMethod('Parse')
$cases = Get-Content -LiteralPath $CasesPath -Raw -Encoding UTF8 | ConvertFrom-Json
$results = @()
foreach ($case in $cases) {
  try {
    $parsed = $parse.Invoke($null, [object[]]@([string]$case.link))
    if ($case.fail) { throw "Expected FormatException for $($case.name)" }
    foreach ($property in $case.expect.PSObject.Properties) {
      $actual = $type.GetProperty($property.Name).GetValue($parsed, $null)
      if ($property.Name -eq 'SiteBaseUri') { $actual = $actual.AbsoluteUri }
      if ([string]$actual -cne [string]$property.Value) { throw "Mismatch $($case.name): $($property.Name) = $actual; expected $($property.Value)" }
    }
    $service = $type.GetMethod('ServiceAddress').Invoke($parsed,[object[]]@('BizService.svc')).AbsoluteUri
    if ($service -cne $case.service) { throw "Mismatch endpoint $($case.name): $service" }
    $results += [pscustomobject]@{name=$case.name;passed=$true;service=$service}
  } catch {
    $inner = $_.Exception.InnerException
    while ($inner -and $inner.InnerException) { $inner = $inner.InnerException }
    if ($case.fail -and $inner -is [FormatException]) {
      $results += [pscustomobject]@{name=$case.name;passed=$true;expectedFormatError=$true}
    } else { throw }
  }
}
foreach ($file in Get-ChildItem -LiteralPath $RuntimePath -File) {
  if (($file.Extension -eq '.dll' -or $file.Name -eq 'WebLabClient.exe') -and $file.Name -ne 'Newtonsoft.Json.dll') {
    [void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($file.FullName))
  }
}
$configure = $assembly.GetType('ustcori_crack.FakeApp', $true).GetMethod('ConfigureEndpoints', [Reflection.BindingFlags]'NonPublic,Static')
$configPath = Join-Path (Split-Path -Parent $ReportPath) 'config-roundtrip'
[void](New-Item -ItemType Directory -Path $configPath -Force)
$mainPath = Join-Path $configPath ([AppDomain]::CurrentDomain.FriendlyName + '.config')
Copy-Item -LiteralPath (Join-Path $RuntimePath 'ustcori_crack.exe.config') -Destination $mainPath -Force
$configChecks = @()
foreach ($name in @('https-port-prefix','legacy-five')) {
  $case = $cases | Where-Object { $_.name -eq $name } | Select-Object -First 1
  $parsed = $parse.Invoke($null,[object[]]@([string]$case.link))
  [void]$configure.Invoke($null,[object[]]@([string]$configPath,$parsed))
  [xml]$main = Get-Content -LiteralPath $mainPath -Raw -Encoding UTF8
  [xml]$client = Get-Content -LiteralPath (Join-Path $configPath 'WebLabClient.exe.config') -Raw -Encoding UTF8
  if ($main.OuterXml -cne $client.OuterXml) { throw "Dual config mismatch for $name" }
  $mode = if ($parsed.SiteBaseUri.Scheme -eq 'https') {'Transport'} else {'None'}
  foreach ($binding in $main.configuration.'system.serviceModel'.bindings.basicHttpBinding.binding) {
    if ($binding.security.mode -cne $mode) { throw "Binding mode mismatch for $name" }
  }
  foreach ($endpoint in $main.configuration.'system.serviceModel'.client.endpoint) {
    if (-not $endpoint.address.StartsWith($parsed.SiteBaseUri.AbsoluteUri)) { throw "Endpoint mismatch for $name" }
  }
  $configChecks += [pscustomobject]@{name=$name;passed=$true;bindingMode=$mode;dualConfigEqual=$true}
}
[pscustomobject]@{parserCases=$results.Count;passed=$true;framework=[Environment]::Version.ToString();results=$results;configChecks=$configChecks} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
Write-Output ('PARSER_CASES_PASS {0}' -f $results.Count)
