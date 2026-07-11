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
    $version = "0.4.1-preview"
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
    "## 版本定位"
    ""
    "v$version 是 SnapCat 的自动更新与可持续升级预览版：在既有截图、OCR、翻译、贴图、画布和图片提示词分析能力之上，补齐发布后自检、下载、校验和替换升级链路。"
    ""
    "## 重点更新"
    ""
    "- 自动检查更新：关于页新增启动后自动检查更新开关；发现新版本时会询问是否下载并升级。"
    "- 手动检查更新：关于页的检查更新按钮会读取 GitHub Releases，匹配当前包类型并显示可用版本。"
    "- 自动升级流程：下载对应 zip 后执行 SHA256 校验、安全解压、独立更新助手替换文件并重启 SnapCat；失败时尽量回滚。"
    "- 主菜单捕获优化：当主菜单正在桌面显示时，按截图快捷键不会再自动隐藏主菜单，方便直接框选菜单内容。"
    "- 用户配置保护：主题、快捷键、API Key、视觉模型配置、历史策略等仍保存在用户目录，覆盖程序文件不会清空个人配置。"
    "- 配置保存整理：视觉配置、翻译 API、自动更新开关等设置继续走本地用户配置保存链路。"
    "- 发布包整理：portable 和 runtime-dependent 包都会携带 Updater 更新助手、VERSION 文件、README、LICENSE 和 SHA256 校验文件。"
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
    "## 已知说明"
    ""
    "- 旧版本如果没有内置 Updater 更新助手，需要先手动替换到此版本或更新版本，之后才能使用自动覆盖升级。"
    "- Windows 高质量文本提取依赖系统文本提取能力和快捷键环境；本地视觉模型用于图片理解与提示词分析，并不替代默认文本提取流程。"
    "- 原生托盘提示受 Windows 长度限制，SnapCat 会按整行展示，放不下的摘要会自动省略。"
    "- 画布标注仍处于预览增强阶段，复杂文本框编辑和局部标注细节后续还会继续打磨。"
    "- 本地轻量翻译适合短文本和基础翻译，长文本、上下文或更高质量翻译建议配置 DeepSeek 等兼容 OpenAI 的 API。"
    "- 此版本是后续本地 AI 增强识别、外部生图服务与 AI 画布能力的稳定预览基线。"
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
