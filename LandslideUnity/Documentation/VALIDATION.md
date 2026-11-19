# 验证记录

## 流域版（2026-11-19）

- Unity 2022.3.62f3c1 构建成功，12 个网格共通过 72 次状态检查。Player 自测通过，房屋终局为轻损 2 栋、局部倒塌 2 栋、整体倒塌 2 栋，22 棵重点树响应、11 棵折断。构建与自测日志为 `Logs/watershed_build_release.log` 和 `Logs/watershed_self_test.log`。
- [流域版成片](unity_watershed_demo_30fps.mp4) 为 24 秒、720 帧、30 fps、1280×720，AAC 48 kHz 双声道。六次换景保持 0.8 秒缓动，编码后音频峰值为 0.694。
- 与路线 A 的 720 帧逐帧对照，回放时间、FS、雨时、速度与前缘推进的最大误差均为 0，房屋和树木响应计数一致。原始数据 SHA-256、体积积分和媒体结果见[机器校验记录](watershed_validation.json)。
- 视觉取证的帧号、判定标准与三人盲测状态见[逐秒分镜与验收](WATERSHED_STORYBOARD.md)。

## 路线 A 分块表现（2026-11-17）

- Unity **2022.3.62f3c1** 编译并构建 Windows Player，退出码 0。`Logs/route_a_build_final.log` 含 `STRAIN_SURFACE_VALIDATED`：张拉块 26、挤压块 14、剪切块 63；2,389 个活动单元共用 3,514 个表面顶点；四级后缘陡坎及时间轴前后定位通过。实际生成的 12 个网格在多个时刻共完成 72 次几何检查，`solverBedUnchanged=true`、`visualOffsetOnly=true`、`seamlessLandscape=true`。
- `Logs/route_a_self_test_final.log` 包含 `STRAIN_DEBRIS_TEST_OK` 与 `LANDSLIDE_SELF_TEST_OK`：源区识别 415 条异块缝，160 个张拉碎土起点贴缝，16 个坡脚块石由挤压触发，51 个碎块在停积后叠置。房屋终局轻损 2、局部倒塌 2、整体倒塌 2；22 棵重点树响应、11 棵折断。碎屑、房屋和树木的前后定位与重播检查通过。
- 原始 `Assets/StreamingAssets/landslide.bytes` 的 SHA-256 仍为 `fd17e70a0fabfdf17cc35e24e2c6ad9fe6cc0aba8c07c7d9731626756a33f4b6`。四级陡坎、三维块缝、层理、额外碎屑属于显示与叙事几何，原求解器没有计算块体断裂、旋转滑面或这些碎屑的质量守恒。
- 失稳后 10–24 s 的 480 帧 Windows Player 离屏基准：路线 A 最终版脚本更新中位 **15.728 ms**、P95 **23.129 ms**；Player 帧间隔中位 **29.344 ms**、P95 **36.973 ms**。采样不含编码与交互 HUD，不能据此保证所有机器的实时帧率；方法和逐帧数据见 [PERFORMANCE.md](PERFORMANCE.md)。
- `Logs/route_a_capture.log` 确认最终 Player 输出 **720 张 1280×720 干净 PNG**。成片 [unity_route_a_demo_30fps.mp4](unity_route_a_demo_30fps.mp4) 通过 [逐帧与媒体校验](route_a_validation.json)：24 秒、720 帧、30 fps；回放时钟、失稳后时间、安全系数、累计雨时、当前/峰值速度及前缘推进相对基线的最大误差均为 **0**，房屋和树木响应数量逐帧一致。六次连续换镜位于第 105、210、360、450、540、600 帧，缓动持续 0.8 s。
- 片尾源区滑失 **3034.487 m³**、坡脚堆积 **1447.018 m³**，由原始厚度场独立核对。音轨为 AAC、48 kHz、双声道，解码峰值约 **0.733**，无削波。成片使用 Poly Haven 2K 地表贴图；具体来源、许可和校验值见 [ASSET_SOURCES.md](ASSET_SOURCES.md)。本轮保持 Built-in 渲染管线。

## 七段分镜与实拍贴图版本（2026-11-16，归档）

- Unity **2022.3.62f3c1** 重新编译、运行地表校验并构建 Windows Player，退出码 0；`Logs/directed_build_clean.log` 含 `SURFACE_PRESENTATION_VALIDATED ... terrainUnchanged=true textured=true seamlessLandscape=true` 和构建成功记录。播放器回放自测也通过，见 `Logs/directed_self_test_final.log`。
- 实际 Player 导出 **720 张 1280×720 干净 PNG**；拍摄时关闭交互式黄色房屋选中框。降雨、房屋近景、侧视和结果页均已查看。捕获日志为 `Logs/directed_capture_clean.log`，元数据为 `../unity_capture_directed/metadata.json`。
- 草地、湿土和岩石采用三套 Poly Haven 的 2K 漫反射、GL 法线和粗糙度源图，经 MD5/大小核对后派生成六张 Unity 贴图；来源、许可和原始校验值见 [ASSET_SOURCES.md](ASSET_SOURCES.md)。本轮未引入外部模型、HDRI 或录音。
- `validate_presentation.py` 将本版与初始 `unity_capture_v2` 逐帧比较：时间、安全系数、雨量、速度和前缘推进的最大误差均为 **0**；房屋与树木响应数量逐帧一致。原始 `landslide.bytes` 的 SHA-256 仍为 `fd17e70a0fabfdf17cc35e24e2c6ad9fe6cc0aba8c07c7d9731626756a33f4b6`。
- 六次换景发生在第 **105、210、360、450、540、600** 帧；边界相机连续，每次后续 24 帧按 0.8 秒 SmoothStep 运动。片尾源区滑失 **3034.487 m³**、坡脚堆积 **1447.018 m³**，与原始求解器厚度场独立积分相符。
- 成片 FFprobe 核对为 H.264、**24 秒 / 720 帧 / 30 fps / 1280×720**，AAC **48 kHz 双声道**；六段中文系统语音与程序音效混合，旁白时段压低背景声。源 WAV 峰值 **0.753**、RMS **0.0579**，AAC 解码峰值 **0.733**，无削波。结果文件：`../unity_capture_directed/validation.json`，复现命令：`python ../landslide_demo/validate_presentation.py`。
- 本轮继续使用 Built-in；视频帧率是导出规格，尚未测当前表现层的实时运行帧率。模型、入渗与破坏阈值仍是教学演示设定，不能作为实地预测。

