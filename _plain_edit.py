import io

def edit(path, reps, label):
    s = io.open(path, 'r', encoding='utf-8').read()
    for old, new in reps:
        cnt = s.count(old)
        if cnt != 1:
            print(f'[WARN] {label}: expected 1 of:\n  {old!r}\n  found {cnt}')
        s = s.replace(old, new)
    io.open(path, 'w', encoding='utf-8').write(s)
    print(f'[OK] wrote {path}')

xaml_reps = [
    # 使用说明：修正「先点创建模板，再拖框」的正确顺序
    ('Text="1. 点「打开图像」选一张要检测的照片"',
     'Text="1. 点「打开图片」选一张要检测的照片"'),
    ('Text="2. 在照片上按住鼠标拖一个框，框住你想找的目标"',
     'Text="2. 点「创建模板」，进入画框模式"'),
    ('Text="3. 点「创建模板」，把这个框存成比对模板"',
     'Text="3. 在照片上按住鼠标拖一个框，框住你想找的目标（松手即存为模板）"'),
    ('Text="5. 点「执行匹配」，程序会在图上找出所有相同目标并画绿框"',
     'Text="5. 点「开始查找」，程序会在图上找出所有相同目标并画绿框"'),

    ('Content="打开图像"', 'Content="打开图片"'),
    ('ToolTip="点这里选择一张要检测的照片（支持 bmp、jpg、png 格式）"',
     'ToolTip="点这里选一张要检测的图片（bmp / jpg / png 都支持）"'),
    ('ToolTip="先在照片上用鼠标拖一个框选中目标，再点这个按钮把它做成模板"',
     'ToolTip="点这个按钮进入画框模式，然后在图片上拖一个框选中要找的目标（松手即存为模板）"'),
    ('Content="执行匹配"', 'Content="开始查找"'),
    ('ToolTip="设置好角度、阈值等参数后，执行旋转NCC模板匹配"',
     'ToolTip="设置好上面的参数后，点这里在整张图片里找出所有相同的目标，并用绿框标出"'),
    ('ToolTip="清空匹配框、表格结果，保留原图和模板"',
     'ToolTip="去掉图上所有的绿框和结果，原图和模板都会保留"'),
    ('Content="检测缺陷（模板比对）"', 'Content="顺便检查缺陷（污渍/划痕/缺料）"'),
    ('ToolTip="对每个匹配目标，逆旋转对齐到模板做差值图，检测污渍/异物、划痕、缺料/破损、亮度/对比度异常，红色半透明标注。"',
     'ToolTip="勾选后，除了找目标，还会在每个目标上检查有没有污渍、划痕、缺料等问题，并用红框标出"'),
    ('Content="恢复默认"', 'Content="恢复默认参数"'),
    ('ToolTip="把上面所有缺陷参数恢复为默认值"',
     'ToolTip="把上面的缺陷参数恢复成默认值"'),
]

cs_reps = [
    ('StatusText = "就绪";', 'StatusText = "已经准备好了，可以开始";'),
    ('StatusText = $"已加载图像: {dlg.FileName}";', 'StatusText = $"图片已读入：{dlg.FileName}";'),
    ('StatusText = "在图像上拖拽绘制模板区域";',
     'StatusText = "在图片上按住鼠标拖一个框，框住要找的目标（松手即成为模板）";'),
    ('StatusText = "正在匹配...";', 'StatusText = "正在查找，请稍候...";'),
    ('StatusText = "匹配已取消";', 'StatusText = "已取消查找";'),
    ('StatusText = "匹配失败";', 'StatusText = "查找失败了";'),
    ('StatusText = $"匹配完成: {results.Count} 个结果, 缺陷 {defects.Count} 处, 匹配耗时 {_matcher.LastMatchMs:F1} ms";',
     'StatusText = $"查找完成：共找到 {results.Count} 个目标，其中 {defects.Count} 处有缺陷，用时 {_matcher.LastMatchMs:F1} 毫秒";'),
    ('StatusText = $"匹配完成: {results.Count} 个结果, 匹配耗时 {_matcher.LastMatchMs:F1} ms (阈={threshold}, 重叠={overlap}, TopN={topN})";',
     'StatusText = $"查找完成：共找到 {results.Count} 个目标，用时 {_matcher.LastMatchMs:F1} 毫秒";'),
    ('StatusText = "结果已清除";', 'StatusText = "结果已清空，绿框已去掉";'),
    ('StatusText = _defectEnabled ? "缺陷检测已启用（模板比对）" : "缺陷检测已关闭";',
     'StatusText = _defectEnabled ? "已开启缺陷检查" : "已关闭缺陷检查";'),
    ('StatusText = "模板区域太小";', 'StatusText = "框选的模板太小了，请框大一点";'),
    ('StatusText = $"模板已创建: {w}x{h}";', 'StatusText = $"模板已做好，大小 {w}×{h}";'),
]

edit('GrayMatch.Wpf/MainWindow.xaml', xaml_reps, 'xaml')
edit('GrayMatch.Wpf/MainWindow.xaml.cs', cs_reps, 'cs')
