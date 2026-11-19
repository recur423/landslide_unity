# 教学视频与 Unity 表现层改造

## 2026-11-19 流域分镜

复杂地形、沟谷转向、分叉汇流和块体运动属于教学可视化。求解器沿用原地形和滑面设定，片尾数字取自原数值场。

[流域版成片](unity_watershed_demo_30fps.mp4) · [逐秒分镜与验收](WATERSHED_STORYBOARD.md) · [数值与媒体校验](watershed_validation.json)

## 2026-11-16—17 路线 A 分块表现

[路线 A 成片](unity_route_a_demo_30fps.mp4)在原 `h/UX/UZ/Bed` 数值文件上增加了应变驱动分块、分层滑面、四级示意后缘陡坎、显示地形起伏和联动碎屑。实现和复现步骤见 [ROUTE_A.md](ROUTE_A.md)，冲撞阶段 Player 实测见 [PERFORMANCE.md](PERFORMANCE.md)。

## 2026-11-16 分镜与真实贴图升级（归档）

该版输出是 `unity_directed_demo_30fps.mp4`；下文 `polished` 路径是更早一版的构建归档，继续保留供画面对照。

草地、湿土和岩石的 2K 漫反射、GL 法线与粗糙度图来自 [Poly Haven（CC0）](ASSET_SOURCES.md)，运行时合成颜色/光滑度贴图。新增 42 m 尺度色斑与远景细节淡出，坡面随降雨变湿、源区裂缝渐张开；滑体边缘渐隐，裂隙沿运移方向拉伸。树冠增加四种轮廓和确定性色调变化，房屋底座改为薄基座。新版仍使用 Built-in，未导入外部树屋模型、HDRI 或现场录音。

影片使用 [七段、24 秒分镜表](STORYBOARD.md)：前段明显呈现雨与湿润变化，12–15 s 靠近房屋，15–18 s 低角度观察滑体厚度，20–21 s 返回全景，21–24 s 在虚化场景上列参数与显示假设。镜头每次转换用 0.8 s 缓动。环境音效经音量调整后与六段本机 Microsoft Huihui 中文系统语音混合；`landslide_demo/generate_narration.ps1`、`narration_cues.json` 可复现旁白。片尾注明地表贴图来自 Poly Haven（CC0）。

在工作区执行新版流程：

```powershell
$workspace = 'E:\graduate\示例教学\vlog'
$unityEditor = 'E:\graduate\unity\2022.3.62f3c1\Editor\Unity.exe'
$player = Join-Path $workspace 'LandslideUnityPlayer\LandslideDemo.exe'
$project = Join-Path $workspace 'LandslideUnity'
$build = Start-Process -FilePath $unityEditor -ArgumentList @('-batchmode','-quit','-projectPath',('"{0}"' -f $project),'-executeMethod','LandslideDemo.Editor.BuildDemo.BuildWindows','-logFile',('"{0}"' -f (Join-Path $project 'Logs\directed_build_final.log'))) -PassThru -Wait -WindowStyle Hidden
if ($build.ExitCode -ne 0) { throw "Unity build failed: $($build.ExitCode)" }
$captureDir = Join-Path $workspace 'unity_capture_directed'
$capture = Start-Process -FilePath $player -ArgumentList @('-batchmode','-screen-width','1280','-screen-height','720','--capture-dir',('"{0}"' -f $captureDir),'-logFile',('"{0}"' -f (Join-Path $project 'Logs\directed_capture.log'))) -PassThru -Wait -WindowStyle Hidden
if ($capture.ExitCode -ne 0) { throw "Player capture failed: $($capture.ExitCode)" }
python (Join-Path $workspace 'landslide_demo\render_unity_preview.py')
python (Join-Path $workspace 'landslide_demo\validate_presentation.py')
```

