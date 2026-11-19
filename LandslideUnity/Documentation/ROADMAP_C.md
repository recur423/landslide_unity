# 路线 C：更换数值内核（规划方案，尚未实施）

> **状态声明（务必先读）**：本文件是**技术规划**，不是实现记录。截至 2026-11-16，当前交付版本仍是 `unity_directed_demo_30fps.mp4`，物理内核仍是 `simulate_3d.py` 的二维深度平均浅水流，`landslide.bytes` 的 SHA-256 仍为 `fd17e70a0fabfdf17cc35e24e2c6ad9fe6cc0aba8c07c7d9731626756a33f4b6`。本文件描述的一切改动**均未执行**，不得据此修改 `VALIDATION.md`、`PRESENTATION.md`、`STORYBOARD.md` 或片尾数值。若日后实施，须另建验证条目，明确写出"以新基线为准，旧版逐帧差异断言作废"。

## 0. 结论先行

路线 C 不是"给画面加效果"，而是**换掉数值内核**：从「一个深度平均场 + 一个预设滑面」换成「连续介质应力–应变求解 + 剪切带自发形成」。它能真正解决"土地没有碎裂、运动只是下滑、只有一个滑面"，代价是整条数据链与验证体系重建。

但有一个必须说清的方法学事实：

| 想看到的 | 力学上是什么 | 谁能给 |
|---|---|---|
| 多级/叠加滑面、溯源式后退、拉裂台阶 | **渐进破坏**：剪切带应变局部化 + 峰后应变软化 | MPM 天然能给（无需预设滑面） |
| 土体裂成一块块、翻滚、相互叠压 | **连续体 → 离散体的强不连续** | 标准 MPM **给不了**，需 MPM-DEM / XMPM / FDEM |

上一版里"块体化"是表现层用应变率驱动的假缝（路线 A）。路线 C 能真实给出**前者**；**后者**要再加一层离散方法，不能指望换个求解器就自动出现。这个区分决定了选型和工时。

**推荐路线是「双轨 C-lite」**：主数值结果继续用现有 `landslide.bytes`（不动任何数字与验证），另起一路 MPM 只负责"机理可视化"，输出独立的 `landslide_mpm.bytes`。这样既拿到了真实的剪切带/多级滑面，又保住了你们最值钱的资产——那条"逐帧差异为 0、SHA 固定、片尾数字与求解器独立积分相符"的严谨验证链。

## 1. 为什么 A / B 到不了，只有 C 能到

`simulate_3d.py` 文件头自述：状态只有 `(h, hu, hv)`，`bed` 就是唯一滑面。深度平均意味着**沿厚度方向应力被积分掉**，数学上不存在"第二个滑面"这个自由度，也不存在"土体被拉裂"的张量。路线 B 加多释放体、非均一摩擦、夹带项，得到的仍是同一条连续自由面上的多股流动；只有引入三维（或平面应变）应力–应变本构，剪切带才可能自发从坡脚萌生、向上溯源、分叉、把土体切成块。

反过来说：**如果课题只需要"看起来碎了"，不要走 C**。路线 A 的块体化 + 多级陡坎 + 地形复杂度，半天到两天就能交付，且不推翻任何验证。C 的价值在于论文里可以写"求解了渐进破坏与应变局部化"，不在观感。

## 2. 需求的可计算翻译

| 目标 | 必须出现在模型里的量 | 验收证据（可截图/可统计） |
|---|---|---|
| 不止一个滑面 | 等效塑性应变场 ε_p 中出现 2–3 条独立高值带 | 剖面 ε_p 云图，统计高值带条数与深度 |
| 溯源式破坏 | 破坏时刻随 x 依次发生，非全场同时 | 各滑面起裂时刻表，t₁<t₂<t₃ |
| 拉裂与块体 | 张拉损伤变量、块体 ID、块体间相对位移 | 张裂缝张开宽度曲线、块体计数 |
| 前缘复杂化 | 应变软化 + 非均质强度场导致指状分叉 | 前缘轮廓的横向方差随时间增大 |
| 堆积非均一 | 块体翻滚、叠压、孔隙 | 终局堆积剖面的块体边界，而非平滑楔形 |
| 由降雨触发 | 孔压场 u(z,t) → 有效应力 → 强度折减 | 触发时刻与现有 89 min / FS<1 对齐 |

