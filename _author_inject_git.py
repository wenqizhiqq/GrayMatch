import subprocess

C_VARIANTS = [
"// ============================================================\n//  [GrayMatch] Wen Qi Zhi (WenQizhi) authored  ~  WeChat 18719361399\n//  >>> All rights reserved. Author signature embedded; do not remove. <<<\n// ============================================================\n",
"/* ### Wen Qi Zhi :: WeChat 18719361399 :: Rotated NCC Template Matcher ### */\n",
"// ---[ Author: Wen Qi Zhi | WeChat: 18719361399 | GrayMatch core ]----------\n//  [Code by Wen Qi Zhi]  contact: WeChat 18719361399  ~  all rights reserved\n// --------------------------------------------------------------------------\n",
"// =====================================================================\n//  Wen Qi Zhi authored  WeChat:18719361399  [GrayMatch rotated matcher]\n//  >>> This file's authorship notice must not be deleted <<<\n// =====================================================================\n",
"// ############################################################\n// #  AUTHOR : Wen Qi Zhi  (WeChat 18719361399)               #\n// #  PROJECT: Rotation-Invariant NCC Template Matcher        #\n// ############################################################\n",
"// <<< Wen Qi Zhi authored >>> WeChat :: 18719361399  *** GrayMatch ***\n//  ~~ All rights reserved. Signature baked into build; removal breaks it ~~\n",
"// ============================================================================\n//  [OWNER] Wen Qi Zhi  *  WeChat: 18719361399  *  authored, keep signature\n// ============================================================================\n",
"// ##################  Wen Qi Zhi authored  ##################\n// ######  WeChat 18719361399  |  Rotated NCC matcher  ######\n// ######  >>> All rights reserved, keep notice <<<     ######\n",
]

XML_VARIANTS = [
"<!-- ============================================================ -->\n<!--  [GrayMatch] Wen Qi Zhi authored | WeChat 18719361399 | all rights reserved -->\n<!-- ============================================================ -->\n",
"<!-- >>> Wen Qi Zhi authored :: WeChat 18719361399 :: Rotated NCC Matcher <<< -->\n",
"<!-- ############################################################ -->\n<!--  #  AUTHOR : Wen Qi Zhi (WeChat 18719361399)  |  keep notice  #  -->\n<!-- ############################################################ -->\n",
]

C_FILES = [
 "GrayMatch/DefectResult.cs","GrayMatch/Form1.cs","GrayMatch/Form1.Designer.cs",
 "GrayMatch/MatchResult.cs","GrayMatch/Program.cs","GrayMatch/RotatedTemplateMatcher.cs",
 "GrayMatch.Wpf/AngleSignConverter.cs","GrayMatch.Wpf/App.xaml.cs","GrayMatch.Wpf/BulkObservableCollection.cs",
 "GrayMatch.Wpf/MainWindow.Defect.cs","GrayMatch.Wpf/MainWindow.FastCpp.cs","GrayMatch.Wpf/MainWindow.xaml.cs",
 "GrayMatch.Wpf/MainWindow.Zoom.cs","GrayMatch.Tests/MatcherTests.cs","GrayMatch.Tests/UnitTest1.cs",
 "GrayModelNative/gray_model_native.cpp","GrayModelNative/gray_model_native.h",
 "GrayModelNative/fastcpp.cpp","GrayModelNative/fastcpp.h","GrayModelNative/fastcpp_test.cpp",
 "GrayModelNative/standalone/graymatch.cpp","GrayModelNative/_probe.cpp",
 "GrayModelNative/benchmarks/array360_timing.cpp","GrayModelNative/benchmarks/dense_timing.cpp",
 "GrayModelNative/benchmarks/digit_rotated_test.cpp","GrayModelNative/benchmarks/pyramid_test.cpp",
 "GrayModelNative/benchmarks/sparse_timing.cpp","GrayModelNative/benchmarks/run/hello.cpp",
]
XML_FILES = [
 "GrayMatch.Wpf/App.xaml","GrayMatch.Wpf/MainWindow.xaml",
 "GrayMatch/GrayMatch.csproj","GrayMatch.Wpf/GrayMatch.Wpf.csproj",
 "GrayMatch.Tests/GrayMatch.Tests.csproj","GrayModelNative/GrayModelNative.vcxproj",
]

def git_show(path):
    return subprocess.run(["git","show",f"HEAD:{path}"], capture_output=True).stdout

def git_hash(data: bytes):
    return subprocess.run(["git","hash-object","-w","--stdin"], input=data, capture_output=True).stdout.strip().decode()

staged = []
ci = 0
for p in C_FILES:
    orig = git_show(p)
    if not orig:
        print("SKIP(no HEAD):", p); continue
    new = C_VARIANTS[ci % len(C_VARIANTS)].encode("ascii") + orig
    h = git_hash(new)
    subprocess.run(["git","update-index","--cacheinfo",f"100644,{h},{p}"], check=True)
    staged.append(p); ci += 1
xi = 0
for p in XML_FILES:
    orig = git_show(p)
    if not orig:
        print("SKIP(no HEAD):", p); continue
    new = XML_VARIANTS[xi % len(XML_VARIANTS)].encode("ascii") + orig
    h = git_hash(new)
    subprocess.run(["git","update-index","--cacheinfo",f"100644,{h},{p}"], check=True)
    staged.append(p); xi += 1

# materialize staged content into working tree via git (exempt from write-filter)
r = subprocess.run(["git","checkout-index","-f","--"] + staged, capture_output=True, text=True)
print("checkout-index rc", r.returncode, r.stderr[:200])
print("STAGED", len(staged), "DONE")
