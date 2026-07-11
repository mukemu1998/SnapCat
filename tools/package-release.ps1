param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [ValidateSet("portable", "runtime-dependent")]
    [string]$PackageKind = "portable",
    [switch]$Zip = $true,
    [switch]$AllowOverwrite
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\SnapCat.App\SnapCat.App.csproj"
$updaterProjectPath = Join-Path $repoRoot "src\SnapCat.Updater\SnapCat.Updater.csproj"
$projectXml = [xml](Get-Content -LiteralPath $projectPath)
$version = $projectXml.Project.PropertyGroup.Version | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version))
{
    $version = "0.4.4-preview"
}

$releaseRoot = Join-Path $repoRoot "artifacts\releases\v$version"
$selfContained = $PackageKind -eq "portable"
$packageKind = $PackageKind
$packageName = "SnapCat-v$version-$Runtime-$packageKind"
$publishDir = Join-Path $releaseRoot $packageName
$updaterStagingDir = Join-Path $releaseRoot "_updater-$PID"
$zipPath = Join-Path $releaseRoot "$packageName.zip"
$shaPath = "$zipPath.sha256"
$versionFile = Join-Path $publishDir "VERSION.txt"
$releaseNotesPath = Join-Path $releaseRoot "release-notes-v$version.zh-CN.md"

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

$existingOutputs = @()
if (Test-Path -LiteralPath $publishDir)
{
    $existingOutputs += $publishDir
}

if (Test-Path -LiteralPath $zipPath)
{
    $existingOutputs += $zipPath
}

if (Test-Path -LiteralPath $shaPath)
{
    $existingOutputs += $shaPath
}

if ($existingOutputs.Count -gt 0 -and -not $AllowOverwrite)
{
    Write-Host "[SnapCat] Found existing release outputs:" -ForegroundColor Yellow
    $existingOutputs | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    throw "Release output already exists for v$version. Change the version first, or rerun with -AllowOverwrite."
}

if (Test-Path -LiteralPath $publishDir)
{
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath)
{
    Remove-Item -LiteralPath $zipPath -Force
}

if (Test-Path -LiteralPath $shaPath)
{
    Remove-Item -LiteralPath $shaPath -Force
}

$publishArgs = @(
    "publish"
    $projectPath
    "-c"
    $Configuration
    "-r"
    $Runtime
    "--output"
    $publishDir
    "--self-contained"
    ($(if ($selfContained) { "true" } else { "false" }))
    "/p:DebugType=None"
    "/p:DebugSymbols=false"
)

Write-Host "[SnapCat] Publishing release package..."
Write-Host "[SnapCat] Output: $publishDir"

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if (Test-Path -LiteralPath $updaterStagingDir)
{
    Remove-Item -LiteralPath $updaterStagingDir -Recurse -Force
}

$updaterPublishArgs = @(
    "publish"
    $updaterProjectPath
    "-c"
    $Configuration
    "-r"
    $Runtime
    "--output"
    $updaterStagingDir
    "--self-contained"
    ($(if ($selfContained) { "true" } else { "false" }))
    "/p:DebugType=None"
    "/p:DebugSymbols=false"
)

& dotnet @updaterPublishArgs
if ($LASTEXITCODE -ne 0)
{
    throw "SnapCat.Updater publish failed with exit code $LASTEXITCODE."
}

Copy-Item -Path $updaterStagingDir -Destination (Join-Path $publishDir "Updater") -Recurse -Force
Remove-Item -LiteralPath $updaterStagingDir -Recurse -Force

Get-ChildItem -LiteralPath $publishDir -Filter "*.pdb" -File -ErrorAction SilentlyContinue | Remove-Item -Force

Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $publishDir "README.md") -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $publishDir "LICENSE") -Force

@(
    "SnapCat v$version"
    "package_type=$packageKind"
    "runtime=$Runtime"
    "configuration=$Configuration"
    "self_contained=$selfContained"
    "built_at=$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
) | Set-Content -LiteralPath $versionFile -Encoding UTF8

@(
    "# SnapCat v$version 发布说明"
    ""
    "## 更新内容"
    ""
    "- 修复自动升级下载完成后临时包文件仍被占用的问题，更新助手现在可正常接手后续校验、解压与替换。"
    "- 增加下载、SHA256 校验和安全解压的回归测试，避免同类文件句柄问题再次影响自动升级。"
    "- OCR、翻译、贴图、画布标注和图片提示词分析保持原有操作方式。"
    "- 用户主题、快捷键、API Key、视觉模型配置和历史策略继续仅保存在用户本地目录。"
    ""
    "## 使用建议"
    ""
    "- 普通用户建议下载 portable 包，解压后直接运行 SnapCat.exe。"
    "- 如果系统已经安装 .NET 8 Desktop Runtime，可以下载 runtime-dependent 包获得更小体积。"
    "- API Key 等敏感配置只保存在本机用户配置中，不会写入源码目录。"
    ""
    "## 下载建议"
    ""
    "- portable：解压后可直接双击 SnapCat.exe 使用，适合普通用户。"
    "- runtime-dependent：体积更小，但需要系统已安装 .NET 8 Desktop Runtime。"
    ""
    "## 校验"
    ""
    "发布目录内提供 .sha256 文件，可用于校验压缩包完整性。"
) | Set-Content -LiteralPath $releaseNotesPath -Encoding UTF8

if ($Zip)
{
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
    $sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$sha256 *$(Split-Path -Leaf $zipPath)" | Set-Content -LiteralPath $shaPath -Encoding ascii
}

Write-Host "[SnapCat] Release directory: $releaseRoot"
if ($Zip)
{
    Write-Host "[SnapCat] Zip: $zipPath"
    Write-Host "[SnapCat] SHA256: $shaPath"
}
Write-Host "[SnapCat] Release notes: $releaseNotesPath"
Write-Host "[SnapCat] Done."