运行前可用 `powershell -File landslide_demo\generate_narration.ps1 -CueFile landslide_demo\narration_cues.json -OutputDirectory landslide_demo\output\narration_huihui` 重新生成语音；合成命令可加 `--no-narration` 输出只有音效的版本。缺少外部贴图时，`SurfaceTextureSet` 回退到原程序纹理。新增资源的准确来源、下载地址、MD5 与派生方式见 [素材来源](ASSET_SOURCES.md)。

## 2026-11-15 首轮优化归档

本轮保留原始数值仿真、数据文件与可定位回放，集中解决悬空地形、单色地表、块状树冠、平光、信息遮挡与无音轨的问题。新版输出统一使用 `polished` 名称，原视频和原捕获帧保留。

## 实现范围

| 项目 | 本版实现 | 入口 |
|---|---|---|
| 地形边界 | 数值网格外补五圈向外、向下延伸的裙边；原网格顶点不变 | `Assets/Scripts/LandslideSurface.cs` → `BuildCutawaySkirt` |
| 周边地貌与天空 | 网格外的山谷与远山环带、分层噪声阴天天空、线性雾、软阴影 | `PresentationEnvironment.cs`、`SceneController.MakeLighting`、`Assets/Resources/Shaders/OvercastSky.shader` |
| 草—土—岩 | 运行时生成 256×256、带 mipmap 的可平铺颜色/光滑度及法线贴图；世界坐标三向投影，按坡度、高度与裸露区域混合 | `SurfaceTextureSet.cs`、`Assets/Shaders/VertexTerrain.shader`、`LandslideTriplanar.cginc` |
| 滑体湿土 | 褐色土壤纹理、裂隙、法线起伏和湿润高光；湿润度按回放阶段变化 | `Assets/Shaders/FracturedSoil.shader`、`LandslideSurface.SetState` |
| 树木 | 交叉叶片簇、透明裁切、保留远景轮廓的 mipmap、树皮纹理和确定性风动 | `ProceduralAssets.cs`、`SceneryTextures.cs`、`Assets/Resources/Shaders/LeafCards.shader` |
| 房屋 | 现有可破坏门窗、墙体、屋顶与基础使用共享木纹、抹灰、窗格、瓦片和混凝土贴图 | `SceneryTextures.cs`、`ProceduralAssets.CreateMaterial` |
| 后期 | HDR 相机、ACES 近似色调映射、轻 Bloom、饱和度/色彩调整、暗角、轻量接触遮蔽 | `PresentationPostProcess.cs`、`Assets/Resources/Shaders/PresentationGrade.shader` |
| 反射 | 天空反射探针，启动时更新；采集天空，不采集场景物体 | `PresentationEnvironment.cs` |
| 交互界面 | 默认简洁 HUD，Tab 或“详细数据”展开原 FS 曲线与房屋参数；点击房屋直接查看详情 | `SceneController.cs` |
| 拍摄 | 相位边界后 0.8 秒 SmoothStep，插值目标、方位角、俯角和距离；导出清洁画面与逐帧元数据 | `SceneController.Capture`、`ShotPose` |
| 影片合成 | 单标签/单数字字幕，切镜时隐藏，最后 3 秒集中列参数及声明；加入程序合成音轨 | `../landslide_demo/render_unity_preview.py`（相对工程根目录） |

本版仍使用 **Built-in**，并未安装或迁移 URP，也没有使用 URP Volume。ACES 是自有 shader 中采用的常见近似拟合曲线；接触遮蔽是从相机深度/法线邻域采样的轻量屏幕效果，不等同于完整 SSAO。反射探针只提供天空反射，不能据此声称已有完整场景反射。以后迁移 URP 时，仍需改写两个 Surface Shader 并重新验证材质、阴影和视频输出。

