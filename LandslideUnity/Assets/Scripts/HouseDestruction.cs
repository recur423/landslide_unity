using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch=System.Diagnostics.Stopwatch;

namespace LandslideDemo
{
    /// <summary>Field-driven, seekable staged damage. Capacity constants are
    /// illustrative. Kinematic large debris keeps colliders; tiny parts have none.
    /// Replay poses do not depend on PhysX integration or frame order.</summary>
    public sealed class HouseDestruction:MonoBehaviour
    {
        const float Step=.25f,ContactH=.08f,ContactV=1.2f,PartialRatio=.65f,TotalRatio=1.35f;
        enum Kind{SideWall,UpstreamWall,DownstreamWall,Roof,Gable,Window,Door}
        sealed class House
        {
            public Vector2 Center;public float BaseY,Capacity,Peak,HitH,HitV;
            public float DisplayShift;
            public float Impact=float.PositiveInfinity,PartialAt=float.PositiveInfinity,TotalAt=float.PositiveInfinity;
            public int FinalClass;public float[] PeakHistory,DepthHistory;
            public readonly List<Fragment> Parts=new List<Fragment>();
            public readonly List<FixedPiece> Foundations=new List<FixedPiece>();
            public readonly List<Transform> Mud=new List<Transform>();public readonly List<Puff> Dust=new List<Puff>();
        }
        sealed class FixedPiece{public Transform Transform;public Vector3 Start;}
        sealed class Fragment
        {
            public Transform Transform;public Vector3 Local,Start,Scale,ReleasePosition,SpinAxis,LastPosition;
            public Quaternion Rotation,ReleaseRotation,LastRotation;public Kind Kind;
            public bool Remnant,Detached,MayUseCCD,PoseSet;
            public float Release=float.PositiveInfinity,SettleAt,SpinRate,Hop;
            public Vector2[] Path;public float[] Distance,Speed;
            public Rigidbody Body;public BoxCollider Collider;
        }
        sealed class Puff
        {
            public Transform Transform;public MeshRenderer Renderer;public MaterialPropertyBlock Block;
            public Vector2 Offset;public float Delay,Life,Size;
        }
        SimulationData data;readonly List<House> houses=new List<House>();
        readonly List<Material> materials=new List<Material>();readonly List<BoxCollider> colliders=new List<BoxCollider>();
        Mesh cube,gable,dustQuad,mound;Texture2D dustTexture;Material dustMaterial;Transform root;
        LineRenderer ring;Camera view;int samples,selected,damaged,light,partial,total,sleeping,ccd;
        float firstImpact=float.PositiveInfinity,updateMS;int inspectionKey=int.MinValue;string inspection;
        readonly StringBuilder text=new StringBuilder(640);
        public int HouseCount{get{return houses.Count;}}
        // Compatibility: reached/damaged houses, not only total collapses.
        public int DestroyedCount{get{return damaged;}}
        public int LightDamageCount{get{return light;}}public int PartialCollapseCount{get{return partial;}}
        public int TotalCollapseCount{get{return total;}}
        // Enabled retained colliders, including stable large rubble.
        public int LargeColliderCount{get{return colliders.Count;}}
        public int SleepingDebrisCount{get{return sleeping;}}public int ActiveCCDCount{get{return ccd;}}
        public int SelectedIndex{get{return selected;}}public float FirstImpactTime{get{return firstImpact;}}
        public float LastUpdateMilliseconds{get{return updateMS;}}
        public Vector3 FocusPoint{get{return data==null?new Vector3(282,55,0):PresentationLayout.Point(282,data.DisplayBedHeight(282,0)+5,0);}}
        static Vector3 HousePoint(House house,Vector3 point)
        { point.z+=house.DisplayShift;return point; }

