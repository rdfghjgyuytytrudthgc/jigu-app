# FILE: jigu-app/tests/capture-early.ps1
# Launch the app, wait until its window exists, then capture immediately.
# (In this sandbox the app's WebView2 host crashes, so we must capture early.)
param(
  [Parameter(Mandatory=$true)][string]$ExePath,
  [Parameter(Mandatory=$true)][string]$OutPath,
  [int]$WaitSeconds = 5
)

Add-Type -AssemblyName System.Drawing
$sig = @"
using System;
using System.Runtime.InteropServices;
public class Cap {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
}
"@
if (-not ("Cap" -as [type])) { Add-Type -TypeDefinition $sig }

$env:JIGU_DEBUG = "1"
$p = Start-Process -FilePath $ExePath -PassThru
Write-Host "pid=$($p.Id)"
Start-Sleep -Seconds $WaitSeconds

$proc = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "process already gone"; exit 1 }
$h = $proc.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { Write-Host "no window handle"; $p.Kill(); exit 2 }

[void][Cap]::ShowWindow($h, 5)
[void][Cap]::SetWindowPos($h, [IntPtr](-1), 30, 30, 0, 0, 0x0041)
[void][Cap]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 900

$r = New-Object Cap+RECT
if (-not [Cap]::GetWindowRect($h, [ref]$r)) { Write-Host "GetWindowRect failed"; $p.Kill(); exit 3 }
$w = $r.R - $r.L
$ht = $r.B - $r.T
if ($w -le 0 -or $ht -le 0) { Write-Host "bad size ${w}x${ht}"; $p.Kill(); exit 4 }

$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "saved: $OutPath  (${w}x${ht})  title='$($proc.MainWindowTitle)'"
$p.Kill()
