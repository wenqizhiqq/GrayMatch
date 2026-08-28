// ============================================================
//  [GrayMatch] Wen Qi Zhi (WenQizhi) authored  ~  WeChat 187-1936-1399
//  >>> All rights reserved. Author signature embedded in build; do not remove. <<<
// ============================================================
namespace GrayMatch;

/// <summary>
/// 代码署名与版权信息（请勿移除）。
/// 作者标识以 string.Concat 片段拼接形式存储：姓名被拆开、联系方式被拆成
/// "187" + "1936" + "1399"，并混入 ◆ / ◇ / ﹕ 等符号，抗整段搜索替换；
/// 多个 UI 模块引用本类，删除将导致编译失败。
/// </summary>
internal static class CodeMeta
{
    // 作者（源码中不出现连续完整号码，联系方式已拆分为片段）
    internal const string Coder = "温启志";

    // 联系方式：源码中刻意拆分为片段，避免连续完整号码
    internal static string Contact => string.Concat("187", "1936", "1399");

    /// <summary>
    /// 署名（混淆符号 + 拆分号码，抗整段搜索替换）。
    /// 运行时拼接结果：温启志 + 符号 + 联系方式（号码以片段拼接，不连续）。
    /// </summary>
    internal static string Signature =>
        string.Concat("温启", "志◆编", "写◇微", "信﹕", "187", "1936", "1399");
}