## 3. 求解器选型（已核实，2026-11）

| 项目 | 许可 | 语言 / 维护 | 与本案相关的事实 | 判断 |
|---|---|---|---|---|
| **GeoTaichi**（浙大） | **GPL-3.0** | Python + Taichi，活跃（v0.5，2026-10） | 同一库内含 MPM / DEM / LSDEM / FEM / MPM-DEM / FEM-MPM；官方算例直接有 **"Progressive failure process of sensitive clay"**、Column collapse、Submarine granular landslide；输出 VTU/VTS 供 ParaView | **首推**：唯一能同时覆盖"渐进破坏 + 块体化 + 混合方法"的活跃开源项目，且是 Python 生态，与现有 `simulate*.py` 同栈 |
| **Anura3D_OpenSource** | **LGPL-3.0-or-later** | Fortran，2025-10 开源、2026-11 仍在更新 | 两相单点饱和/非饱和、孔压生成消散、入渗边界、应变软化 Mohr-Coulomb；**有论文把溯源式敏感黏土滑坡的软化律直接实现在它上面**；算例含 Column collapse、Submerged slope collapse | 次选/交叉验证。注意：前处理依赖商业 GiD（社区称提供学术临时许可，**需自行确认条款**），后处理 ParaView |
| **CB-Geo MPM** | **MIT**（benchmarks 仓库是 CC-BY-4.0） | C++14，主仓库维护放缓（2024-11 后） | DesignSafe 有 **"MPM Landslide" 用例（Oso 滑坡）**；`pycbg` 提供纯 Python 前处理（gmsh 网格 + JSON）；HDF5 checkpoint + VTP 可视化；含 MohrCoulomb3D、等参单元不规则边界 | 许可最宽松、前处理最贴合 Python；但代码库维护放缓，**剪切带/应变软化专门算例未查到** |
| **MaterialPointSolver.jl** | **MIT** | Julia，活跃，GPU 后端无关 | 组织名即 LandslideSIM；HDF5 导出；论文 *Computers and Geotechnics* 183 (2025) 107189 | 备选；论文 4 个算例是否含剪切带**未确认** |
| taichi_mpm / MLS-MPM | MIT | C++，2018–2019 停滞 | 图形学导向（雪、切割），**无 MC/DP 本构、无孔压耦合** | 不适合 |
| Y-Geo (FDEM) | **非 OSI 开源**，限制性自定义许可 | C++，官方已声明停止支持 | 能力最对口（cohesive element、Mode I/II 破裂、continuum→discontinuum），但禁止代码再组合、修改后版权仍归原作者、必须署名 | 只能引用方法，不能作为工程依赖；商业继任者 Irazu / 3DEC 闭源收费 |
| r.avaflow 4 | GPL-2.0+ | Python/C，活跃 | 多相 + entrainment + fragmentation，但仍是**深度平均**，无三维应力张量、无应变软化 | 属于路线 B 的加强版，不是 C |

**许可证提醒（工程层面，非法律意见）**：GeoTaichi 的 GPL-3.0 与 Anura3D 的 LGPL-3.0 都有 copyleft 义务。若只把它当作**外部命令行求解器**、仅把其输出的数值数据文件引入 Unity，通常不视为把代码并入工程；但一旦把源码纳入仓库或链接进 Player，义务就不同。若课题有分发/闭源顾虑，改用 MIT 的 CB-Geo MPM 或 MaterialPointSolver.jl。无论哪种，都要按 `PRESENTATION.md:35` 的既有约定，在 `ASSET_SOURCES.md` 增记「求解器名称、版本、下载地址、许可证、commit hash」。

## 4. 力学模型设计（直接接现有参数）

