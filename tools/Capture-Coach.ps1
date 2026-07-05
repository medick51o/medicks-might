<#
  Capture-Coach.ps1 — launch the app, click the "Crafting Coach" nav tab via UI Automation,
  screenshot the coach landing (goal cards + dust decoder), then close. Clone of
  Capture-Result.ps1 with the nav target swapped.

  Usage: pwsh tools/Capture-Coach.ps1 -Out _design/coach.png
#>
param(
    [string]$Out = "_design/coach.png",
    [string[]]$Then = @(),          # extra buttons to click, in order, after the Coach tab
    [string]$Expect = "",           # optional: assert an element with this UIA name exists before capture
    [switch]$ScrollToEnd,           # optional: scroll the main ScrollViewer to the bottom before capture
    [int]$WaitMs = 2500,
    [string]$Exe = "D4BuildFilter.WPF/bin/Debug/net10.0-windows/D4BuildFilter.exe"
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
public class Win32CapCoach {
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
    if (-not (Invoke-ButtonByName $auto "Crafting Coach")) { Write-Warning "Coach tab button not found -- capturing whatever is shown." }
    foreach ($t in $Then) {
        if (-not (Invoke-ButtonByName $auto $t)) { Write-Warning "Button not found: $t" }
        Start-Sleep -Milliseconds 1200
    }
    Start-Sleep -Milliseconds $WaitMs
    if ($Expect) {
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $Expect)
        $el = $auto.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($el -eq $null) { Write-Host "EXPECT '$Expect': MISSING" } else { Write-Host "EXPECT '$Expect': FOUND" }
    }
    if ($ScrollToEnd) {
        $sCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::IsScrollPatternAvailableProperty, $true)
        foreach ($sv in $auto.FindAll([System.Windows.Automation.TreeScope]::Descendants, $sCond)) {
            $sp = $sv.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
            if (-not $sp.Current.VerticallyScrollable) { continue }
            try { $sp.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 100); Start-Sleep -Milliseconds 600; break } catch {}
        }
    }

    $hwnd = $proc.MainWindowHandle
    [void][Win32CapCoach]::SetForegroundWindow($hwnd); Start-Sleep -Milliseconds 400
    $r = New-Object Win32CapCoach+RECT
    [void][Win32CapCoach]::GetWindowRect($hwnd, [ref]$r)
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc(); [void][Win32CapCoach]::PrintWindow($hwnd, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
    $outPath = Join-Path $root $Out
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "Saved $outPath ($w x $h)"
}
finally {
    if (-not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 500 }
    if (-not $proc.HasExited) { $proc.Kill() }
}


