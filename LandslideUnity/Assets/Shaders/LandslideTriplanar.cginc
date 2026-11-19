#ifndef LANDSLIDE_TRIPLANAR_INCLUDED
#define LANDSLIDE_TRIPLANAR_INCLUDED

float3 TerrainBlendWeights(float3 normal)
{
    float3 weights = pow(abs(normal), 4);
    return weights / max(dot(weights, float3(1,1,1)), 0.0001);
}

float TerrainHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

// Unbounded world-space noise prevents the photographed map from repeating
// as a visible slope-wide wallpaper. A 42 m patch is clear from the overview.
float TerrainMacroNoise(float2 worldXZ)
{
    float2 p = worldXZ / 42.0;
    float2 cell = floor(p), f = frac(p);
    f = f * f * (3 - 2 * f);
    return lerp(lerp(TerrainHash(cell), TerrainHash(cell + float2(1, 0)), f.x),
                lerp(TerrainHash(cell + float2(0, 1)), TerrainHash(cell + 1), f.x), f.y);
}

float4 SampleTerrainPlanar(sampler2D map, float2 uv)
{
    // Two incommensurate, rotated grids prevent a small generated map reading as tiles.
    float2 rotated = float2(.819152 * uv.x - .573576 * uv.y, .573576 * uv.x + .819152 * uv.y);
    return tex2D(map, uv) * .5 + tex2D(map, rotated * 1.371 + float2(.317, .683)) * .5;
}

float4 SampleTerrainTriplanar(sampler2D map, float3 position, float3 weights, float scale)
{
    return SampleTerrainPlanar(map, position.zy * scale) * weights.x
         + SampleTerrainPlanar(map, position.xz * scale) * weights.y
         + SampleTerrainPlanar(map, position.xy * scale) * weights.z;
}

// Generated normals are uncompressed linear RGB maps, rather than DXT5nm imports.
float3 TerrainNormalDetail(sampler2D map, float3 position, float3 weights, float scale)
{
    float2 nx = tex2D(map, position.zy * scale).rg * 2 - 1;
    float2 ny = tex2D(map, position.xz * scale).rg * 2 - 1;
    float2 nz = tex2D(map, position.xy * scale).rg * 2 - 1;
    return float3(0, nx.y, nx.x) * weights.x
         + float3(ny.x, 0, ny.y) * weights.y
         + float3(nz.x, nz.y, 0) * weights.z;
}

float3 TerrainTangentNormal(float3 detail, float3 n, float3 t, float3 b, float strength)
{
    float3 perturbed = normalize(n + (detail - n * dot(detail, n)) * strength);
    return normalize(float3(dot(perturbed, t), dot(perturbed, b), dot(perturbed, n)));
}
#endif