天空通过 [`RenderSettings.skybox`](https://docs.unity.cn/2022.3/Documentation/ScriptReference/RenderSettings-skybox.html) 指定；屏幕后期通过相机的 [`OnRenderImage`](https://docs.unity.cn/2022.3/Documentation/ScriptReference/MonoBehaviour.OnRenderImage.html) 读取和修改渲染结果。这与 Unity 2022.3 的 Built-in 扩展方式一致；官方也明确说明 `OnRenderImage` 不适用于 Scriptable Render Pipeline。

裙边、远山、表面起伏、湿润度、叶片风动和破坏碎片都属于表现层。它们不改变求解域、厚度/速度场、安全系数、质量或能量；滑体湿润高光也不是独立计算的真实含水率。

## 信息与声音

影片保留 24 秒、1280×720、30 fps 的原时间脚本与首次冲击 0.5× 慢放。各段只保留“降雨入渗 + 累计分钟”“滑体加速 + 当前速度”“受损建筑 + 数量”或“坡脚堆积 + 体积”。字幕在镜头边界前 0.1 秒至后 0.8 秒隐藏，之后用 0.35 秒淡入。影片不再逐帧叠加 FS 曲线、房屋逐项参数或显示假设。

最后 3 秒集中展示全过程峰值速度、前缘推进、源区滑失、坡脚堆积、受损建筑、响应树木及建筑损伤分级，并说明“厚度显示 5×、表面起伏和树木/建筑破坏为表现层；地形、材料与破坏阈值为演示设定”。完整工程说明仍保留这些假设。终局体积由原始 `runout_2d_fields.npz` 计算，避免交互 HUD 半秒缓存导致末帧读数滞后。

音轨是固定随机种子生成的原创合成音效，包括雨声、滑动低频、碎石摩擦与建筑损伤冲击。速度包络和冲击时刻取自逐帧元数据；最终编码为 AAC、48 kHz、双声道。它是教学配音效，不是现场录音，也不是声学仿真结果。合成器同时保留独立 WAV，可在剪辑软件中更换或调整。

模型、贴图、天空和声音均由本工程代码生成。本轮没有引入 Poly Haven、ambientCG、Quaternius、Kenney 或其他外部素材，因此不添加虚构下载记录、CC0 资源归属或致谢。若后续替换外部素材，应另行记录具体资源、下载地址、许可与版本。

## 构建与导出

以下为本机 PowerShell 命令，工作区为 `E:\graduate\示例教学\vlog`，Unity 为 `E:\graduate\unity\2022.3.62f3c1\Editor\Unity.exe`。构建前关闭同一工程的 Unity Editor。Unity 和 Player 是 GUI 程序，使用 `Start-Process -Wait -PassThru -WindowStyle Hidden` 等待真正退出，并检查退出码。

先定义路径和运行函数；以下后续命令使用同一 PowerShell 会话：

```powershell
$workspace = 'E:\graduate\示例教学\vlog'
$unityEditor = 'E:\graduate\unity\2022.3.62f3c1\Editor\Unity.exe'
$projectPath = Join-Path $workspace 'LandslideUnity'
$playerPath = Join-Path $workspace 'LandslideUnityPlayer\LandslideDemo.exe'
$logPath = Join-Path $projectPath 'Logs'
New-Item -ItemType Directory -Force -Path $logPath | Out-Null

function Invoke-LandslideProcess {
    param([string]$FilePath, [string[]]$Arguments)
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments `
        -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "Process exited with code $($process.ExitCode): $FilePath"
    }
}
```

构建 Windows Player。该入口会重建最小启动场景，检查地形网格、裙边、纹理、切线和 shader，然后构建 `LandslideUnityPlayer/LandslideDemo.exe`；运行时必须保留整个 Player 目录。

```powershell
Invoke-LandslideProcess -FilePath $unityEditor -Arguments @(
    '-batchmode', '-quit',
    '-projectPath', ('"{0}"' -f $projectPath),
    '-executeMethod', 'LandslideDemo.Editor.BuildDemo.BuildWindows',
    '-logFile', ('"{0}"' -f (Join-Path $logPath 'polish_build.log'))
)
```

运行原数值回放的混合损伤、树木、前后定位和重播自测。成功日志应包含 `LANDSLIDE_SELF_TEST_OK`；这项自测不替代视频的目视验收。

```powershell
Invoke-LandslideProcess -FilePath $playerPath -Arguments @(
    '--self-test', '-screen-width', '1280', '-screen-height', '720',
    '-logFile', ('"{0}"' -f (Join-Path $logPath 'polish_self_test.log'))
)
```

先捕获 18 个关键帧检查天空、地形边界、叶片、损伤过程与切镜插值。关键帧模式只导出稀疏 PNG 和元数据，不是完整视频输入；使用单独目录，避免与 720 帧捕获混合。

```powershell
Invoke-LandslideProcess -FilePath $playerPath -Arguments @(
    '--capture-dir', ('"{0}"' -f (Join-Path $workspace 'unity_capture_polished_keyframes')),
    '--capture-keyframes', '-screen-width', '1280', '-screen-height', '720',
    '-logFile', ('"{0}"' -f (Join-Path $logPath 'polish_keyframes.log'))
)
```

确认关键帧后捕获完整 720 帧。该流程调用离屏相机，PNG 不含交互 HUD；元数据保留原物理数值、标签位置和相机姿态。

```powershell
Invoke-LandslideProcess -FilePath $playerPath -Arguments @(
    '--capture-dir', ('"{0}"' -f (Join-Path $workspace 'unity_capture_polished')),
    '-screen-width', '1280', '-screen-height', '720',
    '-logFile', ('"{0}"' -f (Join-Path $logPath 'polish_capture.log'))
)
```

用已安装 NumPy、Pillow 的 Python 与 PATH 中的 FFmpeg 合成字幕和声音。此步只读取已有捕获及求解器数据，不运行仿真。

```powershell
python "$workspace\landslide_demo\render_unity_preview.py" `
    --capture-dir "$workspace\unity_capture_polished" `
    --output "$workspace\landslide_demo\output\unity_polished_demo_30fps.mp4"
