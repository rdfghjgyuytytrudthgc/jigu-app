# FILE: jigu-app/tests/capture-builtin.ps1
# Launch the built-in native UI, type a Chinese query, run the search and screenshot the window.
param(
  [Parameter(Mandatory=$true)][string]$ExePath,
  [Parameter(Mandatory=$true)][string]$OutPath,
  [int]$WaitSeconds = 5,
  [int]$AfterSearchSeconds = 4
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$sig = @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class W32 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after,
        int X, int Y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(
        IntPtr parent, IntPtr child, string cls, string title);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(
        IntPtr hWnd, uint msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool PostMessage(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
"@
if (-not ("W32" -as [type])) { Add-Type -TypeDefinition $sig }

$p = Start-Process -FilePath $ExePath -ArgumentList '--built-in' -PassThru
Write-Host "started pid=$($p.Id)"
Start-Sleep -Seconds $WaitSeconds

$proc = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
if (-not $proc) { Write-Host "process exited early"; exit 1 }
$h = $proc.MainWindowHandle
if ($h -eq 0) {
  for ($i = 0; $i -lt 24; $i++) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh(); $h = $proc.MainWindowHandle
    if ($h -ne 0) { break }
  }
}
if ($h -eq 0) { Write-Host "no main window handle"; if ($p) { $p.Kill() }; exit 2 }

[void][W32]::ShowWindow($h, 5)
[void][W32]::SetWindowPos($h, [IntPtr](-1), 40, 40, 0, 0, 0x0041)
[void][W32]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 1200

$r = New-Object W32+RECT
[void][W32]::GetWindowRect($h, [ref]$r)
$w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
Write-Host "window rect: $($r.Left),$($r.Top) ${w}x${ht}"

# ---- type the query into the search box (first Edit child of the top-level window) ----
# Query is built from code points so this script stays pure ASCII:
#   U+66F9 U+64CD = "Cao Cao"
$q = [string][char]0x66F9 + [string][char]0x64CD
$edit = [W32]::FindWindowEx($h, [IntPtr]::Zero, "Edit", $null)
Write-Host "edit handle: $edit"
if ($edit -ne [IntPtr]::Zero) {
  [void][W32]::SendMessage($edit, 0x000C, [IntPtr]::Zero, $q)   # WM_SETTEXT
  Start-Sleep -Milliseconds 400
  # Ctrl+Enter triggers the search
  [void][W32]::SendMessage($edit, 0x0100, [IntPtr]0x11, [IntPtr]::Zero)   # WM_KEYDOWN VK_CONTROL
  [void][W32]::SendMessage($edit, 0x0100, [IntPtr]0x0D, [IntPtr]::Zero)   # WM_KEYDOWN VK_RETURN
  [void][W32]::SendMessage($edit, 0x0101, [IntPtr]0x0D, [IntPtr]::Zero)
  [void][W32]::SendMessage($edit, 0x0101, [IntPtr]0x11, [IntPtr]::Zero)
  Write-Host "query set"
} else {
  Write-Host "no edit control found"
}

Start-Sleep -Seconds $AfterSearchSeconds
[void][W32]::SetWindowPos($h, [IntPtr](-1), 40, 40, $w, $ht, 0x0040)
[void][W32]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 900

$r2 = New-Object W32+RECT
[void][W32]::GetWindowRect($h, [ref]$r2)
$w = $r2.Right - $r2.Left; $ht = $r2.Bottom - $r2.Top
Write-Host "final rect: $($r2.Left),$($r2.Top) ${w}x${ht}"

$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r2.Left, $r2.Top, 0, 0, $bmp.Size)
$bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "saved: $OutPath ($([math]::Round((Get-Item $OutPath).Length/1KB,1)) KB)"
Write-Host "title: $($proc.MainWindowTitle)"
$p.Kill()
