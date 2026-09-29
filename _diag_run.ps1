Add-Type -AssemblyName System.Drawing
$sig = @"
using System;
using System.Runtime.InteropServices;
public class WU {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
Add-Type -TypeDefinition $sig -ErrorAction SilentlyContinue

$exe = 'D:\wqz\code\GrayMatch\GrayMatch.Wpf\bin\Debug\net8.0-windows\GrayMatch.Wpf.exe'
$p = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 18
$q = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
if (-not $q) { "EXITED code=$($p.ExitCode)"; exit 1 }
"app up, waiting for auto-match..."
Start-Sleep -Seconds 10
"--- diag.log:"
Get-Content "$env:LOCALAPPDATA\GrayMatch\diag.log" -ErrorAction SilentlyContinue
Start-Sleep -Seconds 240
