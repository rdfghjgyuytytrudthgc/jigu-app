# FILE: jigu-app/tests/capture-window.ps1
# Move the target main window to the top-left corner and capture a screenshot
param(
  [Parameter(Mandatory=$true)][string]$ExePath,
  [Parameter(Mandatory=$true)][string]$OutPath,
  [string]$Arguments = "",
  [int]$WaitSeconds = 6
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$sig = @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after,
        int X, int Y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
}
"@
if (-not ("Win32" -as [type])) { Add-Type -TypeDefinition $sig }

$args2 = @()
if ($Arguments -ne "") { $args2 = $Arguments.Split(" ") }
if ($args2.Count -gt 0) {
  $p = Start-Process -FilePath $ExePath -ArgumentList $args2 -PassThru
} else {
  $p = Start-Process -FilePath $ExePath -PassThru
}
Write-Host "started pid=$($p.Id)"
Start-Sleep -Seconds $WaitSeconds

$proc = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "process exited early"; exit 1 }
$h = $proc.MainWindowHandle
if ($h -eq 0) {
  for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    $h = $proc.MainWindowHandle
    if ($h -ne 0) { break }
  }
}
if ($h -eq 0) { Write-Host "no main window handle"; if ($p) { $p.Kill() }; exit 2 }

[void][Win32]::ShowWindow($h, 5)   # SW_SHOW
[void][Win32]::SetWindowPos($h, [IntPtr]::Zero, 0, 0, 0, 0, 0x0041)  # NOSIZE|SHOWWINDOW
[void][Win32]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 1200

$r = New-Object Win32+RECT
if (-not [Win32]::GetWindowRect($h, [ref]$r)) { Write-Host "GetWindowRect failed"; $p.Kill(); exit 3 }
$w = $r.Right - $r.Left
$ht = $r.Bottom - $r.Top
Write-Host "window rect: $($r.Left),$($r.Top) ${w}x${ht}"
if ($w -le 0 -or $ht -le 0) { Write-Host "bad size"; $p.Kill(); exit 4 }

# Topmost + reposition, so the DSH window cannot steal the capture area
[void][Win32]::SetWindowPos($h, [IntPtr](-1), 40, 40, $w, $ht, 0x0040)
[void][Win32]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 1500

$r2 = New-Object Win32+RECT
[void][Win32]::GetWindowRect($h, [ref]$r2)
$w = $r2.Right - $r2.Left
$ht = $r2.Bottom - $r2.Top
Write-Host "final rect: $($r2.Left),$($r2.Top) ${w}x${ht}"

$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r2.Left, $r2.Top, 0, 0, $bmp.Size)
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
Write-Host "saved: $OutPath ($([math]::Round((Get-Item $OutPath).Length/1KB,1)) KB)"
Write-Host "title: $($proc.MainWindowTitle)"
$p.Kill()