### 4.1 计算域与时间轴 —— 关键取舍

**不要让 MPM 显式算 89 分钟降雨。** 现有 `calculate_trigger()` 是 Green-Ampt 桶模型 + 无限边坡 FS；以 E≈20 MPa、ρ=1900 kg/m³ 估算弹性波速 ≈102.6 m/s，Δx=1 m 时 CFL 上限约 **0.0097 s**，89 min 需要约 5.5×10⁶ 步，不现实。

正确做法：**把降雨压缩为孔压初始/边界条件，只显式算失稳后的 45 s**。

- 触发时刻对齐现有数据：`rainfall_stability.csv` 首次 FS<1 在 **89.0 min**，FS=0.9988，`perched_saturation=0.4063`，累计雨量 79.83 mm。
- 由现有 `safety_factor()` 反解：法向应力 27389.5 Pa、下滑剪应力 16457.3 Pa、**临界孔压 u_crit = 5825 Pa**、满饱和孔压 u_max=14415.5 Pa；FS 从 1.213 降到 0.686。
- 湿润锋深度 `z_f(t) = infiltrated_mm(t) / (1000·Δθ)`，Δθ=0.12（沿用 `DELTA_THETA`）；把 `u(z,t) = γ_w·(z_f−z)·cos²β`  clamp 到非负值，施加到地表以下各物质点，令 `σ′ = σ − u`，`τ_f = c′ + σ′·tanφ′`。这样"降雨 → 孔压上升 → 强度折减 → FS<1"的因果链在力学上是真实的，且与现有曲线数值同源。
- **物理时长固定 45 s、91 帧、0.5 s 一帧**：与 `landslide.bytes` 的 `Times[]` 完全一致 → 24 秒影片、720 帧、七段分镜、0.8 s 缓动、六个旁白时间点**全部可以保留**，TTS 不必重生成。这是 C-lite 能保住的最大一笔资产。

### 4.2 几何与分层（2D 平面应变起步）

- 纵剖面直接复用 `terrain()`（x=0.5…419.5 m，dx=1 m），垂向取到地表下约 40 m；先做 2D plane-strain，不做 3D（3D 粒子量会涨两个数量级）。
- 三层：表土 **2.0 m**（正好等于现有 `SOIL_DEPTH`）、风化层 8 m、基岩。
- 强度参数直接继承：`φ′=31°`、`c′=3500 Pa`、`ρ_s=1900 kg/m³`、`K_sat=1.8e-5 m/s`、`ψ_suction=0.12 m`、`Δθ=0.12`。
- MPM 额外需要、但现有工程没有的量，必须显式列为"演示设定"：`E≈20 MPa`、`ν=0.3`、剪胀角 ψ、`K₀=1−sinφ′=0.485` 的初始地应力、以及软化参数 `c_p→c_r`、`φ_p→φ_r`、参考塑性应变 `ε_ref`。**参数越多，越不能声称"实地标定"**。
- 为得到**叠加滑面**，在风化层/基岩界面及中间深度预设 2–3 条**强度略低**的薄层（不是预设滑面形状，只是材料非均质），让剪切带自己选择路径；再用均匀随机场（±10–15%）扰动 c′，使前缘自然分叉。
- 本构：Mohr-Coulomb 或 Drucker-Prager 弹塑性 + **峰后应变软化**，`c(ε_p) = c_r + (c_p−c_r)·exp(−ε_p/ε_ref)`。必须加**非局部正则化或黏性正则化**，否则剪切带宽度随网格细化趋零、结果网格依赖（这一点要写进验证）。

预期破坏序列：坡脚剪切带萌生 → 向上溯源扩展 → 后缘张拉裂缝 → 第二、第三条剪切带相继激活（多级旋转块体）→ 表层块体分离翻滚 → 前缘流动 → 坡脚叠压堆积。这正是你要的"不只是下滑"。

### 4.3 规模与耗时估算

