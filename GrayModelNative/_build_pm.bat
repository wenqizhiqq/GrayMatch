@echo off
rem Standalone build + run for the point-matcher kernel (no OpenCV, does not touch GrayModelNative.dll)
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
cd /d "%~dp0"
cl /nologo /EHsc /O2 /W3 /std:c++17 /openmp /utf-8 /I. point_matcher_test.cpp point_matcher.cpp /Fe:point_matcher_test.exe
if errorlevel 1 exit /b 1
echo === RUN ===
point_matcher_test.exe
