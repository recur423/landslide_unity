using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LandslideDemo
{
    /// <summary>
    /// Terrain and volumetric-looking soil surface driven by the imported solver fields.
    /// Display elevation is delegated to SimulationData.SurfaceHeight so particles,
    /// rocks and the mesh can share exactly the same display-height convention.
    /// </summary>
    public sealed class LandslideSurface : MonoBehaviour
    {
        public Mesh TerrainMesh { get; private set; }
        public Mesh SoilMesh { get; private set; }
        public Material TerrainMaterial { get; private set; }
        public Vector3[] LandscapeBoundary { get; private set; }
        public Vector3[] LandscapeBoundaryNormals { get; private set; }
        public int VertexCount { get { return SoilMesh == null ? 0 : SoilMesh.vertexCount; } }
        public int ActiveTriangleCount { get; private set; }
        public int FracturedCellCount { get { return fracturedCells.Count; } }
        public int FracturedVertexCount { get { return fracturedVertexCount; } }
        public int OpenFractureBlockCount { get; private set; }
        public int CompressiveBlockCount { get; private set; }
        public int ShearingBlockCount { get; private set; }

        [System.Serializable]
        public struct DisplayFootprint
        {
            public int sampledVertices, spurSpans, downstreamSpans;
            public float frontX, frontZ, frontWidth, nearTipX, farTipX, shelfTipX;
        }

        public DisplayFootprint MeasureDisplayFootprint()
        {
            var shape=new DisplayFootprint();
            if (!fracturedRenderer.enabled) return shape;
            float tip=-1f;
            foreach (int k in activeVertices)
            {
                if (soilColors[k].a < .42f) continue;
                tip=Mathf.Max(tip,soilVertices[k].x);
                shape.sampledVertices++;
                float z=data.Z[k%data.NZ];
                if (z>3f) shape.farTipX=Mathf.Max(shape.farTipX,soilVertices[k].x);
                if (z<-3f) shape.nearTipX=Mathf.Max(shape.nearTipX,soilVertices[k].x);
                if (z>4f && soilVertices[k].x>=209f && soilVertices[k].x<=264f)
                    shape.shelfTipX=Mathf.Max(shape.shelfTipX,soilVertices[k].x);
            }
            if (shape.sampledVertices==0) return shape;
            shape.frontX=tip;
            float toeZ=0f, left=float.PositiveInfinity, right=float.NegativeInfinity;
            int toeCount=0;
            foreach (int k in activeVertices)
            {
                if (soilColors[k].a < .42f) continue;
                Vector3 p=soilVertices[k];
                if (p.x>=tip-1f) {toeZ+=p.z;toeCount++;}
                if (Mathf.Abs(p.x-(tip-8f))>.6f) continue;
                left=Mathf.Min(left,p.z);right=Mathf.Max(right,p.z);
            }
            shape.frontZ=toeZ/Mathf.Max(1,toeCount);
            shape.frontWidth=right>left?right-left:0f;
            shape.spurSpans=CountCoverSpans(227.5f);
            shape.downstreamSpans=CountCoverSpans(249.5f);
            return shape;
        }

        int CountCoverSpans(float x)
        {
            int row=Mathf.RoundToInt((x-data.MinX)/(data.X[1]-data.X[0]));
            if (row<0 || row>=data.NX) return 0;
            int spans=0;
            bool previous=false;
            for (int j=0;j<data.NZ;j++)
            {
                int k=row*data.NZ+j;
                bool covered=activeVertexFlags[k]&&soilColors[k].a>=.42f;
                if (covered&&!previous) spans++;
                previous=covered;
            }
            return spans;
        }

        const float WetDepth = 0.025f;
        const float BlockSpacingX = 8.6f, BlockSpacingZ = 8.2f;
        const float LastExposureArrival = 3.85f;
        const float ExposureRamp = .75f;
        static readonly float[] CrackStartZ = { -13f, -5f, 2f };
        static readonly float[] CrackEndZ = { 7f, 9f, 17f };
        SimulationData data;
        SurfaceTextureSet textureSet;
        Material soilMaterial, crackMaterial;
        readonly List<Material> ownedMaterials = new List<Material>();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        Mesh sideMesh, crackMesh, fracturedMesh, seamMesh;
        MeshRenderer soilRenderer, sideRenderer, crackRenderer, fracturedRenderer, seamRenderer;
        Vector3[] soilVertices, soilNormals, sideVertices, sideNormals;
        Vector4[] soilTangents, sideTangents;
        float[] reliefFactors, groundOffsets, exposureArrivals;
        float[] channelClearance, shelfInfluence, rowScale;
        float[] shelfDepthA, shelfDepthB;
        float shelfBlend, shelfDelay;
        Color[] soilColors, sideColors;
        bool[] activeCells, activeVertexFlags;
        bool[] edgeVertexFlags;
        int[] sideTriangles;
        int[] cellBlocks, activeCellSlots, fracturedSources, fracturedVertexBlocks;
        int[] fracturedCornerVertices, fracturedTriangles, fracturedCellBlocks;
        Vector2[] blockCenters, displayBlockCenters;
        float[] blockExposureArrival;
        Vector3[] fracturedVertices, fracturedNormals, seamVertices, seamNormals;
        Vector4[] fracturedTangents, seamTangents;
        Color[] fracturedColors, seamColors;
        int[] seamTriangles;
        float[] blockWeight, blockStrainX, blockShear, blockTension, blockCompression, blockShearMotion, blockCenterHeights;
        float[] blockCosYaw, blockSinYaw, blockCosPitch, blockSinPitch;
        Quaternion[] blockNormalRotation;
        readonly List<int> fracturedCells = new List<int>(5000);
        readonly Dictionary<long, int> fracturedVertexLookup = new Dictionary<long, int>(12000);
        readonly List<FractureSeam> fractureSeams = new List<FractureSeam>(1500);
        int fracturedVertexCount, seamVertexCount, seamIndexCount, renderedSeamCount = -1;
        bool hasOpenFracture;
        Bounds soilBounds;
        readonly List<int> topTriangles = new List<int>(100000);
        readonly List<int> visibleSideTriangles = new List<int>(40000);
        readonly List<int> activeVertices = new List<int>(12000);
        readonly List<Edge> edges = new List<Edge>(3000);
        readonly List<ScarpTier> scarpTiers = new List<ScarpTier>(4);
        int topologyA = -1, topologyB = -1;
        float previousTime = float.NegativeInfinity;
        float previousScale = float.NaN;
        bool previousFailure;
        float exposureClock = float.PositiveInfinity;

        struct Edge
        {
            public int A, B, Cell, CornerA, CornerB, FracturedA, FracturedB;
            public Edge(int a, int b, int cell, int cornerA, int cornerB)
            {
                A = a; B = b; Cell = cell; CornerA = cornerA; CornerB = cornerB;
                FracturedA = FracturedB = -1;
            }
        }

        struct FractureSeam
        {
            public int CellA, CellB, A0, A1, B0, B1;
            public FractureSeam(int cellA, int cellB, int a0, int a1, int b0, int b1)
            { CellA=cellA; CellB=cellB; A0=a0; A1=a1; B0=b0; B1=b1; }
        }

        sealed class ScarpTier
        {
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public Vector3[] Flat, Raised, Working;
            public Vector4[] Tangents;
            public float[] Arrival;
            public float Start, Activation = -1f;
        }

        public void Initialize(SimulationData simulation, Shader terrainShader, Shader soilShader)
        {
            data = simulation;
            textureSet = new SurfaceTextureSet();
            int count = data.NX * data.NZ;
            soilVertices = new Vector3[count];
            soilNormals = new Vector3[count];
            soilTangents = new Vector4[count];
            soilColors = new Color[count];
            reliefFactors = new float[count];
            groundOffsets = new float[count];
            exposureArrivals = new float[count];
            channelClearance = new float[count];
            shelfInfluence = new float[count];
            rowScale = new float[data.NX];
            for (int i = 0; i < data.NX; i++) rowScale[i] = PresentationLayout.WidthScale(data.X[i]);
            for (int i = 0; i < data.NX; i++)
            for (int j = 0; j < data.NZ; j++)
            {
                int k = i * data.NZ + j;
                reliefFactors[k] = data.VisualReliefFactor(data.X[i], data.Z[j]);
                groundOffsets[k] = data.VisualGroundOffset(data.X[i], data.Z[j]);
                exposureArrivals[k] = ExposureArrival(data.X[i], data.Z[j]);
                channelClearance[k] = PresentationLayout.IslandExposure(data.X[i], data.Z[j]);
                shelfInfluence[k] = PresentationLayout.ShelfInfluence(data.X[i], data.Z[j]);
                soilVertices[k] = PresentationLayout.Point(data.X[i], data.Bed[k] + groundOffsets[k] +
                    data.Depth[0][k] * data.VisualDepthScale * reliefFactors[k] + .018f, data.Z[j]);
                soilNormals[k] = Vector3.up;
                soilTangents[k] = Tangent(Vector3.up);
                soilColors[k] = Color.white;
            }
            activeCells = new bool[(data.NX - 1) * (data.NZ - 1)];
            activeVertexFlags = new bool[count];
            edgeVertexFlags = new bool[count];
            InitializeVoronoiBlocks();

            var terrainMaterial = NewMaterial(terrainShader, "Grass, weathered rock and exposed source");
            TerrainMaterial = terrainMaterial;
            textureSet.Apply(terrainMaterial);
            TerrainMesh = BuildTerrain();
            soilBounds = TerrainMesh.bounds;
            soilBounds.Expand(new Vector3(2f, Mathf.Max(30f, data.VisualDepthScale * 12f), 2f));
            GameObject terrainObject = MeshObject("Original slip-plane terrain", TerrainMesh, terrainMaterial);
            terrainObject.AddComponent<MeshCollider>().sharedMesh = TerrainMesh;
            BuildCutawaySkirt(terrainMaterial);

            soilMaterial = NewMaterial(soilShader, "Textured wet earth and fractured moving soil");
            textureSet.Apply(soilMaterial);
            if (soilMaterial.HasProperty("_Color")) soilMaterial.SetColor("_Color", new Color(.93f, .87f, .77f));
            if (soilMaterial.HasProperty("_Crack")) soilMaterial.SetFloat("_Crack", .045f);
            SoilMesh = new Mesh { name = "Computed sliding soil - interpolated surface", indexFormat = IndexFormat.UInt32 };
            SoilMesh.MarkDynamic();
            soilRenderer = MeshObject("Computed sliding mass", SoilMesh, soilMaterial).GetComponent<MeshRenderer>();

            fracturedMesh = new Mesh { name = "Velocity-strain Voronoi slide blocks", indexFormat = IndexFormat.UInt32 };
            fracturedMesh.MarkDynamic();
            fracturedRenderer = MeshObject("Separated moving soil blocks - display only", fracturedMesh, soilMaterial).GetComponent<MeshRenderer>();

            sideMesh = new Mesh { name = "Moving soil boundary thickness", indexFormat = IndexFormat.UInt32 };
            sideMesh.MarkDynamic();
            Shader layerShader = Resources.Load<Shader>("Shaders/LayeredSlip");
            if (layerShader == null) throw new System.InvalidOperationException("LayeredSlip shader is missing");
            Material layerMaterial = NewMaterial(layerShader, "Exposed soil and rock stratigraphy");
            textureSet.Apply(layerMaterial);
            sideRenderer = MeshObject("Inclined moving slip-plane boundary", sideMesh, layerMaterial).GetComponent<MeshRenderer>();
            seamMesh = new Mesh { name = "Layered fracture interiors beneath strain-opened seams", indexFormat = IndexFormat.UInt32 };
            seamMesh.MarkDynamic();
            seamRenderer = MeshObject("Exposed soil, weathered layer and rock in fractures", seamMesh, layerMaterial).GetComponent<MeshRenderer>();
            BuildSourceScar(layerMaterial);
            BuildCracks();
            SetState(0f, false);
        }

        static float CrackCenterX(int branch, float z)
        {
            float along = z - CrackStartZ[branch];
            return 69.5f + 4.9f * branch + (.10f - .13f * branch) * along
                + 1.15f * Mathf.Sin(along * .26f + branch * 1.6f)
                + .48f * Mathf.Sin(along * 1.15f + branch * .9f);
        }

        internal static float ExposureArrival(float x, float z)
        {
            // A fixed arrival field measured from the actual ribbon branches.
            // Distorted travel distances give a broken contact line without
            // isolated flashes. Imported h still supplies all displayed depth.
            float arrival = LastExposureArrival;
            for (int branch = 0; branch < CrackStartZ.Length; branch++)
            {
                float zAlong = Mathf.Clamp(z, CrackStartZ[branch], CrackEndZ[branch]);
                float dx = x - CrackCenterX(branch, zAlong);
                float dz = z - zAlong;
                float travel = Mathf.Sqrt(dx * dx * .0045f + dz * dz * .010f);
                float localDelay = .42f + branch * .17f + Mathf.Abs(zAlong - (CrackStartZ[branch] + CrackEndZ[branch]) * .5f) * .027f;
                arrival = Mathf.Min(arrival, localDelay + travel);
            }
            float variation = Mathf.PerlinNoise(x * .118f + 29f, z * .097f + 43f) - .48f;
            return Mathf.Clamp(arrival + variation * .63f, .36f, LastExposureArrival);
        }

        float SoilExposure(int index)
        {
            return exposureClock >= LastExposureArrival + ExposureRamp ? 1f :
                Mathf.SmoothStep(0f, 1f, (exposureClock - exposureArrivals[index]) / ExposureRamp);
        }

        void UpdateOpeningCracks(float seconds, bool failed)
        {
            crackRenderer.enabled = failed && seconds > .08f && seconds < 3.15f;
            if (!crackRenderer.enabled) return;
            float clear = 1f - Mathf.SmoothStep(0f, 1f, (seconds - 1.45f) / 1.70f);
            crackMaterial.SetFloat("_CrackTime", seconds);
            crackMaterial.SetFloat("_Opacity", .92f * clear);
        }

        public void SetState(float time, bool failed)
        {
            if (data == null) return;
            float seconds = failed ? Mathf.Clamp(time, 0f, data.TimeMax) : 0f;
            if (soilMaterial != null && soilMaterial.HasProperty("_Wetness"))
            {
                soilMaterial.SetFloat("_Wetness", failed ? Mathf.Lerp(.94f, .66f, seconds / data.TimeMax) : .94f);
                soilMaterial.SetFloat("_ExposureAge", seconds);
            }
            data.Frame(seconds, out int a, out int b, out float blend);
            bool topologyChanged = topologyA != a || topologyB != b;
            bool scaleChanged = !Mathf.Approximately(previousScale, data.VisualDepthScale);
            bool stateChanged = previousFailure != failed;
            if (!topologyChanged && !scaleChanged && !stateChanged && Mathf.Abs(seconds - previousTime) < .00001f)
                return;

            exposureClock = failed ? seconds : float.PositiveInfinity;
            bool stageTiming = PerformanceProbe.ComponentTimingEnabled;
            long topologyStart = stageTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            if (topologyChanged || scaleChanged) RebuildTopology(a, b);
            long afterTopology = stageTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            float[] ha = data.Depth[a], hb = data.Depth[b];
            float[] ua = data.UX[a], ub = data.UX[b], va = data.UZ[a], vb = data.UZ[b];
            shelfDelay = PresentationLayout.ShelfDelay(seconds);
            if (shelfDelay > 0f)
            {
                data.Frame(seconds-shelfDelay,out int oldA,out int oldB,out shelfBlend);
                shelfDepthA = data.Depth[oldA];
                shelfDepthB = data.Depth[oldB];
            }
            for (int active = 0; active < activeVertices.Count; active++)
            {
                int k = activeVertices[active];
                float h = DisplayDepth(k,ha,hb,blend);
                float ux = Mathf.Lerp(ua[k], ub[k], blend);
                float uz = Mathf.Lerp(va[k], vb[k], blend);
                float speed = Mathf.Sqrt(ux * ux + uz * uz);
                float exposure = SoilExposure(k);
                soilVertices[k].y = data.Bed[k] + groundOffsets[k] +
                    h * data.VisualDepthScale * reliefFactors[k] * exposure + .018f;
                Color fast = new Color(1f, .98f, .93f);
                Color deposit = new Color(.85f, .83f, .78f);
                float moving = Mathf.SmoothStep(0f, 1f, speed / 5f);
                soilColors[k] = Color.Lerp(deposit, fast, failed ? moving : .65f);
                // Slight darkening in thinner tails makes the thick moving toe read clearly.
                soilColors[k] *= Mathf.Lerp(.80f, 1f, Mathf.Clamp01(h / .7f));
                // Fade over the first solver cell to blend the displayed mud into
                // the terrain rather than showing an aliased hard-edged carpet.
                float x=soilVertices[k].x,z=soilVertices[k].z;
                float fan=Mathf.SmoothStep(0,1,(x-296f)/31f);
                float tongues=.038f+fan*(.041f*Mathf.Sin(z*.35f+x*.15f)+.027f*Mathf.Sin(z*.64f-x*.10f));
                float feather = Mathf.SmoothStep(0f,1f,(h-Mathf.Max(.012f,tongues))/.095f);
                soilColors[k].a = exposure * channelClearance[k] * feather * (edgeVertexFlags[k] ? .23f : 1f);
            }
            long afterSoilVertices = stageTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            CalculateSurfaceNormals(ha, hb, blend);
            long afterNormals = stageTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            // Keep the hidden continuous mesh available for rewind and validation.
            if (!failed)
            {
                SoilMesh.vertices = soilVertices;
                SoilMesh.normals = soilNormals;
                SoilMesh.tangents = soilTangents;
                SoilMesh.colors = soilColors;
                if (topologyChanged || scaleChanged) SoilMesh.SetTriangles(topTriangles, 0, false);
                SoilMesh.bounds = soilBounds;
            }
            UpdateFracturedSurface(ha, hb, ua, ub, va, vb, blend, seconds, failed);
            long beforeBoundary = stageTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            UpdateBoundaryWalls(failed);
            long afterBoundary = stageTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            UpdateSourceScar(seconds, failed);
            if (stageTiming)
            {
                long afterScarp = System.Diagnostics.Stopwatch.GetTimestamp();
                PerformanceProbe.RecordSurfaceMainTicks(topologyChanged || scaleChanged ? afterTopology-topologyStart : 0,
                    afterSoilVertices-afterTopology,afterNormals-afterSoilVertices,
                    afterBoundary-beforeBoundary,afterScarp-afterBoundary);
            }
            soilRenderer.enabled = false;
            fracturedRenderer.enabled = failed && seconds > .36f;
            fracturedRenderer.shadowCastingMode = seconds >= LastExposureArrival + ExposureRamp
                ? ShadowCastingMode.On : ShadowCastingMode.Off;
            bool fullExposure = failed && seconds >= LastExposureArrival + ExposureRamp;
            sideRenderer.enabled = fullExposure;
            seamRenderer.enabled = failed && hasOpenFracture;
            UpdateOpeningCracks(seconds, failed);
            previousTime = seconds;
            previousFailure = failed;
            previousScale = data.VisualDepthScale;
        }

        Mesh BuildTerrain()
        {
            int count = data.NX * data.NZ;
            var vertices = new Vector3[count];
            var colors = new Color[count];
            var terrainChannels = new Vector2[count];
            var triangles = new int[(data.NX - 1) * (data.NZ - 1) * 6];
            int ti = 0;
            for (int i = 0; i < data.NX; i++)
            for (int j = 0; j < data.NZ; j++)
            {
                int k = i * data.NZ + j;
                float x = data.X[i], z = data.Z[j];
                vertices[k] = PresentationLayout.Point(x, data.Bed[k] + groundOffsets[k], z);
                // The intact bed has no material mask from the initial depth field.
                float grain = Mathf.PerlinNoise(x * .018f + 10f, z * .018f + 17f);
                float tint = Mathf.Lerp(.88f, 1.04f, grain);
                colors[k] = new Color(tint, tint, tint, 0f);
                terrainChannels[k] = data.GroundCover(k);
                if (i == data.NX - 1 || j == data.NZ - 1) continue;
                triangles[ti++] = k; triangles[ti++] = k + 1; triangles[ti++] = k + data.NZ;
                triangles[ti++] = k + data.NZ; triangles[ti++] = k + 1; triangles[ti++] = k + data.NZ + 1;
            }
            var mesh = new Mesh { name = "Original numerical terrain", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.colors = colors; mesh.uv2 = terrainChannels;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // A fixed world-space Voronoi partition gives each moving patch one
        // persistent identity. Its jitter changes the outline, never the timing
        // or amount of motion: those come from the imported velocity gradients.
        void InitializeVoronoiBlocks()
        {
            int rows = data.NZ - 1;
            cellBlocks = new int[(data.NX - 1) * rows];
            activeCellSlots = new int[cellBlocks.Length];
            var lookup = new Dictionary<long, int>();
            var centers = new List<Vector2>(1100);
            for (int i = 0; i < data.NX - 1; i++)
            for (int j = 0; j < rows; j++)
            {
                float x = (data.X[i] + data.X[i + 1]) * .5f;
                float z = (data.Z[j] + data.Z[j + 1]) * .5f;
                int baseX = Mathf.FloorToInt(x / BlockSpacingX);
                int baseZ = Mathf.FloorToInt(z / BlockSpacingZ);
                float nearest = float.PositiveInfinity;
                int chosenX = 0, chosenZ = 0;
                Vector2 chosenCenter = Vector2.zero;
                for (int gx = baseX - 1; gx <= baseX + 1; gx++)
                for (int gz = baseZ - 1; gz <= baseZ + 1; gz++)
                {
                    Vector2 site = BlockSite(gx, gz);
                    float distance = (site - new Vector2(x, z)).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance; chosenX = gx; chosenZ = gz; chosenCenter = site;
                }
                long key = ((long)chosenX << 32) | (uint)chosenZ;
                if (!lookup.TryGetValue(key, out int block))
                {
                    block = centers.Count;
                    lookup.Add(key, block);
                    centers.Add(chosenCenter);
                }
                cellBlocks[i * rows + j] = block;
            }
            blockCenters = centers.ToArray();
            int count = blockCenters.Length;
            displayBlockCenters = new Vector2[count];
            blockExposureArrival = new float[count];
            for (int b=0;b<count;b++)
            {
                Vector2 center=blockCenters[b];
                displayBlockCenters[b]=new Vector2(center.x,PresentationLayout.WorldZ(center.x,center.y));
                blockExposureArrival[b]=ExposureArrival(center.x,center.y);
            }
            blockWeight = new float[count]; blockStrainX = new float[count]; blockShear = new float[count];
            blockTension = new float[count]; blockCompression = new float[count]; blockShearMotion = new float[count];
            blockCenterHeights = new float[count];
            blockCosYaw = new float[count]; blockSinYaw = new float[count];
            blockCosPitch = new float[count]; blockSinPitch = new float[count];
            blockNormalRotation = new Quaternion[count];
        }

        static Vector2 BlockSite(int gx, int gz)
        {
            return new Vector2((gx + (BlockHash(gx, gz, 17) - .5f) * .52f) * BlockSpacingX,
                (gz + (BlockHash(gx, gz, 53) - .5f) * .52f) * BlockSpacingZ);
        }

        static float BlockHash(int x, int z, int salt)
        {
            unchecked
            {
                uint value = (uint)(x * 374761393 + z * 668265263 + salt * 1274126177);
                value = (value ^ (value >> 13)) * 1274126177u;
                value ^= value >> 16;
                return (value & 0x00ffffff) / 16777215f;
            }
        }

        void RebuildFracturedTopology()
        {
            fracturedCells.Clear(); fractureSeams.Clear();
            for (int cell = 0; cell < activeCells.Length; cell++) activeCellSlots[cell] = -1;
            for (int cell = 0; cell < activeCells.Length; cell++)
            {
                if (!activeCells[cell]) continue;
                activeCellSlots[cell] = fracturedCells.Count;
                fracturedCells.Add(cell);
            }
            int cornerCount = fracturedCells.Count * 4, indexCount = fracturedCells.Count * 6;
            if (fracturedVertices == null || fracturedVertices.Length < cornerCount)
            {
                int capacity = Mathf.NextPowerOfTwo(Mathf.Max(4, cornerCount));
                fracturedVertices = new Vector3[capacity]; fracturedNormals = new Vector3[capacity];
                fracturedTangents = new Vector4[capacity]; fracturedColors = new Color[capacity];
                fracturedSources = new int[capacity]; fracturedVertexBlocks = new int[capacity];
            }
            if (fracturedCornerVertices == null || fracturedCornerVertices.Length < cornerCount)
                fracturedCornerVertices = new int[Mathf.NextPowerOfTwo(Mathf.Max(4, cornerCount))];
            if (fracturedTriangles == null || fracturedTriangles.Length < indexCount)
                fracturedTriangles = new int[Mathf.NextPowerOfTwo(Mathf.Max(6, indexCount))];
            if (fracturedCellBlocks == null || fracturedCellBlocks.Length < fracturedCells.Count)
                fracturedCellBlocks = new int[Mathf.NextPowerOfTwo(Mathf.Max(1, fracturedCells.Count))];
            fracturedVertexLookup.Clear();
            fracturedVertexCount = 0;
            int rows = data.NZ - 1;
            for (int c = 0; c < fracturedCells.Count; c++)
            {
                int cell = fracturedCells[c], i = cell / rows, j = cell % rows;
                int source = i * data.NZ + j, v = c * 4, t = c * 6;
                int block = cellBlocks[cell];
                fracturedCellBlocks[c] = block;
                int v0 = GetFracturedVertex(source, block);
                int v1 = GetFracturedVertex(source + 1, block);
                int v2 = GetFracturedVertex(source + data.NZ, block);
                int v3 = GetFracturedVertex(source + data.NZ + 1, block);
                fracturedCornerVertices[v] = v0; fracturedCornerVertices[v + 1] = v1;
                fracturedCornerVertices[v + 2] = v2; fracturedCornerVertices[v + 3] = v3;
                fracturedTriangles[t] = v0; fracturedTriangles[t + 1] = v1; fracturedTriangles[t + 2] = v2;
                fracturedTriangles[t + 3] = v2; fracturedTriangles[t + 4] = v1; fracturedTriangles[t + 5] = v3;
                // Only the interfaces between different Voronoi IDs expose a
                // layered crack interior; cells inside one block stay welded.
                int right = cell + rows, upper = cell + 1;
                if (i + 1 < data.NX - 1 && activeCellSlots[right] >= 0 && cellBlocks[right] != cellBlocks[cell])
                    fractureSeams.Add(new FractureSeam(c, activeCellSlots[right], 2, 3, 1, 0));
                if (j + 1 < rows && activeCellSlots[upper] >= 0 && cellBlocks[upper] != cellBlocks[cell])
                    fractureSeams.Add(new FractureSeam(c, activeCellSlots[upper], 3, 1, 0, 2));
            }
            fracturedMesh.Clear(false);
            fracturedMesh.SetVertices(fracturedVertices, 0, fracturedVertexCount);
            fracturedMesh.SetTriangles(fracturedTriangles, 0, indexCount, 0, false);
            fracturedMesh.bounds = soilBounds;

            seamVertexCount = fractureSeams.Count * 22;
            seamIndexCount = fractureSeams.Count * 42;
            if (seamVertices == null || seamVertices.Length < seamVertexCount)
            {
                int capacity = Mathf.NextPowerOfTwo(Mathf.Max(22, seamVertexCount));
                seamVertices = new Vector3[capacity]; seamNormals = new Vector3[capacity];
                seamTangents = new Vector4[capacity];
                seamColors = new Color[capacity];
            }
            if (seamTriangles == null || seamTriangles.Length < seamIndexCount)
                seamTriangles = new int[Mathf.NextPowerOfTwo(Mathf.Max(42, seamIndexCount))];
            for (int s = 0; s < fractureSeams.Count; s++)
            for (int side = 0; side < 2; side++)
            {
                int baseVertex = s * 22 + side * 8, baseIndex = s * 42 + side * 18;
                for (int layer = 0; layer < 3; layer++)
                {
                    int v = baseVertex + layer * 2, t = baseIndex + layer * 6;
                    seamTriangles[t] = v; seamTriangles[t + 1] = v + 1; seamTriangles[t + 2] = v + 2;
                    seamTriangles[t + 3] = v + 1; seamTriangles[t + 4] = v + 3; seamTriangles[t + 5] = v + 2;
                }
                for (int layer = 0; layer < 4; layer++)
                {
                    float rock = layer / 3f;
                    Color color = new Color(Mathf.Lerp(.73f, .47f, rock), Mathf.Lerp(.59f, .43f, rock),
                        Mathf.Lerp(.43f, .33f, rock), rock * .55f);
                    seamColors[baseVertex + layer * 2] = seamColors[baseVertex + layer * 2 + 1] = color;
                }
            }
            for (int s = 0; s < fractureSeams.Count; s++)
            {
                int v = s * 22 + 16, t = s * 42 + 36;
                seamTriangles[t] = v; seamTriangles[t + 1] = v + 1; seamTriangles[t + 2] = v + 2;
                seamTriangles[t + 3] = v + 3; seamTriangles[t + 4] = v + 4; seamTriangles[t + 5] = v + 5;
                for (int corner = 0; corner < 6; corner++)
                    seamColors[v + corner] = new Color(.44f, .34f, .25f, .5f);
            }
            seamMesh.Clear(false);
            seamMesh.SetVertices(seamVertices, 0, seamVertexCount);
            seamMesh.SetTriangles(seamTriangles, 0, seamIndexCount, 0, false);
            seamMesh.SetColors(seamColors, 0, seamVertexCount);
            seamMesh.bounds = soilBounds;
            renderedSeamCount = fractureSeams.Count;
        }

        int GetFracturedVertex(int source, int block)
        {
            long key = ((long)block << 32) | (uint)source;
            int vertex;
            if (fracturedVertexLookup.TryGetValue(key, out vertex)) return vertex;
            vertex = fracturedVertexCount++;
            fracturedVertexLookup.Add(key, vertex);
            fracturedSources[vertex] = source;
            fracturedVertexBlocks[vertex] = block;
            return vertex;
        }

        void RebuildTopology(int a, int b)
        {
            int rows = data.NZ - 1;
            topTriangles.Clear(); edges.Clear(); activeVertices.Clear();
            System.Array.Clear(activeVertexFlags, 0, activeVertexFlags.Length);
            System.Array.Clear(edgeVertexFlags, 0, edgeVertexFlags.Length);
            float[] h0 = data.Depth[a], h1 = data.Depth[b];
            for (int i = 0; i < data.NX - 1; i++)
            for (int j = 0; j < data.NZ - 1; j++)
            {
                int k = i * data.NZ + j;
                float maximum = Max4(h0[k], h0[k + 1], h0[k + data.NZ], h0[k + data.NZ + 1]);
                maximum = Mathf.Max(maximum, Max4(h1[k], h1[k + 1], h1[k + data.NZ], h1[k + data.NZ + 1]));
                float clearance = Max4(channelClearance[k],channelClearance[k+1],
                    channelClearance[k+data.NZ],channelClearance[k+data.NZ+1]);
                activeCells[i * rows + j] = maximum > WetDepth && clearance > .075f;
            }
            for (int i = 0; i < data.NX - 1; i++)
            for (int j = 0; j < data.NZ - 1; j++)
            {
                if (!activeCells[i * rows + j]) continue;
                int k = i * data.NZ + j;
                MarkActiveVertex(k); MarkActiveVertex(k + 1);
                MarkActiveVertex(k + data.NZ); MarkActiveVertex(k + data.NZ + 1);
                topTriangles.Add(k); topTriangles.Add(k + 1); topTriangles.Add(k + data.NZ);
                topTriangles.Add(k + data.NZ); topTriangles.Add(k + 1); topTriangles.Add(k + data.NZ + 1);
                int cell = i * rows + j;
                if (i == 0 || !activeCells[(i - 1) * rows + j]) edges.Add(new Edge(k, k + 1, cell, 0, 1));
                if (i == data.NX - 2 || !activeCells[(i + 1) * rows + j]) edges.Add(new Edge(k + data.NZ + 1, k + data.NZ, cell, 3, 2));
                if (j == 0 || !activeCells[i * rows + j - 1]) edges.Add(new Edge(k + data.NZ, k, cell, 2, 0));
                if (j == data.NZ - 2 || !activeCells[i * rows + j + 1]) edges.Add(new Edge(k + 1, k + data.NZ + 1, cell, 1, 3));
            }
            foreach (Edge edge in edges) { edgeVertexFlags[edge.A] = true; edgeVertexFlags[edge.B] = true; }
            int sideVertexCount = edges.Count * 8, sideIndexCount = edges.Count * 18;
            if (sideVertices == null || sideVertices.Length < sideVertexCount)
            {
                int capacity = Mathf.NextPowerOfTwo(Mathf.Max(4, sideVertexCount));
                sideVertices = new Vector3[capacity];
                sideNormals = new Vector3[capacity];
                sideTangents = new Vector4[capacity];
                sideColors = new Color[capacity];
            }
            if (sideTriangles == null || sideTriangles.Length < sideIndexCount)
                sideTriangles = new int[Mathf.NextPowerOfTwo(Mathf.Max(6, sideIndexCount))];
            for (int e = 0; e < edges.Count; e++)
            {
                int v = e * 8, q = e * 18;
                for (int layer=0; layer<3; layer++)
                {
                    int s=v+layer*2, t=q+layer*6;
                    sideTriangles[t]=s; sideTriangles[t+1]=s+2; sideTriangles[t+2]=s+1;
                    sideTriangles[t+3]=s+1; sideTriangles[t+4]=s+2; sideTriangles[t+5]=s+3;
                }
                for (int layer=0; layer<4; layer++)
                {
                    float rock = layer/3f;
                    Color color = new Color(Mathf.Lerp(.94f,.73f,rock), Mathf.Lerp(.88f,.78f,rock),
                        Mathf.Lerp(.79f,.73f,rock), rock);
                    sideColors[v+layer*2]=sideColors[v+layer*2+1]=color;
                }
            }
            sideMesh.Clear(false);
            sideMesh.SetVertices(sideVertices, 0, sideVertexCount);
            sideMesh.SetTriangles(sideTriangles, 0, sideIndexCount, 0, false);
            sideMesh.SetColors(sideColors, 0, sideVertexCount);
            RebuildFracturedTopology();
            for (int e = 0; e < edges.Count; e++)
            {
                Edge edge = edges[e];
                int slot = activeCellSlots[edge.Cell];
                edge.FracturedA = fracturedCornerVertices[slot * 4 + edge.CornerA];
                edge.FracturedB = fracturedCornerVertices[slot * 4 + edge.CornerB];
                edges[e] = edge;
            }
            topologyA = a; topologyB = b;
            ActiveTriangleCount = topTriangles.Count / 3 + edges.Count * 6;
        }

        void MarkActiveVertex(int index)
        {
            if (activeVertexFlags[index]) return;
            activeVertexFlags[index] = true;
            activeVertices.Add(index);
        }

        float DisplayDepth(int index, float[] ha, float[] hb, float blend)
        {
            float original = Mathf.Lerp(ha[index],hb[index],blend);
            if (shelfDelay <= 0f || shelfInfluence[index] <= 0f) return original;
            return Mathf.Lerp(original,Mathf.Lerp(shelfDepthA[index],shelfDepthB[index],shelfBlend),
                shelfInfluence[index]);
        }

        float HeightAt(int index, float[] ha, float[] hb, float blend)
        {
            return data.Bed[index] + groundOffsets[index] +
                DisplayDepth(index,ha,hb,blend) * data.VisualDepthScale * reliefFactors[index] * SoilExposure(index);
        }

        void CalculateSurfaceNormals(float[] ha, float[] hb, float blend)
        {
            // Neighbor heights are read directly from fields; no inactive-grid vertex
            // updates, bilinear spatial samples or temporary arrays are required.
            for (int active = 0; active < activeVertices.Count; active++)
            {
                int k = activeVertices[active], i = k / data.NZ, j = k % data.NZ;
                int loX = Mathf.Max(0, i - 1), hiX = Mathf.Min(data.NX - 1, i + 1);
                int loZ = Mathf.Max(0, j - 1), hiZ = Mathf.Min(data.NZ - 1, j + 1);
                int x0=loX * data.NZ+j, x1=hiX * data.NZ+j;
                float dx = data.X[hiX] - data.X[loX];
                float gz = (HeightAt(i * data.NZ + hiZ, ha, hb, blend) - HeightAt(i * data.NZ + loZ, ha, hb, blend))
                    / ((data.Z[hiZ] - data.Z[loZ])*rowScale[i]);
                float gx = (HeightAt(x1,ha,hb,blend)-HeightAt(x0,ha,hb,blend)
                    - gz*(soilVertices[x1].z-soilVertices[x0].z))/dx;
                soilNormals[k] = new Vector3(-gx, 1f, -gz).normalized;
                soilTangents[k] = Tangent(soilNormals[k]);
            }
        }

        void UpdateFracturedSurface(float[] ha, float[] hb, float[] ua, float[] ub,
            float[] va, float[] vb, float blend, float seconds, bool failed)
        {
            bool stageTiming = PerformanceProbe.ComponentTimingEnabled;
            long strainStart = stageTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            System.Array.Clear(blockWeight, 0, blockWeight.Length);
            System.Array.Clear(blockStrainX, 0, blockStrainX.Length);
            System.Array.Clear(blockShear, 0, blockShear.Length);
            int rows = data.NZ - 1;
            for (int c = 0; c < fracturedCells.Count; c++)
            {
                int cell = fracturedCells[c], i = cell / rows, j = cell % rows;
                int k = i * data.NZ + j, next = k + data.NZ;
                float weight = Mathf.Max(.04f, (Mathf.Lerp(ha[k], hb[k], blend) +
                    Mathf.Lerp(ha[k+1], hb[k+1], blend) + Mathf.Lerp(ha[next], hb[next], blend) +
                    Mathf.Lerp(ha[next+1], hb[next+1], blend)) * .25f);
                float u00 = Mathf.Lerp(ua[k], ub[k], blend), u01 = Mathf.Lerp(ua[k+1], ub[k+1], blend);
                float u10 = Mathf.Lerp(ua[next], ub[next], blend), u11 = Mathf.Lerp(ua[next+1], ub[next+1], blend);
                float v00 = Mathf.Lerp(va[k], vb[k], blend), v01 = Mathf.Lerp(va[k+1], vb[k+1], blend);
                float v10 = Mathf.Lerp(va[next], vb[next], blend), v11 = Mathf.Lerp(va[next+1], vb[next+1], blend);
                float duDx = ((u10 + u11) - (u00 + u01)) * .5f / (data.X[i+1] - data.X[i]);
                float duDz = ((u01 + u11) - (u00 + u10)) * .5f / (data.Z[j+1] - data.Z[j]);
                float dvDx = ((v10 + v11) - (v00 + v01)) * .5f / (data.X[i+1] - data.X[i]);
                int block = fracturedCellBlocks[c];
                blockWeight[block] += weight;
                blockStrainX[block] += duDx * weight;
                blockShear[block] += (duDz + dvDx) * .5f * weight;
            }
            hasOpenFracture = false;
            OpenFractureBlockCount = CompressiveBlockCount = ShearingBlockCount = 0;
            for (int b = 0; b < blockCenters.Length; b++)
            {
                float inverse = blockWeight[b] > .0001f ? 1f / blockWeight[b] : 0f;
                float extension = blockStrainX[b] * inverse;
                float shear = blockShear[b] * inverse;
                blockTension[b] = failed ? Mathf.SmoothStep(0, 1, (extension - .035f) / .19f) : 0f;
                blockCompression[b] = failed ? Mathf.SmoothStep(0, 1, (-extension - .07f) / .48f) : 0f;
                blockShearMotion[b] = failed ? Mathf.Clamp(shear / .65f, -1f, 1f) : 0f;
                if (blockWeight[b] > .0001f)
                {
                    Vector2 center=blockCenters[b];
                    float ground=data.DisplayBedHeight(center.x,center.y);
                    float visible=failed?Mathf.SmoothStep(0,1,(seconds-blockExposureArrival[b])/ExposureRamp):1f;
                    blockCenterHeights[b]=Mathf.Lerp(ground,data.SurfaceHeight(seconds,center.x,center.y),visible);
                }
                float yaw = blockShearMotion[b] * 5f * Mathf.Deg2Rad;
                float pitch = -blockTension[b] * 4.5f * Mathf.Deg2Rad;
                blockCosYaw[b] = Mathf.Cos(yaw); blockSinYaw[b] = Mathf.Sin(yaw);
                blockCosPitch[b] = Mathf.Cos(pitch); blockSinPitch[b] = Mathf.Sin(pitch);
                blockNormalRotation[b] = Quaternion.AngleAxis(blockShearMotion[b] * 5f, Vector3.up) *
                    Quaternion.AngleAxis(-blockTension[b] * 4.5f, Vector3.forward);
                if (blockTension[b] > .05f) { hasOpenFracture = true; OpenFractureBlockCount++; }
                if (blockCompression[b] > .05f) CompressiveBlockCount++;
                if (Mathf.Abs(blockShearMotion[b]) > .15f) ShearingBlockCount++;
                if (Mathf.Abs(blockShearMotion[b]) > .25f) hasOpenFracture = true;
            }
            long afterStrain = stageTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            int count = fracturedVertexCount;
            for (int v = 0; v < count; v++)
            {
                int block = fracturedVertexBlocks[v], source = fracturedSources[v];
                Vector2 center = displayBlockCenters[block];
                float centerY = blockCenterHeights[block];
                float tension = blockTension[block], compression = blockCompression[block];
                float shear = blockShearMotion[block];
                float cosYaw = blockCosYaw[block], sinYaw = blockSinYaw[block];
                float cosPitch = blockCosPitch[block], sinPitch = blockSinPitch[block];
                float shearShift = shear * .35f;
                Quaternion rotation = blockNormalRotation[block];
                Vector3 point = soilVertices[source];
                float dx = point.x - center.x, dz = point.z - center.y;
                float radius = Mathf.Sqrt(dx * dx + dz * dz);
                float cover = channelClearance[source];
                float retreat = .52f * tension * Mathf.SmoothStep(0, 1, radius / 3f) * cover;
                float scale = radius > .001f ? Mathf.Max(0f, 1f - retreat / radius) : 1f;
                dx *= scale; dz *= scale;
                float relativeY = point.y - centerY;
                float tiltedX = dx * cosPitch - relativeY * sinPitch;
                float tiltedY = dx * sinPitch + relativeY * cosPitch;
                float arched = compression * 1.05f * cover * Mathf.Clamp01(1f - Mathf.Abs(tiltedX) / 5.4f);
                point.x = center.x + tiltedX * cosYaw - dz * sinYaw + shearShift;
                point.z = center.y + tiltedX * sinYaw + dz * cosYaw;
                float floor=data.Bed[source]+groundOffsets[source];
                if (cover>.10f && (tension+compression+Mathf.Abs(shear))>.015f)
                {
                    float ground=data.DisplayBedHeight(point.x,PresentationLayout.SolverZ(point.x,point.z));
                    floor=Mathf.Max(floor,ground);
                }
                point.y = Mathf.Max(floor+.03f,centerY+tiltedY+arched);
                fracturedVertices[v] = point;
                fracturedNormals[v] = (rotation * soilNormals[source]).normalized;
                fracturedTangents[v] = Tangent(fracturedNormals[v]);
                fracturedColors[v] = soilColors[source];
            }
            long afterDeformation = stageTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            fracturedMesh.SetVertices(fracturedVertices, 0, count);
            fracturedMesh.SetNormals(fracturedNormals, 0, count);
            fracturedMesh.SetTangents(fracturedTangents, 0, count);
            fracturedMesh.SetColors(fracturedColors, 0, count);
            fracturedMesh.bounds = soilBounds;
            long afterUpload = stageTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            UpdateFractureSeams();
            if (stageTiming)
            {
                long afterSeams = System.Diagnostics.Stopwatch.GetTimestamp();
                PerformanceProbe.RecordSurfaceFractureTicks(afterStrain-strainStart,
                    afterDeformation-afterStrain,afterUpload-afterDeformation,afterSeams-afterUpload);
            }
        }

        void UpdateFractureSeams()
        {
            bool stageTiming = PerformanceProbe.ComponentTimingEnabled;
            long geometryStart = stageTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            int openSeams = 0, visibleSeams = 0;
            for (int s = 0; s < fractureSeams.Count; s++)
            {
                FractureSeam seam = fractureSeams[s];
                int a = fracturedCellBlocks[seam.CellA], b = fracturedCellBlocks[seam.CellB];
                float activity = Mathf.Max(blockTension[a], blockTension[b],
                    Mathf.Abs(blockShearMotion[a]) * .45f, Mathf.Abs(blockShearMotion[b]) * .45f);
                float openness = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((activity - .12f) / .62f));
                int a0 = fracturedCornerVertices[seam.CellA * 4 + seam.A0];
                int a1 = fracturedCornerVertices[seam.CellA * 4 + seam.A1];
                int b0 = fracturedCornerVertices[seam.CellB * 4 + seam.B0];
                int b1 = fracturedCornerVertices[seam.CellB * 4 + seam.B1];
                if (Mathf.Min(fracturedColors[a0].a,fracturedColors[a1].a,
                    fracturedColors[b0].a,fracturedColors[b1].a)<.24f) continue;
                bool open = openness > 0f;
                if (stageTiming && open) openSeams++;
                // Both side walls collapse when openness is zero, but a
                // compressive offset can still expose the cap between blocks.
                bool separated = (fracturedVertices[a0] - fracturedVertices[b1]).sqrMagnitude > .00000001f ||
                    (fracturedVertices[a1] - fracturedVertices[b0]).sqrMagnitude > .00000001f;
                if (!open && !separated) continue;
                int start = visibleSeams * 22;
                visibleSeams++;
                WriteFractureWall(start, a0, a1, openness);
                WriteFractureWall(start + 8, b0, b1, openness);
                seamVertices[start + 16] = seamVertices[start + 6];
                seamVertices[start + 17] = seamVertices[start + 7];
                seamVertices[start + 18] = seamVertices[start + 14];
                seamVertices[start + 19] = seamVertices[start + 6];
                seamVertices[start + 20] = seamVertices[start + 14];
                seamVertices[start + 21] = seamVertices[start + 15];
                for (int tri = 0; tri < 2; tri++)
                {
                    int first = start + 16 + tri * 3;
                    Vector3 normal = Vector3.Cross(seamVertices[first + 1] - seamVertices[first],
                        seamVertices[first + 2] - seamVertices[first]).normalized;
                    if (normal.sqrMagnitude < .1f) normal = Vector3.up;
                    Vector4 tangent = Tangent(normal);
                    for (int corner = 0; corner < 3; corner++)
                    {
                        seamNormals[first + corner] = normal;
                        seamTangents[first + corner] = tangent;
                    }
                }
            }
            long afterGeometry = stageTiming ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            // Every seam has the same local triangle/color template. Compacting
            // only the nondegenerate instances changes no visible surface, and
            // a seek rebuilds the prefix from the current solver time.
            if (visibleSeams != renderedSeamCount)
            {
                seamMesh.SetTriangles(seamTriangles, 0, visibleSeams * 42, 0, false);
                renderedSeamCount = visibleSeams;
            }
            seamMesh.SetVertices(seamVertices, 0, seamVertexCount);
            seamMesh.SetNormals(seamNormals, 0, seamVertexCount);
            seamMesh.SetTangents(seamTangents, 0, seamVertexCount);
            seamMesh.bounds = soilBounds;
            hasOpenFracture = visibleSeams > 0;
            if (stageTiming)
                PerformanceProbe.RecordSeamTimingTicks(afterGeometry - geometryStart,
                    System.Diagnostics.Stopwatch.GetTimestamp() - afterGeometry,
                    openSeams, visibleSeams, fractureSeams.Count);
        }

        void WriteFractureWall(int start, int first, int second, float openness)
        {
            Vector3 a = fracturedVertices[first], b = fracturedVertices[second];
            int sourceA = fracturedSources[first], sourceB = fracturedSources[second];
            float floorA = data.Bed[sourceA] + groundOffsets[sourceA] + .002f;
            float floorB = data.Bed[sourceB] + groundOffsets[sourceB] + .002f;
            float drop = .9f * openness;
            Vector3 lowA = new Vector3(a.x, openness > .001f ? Mathf.Min(a.y - .006f * openness, Mathf.Max(floorA, a.y - drop)) : a.y, a.z);
            Vector3 lowB = new Vector3(b.x, openness > .001f ? Mathf.Min(b.y - .006f * openness, Mathf.Max(floorB, b.y - drop)) : b.y, b.z);
            Vector3 normal = Vector3.Cross(b - a, Vector3.down).normalized;
            if (normal.sqrMagnitude < .1f) normal = Vector3.forward;
            Vector4 tangent = Tangent(normal);
            for (int layer = 0; layer < 4; layer++)
            {
                float f = layer / 3f;
                seamVertices[start + layer * 2] = Vector3.Lerp(a, lowA, f);
                seamVertices[start + layer * 2 + 1] = Vector3.Lerp(b, lowB, f);
                seamNormals[start + layer * 2] = seamNormals[start + layer * 2 + 1] = normal;
                seamTangents[start + layer * 2] = seamTangents[start + layer * 2 + 1] = tangent;
            }
        }

        void UpdateBoundaryWalls(bool failed)
        {
            visibleSideTriangles.Clear();
            for (int e = 0; e < edges.Count; e++)
            {
                Edge edge = edges[e]; int v = e * 8;
                Vector3 a = failed ? fracturedVertices[edge.FracturedA] : soilVertices[edge.A];
                Vector3 b = failed ? fracturedVertices[edge.FracturedB] : soilVertices[edge.B];
                Vector3 inward = Vector3.Cross(Vector3.up, b - a).normalized;
                Vector3 baseA = new Vector3(a.x, data.Bed[edge.A] + groundOffsets[edge.A] + .012f, a.z);
                Vector3 baseB = new Vector3(b.x, data.Bed[edge.B] + groundOffsets[edge.B] + .012f, b.z);
                baseA += inward * Mathf.Clamp((a.y - baseA.y) * .16f, .04f, 1.5f);
                baseB += inward * Mathf.Clamp((b.y - baseB.y) * .16f, .04f, 1.5f);
                for (int layer = 0; layer < 4; layer++)
                {
                    float f = layer / 3f;
                    sideVertices[v + layer * 2] = Vector3.Lerp(a, baseA, f);
                    sideVertices[v + layer * 2 + 1] = Vector3.Lerp(b, baseB, f);
                }
                Vector3 normal = Vector3.Cross(sideVertices[v + 2] - a, b - a).normalized;
                if (normal.sqrMagnitude < .1f) normal = Vector3.Cross(Vector3.down, b - a).normalized;
                for (int k = 0; k < 8; k++) { sideNormals[v + k] = normal; sideTangents[v + k] = Tangent(normal); }
                if (Mathf.Min(soilColors[edge.A].a,soilColors[edge.B].a)<.18f ||
                    Mathf.Min(a.y-baseA.y,b.y-baseB.y)<.32f) continue;
                int first=e*18;
                for (int k=0;k<18;k++) visibleSideTriangles.Add(sideTriangles[first+k]);
            }
            int count = edges.Count * 8;
            sideMesh.SetVertices(sideVertices, 0, count);
            sideMesh.SetNormals(sideNormals, 0, count);
            sideMesh.SetTangents(sideTangents, 0, count);
            sideMesh.SetTriangles(visibleSideTriangles,0,false);
            sideMesh.bounds = soilBounds;
        }

        /// <summary>Checks the uploaded wall rim against the uploaded top mesh after a seek.</summary>
        public float MaximumBoundaryTopGap(bool failed)
        {
            if (sideMesh == null || SoilMesh == null || fracturedMesh == null) return float.PositiveInfinity;
            Vector3[] wall = sideMesh.vertices;
            Vector3[] top = failed ? fracturedMesh.vertices : SoilMesh.vertices;
            float maximum = 0f;
            for (int e = 0; e < edges.Count; e++)
            {
                Edge edge = edges[e];
                int a = failed ? edge.FracturedA : edge.A;
                int b = failed ? edge.FracturedB : edge.B;
                maximum = Mathf.Max(maximum, Vector3.Distance(wall[e * 8], top[a]));
                maximum = Mathf.Max(maximum, Vector3.Distance(wall[e * 8 + 1], top[b]));
            }
            return maximum;
        }

        void BuildCutawaySkirt(Material material)
        {
            var perimeter = new List<int>();
            for (int i = 0; i < data.NX; i++) perimeter.Add(i * data.NZ);
            for (int j = 1; j < data.NZ; j++) perimeter.Add((data.NX - 1) * data.NZ + j);
            for (int i = data.NX - 2; i >= 0; i--) perimeter.Add(i * data.NZ + data.NZ - 1);
            for (int j = data.NZ - 2; j > 0; j--) perimeter.Add(j);
            // Five decorative rings extend the landscape outside the numerical domain.
            // The first ring exactly shares its boundary; nothing is added to solver fields.
            const int rings = 5;
            float[] offsets = { 0f, 10f, 28f, 56f, 96f, 150f };
            var verts = new Vector3[perimeter.Count * (rings + 1)];
            var colors = new Color[verts.Length];
            var triangles = new int[perimeter.Count * rings * 6];
            for (int ring = 0; ring <= rings; ring++)
            for (int e = 0; e < perimeter.Count; e++)
            {
                int k = perimeter[e], i = k / data.NZ, j = k % data.NZ;
                Vector3 outward = new Vector3(i == 0 ? -1 : (i == data.NX - 1 ? 1 : 0), 0,
                    j == 0 ? -1 : (j == data.NZ - 1 ? 1 : 0)).normalized;
                float f = ring / (float)rings;
                float unwarp = Mathf.SmoothStep(0,1,(f-.36f)/.64f);
                float x = data.X[i] + outward.x * offsets[ring];
                float z = Mathf.Lerp(soilVertices[k].z,data.Z[j],unwarp) + outward.z * offsets[ring];
                float shoulder = (Mathf.PerlinNoise(x * .008f + 11, z * .008f + 21) - .45f) * 46f;
                float finer = (Mathf.PerlinNoise(x * .024f + 20, z * .024f + 30) - .5f) * 13f;
                float y = data.Bed[k] + groundOffsets[k] - offsets[ring] * .24f
                    + (shoulder+finer) * Mathf.SmoothStep(0,1,f*2f);
                int v = ring * perimeter.Count + e;
                verts[v] = new Vector3(x, y, z);
                float tint = Mathf.Lerp(1, .87f, f);
                colors[v] = new Color(tint, tint, tint, f * .25f);
                if (ring == rings) continue;
                int next = ring * perimeter.Count + (e + 1) % perimeter.Count;
                int outer = v + perimeter.Count, outerNext = next + perimeter.Count;
                int t = (ring * perimeter.Count + e) * 6;
                // Perimeter runs clockwise in world space: outward strips face upward.
                triangles[t] = v; triangles[t + 1] = next; triangles[t + 2] = outer;
                triangles[t + 3] = next; triangles[t + 4] = outerNext; triangles[t + 5] = outer;
            }
            var mesh = new Mesh { name = "Five ring landscape skirt outside numerical domain", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.triangles = triangles; mesh.colors = colors; mesh.RecalculateNormals();
            Vector3[] normals = mesh.normals, boundaryNormals = TerrainMesh.normals;
            for (int e = 0; e < perimeter.Count; e++) normals[e] = boundaryNormals[perimeter[e]];
            mesh.normals = normals; mesh.RecalculateBounds();
            LandscapeBoundary = new Vector3[perimeter.Count];
            LandscapeBoundaryNormals = new Vector3[perimeter.Count];
            System.Array.Copy(verts, rings * perimeter.Count, LandscapeBoundary, 0, perimeter.Count);
            System.Array.Copy(normals, rings * perimeter.Count, LandscapeBoundaryNormals, 0, perimeter.Count);
            MeshObject("Extended natural slope shoulders - visual only", mesh, material);
        }

        void BuildSourceScar(Material material)
        {
            scarpTiers.Clear();
            // The first source contour comes from the unchanged initial h field.
            // Decorative risers then retreat upslope; they never enter Bed or h.
            var head = new int[data.NZ];
            for (int j = 0; j < head.Length; j++)
            {
                head[j] = -1;
                for (int i = 0; i < data.NX; i++)
                    if (data.Depth[0][i * data.NZ + j] > .42f) { head[j] = i; break; }
            }
            float[] starts = { 0f, 3f, 7f, 12f };
            for (int tier = 0; tier < starts.Length; tier++)
            {
                var flat = new List<Vector3>();
                var raised = new List<Vector3>();
                var colors = new List<Color>();
                var triangles = new List<int>();
                var arrivals = new List<float>();
                for (int j = 0; j < data.NZ - 1; j++)
                {
                    if (head[j] < 0 || head[j + 1] < 0) continue;
                    float za = data.Z[j], zb = data.Z[j + 1];
                    float sourceA = data.X[head[j]], sourceB = data.X[head[j + 1]];
                    float xa = sourceA - tier * 4.4f, xb = sourceB - tier * 4.4f;
                    if (xa < data.MinX + 5f || xb < data.MinX + 5f) continue;
                    float riseA = ScarpRise(tier, sourceA, za);
                    float riseB = ScarpRise(tier, sourceB, zb);
                    // Three exposed color bands are duplicated for both face sides.
                    for (int side = 0; side < 2; side++)
                    {
                        int first = raised.Count;
                        for (int level = 0; level < 4; level++)
                        for (int end = 0; end < 2; end++)
                        {
                            float fraction = level / 3f;
                            float x = (end == 0 ? xa : xb) - .55f * fraction;
                            float z = end == 0 ? za : zb;
                            float baseY = data.DisplayBedHeight(x, z) + .035f;
                            float rise = end == 0 ? riseA : riseB;
                            flat.Add(PresentationLayout.Point(x, baseY - .18f, z));
                            raised.Add(PresentationLayout.Point(x, baseY + rise * fraction, z));
                            colors.Add(ScarpBandColor(level, tier));
                            arrivals.Add(ScarpArrival(tier, z));
                        }
                        for (int level = 0; level < 3; level++)
                        {
                            int lo = first + level * 2, hi = lo + 2;
                            if (side == 0)
                            {
                                triangles.Add(lo); triangles.Add(hi); triangles.Add(lo + 1);
                                triangles.Add(lo + 1); triangles.Add(hi); triangles.Add(hi + 1);
                            }
                            else
                            {
                                triangles.Add(lo + 1); triangles.Add(hi); triangles.Add(lo);
                                triangles.Add(hi + 1); triangles.Add(hi); triangles.Add(lo + 1);
                            }
                        }
                    }
                    // Each riser has a narrow tapered bench, so the face has a
                    // visible upper surface instead of ending as a paper wall.
                    int shelf = raised.Count;
                    for (int end = 0; end < 2; end++)
                    {
                        float z = end == 0 ? za : zb;
                        float x = end == 0 ? xa : xb;
                        float rise = end == 0 ? riseA : riseB;
                        float lipX = x - .55f, backX = x - 3.35f;
                        float lipY = data.DisplayBedHeight(lipX, z) + .035f;
                        float backY = data.DisplayBedHeight(backX, z) + .035f;
                        flat.Add(PresentationLayout.Point(lipX, lipY - .18f, z));
                        raised.Add(PresentationLayout.Point(lipX, lipY + rise, z));
                        colors.Add(new Color(.9f, .82f, .7f, .04f));
                        arrivals.Add(ScarpArrival(tier, z));
                        flat.Add(PresentationLayout.Point(backX, backY - .18f, z));
                        raised.Add(PresentationLayout.Point(backX, backY + rise * .16f, z));
                        colors.Add(new Color(.79f, .74f, .66f, .04f));
                        arrivals.Add(ScarpArrival(tier, z));
                    }
                    triangles.Add(shelf); triangles.Add(shelf + 1); triangles.Add(shelf + 2);
                    triangles.Add(shelf + 2); triangles.Add(shelf + 1); triangles.Add(shelf + 3);
                }
                var mesh = new Mesh { name = "Display-only source scarp tier " + (tier + 1), indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(raised); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var renderer = MeshObject("Progressive source scarp tier " + (tier + 1), mesh, material).GetComponent<MeshRenderer>();
                renderer.enabled = false;
                scarpTiers.Add(new ScarpTier { Mesh = mesh, Renderer = renderer, Flat = flat.ToArray(),
                    Raised = raised.ToArray(), Working = raised.ToArray(), Tangents = new Vector4[raised.Count],
                    Arrival = arrivals.ToArray(), Start = starts[tier] });
            }
        }

        static float ScarpArrival(int tier, float z)
        {
            // The first riser opens around the first crack before retreating
            // along the rest of the head, instead of drawing a source outline.
            return tier == 0 ? .65f + Mathf.Abs(z + 3f) * .065f : 0f;
        }

        float ScarpRise(int tier, float sourceX, float z)
        {
            if (tier == 0)
                return Mathf.Max(.8f, data.SurfaceHeight(0f, sourceX, z) - data.DisplayBedHeight(sourceX, z));
            float interior = data.InitialDepth(sourceX + 5f, z) * data.VisualDepthScale;
            return Mathf.Clamp(interior * .38f * Mathf.Pow(.76f, tier - 1), .75f, 2.25f);
        }

        static Color ScarpBandColor(int level, int tier)
        {
            Color color;
            if (level == 0) color = new Color(.56f, .53f, .48f, .95f);
            else if (level == 1) color = new Color(.68f, .62f, .54f, .72f);
            else if (level == 2) color = new Color(.79f, .71f, .61f, .45f);
            else color = new Color(.9f, .8f, .67f, .05f);
            color.r *= 1f - .035f * tier; color.g *= 1f - .035f * tier; color.b *= 1f - .035f * tier;
            return color;
        }

        void UpdateSourceScar(float seconds, bool failed)
        {
            foreach (ScarpTier tier in scarpTiers)
            {
                bool firstTier = tier.Start == 0f;
                float duration = firstTier ? 3.85f : .65f;
                float stageTime = failed ? Mathf.Clamp(seconds - tier.Start, 0f, duration) : 0f;
                tier.Renderer.enabled = stageTime > (firstTier ? .65f : .001f);
                if (!tier.Renderer.enabled || Mathf.Abs(stageTime - tier.Activation) < .0001f)
                {
                    tier.Activation = stageTime;
                    continue;
                }
                for (int i = 0; i < tier.Working.Length; i++)
                {
                    float open = Mathf.SmoothStep(0f, 1f, (stageTime - tier.Arrival[i]) / .65f);
                    tier.Working[i] = Vector3.Lerp(tier.Flat[i], tier.Raised[i], open);
                }
                tier.Mesh.vertices = tier.Working;
                tier.Mesh.RecalculateNormals();
                Vector3[] normals = tier.Mesh.normals;
                for (int i = 0; i < tier.Tangents.Length; i++) tier.Tangents[i] = Tangent(normals[i]);
                tier.Mesh.tangents = tier.Tangents;
                tier.Mesh.RecalculateBounds();
                tier.Activation = stageTime;
            }
        }

        void BuildCracks()
        {
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            for (int branch = 0; branch < CrackStartZ.Length; branch++)
            {
                float start = CrackStartZ[branch], end = CrackEndZ[branch];
                int segments = Mathf.CeilToInt((end - start) / .65f);
                int first = vertices.Count;
                for (int seg = 0; seg <= segments; seg++)
                {
                    float u = seg / (float)segments;
                    float z = Mathf.Lerp(start, end, u);
                    float x = CrackCenterX(branch, z);
                    float width = .08f + .24f * Mathf.Sin(Mathf.PI * u);
                    width *= .8f + .2f * Mathf.Sin(z * .53f + branch);
                    float delay = .055f + branch * .19f + Mathf.Abs(u - .43f) * 1.18f;
                    for (int side = 0; side < 2; side++)
                    {
                        float edge = x + (side == 0 ? -width : width);
                        vertices.Add(PresentationLayout.Point(edge, data.DisplayBedHeight(edge, z) + .095f, z));
                        uv.Add(new Vector2(side, delay));
                    }
                    if (seg == segments) continue;
                    int k = first + seg * 2;
                    triangles.Add(k); triangles.Add(k + 2); triangles.Add(k + 1);
                    triangles.Add(k + 1); triangles.Add(k + 2); triangles.Add(k + 3);
                }
            }
            crackMesh = new Mesh { name = "Pooled source fracture ribbons" };
            crackMesh.SetVertices(vertices); crackMesh.SetUVs(0, uv);
            crackMesh.SetTriangles(triangles, 0); crackMesh.RecalculateNormals();
            var crackShader = Resources.Load<Shader>("Shaders/RainCracks");
            if (crackShader == null) throw new System.InvalidOperationException("RainCracks shader is missing");
            crackMaterial = NewMaterial(crackShader, "Local first rupture");
            crackMaterial.color = new Color(.048f, .035f, .024f);
            crackRenderer = MeshObject("Source back cracks - one pooled mesh", crackMesh, crackMaterial).GetComponent<MeshRenderer>();
            crackRenderer.shadowCastingMode = ShadowCastingMode.Off;
            crackRenderer.enabled = false;
        }

        Material NewMaterial(Shader shader, string label)
        {
            if (shader == null) shader = Shader.Find("Standard");
            var material = new Material(shader) { name = label };
            ownedMaterials.Add(material); return material;
        }

        static float Max4(float a, float b, float c, float d)
        {
            return Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d));
        }

        GameObject MeshObject(string label, Mesh mesh, Material material)
        {
            ownedMeshes.Add(mesh);
            if (mesh.vertexCount > 0)
            {
                Vector3[] normals = mesh.normals;
                var tangents = new Vector4[mesh.vertexCount];
                for (int i = 0; i < tangents.Length; i++) tangents[i] = Tangent(normals[i]);
                mesh.tangents = tangents;
            }
            var go = new GameObject(label); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            return go;
        }

        static Vector4 Tangent(Vector3 normal)
        {
            Vector3 tangent = Vector3.Cross(Mathf.Abs(normal.z) < .95f ? Vector3.forward : Vector3.up, normal).normalized;
            return new Vector4(tangent.x, tangent.y, tangent.z, 1f);
        }

        void OnDestroy()
        {
            foreach (Material material in ownedMaterials) Release(material);
            foreach (Mesh mesh in ownedMeshes) Release(mesh);
            if (textureSet != null) textureSet.Dispose();
        }

        static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
