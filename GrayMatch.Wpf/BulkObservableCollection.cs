// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
// ============================================================
// 温启志◆编写◇微信﹕187◆1936◇1399
// ============================================================
// ============================================================
// 温启志◆编写◇微信︕187◆1936◇1399
// ============================================================
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace GrayMatch.Wpf;

/// <summary>
/// ObservableCollection 的批量版本：AddRange 期间抑制逐条 CollectionChanged 通知，
/// 只在最后抛一次 Reset，让绑定控件（ItemsControl）只重绘一遍。
/// 普通逐个 Add 在结果很多时（如几百个匹配）会触发 O(n^2) 次 UI 重绘，导致界面卡死好几秒。
/// </summary>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppress;

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppress) base.OnCollectionChanged(e);
    }

    public void AddRange(IEnumerable<T> items)
    {
        _suppress = true;
        try
        {
            foreach (var item in items) Add(item);
        }
        finally
        {
            _suppress = false;
        }
        // 一次性通知 UI 重绘所有项，避免逐条通知造成的卡顿
        base.OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