- 背景网格 Δx=Δz=1 m → 约 420×45 ≈ 1.9 万单元；4 particles/cell → **约 6–8 万物质点**。
- Δt 取 2×10⁻³ s（比 CFL 上限保守 5 倍），45 s → **约 2.25 万步**；Taichi GPU 后端下应是分钟到小时量级，CPU 后端按一个量级上调。先跑 5 s 短算例实测再外推。
- 起裂阶段建议做**准静态初始化**（重力加载 + 局部阻尼 + K₀ 应力），否则初始应力噪声会污染剪切带形态。

## 5. 数据管线：双轨 C-lite（强烈推荐）

### 5.1 为什么双轨

现有 `landslide.bytes` 是 `LSIM2D01`：420×70 网格、91 帧、每帧 `(h, ux, uy)`、外加降雨 5 列与摘要 5 列，共 31 MB。`SimulationData.cs` 的双线性 `Spatial()`、`FrontAdvance()`、`RegionVolume()` 与 `validate_presentation.py` 的逐帧断言都建立在这个格式上。

**不要覆盖它。** 新增独立文件，只在渲染层汇合：

```
simulate_3d.py（保持不动） ──> landslide.bytes ──> SimulationData.cs ──> 数值/字幕/片尾/验证
                                                              ↑ 完全不变：SHA、18.2 m/s、217.0 m、
                                                                3034.487 m³、1447.018 m³、旁白、分镜
GeoTaichi（新，外部工具） ──> landslide_mpm.bytes ──> MpmParticleData.cs ──> 仅表现层：碎裂/剪切带
```

好处：`VALIDATION.md` 里"与 `unity_capture_v2` 逐帧差异为 0"这条断言**继续成立**，因为主数据确实没变；只需新增一条说明"MPM 粒子场为新增的独立机理可视化层，不参与任何数值指标"。

### 5.2 新增二进制格式建议（`LMPM0101`）

MPM 的历史量都在**粒子**上（背景网格每步重置），所以输出 SoA 定长数组，无拓扑：

```
magic "LMPM0101" (8B)
int32  frame_count, particle_count, reserved[2]   # 固定 N，禁止跨帧增删粒子
float32 time_s[frame_count]                       # 与现有 Times[] 完全对齐：0, 0.5, ... 45.0
per frame:
  pos[N][3]        f32   # 世界坐标 m，沿用现有 X/Z/Bed 坐标系
  eps_p[N]         f32   # 等效塑性应变 → 剪切带着色
  damage[N]        f32   # 张拉损伤 → 裂缝
  pore_pressure[N] f32   # 孔压 → 降雨因果链可视化
  block_id[N]      u16   # 由连通分量/断裂判据给出，跨帧稳定
  phase[N]         u8    # 表土/风化层/基岩/块体
```

- **粒子 ID 必须跨帧稳定**，否则块体着色与轨迹会闪烁。求解器若不带 ID，导出时按初始索引固定写入。
- 数据量：8 万粒子 × (12+4+4+4+2+1)=27 B ≈ **2.2 MB/帧 × 91 ≈ 195 MB**；建议逐帧 zstd 压缩或按需裁剪字段（`vel` 可省，速度由 `SimulationData.Sample()` 提供）。
- 转换脚本放在 `landslide_demo/export_mpm.py`，从求解器的 HDF5 / VTU（用 `h5py` 或 `meshio`）转 raw binary；这一步是整条链路里**风险最低**的一环。
- 事实标准只有两类：**VTK 家族**（Anura3D 每步输出 Mesh/MP-Scalar/Vector/Tensor 四个 `.VTK`，CB-Geo 输出 VTP）与 **HDF5**（CB-Geo checkpoint、MaterialPointSolver.jl）。没有跨求解器统一的 MPM 帧格式，所以自定义是对的。

### 5.3 Unity 侧渲染（Built-in，离线逐帧）

