<#
.SYNOPSIS
    Prepares everything for a Microsoft Store submission in dist\store.

.DESCRIPTION
    The version comes from Directory.Build.props. If dist\store holds another version it moves to
    dist\old_versions\<that version> first; rebuilding the same version replaces it. Then it builds the
    unsigned MSIX, copies the texts, checks the Partner Center limits and makes the images.

    Taking screenshots closes the running copy of the app. Pass -SkipScreenshots to leave it alone.
#>
[CmdletBinding()]
param(
    [switch] $SkipScreenshots,
    [switch] $SkipPackage
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml] (Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$notes = Join-Path $root "docs\releases\$version.md"
if (-not (Test-Path $notes)) { throw "docs\releases\$version.md is missing (see docs\releases\README.md)" }
$release = (Get-Content $notes -Raw -Encoding UTF8) -replace "`r`n", "`n"

# The first text block under a heading (and sub-heading), or nothing.
function Get-ReleaseBlock([string] $Heading, [string] $SubHeading) {
    $pattern = '(?ms)^## ' + [regex]::Escape($Heading) + '[^\n]*\n'
    if ($SubHeading) { $pattern += '(?:(?!^## ).)*?^### ' + [regex]::Escape($SubHeading) + '\s*\n' }
    $pattern += '(?:(?!^#{1,3} ).)*?^```text\n(.*?)\n^```'
    $match = [regex]::Match($release, $pattern)
    if ($match.Success) { $match.Groups[1].Value }
}

function Assert-Limit([string] $Name, [int] $Length, [int] $Limit) {
    if ($Length -gt $Limit) { throw "$Name is $Length characters (limit: $Limit)" }
}

$certification = Get-ReleaseBlock "Notes for certification"
if (-not $certification) { throw "No Notes for certification block in docs\releases\$version.md" }
Assert-Limit 'Notes for certification' $certification.Length 1999
Write-Host "Notes for certification: $($certification.Length) characters"

$whatsNew = @{}
foreach ($lang in 'tr', 'en') {
    $text = Get-ReleaseBlock "What's new" $lang.ToUpperInvariant()
    if ($text) {
        Assert-Limit "What's new ($lang)" $text.Length 1500
        $whatsNew[$lang] = $text
    }

    $listing = Join-Path $root "docs\store-listing\$lang"
    Assert-Limit "Description ($lang)" (Get-Content (Join-Path $listing 'description.txt') -Raw -Encoding UTF8).Length 10000
    Assert-Limit "Short description ($lang)" (Get-Content (Join-Path $listing 'short-description.txt') -Raw -Encoding UTF8).Length 1000
    Assert-Limit "License terms ($lang)" (Get-Content (Join-Path $listing 'license-terms.txt') -Raw -Encoding UTF8).Length 10000
    $features = @(Get-Content (Join-Path $listing 'features.txt') -Encoding UTF8 | Where-Object { $_ })
    Assert-Limit "Features ($lang), count" $features.Count 20
    $features | ForEach-Object { Assert-Limit "Feature '$_'" $_.Length 200 }
    Assert-Limit "Keywords ($lang), count" @(Get-Content (Join-Path $listing 'keywords.txt') -Encoding UTF8 | Where-Object { $_ }).Count 7
}

$dist = Join-Path $root 'dist'
$store = Join-Path $dist 'store'
$old = Join-Path $dist 'old_versions'
$versionFile = Join-Path $store 'VERSION.txt'

# Nothing is deleted: an earlier release moves aside.
if (Test-Path $store) {
    $previous = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw).Trim() } else { 'unknown' }
    if ($previous -ne $version) {
        New-Item -ItemType Directory -Force $old | Out-Null
        $target = Join-Path $old $previous
        if (Test-Path $target) { $target = "$target-$(Get-Date -Format yyyyMMdd-HHmmss)" }
        Move-Item $store $target
        Write-Host "Moved release $previous to $target"
    }
    else {
        Remove-Item $store -Recurse -Force
    }
}
New-Item -ItemType Directory -Force $store | Out-Null
Set-Content $versionFile $version -Encoding ASCII

$layout = Join-Path $root 'artifacts\package\x64\layout'
if (-not $SkipPackage) {
    & (Join-Path $PSScriptRoot 'package.ps1') | Out-Null
    $packages = New-Item -ItemType Directory -Force (Join-Path $store 'packages')
    Copy-Item (Join-Path $root "artifacts\package\x64\MrWConverter_${version}_x64.msix") $packages.FullName
}

foreach ($lang in 'tr', 'en') {
    $target = New-Item -ItemType Directory -Force (Join-Path $store "listing\$lang")
    Copy-Item (Join-Path $root "docs\store-listing\$lang\*.txt") $target.FullName
    if ($whatsNew[$lang]) { Set-Content (Join-Path $target.FullName 'whats-new.txt') $whatsNew[$lang] -Encoding UTF8 -NoNewline }
}
Set-Content (Join-Path $store 'certification-notes.txt') $certification -Encoding UTF8 -NoNewline
Copy-Item $notes $store
Copy-Item (Join-Path $root 'docs\store-listing\privacy-policy.md') $store

$listings = Join-Path $store 'listing'
$raw = Join-Path $root 'artifacts\store-shots'
if (-not $SkipScreenshots) {
    & (Join-Path $PSScriptRoot 'capture-store-screenshots.ps1') -Exe (Join-Path $layout 'MrWConverter.exe') -OutDir $raw
}
python (Join-Path $PSScriptRoot 'make-listing-images.py') $listings $raw
if ($LASTEXITCODE -ne 0) { throw 'make-listing-images.py failed' }

@"
MrW Converter $version - Microsoft Store gönderimi

packages\                     Packages > yükle (.msix, yalnız x64)
$version.md                   sürüm notu ve gönderim listesi (kaynağı docs\releases\$version.md)
certification-notes.txt       Submission options > Notes for certification
privacy-policy.md             sitede yayında: https://converter.mrwproject.com/privacy/ > Properties > Privacy policy URL
                              Web sitesi: https://converter.mrwproject.com/
                              Destek: https://converter.mrwproject.com/contact/
listing\<tr|en>\description.txt        Store listing > Description
listing\<tr|en>\short-description.txt  Store listing > Short description
listing\<tr|en>\features.txt           Store listing > Product features (satır başına bir madde)
listing\<tr|en>\keywords.txt           Store listing > Search terms (satır başına bir kelime)
listing\<tr|en>\license-terms.txt      Store listing > Additional license terms
listing\<tr|en>\whats-new.txt          Store listing > What's new in this version (yalnız güncellemelerde)
listing\<tr|en>\screenshots\*.png      Store listing > Screenshots (1920x1080, sırayla)
listing\<tr|en>\logos\poster-*.png     Store listing > Store logos > 9:16 Poster art
listing\<tr|en>\logos\boxart-*.png     Store listing > Store logos > 1:1 Box art
listing\<tr|en>\logos\apptile-300x300.png  Store listing > Store logos > 1:1 App tile icon

Ayrıntılar: docs\store-listing\README.md ve docs\releases\README.md
"@ | Set-Content (Join-Path $store 'README.txt') -Encoding UTF8

Write-Host "Store release $version ready in $store"
Get-ChildItem $store -Recurse -File | ForEach-Object { '  ' + $_.FullName.Substring($store.Length + 1) }
