<#
.SYNOPSIS
  Builds the mod and stages everything needed to install it or upload it to the Steam Workshop.

.EXAMPLE
  ./scripts/package.ps1
  ./scripts/package.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Slay the Spire 2" -Install

  Outputs:
    dist/TextSizeSetting/                 the mod folder (manifest + DLL)
    dist/TextSizeSetting-v<version>.zip   install-ready archive (contains mods/TextSizeSetting/...)
    workshop/content/                     what the Steam Workshop uploader will upload
  -Install also copies the mod into <GameDir>/mods/TextSizeSetting for local testing.
#>
param(
    [string]$GameDir = "",
    [switch]$Install
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$modId = "TextSizeSetting"
$manifestPath = Join-Path $root "mod/$modId.json"
$version = (Get-Content $manifestPath -Raw | ConvertFrom-Json).version

if (-not $GameDir) {
    $default = "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2"
    if (Test-Path $default) { $GameDir = $default }
}

$buildArgs = @("build", (Join-Path $root "src/TextSize/TextSize.csproj"), "-c", "Release", "-nologo", "-p:Version=$version")
if ($GameDir) { $buildArgs += "-p:GameDir=$GameDir" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$dll = Join-Path $root "src/TextSize/bin/Release/net9.0/$modId.dll"
if (-not (Test-Path $dll)) { throw "Build output not found: $dll" }

# Make the DLL accept any version of the game's assemblies (see ci/RetargetReferences).
& dotnet run --project (Join-Path $root "ci/RetargetReferences/RetargetReferences.csproj") -c Release -- $dll
if ($LASTEXITCODE -ne 0) { throw "Retargeting references failed." }

# dist/TextSizeSetting
$dist = Join-Path $root "dist"
$stage = Join-Path $dist $modId
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item $manifestPath $stage
Copy-Item $dll $stage
Copy-Item (Join-Path $root "src/TextSize/Fonts/OFL.txt") (Join-Path $stage "AtkinsonHyperlegible-OFL.txt")

# Install-ready zip: mods/TextSizeSetting/...
$zipRoot = Join-Path $dist "zip"
if (Test-Path $zipRoot) { Remove-Item $zipRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $zipRoot "mods") | Out-Null
Copy-Item $stage (Join-Path $zipRoot "mods") -Recurse
$zip = Join-Path $dist "$modId-v$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $zipRoot "mods") -DestinationPath $zip
Remove-Item $zipRoot -Recurse -Force

# workshop/content (uploaded as-is by ModUploader)
$content = Join-Path $root "workshop/content"
if (Test-Path $content) { Remove-Item $content -Recurse -Force }
New-Item -ItemType Directory -Force -Path $content | Out-Null
Copy-Item (Join-Path $stage "*") $content

Write-Host ""
Write-Host "Staged $modId v$version"
Write-Host "  Mod folder:        $stage"
Write-Host "  Zip:               $zip"
Write-Host "  Workshop content:  $content"

if ($Install) {
    if (-not $GameDir) { throw "Pass -GameDir to use -Install." }
    $target = Join-Path $GameDir "mods/$modId"
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $stage "*") $target
    Write-Host "  Installed to:      $target"
}