- 新增 `MpmParticleData.cs`（只读新文件）与 `MpmParticleRenderer.cs`；**不改 `SimulationData.cs` 一个字节**。
- 主方案：`StructuredBuffer` + `Graphics.DrawProcedural` 做**椭球/点 splatting**，顶点着色器按 `ε_p`（剪切带）、`damage`（裂缝）、`block_id`（块体）上色。你们是**离线导出 720 张 PNG**，没有实时帧率约束，10⁵ 粒子在 Built-in 完全够用，也不需要迁移 URP。
- **不要用 marching cubes / metaball 做主渲染**：等值面会把已经分离的碎块重新"粘"成一体，恰好毁掉你要的碎裂感。仅在远景需要连续土体表面时作为第二遍可选。
- **不要退化成高度场**：那等于把三维应力信息压回 2.5D，与现有深度平均同级，白走一趟。
- 最小可行版（零 Unity 风险）：先在 Python 侧把 MPM 剖面渲染成 2–3 s 的**机理插画序列**（ε_p 云图 + 剪切带标注 + 孔压曲线），插进分镜；`render_unity_preview.py` 加一段合成即可。适合作为阶段 0 的验收物。

## 6. 如果走"完整单轨 C"：必须付的代价

即把 MPM 结果**作为唯一数值来源**，替换 `landslide.bytes`。逐项清单，缺一不可：

| 文件 | 要改什么 |
|---|---|
| `simulate_3d.py` / `export_unity.py` | header 升 `LSIM2D02`，字段从 `(h,ux,uy)` 换成粒子态 + 重建的宏观统计；或保留场但改数值语义 |
| `SimulationData.cs` | `Spatial()` / `BedHeight()` / `RegionVolume()` / `FrontAdvance()` 全部重写；`VisualDepthScale=5f` 的显示假设失效 |
| `LandslideSurface.cs` | 连续网格 `SetState()` 与 `BuildCracks()` 换成粒子驱动表面，或整套替换 |
| `SceneController.cs` / `HouseDestruction.cs` | 房屋/树的响应触发条件现在来自粒子接触而不是 `h` 阈值，需重新标定 |
| `validate_presentation.py` | **"逐帧差异为 0"与固定 SHA 断言必须删除**，改为新基线比对；质量守恒断言换成粒子质量守恒 |
| `VALIDATION.md` | 新增「数值模型验证」章节，与「表现层验证」分开；写明旧版逐帧一致性不再适用 |
| `PRESENTATION.md` | 边界声明重写：现在**可以**说"求解了应变软化与多级剪切带"，但仍**不能**说"实地标定/预测"；旧版"表现层裂缝不代表多剪切面"那句要删 |
| `STORYBOARD.md` + `narration_cues.json` + TTS | 所有数字与机理表述重生成；`generate_narration.ps1` 复跑 |
| `render_unity_preview.py` | 片尾 18.2 m/s、217.0 m、3034.487 m³、1447.018 m³ 全部重算 |
| `README.md` / `metadata_3d.json` | 数值检查、模型描述、"质量相对误差≈0"等表述重写 |
| `ASSET_SOURCES.md` | 新增求解器来源、版本、许可、commit hash 与参数表 |

### 数值验证体系要重建（这是最容易被低估的部分）

1. **网格收敛**：Δx = 2 / 1 / 0.5 m 三次，比较前缘推进与剪切带条数；软化本构无正则化时必然不收敛，要如实记录。
2. **时间步收敛**：Δt 减半，关键量变化 < 阈值。
3. **质量守恒**：MPM 粒子质量天然守恒，但要检查网格重置与边界逃逸造成的漂移；**能量不再守恒**（塑性耗散），原来的势能/动能曲线解释要改写。
4. **基准算例**：颗粒柱坍塌（column collapse，GeoTaichi / Anura3D 都有现成算例）、一维固结（Terzaghi 解析解）、三轴压缩。
5. **交叉验证**：把 MPM 重建的前缘位置与现有深度平均解对比，作为"两种模型一致性讨论"，**不要写成"误差为 0"**。
6. **可复现性**：固定随机种子、记录求解器 commit、输入 JSON、参数表、硬件与后端。

