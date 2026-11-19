using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LandslideDemo
{
    /// <summary>Deterministic scenery with shared generated surface textures.</summary>
    public static class ProceduralAssets
    {
        private static Material bark;
        private static Material[] leaves;

        public static Material CreateMaterial(string name, Color color, float smoothness = 0.0f)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse");
            var material = new Material(shader) { name = name, color = color, enableInstancing = true };
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", Mathf.Clamp01(smoothness));
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            SceneryTextures.Apply(material, name);
            return material;
        }

        /// <summary>A centered irregular rock approximately one metre across; scale the instance as needed.</summary>
        public static Mesh CreateRockMesh(int seed, float roughness = 0.25f)
        {
            var random = new System.Random(seed);
            List<Vector3> vertices;
            List<int> triangles;
            Icosphere(1, out vertices, out triangles);
            float noise = Mathf.Clamp(roughness, 0f, 0.7f);
            float sx = Range(random, 0.95f, 1.25f);
            float sy = Range(random, 0.65f, 0.95f);
            float sz = Range(random, 0.85f, 1.15f);
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 v = vertices[i] * (0.5f * Range(random, 1f - noise, 1f + noise));
                v = Vector3.Scale(v, new Vector3(sx, sy, sz));
                v.x += 0.08f * v.y;
                v.y = Mathf.Max(v.y, -0.31f * sy);
                vertices[i] = v;
            }
            return FlatMesh(vertices, triangles, "Rock_" + seed);
        }

        /// <summary>
        /// Creates a tree with its pivot at the base. Position is in world coordinates;
        /// scale applies uniformly to the whole movable tree, including all branches.
        /// It uses two to four renderers and intentionally has no foliage colliders.
        /// </summary>
        public static GameObject CreateTree(Transform parent, Vector3 position, float scale, int seed)
        {
            EnsureTreeMaterials();
            var random = new System.Random(seed);
            int crownVariant = (seed ^ (seed >> 3)) & 3;
            var root = new GameObject("Tree_" + seed);
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            root.transform.localRotation = Quaternion.Euler(0f, Range(random, 0f, 360f), 0f);
            root.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

            // Four repeatable silhouettes: rounded, tall, wide, and sparse.
            float height = Range(random, 5.1f, 6.8f) * (crownVariant == 1 ? 1.13f : 1f);
            float radius = Range(random, 0.19f, 0.27f);
            Vector3 lean = new Vector3(Range(random, -0.3f, 0.3f), 0f, Range(random, -0.3f, 0.3f));
            var wood = new List<CombineInstance>();
            var sourceMeshes = new List<Mesh>();
            Mesh trunk = TaperedCylinder(height, radius, radius * 0.27f, 7);
            sourceMeshes.Add(trunk);
            wood.Add(new CombineInstance
            {
                mesh = trunk,
                transform = Matrix4x4.TRS(Vector3.zero,
                    Quaternion.FromToRotation(Vector3.up, Vector3.up * height + lean), Vector3.one)
            });

            var crowns = new List<CombineInstance>[]
            {
                new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>()
            };
            Mesh crown = CrownMesh(seed ^ 0x32a1);
            int branchCount = crownVariant == 3 ? 4 : random.Next(5, 8);
            for (int i = 0; i < branchCount; i++)
            {
                float angle = i * 2.399963f + Range(random, -0.25f, 0.25f);
                float level = Mathf.Lerp(0.4f, 0.76f, i / (float)Math.Max(1, branchCount - 1));
                float reach = Range(random, 1.15f, 1.85f) * (1.1f - level * 0.25f) *
                    (crownVariant == 1 ? .80f : (crownVariant == 2 ? 1.24f : 1f));
                Vector3 start = Vector3.up * (height * level) + lean * level;
                Vector3 end = start + new Vector3(Mathf.Cos(angle) * reach,
                    Range(random, 0.5f, 1.1f), Mathf.Sin(angle) * reach);
                Vector3 direction = end - start;
                Mesh branch = TaperedCylinder(direction.magnitude, radius * 0.45f,
                    radius * 0.11f, 5);
                sourceMeshes.Add(branch);
                wood.Add(new CombineInstance
                {
                    mesh = branch,
                    transform = Matrix4x4.TRS(start, Quaternion.FromToRotation(Vector3.up, direction), Vector3.one)
                });
                Vector3 crownScale = new Vector3(Range(random, 1.0f, 1.55f),
                    Range(random, 1.1f, 1.7f), Range(random, 1.0f, 1.5f));
                if (crownVariant == 1) crownScale = Vector3.Scale(crownScale, new Vector3(.82f, 1.26f, .82f));
                if (crownVariant == 2) crownScale = Vector3.Scale(crownScale, new Vector3(1.16f, .78f, 1.16f));
                if (crownVariant == 3) crownScale = Vector3.Scale(crownScale, new Vector3(.94f, .88f, .94f));
                crowns[i % 3].Add(new CombineInstance
                {
                    mesh = crown,
                    transform = Matrix4x4.TRS(end + Vector3.up * 0.3f,
                        Quaternion.Euler(0f, Range(random, 0f, 360f), 0f), crownScale)
                });
            }
            crowns[0].Add(new CombineInstance
            {
                mesh = crown,
                transform = Matrix4x4.TRS(Vector3.up * (height * 0.91f) + lean,
                    Quaternion.identity, crownVariant == 1 ? new Vector3(1.0f, 1.9f, 1.0f) :
                        (crownVariant == 2 ? new Vector3(1.55f, 1.22f, 1.5f) : new Vector3(1.25f, 1.65f, 1.2f)))
            });

            Mesh woodCombined = Combine(wood, "TreeWood_" + seed);
            MeshObject(root.transform, "Trunk and branches", woodCombined, bark);
            for (int i = 0; i < crowns.Length; i++)
                if (crowns[i].Count > 0)
                {
                    var crownObject = MeshObject(root.transform, "Crown " + i, Combine(crowns[i], "TreeCrown_" + seed + "_" + i), leaves[i]);
                    var renderer = crownObject.GetComponent<MeshRenderer>();
                    var tint = new MaterialPropertyBlock();
                    float shade = Range(random, .87f, 1.10f);
                    Color baseColor = i == 0 ? new Color(.25f,.35f,.15f) : (i == 1 ? new Color(.34f,.43f,.20f) : new Color(.44f,.50f,.24f));
                    tint.SetColor("_Color", new Color(baseColor.r*shade,baseColor.g*shade,baseColor.b*shade,1));
                    renderer.SetPropertyBlock(tint);
                }
            foreach (Mesh mesh in sourceMeshes) Release(mesh);
            Release(crown);
            return root;
        }

        private static void EnsureTreeMaterials()
        {
            if (bark == null) bark = CreateMaterial("Bark - warm brown", new Color(0.30f, 0.23f, 0.15f));
            if (leaves == null || leaves[0] == null)
            {
                leaves = new[]
                {
                    LeafMaterial("Leaves - deep green", new Color(0.25f, 0.35f, 0.15f)),
                    LeafMaterial("Leaves - mid green", new Color(0.34f, 0.43f, 0.20f)),
                    LeafMaterial("Leaves - sunlit green", new Color(0.44f, 0.50f, 0.24f))
                };
            }
        }

        private static Material LeafMaterial(string name, Color color)
        {
            // Resources keeps this runtime-only shader in standalone builds.
            Shader shader = Resources.Load<Shader>("Shaders/LeafCards");
            if (shader == null) throw new InvalidOperationException("LeafCards shader resource is missing");
            return new Material(shader) { name = name, color = color,
                mainTexture = SceneryTextures.LeafCluster, enableInstancing = true };
        }

        private static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material material)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return child;
        }

        private static Mesh Combine(List<CombineInstance> instances, string name)
        {
            var mesh = new Mesh { name = name };
            mesh.CombineMeshes(instances.ToArray(), true, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CrownMesh(int seed)
        {
            var random = new System.Random(seed);
            var vertices = new List<Vector3>();var triangles = new List<int>();var uv = new List<Vector2>();
            // Crossed sprays form a porous layered canopy instead of opaque polyhedra.
            // Two quads per spray, shared across the tree's three crown renderers.
            for (int i = 0; i < 26; i++)
            {
                float angle = i * 2.399963f, y = 1 - 2 * (i + .5f) / 26;
                float r = Mathf.Sqrt(1 - y * y);
                Vector3 center = new Vector3(Mathf.Cos(angle) * r, y * .88f, Mathf.Sin(angle) * r) * Range(random,.55f,1f);
                float size = Range(random,.52f,.88f);
                Quaternion rotation = Quaternion.Euler(Range(random,-65,65),Range(random,0,360),Range(random,-40,40));
                for(int cross=0;cross<2;cross++)
                {
                    Quaternion q=rotation*Quaternion.Euler(0,cross*90,0);int a=vertices.Count;
                    vertices.Add(center+q*new Vector3(-size,-size*.7f,0));vertices.Add(center+q*new Vector3(size,-size*.7f,0));
                    vertices.Add(center+q*new Vector3(size,size*.7f,0));vertices.Add(center+q*new Vector3(-size,size*.7f,0));
                    uv.Add(Vector2.zero);uv.Add(Vector2.right);uv.Add(Vector2.one);uv.Add(Vector2.up);
                    triangles.AddRange(new[]{a,a+2,a+1,a,a+3,a+2});
                }
            }
            var mesh=new Mesh{name="Layered leaf sprays"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }

        private static Mesh TaperedCylinder(float height, float baseRadius, float topRadius, int sides)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                vertices.Add(new Vector3(Mathf.Cos(a) * baseRadius, 0f, Mathf.Sin(a) * baseRadius));
                vertices.Add(new Vector3(Mathf.Cos(a) * topRadius, height, Mathf.Sin(a) * topRadius));
            }
            int bottom = vertices.Count;
            vertices.Add(Vector3.zero);
            int top = vertices.Count;
            vertices.Add(Vector3.up * height);
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                triangles.AddRange(new[] { 2 * i, 2 * i + 1, 2 * next + 1,
                    2 * i, 2 * next + 1, 2 * next,
                    bottom, 2 * i, 2 * next,
                    top, 2 * next + 1, 2 * i + 1 });
            }
            return FlatMesh(vertices, triangles, "Tapered wood");
        }

        private static Mesh FlatMesh(List<Vector3> vertices, List<int> triangles, string name)
        {
            var flatVertices = new Vector3[triangles.Count];
            var flatTriangles = new int[triangles.Count];
            var uv = new Vector2[triangles.Count];
            for (int i = 0; i < triangles.Count; i++)
            {
                flatVertices[i] = vertices[triangles[i]];
                flatTriangles[i] = i;
                Vector3 p=flatVertices[i];
                uv[i]=new Vector2(Mathf.Atan2(p.z,p.x)/(2*Mathf.PI)+.5f,p.y*.45f);
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = flatVertices;
            mesh.triangles = flatTriangles;
            mesh.uv = uv;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void Icosphere(int subdivisions, out List<Vector3> vertices, out List<int> triangles)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            vertices = new List<Vector3>
            {
                new Vector3(-1,t,0), new Vector3(1,t,0), new Vector3(-1,-t,0), new Vector3(1,-t,0),
                new Vector3(0,-1,t), new Vector3(0,1,t), new Vector3(0,-1,-t), new Vector3(0,1,-t),
                new Vector3(t,0,-1), new Vector3(t,0,1), new Vector3(-t,0,-1), new Vector3(-t,0,1)
            };
            for (int i = 0; i < vertices.Count; i++) vertices[i] = vertices[i].normalized;
            triangles = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
            for (int level = 0; level < subdivisions; level++)
            {
                var refined = new List<int>(triangles.Count * 4);
                var cache = new Dictionary<long, int>();
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    int ab = Midpoint(a, b, vertices, cache);
                    int bc = Midpoint(b, c, vertices, cache);
                    int ca = Midpoint(c, a, vertices, cache);
                    refined.AddRange(new[] { a,ab,ca, b,bc,ab, c,ca,bc, ab,bc,ca });
                }
                triangles = refined;
            }
        }

        private static int Midpoint(int a, int b, List<Vector3> vertices, Dictionary<long, int> cache)
        {
            long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
            int found;
            if (cache.TryGetValue(key, out found)) return found;
            int index = vertices.Count;
            vertices.Add((vertices[a] + vertices[b]).normalized);
            cache.Add(key, index);
            return index;
        }

        private static float Range(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        private static void Release(UnityEngine.Object obj)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
