src = 'GrayMatch.Wpf/MainWindow.Defect.cs'
with open(src, 'rb') as f:
    data = f.read()

replacements = [
    (
        b'private void DefectParam_Changed(object sender, TextChangedEventArgs e) => ApplyDefectParams();',
        b'private void DefectParam_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyDefectParams();'
    ),
    (
        b'        if (TbDefectDiff == null || TbDefectEdgeTol == null || TbDefectEdgeGrad == null ||\n            TbDefectErode == null || TbDefectDilate == null || TbDefectMinArea == null ||\n            TbDefectGlobal == null)\n            return;\n\n        double minAreaPct = Clamp(Parse(TbDefectMinArea.Text, 0.4), 0.001, 50);\n\n        _matcher.DefectOptions = new DefectOptions\n        {\n            DiffThreshold = Clamp(Parse(TbDefectDiff.Text, 45), 1, 254),\n            MinAreaFrac = minAreaPct / 100.0,\n            GlobalBrightnessThresh = Clamp(Parse(TbDefectGlobal.Text, 28), 1, 254),\n            EdgeTolerance = (int)Clamp(Parse(TbDefectEdgeTol.Text, 0), 0, 30),\n            EdgeGradThresh = Clamp(Parse(TbDefectEdgeGrad.Text, 30), 1, 254),\n            ErodeSize = (int)Clamp(Parse(TbDefectErode.Text, 2), 0, 15),\n            DilateSize = (int)Clamp(Parse(TbDefectDilate.Text, 3), 0, 15),\n        };',
        b'        if (SldDefectDiff == null || SldDefectEdgeTol == null || SldDefectEdgeGrad == null ||\n            SldDefectErode == null || SldDefectDilate == null || SldDefectMinArea == null ||\n            SldDefectGlobal == null)\n            return;\n\n        double minAreaPct = Clamp(SldDefectMinArea.Value, 0.001, 50);\n\n        _matcher.DefectOptions = new DefectOptions\n        {\n            DiffThreshold = (int)Clamp(SldDefectDiff.Value, 1, 254),\n            MinAreaFrac = minAreaPct / 100.0,\n            GlobalBrightnessThresh = (int)Clamp(SldDefectGlobal.Value, 1, 254),\n            EdgeTolerance = (int)Clamp(SldDefectEdgeTol.Value, 0, 30),\n            EdgeGradThresh = (int)Clamp(SldDefectEdgeGrad.Value, 1, 254),\n            ErodeSize = (int)Clamp(SldDefectErode.Value, 0, 15),\n            DilateSize = (int)Clamp(SldDefectDilate.Value, 0, 15),\n        };\n\n        if (TbDefectDiffVal != null) TbDefectDiffVal.Text = ((int)SldDefectDiff.Value).ToString();\n        if (TbDefectEdgeTolVal != null) TbDefectEdgeTolVal.Text = ((int)SldDefectEdgeTol.Value).ToString();\n        if (TbDefectEdgeGradVal != null) TbDefectEdgeGradVal.Text = ((int)SldDefectEdgeGrad.Value).ToString();\n        if (TbDefectErodeVal != null) TbDefectErodeVal.Text = ((int)SldDefectErode.Value).ToString();\n        if (TbDefectDilateVal != null) TbDefectDilateVal.Text = ((int)SldDefectDilate.Value).ToString();\n        if (TbDefectMinAreaVal != null) TbDefectMinAreaVal.Text = SldDefectMinArea.Value.ToString("0.00");\n        if (TbDefectGlobalVal != null) TbDefectGlobalVal.Text = ((int)SldDefectGlobal.Value).ToString();'
    ),
    (
        b'        TbDefectDiff.Text = DefDiff;\n        TbDefectEdgeTol.Text = DefEdgeTol;\n        TbDefectEdgeGrad.Text = DefEdgeGrad;\n        TbDefectErode.Text = DefErode;\n        TbDefectDilate.Text = DefDilate;\n        TbDefectMinArea.Text = DefMinAreaPct;\n        TbDefectGlobal.Text = DefGlobal;',
        b'        SldDefectDiff.Value = 45;\n        SldDefectEdgeTol.Value = 0;\n        SldDefectEdgeGrad.Value = 30;\n        SldDefectErode.Value = 2;\n        SldDefectDilate.Value = 3;\n        SldDefectMinArea.Value = 0.4;\n        SldDefectGlobal.Value = 28;'
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