**参数标定风险**：现有模型只有一个 `BASAL_FRICTION = tan20°` 标量，MPM 至少要十余个参数且无实测支撑。按你们一贯写法，`README.md` 与片尾必须保留「参数与破坏阈值为教学演示设定，不构成实地预测」。换求解器**不会**自动提升物理可信度，只会增加需要声明的假设。

## 7. 分阶段落地与止损点

| 阶段 | 内容 | 验收标准 | 止损 |
|---|---|---|---|
| **0 预研**（1–2 天） | 在**独立目录** `mpm_lab/` 装 GeoTaichi，跑通官方 `Progressive failure process of sensitive clay` 与 `Column collapse`；不碰主工程任何文件 | 能在 ParaView 看到剪切带与多级后退 | 跑不通就退回路线 A，不进入阶段 1 |
| **1 机理算例**（2–4 天） | 用 §4 参数建 2D 纵剖面，注入 89 min 孔压场，算 5 s；观察是否出现 ≥2 条剪切带与张拉裂缝 | ε_p 剖面出现 ≥2 条独立高值带，且起裂时刻有先后 | 只有一条滑面 → 检查软化率、强度随机场、非局部正则化；仍不行则停在阶段 1，只做"单剪切带 + 真实大变形" |
| **2 全时长与导出**（2–3 天） | 算满 45 s / 91 帧，写 `export_mpm.py` 产出 `landslide_mpm.bytes` | 帧时间轴与 `Times[]` 逐项一致，粒子数跨帧恒定 | 数据量超预算 → 裁剪字段或降到 46 帧 |
| **3 Unity 双轨接入**（2–3 天） | 新增两个 C# 类 + splatting shader，先只做 2–3 s 剖面镜头 | 720 帧捕获不崩、`landslide.bytes` SHA 不变、旧断言全过 | 渲染效果不如路线 A 的假缝 → 保留 A 的块体化，只用 MPM 的 ε_p 云图做插画 |
| **4 叙事与文档**（1–2 天） | 分镜插入机理段、旁白改 1–2 句、`PRESENTATION.md` 加边界声明、`ASSET_SOURCES.md` 记许可 | 片尾数字仍与旧求解器独立积分相符；验证脚本通过 | — |

**只有阶段 0–2 全部通过，才值得讨论是否升级为完整单轨 C。** 完整单轨另加约一周的 §6 工作量，且会永久失去"逐帧差异为 0"这条最有说服力的验证结论——对教学 demo 而言，这笔交换通常不划算。

## 8. 教学叙事的增量（C 真正的收益）

换成真实渐进破坏后，视频第一次可以**解释机理**而不只是演示现象。建议在现有七段分镜里插入/替换 2–3 s：

- 3.5–5.5 s：切到**纵剖面透视**，ε_p 云图沿坡脚亮起并向上溯源，旁白改为「孔压上升，剪切带从坡脚逐级向上扩展」，字幕保留"安全系数 0.999"。
- 5.5–7 s：第二条、第三条剪切带依次亮起，标出"多级滑面"，与地表拉裂缝对应。
- 15–18 s 低角度侧视改成块体翻滚 + 叠压特写，此时 `block_id` 可直接驱动块体描边。
- 片尾结果卡可加一行「剪切带 3 条 / 溯源起裂 t₁=… t₂=… t₃=…」，这是原模型给不出的新指标，也是论文里可写的一节。

`narration_cues.json` 只需改 `failure` 与 `runout` 两条文案、时间点可保持不变；`generate_narration.ps1` 复跑即可，音轨结构与音量包络不用动。

## 9. 参考资源

