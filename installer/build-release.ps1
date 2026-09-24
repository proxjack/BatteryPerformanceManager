<#
Builds the release zip: publishes the tray app and packs it with the helper,
the scripts and the installer into
dist\BatteryPerformanceManager-<version>-win-x64.zip.

The version comes from <Version> in TrayApp\TrayApp.csproj.

Usage (needs the .NET 8 SDK):
  .\installer\build-release.ps1
#>

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'TrayApp\TrayApp.csproj'

[xml]$csproj = Get-Content -Path $project -Raw
$version = @($csproj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) {
    throw "No <Version> found in $project."
}

Write-Host "Publishing the tray app ($version)..." -ForegroundColor Cyan
dotnet publish $project -c Release -r win-x64 --self-contained true -nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}
$exe = Join-Path $root 'TrayApp\bin\Release\net8.0-windows\win-x64\publish\BatteryPerformanceManager.exe'

$dist = Join-Path $root 'dist'
$staging = Join-Path $dist "BatteryPerformanceManager-$version"
$zip = Join-Path $dist "BatteryPerformanceManager-$version-win-x64.zip"
if (Test-Path $staging) {
    Remove-Item -Path $staging -Recurse -Force
}
New-Item -ItemType Directory -Path $staging -Force | Out-Null

Copy-Item -Path $exe -Destination $staging
foreach ($script in 'BatteryPerformanceHelper.ps1', 'setup-scheduled-tasks.ps1', 'Set-DellBatteryChargeProfile.ps1', 'Get-DellBatteryChargeState.ps1') {
    Copy-Item -Path (Join-Path $root $script) -Destination $staging
}
foreach ($file in 'Install.ps1', 'Install.cmd', 'Uninstall.ps1', 'Uninstall.cmd') {
    Copy-Item -Path (Join-Path $PSScriptRoot $file) -Destination $staging
}
Copy-Item -Path (Join-Path $root 'LICENSE') -Destination (Join-Path $staging 'LICENSE.txt')

# Plain-text files for end users get Windows line endings, so they read fine in Notepad
# and cmd.exe parses the .cmd files reliably whatever git's line-ending settings are.
$readme = (Get-Content -Path (Join-Path $PSScriptRoot 'README.txt') -Raw).Replace('{{VERSION}}', $version)
Set-Content -Path (Join-Path $staging 'README.txt') -Value $readme -Encoding ASCII -NoNewline
foreach ($file in 'README.txt', 'LICENSE.txt', 'Install.cmd', 'Uninstall.cmd') {
    $path = Join-Path $staging $file
    $text = (Get-Content -Path $path -Raw) -replace "`r?`n", "`r`n"
    Set-Content -Path $path -Value $text -Encoding ASCII -NoNewline
}

if (Test-Path $zip) {
    Remove-Item -Path $zip -Force
}
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip

Write-Host ""
Write-Host "Release zip: $zip" -ForegroundColor Green
Write-Host "SHA-256:     $((Get-FileHash -Path $zip -Algorithm SHA256).Hash)"
