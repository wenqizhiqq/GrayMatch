src = 'GrayMatch.Wpf/MainWindow.xaml.cs'
with open(src, 'rb') as f:
    data = f.read()

replacements = [
    (
        b'        TbAngleStart.TextChanged += (_, _) => { UpdateInfluenceFactors(); SaveSettings(); };\r\n        TbAngleEnd.TextChanged += (_, _) => { UpdateInfluenceFactors(); SaveSettings(); };\r\n        TbAngleStep.TextChanged += (_, _) => { UpdateInfluenceFactors(); SaveSettings(); };\r\n        TbThreshold.TextChanged += (_, _) => { UpdateInfluenceFactors(); SaveSettings(); };\r\n        TbOverlap.TextChanged += (_, _) => { UpdateInfluenceFactors(); SaveSettings(); };\r\n        TbTopN.TextChanged += (_, _) => { UpdateInfluenceFactors(); SaveSettings(); };\r\n        CmbPyramid.SelectionChanged += (_, _) => { UpdateInfluenceFactors(); SaveSettings(); };',
        b'        SldAngleStart.ValueChanged += MatchParam_Changed;\r\n        SldAngleEnd.ValueChanged += MatchParam_Changed;\r\n        SldAngleStep.ValueChanged += MatchParam_Changed;\r\n        SldThreshold.ValueChanged += MatchParam_Changed;\r\n        SldOverlap.ValueChanged += MatchParam_Changed;\r\n        SldTopN.ValueChanged += MatchParam_Changed;\r\n        SldPyramid.ValueChanged += MatchParam_Changed;'
    ),
    (
        b'    private void ChkDense_Changed(object sender, RoutedEventArgs e)\r\n    {\r\n        SaveSettings();\r\n    }\r\n\r\n    #endregion',
        b'    private void ChkDense_Changed(object sender, RoutedEventArgs e)\r\n    {\r\n        SaveSettings();\r\n    }\r\n\r\n    private void MatchParam_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)\r\n    {\r\n        if (TbAngleStartVal != null) TbAngleStartVal.Text = SldAngleStart.Value.ToString("0");\r\n        if (TbAngleEndVal != null) TbAngleEndVal.Text = SldAngleEnd.Value.ToString("0");\r\n        if (TbAngleStepVal != null) TbAngleStepVal.Text = SldAngleStep.Value.ToString("0.0");\r\n        if (TbThresholdVal != null) TbThresholdVal.Text = SldThreshold.Value.ToString("0.00");\r\n        if (TbOverlapVal != null) TbOverlapVal.Text = SldOverlap.Value.ToString("0.00");\r\n        if (TbTopNVal != null) TbTopNVal.Text = SldTopN.Value.ToString("0");\r\n        if (TbPyramidVal != null) TbPyramidVal.Text = SldPyramid.Value.ToString("0");\r\n        UpdateInfluenceFactors();\r\n        SaveSettings();\r\n    }\r\n\r\n    #endregion'
    ),
    (
        b'        if (!int.TryParse((CmbPyramid.SelectedItem as ComboBoxItem)?.Content?.ToString(), out int pyramid))\r\n            pyramid = 4;\r\n\r\n        double start = Parse(TbAngleStart.Text, -180);\r\n        double end = Parse(TbAngleEnd.Text, 180);\r\n        double step = Parse(TbAngleStep.Text, 1);\r\n        double threshold = Parse(TbThreshold.Text, 0.5);\r\n        double overlap = Parse(TbOverlap.Text, 0.25);\r\n        int topN = (int)Parse(TbTopN.Text, 200);',
        b'        int pyramid = (int)System.Math.Round(SldPyramid.Value);\r\n\r\n        double start = SldAngleStart.Value;\r\n        double end = SldAngleEnd.Value;\r\n        double step = SldAngleStep.Value;\r\n        double threshold = SldThreshold.Value;\r\n        double overlap = SldOverlap.Value;\r\n        int topN = (int)System.Math.Round(SldTopN.Value);'
    ),
    (
        b'        public string AngleStart { get; set; } = "-180";\r\n        public string AngleEnd { get; set; } = "180";\r\n        public string AngleStep { get; set; } = "1";\r\n        public string Threshold { get; set; } = "0.5";\r\n        public string Overlap { get; set; } = "0.25";\r\n        public string TopN { get; set; } = "200";\r\n        public int PyramidIndex { get; set; } = 3;',
        b'        public double AngleStart { get; set; } = -45;\r\n        public double AngleEnd { get; set; } = 45;\r\n        public double AngleStep { get; set; } = 2;\r\n        public double Threshold { get; set; } = 0.30;\r\n        public double Overlap { get; set; } = 0.25;\r\n        public double TopN { get; set; } = 20;\r\n        public int PyramidLevel { get; set; } = 3;'
    ),
    (
        b'                AngleStart = TbAngleStart?.Text ?? "-180",\r\n                AngleEnd = TbAngleEnd?.Text ?? "180",\r\n                AngleStep = TbAngleStep?.Text ?? "1",\r\n                Threshold = TbThreshold?.Text ?? "0.5",\r\n                Overlap = TbOverlap?.Text ?? "0.25",\r\n                TopN = TbTopN?.Text ?? "200",\r\n                PyramidIndex = CmbPyramid?.SelectedIndex ?? 3,',
        b'                AngleStart = SldAngleStart?.Value ?? -45,\r\n                AngleEnd = SldAngleEnd?.Value ?? 45,\r\n                AngleStep = SldAngleStep?.Value ?? 2,\r\n                Threshold = SldThreshold?.Value ?? 0.30,\r\n                Overlap = SldOverlap?.Value ?? 0.25,\r\n                TopN = SldTopN?.Value ?? 20,\r\n                PyramidLevel = (int)System.Math.Round(SldPyramid?.Value ?? 3),'
    ),
    (
        b'            TbAngleStart.Text = s.AngleStart;\r\n            TbAngleEnd.Text = s.AngleEnd;\r\n            TbAngleStep.Text = s.AngleStep;\r\n            TbThreshold.Text = s.Threshold;\r\n            TbOverlap.Text = s.Overlap;\r\n            TbTopN.Text = s.TopN;\r\n            CmbPyramid.SelectedIndex = s.PyramidIndex;',
        b'            SldAngleStart.Value = s.AngleStart;\r\n            SldAngleEnd.Value = s.AngleEnd;\r\n            SldAngleStep.Value = s.AngleStep;\r\n            SldThreshold.Value = s.Threshold;\r\n            SldOverlap.Value = s.Overlap;\r\n            SldTopN.Value = s.TopN;\r\n            SldPyramid.Value = s.PyramidLevel;\r\n            TbAngleStartVal.Text = s.AngleStart.ToString("0");\r\n            TbAngleEndVal.Text = s.AngleEnd.ToString("0");\r\n            TbAngleStepVal.Text = s.AngleStep.ToString("0.0");\r\n            TbThresholdVal.Text = s.Threshold.ToString("0.00");\r\n            TbOverlapVal.Text = s.Overlap.ToString("0.00");\r\n            TbTopNVal.Text = s.TopN.ToString("0");\r\n            TbPyramidVal.Text = s.PyramidLevel.ToString("0");'
    ),
    (
        b'        double start = Parse(TbAngleStart.Text, -180);\r\n        double end = Parse(TbAngleEnd.Text, 180);\r\n        double step = Parse(TbAngleStep.Text, 1);\r\n        int pyramid = 4;\r\n        if (int.TryParse((CmbPyramid.SelectedItem as ComboBoxItem)?.Content?.ToString(), out int p)) pyramid = p;',
        b'        double start = SldAngleStart?.Value ?? -45;\r\n        double end = SldAngleEnd?.Value ?? 45;\r\n        double step = SldAngleStep?.Value ?? 2;\r\n        int pyramid = (int)System.Math.Round(SldPyramid?.Value ?? 3);'
    ),
]

for i, (old, new) in enumerate(replacements):
    if old not in data:
        print(f'FAIL pattern {i} not found')
        continue
    data = data.replace(old, new, 1)
    print(f'OK pattern {i}')

with open(src + '.new', 'wb') as f:
    f.write(data)
print('wrote', src + '.new')
