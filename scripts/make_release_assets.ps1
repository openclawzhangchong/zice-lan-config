param(
    [string]$Version = '1.3.0'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$binDir = Join-Path $root 'bin'
$stage = Join-Path $binDir "release-$Version"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$exe = Get-ChildItem -LiteralPath $binDir -Filter '*.exe' | Select-Object -First 1
if (-not $exe) { throw 'no exe in bin - run build.ps1 first' }
Copy-Item -LiteralPath $exe.FullName -Destination $stage
foreach ($doc in @('README.md', 'LICENSE', 'CHANGELOG.md')) {
    $path = Join-Path $root $doc
    if (-not (Test-Path -LiteralPath $path)) { throw "missing $doc" }
    Copy-Item -LiteralPath $path -Destination $stage
}

$zip = Join-Path $binDir "zice-lan-config-v$Version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

Write-Output ('EXE=' + $exe.FullName)
Write-Output ('EXE_BYTES=' + $exe.Length)
Write-Output ('ZIP=' + $zip)
Write-Output ('ZIP_BYTES=' + (Get-Item -LiteralPath $zip).Length)
Write-Output ('STAGE=' + ((Get-ChildItem -LiteralPath $stage | ForEach-Object { $_.Name }) -join ', '))
foreach ($file in @($exe.FullName, $zip)) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLower()
    Write-Output ('SHA256 ' + $hash + '  ' + (Split-Path -Leaf $file))
}
