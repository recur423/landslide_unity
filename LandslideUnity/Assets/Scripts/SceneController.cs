using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace LandslideDemo
{
    public sealed class SceneController : MonoBehaviour
    {
        public Shader TerrainShader,SoilShader,StandardShader,ParticleShader;
        const float RainDuration=8;
        public const int CaptureFrames=720;
        SimulationData data;
        LandslideSurface surface;
        LandslideDebris debris;
        TreeResponse keyTrees;
        HouseDestruction houses;
        Transform scenery;
        Camera view;
        ParticleSystem rain;
        Light sun;
        float clock,rate=1,yaw=-9,pitch=21,distance=365,lastRainClock=-1;
        float lastClock=float.NaN,lastScale,lastStatisticsTime=-10,sourceLoss,toeDeposit;
        float previousFogWetness=-1f;
        bool playing=true,effects=true,showTrees=true,follow,captureMode,villageFocus,lastEffects,lastTrees,showDetails;
        float renderedYaw=-9,renderedPitch=21,renderedDistance=365;
        Vector3 cameraTarget=new Vector3(205,83,0);
        GUIStyle heading,label,small,button;
        Texture2D panel,white;
        string error;
        public SimulationData Data {get{return data;}}

        [Serializable] public sealed class FrameLabel {public float x,y;public string text;public bool selected;}
        [Serializable] public sealed class CaptureFrame
        {
            public int frame,phase;public float videoTime,clock,time,FS,rainMinutes,currentSpeed,peakSpeed,advance,sourceLoss,toeDeposit;
            public int damaged,light,partial,total,treesAffected,treesBroken;public string houseInfo;
            public float sourceX,sourceY,toeX,toeY;
            public float cameraX,cameraY,cameraZ,cameraYaw,cameraPitch,cameraDistance,cameraTransition;
            public LandslideSurface.DisplayFootprint displayFootprint;
            public FrameLabel[] labels;
        }
        [Serializable] public sealed class CaptureManifest {public int fps=30;public CaptureFrame[] frames;}

        void Awake()
        {
            try
            {
                Application.targetFrameRate=60;Physics.autoSyncTransforms=false;
                data=new SimulationData(Path.Combine(Application.streamingAssetsPath,"landslide.bytes"));
                MakeLighting();
                surface=new GameObject("Soil volume, front, deposit and source scar").AddComponent<LandslideSurface>();
                surface.transform.SetParent(transform);surface.Initialize(data,TerrainShader,SoilShader);
                gameObject.AddComponent<PresentationEnvironment>().Initialize(data,TerrainShader);
                gameObject.AddComponent<WatershedDetails>().Initialize(data);
                MakeScenery();
                keyTrees=new GameObject("Targeted uprooting and broken trees").AddComponent<TreeResponse>();
                keyTrees.transform.SetParent(transform);keyTrees.Initialize(data,keyTrees.transform);
                debris=new GameObject("Fracture and moving debris").AddComponent<LandslideDebris>();debris.transform.SetParent(transform);
                debris.Initialize(data,ProceduralAssets.CreateMaterial("Loose earth",new Color(.32f,.18f,.075f)),
                    ProceduralAssets.CreateMaterial("Boulders",new Color(.34f,.33f,.29f)));
                houses=new GameObject("Downstream village").AddComponent<HouseDestruction>();houses.transform.SetParent(transform);houses.Initialize(data,ParticleShader);
                MakeRain();RefreshWorld();
                Debug.Log("LANDSLIDE_READY grid="+data.NX+"x"+data.NZ+" frames="+data.FrameCount+" keyTrees="+keyTrees.TreeCount);
            }
            catch(Exception ex){error=ex.ToString();Debug.LogException(ex);playing=false;}
        }
        void Start()
        {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"--no-grade")>=0)view.GetComponent<PresentationPostProcess>().enabled=false;
            for(int i=0;i<args.Length;i++)
            {
                if(args[i]=="--self-test")StartCoroutine(SelfTest());
                if(args[i]=="--capture-dir" && i+1<args.Length)StartCoroutine(Capture(args[i+1]));
            }
        }
        void MakeLighting()
        {
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.45f,.53f,.58f);RenderSettings.ambientEquatorColor=new Color(.34f,.40f,.39f);
            RenderSettings.ambientGroundColor=new Color(.18f,.19f,.18f);RenderSettings.fog=true;RenderSettings.fogColor=new Color(.58f,.62f,.61f);
            RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.00165f;
            sun=new GameObject("Daylight through rain clouds").AddComponent<Light>();sun.type=LightType.Directional;
            sun.transform.rotation=Quaternion.Euler(58,-34,0);sun.intensity=.64f;sun.color=new Color(.92f,.96f,1f);sun.shadows=LightShadows.Soft;
            sun.shadowStrength=.37f;sun.shadowBias=.035f;sun.shadowNormalBias=.22f;
            RenderSettings.sun=sun;QualitySettings.shadowDistance=500;
            var camera=new GameObject("Main Camera");camera.tag="MainCamera";view=camera.AddComponent<Camera>();
            view.clearFlags=CameraClearFlags.Skybox;view.backgroundColor=RenderSettings.fogColor;view.fieldOfView=54;view.nearClipPlane=.3f;view.farClipPlane=2400;
            view.allowHDR=true;view.allowMSAA=true;camera.AddComponent<PresentationPostProcess>();
            SetCamera(true);
        }
        static float Range(System.Random r,float a,float b){return a+(b-a)*(float)r.NextDouble();}
        void MakeScenery()
        {
            scenery=new GameObject("Stable forest and slope rocks").transform;scenery.SetParent(transform);
            var rng=new System.Random(13852);
            for(int i=0;i<145;i++)
            {
                float x=Range(rng,15,390),z=Range(rng,-64,64);
                if (x>113f && x<324f && Mathf.Abs(z)<17f)
                    z=(z<0?-1:1)*Range(rng,19,57);
                float density = Mathf.Lerp(1.1f,.78f,Mathf.InverseLerp(18f,65f,Mathf.Abs(z)));
                ProceduralAssets.CreateTree(scenery,PresentationLayout.Point(x,data.DisplayBedHeight(x,z),z),
                    Range(rng,.8f,1.6f)*density,8000+i);
            }
            var material=ProceduralAssets.CreateMaterial("Weathered rock",new Color(.37f,.38f,.34f));var variants=new Mesh[8];
            for(int i=0;i<8;i++)variants[i]=ProceduralAssets.CreateRockMesh(720+i,.32f);
            for(int i=0;i<125;i++)
            {
                float x=Range(rng,10,406),z=Range(rng,-65,65);if(Mathf.Abs(z)<25 && x<330)z=(z<0?-1:1)*Range(rng,30,65);
                var rock=new GameObject("Slope rock");rock.transform.SetParent(scenery);float s=Range(rng,1,4.3f);
                rock.transform.position=PresentationLayout.Point(x,data.DisplayBedHeight(x,z)+s*.15f,z);rock.transform.localScale=new Vector3(s,s*.9f,s);
                rock.transform.rotation=Quaternion.Euler(Range(rng,0,30),Range(rng,0,360),Range(rng,-20,20));
                rock.AddComponent<MeshFilter>().sharedMesh=variants[i%8];rock.AddComponent<MeshRenderer>().sharedMaterial=material;
            }
        }
        void MakeRain()
        {
            var go=new GameObject("Rain streaks - visible at overview distance");go.transform.SetParent(transform);go.transform.position=new Vector3(210,255,0);go.transform.rotation=Quaternion.Euler(90,0,0);
            rain=go.AddComponent<ParticleSystem>();
            rain.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            rain.useAutoRandomSeed=false;rain.randomSeed=150727u;
            var main=rain.main;main.startLifetime=4.3f;main.startSpeed=62;main.startSize=.26f;main.maxParticles=11000;
            main.startColor=new Color(.70f,.82f,.86f,.56f);main.simulationSpace=ParticleSystemSimulationSpace.World;main.playOnAwake=false;rain.Pause();
            var shape=rain.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(540,200,1);var emission=rain.emission;emission.rateOverTime=2150;
            var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Stretch;renderer.lengthScale=12;renderer.velocityScale=.02f;
            Shader streakShader=Resources.Load<Shader>("Shaders/RainStreak");
            renderer.sharedMaterial=new Material(streakShader==null?ParticleShader:streakShader);renderer.shadowCastingMode=ShadowCastingMode.Off;
        }
        void Update()
        {
            if(data==null)return;
            if(!captureMode)
            {
                if(Input.GetKeyDown(KeyCode.Space))playing=!playing;if(Input.GetKeyDown(KeyCode.R))clock=0;
                if(Input.GetKeyDown(KeyCode.Tab))showDetails=!showDetails;
                if(playing)clock=Mathf.Min(clock+Time.deltaTime*rate,RainDuration+data.TimeMax);
                if(Input.GetMouseButton(1)){yaw+=Input.GetAxis("Mouse X")*3;pitch=Mathf.Clamp(pitch-Input.GetAxis("Mouse Y")*2,8,75);follow=false;villageFocus=false;}
                distance=Mathf.Clamp(distance-Input.mouseScrollDelta.y*18,65,620);
                if(Input.GetMouseButtonDown(0) && Input.mousePosition.y>118 && Input.mousePosition.y<Screen.height-100)
                {int picked=houses.PickHouse(view.ScreenPointToRay(Input.mousePosition));if(picked>=0){houses.SelectHouse(picked);showDetails=true;}}
                RefreshWorld();
            }
            if(!captureMode)SetCamera(false);
        }
        void RefreshWorld()
        {
            if(data==null || surface==null)return;
            Shader.SetGlobalFloat("_PresentationTime",clock);
            float rainProgress=Mathf.Clamp01(clock/RainDuration);
            if (previousFogWetness != rainProgress)
            {
                RenderSettings.fogDensity = Mathf.Lerp(.0015f,.00205f,rainProgress);
                previousFogWetness = rainProgress;
            }
            Shader.SetGlobalFloat("_RainWetness",rainProgress);
            Shader.SetGlobalFloat("_RainStrength",effects?(clock<RainDuration?Mathf.Lerp(.62f,1f,rainProgress):Mathf.Clamp01(1f-(clock-RainDuration)*1.8f)):0f);
            if(clock==lastClock && data.VisualDepthScale==lastScale && effects==lastEffects && showTrees==lastTrees)return;
            bool failed=clock>=RainDuration;float t=Mathf.Max(0,clock-RainDuration);
            if(PerformanceProbe.ComponentTimingEnabled)
            {
                long before=System.Diagnostics.Stopwatch.GetTimestamp();
                surface.SetState(t,failed);long afterSurface=System.Diagnostics.Stopwatch.GetTimestamp();
                keyTrees.SetState(t,failed,showTrees);long afterTrees=System.Diagnostics.Stopwatch.GetTimestamp();
                debris.SetState(t,failed,effects);long afterDebris=System.Diagnostics.Stopwatch.GetTimestamp();
                houses.SetState(t,failed,effects);long afterHouses=System.Diagnostics.Stopwatch.GetTimestamp();
                PerformanceProbe.RecordComponentTicks(afterSurface-before,afterTrees-afterSurface,
                    afterDebris-afterTrees,afterHouses-afterDebris);
            }
            else
            {
                surface.SetState(t,failed);keyTrees.SetState(t,failed,showTrees);
                debris.SetState(t,failed,effects);houses.SetState(t,failed,effects);
            }
            if(scenery.gameObject.activeSelf!=showTrees)scenery.gameObject.SetActive(showTrees);
            var emission=rain.emission;emission.enabled=!failed && effects;
            if(failed || !effects)rain.Clear();else
            {
                float delta=clock-lastRainClock;
                if(lastRainClock<0 || delta<0 || delta>.2f)rain.Simulate(Mathf.Min(clock+1.25f,4.3f),true,true,false);
                else if(delta>0)rain.Simulate(delta,true,false,false);
            }
            lastRainClock=clock;lastClock=clock;lastScale=data.VisualDepthScale;lastEffects=effects;lastTrees=showTrees;
            if(Mathf.Abs(t-lastStatisticsTime)>.5f || (t==data.TimeMax && lastStatisticsTime!=t))
            {sourceLoss=data.RegionVolume(t,55,125,true);toeDeposit=data.RegionVolume(t,250,420,false);lastStatisticsTime=t;}
        }
        void SetCamera(bool immediate)
        {
            if(view==null)return;
            float x=data==null?205:Mathf.Clamp(data.Statistic(Mathf.Max(0,clock-RainDuration),0)-30,70,320);
            Vector3 target=follow && data!=null?PresentationLayout.Point(x,data.SurfaceHeight(Mathf.Max(0,clock-RainDuration),x,0)+5,0):new Vector3(205,84,0);
            if(villageFocus && houses!=null)target=houses.FocusPoint;
            cameraTarget=immediate?target:Vector3.Lerp(cameraTarget,target,Time.unscaledDeltaTime*2);
            float blend=immediate?1:1-Mathf.Exp(-Time.unscaledDeltaTime*4);
            renderedYaw=Mathf.LerpAngle(renderedYaw,yaw,blend);renderedPitch=Mathf.Lerp(renderedPitch,pitch,blend);renderedDistance=Mathf.Lerp(renderedDistance,distance,blend);
            view.transform.position=cameraTarget+Quaternion.Euler(renderedPitch,renderedYaw,0)*new Vector3(0,0,-renderedDistance);
            view.transform.position+=FailureShake(clock);
            view.transform.LookAt(cameraTarget);
        }
        static Vector3 FailureShake(float videoClock)
        {
            float age=videoClock-RainDuration;
            if(age<0 || age>.75f)return Vector3.zero;
            float envelope=1f-Mathf.SmoothStep(0,1,age/.75f);
            return new Vector3(Mathf.Sin(age*43f)*.58f,Mathf.Sin(age*57f+.4f)*.37f,0)*envelope;
        }
        int RainRow {get{return Mathf.Clamp(Mathf.RoundToInt(clock/RainDuration*89),0,89);}}
        void InitGUI()
        {
            var face=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","SimHei","Arial"},18);
            heading=new GUIStyle(GUI.skin.label){font=face,fontSize=24,fontStyle=FontStyle.Bold};heading.normal.textColor=new Color(.94f,.97f,1);
            label=new GUIStyle(GUI.skin.label){font=face,fontSize=16};label.normal.textColor=new Color(.87f,.93f,.96f);
            small=new GUIStyle(label){fontSize=15};button=new GUIStyle(GUI.skin.button){font=face,fontSize=15};
            panel=new Texture2D(1,1);panel.SetPixel(0,0,new Color(.025f,.06f,.09f,.92f));panel.Apply();white=Texture2D.whiteTexture;
        }
        void Line(Vector2 a,Vector2 b,Color color,float width=2)
        {
            var matrix=GUI.matrix;var old=GUI.color;GUI.color=color;float angle=Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle,a);GUI.DrawTexture(new Rect(a.x,a.y,Vector2.Distance(a,b),width),white);GUI.matrix=matrix;GUI.color=old;
        }
        void DrawFS(Rect rect)
        {
            GUI.DrawTexture(rect,panel);GUI.Label(new Rect(rect.x+12,rect.y+6,rect.width-20,26),"实际计算 FS 曲线 · 89 min 失稳",small);
            float left=rect.x+32,right=rect.xMax-18,top=rect.y+40,bottom=rect.yMax-25;
            Func<float,float> py=v=>bottom-(v-.8f)/.45f*(bottom-top);
            Line(new Vector2(left,py(1)),new Vector2(right,py(1)),new Color(1,.4f,.35f),1);
            int reached=clock<RainDuration?RainRow:89;
            for(int i=1;i<=89;i++)Line(new Vector2(Mathf.Lerp(left,right,(i-1)/89f),py(data.Rain[i-1,4])),
                new Vector2(Mathf.Lerp(left,right,i/89f),py(data.Rain[i,4])),i<=reached?new Color(1,.73f,.3f):new Color(.45f,.45f,.4f),2);
            Line(new Vector2(right,top),new Vector2(right,bottom),new Color(1,.4f,.35f),1);
            GUI.Label(new Rect(left,bottom+2,100,22),"0 min",small);GUI.Label(new Rect(right-65,bottom+2,90,22),"89 min",small);
        }
        void OnGUI()
        {
            if(captureMode)return;
            if(heading==null)InitGUI();GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1280f,Screen.height/720f,1));
            if(error==null && data!=null && !showDetails){DrawCompactGUI();return;}
            GUI.DrawTexture(new Rect(0,0,1280,92),panel);GUI.Label(new Rect(28,12,930,36),"降雨触发滑坡 | 体积、树木与房屋响应",heading);
            GUI.Label(new Rect(30,54,1100,28),"实际厚度与速度参与触发；厚度显示 5×，表面起伏与倒塌为表现层",label);
            if(GUI.Button(new Rect(1135,24,115,32),"收起数据",button))showDetails=false;
            if(error!=null){GUI.TextArea(new Rect(30,110,1200,400),error);return;}if(data==null)return;
            float t=Mathf.Max(0,clock-RainDuration);bool failed=clock>=RainDuration;
            GUI.DrawTexture(new Rect(28,108,348,142),panel);
            GUI.Label(new Rect(42,116,330,26),failed?"失稳后时间  "+t.ToString("F1")+" s":"累计降雨时间  "+RainRow+" min",label);
            GUI.Label(new Rect(42,145,330,25),"当前最大速度  "+data.Statistic(t,1).ToString("F1")+" m/s",label);
            GUI.Label(new Rect(42,174,330,25),"全过程峰值  "+data.PeakSpeed.ToString("F1")+" m/s",label);
            GUI.Label(new Rect(42,203,330,27),"前缘推进  "+data.FrontAdvance(t).ToString("F1")+" m",label);
            DrawFS(new Rect(28,263,348,145));
            if(GUI.Button(new Rect(950,108,90,30),"全景",button)){follow=false;villageFocus=false;distance=365;pitch=21;yaw=-9;}
            if(GUI.Button(new Rect(1050,108,100,30),"滑体特写",button)){follow=true;villageFocus=false;distance=150;pitch=37;}
            if(GUI.Button(new Rect(1160,108,100,30),"房屋特写",button)){follow=false;villageFocus=true;distance=110;pitch=37;yaw=-10;}
            GUI.DrawTexture(new Rect(930,153,330,280),panel);GUI.Label(new Rect(943,162,306,260),houses.InspectionText(t,failed),small);
            for(int i=0;i<houses.HouseCount;i++)
            {
                Vector3 p=view.WorldToScreenPoint(houses.HouseLabelPosition(i));if(p.z<0)continue;
                float x=p.x/Screen.width*1280,y=(1-p.y/Screen.height)*720;
                if(x>380 && x<925 && y>95 && y<590)
                    if(GUI.Button(new Rect(x-23,y-13,46,26),"H"+(i+1),button))houses.SelectHouse(i);
            }
            if(t>30)
            {
                GUI.DrawTexture(new Rect(383,496,520,94),panel);
                GUI.Label(new Rect(395,502,505,26),"受损 "+houses.DestroyedCount+"/6 · 轻损 "+houses.LightDamageCount+" · 局部 "+houses.PartialCollapseCount+" · 整体 "+houses.TotalCollapseCount,label);
                GUI.Label(new Rect(395,533,505,26),"源区滑失 "+sourceLoss.ToString("F0")+" m³ / 坡脚堆积 "+toeDeposit.ToString("F0")+" m³",label);
                GUI.Label(new Rect(395,562,505,24),"响应树木 "+keyTrees.AffectedCount+" / 折断 "+keyTrees.BrokenCount,small);
                SceneMarker(PresentationLayout.Point(90,data.SurfaceHeight(t,90,0)+2,0),"源区凹陷");
                SceneMarker(PresentationLayout.Point(310,data.SurfaceHeight(t,310,0)+2,0),"坡脚堆积");
            }
            GUI.DrawTexture(new Rect(0,603,1280,117),panel);
            if(GUI.Button(new Rect(28,616,90,33),playing?"暂停":"播放",button))playing=!playing;
            if(GUI.Button(new Rect(128,616,90,33),"重播",button)){clock=0;playing=true;rain.Clear();}
            if(GUI.Button(new Rect(228,616,100,33),"跳到失稳",button))clock=8.01f;
            float seek=GUI.HorizontalSlider(new Rect(350,628,570,24),clock,0,8+data.TimeMax);if(Mathf.Abs(seek-clock)>.005f){clock=seek;RefreshWorld();}
            GUI.Label(new Rect(942,620,300,26),"场景回放 "+clock.ToString("F1")+" / 53 s",label);
            effects=GUI.Toggle(new Rect(30,668,120,25),effects,"破碎 / 扬尘",label);showTrees=GUI.Toggle(new Rect(160,668,120,25),showTrees,"树木 / 岩石",label);
            bool oldFollow=follow;follow=GUI.Toggle(new Rect(290,668,100,25),follow,"跟随滑体",label);if(follow&&!oldFollow)villageFocus=false;
            GUI.Label(new Rect(405,668,60,25),"速度",label);rate=GUI.HorizontalSlider(new Rect(460,676,110,19),rate,.25f,3);
            GUI.Label(new Rect(583,668,80,25),rate.ToString("F1")+"×",label);
            GUI.Label(new Rect(680,664,585,31),"点击编号查看房屋 · 右键旋转 · 滚轮缩放 · 空格暂停",small);
        }
        void DrawCompactGUI()
        {
            GUI.Label(new Rect(26,18,620,36),"降雨 · 滑坡 · 村落响应",heading);
            if(GUI.Button(new Rect(942,22,92,30),"全景",button)){follow=false;villageFocus=false;distance=365;pitch=21;yaw=-9;}
            if(GUI.Button(new Rect(1042,22,102,30),"滑体特写",button)){follow=true;villageFocus=false;distance=165;pitch=38;}
            if(GUI.Button(new Rect(1152,22,102,30),"房屋特写",button)){follow=false;villageFocus=true;distance=115;pitch=34;yaw=-9;}
            float t=Mathf.Max(0,clock-RainDuration);
            string metric=clock<RainDuration?"累计降雨   "+RainRow+" min":(t<30?"滑体速度   "+data.Statistic(t,1).ToString("F1")+" m/s":"前缘推进   "+data.FrontAdvance(t).ToString("F0")+" m");
            GUI.DrawTexture(new Rect(26,588,320,43),panel);GUI.Label(new Rect(39,596,300,28),metric,label);
            GUI.DrawTexture(new Rect(0,656,1280,64),panel);
            if(GUI.Button(new Rect(25,674,70,29),playing?"暂停":"播放",button))playing=!playing;
            if(GUI.Button(new Rect(105,674,70,29),"重播",button)){clock=0;playing=true;rain.Clear();}
            float seek=GUI.HorizontalSlider(new Rect(195,684,578,20),clock,0,RainDuration+data.TimeMax);
            if(Mathf.Abs(seek-clock)>.005f){clock=seek;RefreshWorld();}
            GUI.Label(new Rect(790,677,130,26),clock.ToString("F1")+" / 53 s",small);
            if(GUI.Button(new Rect(930,674,100,29),"详细数据",button))showDetails=true;
            GUI.Label(new Rect(1040,676,230,27),"右键旋转 / 滚轮缩放",small);
        }
        void SceneMarker(Vector3 point,string title)
        {
            Vector3 p=view.WorldToScreenPoint(point);if(p.z<0)return;
            float x=p.x/Screen.width*1280,y=(1-p.y/Screen.height)*720;
            GUI.DrawTexture(new Rect(x-53,y-36,106,30),panel);GUI.Label(new Rect(x-47,y-32,100,26),title,label);
        }
        IEnumerator SelfTest()
        {
            playing=false;yield return null;if(data==null){Application.Quit(2);yield break;}
            foreach(float value in new[]{0f,8.5f,19f,24f,32f,53f}){clock=value;RefreshWorld();yield return null;}
            if(houses.DestroyedCount!=6 || houses.LightDamageCount<1 || houses.PartialCollapseCount<1 || houses.TotalCollapseCount<1 || keyTrees.BrokenCount<1)
            {Debug.LogError("UPGRADE_TEST_FAILED: missing mixed damage/tree response");Application.Quit(3);yield break;}
            Debug.Log("UPGRADE_FINAL light="+houses.LightDamageCount+" partial="+houses.PartialCollapseCount+" total="+houses.TotalCollapseCount+" trees="+keyTrees.AffectedCount+" broken="+keyTrees.BrokenCount);
            clock=29;RefreshWorld();var parts=houses.GetComponentsInChildren<Transform>(true);var pos=new Vector3[parts.Length];
            for(int i=0;i<parts.Length;i++)pos[i]=parts[i].position;
            clock=40;RefreshWorld();clock=29;RefreshWorld();
            for(int i=0;i<parts.Length;i++)if(Vector3.Distance(pos[i],parts[i].position)>.001f)
            {Debug.LogError("UPGRADE_TEST_FAILED: seek determinism");Application.Quit(4);yield break;}
            clock=0;RefreshWorld();
            if(houses.DestroyedCount!=0 || keyTrees.AffectedCount!=0){Debug.LogError("UPGRADE_TEST_FAILED: rewind");Application.Quit(5);yield break;}
            if(!keyTrees.ValidateTimeline(out string treeResult)){Debug.LogError("TREE_RESPONSE_TEST_FAILED "+treeResult);Application.Quit(6);yield break;}
            Debug.Log("TREE_RESPONSE_TEST_OK "+treeResult);
            if(!debris.ValidateTimeline(out string debrisResult)){Debug.LogError("STRAIN_DEBRIS_TEST_FAILED "+debrisResult);Application.Quit(7);yield break;}
            Debug.Log("STRAIN_DEBRIS_TEST_OK "+debrisResult);
            Debug.Log("LANDSLIDE_SELF_TEST_OK mixedDamage=true trees=true seekAndRewind=true vertices="+surface.VertexCount);Application.Quit(0);
        }
        static float PreviewTime(int k)
        {
            if(k<105)return k/104f*8;
            if(k<210)return 8+(k-105)/104f*10;
            if(k<240)return 18+(k-210)/29f*2;
            if(k<360)return 20+(k-240)/119f*2;
            if(k<600)return 22+(k-360)/239f*11;
            return 8+Mathf.Lerp(25,45,Mathf.Clamp01((k-600)/84f));
        }
        struct CapturePose
        {
            public Vector3 Target;
            public float Yaw,Pitch,Distance;
            public static CapturePose Blend(CapturePose a,CapturePose b,float t)
            {
                return new CapturePose{Target=Vector3.Lerp(a.Target,b.Target,t),Yaw=Mathf.LerpAngle(a.Yaw,b.Yaw,t),
                    Pitch=Mathf.Lerp(a.Pitch,b.Pitch,t),Distance=Mathf.Lerp(a.Distance,b.Distance,t)};
            }
        }
        CapturePose ShotPose(int k,int phase)
        {
            float t=Mathf.Max(0,clock-RainDuration);
            var pose=new CapturePose{Target=new Vector3(195,79,0),Yaw=-17,Pitch=22,Distance=325};
            if(phase==0)
            {
                float sweep = Mathf.SmoothStep(0,1,k/104f);
                pose.Target = Vector3.Lerp(new Vector3(184,99,-11), new Vector3(148,111,-12), sweep);
                pose.Distance = Mathf.Lerp(300,248,sweep);
                pose.Yaw = Mathf.Lerp(-15,-27,sweep);
                pose.Pitch = Mathf.Lerp(20,23,sweep);
            }
            if(phase==1)
            {
                float followFront=Mathf.SmoothStep(0f,1f,(k-140f)/57f);
                float x=Mathf.Lerp(77f,Mathf.Clamp(data.Statistic(t,0)-37f,88f,179f),followFront);
                pose.Target=PresentationLayout.Point(x,data.DisplayBedHeight(x,0)+4,0);
                pose.Distance=Mathf.Lerp(130,159,followFront);
                pose.Yaw=-22;pose.Pitch=Mathf.Lerp(31,48,followFront);
            }
            if(phase==2)
            {
                float down = Mathf.SmoothStep(0,1,(k-270f)/89f);
                float x = Mathf.Lerp(224,256,down);
                pose.Target = PresentationLayout.Point(x,data.DisplayBedHeight(x,0)+3,0);
                pose.Yaw=Mathf.Lerp(-15,-33,down);pose.Distance=Mathf.Lerp(136,142,down);pose.Pitch=62;
            }
            if(phase==3)
            {
                float move=Mathf.SmoothStep(0,1,(k-366f)/78f);
                float x=Mathf.Lerp(257,285,move);
                pose.Target=PresentationLayout.Point(x,data.SurfaceHeight(t,x,0)+1,0);
                pose.Yaw=Mathf.Lerp(-55,-43,move);pose.Pitch=Mathf.Lerp(14,19,move);pose.Distance=Mathf.Lerp(129,112,move);
            }
            if(phase==4)
            {
                pose.Target=PresentationLayout.Point(301,data.DisplayBedHeight(301,0)+4,0);
                pose.Yaw=-24;pose.Pitch=39;pose.Distance=137;
            }
            if(phase==5)
            {
                pose.Target=PresentationLayout.Point(307,data.DisplayBedHeight(307,0)+5,0);
                pose.Yaw=-34;pose.Pitch=31;pose.Distance=149;
            }
            if(phase==6)
            {
                float blend=Mathf.SmoothStep(0,1,(k-600)/33f);
                Vector3 toe=PresentationLayout.Point(307,data.DisplayBedHeight(307,0)+5,0);
                pose.Target=Vector3.Lerp(toe,new Vector3(195,79,0),blend);
                pose.Yaw=Mathf.Lerp(-34,-24,blend);
                pose.Distance=Mathf.Lerp(149,319,blend);pose.Pitch=31;
            }
            return pose;
        }
        IEnumerator Capture(string directory)
        {
            captureMode=true;playing=false;Directory.CreateDirectory(directory);yield return null;yield return null;
            if(data==null || error!=null){Debug.LogError("CAPTURE_FAILED "+error);Application.Quit(7);yield break;}
            // The interactive selection footprint is an inspection aid, not a film element.
            houses.SetSelectionFootprintVisible(false);
            bool keyframes=Array.IndexOf(Environment.GetCommandLineArgs(),"--capture-keyframes")>=0;
            var sampleFrames=new HashSet<int>(new[]{0,45,60,90,104,105,110,117,120,129,135,150,180,209,210,222,234,240,255,270,300,315,330,359,360,372,384,420,449,450,462,474,480,510,539,540,552,564,599,600,612,624,633,660,719});
            // The camera grades HDR internally; PNG readback must use an sRGB LDR target.
            var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB){antiAliasing=4};
            var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            var frames=new List<CaptureFrame>();
            var pose=ShotPose(0,0);
            CapturePose transitionFrom=pose;int previousPhase=0,transitionStart=-100;
            for(int k=0;k<CaptureFrames;k++)
            {
                clock=PreviewTime(k);
                int phase=k<105?0:(k<210?1:(k<360?2:(k<450?3:(k<540?4:(k<600?5:6)))));
                houses.SelectHouse(k<405?0:(k<500?2:4));RefreshWorld();
                if(k==45)Debug.Log("PRESENTATION_RAIN particles="+rain.particleCount+" wetness="+Mathf.Clamp01(clock/RainDuration).ToString("F2"));
                if(phase!=previousPhase){transitionFrom=pose;transitionStart=k;previousPhase=phase;}
                float transition=Mathf.SmoothStep(0,1,(k-transitionStart)/24f);
                pose=CapturePose.Blend(transitionFrom,ShotPose(k,phase),transition);
                cameraTarget=pose.Target;
                view.transform.position=pose.Target+Quaternion.Euler(pose.Pitch,pose.Yaw,0)*new Vector3(0,0,-pose.Distance)+FailureShake(clock);
                view.transform.LookAt(pose.Target);
                if(keyframes && !sampleFrames.Contains(k))continue;
                yield return null;view.targetTexture=target;view.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
                view.targetTexture=null;RenderTexture.active=null;File.WriteAllBytes(Path.Combine(directory,k.ToString("D4")+".png"),image.EncodeToPNG());
                float t=Mathf.Max(0,clock-8);var labels=new FrameLabel[houses.HouseCount];
                for(int i=0;i<labels.Length;i++){Vector3 p=view.WorldToViewportPoint(houses.HouseLabelPosition(i));labels[i]=new FrameLabel{x=p.x*1280,y=(1-p.y)*720,text="H"+(i+1),selected=i==houses.SelectedIndex};}
                Vector3 sourceScreen=view.WorldToViewportPoint(PresentationLayout.Point(90,data.SurfaceHeight(t,90,0)+2,0));
                Vector3 toeScreen=view.WorldToViewportPoint(PresentationLayout.Point(310,data.SurfaceHeight(t,310,0)+2,0));
                frames.Add(new CaptureFrame{frame=k,phase=phase,videoTime=k/30f,clock=clock,time=t,FS=data.Rain[RainRow,4],rainMinutes=RainRow,
                    currentSpeed=data.Statistic(t,1),peakSpeed=data.PeakSpeed,advance=data.FrontAdvance(t),sourceLoss=sourceLoss,toeDeposit=toeDeposit,
                    damaged=houses.DestroyedCount,light=houses.LightDamageCount,partial=houses.PartialCollapseCount,total=houses.TotalCollapseCount,
                    treesAffected=keyTrees.AffectedCount,treesBroken=keyTrees.BrokenCount,houseInfo=houses.InspectionText(t,clock>=8),labels=labels,
                    sourceX=sourceScreen.x*1280,sourceY=(1-sourceScreen.y)*720,toeX=toeScreen.x*1280,toeY=(1-toeScreen.y)*720,
                    cameraX=pose.Target.x,cameraY=pose.Target.y,cameraZ=pose.Target.z,cameraYaw=pose.Yaw,cameraPitch=pose.Pitch,
                    cameraDistance=pose.Distance,cameraTransition=transition,displayFootprint=surface.MeasureDisplayFootprint()});
                if(k%150==0)Debug.Log("CAPTURE_PROGRESS "+k+"/"+CaptureFrames);
            }
            File.WriteAllText(Path.Combine(directory,"metadata.json"),JsonUtility.ToJson(new CaptureManifest{frames=frames.ToArray()},true));target.Release();Destroy(target);Destroy(image);
            Debug.Log("LANDSLIDE_CAPTURE_OK frames="+frames.Count+" fps=30 clean=true");Application.Quit(0);
        }
    }
}
