param(
    [switch]$Download
)

# Builds dist/ZapretOBKHOD-router.zip from the Router folder.
# With -Download it fetches the pinned zapret v72.13 binaries first.
# This script is ASCII-only for PowerShell 5.1 compatibility.

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$router = Join-Path $root 'Router'
$dist = Join-Path $root 'dist'
$bin = Join-Path $router 'bin'
$lists = Join-Path $router 'lists'
$fake = Join-Path $router 'fake'
$checksums = Join-Path $router 'CHECKSUMS.sha256'

$zipUrl = 'https://github.com/bol-van/zapret/releases/download/v72.13/zapret-v72.13-openwrt-embedded.tar.gz'
$expected = @{
    'bin/tpws-mipsel' = 'd20ad948f43b586524cce7174f5655b92438926e1bb8ceab4dd4887a80b8c5d7'
    'bin/nfqws-mipsel' = '71b558ddbfaea3a2ca7484cb95ca4fa6dd5b8785750ed7e07c677af4231294aa'
    'bin/tpws-mips' = '1e58f2be6e4531d0457330837c901c06aedeb1bea9117ec35b8b04622c9c301d'
    'bin/nfqws-mips' = 'f385d610da994c9473263d2df187fcfdd19a1b7b254937b5aa3c983be239db83'
}

New-Item -ItemType Directory -Force $bin, $lists, $fake, $dist | Out-Null

if ($Download) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) 'zobr-router-build'
    New-Item -ItemType Directory -Force $tmp | Out-Null
    $archive = Join-Path $tmp 'zapret-openwrt.tar.gz'
    $extract = Join-Path $tmp 'src'
    Write-Host 'Downloading zapret v72.13 embedded archive...'
    & curl.exe -fsSL --max-time 300 -o $archive $zipUrl
    if ($LASTEXITCODE -ne 0) { throw 'Download failed' }
    New-Item -ItemType Directory -Force $extract | Out-Null
    & tar.exe -xzf $archive -C $extract
    $source = Join-Path $extract 'zapret-v72.13\binaries'
    Copy-Item (Join-Path $source 'linux-mipsel\tpws') (Join-Path $bin 'tpws-mipsel') -Force
    Copy-Item (Join-Path $source 'linux-mipsel\nfqws') (Join-Path $bin 'nfqws-mipsel') -Force
    Copy-Item (Join-Path $source 'linux-mips\tpws') (Join-Path $bin 'tpws-mips') -Force
    Copy-Item (Join-Path $source 'linux-mips\nfqws') (Join-Path $bin 'nfqws-mips') -Force
    Copy-Item (Join-Path $root 'lists\*.txt') $lists -Force
    Copy-Item (Join-Path $root 'engine\files\fake\quic_initial_www_google_com.bin') $fake -Force
    Copy-Item (Join-Path $root 'engine\files\fake\discord-ip-discovery-with-port.bin') $fake -Force
    Copy-Item (Join-Path $root 'engine\LICENSE-zapret.txt') $router -Force
    Copy-Item (Join-Path $root 'engine\LICENSE-GPL-3.0.txt') $router -Force
    Copy-Item (Join-Path $root 'engine\LICENSE-LGPL-3.0.txt') $router -Force
    Copy-Item (Join-Path $root 'engine\THIRD-PARTY-NOTICES.txt') $router -Force
}

foreach ($entry in $expected.GetEnumerator()) {
    $path = Join-Path $router ($entry.Key -replace '/', '\')
    if (-not (Test-Path $path)) { throw "Missing binary: $($entry.Key)" }
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $entry.Value) { throw "Bad hash for $($entry.Key): $hash" }
}

if (-not (Test-Path $checksums)) { throw "Missing $checksums" }
foreach ($line in (Get-Content $checksums)) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $parts = $line -split '\s+', 2
    $path = Join-Path $router ($parts[1] -replace '/', '\')
    if (-not (Test-Path $path)) { throw "Missing file: $($parts[1])" }
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $parts[0]) { throw "Checksum mismatch: $($parts[1])" }
}

$target = Join-Path $dist 'ZapretOBKHOD-router.zip'
$stage = Join-Path $dist 'stage'
$pkg = Join-Path $stage 'ZapretOBKHOD-router'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $pkg | Out-Null
Copy-Item (Join-Path $router '*') $pkg -Recurse -Force
Compress-Archive -Path $pkg -DestinationPath $target -Force
Remove-Item $stage -Recurse -Force
Write-Host "Built: $target"
Write-Host "Size: $((Get-Item $target).Length) bytes"
