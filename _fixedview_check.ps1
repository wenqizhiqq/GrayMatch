Add-Type -AssemblyName System.Drawing
$sig = @"
using System;
using System.Runtime.InteropServices;
public class WT {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
}
"@
Add-Type -TypeDefinition $sig -ErrorAction SilentlyContinue
[WT]::SetProcessDPIAware() | Out-Null

Remove-Item "$env:LOCALAPPDATA\GrayMatch\crash.log" -ErrorAction SilentlyContinue
$exe = 'D:\wqz\code\GrayMatch\GrayMatch.Wpf\bin\Debug\net8.0-windows\GrayMatch.Wpf.exe'
$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 15
$q = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
if (-not $q) { "EXITED code=$($p.ExitCode)"; exit 1 }
[WT]::ShowWindow($q.MainWindowHandle, 3) | Out-Null
Start-Sleep -Seconds 3
$q.Refresh()
$h = $q.MainWindowHandle
$r = New-Object WT+RECT
[WT]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
"alive pid=$($p.Id) window=${w}x${hh}"
$bmp = New-Object System.Drawing.Bitmap($w, $hh)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[WT]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save('D:\wqz\code\GrayMatch\_fixedview.png', [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"captured"
Start-Sleep -Seconds 200