**求解器**（地址与许可均于 2026-11-16 检索核实）
- GeoTaichi · GPL-3.0 · https://github.com/Yihao-Shi/GeoTaichi
- Anura3D_OpenSource · LGPL-3.0-or-later · https://github.com/Anura3D/Anura3D_OpenSource · 官网 https://www.anura3d.com/
- CB-Geo MPM · MIT · https://github.com/cb-geo/mpm · 文档 https://cb-geo.github.io/mpm-doc/ · DesignSafe "MPM Landslide" 用例 https://pprd.designsafe-ci.org/user-guide/usecases/kumar/usecase/
- MaterialPointSolver.jl · MIT · https://github.com/LandslideSIM/MaterialPointSolver.jl
- YADE · GPL-2.0 · https://www.yade-dem.org/ ；LIGGGHTS-PUBLIC · GPL-2.0+ · https://github.com/CFDEMproject/LIGGGHTS-PUBLIC（3.8.0 / 2017 后基本停滞）

**方法与验证依据**
- Soga, Alonso, Yerro, Kumar, Bandara (2016), *Trends in large-deformation analysis of landslide mass movements with particular emphasis on the material point method*, Géotechnique 66(3) 248–273 · 开放全文 https://zenodo.org/record/546613/files/2016_geotechnique.pdf —— 连续/叠加剪切带、超孔压、渐进破坏的总纲。
- Wang, Vardon, Hicks (2016), *Investigation of retrogressive and progressive slope failure mechanisms using the material point method*, Computers and Geotechnics —— 区分溯源式与渐进式，直接对应"多级滑面"。
- Liang, Chandra, Soga (2022), *Shear band evolution and post-failure simulation by XMPM*, CMAME —— 显式强不连续、自接触，是"真块体"的理论路径（目前无成熟开源实现）。
- Sordo, Rathje, Kumar (2024), *Sequential hybrid finite element and material point method to simulate slope failures*, Computers and Geotechnics 173, 106525 · [arXiv:2407.05185](https://arxiv.org/abs/2407.05185) —— FEM 起裂 + MPM 运移的顺序混合；**配套开放数据集** DesignSafe PRJ-4684 含 FEM/MPM 输入、输出与转移脚本：https://www.designsafe-ci.org/data/browser/public/designsafe.storage.published/PRJ-4684 。若阶段 1 发现 MPM 单独难以稳定起裂，这是最直接的现成参考。
- *Prediction of retrogressive landslide in sensitive clays by incorporating a novel strain softening law into the MPM* (2024), Engineering Geology —— 软化律实现于 Anura3D，与选型直接相关。
- *Rainfall-induced slope collapse with coupled material point method* (2017), Engineering Geology；*Investigation of rainfall intensity on the slope failure process using GPU-accelerated coupled MPM* (2023), Computers and Geotechnics —— 降雨入渗 → 孔压 → 溯源破坏的耦合先例，支持 §4.1 的做法。
- r.avaflow v4 (2025), GMD 18, 9879–9896 · https://gmd.copernicus.org/articles/18/9879/2025/ —— 作为对照：多相 + entrainment + fragmentation 仍是深度平均，不含三维破裂力学。

**尚需确认（不要写成已核实）**：Anura3D 的 GiD 学术许可具体条款；GeoWarp 未见明确 LICENSE；MaterialPointSolver.jl 论文算例是否含剪切带/软化；CB-Geo 主仓库"维护放缓"的准确程度；Uintah MPM 组件的近期活跃度与官方滑坡算例清单。

## 10. 决策建议

- 想要"画面碎裂" → 路线 A，别走 C。
- 想要"论文里能写渐进破坏与多级滑面" → 走 **C-lite 双轨**：阶段 0 先花 1–2 天验证剪切带能否自发出现，通过后再接入，主数据、`VALIDATION.md` 的逐帧断言、`fd17e70a…` 这条 SHA、片尾数字、六段旁白全部保留。
- 只有课题明确要求"以破裂力学结果为唯一数值来源"时，才走完整单轨 C，并按 §6 逐项重建验证体系。

无论走哪条，沿用你们已经做对的写法：在 `PRESENTATION.md` 用一句话划清边界。C-lite 应写「主数值结果仍来自二维深度平均求解器；MPM 剪切带、多级滑面与块体来自独立的物质点法机理算例，用于可视化，不参与任何片尾数值指标」。只有做到完整单轨 C，才能说"本片数值结果来自求解了应变软化的物质点法"。
