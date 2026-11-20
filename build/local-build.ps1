<#
  本机编译 ustcori_crack：用本机 Roslyn csc.dll + .NET Framework 4.8 引用程序集，
  生成 AnyCPU / net48 的 Windows EXE，并产出与远端一致的 build-manifest.json。
  用法：
    pwsh -File build/local-build.ps1
    pwsh -File build/local-build.ps1 -Configuration Release -OutDir bin/Release-local
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutDir = 'bin/Release-local'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot          # ...\ustcoriCrack
$projDir = Join-Path $root 'ustocri_crack'
$out = Join-Path $projDir $OutDir
$obj = Join-Path $projDir 'obj/local'

$csc = 'C:\Program Files\dotnet\sdk\11.0.100\Roslyn\bincore\csc.dll'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$refDir = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'

foreach ($p in @($csc, $dotnet, $refDir, (Join-Path $projDir 'ustcori_crack.csproj'))) {
    if (-not (Test-Path -LiteralPath $p)) { throw "缺少编译输入：$p" }
}

New-Item -ItemType Directory -Force -Path $out, $obj | Out-Null

# ---- 源文件与引用：直接读 csproj，避免手工维护两份清单 ----
[xml]$proj = Get-Content -LiteralPath (Join-Path $projDir 'ustcori_crack.csproj') -Raw
$ns = New-Object System.Xml.XmlNamespaceManager($proj.NameTable)
$ns.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')

$sources = @()
foreach ($n in $proj.SelectNodes('//m:ItemGroup/m:Compile', $ns)) {
    $sources += (Join-Path $projDir ($n.Include -replace '\\', '\'))
}
$libRefs = @()
foreach ($n in $proj.SelectNodes('//m:ItemGroup/m:Reference', $ns)) {
    $hint = $n.SelectSingleNode('m:HintPath', $ns)
    if ($hint) { $libRefs += (Join-Path $projDir ($hint.InnerText -replace '\\', '\')) }
}

$missing = @($sources + $libRefs | Where-Object { -not (Test-Path -LiteralPath $_) })
if ($missing.Count -gt 0) { throw "缺少输入文件：`n$($missing -join "`n")" }

function Test-ManagedAssembly([string]$path) {
    # 引用目录里混有 Thunk/Wrapper 这类原生 DLL，csc 只接受真正的托管程序集
    try { [void][System.Reflection.AssemblyName]::GetAssemblyName($path); return $true } catch { return $false }
}

$frameworkRefs = @(Get-ChildItem -LiteralPath $refDir -Filter *.dll | Sort-Object Name |
    Where-Object { Test-ManagedAssembly $_.FullName } | ForEach-Object { $_.FullName })

# 目标框架特性文件（csc 手工编译时必须自己提供）
$attrs = Join-Path $obj '.NETFramework,Version=v4.8.AssemblyAttributes.cs'
Set-Content -LiteralPath $attrs -Encoding utf8 -Value @'
[assembly: global::System.Runtime.Versioning.TargetFrameworkAttribute(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
'@

$exe = Join-Path $out 'ustcori_crack.exe'
$rsp = Join-Path $obj 'release.rsp'

$lines = @(
    '/nologo', '/noconfig', '/nostdlib+', '/target:exe', '/main:ustcori_crack.Program',
    '/platform:anycpu', '/langversion:7.3', '/codepage:65001', '/utf8output',
    '/optimize+', '/debug-', '/deterministic+', '/warn:4', '/filealign:512', '/define:TRACE',
    "/out:`"$exe`"", "/win32icon:`"$(Join-Path $projDir 'favicon.ico')`""
)
foreach ($r in $frameworkRefs) { $lines += "/reference:`"$r`"" }
foreach ($r in $libRefs) { $lines += "/reference:`"$r`"" }
foreach ($s in $sources) { $lines += "`"$s`"" }
$lines += "`"$attrs`""
Set-Content -LiteralPath $rsp -Encoding utf8 -Value ($lines -join "`r`n")

Push-Location $projDir
try {
    & $dotnet exec $csc "@$rsp"
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}
if ($code -ne 0) { throw "编译失败，csc 退出码 $code" }
if (-not (Test-Path -LiteralPath $exe)) { throw '编译器返回成功但没有产物' }

# ---- 运行期文件：配置 + 厂商 DLL ----
Copy-Item -LiteralPath (Join-Path $projDir 'app.config') -Destination (Join-Path $out 'ustcori_crack.exe.config') -Force
foreach ($f in Get-ChildItem -LiteralPath (Join-Path $projDir 'lib') -Include *.dll, *.exe, *.exe.config -File) {
    Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $out $f.Name) -Force
}
$webCfg = Join-Path $out 'WebLabClient.exe.config'
if (Test-Path -LiteralPath $webCfg) {
    Copy-Item -LiteralPath (Join-Path $projDir 'app.config') -Destination $webCfg -Force
}

$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    target_framework = '.NETFramework,Version=v4.8'
    configuration    = $Configuration
    compiler         = $csc
    compiler_version = (& $dotnet exec $csc -version 2>&1 | Select-Object -First 1)
    source_count     = $sources.Count
    'source_hashes'  = [ordered]@{}
    'output'         = [ordered]@{ name = 'ustcori_crack.exe'; bytes = (Get-Item -LiteralPath $exe).Length; sha256 = $hash }
}
foreach ($s in $sources) { $manifest.source_hashes[(Resolve-Path -LiteralPath $s -Relative -RelativeBasePath $root).Replace('\', '/')] = (Get-FileHash -LiteralPath $s -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $out 'build-manifest.local.json') -Encoding utf8

Write-Host "编译成功：$exe" -ForegroundColor Green
Write-Host ("  bytes  : {0}" -f $manifest.output.bytes)
Write-Host ("  sha256 : {0}" -f $hash)
Write-Host ("  输出目录: {0}" -f $out)
