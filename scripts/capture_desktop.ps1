param(
    [string]$OutPng = ''
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public struct Rect2 { public int Left; public int Top; public int Right; public int Bottom; }

public static class DesktopWin
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Rect2 rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);
}
'@

$root = Split-Path -Parent $PSScriptRoot
if (-not $OutPng) { $OutPng = Join-Path $root 'docs\screens\desktop-current.png' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutPng) | Out-Null
if (Test-Path -LiteralPath $OutPng) { Remove-Item -LiteralPath $OutPng -Force }

$screenW = [DesktopWin]::GetSystemMetrics(0)
$screenH = [DesktopWin]::GetSystemMetrics(1)
Write-Output ('SCREEN=' + $screenW + 'x' + $screenH)
Write-Output ('REG_WALLPAPER=' + (Get-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name WallPaper).WallPaper)
Write-Output ('REG_STYLE=' + (Get-ItemProperty -Path 'HKCU:\Control Panel\Desktop' -Name WallpaperStyle).WallpaperStyle)

$hWnd = [DesktopWin]::FindWindow('Progman', $null)
if ($hWnd -eq [IntPtr]::Zero) { throw 'Progman window not found' }
$rect = New-Object Rect2
if (-not [DesktopWin]::GetWindowRect($hWnd, [ref]$rect)) { throw 'GetWindowRect failed' }
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -lt 100 -or $height -lt 100) { throw "suspicious desktop size ${width}x${height}" }

$bmp = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $graphics.GetHdc()
$ok = [DesktopWin]::PrintWindow($hWnd, $hdc, 2)
$graphics.ReleaseHdc($hdc)
$graphics.Dispose()
if (-not $ok) { $bmp.Dispose(); throw 'PrintWindow on Progman failed' }
$bmp.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ('SCREENSHOT=' + $OutPng + ' ' + $width + 'x' + $height)
