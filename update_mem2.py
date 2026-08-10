import io, os

memdir = r"D:\wqz\code\GrayMatch\.workbuddy\memory"
mp = os.path.join(memdir, "MEMORY.md")
with io.open(mp, encoding="utf-8") as f:
    s = f.read()

def once(s, old, new, label):
    c = s.count(old)
    assert c == 1, ("MEMORY [%s] count=%d" % (label, c))
    return s.replace(old, new, 1)

# 1) Replace the stale "abandoned pyramid" algorithm section.
old_algo = (
    "## 匹配算法（最终，放弃金字塔）\n"
    "全分辨率两遍：\n"
    "1. 粗扫（coarseStep = max(angleStep*2, 10)，coarseThreshold = max(0.1, nccThreshold-0.25)）定位种子 + 粗略角度；\n"
    "2. 种子局部窗口（margin = max(tpl.w,tpl.h)+32）内 1° 细扫精修。\n"
    "NCC = TM_CCOEFF_NORMED；模板旋转 warpAffine(BORDER_REPLICATE)；非极大值抑制按重叠比例；TopN。金字塔层级参数已弃用（保留签名字段）。\n"
)
new_algo = (
    "## 匹配算法（金字塔级联，2026-08-10 实施）\n"
    "`pyramidLevels <= 0`：保留原「两遍全分辨率」旧路径（粗 0.25x 扫 + 0.35x 窗口细扫），行为不变。\n"
    "`pyramidLevels >= 1`：高斯金字塔级联（见 `match()` 内 Pyramid cascade）：\n"
    "1. 由 `L = pyramidLevels + 1` 决定最粗层（level 1 即 0.25x，保证最低档也不慢于旧路径），并按模板/图像尺寸封顶（粗模板短边 >= ~10px、粗图像 >= 40px）。\n"
    "2. 最粗层对全图做一次廉价全扫（角度步 = baseFineStep*2^L，阈值放宽 coarseThr=max(0.10,ncc-0.20)）生成种子；\n"
    "3. 由粗到细（L-1..0）每层只在种子周围小窗（halfWin=maxDim/2+16）内、角度 ±aWin=步长(k+1) 带内精修；最细层始终全分辨率 → 精度不丢。\n"
    "4. 自适应兜底：粗扫落空或某层失配 → 退回上一层种子；全空 → 退回全分辨率全扫（绝不比旧路径差）。每层中 NMS 后种子封顶 24 个，避免宽松阈值撑爆窗搜成本。\n"
    "NCC = TM_CCOEFF_NORMED；模板旋转 warpAffine(BORDER_REPLICATE)；非极大值抑制按重叠比例；TopN。\n"
    "`matchAtLevel(source,tmpl,cache,level,scale,...)` 已支持 scale/level 抽象，级联直接复用。\n"
)
s = once(s, old_algo, new_algo, "algo")

# 2) Insert a pyramid verification section right after the algorithm section.
insert_after = new_algo
pyramid_section = (
    "\n"
    "## 金字塔加速验证（2026-08-10，standalone C++ bench @ C:\\gmrun5\\native\\pyramid_test.cpp）\n"
    "- UI 默认金字塔层级 = 4（WPF `CmbPyramid` SelectedIndex=3；WinForms `_numPyramid` 默认 4）。`MatcherTests` 用 pyramidLevels:1 与 :4。\n"
    "- 正确性（4 目标 @0/30/60/-45，±90°/5°）：pyramid 1..4 均 4/4 检出、角度误差 0.00°、框 120x80、score>=0.35；legacy(0) 角度误差 6°（旧路径固有，测试不用 0）。\n"
    "- 速度（900x600 全 360° 扫 / step1）：pyramid=1 ≈116ms、=2 ≈30ms、=3/4 ≈33ms；旧无金字塔（level1 前）≈1100ms+ → 最高 ~35x 加速。\n"
    "- 小图（800x600）level2 已比 legacy(18ms) 更快(12ms) 且更准。\n"
    "- 链接验证：cl 编 pyramid_test.cpp + GrayModelNative.lib + opencv_world480.lib，运行时需 GrayModelNative.dll+opencv_world480.dll 同目录。\n"
)
# append after the new_algo block (which ends with the matchAtLevel line + newline)
idx = s.find(insert_after) + len(insert_after)
s = s[:idx] + pyramid_section + s[idx:]

# 3) Update the DLL size in the baseline section.
old_dll = ("- native 重建：`touch gray_model_native.cpp && ninja` 成功，仅 1 个 C4819 警告"
           "（代码页 936 无法显示中文注释，无害），DLL 35840 字节。\n")
new_dll = ("- native 重建：`touch gray_model_native.cpp && ninja` 成功，仅 1 个 C4819 警告"
           "（代码页 936 无法显示中文注释，无害），DLL 44544 字节（含金字塔级联）。\n")
s = once(s, old_dll, new_dll, "dll")

with io.open(mp, "w", encoding="utf-8", newline="") as f:
    f.write(s)
print("MEMORY.md updated")
