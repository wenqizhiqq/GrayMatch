@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
cl /EHsc /O2 /std:c++17 /I. fastcpp_test.cpp fastcpp.cpp /Fe:fastcpp_test.exe >> _fcbuild.log 2>&1
echo === RUN === >> _fcbuild.log 2>&1
if exist fastcpp_test.exe fastcpp_test.exe >> _fcbuild.log 2>&1
type _fcbuild.log
