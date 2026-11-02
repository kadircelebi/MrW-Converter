<#
.SYNOPSIS
    Downloads the pinned full LGPL FFmpeg into third_party\ffmpeg\fixtures-win-<architecture>.

.DESCRIPTION
    The tests make their sample files with it, in formats the FFmpeg the app ships has no
    encoder for. It does not go into the app: that one comes from scripts\build-ffmpeg.ps1.
    The binaries are too large for the repository, so this runs once per checkout and again
    whenever the pinned version changes.

    The month-end build is pinned because BtbN/FFmpeg-Builds keeps those for two years
    and removes other daily builds after about 14 days.
#>
param(
    [ValidateSet('x64', 'arm64')]
    [string] $Architecture = 'x64'
)

$ErrorActionPreference = 'Stop'

$tag = 'autobuild-2026-09-30-13-08'
$builds = @{
    x64   = @{
        File   = 'ffmpeg-n9.0.2-17-g2a571b6068-win64-lgpl-shared-9.0.zip'
        Sha256 = '7157177b8a6cb2174c1650ba8c71b363f2c78cba5330f88c4c02cf5b2b880646'
    }
    arm64 = @{
        File   = 'ffmpeg-n9.0.2-17-g2a571b6068-winarm64-lgpl-shared-9.0.zip'
        Sha256 = 'f3133abf5c2b6ae45bd428d480cf830a9fbda3a3866dc249110e85f99f13b8ce'
    }
}

$build = $builds[$Architecture]
$url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$tag/$($build.File)"
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root "third_party\ffmpeg\fixtures-win-$Architecture"
$buildInfoPath = Join-Path $target 'BUILD-INFO.txt'

if ((Test-Path $buildInfoPath) -and ((Get-Content $buildInfoPath -Raw) -match [regex]::Escape($build.Sha256))) {
    Write-Host "FFmpeg is already set up in $target"
    return
}

$work = Join-Path ([IO.Path]::GetTempPath()) "mrwconverter-ffmpeg-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $work | Out-Null

try {
    $archive = Join-Path $work $build.File
    Write-Host "Downloading $url"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest $url -OutFile $archive -UseBasicParsing

    $actual = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $build.Sha256) { throw "Checksum mismatch for $($build.File): $actual" }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $extracted = Join-Path $work 'extracted'
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $extracted)
    $package = Get-ChildItem $extracted -Directory | Select-Object -First 1

    $staging = Join-Path $work 'staging'
    New-Item -ItemType Directory $staging | Out-Null
    Copy-Item (Join-Path $package.FullName 'bin\*') $staging -Exclude 'ffplay.exe'
    Copy-Item (Join-Path $package.FullName 'LICENSE.txt') (Join-Path $staging 'FFmpeg-LICENSE.txt')

    $hostArchitecture = if ([Environment]::Is64BitOperatingSystem -and $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
    $version = '(not checked: built for another architecture)'
    if ($hostArchitecture -eq $Architecture) {
        $version = (& (Join-Path $staging 'ffmpeg.exe') -hide_banner -version) -join "`n"
        if ($version -match '--enable-gpl|--enable-nonfree') {
            throw 'This FFmpeg build is not LGPL. Do not ship it.'
        }
    }

    @(
        "Source build: $url"
        "SHA-256: $($build.Sha256)"
        ''
        $version
    ) | Set-Content (Join-Path $staging 'BUILD-INFO.txt') -Encoding UTF8

    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory (Split-Path $target -Parent) -Force | Out-Null
    Move-Item $staging $target
    Write-Host "FFmpeg is ready in $target"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
