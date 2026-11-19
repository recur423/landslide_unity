using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandslideDemo
{
    /// <summary>
    /// Prominent, deterministic tree response at the numerical flow front.
    /// Imported depth/velocity trigger damage and drive cached advection paths;
    /// uprooting, snapping and burial are illustrative presentation effects.
    /// </summary>
    public sealed class TreeResponse : MonoBehaviour
    {
        const float Step = 0.25f;
        const float FallSeconds = 0.65f;
        const float ReleaseDelay = 0.75f;

        sealed class Tree
        {
            public Vector2 Origin;
            public Transform Whole, Upper, Stump, RootBall, Scar;
            public Quaternion Yaw;
            public Vector3 FlowDirection, TiltAxis, SpinAxis;
            public Vector2[] Path;
            public float[] Travel;
            public float Impact = float.PositiveInfinity;
            public float Scale, CutHeight, Seed;
            public Bounds WholeBounds, UpperBounds;
            public bool Breakable;
            public readonly List<Splinter> Splinters = new List<Splinter>();
        }
        sealed class Splinter
        {
            public Transform Transform;
            public Vector3 Velocity, Spin;
        }

        SimulationData data;
        Transform group;
        int pathCount, affected, broken;
        float firstResponse = float.PositiveInfinity;
        readonly List<Tree> trees = new List<Tree>();
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        Material exposedWood, soil, roots;
        Mesh rootRock;

        public int TreeCount { get { return trees.Count; } }
        public int AffectedCount { get { return affected; } }
        public int BrokenCount { get { return broken; } }
        public float FirstResponseTime { get { return firstResponse; } }

        public void Initialize(SimulationData simulation, Transform parent)
        {
            if (group != null) return;
            data = simulation;
            pathCount = Mathf.CeilToInt(data.TimeMax / Step) + 1;
            group = new GameObject("Key trees | field-triggered uprooting and snapping").transform;
            group.SetParent(parent != null ? parent : transform, false);
            exposedWood = ProceduralAssets.CreateMaterial("Fresh broken tree wood", new Color(.67f,.43f,.19f));
            soil = ProceduralAssets.CreateMaterial("Root ball wet soil", new Color(.22f,.13f,.055f));
            roots = ProceduralAssets.CreateMaterial("Exposed tree roots", new Color(.24f,.12f,.045f));
            rootRock = ProceduralAssets.CreateRockMesh(18721, .48f);
            ownedMeshes.Add(rootRock);
            // Selected within the flow; downstream points avoid the six building
            // footprints. The second half frames trees beside the village.
            Vector2[] positions = {
                new Vector2(94,-12),new Vector2(112,13),new Vector2(136,-15),
                new Vector2(151,15),new Vector2(169,-12),new Vector2(185,13),
                new Vector2(204,-13),new Vector2(220,13),new Vector2(234,-12),
                new Vector2(243,10),new Vector2(245,-12),new Vector2(250,0),
                new Vector2(267,-1),new Vector2(270,14),new Vector2(289,-1),
                new Vector2(291,13),new Vector2(300,0),new Vector2(316,-7),
                new Vector2(322,0),new Vector2(320,4),new Vector2(240,-2),
                new Vector2(280,-1)
            };
            var rng = new System.Random(62419);
            for (int i = 0; i < positions.Length; i++)
            {
                var tree = new Tree {
                    Origin = positions[i], Scale = positions[i].x>=230?Range(rng,.85f,1.12f):Range(rng,1.10f,1.45f),
                    CutHeight = Range(rng,1.05f,1.40f), Breakable = (i % 2) == 1,
                    Seed = Range(rng,0,6.28f), Path = new Vector2[pathCount],
                    Travel = new float[pathCount]
                };
                float baseY = OriginalSurface(tree.Origin);
                GameObject whole = ProceduralAssets.CreateTree(group,
                    PresentationLayout.Point(tree.Origin.x,baseY,tree.Origin.y),tree.Scale,25300+i);
                whole.name = "Key tree " + (i+1) + (tree.Breakable ? " | snap" : " | uproot");
                tree.Whole = whole.transform;
                tree.Yaw = tree.Whole.rotation;
                foreach (var filter in whole.GetComponentsInChildren<MeshFilter>())
                    if (filter.sharedMesh != null) ownedMeshes.Add(filter.sharedMesh);
                tree.WholeBounds = AssemblyBounds(tree.Whole);
                FindImpact(tree);
                firstResponse = Mathf.Min(firstResponse,tree.Impact);
                tree.TiltAxis = Vector3.Cross(Vector3.up,tree.FlowDirection).normalized;
                tree.SpinAxis = (tree.FlowDirection + Vector3.up * .08f).normalized;
                if (tree.Breakable) BuildBrokenAssembly(tree,rng);
                else BuildRootBall(tree,rng);
                BuildPath(tree);
                trees.Add(tree);
            }
            SetState(0,false,true);
            Debug.Log("TREE_RESPONSE_READY count="+TreeCount+" firstResponse="+FirstResponseTime.ToString("F2")+"s");
        }

        float OriginalSurface(Vector2 p)
        { return data.DisplayBedHeight(p.x,p.y); }

        void FindImpact(Tree tree)
        {
            tree.FlowDirection = Vector3.right;
            for (int i=0;i<pathCount;i++)
            {
                float t=Mathf.Min(i*Step,data.TimeMax);
                data.Sample(t,tree.Origin.x,tree.Origin.y,out float depth,out Vector2 velocity);
                if(depth>.10f && velocity.magnitude>.60f)
                {
                    tree.Impact=t;
                    Vector3 from = PresentationLayout.Point(tree.Origin.x, 0f, tree.Origin.y);
                    Vector3 to = PresentationLayout.Point(tree.Origin.x + velocity.x, 0f, tree.Origin.y + velocity.y);
                    tree.FlowDirection=(to-from).normalized;
                    return;
                }
            }
        }

        void BuildBrokenAssembly(Tree tree,System.Random rng)
        {
            tree.Upper=new GameObject("Separated upper trunk and crown").transform;
            tree.Upper.SetParent(group,false);tree.Upper.localScale=Vector3.one*tree.Scale;
            foreach(var source in tree.Whole.GetComponentsInChildren<MeshFilter>())
            {
                Mesh clipped=ClipAbove(source.sharedMesh,tree.CutHeight);
                if(clipped==null)continue;
                ownedMeshes.Add(clipped);
                var part=new GameObject(source.name+" | upper");part.transform.SetParent(tree.Upper,false);
                part.AddComponent<MeshFilter>().sharedMesh=clipped;
                part.AddComponent<MeshRenderer>().sharedMaterials=source.GetComponent<MeshRenderer>().sharedMaterials;
            }
            tree.UpperBounds=AssemblyBounds(tree.Upper);
            tree.Stump=new GameObject("Fixed snapped trunk stump").transform;
            tree.Stump.SetParent(group,false);
            tree.Stump.localScale=Vector3.one*tree.Scale;
            AddMesh(tree.Stump,"Jagged trunk stump",Cylinder(tree.CutHeight,.25f,.20f,9,true),roots);
            Transform cap=AddMesh(tree.Stump,"Fresh snapped wood",Cylinder(.065f,.205f,.185f,9,true),exposedWood);
            cap.localPosition=Vector3.up*tree.CutHeight;
            Transform upperCap=AddMesh(tree.Upper,"Exposed upper cut",Cylinder(.06f,.20f,.18f,9,false),exposedWood);
            upperCap.localPosition=Vector3.up*.01f;
            for(int i=0;i<4;i++)
            {
                var splinter=new Splinter();
                splinter.Transform=AddMesh(group,"Snapping wood splinter",Cylinder(1,.07f,.025f,4,true),exposedWood);
                splinter.Transform.localScale=new Vector3(1,Range(rng,.5f,1.1f),1);
                splinter.Velocity=tree.FlowDirection*Range(rng,1.8f,3.8f)+Vector3.up*Range(rng,2.1f,4.1f)
                    +tree.TiltAxis*Range(rng,-1.5f,1.5f);
                splinter.Spin=new Vector3(Range(rng,50,120),Range(rng,40,140),Range(rng,60,180));
                tree.Splinters.Add(splinter);
            }
        }

        void BuildRootBall(Tree tree,System.Random rng)
        {
            tree.RootBall=new GameObject("Exposed soil root ball").transform;
            tree.RootBall.SetParent(tree.Whole,false);
            var rock=new GameObject("Torn earth around roots");rock.transform.SetParent(tree.RootBall,false);
            rock.transform.localPosition=new Vector3(0,-.32f,0);rock.transform.localScale=new Vector3(1.65f,.95f,1.55f);
            rock.AddComponent<MeshFilter>().sharedMesh=rootRock;rock.AddComponent<MeshRenderer>().sharedMaterial=soil;
            for(int i=0;i<6;i++)
            {
                float angle=i*Mathf.PI/3+Range(rng,-.15f,.15f);
                Vector3 end=new Vector3(Mathf.Cos(angle)*Range(rng,.8f,1.35f),Range(rng,-.55f,-.25f),
                    Mathf.Sin(angle)*Range(rng,.8f,1.35f));
                Transform root=AddMesh(tree.RootBall,"Exposed torn root",Cylinder(end.magnitude,.095f,.025f,5,false),roots);
                root.localRotation=Quaternion.FromToRotation(Vector3.up,end);
            }
            tree.Scar=new GameObject("Root socket left on slope").transform;tree.Scar.SetParent(group,false);
            var pit=new GameObject("Dark uprooting scar");pit.transform.SetParent(tree.Scar,false);
            pit.transform.localScale=new Vector3(tree.Scale*1.45f,.16f,tree.Scale*1.3f);
            pit.AddComponent<MeshFilter>().sharedMesh=rootRock;pit.AddComponent<MeshRenderer>().sharedMaterial=soil;
        }

        void BuildPath(Tree tree)
        {
            Vector2 p=tree.Origin;
            if(tree.Breakable)
                p+=new Vector2(tree.FlowDirection.x,tree.FlowDirection.z)*
                    (tree.CutHeight*tree.Scale*Mathf.Sin(82*Mathf.Deg2Rad));
            float travelled=0;
            for(int i=0;i<pathCount;i++)
            {
                float t=Mathf.Min(i*Step,data.TimeMax);
                tree.Path[i]=p;tree.Travel[i]=travelled;
                if(t<tree.Impact+ReleaseDelay || i+1==pathCount)continue;
                data.Sample(t,p.x,p.y,out float h,out Vector2 v);
                Vector2 mid=p+v*(Step*.5f);
                data.Sample(Mathf.Min(t+Step*.5f,data.TimeMax),mid.x,mid.y,out float hm,out Vector2 vm);
                if(hm<.015f)vm*=Mathf.Clamp01(hm/.015f);
                Vector2 next=p+vm*Step;
                next.x=Mathf.Clamp(next.x,data.MinX,data.MaxX);next.y=Mathf.Clamp(next.y,data.MinZ,data.MaxZ);
                travelled+=Vector2.Distance(p,next);p=next;
            }
        }

        public void SetState(float t,bool failed,bool visible)
        {
            if(group==null || data==null)return;
            if(group.gameObject.activeSelf!=visible)group.gameObject.SetActive(visible);
            t=Mathf.Clamp(t,0,data.TimeMax);
            affected=0;broken=0;
            foreach(Tree tree in trees)
            {
                if(failed && t>=tree.Impact)affected++;
                if(failed && tree.Breakable && t>=tree.Impact+ReleaseDelay)broken++;
            }
            if(!visible)return;
            float frame=Mathf.Clamp(t/Step,0,pathCount-1);int a=Mathf.FloorToInt(frame),b=Mathf.Min(a+1,pathCount-1);
            float blend=frame-a;
            foreach(Tree tree in trees)
            {
                float ground=OriginalSurface(tree.Origin);
                float age=failed?t-tree.Impact:-1;
                bool responded=failed && age>=0;
                bool snapped=responded && tree.Breakable && age>=ReleaseDelay;
                // Hidden alternate assemblies also receive a canonical pose so
                // full-hierarchy snapshots are independent of seek history.
                tree.Whole.position=PresentationLayout.Point(tree.Origin.x,ground,tree.Origin.y);
                tree.Whole.rotation=tree.Yaw;
                tree.Whole.gameObject.SetActive(!snapped);
                if(tree.Upper!=null)
                {
                    tree.Upper.gameObject.SetActive(snapped);
                    tree.Upper.position=PresentationLayout.Point(tree.Origin.x,ground+tree.CutHeight*tree.Scale,tree.Origin.y);
                    tree.Upper.rotation=tree.Yaw;
                }
                if(tree.Stump!=null)
                {
                    tree.Stump.gameObject.SetActive(snapped);
                    tree.Stump.position=PresentationLayout.Point(tree.Origin.x,ground,tree.Origin.y);
                    tree.Stump.rotation=tree.Yaw;
                }
                if(tree.RootBall!=null)tree.RootBall.gameObject.SetActive(responded && age>.16f);
                if(tree.Scar!=null)
                {
                    tree.Scar.gameObject.SetActive(responded && age>ReleaseDelay);
                    tree.Scar.position=PresentationLayout.Point(tree.Origin.x,data.DisplayBedHeight(tree.Origin.x,tree.Origin.y)+.08f,tree.Origin.y);
                }
                if(!responded)
                {
                    tree.Whole.position=PresentationLayout.Point(tree.Origin.x,ground,tree.Origin.y);
                    tree.Whole.rotation=tree.Yaw;
                    UpdateSplinters(tree,-1,ground);continue;
                }
                float tilt=Mathf.SmoothStep(0,1,age/FallSeconds)*(tree.Breakable?82:87);
                Quaternion rotation=Quaternion.AngleAxis(tilt,tree.TiltAxis)*tree.Yaw;
                if(age<ReleaseDelay)
                {
                    float lift=tree.Breakable?0:Mathf.SmoothStep(0,.65f,age/FallSeconds);
                    tree.Whole.position=PresentationLayout.Point(tree.Origin.x,ground+lift,tree.Origin.y);
                    tree.Whole.rotation=rotation;
                }
                else
                {
                    Vector2 p=Vector2.Lerp(tree.Path[a],tree.Path[b],blend);
                    float travel=Mathf.Lerp(tree.Travel[a],tree.Travel[b],blend);
                    float after=age-ReleaseDelay;
                    float roll=travel*(tree.Breakable?2.2f:1.35f)/tree.Scale;
                    rotation=Quaternion.AngleAxis(roll,tree.SpinAxis)*rotation;
                    Bounds bounds=snapped?tree.UpperBounds:tree.WholeBounds;
                    float support=SupportHeight(bounds,rotation,tree.Scale);
                    data.Sample(t,p.x,p.y,out float depth,out Vector2 velocity);
                    float burial=Mathf.SmoothStep(0,1,(p.x-250)/72)*Mathf.Clamp01((after-3)/5)*
                        Mathf.Clamp01(depth/.8f)*(1-Mathf.Clamp01(velocity.magnitude/6))*1.75f;
                    float rest=data.SurfaceHeight(t,p.x,p.y)+support-burial+.04f;
                    float startY=ground+(snapped?tree.CutHeight*tree.Scale*Mathf.Cos(82*Mathf.Deg2Rad):.65f);
                    float y=Mathf.Lerp(startY,rest,Mathf.SmoothStep(0,1,after/.85f));
                    y+=Mathf.Sin(Mathf.Clamp01(after/1.2f)*Mathf.PI)*.65f;
                    Transform moving=snapped?tree.Upper:tree.Whole;
                    moving.position=PresentationLayout.Point(p.x,y,p.y);moving.rotation=rotation;
                }
                UpdateSplinters(tree,age-ReleaseDelay,ground);
            }
        }

        void UpdateSplinters(Tree tree,float age,float ground)
        {
            foreach(Splinter splinter in tree.Splinters)
            {
                bool show=age>=0 && age<1.6f;splinter.Transform.gameObject.SetActive(show);
                float poseAge=Mathf.Clamp(age,0,1.6f);
                Vector3 start=PresentationLayout.Point(tree.Origin.x,ground+tree.CutHeight*tree.Scale,tree.Origin.y);
                Vector3 p=start+splinter.Velocity*poseAge+Vector3.down*(4.905f*poseAge*poseAge);
                p.y=Mathf.Max(p.y,data.DisplayBedHeight(p.x,PresentationLayout.SolverZ(p.x,p.z))+.1f);
                splinter.Transform.position=p;splinter.Transform.rotation=Quaternion.Euler(splinter.Spin*poseAge);
            }
        }

        /// <summary>Runtime gate for response, pre-transport tilt, seek and rewind.</summary>
        public bool ValidateTimeline(out string result)
        {
            if(data==null || group==null){result="Tree response not initialized";return false;}
            int expectedBroken=0;
            foreach(Tree tree in trees)
            {
                if(tree.Breakable)expectedBroken++;
                if(float.IsInfinity(tree.Impact))
                {result="A targeted tree is outside the computed flow";SetState(0,false,true);return false;}
                SetState(tree.Impact+.60f,true,true);
                if(Vector3.Angle(tree.Whole.up,Vector3.up)<60 ||
                    Vector2.Distance(new Vector2(tree.Whole.position.x,tree.Whole.position.z),
                        new Vector2(tree.Origin.x,PresentationLayout.WorldZ(tree.Origin.x,tree.Origin.y)))>.001f)
                {result="A tree failed to tilt before transport";SetState(0,false,true);return false;}
            }
            SetState(data.TimeMax,true,true);
            if(AffectedCount!=TreeCount || BrokenCount!=expectedBroken)
            {result="Final tree damage count mismatch";SetState(0,false,true);return false;}
            const float probe=14.25f;
            SetState(probe,true,true);
            Transform[] nodes=group.GetComponentsInChildren<Transform>(true);
            var positions=new Vector3[nodes.Length];var rotations=new Quaternion[nodes.Length];var active=new bool[nodes.Length];
            for(int i=0;i<nodes.Length;i++)
            {positions[i]=nodes[i].position;rotations[i]=nodes[i].rotation;active[i]=nodes[i].gameObject.activeSelf;}
            SetState(data.TimeMax,true,true);SetState(0,false,true);SetState(probe,true,true);
            for(int i=0;i<nodes.Length;i++)
            {
                if(Vector3.Distance(positions[i],nodes[i].position)>.001f ||
                    Quaternion.Angle(rotations[i],nodes[i].rotation)>.01f || active[i]!=nodes[i].gameObject.activeSelf)
                {result="Tree hierarchy is not seek deterministic: "+nodes[i].name;SetState(0,false,true);return false;}
            }
            SetState(0,false,true);
            if(AffectedCount!=0 || BrokenCount!=0)
            {result="Rewind did not restore standing trees";return false;}
            result="trees="+TreeCount+" finalAffected="+TreeCount+" finalBroken="+expectedBroken+
                " firstResponse="+FirstResponseTime.ToString("F2")+"s tiltBeforeTransport=true seekAndRewind=true";
            return true;
        }

        static float SupportHeight(Bounds bounds,Quaternion rotation,float scale)
        {
            float min=float.PositiveInfinity;Vector3 low=bounds.min,high=bounds.max;
            for(int i=0;i<8;i++)
            {
                Vector3 p=new Vector3((i&1)==0?low.x:high.x,(i&2)==0?low.y:high.y,(i&4)==0?low.z:high.z);
                min=Mathf.Min(min,(rotation*(p*scale)).y);
            }
            return Mathf.Max(0,-min);
        }
        static Bounds AssemblyBounds(Transform root)
        {
            Bounds result=new Bounds(Vector3.zero,Vector3.zero);bool first=true;
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Bounds bounds=filter.sharedMesh.bounds;
                if(first){result=bounds;first=false;}else{result.Encapsulate(bounds.min);result.Encapsulate(bounds.max);}
            }
            return result;
        }
        Transform AddMesh(Transform parent,string name,Mesh mesh,Material material)
        {
            ownedMeshes.Add(mesh);var obj=new GameObject(name);obj.transform.SetParent(parent,false);
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=material;
            return obj.transform;
        }
        static float Range(System.Random rng,float low,float high)
        {return low+(high-low)*(float)rng.NextDouble();}

        static Mesh ClipAbove(Mesh source,float cut)
        {
            var verts=new List<Vector3>();var uvs=new List<Vector2>();var indices=new List<int>();
            Vector3[] input=source.vertices;Vector2[] inputUV=source.uv;int[] triangles=source.triangles;
            bool hasUV=inputUV.Length==input.Length;
            for(int i=0;i<triangles.Length;i+=3)
            {
                var polygon=new List<Vector3>(4);var polygonUV=new List<Vector2>(4);
                int previousIndex=triangles[i+2];Vector3 previous=input[previousIndex];
                Vector2 previousUV=hasUV?inputUV[previousIndex]:Vector2.zero;bool previousInside=previous.y>=cut;
                for(int j=0;j<3;j++)
                {
                    int currentIndex=triangles[i+j];Vector3 current=input[currentIndex];
                    Vector2 currentUV=hasUV?inputUV[currentIndex]:Vector2.zero;bool inside=current.y>=cut;
                    if(inside!=previousInside)
                    {
                        float f=(cut-previous.y)/(current.y-previous.y);
                        polygon.Add(Vector3.LerpUnclamped(previous,current,f));
                        polygonUV.Add(Vector2.LerpUnclamped(previousUV,currentUV,f));
                    }
                    if(inside){polygon.Add(current);polygonUV.Add(currentUV);}
                    previous=current;previousUV=currentUV;previousInside=inside;
                }
                if(polygon.Count<3)continue;
                int start=verts.Count;for(int j=0;j<polygon.Count;j++){verts.Add(polygon[j]-Vector3.up*cut);uvs.Add(polygonUV[j]);}
                for(int j=1;j<polygon.Count-1;j++){indices.Add(start);indices.Add(start+j);indices.Add(start+j+1);}
            }
            if(verts.Count==0)return null;
            var mesh=new Mesh{name=source.name+"_snapped_upper"};mesh.SetVertices(verts);mesh.SetTriangles(indices,0);
            mesh.SetUVs(0,uvs);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();return mesh;
        }
        static Mesh Cylinder(float height,float baseRadius,float topRadius,int sides,bool jagged)
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int i=0;i<sides;i++)
            {
                float angle=i*Mathf.PI*2/sides;Vector3 radial=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                vertices.Add(radial*baseRadius);
                vertices.Add(radial*topRadius+Vector3.up*(height+(jagged?(i%2==0?.10f:-.04f):0)));
            }
            int bottom=vertices.Count;vertices.Add(Vector3.zero);int top=vertices.Count;vertices.Add(Vector3.up*height);
            for(int i=0;i<sides;i++)
            {
                int a=i*2,b=((i+1)%sides)*2;
                triangles.AddRange(new[]{a,b,a+1,b,b+1,a+1,bottom,b,a,top,a+1,b+1});
            }
            var mesh=new Mesh{name="Tree tapered wood/root segment"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        void OnDestroy()
        {
            foreach(Mesh mesh in ownedMeshes)if(mesh!=null)Destroy(mesh);
            if(exposedWood!=null)Destroy(exposedWood);if(soil!=null)Destroy(soil);if(roots!=null)Destroy(roots);
        }
    }
}
