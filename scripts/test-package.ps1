<#
.SYNOPSIS
    Installs a built .msix side by side under a test identity, or removes that copy.

.DESCRIPTION
    Never registers the real identity, which would replace a Store copy on this machine. The package is
    unpacked to artifacts\local-layout\<version>-test, its identity gets ".Test" and its name " Test",
    and the folder is registered. Needs Developer Mode, not administrator rights.
#>
param(
    [string] $Package,
    [switch] $Remove
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$testName = 'MrWProject.MrWConverter.Test'

if ($Remove) {
    Get-AppxPackage $testName | Remove-AppxPackage
    Write-Host "Removed $testName"
    return
}

$version = ([xml] (Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
if (-not $Package) { $Package = Join-Path $root "dist\store\packages\MrWConverter_${version}_x64.msix" }
if (-not (Test-Path $Package)) { throw "$Package is missing. Run scripts\build-store-release.ps1." }

$layout = Join-Path $root "artifacts\local-layout\$version-test"
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory $layout | Out-Null
& "$env:SystemRoot\System32\tar.exe" -xf $Package -C $layout
if ($LASTEXITCODE -ne 0) { throw 'Could not unpack the package.' }
foreach ($file in 'AppxSignature.p7x', 'AppxBlockMap.xml', '[Content_Types].xml') {
    Remove-Item -LiteralPath (Join-Path $layout $file) -ErrorAction SilentlyContinue
}

$path = Join-Path $layout 'AppxManifest.xml'
$manifest = [xml] (Get-Content $path -Encoding UTF8)
$manifest.Package.Identity.Name = $testName
$manifest.Package.Properties.DisplayName = 'MrW Converter Test'
$manifest.Package.Applications.Application.VisualElements.DisplayName = 'MrW Converter Test'
$manifest.Save($path)

Add-AppxPackage -Register $path
$installed = Get-AppxPackage $testName
Write-Host "Installed $($installed.PackageFullName)"
Write-Host "Family name $($installed.PackageFamilyName), app id MrWConverter, folder $layout"
Write-Host 'When done: scripts\test-package.ps1 -Remove'
