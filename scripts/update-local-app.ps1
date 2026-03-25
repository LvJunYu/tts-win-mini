param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repoRoot "src\Stt.App\Stt.App.csproj"
$localAppDir = Join-Path $repoRoot "artifacts\local-app\$Runtime"
$installedAppDir = Join-Path $env:LOCALAPPDATA "Programs\whisper"
$settingsPath = Join-Path $env:LOCALAPPDATA "whisper\whisper.settings.json"
$shortcutPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\whisper.lnk"
$runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$runValueName = "whisper"
$exePath = Join-Path $localAppDir "whisper.exe"

Get-Process whisper -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

if (Test-Path $localAppDir) {
    Remove-Item $localAppDir -Recurse -Force
}

New-Item -ItemType Directory -Path $localAppDir -Force | Out-Null

$publishArgs = @(
    "publish",
    $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-o", $localAppDir,
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

$shortcutDirectory = Split-Path $shortcutPath -Parent
New-Item -ItemType Directory -Path $shortcutDirectory -Force | Out-Null

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $localAppDir
$shortcut.IconLocation = $exePath
$shortcut.Description = "whisper"
$shortcut.Save()

New-Item -Path $runKeyPath -Force | Out-Null
$launchOnWindowsLogin = $true

if (Test-Path $settingsPath) {
    try {
        $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
        if ($null -ne $settings.LaunchOnWindowsLogin) {
            $launchOnWindowsLogin = [bool]$settings.LaunchOnWindowsLogin
        }
    }
    catch {
        Write-Warning "Couldn't read launch-on-login setting from $settingsPath. Keeping startup enabled."
    }
}

if ($launchOnWindowsLogin) {
    Set-ItemProperty -Path $runKeyPath -Name $runValueName -Value ('"{0}"' -f $exePath)
}
else {
    Remove-ItemProperty -Path $runKeyPath -Name $runValueName -ErrorAction SilentlyContinue
}

if (Test-Path $installedAppDir) {
    Remove-Item $installedAppDir -Recurse -Force
}

Write-Host "Published local app to: $localAppDir"
Write-Host "Updated Start Menu shortcut: $shortcutPath"
Write-Host "Removed installed app directory: $installedAppDir"

if (-not $NoLaunch) {
    $process = Start-Process -FilePath $exePath -WorkingDirectory $localAppDir -PassThru
    Write-Host "Launched local app. PID: $($process.Id)"
}
