using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace LandslideDemo
{
    public sealed class WatershedDetails : MonoBehaviour
    {
        readonly System.Collections.Generic.List<Mesh> meshes = new System.Collections.Generic.List<Mesh>();
        Material water, track;

        public void Initialize(SimulationData data)
        {
            Shader shader = Resources.Load<Shader>("Shaders/WatershedDetails");
            if (shader == null) return;
            water = new Material(shader) { name = "Rainwater in side hollows" };
            water.SetFloat("_Water", 1);
            track = new Material(shader) { name = "Compacted village track" };
            AddRibbon(data, "Winding village track", 271, 418, 122, 2.35f, 0.11f, track,
                x => 26f + 7.5f * Mathf.Sin((x-272f)*.044f) + 2.2f * Mathf.Sin(x*.092f));
            AddRibbon(data, "Runoff at side tributary", 143, 192, 59, 2.7f, 0.095f, water,
                x => 35f - (x-143f)*.36f + 1.8f*Mathf.Sin(x*.15f));
            AddRibbon(data, "Turbid toe drainage", 331, 400, 66, 4.2f, 0.080f, water,
                x => -18f - 4.5f*Mathf.Sin((x-331f)*.052f));
            AddRibbon(data, "Upland wet hollow", 50, 94, 49, 2.5f, 0.075f, water,
                x => -37f + (x-50f)*.19f + 1.6f*Mathf.Sin(x*.12f));
        }

        void AddRibbon(SimulationData data, string name, float from, float to, int count,
            float width, float lift, Material material, Func<float,float> center)
        {
            const int across = 7;
            var points = new Vector3[count*across];
            var uv = new Vector2[points.Length];
            var triangles = new int[(count-1)*(across-1)*6];
            for (int i=0; i<count; i++)
            {
                float u = i/(count-1f);
                float x = Mathf.Lerp(from,to,u);
                float z = center(x);
                float profile = .66f+.34f*Mathf.Sin(Mathf.PI*u);
                for (int j=0; j<across; j++)
                {
                    int k = i*across+j;
                    float v = j/(across-1f);
                    float worldZ = PresentationLayout.WorldZ(x,z) + (v-.5f)*width*profile;
                    float solverZ = PresentationLayout.SolverZ(x,worldZ);
                    points[k] = new Vector3(x,data.DisplayBedHeight(x,solverZ)+lift,worldZ);
                    uv[k] = new Vector2(u,v);
                    if (i==count-1 || j==across-1) continue;
                    int t=(i*(across-1)+j)*6;
                    triangles[t]=k; triangles[t+1]=k+1; triangles[t+2]=k+across;
                    triangles[t+3]=k+1; triangles[t+4]=k+across+1; triangles[t+5]=k+across;
                }
            }
            var mesh = new Mesh { name = name, vertices = points, uv = uv, triangles = triangles };
            meshes.Add(mesh);
            var child = new GameObject(name);
            child.transform.SetParent(transform,false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer=child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
        }

        void OnDestroy()
        {
            foreach (Mesh mesh in meshes) Destroy(mesh);
            if (water != null) Destroy(water);
            if (track != null) Destroy(track);
        }
    }
}
