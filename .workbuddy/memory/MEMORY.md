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

## 验证基线
全 3 测试 PASS：Can_Detect 32ms/4；Benchmark（全 360°）33ms/4；WPF 0 警告 0 错误。
