using UnityEngine;
using UnityEngine.Rendering;

namespace LandslideDemo
{
    /// <summary>Decorative landscape continuing the surface skirt; never sampled by physics.</summary>
    public sealed class PresentationEnvironment : MonoBehaviour
    {
        Material sky, landscape;
        Mesh mesh;
        public void Initialize(SimulationData data, Shader terrainShader)
        {
            Shader shader = Resources.Load<Shader>("Shaders/OvercastSky");
            if (shader == null) throw new System.InvalidOperationException("Missing overcast sky shader");
            sky = new Material(shader) { name = "Layered overcast teaching sky" };
            RenderSettings.skybox = sky;
            var surface = GetComponentInChildren<LandslideSurface>();
            if (surface == null || surface.LandscapeBoundary == null)
                throw new System.InvalidOperationException("Initialize the numerical surface and skirt before its environment.");
            // The skirt owns this material and texture set; the environment only borrows them.
            landscape = surface.TerrainMaterial;
            BuildLandscape(data, surface);
            var probeObject = new GameObject("Sky reflection probe");
            probeObject.transform.SetParent(transform, false);
            probeObject.transform.position = new Vector3(210, 110, 0);
            var probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.resolution = 128; probe.size = new Vector3(1100, 600, 850);
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.cullingMask = 0; probe.intensity = .65f;
        }

        void BuildLandscape(SimulationData data, LandslideSurface surface)
        {
            // Ring zero reuses every actual outer-skirt vertex: no overlapping near surface.
            // Preserve its full perimeter resolution so even the narrow domain ends meet exactly.
            Vector3[] boundary = surface.LandscapeBoundary, boundaryNormals = surface.LandscapeBoundaryNormals;
            int segments = boundary.Length;
            float[] offsets = { 0, 12, 25, 39, 55, 73, 93, 115, 139, 165, 193,
                223, 255, 290, 328, 369, 414, 463, 517, 577, 643, 716,
                796, 884, 981, 1088, 1205, 1334, 1475, 1630, 1800 };
            int rings = offsets.Length;
            var vertices = new Vector3[segments * rings];
            var colors = new Color[vertices.Length];
            var landscapeFade = new Vector2[vertices.Length];
            float centerX = (data.MinX + data.MaxX) * .5f, centerZ = (data.MinZ + data.MaxZ) * .5f;
            float halfX = 1f, halfZ = 1f;
            foreach (Vector3 p in boundary)
            {
                halfX = Mathf.Max(halfX, Mathf.Abs(p.x - centerX));
                halfZ = Mathf.Max(halfZ, Mathf.Abs(p.z - centerZ));
            }
            for (int ring = 0; ring < rings; ring++)
            for (int i = 0; i < segments; i++)
            {
                Vector3 start = boundary[i];
                // Preserve spacing along each established edge. Unit radial
                // directions crowd segments on long sides toward the corners.
                float d = offsets[ring];
                float x = start.x + (start.x - centerX) / halfX * d;
                float z = start.z + (start.z - centerZ) / halfZ * d;
                // After a short transition, all rings sample one continuous
                // mountain height field. No mountain follows a radial ring.
                float initialSlope = -.13f * d;
                float y = Mathf.Lerp(start.y + initialSlope, ExteriorHeight(x, z), Edge(0, 220, d));
                vertices[ring * segments + i] = ring == 0 ? start : new Vector3(x, y, z);
                float tint = Mathf.Lerp(.87f, .99f, Edge(0, 420, d));
                colors[ring * segments + i] = new Color(tint, tint, tint, .25f);
                landscapeFade[ring * segments + i] = new Vector2(Edge(11, 320, d), 0);
            }
            var triangles = new int[(rings - 1) * segments * 6]; int n = 0;
            for (int ring = 0; ring < rings - 1; ring++)
            for (int i = 0; i < segments; i++)
            {
                int a = ring * segments + i, b = ring * segments + (i + 1) % segments, c = a + segments, e = b + segments;
                triangles[n++] = a; triangles[n++] = b; triangles[n++] = c;
                triangles[n++] = b; triangles[n++] = e; triangles[n++] = c;
            }
            mesh = new Mesh { name = "Surrounding valley continuous with landscape skirt" };
            mesh.vertices = vertices; mesh.colors = colors; mesh.uv2 = landscapeFade; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var normals = mesh.normals;
            for (int i = 0; i < segments; i++) normals[i] = boundaryNormals[i];
            mesh.normals = normals;
            var tangents = new Vector4[vertices.Length];
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 t = Vector3.Cross(Mathf.Abs(normals[i].z) < .95f ? Vector3.forward : Vector3.up, normals[i]).normalized;
                tangents[i] = new Vector4(t.x, t.y, t.z, 1);
            }
            mesh.tangents = tangents;
            var obj = new GameObject("Surrounding landscape - outside simulation"); obj.transform.SetParent(transform, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = landscape;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        static float ExteriorHeight(float x, float z)
        {
            // Irregularly spaced massifs on two axes leave lower passes between
            // their summits. Angled foothills cross the view at unequal depths.
            float floor = 15f + 16f * Mathf.PerlinNoise(x * .0024f + 84.5f, z * .0024f + 35.1f);
            float longitudinalGrade = Mathf.Clamp((220f - x) * .145f, -17f, 85f);
            float xWarp = x + 46f * (Mathf.PerlinNoise(x * .0021f + 5.1f, z * .0032f + 36.7f) - .5f);
            float zWarp = z + 57f * (Mathf.PerlinNoise(x * .0027f + 9.7f, z * .002f + 27.6f) - .5f);
            float northFore = Peak(xWarp, zWarp, -42, 358, 195, 133, -.45f, 120)
                            + Peak(xWarp, zWarp, 480, 335, 188, 112, .59f, 152);
            float northBack = Peak(xWarp, zWarp, -270, 660, 246, 213, .34f, 197)
                            + Peak(xWarp, zWarp, 274, 642, 208, 149, -.70f, 185)
                            + Peak(xWarp, zWarp, 755, 805, 271, 181, -.47f, 235);
            float west = Peak(xWarp, zWarp, -463, 34, 276, 219, -.73f, 121);
            float south = Peak(xWarp, zWarp, -146, -437, 218, 195, .42f, 126)
                        + Peak(xWarp, zWarp, 315, -532, 236, 184, -.12f, 101)
                        + Peak(xWarp, zWarp, 602, -348, 174, 153, .64f, 132);
            float mountains = northFore + northBack + west + south;
            float rill = Mathf.Abs(2f * Mathf.PerlinNoise(x * .0103f + 12.4f, z * .00453f + 9.8f) - 1f);
            float crags = Mathf.PerlinNoise(x * .0091f + 42, z * .0087f + 15.5f) - .5f;
            return floor + longitudinalGrade + mountains + (13f * crags - 11f * rill) * Edge(20, 120, mountains);
        }

        static float Peak(float x, float z, float centerX, float centerZ, float length, float width,
            float diagonal, float height)
        {
            float dx = (x - centerX + (z - centerZ) * diagonal) / length;
            float dz = (z - centerZ) / width;
            return height * Mathf.Exp(-dx * dx - dz * dz);
        }

        static float Edge(float low, float high, float value)
        {
            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(low, high, value));
        }

        void OnDestroy()
        {
            if (RenderSettings.skybox == sky) RenderSettings.skybox = null;
            // The shared landscape material and maps are released by LandslideSurface.
            if (sky != null) Release(sky); if (mesh != null) Release(mesh);
        }

        static void Release(Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
