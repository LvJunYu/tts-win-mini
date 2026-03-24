param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [switch]$FrameworkDependent,
    [switch]$Zip
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repoRoot "src\Stt.App\Stt.App.csproj"
$publishRoot = Join-Path $repoRoot "artifacts\publish\$Runtime"
$selfContainedValue = "true"

if ($SelfContained.IsPresent -and $FrameworkDependent.IsPresent) {
    throw "Specify either -SelfContained or -FrameworkDependent, not both."
}

if ($FrameworkDependent.IsPresent) {
    $selfContainedValue = "false"
}

if (Test-Path $publishRoot) {
    Remove-Item $publishRoot -Recurse -Force
}

$publishArgs = @(
    "publish",
    $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedValue,
    "-o", $publishRoot,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($Zip) {
    $zipPath = Join-Path $repoRoot "artifacts\jotmic-$Runtime.zip"
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $zipPath
    Write-Host "Created archive: $zipPath"
}

Write-Host "Published app to: $publishRoot"
