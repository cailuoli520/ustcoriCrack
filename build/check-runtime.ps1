param(
    [string]$RuntimePath = $PSScriptRoot,
    [switch]$StrictPackage
)
$ErrorActionPreference = 'Stop'
try {
    $root = (Resolve-Path -LiteralPath $RuntimePath).Path
    $manifestPath = Join-Path $root 'runtime-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'RUNTIME_MANIFEST_MISSING' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $required = @(
        'ustcori_crack.exe', 'WebLabClient.exe',
        'USTCORi.WebLabClient.Model.dll', 'USTCORi.SimExp.Instruments.Component.dll',
        'USTCORI.CommonLib.Security.dll', 'PackageLibrary.dll', 'Newtonsoft.Json.dll',
        'ICSharpCode.SharpZipLib.dll', 'Unity3DWPFConnection.dll', 'WpfAnimatedGif.dll'
    )
    $entries = @($manifest.files)
    foreach ($name in $required) {
        $entry = @($entries | Where-Object { $_.name -ceq $name })
        if ($entry.Count -ne 1 -or -not $entry[0].assemblyFullName) { throw "MANIFEST_ASSEMBLY_MISSING:$name" }
    }
    $framework = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction SilentlyContinue
    if (-not $framework -or $framework.Release -lt 528040) { throw 'NET_FRAMEWORK_4_8_OR_NEWER_REQUIRED' }
    $assemblyCount = 0
    foreach ($entry in $entries) {
        if ([IO.Path]::GetFileName([string]$entry.name) -cne [string]$entry.name) { throw 'MANIFEST_NAME_MUST_BE_A_FILENAME' }
        $file = Join-Path $root $entry.name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "MISSING_FILE:$($entry.name)" }
        if (-not $entry.mutable -or $StrictPackage) {
            $algorithm = [Security.Cryptography.SHA256]::Create()
            $stream = [IO.File]::OpenRead($file)
            try { $hash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
            finally { $stream.Dispose(); $algorithm.Dispose() }
            if ($hash -cne $entry.sha256 -or (Get-Item -LiteralPath $file).Length -ne $entry.bytes) { throw "FILE_HASH_OR_SIZE_MISMATCH:$($entry.name)" }
        }
        if ($entry.assemblyFullName) {
            $identity = [Reflection.AssemblyName]::GetAssemblyName($file).FullName
            if ($identity -cne $entry.assemblyFullName) { throw "ASSEMBLY_IDENTITY_MISMATCH:$($entry.name)" }
            $assemblyCount++
        }
    }
    if ($assemblyCount -ne 10) { throw "EXPECTED_TEN_MANAGED_ASSEMBLIES:$assemblyCount" }
    foreach ($name in @('ustcori_crack.exe.config', 'WebLabClient.exe.config')) {
        $path = Join-Path $root $name
        $document = New-Object System.Xml.XmlDocument
        $document.XmlResolver = $null
        $document.LoadXml((Get-Content -LiteralPath $path -Raw -Encoding UTF8))
        $endpoints = @($document.SelectNodes('/configuration/system.serviceModel/client/endpoint'))
        if ($endpoints.Count -ne 3) { throw "EXPECTED_THREE_WCF_ENDPOINTS:$name" }
        foreach ($endpoint in $endpoints) {
            $uri = [Uri]::new([string]$endpoint.GetAttribute('address'), [UriKind]::Absolute)
            if ($uri.Scheme -notin @('http', 'https') -or -not $uri.IsWellFormedOriginalString() -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) { throw "INVALID_ENDPOINT_URI:$name" }
            if ($StrictPackage -and -not $uri.IsLoopback) { throw "PACKAGE_ENDPOINT_MUST_BE_LOOPBACK:$name" }
        }
        Write-Output "XML_URI_PASS:$name"
    }
    Write-Output "RUNTIME_CHECK_PASS assemblies=$assemblyCount frameworkRelease=$($framework.Release)"
    Write-Output 'This check loads assembly metadata only and sends no requests.'
    exit 0
} catch {
    Write-Output ('RUNTIME_CHECK_FAIL: ' + $_.Exception.Message)
    Write-Output 'Re-extract the complete runtime ZIP into a fresh directory; do not copy the EXE alone.'
    exit 1
}
