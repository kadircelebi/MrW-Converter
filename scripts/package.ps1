<#
.SYNOPSIS
    Publishes the app and builds an MSIX for Microsoft Store submission.

.DESCRIPTION
    Writes artifacts\package\<architecture>\layout and MrWConverter_<version>_<architecture>.msix.
    The MSIX is unsigned: the Store signs it after certification. To try the app with
    package identity on a PC in Developer Mode, register the layout folder:

        Add-AppxPackage -Register artifacts\package\x64\layout\AppxManifest.xml
#>
param(
    [ValidateSet('x64', 'arm64')]
    [string] $Architecture = 'x64'
)

$ErrorActionPreference = 'Stop'

function Find-SdkTool([string] $Name) {
    $tool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$Name" -ErrorAction SilentlyContinue |
        Sort-Object { [version] $_.Directory.Parent.Name } |
        Select-Object -Last 1
    if (-not $tool) { throw "$Name was not found. Install the Windows SDK." }
    return $tool.FullName
}

function Assert-Success([string] $Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed with exit code $LASTEXITCODE." }
}

$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root "artifacts\package\$Architecture"
$layout = Join-Path $output 'layout'
$makeAppx = Find-SdkTool 'makeappx.exe'
$makePri = Find-SdkTool 'makepri.exe'

# The Store requires the fourth version part to be 0.
$version = ([xml] (Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$packageVersion = "$version.0"

$ffmpeg = Join-Path $root "third_party\ffmpeg\win-$Architecture\ffmpeg.exe"
if (-not (Test-Path $ffmpeg)) {
    throw "FFmpeg is missing. Run: scripts\build-ffmpeg.ps1"
}

if (-not (Test-Path (Join-Path $root 'third_party\models\ben2-base.onnx'))) {
    throw "The model is missing. Run: scripts\setup-models.ps1"
}

if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory $layout | Out-Null

# .NET has no Store framework package, so the runtime ships inside the MSIX.
dotnet publish (Join-Path $root 'src\MrWConverter.App') `
    --configuration Release `
    --runtime "win-$Architecture" `
    --self-contained true `
    --output $layout `
    -p:PublishReadyToRun=true `
    -p:DebugType=none
Assert-Success 'dotnet publish'

Copy-Item (Join-Path $root 'packaging\Assets') (Join-Path $layout 'Assets') -Recurse

$manifest = [xml] (Get-Content (Join-Path $root 'packaging\AppxManifest.xml') -Encoding UTF8)
$manifest.Package.Identity.Version = $packageVersion
$manifest.Package.Identity.ProcessorArchitecture = $Architecture
$manifest.Save((Join-Path $layout 'AppxManifest.xml'))

$priConfig = Join-Path $output 'priconfig.xml'
& $makePri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o
Assert-Success 'makepri createconfig'

# The default config splits scales and languages into files meant for separate resource
# packages. This is a single package, so everything has to stay in resources.pri.
$config = [xml] (Get-Content $priConfig -Encoding UTF8)
[void] $config.resources.RemoveChild($config.resources.packaging)
$config.Save($priConfig)

& $makePri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o
Assert-Success 'makepri new'

$package = Join-Path $output "MrWConverter_${version}_$Architecture.msix"
& $makeAppx pack /d $layout /p $package /o
Assert-Success 'makeappx pack'

Write-Host "Created $package"
