param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [switch]$Zip
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\SnapCat.App\SnapCat.App.csproj"
$updaterProjectPath = Join-Path $repoRoot "src\SnapCat.Updater\SnapCat.Updater.csproj"
$outputRoot = Join-Path $repoRoot "artifacts\dev-test"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$projectXml = [xml](Get-Content -LiteralPath $projectPath)
$version = $projectXml.Project.PropertyGroup.Version | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version))
{
    $version = "0.4.3-preview"
}

$publishDir = Join-Path $outputRoot "SnapCat-v$version-dev-$timestamp"
$updaterPublishDir = Join-Path $outputRoot "_updater-$timestamp"
$currentDir = Join-Path $outputRoot "current"

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

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
    ($(if ($SelfContained) { "true" } else { "false" }))
)

Write-Host "[SnapCat] Publishing dev build..."
Write-Host "[SnapCat] Output: $publishDir"

& dotnet @publishArgs

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$updaterPublishArgs = @(
    "publish"
    $updaterProjectPath
    "-c"
    $Configuration
    "-r"
    $Runtime
    "--output"
    $updaterPublishDir
    "--self-contained"
    ($(if ($SelfContained) { "true" } else { "false" }))
)

& dotnet @updaterPublishArgs

if ($LASTEXITCODE -ne 0)
{
    throw "SnapCat.Updater publish failed with exit code $LASTEXITCODE."
}

Copy-Item -Path $updaterPublishDir -Destination (Join-Path $publishDir "Updater") -Recurse -Force
Remove-Item -LiteralPath $updaterPublishDir -Recurse -Force

if (Test-Path -LiteralPath $currentDir)
{
    Remove-Item -LiteralPath $currentDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $currentDir | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $currentDir -Recurse -Force

$versionFile = Join-Path $currentDir "VERSION.txt"
@(
    "SnapCat v$version"
    "source=$publishDir"
    "built_at=$timestamp"
) | Set-Content -LiteralPath $versionFile -Encoding UTF8

Write-Host "[SnapCat] Current: $currentDir"

$zipPath = "$publishDir.zip"
if ($Zip)
{
    if (Test-Path -LiteralPath $zipPath)
    {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
    Write-Host "[SnapCat] Zip: $zipPath"
}

Write-Host "[SnapCat] Done."
