import io, os

memdir = r"D:\wqz\code\GrayMatch\.workbuddy\memory"
os.makedirs(memdir, exist_ok=True)

# ---------- daily log ----------
logp = os.path.join(memdir, "2026-08-08.md")
daily = (
"# 2026-08-08 工作日志\n\n"
"## 移除形状匹配（用户再次要求）\n"
"- 用户：\"还是把形状匹配删除吧\" —— 撤销上一轮加回的形状匹配（matchMode=1）。\n"
"- 清除位置：native gray_model_native.cpp/h（删 gradientMagnitude / bool grad / gradMode / matchMode）；\n"
"  RotatedTemplateMatcher.cs（删 MatchMode 属性、Match 与 gm_match P/Invoke 的 matchMode 参数）；\n"
"  MainWindow.xaml（删 匹配方式 group + ChkShapeMode）；MainWindow.xaml.cs（删 IsShapeMode）；\n"
"  Form1.cs（删 _chkShape 复选框及调用）；MatcherTests.cs（两处 matchMode: 0 命名参数）。\n"
"- 当前架构回归纯灰度 NCC，C API 不再有 matchMode。\n"
"- native DLL 用 ninja 重建成功（35840 字节，原 37376）；仅 1 个 C4819 警告（代码页 936 无法显示中文注释，无害）。\n\n"
"## 关键事故：genie-trash 把 .h/.cs 写成二进制乱码\n"
"- 开工时 gray_model_native.h、RotatedTemplateMatcher.cs 等被安全删除钩子损坏为二进制垃圾，\n"
"  但 git status 仍显示干净、git show HEAD:<relpath> 返回干净源码。\n"
"- 恢复法：git show HEAD:<relpath> 取 blob，用 Python io.open(p,'w',encoding='utf-8') 写回；\n"
"  已恢复 7 个目标文件后再做编辑。\n"
"- 教训：改 .h/.cs 前若 Read 报\"二进制\"或 Python 读出乱码，先 git show HEAD: 确认是否被钩子损坏，勿在乱码上改。\n"
)
with io.open(logp, 'w', encoding='utf-8', newline='') as f:
    f.write(daily)
print("daily log written:", logp)

# ---------- MEMORY.md ----------
mp = os.path.join(memdir, "MEMORY.md")
with io.open(mp, encoding='utf-8') as f:
    s = f.read()

def once(s, old, new, label):
    c = s.count(old)
    assert c == 1, "MEMORY [%s] count=%d" % (label, c)
    return s.replace(old, new, 1)

s = once(s,
    "## 形状匹配（当前启用，matchMode=1）\n灰度/形状双模式共用同一套两遍流程",
    "## 形状匹配（已删除 —— 2026-08-08 用户再次要求移除）\n"
    "> 历史：用户反复横跳（不需要→需要→又删除）。当前代码是**纯灰度 NCC**，无 matchMode 参数、\n"
    "gradientMagnitude、ChkShapeMode/IsShapeMode/_chkShape、MatchMode 属性。\n"
    "> 若需重新加回，下面是可复用要点：\n"
    "灰度/形状双模式共用同一套两遍流程",
    "shape header")

s = once(s,
    "- 效果对比（demo，4 目标）：正常光照 灰度 4/4 vs 形状 4/4；强光照梯度+局部反相 灰度 **3/4**（漏检反相目标）vs 形状 **4/4**。耗时两者都约 20 ms。\n\n## VS 2019+ CS0102 故障排除",
    "- 效果对比（demo，4 目标）：正常光照 灰度 4/4 vs 形状 4/4；强光照梯度+局部反相 灰度 **3/4**（漏检反相目标）vs 形状 **4/4**。耗时两者都约 20 ms。\n\n"
    "## 沙箱 genie-trash 把 .h/.cs 写成二进制乱码（2026-08-08 实测）\n"
    "- 安全删除钩子会把源文件损坏为二进制垃圾（头部是非文本字节，Python 读出来是乱码），但 `git status` 仍显示干净、`git show HEAD:<relpath>` 返回干净源码。\n"
    "- **恢复**：`git show HEAD:<relpath>` 取 blob -> Python `io.open(p,'w',encoding='utf-8')` 写回。改 .h/.cs 前若 Read 报\"二进制\"或 Python 读出乱码，先 `git show HEAD:` 确认是否被钩子损坏，勿在乱码上改。\n"
    "- 另：`rm` 被钩子 fail-closed 拒绝，删工作区内辅助脚本/日志会被拦，需用户在正常环境手动删。\n\n"
    "## VS 2019+ CS0102 故障排除",
    "shape recovery section")

s = once(s,
    "## 验证基线（2026-08-08）",
    "## 验证基线（2026-08-08；形状匹配已删除，现为纯灰度 NCC）\n"
    "- native 重建：`touch gray_model_native.cpp && ninja` 成功，仅 1 个 C4819 警告（代码页 936 无法显示中文注释，无害），DLL 35840 字节。\n"
    "- C# 全量 build/test 须在用户正常环境执行（沙箱 genie-trash 冻结 obj/bin 无法再生）；建议关闭 VS 删所有 obj*/bin* 后 Rebuild。",
    "baseline header")

with io.open(mp, 'w', encoding='utf-8', newline='') as f:
    f.write(s)
print("MEMORY.md updated")
