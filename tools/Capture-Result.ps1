<#
  Capture-Result.ps1 — launch the app, click "Load sample build (offline)" via UI Automation
  so we land on the compiled-filter Result screen, then screenshot it. Verifies result-page
  design changes (import-code ticket, Copy flip, demoted re-import note).

  Usage: pwsh tools/Capture-Result.ps1 -Out _design/beta9_result.png
#>
param(
    [string]$Out = "_design/beta9_result.png",
    [int]$WaitMs = 4000,
    [string]$Exe = "D4BuildFilter.WPF/bin/Release/net10.0-windows/D4BuildFilter.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$exePath = Join-Path $root $Exe
if (-not (Test-Path $exePath)) { throw "Exe not found: $exePath" }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Cap2 {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

function Invoke-ButtonByName($rootEl, $name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $btn = $rootEl.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($btn -eq $null) { return $false }
    $ip = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $ip.Invoke()
    return $true
}

$proc = Start-Process -FilePath $exePath -PassThru
try {
    $deadline = (Get-Date).AddSeconds(20)
    while ($proc.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200; $proc.Refresh() }
    if ($proc.MainWindowHandle -eq 0) { throw "Window never appeared." }
    Start-Sleep -Milliseconds 2500

    $auto = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $clicked = Invoke-ButtonByName $auto "Load sample build (offline)"
    if (-not $clicked) {
        # fall back to any button whose name contains 'sample'
        $all = $auto.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)))
        foreach ($b in $all) { if ($b.Current.Name -like "*sample*") { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); $clicked=$true; break } }
    }
    if (-not $clicked) { Write-Warning "Sample button not found -- capturing whatever is shown." }
    Start-Sleep -Milliseconds $WaitMs

    $hwnd = $proc.MainWindowHandle
    [void][Win32Cap2]::SetForegroundWindow($hwnd); Start-Sleep -Milliseconds 400
    $r = New-Object Win32Cap2+RECT
    [void][Win32Cap2]::GetWindowRect($hwnd, [ref]$r)
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc(); [void][Win32Cap2]::PrintWindow($hwnd, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
    $outPath = Join-Path $root $Out
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "Saved $outPath ($w x $h)"
}
finally {
    if (-not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 500 }
    if (-not $proc.HasExited) { $proc.Kill() }
}
