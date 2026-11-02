param(
    [int]$WaitSeconds = 85,
    [int]$StopAfterSeconds = 0,
    [string]$OutPng = '',
    [switch]$IncludeApplyDialog,
    [switch]$WallpaperOnly,
    [switch]$HotspotOnly
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public struct WinRect { public int Left; public int Top; public int Right; public int Bottom; }

public static class Win32Win
{
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out WinRect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);

    public struct POINT { public int X; public int Y; }

    public static IntPtr MakeLParam(int x, int y)
    {
        return (IntPtr)((y << 16) | (x & 0xFFFF));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int maxCount);

    public static string ClassOf(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, 256);
        return sb.ToString();
    }

    public static List<IntPtr> ChildWindows(IntPtr parent)
    {
        var list = new List<IntPtr>();
        EnumProc proc = delegate(IntPtr h, IntPtr l) { list.Add(h); return true; };
        EnumChildWindows(parent, proc, IntPtr.Zero);
        GC.KeepAlive(proc);
        return list;
    }

    public static List<IntPtr> TopWindows(int processId)
    {
        var list = new List<IntPtr>();
        EnumProc proc = delegate(IntPtr h, IntPtr l)
        {
            int pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == processId && IsWindowVisible(h) && GetWindowTextLength(h) > 0)
            {
                list.Add(h);
            }
            return true;
        };
        EnumWindows(proc, IntPtr.Zero);
        GC.KeepAlive(proc);
        return list;
    }

    public static string TextOf(IntPtr hWnd)
    {
        var sb = new StringBuilder(512);
        GetWindowText(hWnd, sb, 512);
        return sb.ToString();
    }
}
'@

$root = Split-Path -Parent $PSScriptRoot
$exe = Get-ChildItem -LiteralPath (Join-Path $root 'bin') -Filter '*.exe' |
    Sort-Object { [version]$_.VersionInfo.FileVersion } -Descending | Select-Object -First 1
if (-not $exe) { throw 'no exe in bin - run build.ps1 first' }
$ver = (Get-Item -LiteralPath $exe.FullName).VersionInfo.ProductVersion
$screenDir = Join-Path $root 'docs\screens'
if (-not $OutPng) { $OutPng = Join-Path $screenDir "gui-$ver.png" }
$dialogPng = Join-Path $screenDir "confirm-dialog-$ver.png"
New-Item -ItemType Directory -Force -Path $screenDir | Out-Null
foreach ($png in @($OutPng, $dialogPng)) {
    if (Test-Path -LiteralPath $png) { Remove-Item -LiteralPath $png -Force }
}

function Save-Png($hWnd, $path) {
    $rect = New-Object WinRect
    if (-not [Win32Win]::GetWindowRect($hWnd, [ref]$rect)) { throw "GetWindowRect failed for $path" }
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -lt 60 -or $height -lt 60) { throw "suspicious window size ${width}x${height}" }
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $graphics.GetHdc()
    $ok = [Win32Win]::PrintWindow($hWnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
    if (-not $ok) { $bmp.Dispose(); throw "PrintWindow failed for $path" }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output ("SCREENSHOT=" + $path + " " + $width + "x" + $height)
}

function Click-Child($parent, $needle) {
    foreach ($h in [Win32Win]::ChildWindows($parent)) {
        if ([Win32Win]::TextOf($h) -like $needle) {
            if (-not [Win32Win]::PostMessage($h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)) { throw "BM_CLICK post failed for $needle" }
            return $h
        }
    }
    throw "control not found: $needle"
}

function Adapter-State {
    Get-CimInstance Win32_NetworkAdapterConfiguration | Where-Object { $_.IPEnabled -and $_.IPAddress } |
        ForEach-Object { ($_.IPAddress | Where-Object { $_ -notlike '*:*' }) -join ',' }
}

$before = Adapter-State
Write-Output ('ADAPTER_IP_BEFORE=' + ($before -join ' | '))

