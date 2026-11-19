using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandslideDemo
{
    /// <summary>
    /// Decorative fracture, tumbling clods and dust, driven by the imported flow
    /// field.  The main h/u/v solver remains authoritative: these pieces do not
    /// add mass, change the flow, or claim to solve 3-D fragmentation mechanics.
    /// All paths and particle ages are functions of slide time, so seeking,
    /// rewinding and pausing produce the same visible state.
    /// </summary>
    public sealed class LandslideDebris : MonoBehaviour
    {
        const float PathStep = 0.25f;
        const int SoilCount = 160;
        const int BoulderCount = 16;
        const int DustCount = 72;
        // Match the fixed world-space sites used by LandslideSurface exactly.
        const float BlockSpacingX = 8.6f, BlockSpacingZ = 8.2f;

        struct SourceSeam
        {
            public Vector2 Midpoint, Tangent;
            public float Launch;
            public SourceSeam(Vector2 midpoint, Vector2 tangent, float launch)
            { Midpoint = midpoint; Tangent = tangent; Launch = launch; }
        }

        sealed class Piece
        {
            public Transform Transform;
            public Vector3 BaseScale;
            public Quaternion InitialRotation;
            public Vector3 SpinAxis;
            public Vector2[] Positions;
            public float[] Distances;
            public float[] Speeds;
            public float[] Tensions;
            public float[] Compressions;
            public float LaunchDelay;
            public float DisplayLaunch;
            public float EjectionImpulse;
            public float SettledAt;
            public int StackLayer;
            public float StackHeight;
            public bool Boulder;
        }

        sealed class DustPuff
        {
            public Transform Transform;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Properties;
            public int PieceIndex;
            public float Offset;
            public float Lifetime;
            public float Size;
            public Vector2 Jitter;
        }

        SimulationData data;
        GameObject effectsRoot;
        readonly List<Piece> pieces = new List<Piece>();
        readonly List<SourceSeam> sourceSeams = new List<SourceSeam>(500);
        readonly List<DustPuff> dust = new List<DustPuff>();
        readonly List<LineRenderer> cracks = new List<LineRenderer>();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        Material dustMaterial;
        Material crackMaterial;
        Texture2D dustTexture;
        Camera viewCamera;
        int pathSamples;
        public int TensionPieceCount { get; private set; }
        public int CompressionBoulderCount { get; private set; }
        public int StackedPieceCount { get; private set; }

        public void Initialize(SimulationData simulation, Material soilMaterial, Material rockMaterial)
        {
            if (effectsRoot != null) return;
            data = simulation;
            pathSamples = Mathf.CeilToInt(data.TimeMax / PathStep) + 1;
            effectsRoot = new GameObject("Time-seekable fracture and debris FX");
            effectsRoot.transform.SetParent(transform, false);
            var random = new System.Random(73921);
            BuildSourceSeams();
            var variants = new Mesh[12];
            for (int i = 0; i < variants.Length; i++)
            {
                variants[i] = ProceduralAssets.CreateRockMesh(9821 + i, 0.42f);
                ownedMeshes.Add(variants[i]);
            }

            for (int i = 0; i < SoilCount + BoulderCount; i++)
            {
                bool boulder = i >= SoilCount;
                Vector2 start;
                float launch, impulse;
                if (boulder) FindCompressionEvent(i - SoilCount, out start, out launch, out impulse);
                else { FindTensionLaunch(random, out start, out launch); impulse = 0f; }
                var obj = new GameObject(boulder ? "Advected boulder" : "Fractured soil clod");
                obj.transform.SetParent(effectsRoot.transform, false);
                obj.AddComponent<MeshFilter>().sharedMesh = variants[i % variants.Length];
                obj.AddComponent<MeshRenderer>().sharedMaterial = boulder ? rockMaterial : soilMaterial;
                float size = boulder ? Range(random, 1.0f, 2.0f) : Range(random, 0.48f, 1.15f);
                var piece = new Piece
                {
                    Transform = obj.transform,
                    BaseScale = new Vector3(size * Range(random, 0.8f, 1.4f),
                        size * Range(random, 0.48f, 0.9f), size * Range(random, 0.8f, 1.3f)),
                    InitialRotation = Quaternion.Euler(Range(random, 0, 360), Range(random, 0, 360), Range(random, 0, 360)),
                    SpinAxis = new Vector3(Range(random, -0.4f, 0.4f), Range(random, -0.25f, 0.25f), 1).normalized,
                    Positions = new Vector2[pathSamples],
                    Speeds = new float[pathSamples],
                    Distances = new float[pathSamples],
                    Tensions = new float[pathSamples],
                    Compressions = new float[pathSamples],
                    LaunchDelay = launch,
                    DisplayLaunch = boulder ? launch :
                        Mathf.Max(launch,LandslideSurface.ExposureArrival(start.x,start.y)+.36f),
                    EjectionImpulse = impulse,
                    SettledAt = float.PositiveInfinity,
                    Boulder = boulder
                };
                PrecomputePath(piece, start);
                pieces.Add(piece);
            }
            AssignSettlementStacks();
            foreach (Piece piece in pieces)
            {
                if (float.IsInfinity(piece.LaunchDelay)) continue;
                if (piece.Boulder) CompressionBoulderCount++;
                else TensionPieceCount++;
                if (piece.StackLayer > 0) StackedPieceCount++;
            }
            Debug.Log("STRAIN_DEBRIS_READY tension=" + TensionPieceCount +
                " compressionBoulders=" + CompressionBoulderCount +
                " stacked=" + StackedPieceCount +
                " sourceVoronoiSeams=" + sourceSeams.Count);
            CreateCracks();
            CreateDust(random);
            effectsRoot.SetActive(false);
        }

        static float Range(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        // Positive longitudinal velocity gradient opens a crack; a negative
        // gradient compresses the advancing toe. Both come from the imported UX.
        void VelocityStrain(float time, float x, float z,
            out float tension, out float compression)
        {
            const float radius = 2f;
            float left = Mathf.Max(data.MinX, x - radius), right = Mathf.Min(data.MaxX, x + radius);
            data.Sample(time, left, z, out float unusedDepth, out Vector2 uxLeft);
            data.Sample(time, right, z, out unusedDepth, out Vector2 uxRight);
            float longitudinal = (uxRight.x - uxLeft.x) / Mathf.Max(.01f, right - left);
            tension = Mathf.Max(0f, longitudinal);
            compression = Mathf.Max(0f, -longitudinal);
        }

        // A soil clod is born on an actual grid edge separating two different
        // Voronoi cell IDs, matching the fractured surface's seam topology.
        // Its appearance time is the first positive UX-gradient crossing at
        // that exact position, not a random animation clock.
        void FindTensionLaunch(System.Random random, out Vector2 start, out float launch)
        {
            start = Vector2.zero;
            launch = float.PositiveInfinity;
            if (sourceSeams.Count == 0) return;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                SourceSeam seam = sourceSeams[random.Next(sourceSeams.Count)];
                // Tangential jitter stays on the same cell edge (X cells are
                // 1 m, Z cells 2 m in the supplied field).
                Vector2 candidate = seam.Midpoint + seam.Tangent * Range(random, -.22f, .22f);
                float crossing = FirstTensionCrossing(candidate);
                if (float.IsInfinity(crossing)) continue;
                start = candidate;
                launch = crossing;
                return;
            }
            // Every retained midpoint was already verified against the field.
            SourceSeam fallback = sourceSeams[random.Next(sourceSeams.Count)];
            start = fallback.Midpoint;
            launch = fallback.Launch;
        }

        float FirstTensionCrossing(Vector2 point)
        {
            const float openingThreshold = .085f;
            float previous = 0f;
            for (float t = .5f; t <= 7f; t += .5f)
            {
                VelocityStrain(t, point.x, point.y, out float opening, out float unusedCompression);
                if (opening >= openingThreshold)
                {
                    float fraction = Mathf.Clamp01((openingThreshold - previous) /
                        Mathf.Max(.0001f, opening - previous));
                    return t - .5f + .5f * fraction;
                }
                previous = opening;
            }
            return float.PositiveInfinity;
        }

        void BuildSourceSeams()
        {
            sourceSeams.Clear();
            int rows = data.NZ - 1;
            for (int i = 0; i < data.NX - 1; i++)
            {
                float cellX = (data.X[i] + data.X[i + 1]) * .5f;
                if (cellX < 61f || cellX > 116f) continue;
                for (int j = 0; j < rows; j++)
                {
                    float cellZ = (data.Z[j] + data.Z[j + 1]) * .5f;
                    if (Mathf.Abs(cellZ) > 23f) continue;
                    long block = VoronoiBlockKey(cellX, cellZ);
                    if (i + 1 < data.NX - 1)
                    {
                        float neighborX = (data.X[i + 1] + data.X[i + 2]) * .5f;
                        if (block != VoronoiBlockKey(neighborX, cellZ))
                            AddSourceSeam(new Vector2(data.X[i + 1], cellZ), Vector2.up);
                    }
                    if (j + 1 < rows)
                    {
                        float neighborZ = (data.Z[j + 1] + data.Z[j + 2]) * .5f;
                        if (block != VoronoiBlockKey(cellX, neighborZ))
                            AddSourceSeam(new Vector2(cellX, data.Z[j + 1]), Vector2.right);
                    }
                }
            }
        }

        void AddSourceSeam(Vector2 midpoint, Vector2 tangent)
        {
            if (midpoint.x < 61.75f || midpoint.x > 115.95f || Mathf.Abs(midpoint.y) > 22f ||
                data.InitialDepth(midpoint.x, midpoint.y) <= .22f) return;
            float crossing = FirstTensionCrossing(midpoint);
            if (!float.IsInfinity(crossing)) sourceSeams.Add(new SourceSeam(midpoint, tangent, crossing));
        }

        static long VoronoiBlockKey(float x, float z)
        {
            int baseX = Mathf.FloorToInt(x / BlockSpacingX);
            int baseZ = Mathf.FloorToInt(z / BlockSpacingZ);
            float nearest = float.PositiveInfinity;
            int chosenX = 0, chosenZ = 0;
            for (int gx = baseX - 1; gx <= baseX + 1; gx++)
            for (int gz = baseZ - 1; gz <= baseZ + 1; gz++)
            {
                Vector2 site = BlockSite(gx, gz);
                float distance = (site - new Vector2(x, z)).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance; chosenX = gx; chosenZ = gz;
            }
            return ((long)chosenX << 32) | (uint)chosenZ;
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

        void FindCompressionEvent(int index, out Vector2 start, out float launch, out float impulse)
        {
            float preferred = 8f + index * (10.5f / Mathf.Max(1, BoulderCount - 1));
            float lane = -10f + index * (20f / Mathf.Max(1, BoulderCount - 1));
            start = Vector2.zero;
            launch = float.PositiveInfinity;
            impulse = 0f;
            float bestScore = float.NegativeInfinity;
            for (int dt = -2; dt <= 2; dt++)
            {
                float time = Mathf.Clamp(preferred + dt * .5f, 7.5f, data.TimeMax);
                float front = data.Statistic(time, 0);
                for (float x = front - 33f; x <= front - 1f; x += 2.5f)
                for (float z = lane - 5f; z <= lane + 5f; z += 2.5f)
                {
                    data.Sample(time, x, z, out float depth, out Vector2 velocity);
                    if (depth < .08f || velocity.magnitude < 1.5f) continue;
                    VelocityStrain(time, x, z, out float unusedTension, out float pressure);
                    if (pressure < .08f) continue;
                    float score = pressure + .008f * velocity.magnitude - .008f * Mathf.Abs(time - preferred);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    start = new Vector2(x, z);
                    launch = time;
                    impulse = pressure;
                }
            }
        }

        void AssignSettlementStacks()
        {
            var settled = new List<Piece>();
            foreach (Piece piece in pieces)
            {
                if (float.IsInfinity(piece.LaunchDelay)) continue;
                int last = pathSamples - 1;
                Vector2 final = piece.Positions[last];
                data.Sample(data.TimeMax, final.x, final.y, out float depth, out Vector2 unusedVelocity);
                if (final.x < 220f || depth < .08f || piece.Speeds[last] > .45f) continue;
                int lastMoving = -1;
                for (int i = 0; i < pathSamples; i++)
                    if (piece.Speeds[i] > .65f) lastMoving = i;
                if (lastMoving < 0) continue;
                piece.SettledAt = Mathf.Max(piece.LaunchDelay, Mathf.Min(data.TimeMax,
                    (lastMoving + 1) * PathStep));
                settled.Add(piece);
            }
            settled.Sort((a, b) => a.SettledAt.CompareTo(b.SettledAt));
            for (int i = 0; i < settled.Count; i++)
            {
                Piece piece = settled[i];
                for (int j = 0; j < i; j++)
                    if (Vector2.Distance(piece.Positions[pathSamples - 1],
                        settled[j].Positions[pathSamples - 1]) < 2.8f)
                        piece.StackLayer = Mathf.Max(piece.StackLayer, settled[j].StackLayer + 1);
                piece.StackLayer = Mathf.Min(piece.StackLayer, 3);
                piece.StackHeight = piece.StackLayer * .48f;
            }
        }

        void PrecomputePath(Piece piece, Vector2 start)
        {
            Vector2 position = start;
            float travelled = 0;
            for (int i = 0; i < pathSamples; i++)
            {
                float t = Mathf.Min(i * PathStep, data.TimeMax);
                piece.Positions[i] = position;
                piece.Distances[i] = travelled;
                if (t >= piece.LaunchDelay)
                {
                    data.Sample(t, position.x, position.y, out float depth, out Vector2 localVelocity);
                    piece.Speeds[i] = depth > .02f ? localVelocity.magnitude : 0f;
                    VelocityStrain(t, position.x, position.y, out piece.Tensions[i],
                        out piece.Compressions[i]);
                }
                if (i + 1 == pathSamples) break;
                float step = Mathf.Max(0f, Mathf.Min(PathStep, data.TimeMax - t) -
                    Mathf.Max(0f, piece.LaunchDelay - t));
                if (step <= 0f) continue;
                float startTime = Mathf.Max(t, piece.LaunchDelay);
                data.Sample(startTime, position.x, position.y, out float startDepth, out Vector2 velocity);
                if (startDepth <= .01f) velocity = Vector2.zero;
                // Midpoint advection follows the two imported horizontal fields.
                Vector2 midpoint = position + velocity * (step * .5f);
                data.Sample(Mathf.Min(startTime + step * .5f, data.TimeMax), midpoint.x, midpoint.y,
                    out float midDepth, out Vector2 midVelocity);
                if (midDepth <= 0.01f) midVelocity *= Mathf.Clamp01(midDepth / 0.01f);
                Vector2 next = position + midVelocity * step;
                next.x = Mathf.Clamp(next.x, 0.5f, 419.5f);
                next.y = Mathf.Clamp(next.y, -68.5f, 68.5f);
                travelled += Vector2.Distance(position, next);
                position = next;
            }
        }

        void CreateCracks()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            crackMaterial = new Material(shader) { name = "Dark soil fracture seams" };
            int count = Mathf.Min(64, sourceSeams.Count);
            for (int i = 0; i < count; i++)
            {
                SourceSeam seam = sourceSeams[i * sourceSeams.Count / count];
                var obj = new GameObject("Source fracture seam");
                obj.transform.SetParent(effectsRoot.transform, false);
                var line = obj.AddComponent<LineRenderer>();
                line.sharedMaterial = crackMaterial;
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.numCornerVertices = 1;
                line.numCapVertices = 1;
                line.widthMultiplier = 0.15f;
                float halfLength = seam.Tangent.x > .5f ?
                    (data.X[1] - data.X[0]) * .44f : (data.Z[1] - data.Z[0]) * .44f;
                for (int j = 0; j < 2; j++)
                {
                    Vector2 point = seam.Midpoint + seam.Tangent * (j == 0 ? -halfLength : halfLength);
                    float py = data.SurfaceHeight(0, point.x, point.y) + .035f;
                    line.SetPosition(j, PresentationLayout.Point(point.x, py, point.y));
                }
                line.startColor = line.endColor = new Color(0.13f, 0.065f, 0.026f, 0.9f);
                cracks.Add(line);
            }
        }

        void CreateDust(System.Random random)
        {
            dustTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
            {
                name = "Procedural soft dust alpha",
                wrapMode = TextureWrapMode.Clamp
            };
            var colors = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float radius = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                float alpha = Mathf.Pow(Mathf.Clamp01(1 - radius), 1.7f);
                colors[x + y * 32] = new Color(1, 1, 1, alpha);
            }
            dustTexture.SetPixels(colors);
            dustTexture.Apply(false, true);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            dustMaterial = new Material(shader) { name = "Procedural dust billboards", mainTexture = dustTexture };
            dustMaterial.renderQueue = 3000;
            var quad = new Mesh { name = "Double-sided dust quad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 };
            quad.RecalculateNormals();
            ownedMeshes.Add(quad);
            for (int i = 0; i < DustCount; i++)
            {
                var obj = new GameObject("Pooled advected dust puff");
                obj.transform.SetParent(effectsRoot.transform, false);
                obj.AddComponent<MeshFilter>().sharedMesh = quad;
                var renderer = obj.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = dustMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                dust.Add(new DustPuff
                {
                    Transform = obj.transform,
                    Renderer = renderer,
                    Properties = new MaterialPropertyBlock(),
                    PieceIndex = (i * 13 + 7) % pieces.Count,
                    Offset = Range(random, 0, 1.6f),
                    Lifetime = Range(random, 1.1f, 1.9f),
                    Size = Range(random, 3.5f, 5.5f),
                    Jitter = new Vector2(Range(random, -1.5f, 1.5f), Range(random, -1.5f, 1.5f))
                });
            }
        }

        void EvaluatePath(Piece piece, float time, out Vector2 position, out float speed, out float distance)
        {
            float index = Mathf.Clamp(time / PathStep, 0, pathSamples - 1);
            int a = Mathf.FloorToInt(index);
            int b = Mathf.Min(a + 1, pathSamples - 1);
            float f = index - a;
            position = Vector2.LerpUnclamped(piece.Positions[a], piece.Positions[b], f);
            speed = Mathf.LerpUnclamped(piece.Speeds[a], piece.Speeds[b], f);
            distance = Mathf.LerpUnclamped(piece.Distances[a], piece.Distances[b], f);
        }

        float EvaluateSeries(float[] samples, float time)
        {
            float index = Mathf.Clamp(time / PathStep, 0, pathSamples - 1);
            int a = Mathf.FloorToInt(index), b = Mathf.Min(a + 1, pathSamples - 1);
            return Mathf.LerpUnclamped(samples[a], samples[b], index - a);
        }

        public void SetState(float slideTime, bool failed, bool enabledEffects)
        {
            if (effectsRoot == null || data == null) return;
            bool visible = failed && enabledEffects;
            if (effectsRoot.activeSelf != visible) effectsRoot.SetActive(visible);
            if (!visible) return;
            float time = Mathf.Clamp(slideTime, 0, data.TimeMax);
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                bool launched = time >= piece.DisplayLaunch;
                if (piece.Transform.gameObject.activeSelf != launched)
                    piece.Transform.gameObject.SetActive(launched);
                if (!launched) continue;
                float openingDelay = (piece.DisplayLaunch-piece.LaunchDelay) *
                    (1f-Mathf.SmoothStep(0,1,(time-piece.DisplayLaunch)/2.7f));
                float sampleTime = time-openingDelay;
                EvaluatePath(piece, sampleTime, out Vector2 p, out float speed, out float distance);
                float poseTime = Mathf.Max(piece.LaunchDelay,sampleTime -
                    PresentationLayout.ShelfDelay(time)*PresentationLayout.ShelfInfluence(p.x,p.y));
                if (poseTime < sampleTime) EvaluatePath(piece, poseTime, out p, out speed, out distance);
                float reveal = Mathf.SmoothStep(0, 1, (time - piece.DisplayLaunch) / 0.25f);
                float tension = EvaluateSeries(piece.Tensions, time);
                float age = time - piece.LaunchDelay;
                // One toe ejection follows the measured compression event. Source
                // clods only lift as their measured tension crack opens.
                float hop = piece.Boulder && age < .8f
                    ? Mathf.Sin(Mathf.PI * age / .8f) * Mathf.Min(1.45f, piece.EjectionImpulse * 3.5f)
                    : (piece.Boulder ? 0f : Mathf.Min(.18f, tension * .8f));
                float stack = float.IsInfinity(piece.SettledAt) ? 0f :
                    piece.StackHeight * Mathf.SmoothStep(0f, 1f, (time - piece.SettledAt) / .7f);
                float y = data.SurfaceHeight(poseTime, p.x, p.y);
                if (!piece.Boulder)
                {
                    y = Mathf.Lerp(data.DisplayBedHeight(p.x,p.y), y, Mathf.SmoothStep(0f,1f,(time-.75f)/2.75f));
                }
                y += piece.BaseScale.y * .25f + hop + stack;
                piece.Transform.position = PresentationLayout.Point(p.x, y, p.y);
                reveal *= PresentationLayout.IslandExposure(p.x,p.y);
                piece.Transform.localScale = piece.BaseScale * reveal;
                piece.Transform.rotation = Quaternion.AngleAxis(distance * (piece.Boulder ? 40f : 65f) /
                    Mathf.Max(piece.BaseScale.x, 0.4f), piece.SpinAxis) * piece.InitialRotation;
            }

            for (int i = 0; i < cracks.Count; i++)
            {
                LineRenderer line = cracks[i];
                Vector3 center = (line.GetPosition(0) + line.GetPosition(1)) * .5f;
                VelocityStrain(time, center.x, PresentationLayout.SolverZ(center.x,center.z), out float opening,
                    out float unusedCompression);
                bool show = time > 2.7f && time < 8f && opening > .035f;
                cracks[i].gameObject.SetActive(show);
                if (show)
                {
                    float alpha = Mathf.Clamp01((opening - .035f) / .15f) *
                        Mathf.Clamp01((8f - time) / 1.3f);
                    Color color = new Color(.12f, .055f, .02f, alpha);
                    line.startColor = line.endColor = color;
                    line.widthMultiplier = .12f + Mathf.Min(.55f, opening * 1.7f);
                    for (int j = 0; j < line.positionCount; j++)
                    {
                        Vector3 p = line.GetPosition(j);
                        p.y = data.SurfaceHeight(time, p.x, PresentationLayout.SolverZ(p.x,p.z)) + .035f;
                        line.SetPosition(j, p);
                    }
                }
            }
            if (viewCamera == null) viewCamera = Camera.main;
            for (int i = 0; i < dust.Count; i++) UpdateDust(dust[i], time);
        }

        void UpdateDust(DustPuff puff, float time)
        {
            float period = puff.Lifetime * 0.88f;
            float emission = Mathf.Floor((time + puff.Offset) / period) * period - puff.Offset;
            float age = time - emission;
            Piece piece = pieces[puff.PieceIndex];
            EvaluatePath(piece, Mathf.Max(emission, 0), out Vector2 p, out float speed, out float unusedDistance);
            bool show = emission > piece.DisplayLaunch && age < puff.Lifetime && speed > 0.8f;
            if (puff.Transform.gameObject.activeSelf != show) puff.Transform.gameObject.SetActive(show);
            if (!show) return;
            data.Sample(emission, p.x, p.y, out float depth, out Vector2 velocity);
            Vector2 drift = velocity * (age * 0.12f) + puff.Jitter;
            float y = data.SurfaceHeight(emission, p.x, p.y) + 0.5f + age * 1.1f;
            if (emission < 2.7f)
                y = data.DisplayBedHeight(p.x,p.y) + .2f + age*1.1f;
            puff.Transform.position = PresentationLayout.Point(p.x + drift.x, y, p.y + drift.y);
            if (viewCamera != null)
                puff.Transform.rotation = Quaternion.LookRotation(viewCamera.transform.position - puff.Transform.position);
            float size = puff.Size + age * 2.2f;
            puff.Transform.localScale = new Vector3(size, size * 0.75f, size);
            float strain = Mathf.Max(EvaluateSeries(piece.Tensions, Mathf.Max(emission, 0)) / .14f,
                EvaluateSeries(piece.Compressions, Mathf.Max(emission, 0)) / .20f);
            float alpha = Mathf.Min(.34f, speed * .018f) * Mathf.Lerp(.35f, 1f, Mathf.Clamp01(strain)) *
                Mathf.Clamp01(age * 4) * Mathf.Clamp01(1 - age / puff.Lifetime);
            Color tint = new Color(0.60f, 0.45f, 0.29f, alpha);
            puff.Properties.SetColor("_Color", tint);
            puff.Properties.SetColor("_TintColor", tint);
            puff.Renderer.SetPropertyBlock(puff.Properties);
        }

        public bool ValidateTimeline(out string result)
        {
            result = "";
            if (sourceSeams.Count == 0 || TensionPieceCount == 0 || CompressionBoulderCount == 0)
            { result = "Missing strain-driven seam or debris events."; return false; }
            int aligned = 0;
            foreach (Piece piece in pieces)
            {
                if (piece.Boulder || float.IsInfinity(piece.LaunchDelay)) continue;
                Vector2 start = piece.Positions[0];
                bool onSeam = false;
                foreach (SourceSeam seam in sourceSeams)
                {
                    Vector2 offset = start - seam.Midpoint;
                    float along = Vector2.Dot(offset, seam.Tangent);
                    if (Mathf.Abs(along) <= .221f &&
                        (offset - seam.Tangent * along).sqrMagnitude < .0000001f)
                    { onSeam = true; break; }
                }
                if (!onSeam)
                { result = "A source clod does not begin on a Voronoi seam."; return false; }
                float crossing = FirstTensionCrossing(start);
                if (float.IsInfinity(crossing) || Mathf.Abs(crossing - piece.LaunchDelay) > .001f)
                { result = "A source clod launch time disagrees with its UX gradient."; return false; }
                aligned++;
            }
            var positions = new Vector3[pieces.Count];
            var rotations = new Quaternion[pieces.Count];
            var scales = new Vector3[pieces.Count];
            var active = new bool[pieces.Count];
            try
            {
                SetState(12f, true, true);
                for (int i = 0; i < pieces.Count; i++)
                {
                    active[i] = pieces[i].Transform.gameObject.activeSelf;
                    if (!active[i]) continue;
                    positions[i] = pieces[i].Transform.position;
                    rotations[i] = pieces[i].Transform.rotation;
                    scales[i] = pieces[i].Transform.localScale;
                }
                SetState(24f, true, true);
                SetState(12f, true, true);
                for (int i = 0; i < pieces.Count; i++)
                {
                    Transform part = pieces[i].Transform;
                    if (part.gameObject.activeSelf != active[i])
                    { result = "Debris visibility changed after seeking."; return false; }
                    if (!active[i]) continue;
                    if ((part.position - positions[i]).sqrMagnitude > .00000001f ||
                        (part.localScale - scales[i]).sqrMagnitude > .00000001f ||
                        Quaternion.Angle(part.rotation, rotations[i]) > .001f)
                    { result = "Debris transforms changed after seeking."; return false; }
                }
            }
            finally { SetState(0f, false, true); }
            result = "alignedSourceClods=" + aligned + " sourceSeams=" + sourceSeams.Count +
                " compressionBoulders=" + CompressionBoulderCount + " seekAndRewind=true";
            return true;
        }

        void OnDestroy()
        {
            foreach (Mesh mesh in ownedMeshes) if (mesh != null) Destroy(mesh);
            if (dustMaterial != null) Destroy(dustMaterial);
            if (crackMaterial != null) Destroy(crackMaterial);
            if (dustTexture != null) Destroy(dustTexture);
        }
    }
}
