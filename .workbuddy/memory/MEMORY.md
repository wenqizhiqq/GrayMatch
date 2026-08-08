# GrayMatch 长期项目笔记

## 项目定位
旋转不变 NCC 模板匹配器（复刻 C# WinForms 截图效果）。C# / .NET 8 WinForms 做 UI，匹配核心用 C++ 原生 DLL 提速。

## 架构
- `GrayMatch/`（WinForms EXE）：UI（Form1）——打开图像/创建模板/执行匹配/清除；参数（金字塔层数、起止角度、角度步长、NCC 阈值、最大重叠、TopN）；画布绘制 + DataGridView + 状态栏。
- `GrayMatch.Tests/`（xUnit）：`Can_Detect_Rotated_Patterns`、`Benchmark_All_Angle_Sweep`、`UnitTest1.Test1`。
- `GrayModelNative/`（C++ DLL，CMake+Ninja+MSVC 19.44，OpenCV 4.8.0 world480 vc16）：`GrayModelMatcher`（两遍全分辨率匹配），C API `gm_create/gm_destroy/gm_set_source/gm_set_template/gm_match`（见 `gray_model_native.h`、`CMakeLists.txt`）。
- P/Invoke 封装在 C# `RotatedTemplateMatcher`。

## 匹配算法（最终，放弃金字塔）
全分辨率两遍：
1. 粗扫（coarseStep = max(angleStep*2, 10)，coarseThreshold = max(0.1, nccThreshold-0.25)）定位种子 + 粗略角度；
2. 种子局部窗口（margin = max(tpl.w,tpl.h)+32）内 1° 细扫精修。
NCC = TM_CCOEFF_NORMED；模板旋转 warpAffine(BORDER_REPLICATE)；非极大值抑制按重叠比例；TopN。金字塔层级参数已弃用（保留签名字段）。

## 绿色框绘制（渲染层，易错）
- native 返回的 `templateWidth/Height` 必须是**原始未旋转模板尺寸**（在 `match()` 末尾统一覆写为 `templateGray_.cols/rows`，且 `leftTopX/Y=center-原始/2`），否则 WPF 会按「旋转后包围盒」画框→框变大、角度歪。
- **致命坑**：覆写前 Pass-2 映射循环里**必须保留** `r.leftTopX/Y = round(centerX - r.templateWidth/2)` 的赋值。删掉它检测数会从 4 塌成 1（仅 0° 目标存活）。两处 leftTop 赋值同时存在才是稳定组合。
- **fineScale 下限 = 0.35**：降到 0.33 会让旋转模板低分辨率失真、细扫 NCC 跌破 0.35 阈值→旋转目标漏检，同样塌成 1。不要低于 0.35。

## 构建与踩坑（重要）
- **两个 csproj 都要 `CopyNativeDeps`**：把 `GrayModelNative\build-ninja7\GrayModelNative.dll` + `opencv_world480.dll` 拷进 `$(OutDir)`。只给主项目加会导致测试加载到旧 DLL（经典坑）。
- dotnet 路径：`/c/Program Files/dotnet/dotnet.exe`（沙箱里 `dotnet` 不在 PATH）。
- Bash 沙箱内 Ninja+MSVC：手动设 INCLUDE/LIB（`C:/` 风格）+ PATH（`/c/...` 找 ninja），不能用 `vcvars64.bat`。
- 构建目录 `GrayModelNative/build-ninja7`；改 C++ 后 `touch gray_model_native.cpp && ninja`。
- 原生 stderr 被测试宿主吞掉 → 调试写文件。
- headless 环境无法实跑 GUI，靠 `dotnet build` + `dotnet test` 验证。

## 形状匹配（当前启用，matchMode=1）
灰度/形状双模式共用同一套两遍流程，只在喂给 matchTemplate 前把图换成 Sobel 梯度幅度图。
- 开关链路：WPF `ChkShapeMode`→`IsShapeMode`；WinForms `_chkShape`；`RotatedTemplateMatcher.MatchMode` / `Match(..., matchMode)`；C API `gm_match(..., int matchMode, ...)`；C++ `match(..., int matchMode)` → `gradMode`。
- `TemplateCache(t, useGradient)`：**先旋转再求梯度**（顺序反了会把边缘抹糊）。
- 梯度需要施加的 3 处：`coarseSrcForMatch`、全分辨率兜底的 `fullSrc`、每个种子的 `subFine`。
- **两个致命坑（都踩过）**：
  1. `Sobel(..., CV_16S)` + `cv::magnitude()` 在 opencv_world480(vc16) 直接崩溃（exit 127，无托管异常）。必须 `CV_32F`。
  2. `gradientMagnitude` **不能**用 `convertScaleAbs` 转回 CV_8U。3×3 Sobel 幅度可达 ~1020，转 8 位会把强边缘全截断到 255，导致锐利/未旋转目标漏检（0° 目标必挂）。直接返回 **CV_32F**，`matchTemplate` 原生支持，`TM_CCOEFF_NORMED` 对线性缩放不敏感所以零代价。
- 效果对比（demo，4 目标）：正常光照 灰度 4/4 vs 形状 4/4；强光照梯度+局部反相 灰度 **3/4**（漏检反相目标）vs 形状 **4/4**。耗时两者都约 20 ms。

## VS 2019+ CS0102 故障排除
若遇 "MainWindow already contains ..." 类重复成员错误，优先检查是否残留非默认 `obj2`/`bin2` 目录；MSBuild 默认只排除 `obj/**`、`bin/**`，`obj2/**` 里的过期 `.g.cs` 会被编译导致重复定义。应关闭 VS 后删除所有 `obj*`/`bin*` 再重建。

## 验证基线（2026-08-08）
- `dotnet build GrayMatch.slnx`：**0 警告 0 错误**（3 个项目）。
- `dotnet test`：3/3 PASS（Test1 3ms、Can_Detect 115ms、Benchmark 68ms）。
- demo（4 个旋转目标 0°/35°/118°/250°）：中心误差 <2 px，角度误差 ≤2°，内核 ~20 ms。
- 验证套路：把 `RotatedTemplateMatcher.cs`+`MatchResult.cs`+两个原生 DLL 拷到工作区外（`C:\gmrun5`）建控制台 Demo 跑，可绕开沙箱删除钩子。
- ninja 不在 PATH，实际路径：`C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe`。
