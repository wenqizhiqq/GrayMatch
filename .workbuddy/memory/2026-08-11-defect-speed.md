## 缺陷检测：提速 + 框过大修复（2026-08-11 续）

用户反馈：「划痕的显示太大了吧，而且你需要提速」。

### 1) 提速：目标驱动的单次 warp（58x ~ 620x）
旧路径每个匹配都 WarpAffine(srcGray, srcRot, rotMat, srcGray.Size()) warp 整幅图再 GetRectSubPix 裁剪 -> O(图像面积)/匹配。
新路径把「摆正整图」+「裁模板窗」折叠成一次 dsize=(tw,th) 的仿射：
  M = getRotationMatrix2D(center, -ang); M[0,2] -= cx-(tw-1)/2; M[1,2] -= cy-(th-1)/2;
  WarpAffine(srcGray, patch, M, new Size(tw,th), INTER_LINEAR, BORDER_REPLICATE)
warpAffine 是目标驱动的，代价降为 O(模板面积)/匹配，与源图尺寸无关。
另外：复用已缓存的 _sourceGray（不再每次全图 CvtColor）；Parallel.For 并行遍历匹配（每 index 一个 bucket 最后合并，无锁）。
实测（C://gmrun5//defect_test）：
  900x700 / 12 实例   : 旧 7.30ms  -> 新 0.13ms  (58x)
  2400x1800 / 20 实例 : 旧 70.6ms  -> 新 0.17ms  (418x)
  4000x3000 / 30 实例 : 旧 235.5ms -> 新 0.38ms  (620x)

### 2) 框过大：改用 minAreaRect 紧致旋转框
主因：旧覆盖层画的是 boundingRect（轴对齐）。斜向划痕的轴对齐包围盒几乎是个大方块，再按 -Angle 旋转 -> 视觉巨大且方位错。
- DefectResult 改为 W/H = minAreaRect.Size（double）+ RectAngle = -ang + minRect.Angle；新增 BoxLeft/BoxTop = ImgC - LeftTop - W/2（框心落在缺陷心，绕自身中心旋转）。
- XAML 覆盖层绑定 BoxLeft/BoxTop/W/H + RotateTransform Angle={Binding RectAngle}（不再用 AngleNegate）。Opacity 0.4->0.35，Stroke 2->1.5，字号 14->12。
- 全局亮度异常不再画满模板矩形（旧 X=0,Y=0,W=tw,H=th 就是截图里那个巨大红块），改居中小徽标 badge = clamp(0.22*minDim, 12, 40)。
- 阈值收紧：diffThreshold 35->45；globalBrightnessThresh 22->28；新增 maxAreaFrac=0.60 丢弃整体位移型大轮廓；边框带 margin=max(2, 4%*minDim) 抹掉（亚像素/亚度位姿误差必然点亮实例轮廓，是旧代码大块假划痕的来源）。
- 划痕判据从 ar>=3 收紧为：ar>=4 && shortSide<=max(3,0.22*minDim) && longSide>=0.15*minDim && areaFrac<=0.25。

### 3) 关键坑：形态学开运算核必须 2x2，不能 3x3
3x3 开运算会把 2px 宽的真实划痕整条抹掉（腐蚀需 3x3 全白邻域）。2x2 只杀 1px 对位噪声、保留 >=2px 划痕。实测 3x3 时划痕漏检，改 2x2 后恢复。

### 4) 旋转方向定论（务必记住）
cv::getRotationMatrix2D(c, θ) 在屏幕坐标(y 向下)里等价于 R_screen(-θ)；warpAffine 对图像内容施加的正是 M 本身。
=> native 用 getRotationMatrix2D(+angle) 生成实例，故实例内容 = R_screen(-angle) 的模板；摆正要用 getRotationMatrix2D(center, -ang)（产品代码本来就是对的）。
=> 模板局部 -> 图像空间用 R_screen(-ang)；局部矩形角 θ 映射为 θ + (-ang)。
=> OpenCV RotatedRect.Angle 与 WPF RotateTransform.Angle 同向同义（用 points() 推导核对过）。
曾误判为产品 bug，实为验证台 toSrc 用了 R_screen(+θ) 镜像方向。验证台已修正，并把模板改成非对称（加两个偏心色块）——对称模板会掩盖方向错误，别再用。

### 验证结果（非对称模板，实例 +30°）
注入 半径5 暗斑 + 20px 长 2px 宽亮线 -> 恰好 2 个缺陷、零假阳性：
  划痕      box 2.0x20.0 @60°  areaFrac 1.7%
  污渍/异物  box 9.2x9.9  @15°  areaFrac 3.8%
（旧代码这里会给出接近整模板的大框）

### 环境
- GrayMatch.Wpf 构建成功 0 警告 0 错误（含全部改动，仅 2x2 核常量在其后）。
- 之后再构建失败：GrayMatch.Wpf.pdb / GrayMatch\obj\*.cache 拒绝写入 —— WPF 应用正在运行 + obj 被冻结，非代码问题。
- MainWindow.xaml.cs 被独占锁定，状态栏「缺陷检测 xx ms」耗时补丁未写入，需关闭 VS/应用后手工加（RunMatchAsync 里 DetectDefects 前后加 Stopwatch）。
