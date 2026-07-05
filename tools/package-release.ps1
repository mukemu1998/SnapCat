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
    $version = "0.2.1"
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
    "v$version 是 SnapCat v0.2 系列的稳定整理版，重点保留 v0.2.0 之后的可见体验改进，并完成第一轮源码结构整理，适合作为后续深拆 API 配置与贴图逻辑前的回滚基线。"
    ""
    "## 重点更新"
    ""
    "- 用户配置持久化：快捷键、主题、API 配置、清理天数等用户设置默认写入本机用户目录，便于覆盖升级后继续保留。"
    "- 自由框选增强：保留智能预框选、颜色放大镜、右键退出、等待操作框数值调整和比例选择等交互。"
    "- 翻译浮窗稳定：支持本地/API 翻译切换、多 API 配置选择、语言方向提示、再次框选和译文高度自适应。"
    "- 贴图管理增强：支持缩放、翻转、阵列、隐藏、分组、重启恢复、托盘显示/隐藏和主菜单缩略图管理。"
    "- 主菜单整理：目录、关于、运行状态、截图管理、贴图管理、用户配置和快捷键设置进一步统一为深色主题风格。"
    "- 启动体验优化：保留启动 logo 加载页、动态图标和托盘菜单风格统一，降低主菜单慢加载的突兀感。"
    "- 源码结构整理：拆分主窗口、翻译浮窗、历史记录、快捷键、设置摘要、下拉框选择和系统路径打开等逻辑，降低后续改坏风险。"
    "- MVVM 渐进过渡：新增基础 ViewModel、命令封装和 API 配置编辑模型，为后续继续拆分主窗口状态机打基础。"
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
    "- 本地轻量翻译适合短文本和基础翻译，长文本或更高质量翻译建议配置 DeepSeek 等兼容 OpenAI 的 API。"
    "- 智能预框选仍属于早期增强能力，不同软件窗口的命中效果可能不同，后续会继续优化。"
    "- OCR 效果会受到截图清晰度、字体、背景和系统 OCR 能力影响。"
    "- 此版本适合作为继续深拆 API 配置编辑器和贴图窗口前的回滚基线。"
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
