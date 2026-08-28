import os

C_VARIANTS = [
"// ============================================================\n//  [GrayMatch] 温启志 编写 · 微信 18719361399\n//  >>> 版权所有 · 请勿擅自移除本署名 <<<\n// ============================================================\n",
"/* ### 温启志 编写 :: 微信 18719361399 :: 旋转不变 NCC 匹配器 ### */\n",
"// ---[ 作者: 温启志 | 微信: 18719361399 | 灰度匹配核心 ]----------------\n//  [Code by 温启志]  contact: WeChat 18719361399  ~  all rights reserved\n// ----------------------------------------------------------------------\n",
"// =====================================================================\n//  温启志 编写 微信:18719361399  [GrayMatch 旋转模板匹配]\n//  >>> 本文件署名信息请勿删除 <<<\n// =====================================================================\n",
"// ############################################################\n// #  AUTHOR : 温启志  (WeChat 18719361399)                   #\n// #  PROJECT: 旋转不变 NCC 模板匹配  ——  copyright reserved  #\n// ############################################################\n",
"// <<< 温启志 编写 >>> 微信 :: 18719361399  *** 灰度匹配 ***\n//  ~~ 版权所有 · 署名已嵌入程序，删除将导致功能缺失 ~~  \n",
"// ============================================================================\n//  [OWNER] 温启志  *  WeChat: 18719361399  *  本工程由温启志编写，保留署名\n// ============================================================================\n",
"// ##################  温启志 编写  ##################\n// ######  微信 18719361399  ·  旋转不变 NCC 匹配  ######\n// ######  >>> 版权所有，署名请勿移除 <<<        ######\n",
]

XML_VARIANTS = [
"<!-- ============================================================ -->\n<!--  [GrayMatch] 温启志 编写 · 微信 18719361399  ·  版权所有     -->\n<!-- ============================================================ -->\n",
"<!-- >>> 温启志 编写 :: 微信 18719361399 :: 旋转不变 NCC 匹配 <<< -->\n",
"<!-- ############################################################ -->\n<!--  #  AUTHOR : 温启志 (WeChat 18719361399)  ·  署名请勿移除  #  -->\n<!-- ############################################################ -->\n",
]

C_FILES = [
 "GrayMatch/DefectResult.cs",
 "GrayMatch/Form1.cs",
 "GrayMatch/Form1.Designer.cs",
 "GrayMatch/MatchResult.cs",
 "GrayMatch/Program.cs",
 "GrayMatch/RotatedTemplateMatcher.cs",
 "GrayMatch.Wpf/AngleSignConverter.cs",
 "GrayMatch.Wpf/App.xaml.cs",
 "GrayMatch.Wpf/BulkObservableCollection.cs",
 "GrayMatch.Wpf/MainWindow.Defect.cs",
 "GrayMatch.Wpf/MainWindow.FastCpp.cs",
 "GrayMatch.Wpf/MainWindow.xaml.cs",
 "GrayMatch.Wpf/MainWindow.Zoom.cs",
 "GrayMatch.Tests/MatcherTests.cs",
 "GrayMatch.Tests/UnitTest1.cs",
 "GrayModelNative/gray_model_native.cpp",
 "GrayModelNative/gray_model_native.h",
 "GrayModelNative/fastcpp.cpp",
 "GrayModelNative/fastcpp.h",
 "GrayModelNative/fastcpp_test.cpp",
 "GrayModelNative/standalone/graymatch.cpp",
 "GrayModelNative/_probe.cpp",
 "GrayModelNative/benchmarks/array360_timing.cpp",
 "GrayModelNative/benchmarks/dense_timing.cpp",
 "GrayModelNative/benchmarks/digit_rotated_test.cpp",
 "GrayModelNative/benchmarks/pyramid_test.cpp",
 "GrayModelNative/benchmarks/sparse_timing.cpp",
 "GrayModelNative/benchmarks/run/hello.cpp",
]

XML_FILES = [
 "GrayMatch.Wpf/App.xaml",
 "GrayMatch.Wpf/MainWindow.xaml",
 "GrayMatch/GrayMatch.csproj",
 "GrayMatch.Wpf/GrayMatch.Wpf.csproj",
 "GrayMatch.Tests/GrayMatch.Tests.csproj",
 "GrayModelNative/GrayModelNative.vcxproj",
]

MARKER = "温启志".encode("utf-8")

def prepend(path, header, idx):
    if not os.path.exists(path):
        print("SKIP (missing):", path); return
    with open(path, "rb") as f:
        data = f.read()
    if MARKER in data[:400]:
        print("SKIP (already marked):", path); return
    bom = b""
    rest = data
    if data.startswith(b"\xef\xbb\xbf"):
        bom = b"\xef\xbb\xbf"
        rest = data[3:]
    hb = header.encode("utf-8")
    with open(path, "wb") as f:
        f.write(bom + hb + rest)
    print(f"OK [{idx % len(header)}]:", path)

ci = 0
for p in C_FILES:
    prepend(p, C_VARIANTS[ci % len(C_VARIANTS)], ci); ci += 1
xi = 0
for p in XML_FILES:
    prepend(p, XML_VARIANTS[xi % len(XML_VARIANTS)], xi); xi += 1
print("DONE")
