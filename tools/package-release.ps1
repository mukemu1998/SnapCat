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
$projectXml = [xml](Get-Content -LiteralPath $projectPath)
$version = $projectXml.Project.PropertyGroup.Version | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version))
{
    $version = "0.4.0-preview"
}

$releaseRoot = Join-Path $repoRoot "artifacts\releases\v$version"
$selfContained = $PackageKind -eq "portable"
$packageKind = $PackageKind
$packageName = "SnapCat-v$version-$Runtime-$packageKind"
$publishDir = Join-Path $releaseRoot $packageName
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
    "v$version 是 SnapCat 的图片提示词分析预览版：在既有截图、OCR、翻译、贴图与画布能力之上，开放本地和云端视觉模型的第一条可用分析链路。"
    ""
    "## 重点更新"
    ""
    "- 图片提示词分析：新增框选图片分析命令、等待菜单入口和独立快捷键，可将选区交给视觉模型整理为可编辑提示词。"
    "- 本地与云端视觉模型：支持 Ollama 本地视觉模型和 OpenAI 兼容视觉接口；配置卡片支持默认选择、连接检测和模型切换。"
    "- 双语分析结果：输出完整中文分析与完整英文分析，包含主体、外观、构图、光影、风格、正向提示词和负面提示词。"
    "- 浮窗交互：分析浮窗贴近选区显示，支持复制、临时切换分析配置和针对当前截图重新分析。"
    "- 设置可靠性：修复视觉配置、API 配置和快捷键等用户设置的保存恢复链路；保存按钮仅在实际修改后显示。"
    "- 翻译 API 整理：翻译 API 采用卡片选择和折叠编辑架，API Key 继续默认隐藏，并保持本地加密存储。"
    "- 用户配置隔离：模型地址、密钥、快捷键、主题和历史策略仅保存在用户目录，不写入源码或发布包。"
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
