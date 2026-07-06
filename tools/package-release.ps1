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
    $version = "0.3.0-preview"
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
    "v$version 是 SnapCat v0.3 预览阶段版，重点补齐框选预识别、翻译浮窗、多语言朗读、等待操作菜单和贴图交互的体验稳定性，适合作为下一轮更大功能开发前的回滚基线。"
    ""
    "## 重点更新"
    ""
    "- 框选预识别增强：框选层改为单一自绘层，并引入类似 Snow Shot 的后台候选缓存，让窗口、控件、文本和卡片区域更容易被预选。"
    "- 放大镜体验优化：颜色放大镜改为基于屏幕快照裁切和坐标变换跟随，减少鼠标移动时的延迟和闪烁。"
    "- 框选模式稳定：禁止重复叠加进入框选模式，支持右键单击退出且不把右键事件传递到下层窗口。"
    "- 等待操作菜单优化：整体拖动时隐藏菜单，停止后重新显示，并对菜单和线框进行设备像素对齐，降低文字虚焦感。"
    "- 翻译浮窗稳定：首次打开读取全局默认翻译来源，浮窗会话内可自由切换本地/API 与 API 配置，再次框选沿用当前选择。"
    "- 多语言与朗读：翻译语言统一支持中文、英语、日语、韩语、越南语、法语、德语和俄语，原文/译文区域增加语音朗读。"
    "- 文本操作增强：原文框增加清空按钮，原文和译文复制、朗读按钮位置进一步整理。"
    "- 贴图管理增强：支持缩放、翻转、阵列、隐藏、分组、重启恢复、托盘显示/隐藏和主菜单缩略图管理。"
    "- 主菜单整理：目录图标、关于页、运行状态、截图管理、贴图管理、用户配置和快捷键设置进一步统一为主题风格。"
    "- 启动体验优化：保留启动 logo 加载页、动态图标和托盘菜单风格统一，降低主菜单慢加载的突兀感。"
    "- 架构规范延续：继续遵循截图/框选引擎、贴图引擎、OCR/翻译管线、设置与用户数据四条开发主线。"
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
    "- 本地轻量翻译适合短文本和基础翻译，长文本、上下文或更高质量翻译建议配置 DeepSeek 等兼容 OpenAI 的 API。"
    "- 智能预框选仍属于预览增强能力，不同软件窗口的 UIA 暴露程度不同，命中效果可能不完全一致。"
    "- OCR 效果会受到截图清晰度、字体、背景和系统 OCR 能力影响。"
    "- 此版本适合作为继续开发全屏画布、标注工具和更复杂贴图编辑前的阶段基线。"
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