## 表现层优化（2026-11-15，归档）

- Unity **2022.3.62f3c1**：C#、地表/湿土/叶片/天空/后期 shader 编译及 Windows x64 Mono Player 构建成功，退出码 0。日志：`Logs/polish_build_final.log`。
- 实际生成的 7 个网格在 0、10、22、45 s 共进行 28 次检查：索引、有限坐标、法线、切线及三角朝向通过；420×70 原始地形逐点保持不变，裙边和外围山地接缝的位置/法线一致，共用材质。日志输出 `SURFACE_PRESENTATION_VALIDATED ... seamlessLandscape=true`。
- 最终 Player 回放自测通过：轻损 2、局部倒塌 2、整体倒塌 2；22 棵重点树响应、11 棵折断；时间轴前后定位与重播一致，回到开头受损计数为 0。日志：`Logs/polish_self_test_final.log`。
- 最终 Player 使用真实 D3D11 渲染导出 **720 帧干净画面**，没有交互 HUD。检查全景、滑体、村落冲击、坡脚及片尾，并在对照图中保留原版画面。日志：`Logs/polish_capture_final.log`。
- 新捕获与 `unity_capture_v2/metadata.json` 逐帧比对，回放时间、安全系数、累计降雨、当前速度、全过程峰值和前缘推进的最大差异均为 **0**；房屋各级损伤及树木响应数量逐帧一致。没有重新运行求解器或修改 `landslide.bytes`。
- 旧视频末帧统计缓存为源区滑失 3029.250 m³、坡脚堆积 1444.995 m³。新版末帧强制刷新，并与原始求解器厚度场独立积分核对，得到 **3034.487 m³、1447.018 m³**。这是显示统计修正，原厚度数据没有变化。
- 第 105、210、600 帧切换相位时，相机目标、角度和距离与前帧连续；随后 **24 帧 / 0.8 s** 的完整 SmoothStep 曲线通过检查。
- FFprobe 确认影片为 H.264、**1280×720、30 fps、720 帧、24 s**；AAC **48 kHz 双声道、24 s**。原创合成 WAV 峰值 0.4930、RMS 0.0458，最终 AAC 解码峰值 0.4906，均无削波。
- 机器可读结果：`../unity_capture_polished/validation.json`（相对于工程根目录）；复现命令：`python ../landslide_demo/validate_presentation.py`。校验器使用主输出目录中的 MP4 和独立 WAV。
- 本轮保持 **Built-in**，使用 ACES 近似拟合、轻 Bloom、暗角及轻量接触遮蔽；没有迁移 URP、使用 Volume 或声称实现完整 SSAO。纹理、树木、天空和音效由程序生成，没有下载外部美术或现场录音。
- 原性能报告仍对应 2026-11-06 的旧表现层，本轮不据视频导出规格宣称实时帧率。

数据 SHA-256：`fd17e70a0fabfdf17cc35e24e2c6ad9fe6cc0aba8c07c7d9731626756a33f4b6`。

新版影片为 `Documentation/unity_polished_demo_30fps.mp4`，对照图为 `Documentation/polished_comparison.png`。旧影片与旧截图保留。

## 前一轮回放优化（2026-11-06）

- Unity 2022.3.62f3c1：C#、自定义着色器编译及 Windows x64 Mono Player 构建成功。
- 原始数据为 420×70 网格、91 个时刻；显示增强不修改厚度、速度、安全系数和体积指标。
- 完整回放终局轻损 2、局部倒塌 2、整体倒塌 2；回到开头受损计数为 0。
- 房屋层级检查 21 s → 32 s → 21 s，所有部件位置差异小于 0.001 m。
- 22 棵重点树最终全部响应，其中 11 棵折断；逐树验证输运前已倾斜超过 60°。
- 重点树全部层级的位置、旋转、显隐状态经过时间轴跳转和重播检查，结果一致。
- 日志输出 `UPGRADE_FINAL`、`TREE_RESPONSE_TEST_OK`、`LANDSLIDE_SELF_TEST_OK`。
- 原版、新版分别完成 480 帧冲撞阶段实际 Player 性能采样，见 `PERFORMANCE.md`。
- 编译 Player 导出 720 帧，检查源区、前缘、分阶段损伤、倒伏树木、坡脚与全景。

验证针对运行、数据回放、时间轴确定性和表现；不构成真实山体或建筑结构预测的场地标定。
