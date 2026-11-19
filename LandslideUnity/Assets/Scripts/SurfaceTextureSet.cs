using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandslideDemo
{
    /// <summary>CC0 photographed PBR maps with deterministic generated fallback.</summary>
    public sealed class SurfaceTextureSet : IDisposable
    {
        const int Size = 256;
        readonly List<Texture2D> textures = new List<Texture2D>();
        readonly Texture2D[] albedo = new Texture2D[3], normal = new Texture2D[3];

        public SurfaceTextureSet()
        {
            string[] names = { "grass", "earth", "rock" };
            for (int layer = 0; layer < 3; layer++)
            {
                string path = "SurfacePBR/" + names[layer];
                Texture2D importedAlbedo = Resources.Load<Texture2D>(path + "_albedo_smoothness");
                Texture2D importedNormal = Resources.Load<Texture2D>(path + "_normal_gl");
                if (importedAlbedo != null && importedNormal != null &&
                    importedAlbedo.width == importedNormal.width && importedAlbedo.height == importedNormal.height)
                {
                    albedo[layer] = importedAlbedo;
                    normal[layer] = importedNormal;
                }
                else Generate(layer);
            }
        }

        public void Apply(Material material)
        {
            string[] names = { "_Grass", "_Earth", "_Rock" };
            for (int i = 0; i < names.Length; i++)
            {
                if (material.HasProperty(names[i] + "Map")) material.SetTexture(names[i] + "Map", albedo[i]);
                if (material.HasProperty(names[i] + "Normal")) material.SetTexture(names[i] + "Normal", normal[i]);
            }
        }

        void Generate(int layer)
        {
            var heights = new float[Size * Size];
            var colors = new Color[heights.Length];
            var normals = new Color[heights.Length];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float coarse = Noise(x, y, 4, 41 + layer * 71);
                float middle = Noise(x, y, 16, 83 + layer * 37);
                float fine = Noise(x, y, 64, 197 + layer * 19);
                float grain = Hash(x, y, 331 + layer * 29);
                float n = coarse * .46f + middle * .29f + fine * .17f + grain * .08f;
                Color dark, light;
                float roughness;
                if (layer == 0)
                {
                    dark = new Color(.17f, .28f, .16f); light = new Color(.41f, .49f, .29f);
                    float stem = Edge(.58f, .82f, Noise(x * 3, y, 32, 613));
                    colors[x + y * Size] = Color.Lerp(Color.Lerp(dark, light, n), new Color(.45f, .44f, .31f), stem * .12f);
                    heights[x + y * Size] = .3f * middle + .4f * fine + .3f * grain;
                    roughness = .88f;
                }
                else if (layer == 1)
                {
                    dark = new Color(.225f, .162f, .105f); light = new Color(.53f, .405f, .265f);
                    float pebble = Edge(.64f, .77f, middle);
                    colors[x + y * Size] = Color.Lerp(Color.Lerp(dark, light, n), new Color(.48f, .465f, .395f), pebble * .65f);
                    heights[x + y * Size] = .46f * middle + .26f * fine + .2f * pebble + .08f * grain;
                    roughness = .94f;
                }
                else
                {
                    dark = new Color(.26f, .27f, .255f); light = new Color(.58f, .555f, .475f);
                    float vein = Edge(.46f, .51f, coarse) - Edge(.53f, .58f, coarse);
                    colors[x + y * Size] = Color.Lerp(dark, light, n) * Mathf.Lerp(1f, .68f, vein);
                    heights[x + y * Size] = .58f * middle + .23f * fine + .1f * grain - .09f * vein;
                    roughness = .83f;
                }
                Color c = colors[x + y * Size];
                // Alpha stores smoothness; RGB normals use a separate linear map.
                c.a = (1f - roughness) * Mathf.Lerp(.7f, 1.3f, fine);
                colors[x + y * Size] = c;
            }
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = heights[(x + 1) % Size + y * Size] - heights[(x + Size - 1) % Size + y * Size];
                float dy = heights[x + (y + 1) % Size * Size] - heights[x + (y + Size - 1) % Size * Size];
                Vector3 n = new Vector3(-dx * 1.25f, -dy * 1.25f, 1f).normalized;
                normals[x + y * Size] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1f);
            }
            albedo[layer] = MakeTexture("Generated " + layer + " albedo-smoothness", colors, false);
            normal[layer] = MakeTexture("Generated " + layer + " normal", normals, true);
        }

        Texture2D MakeTexture(string name, Color[] pixels, bool linear)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true, linear)
            { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            texture.SetPixels(pixels); texture.Apply(true, true); textures.Add(texture); return texture;
        }

        static float Noise(int x, int y, int cells, int seed)
        {
            float u = x / (float)Size * cells, v = y / (float)Size * cells;
            int i = Mathf.FloorToInt(u), j = Mathf.FloorToInt(v);
            float a = Mathf.SmoothStep(0f, 1f, u - i), b = Mathf.SmoothStep(0f, 1f, v - j);
            int ix = (i % cells + cells) % cells, jy = (j % cells + cells) % cells;
            return Mathf.Lerp(Mathf.Lerp(Hash(ix, jy, seed), Hash((ix + 1) % cells, jy, seed), a),
                Mathf.Lerp(Hash(ix, (jy + 1) % cells, seed), Hash((ix + 1) % cells, (jy + 1) % cells, seed), a), b);
        }

        static float Edge(float low, float high, float value)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(low, high, value));
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0x00ffffff) / 16777215f;
            }
        }

        public void Dispose()
        {
            foreach (Texture2D texture in textures)
                if (texture != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
                    else UnityEngine.Object.DestroyImmediate(texture);
                }
            textures.Clear();
        }
    }
}
