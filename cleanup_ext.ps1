# cleanup_ext.ps1
# Remove redundant OpenCV build artifacts from GrayMatch/ext/opencv/opencv.
# Safe to run: only third-party OpenCV binaries (re-downloadable). Your project
# source (GrayModelNative/, GrayMatch/, GrayMatch.Wpf/) is NOT touched.
#
# Run in a NORMAL PowerShell (the sandbox blocks deletion). From the project root:
#   powershell -ExecutionPolicy Bypass -File cleanup_ext.ps1
#
# In the normal environment the safe-delete hook sends removed items to the
# Recycle Bin first, so they remain recoverable.
$ErrorActionPreference = 'Continue'
$base = 'D:\wqz\code\GrayMatch\ext\opencv\opencv'
$keepDll = 'opencv_world480.dll'

function Remove-Target {
    param([string]$Path)
    if (-not (Test-Path $Path)) { Write-Host ("MISSING " + $Path); return }
    $sz = (Get-ChildItem $Path -Recurse -File -ErrorAction SilentlyContinue |
           Measure-Object -Property Length -Sum).Sum
    try {
        Remove-Item $Path -Recurse -Force -ErrorAction Stop
        Write-Host ('DELETED {0,8:N1} MB  {1}' -f ($sz / 1MB), $Path)
    } catch {
        Write-Host ('FAILED  {0} : {1}' -f $Path, $_.Exception.Message)
    }
}

# 1. OpenCV source tree (we only use the prebuilt libs)
Remove-Target (Join-Path $base 'sources')

# 2. Unused top-level build dirs (language bindings, sample apps, cascade xmls)
foreach ($d in @('python', 'java', 'bin', 'etc')) {
    Remove-Target (Join-Path $base ('build\' + $d))
}

# 3. All files in vc16/bin except the runtime dll
$binp = Join-Path $base 'build\x64\vc16\bin'
if (Test-Path $binp) {
    Get-ChildItem $binp | Where-Object { $_.Name -ne $keepDll } |
        ForEach-Object { Remove-Target $_.FullName }
}

# 4. Debug import lib
Remove-Target (Join-Path $base 'build\x64\vc16\lib\opencv_world480d.lib')

Write-Host ''
Write-Host 'KEPT (required for build + runtime):'
Write-Host ('  ' + (Join-Path $base 'build\include'))
Write-Host ('  ' + (Join-Path $base 'build\x64\vc16\lib\opencv_world480.lib'))
Write-Host ('  ' + (Join-Path $base 'build\x64\vc16\bin\opencv_world480.dll'))
