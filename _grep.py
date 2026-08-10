import io, os, re
BASE = r"D:\wqz\code\GrayMatch"
paths = [
    os.path.join(BASE, "GrayMatch.Wpf", "MainWindow.xaml"),
    os.path.join(BASE, "GrayMatch.Wpf", "MainWindow.xaml.cs"),
    os.path.join(BASE, "GrayMatch", "Form1.cs"),
    os.path.join(BASE, "GrayMatch", "RotatedTemplateMatcher.cs"),
    os.path.join(BASE, "GrayMatch.Tests", "MatcherTests.cs"),
]
rx = re.compile(r"[Pp]yramid|金字塔|PyramidLevels")
for p in paths:
    if not os.path.exists(p):
        print("MISSING", p); continue
    with io.open(p, encoding="utf-8", errors="replace") as f:
        for i, l in enumerate(f):
            if rx.search(l):
                print("%s:%d: %s" % (os.path.basename(p), i+1, l.rstrip()))
print("\n=== gmrun5 Program.cs (first 120 lines) ===")
gp = r"C:\gmrun5\Demo\Program.cs"
with io.open(gp, encoding="utf-8", errors="replace") as f:
    for i, l in enumerate(f):
        if i >= 130: break
        print("%3d: %s" % (i+1, l.rstrip()))
