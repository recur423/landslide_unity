# 本轮外部素材来源（2026-11-15）

本轮使用三套 Poly Haven 实拍地表 PBR 贴图。该站的[官方许可页](https://polyhaven.com/license)声明其 HDRI、贴图和模型均为 **CC0**，可商用且无需署名；为让教学作品的来源可核查，我们仍在文档及片尾保留「地表贴图：Poly Haven（CC0）」致谢。下载地址和 MD5 由[官方公开 API](https://polyhaven.com/our-api)取得，下载时逐个核对大小与 MD5。网站的素材页：[Leafy Grass](https://polyhaven.com/a/leafy_grass)、[Brown Mud 02](https://polyhaven.com/a/brown_mud_02)、[Rock Ground](https://polyhaven.com/a/rock_ground)。

| 用途 | 素材 / 作者 | 原图分辨率 | 使用的源图（官方直接下载） |
|---|---|---:|---|
| 草地 | Leafy Grass / Charlotte Baglioni | 2048² | [Diffuse](https://dl.polyhaven.org/file/ph-assets/Textures/jpg/2k/leafy_grass/leafy_grass_diff_2k.jpg) · [Normal (GL)](https://dl.polyhaven.org/file/ph-assets/Textures/jpg/2k/leafy_grass/leafy_grass_nor_gl_2k.jpg) · [Rough](https://dl.polyhaven.org/file/ph-assets/Textures/jpg/2k/leafy_grass/leafy_grass_rough_2k.jpg) |
| 湿土 | Brown Mud 02 / Rob Tuytel | 2048² | [Diffuse](https://dl.polyhaven.org/file/ph-assets/Textures/jpg/2k/brown_mud_02/brown_mud_02_diff_2k.jpg) · [Normal (GL)](https://dl.polyhaven.org/file/ph-assets/Textures/jpg/2k/brown_mud_02/brown_mud_02_nor_gl_2k.jpg) · [Rough](https://dl.polyhaven.org/file/ph-assets/Textures/jpg/2k/brown_mud_02/brown_mud_02_rough_2k.jpg) |
| 岩石 | Rock Ground / Rob Tuytel | 2048² | [Diffuse](https://dl.polyhaven.org/file/ph-assets/Textures/jpg/2k/rock_ground/rock_ground_diff_2k.jpg) · [Normal (GL)](https://dl.polyhaven.org/file/ph-assets/Textures/jpg/2k/rock_ground/rock_ground_nor_gl_2k.jpg) · [Rough](https://dl.polyhaven.org/file/ph-assets/Textures/jpg/2k/rock_ground/rock_ground_rough_2k.jpg) |

源文件的精确 URL、官方 MD5、字节数保存在 `Assets/Resources/SurfacePBR/source_manifest.json`。仓库内的六张 `*.png` 是通过 `../landslide_demo/fetch_surface_pbr.py` 从官方 2K JPG 派生：Diffuse 作为 RGB，`1 − Rough` 写入 albedo 的 alpha 通道作 smoothness，GL 法线保留 RGB；生成阶段未增添第三方图像内容。Unity 的 `SurfaceTextureImporter` 将 albedo 按 sRGB、法线按 linear 导入，且对 2K 图启用 mipmap；运行时缺少图片时仍使用原有程序生成贴图。

本轮没有引入外部 HDRI、树木/房屋模型或声音录音。天空、模型细节和合成音效仍由项目代码生成；旁白若使用系统 TTS，也不应写作外部素材。重建资源时运行 `python ../landslide_demo/fetch_surface_pbr.py`。再次获取时以官方许可页和 API 返回值为准。