        public void Initialize(SimulationData simulation,Shader particleShader)
        {
            if(root!=null)return;data=simulation;samples=Mathf.CeilToInt(data.TimeMax/Step)+1;
            root=new GameObject("Numbered village | staged damage").transform;root.SetParent(transform,false);
            var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);cube=primitive.GetComponent<MeshFilter>().sharedMesh;Destroy(primitive);
            gable=GableMesh();mound=ProceduralAssets.CreateRockMesh(8672,.17f);MakeDustMaterial(particleShader);
            var rng=new System.Random(48319);
            Material[] plaster={Mat("Warm plaster",new Color(.78f,.70f,.52f)),Mat("Whitewashed plaster",new Color(.80f,.79f,.68f)),Mat("Ochre plaster",new Color(.69f,.56f,.36f))};
            Material red=Mat("Terracotta roof",new Color(.43f,.13f,.075f)),slate=Mat("Slate roof",new Color(.20f,.25f,.27f));
            Material timber=Mat("Timber frames",new Color(.20f,.115f,.045f)),glass=Mat("Blue glazing",new Color(.12f,.27f,.34f));
            Material concrete=Mat("Foundations",new Color(.43f,.42f,.38f)),mud=Mat("Deposited mud at house corners",new Color(.29f,.14f,.055f));
            Vector2[] positions={new Vector2(255,-12),new Vector2(260,12),new Vector2(278,-14),new Vector2(282,12),new Vector2(304,-12),new Vector2(312,12)};
            // Fixed illustrative capacities: C uses I=h*|v|^2 units (m^3/s^2).
            // Different C values depict different building conditions, not tested strengths.
            float[] capacities={90,95,80,100,60,45};
            for(int i=0;i<positions.Length;i++)
            {
                var h=new House{Center=positions[i],Capacity=capacities[i],PeakHistory=new float[samples],DepthHistory=new float[samples]};
                h.DisplayShift=PresentationLayout.WorldZ(h.Center.x,h.Center.y)-h.Center.y;
                h.BaseY=FoundationTop(h);Analyse(h);firstImpact=Mathf.Min(firstImpact,h.Impact);
                Transform parent=new GameObject("House "+(i+1).ToString("D2")+" | "+ClassName(h.FinalClass)).transform;parent.SetParent(root,false);
                // A thin inset plinth keeps the base legible in close shots without
                // the oversized brown rectangle that floated around each house.
                Fixed(h,parent,"Inset foundation plinth",concrete,new Vector3(h.Center.x,h.BaseY-.12f,h.Center.y),new Vector3(8.04f,.24f,6.04f));
                for(int sx=-1;sx<=1;sx+=2)for(int sz=-1;sz<=1;sz+=2)
                {
                    float px=h.Center.x+sx*3.6f,pz=h.Center.y+sz*2.6f,ground=HouseGround(h,px,pz)-.08f;
                    float height=Mathf.Max(.25f,h.BaseY-.58f-ground);
                    Fixed(h,parent,"Support pier",concrete,new Vector3(px,ground+height*.5f,pz),new Vector3(.65f,height,.65f));
                    var cap=new GameObject("Mud burying foundation corner");cap.transform.SetParent(parent,false);
                    cap.AddComponent<MeshFilter>().sharedMesh=mound;cap.AddComponent<MeshRenderer>().sharedMaterial=mud;
                    cap.transform.position=HousePoint(h,new Vector3(px,ground+.08f,pz));cap.SetActive(false);h.Mud.Add(cap.transform);
                }
                BuildHouse(h,parent,plaster[i%3],i%3==1?slate:red,timber,glass,rng);BuildDust(h,parent,rng);houses.Add(h);
                Debug.Log("HOUSE_DEMAND house="+(i+1)+" contact="+h.Impact.ToString("F2")+"s h="+h.HitH.ToString("F3")+
                    "m v="+h.HitV.ToString("F2")+"m/s peakI="+h.Peak.ToString("F2")+" C="+h.Capacity+
                    " ratio="+(h.Peak/h.Capacity).ToString("F2")+" final="+ClassName(h.FinalClass));
            }
            BuildRing(particleShader);SelectHouse(0);SetState(0,false,true);
        }
        Material Mat(string name,Color color){Material m=ProceduralAssets.CreateMaterial(name,color);materials.Add(m);return m;}
        float HouseGround(House house,float x,float z)
        {
            return data.DisplayBedHeight(x,PresentationLayout.SolverZ(x,z+house.DisplayShift));
        }
        float FoundationTop(House house)
        {
            Vector2 p=house.Center;
            float top=float.NegativeInfinity;for(int a=-1;a<=1;a+=2)for(int b=-1;b<=1;b+=2)top=Mathf.Max(top,HouseGround(house,p.x+a*4.3f,p.y+b*3.3f));return top+.12f;
        }
        void Analyse(House h)
        {
            float peak=0,maxH=0;int slot=0;
            for(int tick=0;tick<=Mathf.CeilToInt(data.TimeMax/.125f);tick++)
            {
                float time=Mathf.Min(data.TimeMax,tick*.125f);
                for(int s=0;s<5;s++)
                {
                    float x=h.Center.x+(s==4?0:-4.35f),z=h.Center.y+(s==4?0:-3+s*2);
                    data.Sample(time,x,z,out float depth,out Vector2 velocity);float speed=velocity.magnitude,index=depth*speed*speed;
                    peak=Mathf.Max(peak,index);maxH=Mathf.Max(maxH,depth);
                    if(float.IsPositiveInfinity(h.Impact)&&depth>ContactH&&speed>ContactV){h.Impact=time;h.HitH=depth;h.HitV=speed;}
                    if(float.IsPositiveInfinity(h.PartialAt)&&index>=h.Capacity*PartialRatio)h.PartialAt=time;
                    if(float.IsPositiveInfinity(h.TotalAt)&&index>=h.Capacity*TotalRatio)h.TotalAt=time;
                }
                while(slot<samples&&slot*Step<=time+.0001f){h.PeakHistory[slot]=peak;h.DepthHistory[slot]=maxH;slot++;}
            }
            while(slot<samples){h.PeakHistory[slot]=peak;h.DepthHistory[slot]=maxH;slot++;}
            h.Peak=peak;float ratio=peak/h.Capacity;h.FinalClass=ratio>=TotalRatio?2:(ratio>=PartialRatio?1:0);
        }
        void BuildHouse(House h,Transform parent,Material wall,Material roof,Material timber,Material glass,System.Random rng)
        {
            for(int side=-1;side<=1;side+=2)
            {
                for(int bay=0;bay<4;bay++)for(int row=0;row<2;row++)
                    Part(h,parent,"Side wall panel",wall,Kind.SideWall,new Vector3(-3+bay*2,1.2f+row*2.4f,side*3),new Vector3(1.97f,2.36f,.28f),Quaternion.identity,row,bay,rng);
                for(int bay=0;bay<3;bay++)for(int row=0;row<2;row++)
                    Part(h,parent,side<0?"Upstream impact wall":"Downstream wall",wall,side<0?Kind.UpstreamWall:Kind.DownstreamWall,
                        new Vector3(side*4,1.2f+row*2.4f,-2+bay*2),new Vector3(.28f,2.36f,1.97f),Quaternion.identity,row,bay,rng);
                for(int bay=0;bay<3;bay++)
                {
                    float x=-2.7f+bay*2.7f;
                    Part(h,parent,"Timber window frame",timber,Kind.Window,new Vector3(x,3.05f,side*3.18f),new Vector3(1.3f,1.45f,.13f),Quaternion.identity,0,bay,rng);
                    Part(h,parent,"Glazing fragment",glass,Kind.Window,new Vector3(x,3.05f,side*3.26f),new Vector3(1.08f,1.22f,.05f),Quaternion.identity,0,bay,rng);
                }
            }
            Part(h,parent,"Timber door",timber,Kind.Door,new Vector3(.7f,1.08f,-3.19f),new Vector3(1.35f,2.16f,.16f),Quaternion.identity,0,0,rng);
            const float angle=25;float width=3.45f,rise=Mathf.Tan(angle*Mathf.Deg2Rad)*width,length=width/Mathf.Cos(angle*Mathf.Deg2Rad);
            for(int side=-1;side<=1;side+=2)for(int i=0;i<4;i++)for(int j=0;j<2;j++)
            {
                float z=side*(j+.5f)*width/2,y=4.8f+rise-Mathf.Abs(z)*Mathf.Tan(angle*Mathf.Deg2Rad);
                Part(h,parent,"Roof slab after support loss",roof,Kind.Roof,new Vector3(-3.45f+i*2.3f,y,z),new Vector3(2.27f,.19f,length/2-.025f),Quaternion.Euler(side*angle,0,0),j,i,rng);
            }
            for(int side=-1;side<=1;side+=2)Part(h,parent,"Gable wall",wall,Kind.Gable,new Vector3(side*4,4.8f+rise*.5f,0),new Vector3(.28f,rise,6),Quaternion.identity,0,0,rng,gable);
        }
        void Fixed(House h,Transform parent,string name,Material mat,Vector3 pos,Vector3 scale)
        {
            var obj=new GameObject(name);obj.transform.SetParent(parent,false);obj.transform.position=pos;obj.transform.localScale=scale;
            obj.AddComponent<MeshFilter>().sharedMesh=cube;obj.AddComponent<MeshRenderer>().sharedMaterial=mat;h.Foundations.Add(new FixedPiece{Transform=obj.transform,Start=pos});
        }
        void Part(House h,Transform parent,string name,Material mat,Kind kind,Vector3 local,Vector3 scale,Quaternion rot,int row,int bay,System.Random rng,Mesh mesh=null)
        {
            var obj=new GameObject(name);obj.transform.SetParent(parent,false);Vector3 start=new Vector3(h.Center.x+local.x,h.BaseY+local.y,h.Center.y+local.z);
            obj.transform.position=start;obj.transform.localScale=scale;obj.transform.rotation=rot;
            obj.AddComponent<MeshFilter>().sharedMesh=mesh==null?cube:mesh;obj.AddComponent<MeshRenderer>().sharedMaterial=mat;
            var p=new Fragment{Transform=obj.transform,Local=local,Start=start,Scale=scale,Rotation=rot,Kind=kind,
                SpinAxis=new Vector3(Range(rng,-.2f,.2f),Range(rng,-.12f,.12f),1).normalized,SpinRate=Range(rng,8,17),Hop=Range(rng,.10f,.36f)};
            bool major=kind==Kind.SideWall||kind==Kind.UpstreamWall||kind==Kind.DownstreamWall||kind==Kind.Roof||kind==Kind.Gable;
            if(major)
            {
                p.Collider=obj.AddComponent<BoxCollider>();p.Collider.size=Vector3.one;p.Body=obj.AddComponent<Rigidbody>();
                p.Body.isKinematic=true;p.Body.useGravity=false;p.Body.interpolation=RigidbodyInterpolation.None;p.Body.collisionDetectionMode=CollisionDetectionMode.Discrete;
                colliders.Add(p.Collider);p.MayUseCCD=kind==Kind.Roof&&(bay==0||bay==3)&&row==0;
            }
            if(h.FinalClass==0)p.Detached=(kind==Kind.UpstreamWall&&row==1&&bay==1)||(kind==Kind.Window&&local.x< -2&&local.z<0)||(kind==Kind.Roof&&bay==0&&row==0&&local.z<0);
            else if(h.FinalClass==1)p.Detached=kind==Kind.UpstreamWall||((kind==Kind.SideWall||kind==Kind.Roof||kind==Kind.Window||kind==Kind.Gable)&&local.x<0);
            else{p.Remnant=(kind==Kind.DownstreamWall&&row==0)||(kind==Kind.SideWall&&row==0&&local.x>2);p.Detached=!p.Remnant;}
            if(p.Detached)
            {
                if(kind==Kind.UpstreamWall||kind==Kind.Window)p.Release=h.Impact+Range(rng,.35f,1.05f);
                else if(kind==Kind.Roof)p.Release=Mathf.Max(h.Impact+Range(rng,2.35f,3.15f),h.FinalClass==2?h.TotalAt+.25f:(h.FinalClass==1?h.PartialAt+.4f:h.Impact+2.5f));
                else p.Release=Mathf.Max(h.Impact+Range(rng,2.5f,3.35f),h.PartialAt+.15f);
                if(h.FinalClass==0&&kind!=Kind.Roof)p.Release=h.Impact+Range(rng,.5f,1.1f);
                p.SettleAt=Mathf.Min(data.TimeMax,p.Release+Range(rng,8,10));
                AttachedPose(h,p,p.Release,true,out p.ReleasePosition,out p.ReleaseRotation,out Vector3 unused);
                p.Path=new Vector2[samples];p.Distance=new float[samples];p.Speed=new float[samples];Precompute(p);
            }
            h.Parts.Add(p);
        }
        void Precompute(Fragment p)
        {
            Vector2 pos=new Vector2(p.ReleasePosition.x,p.ReleasePosition.z);float distance=0,dragAt=p.Release+.75f;
            for(int k=0;k<samples;k++)
            {
                float t=Mathf.Min(k*Step,data.TimeMax);p.Path[k]=pos;p.Distance[k]=distance;
                if(t<dragAt||t>=p.SettleAt||k+1==samples)continue;
                data.Sample(t,pos.x,pos.y,out float h,out Vector2 v);Vector2 mid=pos+v*Step*.25f;
                data.Sample(Mathf.Min(data.TimeMax,t+Step*.5f),mid.x,mid.y,out float midH,out Vector2 midV);
                if(midH<.008f)midV*=Mathf.Clamp01(midH/.008f);
                Vector2 next=pos+midV*(Step*Mathf.SmoothStep(0,.54f,(t-dragAt)/1.4f));
                next.x=Mathf.Clamp(next.x,data.MinX,data.MaxX);next.y=Mathf.Clamp(next.y,data.MinZ,data.MaxZ);
                float delta=Vector2.Distance(pos,next);distance+=delta;p.Speed[k]=delta/Step;pos=next;
            }
        }
        float History(float[] values,float time)
        {
            float q=Mathf.Clamp(time/Step,0,samples-1);int a=Mathf.FloorToInt(q),b=Mathf.Min(a+1,samples-1);return Mathf.Lerp(values[a],values[b],q-a);
        }
        float Burial(House h,float time)
        {
            float age=time-h.Impact;if(age<=0)return 0;float deposit=History(h.DepthHistory,time)*data.VisualDepthScale,target=h.FinalClass==2?1.45f:(h.FinalClass==1?.72f:.18f);
            return Mathf.SmoothStep(0,1,(age-2.6f)/3.5f)*Mathf.Min(target,.2f+deposit*.65f);
        }
        float Tilt(House h,float time)
        {
            float age=time-h.Impact;if(age<=.8f)return 0;float max=h.FinalClass==2?19:(h.FinalClass==1?11:3.5f);
            float angle=max*Mathf.SmoothStep(0,1,(age-.8f)/1.4f);if(h.FinalClass>0)angle=Mathf.Lerp(angle,h.FinalClass==2?3.5f:6,Mathf.SmoothStep(0,1,(age-3.7f)/1.8f));return angle;
        }
        void AttachedPose(House h,Fragment p,float time,bool failed,out Vector3 position,out Quaternion rotation,out Vector3 scale)
        {
            float age=failed?time-h.Impact:-1;Quaternion tilt=Quaternion.AngleAxis(failed?-Tilt(h,time):0,Vector3.forward);Vector3 local=p.Local;scale=p.Scale;
            if(p.Remnant&&age>2.8f){float f=Mathf.SmoothStep(0,1,(age-2.8f)/2.1f);scale.y*=Mathf.Lerp(1,.53f,f);local.y-=p.Scale.y*.235f*f;}
            position=new Vector3(h.Center.x,h.BaseY-(failed?Burial(h,time):0),h.Center.y)+tilt*local;rotation=tilt*p.Rotation;
        }
        public void SetState(float slideTime,bool failed,bool effectsEnabled)
        {
            if(data==null)return;long started=Stopwatch.GetTimestamp();damaged=light=partial=total=sleeping=ccd=0;
            float time=Mathf.Clamp(slideTime,0,data.TimeMax);if(view==null)view=Camera.main;
            float q=Mathf.Clamp(time/Step,0,samples-1);int a=Mathf.FloorToInt(q),b=Mathf.Min(a+1,samples-1);float f=q-a;
            foreach(House h in houses)
            {
                bool hit=failed&&time>=h.Impact;int level=CurrentClass(h,time,failed);
                if(hit){damaged++;if(level==0)light++;else if(level==1)partial++;else total++;}
                float burial=hit?Burial(h,time):0;foreach(FixedPiece fixedPiece in h.Foundations)fixedPiece.Transform.position=HousePoint(h,fixedPiece.Start-Vector3.up*burial);
                UpdateMud(h,time,hit);
                foreach(Fragment p in h.Parts)
                {
                    bool released=failed&&p.Detached&&time>=p.Release,stable=false;float speed=0;Vector3 pos,scale;Quaternion rot;
                    if(!released)
                    {
                        AttachedPose(h,p,time,failed,out pos,out rot,out scale);
                        pos=HousePoint(h,pos);
                    }
                    else
                    {
                        float age=time-p.Release;Vector2 xz=Vector2.Lerp(p.Path[a],p.Path[b],f);float travel=Mathf.Lerp(p.Distance[a],p.Distance[b],f);speed=Mathf.Lerp(p.Speed[a],p.Speed[b],f);
                        float drop=Mathf.SmoothStep(0,1,age/1.45f);Quaternion turn=Quaternion.AngleAxis(travel*p.SpinRate+Mathf.Min(age,1.5f)*35,p.SpinAxis)*p.ReleaseRotation;
                        rot=Quaternion.Slerp(p.ReleaseRotation,turn,drop);scale=p.Scale;
                        float support=.5f*(Mathf.Abs((rot*Vector3.right).y)*scale.x+Mathf.Abs((rot*Vector3.up).y)*scale.y+Mathf.Abs((rot*Vector3.forward).y)*scale.z);
                        stable=time>=p.SettleAt;float surface=data.SurfaceHeight(stable?p.SettleAt:time,xz.x,xz.y)+support+.025f;
                        float hop=age<1.5f?Mathf.Sin(Mathf.Clamp01(age/1.5f)*Mathf.PI)*p.Hop:0;pos=new Vector3(xz.x,Mathf.Lerp(p.ReleasePosition.y,surface,drop)+hop,xz.y);
                        pos=Vector3.Lerp(HousePoint(h,pos),PresentationLayout.Point(pos),drop);
                    }
                    if(!p.PoseSet||(p.LastPosition-pos).sqrMagnitude>.000001f||Quaternion.Angle(p.LastRotation,rot)>.005f)
                    {p.Transform.SetPositionAndRotation(pos,rot);p.LastPosition=pos;p.LastRotation=rot;p.PoseSet=true;}
                    p.Transform.localScale=scale;
                    if(p.Body!=null)
                    {
                        bool fast=released&&!stable&&p.MayUseCCD&&speed>6;CollisionDetectionMode mode=fast?CollisionDetectionMode.ContinuousSpeculative:CollisionDetectionMode.Discrete;
                        if(p.Body.collisionDetectionMode!=mode)p.Body.collisionDetectionMode=mode;if(fast)ccd++;
                        if(stable){p.Body.Sleep();sleeping++;}else if(released&&speed>.2f)p.Body.WakeUp();
                    }
                }
                foreach(Puff puff in h.Dust)UpdateDust(h,puff,time,hit&&effectsEnabled);
            }
            UpdateRing(time,failed);updateMS=(float)((Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency);
        }
        int CurrentClass(House h,float t,bool failed)
        {
            if(!failed||t<h.Impact)return -1;if(h.FinalClass==2&&t>=Mathf.Max(h.TotalAt,h.Impact+4.7f))return 2;
            return h.FinalClass>=1&&t>=Mathf.Max(h.PartialAt,h.Impact+2.6f)?1:0;
        }
        void UpdateMud(House h,float t,bool hit)
        {
            float grow=hit?Mathf.SmoothStep(0,1,(t-h.Impact-1.5f)/3.8f):0,deposit=History(h.DepthHistory,t)*data.VisualDepthScale;
            foreach(Transform cap in h.Mud)
            {
                bool show=grow>.01f&&deposit>.15f;if(cap.gameObject.activeSelf!=show)cap.gameObject.SetActive(show);if(!show)continue;
                Vector3 p=cap.position;p.y=data.DisplayBedHeight(p.x,PresentationLayout.SolverZ(p.x,p.z))+Mathf.Min(deposit*.18f,.5f)-.12f;cap.position=p;
                cap.localScale=new Vector3(4.2f,Mathf.Clamp(deposit*.95f,.3f,2.4f)*grow,3.7f);
            }
        }
        public void SelectHouse(int zeroBased){selected=Mathf.Clamp(zeroBased,0,Mathf.Max(0,houses.Count-1));inspectionKey=int.MinValue;}
        public string HouseName(int i){return "房屋 "+(Mathf.Clamp(i,0,Mathf.Max(0,houses.Count-1))+1).ToString("D2");}
        public Vector3 HouseLabelPosition(int i)
        {if(houses.Count==0)return FocusPoint;House h=houses[Mathf.Clamp(i,0,houses.Count-1)];return HousePoint(h,new Vector3(h.Center.x,h.BaseY+8,h.Center.y));}
        public int PickHouse(Ray ray)
        {
            int result=-1;float nearest=float.PositiveInfinity;
            for(int i=0;i<houses.Count;i++)
            {House h=houses[i];var bounds=new Bounds(HousePoint(h,new Vector3(h.Center.x,h.BaseY+3,h.Center.y)),new Vector3(11,12,9));
                if(bounds.IntersectRay(ray,out float entry)&&entry<nearest){nearest=entry;result=i;}}
            return result;
        }
        public string InspectionText(float t,bool failed)
        {
            if(houses.Count==0)return "房屋数据尚未载入";int key=Mathf.FloorToInt(t*10)+selected*100000+(failed?1000000:0);if(key==inspectionKey)return inspection;
            inspectionKey=key;House h=houses[selected];data.Sample(t,h.Center.x,h.Center.y,out float depth,out Vector2 velocity);float ratio=History(h.PeakHistory,t)/h.Capacity;
            int current=CurrentClass(h,t,failed);
            text.Length=0;text.Append(HouseName(selected)).Append(" | ").Append(current<0?"未受损":ClassName(current)).Append('\n');
            text.Append(Stage(h,t,failed)).Append('\n');
            text.Append("局地 h=").Append(depth.ToString("F3")).Append("m  v=").Append(velocity.magnitude.ToString("F1")).Append("m/s\n");
            text.Append("首次接触 t=").Append(h.Impact.ToString("F2")).Append(" s\n");
            text.Append("接触 h=").Append(h.HitH.ToString("F3")).Append("m v=").Append(h.HitV.ToString("F1")).Append("m/s\n");
            text.Append("阈值 h>0.08m 且 v>1.2m/s\n");
            text.Append("I=").Append(History(h.PeakHistory,t).ToString("F1")).Append(" / C=").Append(h.Capacity.ToString("F0")).Append(" (m³/s²)\n");
            text.Append("I/C=").Append(ratio.ToString("F2")).Append("  局部.65 / 整体1.35\n");
            text.Append("事件最终：").Append(ClassName(h.FinalClass)).Append('\n');
            text.Append("能力常数为演示设定，非结构验算");inspection=text.ToString();return inspection;
        }
        string Stage(House h,float t,bool failed)
        {
            if(!failed||t<h.Impact)return "未接触 / 完整";float age=t-h.Impact;
            if(age<1.1f)return "①迎坡墙受冲、开裂";if(age<2.3f)return "②主体倾斜、支撑变形";
            if(h.FinalClass==0)return "轻损，保留主体屋顶";
            float roofAt=Mathf.Max(h.Impact+2.35f,h.FinalClass==2?h.TotalAt+.25f:h.PartialAt+.4f);
            if(t<roofAt+1.1f)return "③支撑丧失、屋顶坠落";if(age<5.5f)return "④倒落碎片被拖曳";
            return h.FinalClass==1?"局部垮塌，残墙半屋顶":"整体垮塌，残墙掩埋";
        }
        static string ClassName(int level){return level==2?"整体垮塌":(level==1?"局部垮塌":"轻损");}
        void BuildRing(Shader shader)
        {
            var obj=new GameObject("Selected house footprint");obj.transform.SetParent(root,false);ring=obj.AddComponent<LineRenderer>();ring.positionCount=5;ring.useWorldSpace=true;
            var material=new Material(shader==null?Shader.Find("Sprites/Default"):shader);materials.Add(material);ring.sharedMaterial=material;
            ring.startColor=ring.endColor=new Color(1,.73f,.18f,.9f);ring.widthMultiplier=.18f;ring.numCornerVertices=2;
        }
        public void SetSelectionFootprintVisible(bool visible)
        {
            if(ring!=null)ring.enabled=visible;
        }
        void UpdateRing(float t,bool failed)
        {
            if(ring==null||houses.Count==0)return;House h=houses[selected];
            for(int i=0;i<5;i++)
            {float x=h.Center.x+(i==0||i==3||i==4?-5:5),z=h.Center.y+(i<2||i==4?-4:4);
                float y=Mathf.Max(h.BaseY-(failed?Burial(h,t):0),data.SurfaceHeight(t,x,PresentationLayout.SolverZ(x,z+h.DisplayShift)))+.16f;
                ring.SetPosition(i,HousePoint(h,new Vector3(x,y,z)));}
        }
        void MakeDustMaterial(Shader shader)
        {
            if(shader==null)shader=Shader.Find("Sprites/Default");dustTexture=new Texture2D(32,32,TextureFormat.RGBA32,false){name="Collapse dust alpha",wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[1024];for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {float r=new Vector2((x-15.5f)/15.5f,(y-15.5f)/15.5f).magnitude;pixels[x+y*32]=new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-r),1.5f));}
            dustTexture.SetPixels(pixels);dustTexture.Apply(false,true);dustMaterial=new Material(shader){name="Plaster collapse dust",mainTexture=dustTexture,renderQueue=3000};
            dustQuad=new Mesh{name="Pooled building dust"};dustQuad.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};
            dustQuad.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};dustQuad.triangles=new[]{0,1,2,0,2,3,2,1,0,3,2,0};dustQuad.RecalculateNormals();
        }
        void BuildDust(House h,Transform parent,System.Random rng)
        {
            for(int i=0;i<12;i++)
            {
                var obj=new GameObject("Pooled staged collapse dust");obj.transform.SetParent(parent,false);obj.AddComponent<MeshFilter>().sharedMesh=dustQuad;
                var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=dustMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                h.Dust.Add(new Puff{Transform=obj.transform,Renderer=renderer,Block=new MaterialPropertyBlock(),Offset=new Vector2(Range(rng,-4,4),Range(rng,-3,3)),
                    Delay=i<4?Range(rng,.3f,1):Range(rng,2.5f,4.2f),Life=Range(rng,2.2f,3.6f),Size=Range(rng,3,5)});obj.SetActive(false);
            }
        }
        void UpdateDust(House h,Puff puff,float t,bool enabled)
        {
            float age=t-h.Impact-puff.Delay;bool visible=enabled&&age>0&&age<puff.Life&&(h.FinalClass>0||puff.Delay<1.1f);
            if(puff.Transform.gameObject.activeSelf!=visible)puff.Transform.gameObject.SetActive(visible);if(!visible)return;
            Vector2 p=h.Center+puff.Offset;data.Sample(h.Impact,p.x,p.y,out float depth,out Vector2 velocity);Vector2 drift=velocity*(age*.07f);
            puff.Transform.position=HousePoint(h,new Vector3(p.x+drift.x,h.BaseY+1.1f+age*1.1f,p.y+drift.y));
            if(view!=null)puff.Transform.rotation=Quaternion.LookRotation(view.transform.position-puff.Transform.position);
            float size=puff.Size+age*1.7f;puff.Transform.localScale=new Vector3(size,size*.8f,size);
            float alpha=.3f*Mathf.Clamp01(age*3)*Mathf.Clamp01(1-age/puff.Life);Color tint=new Color(.71f,.65f,.52f,alpha);
            puff.Block.SetColor("_Color",tint);puff.Block.SetColor("_TintColor",tint);puff.Renderer.SetPropertyBlock(puff.Block);
        }
        static float Range(System.Random rng,float min,float max){return min+(float)rng.NextDouble()*(max-min);}
        static Mesh GableMesh()
        {
            var mesh=new Mesh{name="Triangular gable"};mesh.vertices=new[]{new Vector3(-.5f,-.5f,-.5f),new Vector3(-.5f,-.5f,.5f),new Vector3(-.5f,.5f,0),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.5f,0)};
            mesh.triangles=new[]{0,1,2,3,5,4,0,3,4,0,4,1,1,4,5,1,5,2,2,5,3,2,3,0};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        void OnDestroy()
        {
            foreach(Material m in materials)if(m!=null)Destroy(m);if(dustMaterial!=null)Destroy(dustMaterial);if(dustTexture!=null)Destroy(dustTexture);
            if(gable!=null)Destroy(gable);if(dustQuad!=null)Destroy(dustQuad);if(mound!=null)Destroy(mound);
        }
    }
}
