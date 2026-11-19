using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace LandslideDemo
{
    // All numerical arrays retain metres, seconds, kg and joules from Python.
    public sealed class SimulationData
    {
        public readonly int NX, NZ, FrameCount;
        public readonly float[] X, Z, Times, Bed;
        public readonly float[][] Depth, UX, UZ;
        public readonly float[,] Rain, Summary;
        readonly float[] displayBed, visualGround, visualFactor;
        readonly Vector2[] groundCover;
        public float VisualDepthScale = 5f;
        public float PeakSpeed { get; private set; }
        public float TimeMax { get { return Times[FrameCount - 1]; } }
        public float MinX { get { return X[0]; } }
        public float MaxX { get { return X[NX - 1]; } }
        public float MinZ { get { return Z[0]; } }
        public float MaxZ { get { return Z[NZ - 1]; } }

        public SimulationData(string path)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "LSIM2D01")
                    throw new InvalidDataException("Unsupported landslide data header");
                NX = reader.ReadInt32(); NZ = reader.ReadInt32();
                FrameCount = reader.ReadInt32(); int nr = reader.ReadInt32();
                if (NX < 2 || NZ < 2 || FrameCount < 2 || NX * NZ > 1000000)
                    throw new InvalidDataException("Invalid field dimensions");
                X = Read(reader, NX); Z = Read(reader, NZ); Times = Read(reader, FrameCount);
                Bed = Read(reader, NX * NZ);
                Depth = new float[FrameCount][]; UX = new float[FrameCount][]; UZ = new float[FrameCount][];
                for (int t = 0; t < FrameCount; t++)
                {
                    Depth[t] = Read(reader, NX * NZ); UX[t] = Read(reader, NX * NZ); UZ[t] = Read(reader, NX * NZ);
                }
                Rain = new float[nr, 5]; Summary = new float[FrameCount, 5];
                for (int i = 0; i < nr; i++) for (int k = 0; k < 5; k++) Rain[i,k] = reader.ReadSingle();
                for (int i = 0; i < FrameCount; i++) for (int k = 0; k < 5; k++) Summary[i,k] = reader.ReadSingle();
                for(int i=0;i<FrameCount;i++)PeakSpeed=Mathf.Max(PeakSpeed,Summary[i,1]);
                if (reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new InvalidDataException("Unexpected trailing data");
            }
            int cells = NX * NZ;
            displayBed = new float[cells]; visualGround = new float[cells]; visualFactor = new float[cells];
            groundCover = new Vector2[cells];
            for (int i = 0; i < NX; i++) for (int j = 0; j < NZ; j++)
            {
                int k = i * NZ + j;
                float groundOffset = ComputeVisualGroundOffset(X[i], Z[j], out float cover);
                visualGround[k] = groundOffset;
                displayBed[k] = Bed[k] + groundOffset;
                visualFactor[k] = ComputeVisualReliefFactor(X[i], Z[j], groundOffset);
                groundCover[k] = new Vector2(0f, cover);
            }
        }
        static float[] Read(BinaryReader r, int n)
        {
            byte[] bytes = r.ReadBytes(n * 4);
            if (bytes.Length != n * 4) throw new EndOfStreamException();
            var values = new float[n]; Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length); return values;
        }
        public void Frame(float seconds, out int a, out int b, out float blend)
        {
            float f = Mathf.Clamp(seconds / (Times[1] - Times[0]), 0, FrameCount - 1);
            a = Mathf.FloorToInt(f); b = Mathf.Min(a + 1, FrameCount - 1); blend = f - a;
        }
        float Spatial(float[] values, float x, float z)
        {
            float fi = Mathf.Clamp((x - MinX) / (X[1] - X[0]), 0, NX - 1);
            float fj = Mathf.Clamp((z - MinZ) / (Z[1] - Z[0]), 0, NZ - 1);
            int i = Mathf.Min(Mathf.FloorToInt(fi), NX - 2), j = Mathf.Min(Mathf.FloorToInt(fj), NZ - 2);
            float a = fi - i, b = fj - j;
            return Mathf.Lerp(Mathf.Lerp(values[i * NZ + j], values[(i + 1) * NZ + j], a),
                              Mathf.Lerp(values[i * NZ + j + 1], values[(i + 1) * NZ + j + 1], a), b);
        }
        // BedHeight stays the unmodified solver elevation. Only geometry and
        // scenery should use DisplayBedHeight.
        public float BedHeight(float x, float z) { return Spatial(Bed, x, z); }
        public float DisplayBedHeight(float x, float z) { return Spatial(displayBed, x, z); }
        public float DisplayWorldBedHeight(float x, float worldZ)
        { return DisplayBedHeight(x, PresentationLayout.SolverZ(x, worldZ)); }
        public float InitialDepth(float x, float z) { return Spatial(Depth[0], x, z); }
        public float SurfaceHeight(float seconds, float x, float z)
        {
            float ground = DisplayBedHeight(x, z);
            if (x < MinX || x > MaxX || z < MinZ || z > MaxZ) return ground;
            Frame(seconds, out int a, out int b, out float blend);
            float h = Mathf.Lerp(Spatial(Depth[a], x, z), Spatial(Depth[b], x, z), blend);
            return h <= .0001f ? ground : ground + h * VisualDepthScale * VisualReliefFactor(x, z);
        }
        static float ReliefNoise(float x, float z)
        {
            // Three world-space octaves. The fixed offsets make this repeatable
            // across playback, seeking and separate Player captures.
            float broad = 2f * Mathf.PerlinNoise(x * .012f + 17.4f, z * .012f + 93.7f) - 1f;
            float medium = 2f * Mathf.PerlinNoise(x * .031f + 61.8f, z * .031f + 12.6f) - 1f;
            float fine = 2f * Mathf.PerlinNoise(x * .074f + 38.2f, z * .074f + 47.1f) - 1f;
            return .56f * broad + .29f * medium + .15f * fine;
        }
        public float VisualGroundOffset(float x, float z)
        { return Spatial(visualGround, x, z); }

        // This is a static material mask from the decorative landforms. It
        // does not sample initial thickness or predict where the slope fails.
        public Vector2 GroundCover(int gridIndex) { return groundCover[gridIndex]; }

        static float Smooth(float low, float high, float value)
        { return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(low, high, value)); }

        static float Landform(float x, float z, float xCenter, float zCenter, float length, float width)
        { return PresentationLayout.Bell(x, xCenter, length) * PresentationLayout.Bell(z, zCenter, width); }

        // x progresses downhill. Lateral catchments curve into the central
        // ravine at different elevations instead of following parallel bands.
        static float Tributary(float x, float z, float tip, float mouth, float side, float bend,
            float depth, out float floor)
        {
            float p = Mathf.InverseLerp(tip, mouth, x);
            float branchZ = side * Mathf.Pow(1f - p, 1.18f) + bend * Mathf.Sin(Mathf.PI * p);
            float span = Smooth(tip - 1f, tip + 21f, x) * (1f - Smooth(mouth + 2f, mouth + 20f, x));
            float profile = PresentationLayout.Bell(z, branchZ, Mathf.Lerp(7.2f, 13.4f, p));
            floor = span * PresentationLayout.Bell(z, branchZ, Mathf.Lerp(4.5f, 8f, p));
            return depth * span * profile;
        }

        float ComputeVisualGroundOffset(float x, float z, out float cover)
        {
            // Taper the display-only relief to zero at the numerical boundary,
            // preserving the skirt and its surrounding landscape seam.
            cover = 0f;
            float edge = Smooth(MinX, MinX + 24f, x) * (1f - Smooth(MaxX - 28f, MaxX, x))
                       * Smooth(MinZ, MinZ + 18f, z) * (1f - Smooth(MaxZ - 18f, MaxZ, z));
            if (edge <= 0f) return 0f;

            float worldZ = PresentationLayout.WorldZ(x, z);
            float roughness = ReliefNoise(x, worldZ);
            float creekZ = 1.7f * ReliefNoise(x + 49f, 65f);
            float village = Landform(x, z, 284f, 0f, 51f, 22f);
            float midreach = PresentationLayout.Bell(x, 179f, 119f);
            float ravineWidth = Mathf.Lerp(17f, 12f, midreach) + 4f * roughness;
            float ravine = PresentationLayout.Bell(z, creekZ, ravineWidth);
            float ravineDepth = Mathf.Lerp(3.5f + 4.4f * midreach, .6f, Smooth(244f, 288f, x));
            float branchA = Tributary(x, z, 58f, 149f, -51f, -6f, 7.2f, out float wetA);
            float branchB = Tributary(x, z, 81f, 210f, 57f, 10f, 9.3f, out float wetB);
            float branchC = Tributary(x, z, 179f, 279f, -54f, -7f, 6.2f, out float wetC);
            // This lower tributary loses incision as it reaches the village
            // flats. Keep runoff colour and a shallow confluence on the fan.
            branchC *= Mathf.Lerp(1f, .24f, Smooth(246f, 274f, x));
            float lateral = Mathf.Max(branchA, Mathf.Max(branchB, branchC));
            // Blend cuts at confluences rather than adding them into a pit.
            float cut = Mathf.Max(ravine * ravineDepth, lateral);

            float firstNose = 12.5f * Landform(x, z, 128f, -27f - .12f * (x - 128f), 69f, 25f);
            float secondNose = 14.2f * Landform(x, z, 174f, 20f + .17f * (x - 174f), 67f, 26f);
            float southernSpur = 9.4f * Landform(x, z, 228f, -26f + .11f * (x - 228f), 52f, 26f);
            float noses = (firstNose + secondNose + southernSpur) * (1f - .75f * ravine);
            // The two benches soften the downhill grade at their inner edges,
            // then slope back into the surrounding shoulders.
            float upperBench = .37f * (x - 111f) * Landform(x, z, 111f, 34f, 33f, 22f);
            float lowerBench = .22f * (x - 229f) * Landform(x, z, 229f, -39f, 36f, 24f);
            float basin = 4.6f * Landform(x, z, 61f, 26f + .13f * (x - 61f), 47f, 26f);
            float rockNose = Mathf.Pow(PresentationLayout.Island(x, z), .84f);
            float shelf = 2.35f * PresentationLayout.ShelfInfluence(x, z) * PresentationLayout.Bell(z, 10f, 15f);
            float fan = Landform(x, z, 321f, 1f + 2f * roughness, 60f, 29f);
            float fanSurface = fan * (2.7f - .019f * (x - 310f) + .42f * roughness);
            float confluenceBars = 1.35f * Landform(x, z, 153f, -9f, 14f, 9f)
                                 + 1.7f * Landform(x, z, 214f, 10f, 15f, 12f)
                                 + .9f * Landform(x, z, 286f, -8f, 18f, 13f);

            float wetRavine = .6f * PresentationLayout.Bell(z, creekZ, ravineWidth * .75f)
                                   * (1f - .52f * Smooth(276f, 323f, x));
            float wetland = Mathf.Max(wetRavine, .79f * Mathf.Max(wetA, Mathf.Max(wetB, wetC))) + .22f * fan;
            float outcrop = .99f * Smooth(.05f, .63f, rockNose)
                + .45f * Smooth(10f, 13f, secondNose) * Smooth(-.12f, .29f, roughness);
            cover = edge * (Mathf.Clamp01(wetland) * (1f - Mathf.Clamp01(outcrop)) - Mathf.Clamp01(outcrop));

            float offset = noses - cut - basin + upperBench + lowerBench + 6.4f * rockNose
                         + shelf + fanSurface + confluenceBars + (1.15f - .68f * village) * roughness;
            return edge * offset;
        }
        public float VisualReliefFactor(float x,float z)
        { return Spatial(visualFactor, x, z); }
        static float ComputeVisualReliefFactor(float x, float z, float groundOffset)
        {
            // The fBm and shallow landforms affect display thickness only;
            // Depth, Bed and all derived statistics remain untouched.
            float texture = ReliefNoise(x + 71f, z - 43f);
            float bank = 1f + .08f * PresentationLayout.ShelfInfluence(x, z);
            float depthFactor = Mathf.Pow(PresentationLayout.WidthScale(x), -.48f);
            float islandClearance = 1f - .98f * PresentationLayout.Island(x, z);
            float textureFactor = Mathf.Clamp(1f + .17f * texture + .012f * groundOffset, .8f, 1.2f);
            return textureFactor * bank * depthFactor * islandClearance;
        }
        public float FrontAdvance(float seconds){return Mathf.Max(0,Statistic(seconds,0)-Summary[0,0]);}
        public float RegionVolume(float seconds,float minX,float maxX,bool removed)
        {
            Frame(seconds,out int a,out int b,out float f);double volume=0;
            for(int i=0;i<NX;i++)if(X[i]>=minX && X[i]<=maxX)for(int j=0;j<NZ;j++)
            {int k=i*NZ+j;float h=Mathf.Lerp(Depth[a][k],Depth[b][k],f);volume+=removed?Mathf.Max(0,Depth[0][k]-h):h;}
            return (float)(volume*(X[1]-X[0])*(Z[1]-Z[0]));
        }
        public void Sample(float seconds, float x, float z, out float depth, out Vector2 velocity)
        {
            if (x < MinX || x > MaxX || z < MinZ || z > MaxZ) { depth = 0; velocity = Vector2.zero; return; }
            Frame(seconds, out int a, out int b, out float f);
            depth = Mathf.Lerp(Spatial(Depth[a], x,z), Spatial(Depth[b],x,z),f);
            velocity = new Vector2(Mathf.Lerp(Spatial(UX[a],x,z),Spatial(UX[b],x,z),f),
                                   Mathf.Lerp(Spatial(UZ[a],x,z),Spatial(UZ[b],x,z),f));
        }
        public float Statistic(float seconds, int k)
        { Frame(seconds,out int a,out int b,out float f); return Mathf.Lerp(Summary[a,k],Summary[b,k],f); }
    }
}
