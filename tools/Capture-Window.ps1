<#
  Capture-Window.ps1 — launch the built MedicK's Might exe, wait for it to render,
  screenshot its window to a PNG, then close it. Used to verify design changes without
  hand-grabbing with Snagit.

  Usage:
    pwsh tools/Capture-Window.ps1 -Out _design/beta9_landing.png
    pwsh tools/Capture-Window.ps1 -Out _design/beta9_landing.png -WaitMs 3500

  Notes:
    - Requires an interactive desktop session (the WPF window must actually render).
    - Uses PrintWindow so the target need not be the foreground window.
#>
param(
    [string]$Out = "_design/capture.png",
    [int]$WaitMs = 3000,
    [string]$Exe = "D4BuildFilter.WPF/bin/Release/net10.0-windows/D4BuildFilter.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$exePath = Join-Path $root $Exe
if (-not (Test-Path $exePath)) { throw "Exe not found: $exePath  (run: dotnet build -c Release D4BuildFilter.WPF)" }

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Cap {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$proc = Start-Process -FilePath $exePath -PassThru
try {
    # wait for the main window handle, then give WPF time to lay out + load themes
    $deadline = (Get-Date).AddSeconds(20)
    while ($proc.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $proc.Refresh()
    }
    if ($proc.MainWindowHandle -eq 0) { throw "Window never appeared." }
    Start-Sleep -Milliseconds $WaitMs

    $hwnd = $proc.MainWindowHandle
    [void][Win32Cap]::SetForegroundWindow($hwnd)
    Start-Sleep -Milliseconds 400

    $r = New-Object Win32Cap+RECT
    [void][Win32Cap]::GetWindowRect($hwnd, [ref]$r)
    $w = $r.Right - $r.Left
    $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { throw "Bad window rect ($w x $h)." }

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    # flag 2 = PW_RENDERFULLCONTENT (captures hardware-composited content like WPF)
    [void][Win32Cap]::PrintWindow($hwnd, $hdc, 2)
    $g.ReleaseHdc($hdc)
    $g.Dispose()

    $outPath = Join-Path $root $Out
    $outDir = Split-Path -Parent $outPath
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force $outDir | Out-Null }
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Saved $outPath ($w x $h)"
}
finally {
    if (-not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 500 }
    if (-not $proc.HasExited) { $proc.Kill() }
}