```

本节归档影片若需重新校验，运行 `python "$workspace\landslide_demo\validate_presentation.py" --capture-dir "$workspace\unity_capture_polished" --video "$workspace\landslide_demo\output\unity_polished_demo_30fps.mp4" --expected-boundaries 105,210,600`。旧捕获的片尾体积存在统计缓存滞后，当时末帧按原求解器厚度场重新积分核对。当前校验器不带参数时检验本文开头的最新影片。

快速验证合成管线可添加 `--max-frames 45 --no-docs` 并指定独立输出文件。`--no-docs` 不更新文档预览；`--endcard-seconds 0` 可关闭片尾，保留单行标注。原始 `unity_capture_polished/*.png` 始终保留，方便在剪辑软件中重新配字、加旁白或替换音轨。

## 输出与验证范围

| 输出 | 路径（相对工作区） |
|---|---|
| 新完整捕获与元数据 | `unity_capture_polished/` |
| 新关键帧捕获 | `unity_capture_polished_keyframes/` |
| 新影片及独立音轨 | `landslide_demo/output/unity_polished_demo_30fps.mp4`、同名 `.wav` |
| 文档影片 | `LandslideUnity/Documentation/unity_polished_demo_30fps.mp4` |
| 文档截图 | `LandslideUnity/Documentation/polished_volume_preview.png`、`polished_preview.png`、`polished_deposit_preview.png`、`polished_summary_preview.png` |
| 保留的旧影片 | `LandslideUnity/Documentation/unity_demo_preview.mp4`、`landslide_demo/output/unity_optimized_demo_30fps.mp4` |
| 保留的旧捕获 | `unity_capture_v2/` |
| 本轮日志 | `LandslideUnity/Logs/polish_*.log` |

本轮实际构建、自测、捕获和媒体检查结果以 [VALIDATION.md](VALIDATION.md) 为准。[PERFORMANCE.md](PERFORMANCE.md) 分列 2026-11-17 路线 A 实测与 2026-11-06 历史测量。24 秒、30 fps 是文件导出规格，不能代替运行性能测量。
