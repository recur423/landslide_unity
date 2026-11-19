# 路线 A：同一份数值场的分块表现（2026-11-16—17）

本轮只改 Unity 的显示几何、材质和碎屑事件。`landslide.bytes` 中的 `Bed`、厚度 `h`、水平速度 `UX/UZ`、降雨、安全系数和统计值均未写回或重算；原文件 SHA-256 仍为 `fd17e70a0fabfdf17cc35e24e2c6ad9fe6cc0aba8c07c7d9731626756a33f4b6`。

| 表现 | 数值依据与实现 | 入口 |
|---|---|---|
| 分块张拉、挤压、剪切 | 固定世界坐标的约 8.6 × 8.2 m Voronoi 块 ID；对每帧 `UX/UZ` 做单元差分。正 `∂UX/∂x` 使块缘后退并后仰，负值使缝闭合并轻微上拱，`(∂UX/∂z + ∂UZ/∂x)/2` 驱动侧向错动。静态块形的微小扰动不参与动画。 | `LandslideSurface.InitializeVoronoiBlocks`、`UpdateFracturedSurface` |
| 裂缝内层理与滑体侧墙 | 相邻不同块之间只要边缘张开或错开就显露薄的土、风化层、岩色层与缝底；纯挤压造成的错台也保留可见侧壁。重合的闭合边跳过几何计算，可见三角形索引压成前缀；顶点、法线和切线缓冲仍全量上传。外缘厚度墙向内倾斜成为可见剪切面。复用已有 Poly Haven 土、岩贴图。 | `LandslideSurface.UpdateFractureSeams`、`UpdateBoundaryWalls`、`LayeredSlip.shader` |
| 多级后缘陡坎 | 根据初始厚度场的源区边界生成四级台阶，在失稳后约 0、3、7、12 s 依次显露；可前后定位并复原。 | `LandslideSurface.BuildSourceScar`、`UpdateSourceScar` |
| 地形起伏 | 仅在显示高程加入三尺度 fBm、三条浅冲沟和两道横向脊，偏移限制为 −1.2 至 +0.9 m，并在数值网格边缘平滑归零。房屋、树木、岩石的贴地高度同步使用显示高程。 | `SimulationData.VisualGroundOffset`、`DisplayBedHeight`、`VisualReliefFactor` |
| 联动碎屑 | 源区张拉碎土与短裂纹线沿同一 Voronoi 规则得到的异块网格边布置；局地正向速度梯度首次越过阈值决定发射时间。前缘块石由挤压事件触发一次抛起；低速停积后按邻近落点叠置。轨迹仍按原 `UX/UZ` 平流，时间轴可重放。 | `LandslideDebris.VelocityStrain`、`FindTensionLaunch`、`FindCompressionEvent`、`AssignSettlementStacks` |

**物理解释边界：** 张拉、挤压和侧向剪切取自已有二维速度场，因此裂缝出现的位置与时间有数值依据；块的三维开缝、后仰、上拱、层理和四级陡坎是教学可视化。原求解器没有计算块体断裂、旋转滑面或这些额外碎屑的质量与能量，影片不能据此声称求解出多级旋转滑坡。

## 构建与导出

在工作区 `E:\graduate\示例教学\vlog` 执行。Unity Editor 为 `E:\graduate\unity\2022.3.62f3c1\Editor\Unity.exe`；完整构建步骤也可参照 [PRESENTATION.md](PRESENTATION.md)。

```powershell
$workspace = (Resolve-Path '.').Path
$project = Join-Path $workspace 'LandslideUnity'
$editor = 'E:\graduate\unity\2022.3.62f3c1\Editor\Unity.exe'
$build = Start-Process -FilePath $editor -ArgumentList @('-batchmode','-quit','-projectPath',('"{0}"' -f $project),'-executeMethod','LandslideDemo.Editor.BuildDemo.BuildWindows','-logFile',('"{0}"' -f (Join-Path $project 'Logs\route_a_build_final.log'))) -PassThru -Wait -WindowStyle Hidden
if ($build.ExitCode -ne 0) { throw "Unity build failed: $($build.ExitCode)" }
$player = Join-Path $workspace 'LandslideUnityPlayer\LandslideDemo.exe'
$selfTest = Start-Process -FilePath $player -ArgumentList @('-batchmode','--self-test','-logFile',('"{0}"' -f (Join-Path $project 'Logs\route_a_self_test_final.log'))) -PassThru -Wait -WindowStyle Hidden
if ($selfTest.ExitCode -ne 0) { throw "Player self-test failed: $($selfTest.ExitCode)" }
$capture = Start-Process -FilePath $player -ArgumentList @('-batchmode','-screen-width','1280','-screen-height','720','--capture-dir',('"{0}"' -f (Join-Path $workspace 'unity_capture_route_a')),'-logFile',('"{0}"' -f (Join-Path $project 'Logs\route_a_capture.log'))) -PassThru -Wait -WindowStyle Hidden
if ($capture.ExitCode -ne 0) { throw "Player capture failed: $($capture.ExitCode)" }
python landslide_demo/render_unity_preview.py --capture-dir unity_capture_route_a --output landslide_demo/output/unity_route_a_demo_30fps.mp4 --docs-video-name unity_route_a_demo_30fps.mp4 --preview-prefix route_a
python landslide_demo/validate_presentation.py --capture-dir unity_capture_route_a --video landslide_demo/output/unity_route_a_demo_30fps.mp4
```

构建会检查实际网格的三角形朝向、有限坐标、法线和切线，以及场景外围接缝。Player 自测继续检查时间轴前后定位、房屋分级与树木响应；影片校验将逐帧对照最初的数值回放。

最终 [24 秒路线 A 成片](unity_route_a_demo_30fps.mp4) 与[逐帧校验](route_a_validation.json)已生成；构建、自测和媒体核验结果见 [VALIDATION.md](VALIDATION.md)，冲撞阶段的 Player 实测及其适用范围见 [PERFORMANCE.md](PERFORMANCE.md)。视频的 30 fps 是导出规格。
