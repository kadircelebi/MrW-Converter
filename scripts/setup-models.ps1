<#
.SYNOPSIS
    Downloads the background removal model into third_party\models.

.DESCRIPTION
    The file is too large for the repository, so this runs once per checkout. It ships
    inside the app and the MSIX; users never download it.

    BEN2 Base is published as ONNX by its authors (Prama LLC) under the MIT license:
    https://huggingface.co/PramaLLC/BEN2 and https://github.com/PramaLLC/BEN2
    The URL is pinned to a commit, so the download can't change underneath us.
#>

$ErrorActionPreference = 'Stop'

$url = 'https://huggingface.co/PramaLLC/BEN2/resolve/e48a20765fb421d19dcdb0bf3cc61e802ca5ec8f/BEN2_Base.onnx'
$sha256 = '22cea62108ff53b7ccc20f7a008bf30494228d84b1687f29ecbe76936a998101'

$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root 'third_party\models'
$target = Join-Path $folder 'ben2-base.onnx'

if ((Test-Path $target) -and (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant() -eq $sha256) {
    Write-Host "The model is already set up in $target"
    return
}

New-Item -ItemType Directory $folder -Force | Out-Null
$partial = "$target.part"

try {
    Write-Host "Downloading $url"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest $url -OutFile $partial -UseBasicParsing

    $actual = (Get-FileHash $partial -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha256) { throw "Checksum mismatch: $actual" }

    Move-Item $partial $target -Force
    Write-Host "The model is ready in $target"
}
finally {
    Remove-Item $partial -ErrorAction SilentlyContinue
}
