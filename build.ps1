param(
    [string]$BinDir = ''
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$srcDir = Join-Path $root 'src'
if (-not $BinDir) { $BinDir = Join-Path $root 'bin' }

$cscCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) { throw 'csc.exe not found (.NET Framework 4.x C# compiler ships with Windows).' }

# Product name and version live in Product.cs; read them as UTF-8 so this script stays ASCII.
$productText = [System.IO.File]::ReadAllText((Join-Path $srcDir 'Product.cs'), [System.Text.Encoding]::UTF8)
function Get-Const([string]$name) {
    $m = [regex]::Match($productText, "(?m)$name\s*=\s*""([^""]*)""")
    if (-not $m.Success) { throw "constant $name not found in src/Product.cs" }
    return $m.Groups[1].Value
}
$title = Get-Const 'Title'
$versionText = Get-Const 'VersionText'
$fileVersion = Get-Const 'FileVersion'
$company = Get-Const 'Company'

Write-Output "product      : $title $versionText"
Write-Output "file version : $fileVersion"

New-Item -ItemType Directory -Force -Path $BinDir | Out-Null
$exe = Join-Path $BinDir "$title.exe"
if (Test-Path -LiteralPath $exe) {
    try { Remove-Item -LiteralPath $exe -Force }
    catch { throw "$exe is locked - close the running program before building" }
}

$asm = Join-Path $BinDir 'AssemblyInfo.cs'
$lines = @(
    'using System.Reflection;',
    '',
    "[assembly: AssemblyTitle(""$title"")]",
    "[assembly: AssemblyDescription(""$title"")]",
    "[assembly: AssemblyProduct(""$title"")]",
    "[assembly: AssemblyCompany(""$company"")]",
    "[assembly: AssemblyInformationalVersion(""$versionText"")]",
    "[assembly: AssemblyVersion(""$fileVersion"")]",
    "[assembly: AssemblyFileVersion(""$fileVersion"")]"
)
[System.IO.File]::WriteAllText($asm, ($lines -join [Environment]::NewLine), (New-Object System.Text.UTF8Encoding($false)))

$sources = Get-ChildItem -LiteralPath $srcDir -Filter '*.cs' | ForEach-Object { $_.FullName }
$sources += $asm

$icon = Join-Path $srcDir 'app.ico'
$iconArgs = @()
if (Test-Path -LiteralPath $icon) {
    $iconArgs = @("/win32icon:$icon")
} else {
    Write-Warning "src\app.ico missing - the exe will ship with the default Windows icon"
}

& $csc '/nologo' '/target:winexe' '/optimize+' '/platform:anycpu' '/codepage:65001' `
    "/win32manifest:$srcDir\app.manifest" $iconArgs `
    '/r:System.Windows.Forms.dll' '/r:System.Drawing.dll' '/r:System.Management.dll' `
    "/out:$exe" $sources
if ($LASTEXITCODE -ne 0) { throw "csc failed, exit code $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $exe)) { throw "csc reported success but $exe is missing" }

$version = (Get-Item -LiteralPath $exe).VersionInfo
if ($version.FileVersion -ne $fileVersion) { throw "FileVersion mismatch: got $($version.FileVersion), want $fileVersion" }
if ($version.ProductVersion -ne $versionText) { throw "ProductVersion mismatch: got $($version.ProductVersion), want $versionText" }

$selfTestOut = Join-Path $BinDir 'selftest.txt'
if (Test-Path -LiteralPath $selfTestOut) { Remove-Item -LiteralPath $selfTestOut -Force }
$proc = Start-Process -FilePath $exe -ArgumentList @('--selftest', "--out=`"$selfTestOut`"") -Wait -PassThru
if (-not (Test-Path -LiteralPath $selfTestOut)) { throw "selftest produced no report at $selfTestOut" }

$report = [System.IO.File]::ReadAllText($selfTestOut, [System.Text.Encoding]::UTF8)
$failLines = @($report -split "`r?`n" | Where-Object { $_ -like 'FAIL *' })
$totalLine = ($report -split "`r?`n" | Where-Object { $_ -like 'SELFTEST_RESULT *' }) | Select-Object -First 1

Write-Output "selftest     : $totalLine"
if ($failLines.Count -gt 0) {
    $failLines | ForEach-Object { Write-Output $_ }
    throw "selftest has $($failLines.Count) failing checks"
}
if ($proc.ExitCode -ne 0) { throw "selftest exit code $($proc.ExitCode)" }

$size = (Get-Item -LiteralPath $exe).Length
Write-Output "BUILD_PASS"
Write-Output "EXE=$exe"
Write-Output "SIZE_BYTES=$size"
Write-Output "FILEVERSION=$($version.FileVersion)"
Write-Output "PRODUCTVERSION=$($version.ProductVersion)"