$proc = Start-Process -FilePath $exe.FullName -PassThru
$deadline = (Get-Date).AddSeconds(20)
while ($proc.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
    $proc.Refresh()
}
if ($proc.MainWindowHandle -eq 0) { throw 'window never appeared' }
Start-Sleep -Seconds 3
Write-Output ('WINDOW=' + $proc.MainWindowTitle)

$kids = [Win32Win]::ChildWindows($proc.MainWindowHandle)
Write-Output ('CHILD_CONTROLS=' + $kids.Count)
foreach ($h in $kids) {
    $text = [Win32Win]::TextOf($h)
    if ($text) { Write-Output ('  control: [' + ($text -replace "`r?`n", ' / ') + ']') }
}

if ($HotspotOnly) {
    $scopeKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters'
    $scopeBefore = (Get-ItemProperty -LiteralPath $scopeKey).ScopeAddress
    Write-Output ('SCOPE_BEFORE=' + $scopeBefore)
    Click-Child $proc.MainWindowHandle '*算建议段*' | Out-Null
    Write-Output 'INVOKED=hotspot-advice(BM_CLICK)'
    Start-Sleep -Seconds 25
    $listBox = [IntPtr]::Zero
    $listBoxTop = -1
    foreach ($h in [Win32Win]::ChildWindows($proc.MainWindowHandle)) {
        if ([Win32Win]::ClassOf($h) -like '*LISTBOX*') {
            $r = New-Object WinRect
            $null = [Win32Win]::GetWindowRect($h, [ref]$r)
            # two listboxes exist (free-IP list above, hotspot list below); take the lower one
            if ($r.Top -gt $listBoxTop) { $listBoxTop = $r.Top; $listBox = $h }
        }
    }
    if ($listBox -eq [IntPtr]::Zero) { throw 'hotspot suggestion listbox not found' }
    $null = [Win32Win]::PostMessage($listBox, 0x0201, [IntPtr]0x0001, [Win32Win]::MakeLParam(30, 12))
    $null = [Win32Win]::PostMessage($listBox, 0x0202, [IntPtr]::Zero, [Win32Win]::MakeLParam(30, 12))
    Write-Output 'INVOKED=list-select(WM_LBUTTONDOWN)'
    Start-Sleep -Seconds 2
    Save-Png $proc.MainWindowHandle $OutPng
    Click-Child $proc.MainWindowHandle '*写入选中段*' | Out-Null
    Write-Output 'INVOKED=hotspot-apply(BM_CLICK)'
    Start-Sleep -Seconds 3
    $dialog = [IntPtr]::Zero
    foreach ($top in [Win32Win]::TopWindows($proc.Id)) {
        if ([Win32Win]::TextOf($top) -like '*确认修改热点网段*') { $dialog = $top }
    }
    if ($dialog -eq [IntPtr]::Zero) { throw 'hotspot confirm dialog did not open' }
    Save-Png $dialog (Join-Path $screenDir "confirm-hotspot-$ver.png")
    Click-Child $dialog '*否*' | Out-Null
    Write-Output 'INVOKED=cancel(否)'
    Start-Sleep -Seconds 3
    $null = $proc.CloseMainWindow()
    Start-Sleep -Seconds 2
    if (-not $proc.HasExited) { $proc.Kill() }
    $scopeAfter = (Get-ItemProperty -LiteralPath $scopeKey).ScopeAddress
    Write-Output ('SCOPE_AFTER=' + $scopeAfter)
    if ($scopeBefore -ne $scopeAfter) { throw 'HOTSPOT SCOPE CHANGED on the cancel path' }
    Write-Output 'SCOPE_UNCHANGED=ok'
    $hotLog = Get-ChildItem -LiteralPath (Join-Path $root 'bin\logs') -Filter 'run-*.log' | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($hotLog) {
        Get-Content -LiteralPath $hotLog.FullName -Encoding UTF8 | Select-Object -Last 5 | ForEach-Object { Write-Output ('  log: ' + $_) }
    }
    Write-Output 'CLOSED=ok'
    return
}

