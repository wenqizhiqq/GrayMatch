@echo off
rem Rebuild GrayModelNative.dll into build-ninja9 (the path the WPF csproj copies from).
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
cd /d "%~dp0"
cmake --build build-ninja9
if errorlevel 1 exit /b 1
echo === EXPORTS ===
dumpbin /exports build-ninja9\GrayModelNative.dll | findstr /i "gm_"
