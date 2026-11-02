<#
.SYNOPSIS
    Builds the FFmpeg the app ships into third_party\ffmpeg\win-x64, from pinned sources.

.DESCRIPTION
    LGPL 2.1, with only the codecs, formats and filters the app uses. The compiler comes from a
    copy of MSYS2 unpacked under artifacts\ffbuild, so nothing is installed on the machine and no
    administrator rights are needed. About 2 GB of disk and some minutes the first time.

    What is built is listed in scripts\build-ffmpeg.sh. artifacts\ffbuild\source-bundle ends up
    holding everything that has to be published for the LGPL.
#>
$ErrorActionPreference = 'Stop'

$msysUrl = 'https://github.com/msys2/msys2-installer/releases/download/2026-09-27/msys2-base-x86_64-20260927.tar.xz'
$msysSha256 = 'ea2f31a0b6ade63914ce441ffb022f0f6aa96982bfefa2326460a26d5fb01322'
$packages = @(
    'make', 'diffutils', 'tar', 'xz', 'gzip', 'perl', 'curl',
    'mingw-w64-ucrt-x86_64-gcc', 'mingw-w64-ucrt-x86_64-binutils', 'mingw-w64-ucrt-x86_64-nasm',
    'mingw-w64-ucrt-x86_64-pkgconf', 'mingw-w64-ucrt-x86_64-meson', 'mingw-w64-ucrt-x86_64-ninja'
)

$root = Split-Path $PSScriptRoot -Parent
$work = Join-Path $root 'artifacts\ffbuild'
$msys = Join-Path $work 'msys64'
$archive = Join-Path $work 'msys2-base.tar.xz'
$output = Join-Path $root 'third_party\ffmpeg\win-x64'
$bash = Join-Path $msys 'usr\bin\bash.exe'
New-Item -ItemType Directory $work -Force | Out-Null

function Invoke-Msys([string] $Command) {
    $env:MSYSTEM = 'UCRT64'
    $env:CHERE_INVOKING = '1'
    $env:MSYS2_PATH_TYPE = 'strict'
    & $bash -lc $Command
    if ($LASTEXITCODE -ne 0) { throw "This step failed with exit code $LASTEXITCODE`: $Command" }
}

if (-not (Test-Path $bash)) {
    if (-not (Test-Path $archive)) {
        Write-Host "Downloading $msysUrl"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest $msysUrl -OutFile $archive -UseBasicParsing
    }
    $actual = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $msysSha256) { throw "Checksum mismatch for MSYS2: $actual" }
    & "$env:SystemRoot\System32\tar.exe" -xf $archive -C $work
    if ($LASTEXITCODE -ne 0) { throw 'Could not unpack MSYS2.' }
}

Set-Location $root
Invoke-Msys 'pacman-key --init >/dev/null 2>&1; pacman-key --populate >/dev/null 2>&1; true'
Invoke-Msys "pacman --noconfirm --needed -S $($packages -join ' ')"

# The script's own output goes to a new folder, so a failed build leaves the old one in place.
$fresh = Join-Path $work 'output'
$script = (Join-Path $PSScriptRoot 'build-ffmpeg.sh') -replace '\\', '/'
Invoke-Msys "bash '$script' '$($work -replace '\\', '/')' '$($fresh -replace '\\', '/')'"

# The full download from setup-ffmpeg.ps1 stays on as the tests' tool for making sample files in
# formats this build can't write.
$fixtures = Join-Path $root 'third_party\ffmpeg\fixtures-win-x64'
if ((Test-Path $output) -and -not (Test-Path $fixtures) -and ((Get-Content (Join-Path $output 'BUILD-INFO.txt') -Raw) -match 'BtbN')) {
    Move-Item $output $fixtures
}
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory (Split-Path $output -Parent) -Force | Out-Null
Move-Item $fresh $output
Write-Host "FFmpeg is ready in $output"