if ($WallpaperOnly) {
    Click-Child $proc.MainWindowHandle '*生成并设为壁纸*' | Out-Null
    Write-Output 'INVOKED=wallpaper-dropdown-colors(BM_CLICK)'
    Start-Sleep -Seconds 8
    Click-Child $proc.MainWindowHandle '*一键纯黑壁纸*' | Out-Null
    Write-Output 'INVOKED=wallpaper-forced-black(BM_CLICK)'
    Start-Sleep -Seconds 18
    Save-Png $proc.MainWindowHandle $OutPng
    $null = $proc.CloseMainWindow()
    Start-Sleep -Seconds 2
    if (-not $proc.HasExited) { $proc.Kill() }
    $wallLog = Get-ChildItem -LiteralPath (Join-Path $root 'bin\logs') -Filter 'run-*.log' | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($wallLog) {
        Get-Content -LiteralPath $wallLog.FullName -Encoding UTF8 | Select-Object -Last 7 | ForEach-Object { Write-Output ('  log: ' + $_) }
    }
    Write-Output 'CLOSED=ok'
    return
}

Click-Child $proc.MainWindowHandle '*开始扫描*' | Out-Null
Write-Output 'INVOKED=scan(BM_CLICK)'
if ($StopAfterSeconds -gt 0) {
    Start-Sleep -Seconds $StopAfterSeconds
    Click-Child $proc.MainWindowHandle '停止' | Out-Null
    Write-Output ('INVOKED=stop(BM_CLICK after ' + $StopAfterSeconds + 's)')
    Start-Sleep -Seconds 25
} else {
    Start-Sleep -Seconds $WaitSeconds
}

if ($IncludeApplyDialog) {
    Click-Child $proc.MainWindowHandle '*随机抽一个*' | Out-Null
    Write-Output 'INVOKED=random(BM_CLICK)'
    Start-Sleep -Seconds 2
    Click-Child $proc.MainWindowHandle '*写入静态*' | Out-Null
    Write-Output 'INVOKED=apply(BM_CLICK)'
    Start-Sleep -Seconds 3
    $dialog = $null
    foreach ($top in [Win32Win]::TopWindows($proc.Id)) {
        Write-Output ('  topwindow: [' + ([Win32Win]::TextOf($top) -replace "`r?`n", ' / ') + ']')
        if ([Win32Win]::TextOf($top) -like '*确认写入*') { $dialog = $top }
    }
    if (-not $dialog) { throw 'confirm dialog did not open' }
    Save-Png $dialog $dialogPng
    Click-Child $dialog '*否*' | Out-Null
    Write-Output 'INVOKED=cancel(否)'
    Start-Sleep -Seconds 3
}

Save-Png $proc.MainWindowHandle $OutPng

$null = $proc.CloseMainWindow()
Start-Sleep -Seconds 2
if (-not $proc.HasExited) { $proc.Kill() }
Write-Output 'CLOSED=ok'

$after = Adapter-State
Write-Output ('ADAPTER_IP_AFTER=' + ($after -join ' | '))
if (($before -join ',') -ne ($after -join ',')) { throw 'ADAPTER IP CHANGED - cancel path is not safe' }
Write-Output 'UNCHANGED=ok'

$logFile = Get-ChildItem -LiteralPath (Join-Path $root 'bin\logs') -Filter 'run-*.log' | Sort-Object LastWriteTime | Select-Object -Last 1
if ($logFile) {
    Write-Output ('LOGFILE=' + $logFile.FullName)
    Get-Content -LiteralPath $logFile.FullName -Encoding UTF8 | Select-Object -Last 8 | ForEach-Object { Write-Output ('  log: ' + $_) }
}
