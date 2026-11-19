using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LandslideDemo.Editor
{
    /// <summary>Checks the actual generated geometry, rather than reimplementing its formulas.</summary>
    public static class ValidateSurfacePresentation
    {
        [MenuItem("Landslide Demo/Validate Surface Presentation")]
        public static void Run()
        {
            var data = new SimulationData(Path.Combine(Application.streamingAssetsPath, "landslide.bytes"));
            float[] originalBed = (float[])data.Bed.Clone();
            Shader terrain = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/VertexTerrain.shader");
            Shader soil = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/FracturedSoil.shader");
            Require(terrain != null && soil != null, "Custom terrain shaders are missing.");
            Require(!ShaderUtil.ShaderHasError(terrain) && !ShaderUtil.ShaderHasError(soil), "A terrain shader has compiler errors.");
            var root = new GameObject("Temporary surface validation");
            try
            {
                var surface = root.AddComponent<LandslideSurface>(); surface.Initialize(data, terrain, soil);
                root.AddComponent<PresentationEnvironment>().Initialize(data, terrain);
                Vector3[] original = surface.TerrainMesh.vertices;
                Require(original.Length == data.NX * data.NZ, "The numerical grid was changed.");
                for (int k = 0; k < original.Length; k++)
                {
                    float x = data.X[k / data.NZ], z = data.Z[k % data.NZ];
                    Require(data.Bed[k] == originalBed[k], "The imported numerical bed was modified.");
                    Require(original[k] == PresentationLayout.Point(x, data.Bed[k] + data.VisualGroundOffset(x, z), z),
                        "Display terrain differs from the display layout.");
                    Require(Mathf.Abs(PresentationLayout.SolverZ(x,original[k].z)-z)<.0001f,
                        "The render-only lateral layout does not invert.");
                }
                int meshCount = 0, checks = 0;
                foreach (float seconds in new[] { 0f, 3.5f, 7.5f, 12.5f, 22f, data.TimeMax })
                {
                    surface.SetState(seconds, true);
                    Require(surface.MaximumBoundaryTopGap(true) < .0001f,
                        "Moving boundary wall detached from its Voronoi block at t=" + seconds);
                    foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
                    {
                        CheckMesh(filter.sharedMesh); checks++;
                        if (seconds == 0) meshCount++;
                    }
                }
                MeshRenderer[] tiers = Array.FindAll(root.GetComponentsInChildren<MeshRenderer>(true),
                    r => r.name.StartsWith("Progressive source scarp tier "));
                Require(tiers.Length == 4, "Expected four display-only source scarp tiers.");
                foreach (float seconds in new[] { 0f, 3.5f, 7.5f, 12.5f })
                {
                    surface.SetState(seconds, true);
                    int visible = Array.FindAll(tiers, r => r.enabled).Length;
                    int expected = seconds < 1f ? 0 : (seconds < 7f ? 2 : (seconds < 12f ? 3 : 4));
                    Require(visible == expected, "Source scarp tier activation disagrees with playback time " + seconds);
                }
                surface.SetState(3f, true);
                int tensile = surface.OpenFractureBlockCount;
                Require(tensile > 0, "The imported source velocity field did not open any tension blocks.");
                surface.SetState(12f, true);
                int compressive = surface.CompressiveBlockCount, shearing = surface.ShearingBlockCount;
                Require(compressive > 0 && shearing > 0, "The imported field did not drive compression and shear blocks.");
                MeshFilter blocks = Array.Find(root.GetComponentsInChildren<MeshFilter>(),
                    f => f.sharedMesh.name.Contains("Velocity-strain Voronoi"));
                MeshFilter seams = Array.Find(root.GetComponentsInChildren<MeshFilter>(),
                    f => f.sharedMesh.name.Contains("Layered fracture interiors"));
                Require(blocks != null && surface.FracturedCellCount > 0, "Strain blocks were not created.");
                Require(seams != null, "Layered fracture mesh was not created.");
                Vector3[] firstBlocks = blocks.sharedMesh.vertices;
                Vector3[] firstSeams = seams.sharedMesh.vertices;
                int[] firstSeamTriangles = seams.sharedMesh.triangles;
                Require(firstBlocks.Length == surface.FracturedVertexCount &&
                    firstBlocks.Length < surface.FracturedCellCount * 4,
                    "Fractured cells did not share vertices within each Voronoi block.");
                surface.SetState(3f, true);
                surface.SetState(12f, true);
                Vector3[] repeatedBlocks = blocks.sharedMesh.vertices;
                Require(firstBlocks.Length == repeatedBlocks.Length, "Seeking changed block topology at the same time.");
                for (int k = 0; k < firstBlocks.Length; k++)
                    Require((firstBlocks[k] - repeatedBlocks[k]).sqrMagnitude < .0000001f,
                        "Strain blocks did not reproduce after seeking.");
                int[] repeatedSeamTriangles = seams.sharedMesh.triangles;
                Vector3[] repeatedSeams = seams.sharedMesh.vertices;
                Require(firstSeamTriangles.Length == repeatedSeamTriangles.Length,
                    "Fracture visibility changed after seeking to the same time.");
                for (int k = 0; k < firstSeamTriangles.Length; k++)
                    Require(firstSeamTriangles[k] == repeatedSeamTriangles[k],
                        "Fracture topology changed after seeking to the same time.");
                int referencedSeamVertices = firstSeamTriangles.Length / 42 * 22;
                for (int k = 0; k < referencedSeamVertices; k++)
                    Require((firstSeams[k] - repeatedSeams[k]).sqrMagnitude < .0000001f,
                        "Visible fracture geometry did not reproduce after seeking.");
                surface.SetState(0f, false);
                Require(surface.MaximumBoundaryTopGap(false) < .0001f,
                    "Boundary wall did not reattach to the continuous surface after rewind.");
                Require(Array.FindAll(tiers, r => r.enabled).Length == 0,
                    "Source scarp tiers remained visible after rewind.");
                MeshFilter skirt = Array.Find(root.GetComponentsInChildren<MeshFilter>(), f => f.sharedMesh.name.Contains("Five ring"));
                Require(skirt != null, "Landscape skirt was not created.");
                Require(skirt.sharedMesh.bounds.size.x > data.MaxX - data.MinX + 200, "Landscape does not extend beyond the numerical domain.");
                MeshFilter environment = Array.Find(root.GetComponentsInChildren<MeshFilter>(), f => f.sharedMesh.name.Contains("continuous with landscape skirt"));
                Require(environment != null, "Surrounding landscape was not created.");
                Vector3[] outerRing = surface.LandscapeBoundary, continuation = environment.sharedMesh.vertices;
                Vector3[] outerNormals = surface.LandscapeBoundaryNormals, continuationNormals = environment.sharedMesh.normals;
                for (int i = 0; i < outerRing.Length; i++)
                {
                    Require(outerRing[i] == continuation[i], "Landscape seam position mismatch.");
                    Require(outerNormals[i] == continuationNormals[i], "Landscape seam shading mismatch.");
                }
                Require(environment.GetComponent<MeshRenderer>().sharedMaterial == surface.TerrainMaterial, "The surrounding landscape must share terrain textures.");
                // Imported Poly Haven maps must really resolve through Resources in
                // both Editor and Player; the generated 256px set remains a fallback.
                foreach (string layer in new[] { "grass", "earth", "rock" })
                {
                    foreach (string suffix in new[] { "_albedo_smoothness", "_normal_gl" })
                    {
                        string path = "SurfacePBR/" + layer + suffix;
                        var imported = Resources.Load<Texture2D>(path);
                        Require(imported != null && imported.width == 2048 && imported.height == 2048,
                            "Missing 2K photographed terrain texture: " + path);
                        Require(imported.mipmapCount > 1 && imported.wrapMode == TextureWrapMode.Repeat,
                            "Imported terrain texture is not tiled with mipmaps: " + path);
                    }
                    string normalPath = "Assets/Resources/SurfacePBR/" + layer + "_normal_gl.png";
                    var normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
                    Require(normalImporter != null && !normalImporter.sRGBTexture,
                        "GL normal map must be imported as linear color: " + normalPath);
                }
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
                {
                    Material material = renderer.sharedMaterial;
                    foreach (string property in new[] { "_EarthMap", "_EarthNormal" })
                    {
                        if (!material.HasProperty(property)) continue;
                        var texture = material.GetTexture(property) as Texture2D;
                        Require(texture != null && texture.width == 2048 && texture.mipmapCount > 1, "Photographed PBR texture or mipmaps missing: " + property);
                        Require(texture.wrapMode == TextureWrapMode.Repeat, "Triplanar texture must tile.");
                    }
                }
                Debug.Log("STRAIN_SURFACE_VALIDATED tensionBlocks=" + tensile + " compressionBlocks=" +
                    compressive + " shearBlocks=" + shearing + " cells=" + surface.FracturedCellCount +
                    " sharedVertices=" + surface.FracturedVertexCount +
                    " scarpTiers=4 seekAndRewind=true");
                Debug.Log("SURFACE_PRESENTATION_VALIDATED grid=" + data.NX + "x" + data.NZ +
                    " meshes=" + meshCount + " meshStateChecks=" + checks +
                    " solverBedUnchanged=true visualOffsetOnly=true textured=true seamlessLandscape=true");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static void CheckMesh(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices, normals = mesh.normals; Vector4[] tangents = mesh.tangents;
            Require(normals.Length == vertices.Length && tangents.Length == vertices.Length, mesh.name + ": normal/tangent count mismatch.");
            for (int i = 0; i < vertices.Length; i++)
            {
                Require(Finite(vertices[i].x) && Finite(vertices[i].y) && Finite(vertices[i].z), mesh.name + ": invalid vertex.");
                Require(Finite(normals[i].sqrMagnitude), mesh.name + ": invalid normal.");
                // Unused dynamic-grid vertices need no tangent frame, but all initialized ones do.
                if (normals[i].sqrMagnitude < .1f) continue;
                Vector3 tangent = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                Require(Mathf.Abs(Vector3.Dot(normals[i], tangent)) < .02f, mesh.name + ": tangent is not orthogonal.");
                Require(tangent.sqrMagnitude > .95f, mesh.name + ": missing tangent frame.");
            }
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Require(a >= 0 && b >= 0 && c >= 0 && a < vertices.Length && b < vertices.Length && c < vertices.Length, mesh.name + ": triangle index outside vertex buffer.");
                Vector3 geometric = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                if (geometric.sqrMagnitude < .0000001f) continue; // A vanishing wet front may close a boundary wall.
                Vector3 interpolated = normals[a] + normals[b] + normals[c];
                Require(Vector3.Dot(geometric, interpolated) >= -.0001f, mesh.name + ": triangle winding opposes its shading normal. triangle=" + (i / 3) +
                    " indices=" + a + "," + b + "," + c + " positions=" + vertices[a].ToString("F3") + ";" + vertices[b].ToString("F3") + ";" + vertices[c].ToString("F3") +
                    " geometric=" + geometric.ToString("F3") + " shading=" + interpolated.ToString("F3") + " normals=" + normals[a].ToString("F3") + ";" + normals[b].ToString("F3") + ";" + normals[c].ToString("F3"));
            }
        }

        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
