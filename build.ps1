[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path $PSScriptRoot).Path
$WorkspaceRoot = (Resolve-Path (Join-Path $ProjectRoot '..\..')).Path
$Project = Join-Path $ProjectRoot 'src\GuaiMiao\GuaiMiao.csproj'
$OutputDir = Join-Path $WorkspaceRoot 'outputs\guai-miao-desktop'
$QaDir = Join-Path $OutputDir 'qa'
$PublishDir = Join-Path $ProjectRoot 'artifacts\publish'
$StageDir = Join-Path $ProjectRoot 'artifacts\source-package'
$PortableDotnet = Join-Path $WorkspaceRoot 'work\.tooling\dotnet-8.0.423\dotnet.exe'
$LocalFeed = Join-Path $WorkspaceRoot 'work\.tooling\local-feed'
$LocalNugetPackages = Join-Path $WorkspaceRoot 'work\.tooling\nuget-packages'
$NugetPackages = if (Test-Path -LiteralPath $LocalNugetPackages) {
    $LocalNugetPackages
} else {
    Join-Path $ProjectRoot 'artifacts\nuget-packages'
}
$DotnetCliHome = Join-Path $ProjectRoot 'artifacts\dotnet-cli-home'

New-Item -ItemType Directory -Force $DotnetCliHome, $NugetPackages | Out-Null
$env:DOTNET_CLI_HOME = $DotnetCliHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = $NugetPackages

$Dotnet = if (Test-Path -LiteralPath $PortableDotnet) {
    $PortableDotnet
} else {
    (Get-Command dotnet -ErrorAction SilentlyContinue).Source
}
if (-not $Dotnet -or -not (& $Dotnet --list-sdks)) {
    throw '.NET 8 SDK 未安装；系统 dotnet 可能只有运行时。'
}
New-Item -ItemType Directory -Force $OutputDir, $QaDir | Out-Null
$PublishArguments = @(
    'publish', $Project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:PublishTrimmed=false',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:NuGetAudit=false',
    '-o', $PublishDir
)
if (Test-Path -LiteralPath $LocalFeed) {
    $PublishArguments += @('--source', $LocalFeed)
}
& $Dotnet @PublishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码：$LASTEXITCODE"
}

$PublishedExe = Join-Path $PublishDir '乖喵.exe'
if (-not (Test-Path -LiteralPath $PublishedExe)) {
    throw '发布未生成乖喵.exe。'
}

$ReleaseExe = Join-Path $OutputDir '乖喵.exe'
Copy-Item -LiteralPath $PublishedExe -Destination $ReleaseExe -Force

$SelfTestReport = Join-Path $QaDir 'self-test.json'
$test = Start-Process -FilePath $ReleaseExe -ArgumentList @('--self-test', $SelfTestReport) -Wait -PassThru
if ($test.ExitCode -ne 0) {
    throw "内置自测失败，退出码：$($test.ExitCode)"
}

$signature = Get-AuthenticodeSignature -LiteralPath $ReleaseExe
$signatureReport = [ordered]@{
    expected = 'NotSigned'
    actual = $signature.Status.ToString()
    ok = $signature.Status -eq [System.Management.Automation.SignatureStatus]::NotSigned
}
$signatureReport | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $QaDir 'signature-status.json') -Encoding utf8
if (-not $signatureReport.ok) {
    throw "签名状态不符合未签名要求：$($signature.Status)"
}

$hash = Get-FileHash -LiteralPath $ReleaseExe -Algorithm SHA256
"$($hash.Hash.ToLowerInvariant())  乖喵.exe" | Set-Content -LiteralPath (Join-Path $OutputDir '乖喵.exe.sha256') -Encoding utf8NoBOM

Copy-Item -LiteralPath (Join-Path $ProjectRoot 'README.md') -Destination (Join-Path $OutputDir 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'LICENSE.txt') -Destination (Join-Path $OutputDir 'LICENSE.txt') -Force

$projectPrefix = $ProjectRoot.TrimEnd('\') + '\'
$stageFull = [IO.Path]::GetFullPath($StageDir)
if (-not $stageFull.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw '拒绝清理项目目录之外的源码暂存目录。'
}
if (Test-Path -LiteralPath $stageFull) {
    Remove-Item -LiteralPath $stageFull -Recurse -Force
}
New-Item -ItemType Directory -Force $stageFull | Out-Null
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'src') -Destination $stageFull -Recurse
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'tools') -Destination $stageFull -Recurse
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'README.md') -Destination $stageFull
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'LICENSE.txt') -Destination $stageFull
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'build.ps1') -Destination $stageFull
Get-ChildItem -LiteralPath $stageFull -Recurse -Directory | Where-Object Name -in @('bin', 'obj') |
    Sort-Object FullName -Descending | Remove-Item -Recurse -Force

$SourceZip = Join-Path $OutputDir '乖喵-source.zip'
if (Test-Path -LiteralPath $SourceZip) {
    Remove-Item -LiteralPath $SourceZip -Force
}
Compress-Archive -Path (Join-Path $stageFull '*') -DestinationPath $SourceZip -CompressionLevel Optimal

$RegressionSource = Join-Path $ProjectRoot 'qa\feature-regression-1.2.0.json'
if (Test-Path -LiteralPath $RegressionSource) {
    Copy-Item -LiteralPath $RegressionSource -Destination (Join-Path $QaDir 'feature-regression-1.2.0.json') -Force
}

$buildReport = [ordered]@{
    ok = $true
    product = '乖喵'
    version = (Get-Item -LiteralPath $ReleaseExe).VersionInfo.FileVersion
    runtime = 'win-x64 self-contained single-file'
    targetFramework = 'net8.0-windows10.0.19041.0'
    supportedWindows = 'Windows 10/11 x64'
    offlineAtRuntime = $false
    networkPolicy = 'User-initiated GitHub update checks only'
    signed = $false
    sha256 = $hash.Hash.ToLowerInvariant()
    builtAt = [DateTimeOffset]::Now.ToString('O')
}
$buildReport | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $QaDir 'build-report.json') -Encoding utf8
Write-Host "完成：$ReleaseExe"
