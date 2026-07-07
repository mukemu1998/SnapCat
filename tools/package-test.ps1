param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Label = "manual-test",
    [switch]$SelfContained = $true,
    [switch]$Zip = $true
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\SnapCat.App\SnapCat.App.csproj"
$projectXml = [xml](Get-Content -LiteralPath $projectPath)
$version = $projectXml.Project.PropertyGroup.Version | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version))
{
    $version = "0.3.1-preview"
}

$safeLabel = ($Label -replace "[^0-9A-Za-z\-_]+", "-").Trim("-")
if ([string]::IsNullOrWhiteSpace($safeLabel))
{
    $safeLabel = "manual-test"
}

$buildRoot = Join-Path $repoRoot "artifacts\test-builds\current"
$packageName = "SnapCat-test-$Runtime-portable"
$publishDir = Join-Path $buildRoot $packageName
$stagingRoot = Join-Path $buildRoot "_staging-$PID"
$stagingPublishDir = Join-Path $stagingRoot $packageName
$zipPath = Join-Path $buildRoot "$packageName.zip"
$shaPath = "$zipPath.sha256"
$versionFile = Join-Path $publishDir "VERSION.txt"

if (Test-Path -LiteralPath $buildRoot)
{
    $resolvedRepoRoot = [System.IO.Path]::GetFullPath($repoRoot)
    $resolvedBuildRoot = [System.IO.Path]::GetFullPath($buildRoot)
    if (-not $resolvedBuildRoot.StartsWith($resolvedRepoRoot, [System.StringComparison]::OrdinalIgnoreCase))
    {
        throw "Refuse to clean test build directory outside repository: $resolvedBuildRoot"
    }
}

New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
if (Test-Path -LiteralPath $stagingRoot)
{
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stagingPublishDir | Out-Null

$publishArgs = @(
    "publish"
    $projectPath
    "-c"
    $Configuration
    "-r"
    $Runtime
    "--output"
    $stagingPublishDir
    "--self-contained"
    ($(if ($SelfContained) { "true" } else { "false" }))
    "/p:DebugType=None"
    "/p:DebugSymbols=false"
)

Write-Host "[SnapCat] Publishing test package..."
Write-Host "[SnapCat] Output: $publishDir"

try
{
    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    Get-ChildItem -LiteralPath $stagingPublishDir -Filter "*.pdb" -File -ErrorAction SilentlyContinue | Remove-Item -Force

    Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination (Join-Path $stagingPublishDir "README.md") -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $stagingPublishDir "LICENSE") -Force

    @(
        "SnapCat v$version"
        "package_type=test"
        "runtime=$Runtime"
        "configuration=$Configuration"
        "self_contained=$($SelfContained.IsPresent)"
        "label=$safeLabel"
        "built_at=$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    ) | Set-Content -LiteralPath (Join-Path $stagingPublishDir "VERSION.txt") -Encoding UTF8

    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
    & robocopy $stagingPublishDir $publishDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7)
    {
        throw "robocopy failed with exit code $LASTEXITCODE."
    }

    if ($Zip)
    {
        Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
        $sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        "$sha256 *$(Split-Path -Leaf $zipPath)" | Set-Content -LiteralPath $shaPath -Encoding ascii
    }
}
finally
{
    if (Test-Path -LiteralPath $stagingRoot)
    {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "[SnapCat] Test directory: $buildRoot"
if ($Zip)
{
    Write-Host "[SnapCat] Zip: $zipPath"
    Write-Host "[SnapCat] SHA256: $shaPath"
}
Write-Host "[SnapCat] Done."
