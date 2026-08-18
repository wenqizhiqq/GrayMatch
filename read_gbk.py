import sys
sys.stdout.reconfigure(encoding='utf-8')
with open(r'GrayMatch/RotatedTemplateMatcher.cs','r',encoding='gbk',errors='replace') as f:
    print(f.read())
