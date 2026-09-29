# -*- coding: utf-8 -*-
"""改成“固定自适应、无缩放无拖动”的显示，并顺便调重叠抑制默认值。"""
import os, sys

root = r'D:\wqz\code\GrayMatch'
ok_all = True


def load(p):
    return open(p, 'rb').read().decode('utf-8')


def save(p, s):
    open(p, 'wb').write(s.encode('utf-8'))


def rep1(s, old, new, tag):
    global ok_all
    n = s.count(old)
    if n != 1:
        print(f'  !! {tag}: count={n}')
        ok_all = False
        return s
    print(f'  ok {tag}')
    return s.replace(old, new, 1)


# ---------------- 1) MainWindow.Zoom.cs：去掉缩放/平移 ----------------
src = load(os.path.join(root, '_new_zoom2.cs.txt')).replace('\r\n', '\n').replace('\n', '\r\n')
save(os.path.join(root, 'GrayMatch.Wpf', 'MainWindow.Zoom.cs'), src)
print('Zoom.cs -> 固定自适应版')

# ---------------- 2) XAML：Viewbox + 去掉鼠标交互 ----------------
xp = os.path.join(root, 'GrayMatch.Wpf', 'MainWindow.xaml')
t = load(xp)

old_open = (
'                <Grid x:Name="ImageViewport"\r\n'
'                      Background="#1D1D1F"\r\n'
'                      ClipToBounds="True"\r\n'
'                      MouseWheel="ImageArea_MouseWheel"\r\n'
'                      MouseDown="ImageArea_MouseDown"\r\n'
'                      MouseMove="ImageArea_MouseMove"\r\n'
'                      MouseUp="ImageArea_MouseUp"\r\n'
'                      LostMouseCapture="ImageArea_LostMouseCapture"\r\n'
'                      Loaded="ImageViewer_Loaded">\r\n')
new_open = (
'                <!-- 固定自适应显示：Viewbox 让整幅图始终铺满并居中，不提供缩放/平移 -->\r\n'
'                <Viewbox x:Name="ImageViewport"\r\n'
'                         Stretch="Uniform"\r\n'
'                         StretchDirection="Both"\r\n'
'                         ClipToBounds="True"\r\n'
'                         Loaded="ImageViewer_Loaded">\r\n')
t = rep1(t, old_open, new_open, 'xaml-viewbox-open')

old_tf = (
'                        <!--\r\n'
'                            缩放/平移全部走渲染变换：逻辑尺寸 = 图像显示尺寸，\r\n'
'                            与视口滚动/居中布局完全解耦，中键拖动不会被 ScrollViewer 钳死。\r\n'
'                            变换原点在左上角 (0,0)，即“视口坐标 = 图像坐标 × 缩放 + 平移”。\r\n'
'                        -->\r\n'
'                        <Grid.RenderTransform>\r\n'
'                            <TransformGroup>\r\n'
'                                <ScaleTransform x:Name="ImageScaleTransform" ScaleX="1" ScaleY="1"/>\r\n'
'                                <TranslateTransform x:Name="ImageTranslateTransform" X="0" Y="0"/>\r\n'
'                            </TransformGroup>\r\n'
'                        </Grid.RenderTransform>\r\n')
t = rep1(t, old_tf, '', 'xaml-transform-removed')

old_close = '                </Grid>\r\n                <TextBlock Text="滚轮缩放 · 中键拖动平移 · 双击复位"'
new_close = '                </Viewbox>\r\n                <TextBlock Text="整幅自适应显示 · 拖框选模板"'
t = rep1(t, old_close, new_close, 'xaml-viewbox-close')

# 密集模式默认勾选 + 最大允许重叠默认 0.10
t = rep1(t,
         '<CheckBox x:Name="ChkDense" Content="密集模式（适合规则阵列）" IsChecked="False"',
         '<CheckBox x:Name="ChkDense" Content="密集模式（适合规则阵列）" IsChecked="True"',
         'xaml-dense-default')
t = rep1(t,
         '<Slider x:Name="SldOverlap" Style="{StaticResource ModernSlider}" Minimum="0" Maximum="1" Value="0.25" TickFrequency="0.01"',
         '<Slider x:Name="SldOverlap" Style="{StaticResource ModernSlider}" Minimum="0" Maximum="1" Value="0.10" TickFrequency="0.01"',
         'xaml-overlap-default')
t = rep1(t,
         '<TextBlock x:Name="TbOverlapVal" Text="0.25" Style="{StaticResource ValueText}"/>',
         '<TextBlock x:Name="TbOverlapVal" Text="0.10" Style="{StaticResource ValueText}"/>',
         'xaml-overlap-label')
t = rep1(t,
         'ToolTip="两个候选框重叠超过该比例时只保留分数高的。越大越宽松。"/>',
         'ToolTip="两个候选框重叠超过该比例时只保留分数高的。默认 0.10：只有几乎同一个位置的重复框才被去掉，互相压住/挨着的目标仍会各自检出；调大更宽松。"/>',
         'xaml-overlap-tip')
t = rep1(t,
         '<TextBlock Text="默认不勾选=快速; 规则阵列/密集图才勾选=全检出(较慢)" Style="{StaticResource CompactText}" Foreground="#A1A1A6" Margin="0,0,0,2"/>',
         '<TextBlock Text="默认勾选=全检出（重叠/密集目标都各自检出）; 取消=更快但可能漏检" Style="{StaticResource CompactText}" Foreground="#A1A1A6" Margin="0,0,0,2"/>',
         'xaml-dense-hint')
t = rep1(t,
         'ToolTip="将上方所有匹配参数恢复为推荐默认值（-180~180°, 步长1, 阈值0.50, 重叠0.25, TopN10, 金字塔4）"',
         'ToolTip="将上方所有匹配参数恢复为推荐默认值（-180~180°, 步长1, 阈值0.80, 重叠0.10, TopN200, 金字塔4, 密集模式勾选）"',
         'xaml-defaults-tip')

save(xp, t)

# ---------------- 3) MainWindow.xaml.cs：默认重叠 0.10 ----------------
cs = os.path.join(root, 'GrayMatch.Wpf', 'MainWindow.xaml.cs')
c = load(cs)
c = rep1(c, '        public double Overlap { get; set; } = 0.25;',
         '        public double Overlap { get; set; } = 0.10;', 'cs-overlap-default')
c = rep1(c, '            SldOverlap.Value = 0.25;', '            SldOverlap.Value = 0.10;', 'cs-overlap-btn')
c = rep1(c, '                Overlap = SldOverlap?.Value ?? 0.25,',
         '                Overlap = SldOverlap?.Value ?? 0.10,', 'cs-overlap-fallback')
save(cs, c)

print('DONE' if ok_all else 'SOME FAILED')
sys.exit(0 if ok_all else 1)
