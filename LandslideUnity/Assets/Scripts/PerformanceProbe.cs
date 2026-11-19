using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace LandslideDemo
{
    // Controlled runtime benchmark: no PNG encoding or video capture in timed loop.
    public sealed class PerformanceProbe : MonoBehaviour
    {
        [Serializable] sealed class Distribution { public double median,p95,max; }
        [Serializable] sealed class Report
        {
            public string unityVersion,graphicsDevice,scope;
            public int samples,colliderCount,rigidbodyCount;
            public Distribution scriptUpdateMs,renderSubmissionMs,frameIntervalMs;
            public Distribution surfaceMs,treesMs,debrisMs,housesMs,otherScriptMs;
            public SurfaceStageReport surfaceStages;
        }
        [Serializable] sealed class SurfaceStageReport
        {
            public Distribution topologyMs,soilVerticesMs,normalsMs,strainMs,blockDeformationMs;
            public Distribution blockUploadMs,seamsMs,boundaryWallsMs,scarpMs,otherSurfaceMs;
            public Distribution seamGeometryMs,seamUploadMs,openSeams,visibleSeams,totalSeams;
        }
        internal struct SurfaceStageTimings
        {
            public double topologyMs,soilVerticesMs,normalsMs,strainMs,blockDeformationMs;
            public double blockUploadMs,seamsMs,boundaryWallsMs,scarpMs;
            public double seamGeometryMs,seamUploadMs;
            public int openSeams,visibleSeams,totalSeams;
            public double Total { get { return topologyMs+soilVerticesMs+normalsMs+strainMs+
                blockDeformationMs+blockUploadMs+seamsMs+boundaryWallsMs+scarpMs; } }
        }
        // Off in normal playback and capture. RefreshWorld only reads this flag.
        internal static bool ComponentTimingEnabled;
        internal static double LastSurfaceMs,LastTreesMs,LastDebrisMs,LastHousesMs;
        internal static SurfaceStageTimings LastSurfaceStages;
        internal static void ResetComponentTimings()
        {
            LastSurfaceMs=LastTreesMs=LastDebrisMs=LastHousesMs=0;
            LastSurfaceStages=default(SurfaceStageTimings);
        }
        internal static void RecordSurfaceMainTicks(long topology,long soilVertices,long normals,
            long boundaryWalls,long scarp)
        {
            double ms=1000.0/Stopwatch.Frequency;
            SurfaceStageTimings stages=LastSurfaceStages;
            stages.topologyMs=topology*ms;stages.soilVerticesMs=soilVertices*ms;
            stages.normalsMs=normals*ms;stages.boundaryWallsMs=boundaryWalls*ms;
            stages.scarpMs=scarp*ms;
            LastSurfaceStages=stages;
        }
        internal static void RecordSurfaceFractureTicks(long strain,long blockDeformation,
            long blockUpload,long seams)
        {
            double ms=1000.0/Stopwatch.Frequency;
            SurfaceStageTimings stages=LastSurfaceStages;
            stages.strainMs=strain*ms;stages.blockDeformationMs=blockDeformation*ms;
            stages.blockUploadMs=blockUpload*ms;stages.seamsMs=seams*ms;
            LastSurfaceStages=stages;
        }
        internal static void RecordSeamTimingTicks(long geometry,long upload,
            int openSeams,int visibleSeams,int totalSeams)
        {
            double ms=1000.0/Stopwatch.Frequency;
            SurfaceStageTimings stages=LastSurfaceStages;
            stages.seamGeometryMs=geometry*ms;stages.seamUploadMs=upload*ms;
            stages.openSeams=openSeams;stages.visibleSeams=visibleSeams;stages.totalSeams=totalSeams;
            LastSurfaceStages=stages;
        }
        internal static void RecordComponentTicks(long surface,long trees,long debris,long houses)
        {
            double ms=1000.0/Stopwatch.Frequency;
            LastSurfaceMs=surface*ms;LastTreesMs=trees*ms;
            LastDebrisMs=debris*ms;LastHousesMs=houses*ms;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length-1;i++)if(args[i]=="--benchmark-dir")
            { var go=new GameObject("Runtime impact benchmark");go.AddComponent<PerformanceProbe>().directory=args[i+1];break; }
        }
        string directory;
        static Distribution Stats(List<double> values)
        { values.Sort();return new Distribution{median=values[values.Count/2],p95=values[(int)((values.Count-1)*.95)],max=values[values.Count-1]}; }
        IEnumerator Start()
        {
            yield return null;
            var controller=FindObjectOfType<SceneController>();
            if(controller==null || controller.Data==null){Application.Quit(10);yield break;}
            controller.enabled=false;
            QualitySettings.vSyncCount=0;Application.targetFrameRate=-1;
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var clock=typeof(SceneController).GetField("clock",flags);
            var refresh=typeof(SceneController).GetMethod("RefreshWorld",flags);
            var view=Camera.main;
            view.transform.position=new Vector3(282,controller.Data.BedHeight(282,0)+83,-125);
            view.transform.LookAt(new Vector3(282,controller.Data.BedHeight(282,0)+4,0));
            var target=new RenderTexture(1280,720,24);view.targetTexture=target;
            Directory.CreateDirectory(directory);
            var script=new List<double>();var render=new List<double>();var frame=new List<double>();
            var surfaceTimes=new List<double>();var treeTimes=new List<double>();
            var debrisTimes=new List<double>();var houseTimes=new List<double>();var otherTimes=new List<double>();
            var stageTimes=new List<double>[15];
            for(int stage=0;stage<stageTimes.Length;stage++)stageTimes[stage]=new List<double>();
            var watch=new Stopwatch();var rows=new List<string>{"simulation_time_s,script_update_ms,render_submission_ms,frame_interval_ms"};
            var componentRows=new List<string>{"simulation_time_s,surface_ms,trees_ms,debris_ms,houses_ms,other_script_ms"};
            var stageRows=new List<string>{"simulation_time_s,topology_ms,soil_vertices_ms,normals_ms,strain_ms,block_deformation_ms,block_upload_ms,seams_ms,boundary_walls_ms,scarp_ms,other_surface_ms,seam_geometry_ms,seam_upload_ms,open_seams,visible_seams,total_seams"};
            ComponentTimingEnabled=true;
            try
            {
                for(int i=-60;i<480;i++)
                {
                    float t=10+Mathf.Max(i,0)/479f*14;
                    clock.SetValue(controller,8+t);
                    ResetComponentTimings();
                    watch.Restart();refresh.Invoke(controller,null);Physics.SyncTransforms();watch.Stop();double update=watch.Elapsed.TotalMilliseconds;
                    watch.Restart();view.Render();watch.Stop();double draw=watch.Elapsed.TotalMilliseconds;
                    if(i>=0)
                    {
                        double delta=Time.unscaledDeltaTime*1000;
                        script.Add(update);render.Add(draw);frame.Add(delta);
                        rows.Add(string.Format(CultureInfo.InvariantCulture,"{0:F4},{1:F5},{2:F5},{3:F5}",t,update,draw,delta));
                        double other=Math.Max(0,update-LastSurfaceMs-LastTreesMs-LastDebrisMs-LastHousesMs);
                        surfaceTimes.Add(LastSurfaceMs);treeTimes.Add(LastTreesMs);
                        debrisTimes.Add(LastDebrisMs);houseTimes.Add(LastHousesMs);otherTimes.Add(other);
                        componentRows.Add(string.Format(CultureInfo.InvariantCulture,"{0:F4},{1:F5},{2:F5},{3:F5},{4:F5},{5:F5}",
                            t,LastSurfaceMs,LastTreesMs,LastDebrisMs,LastHousesMs,other));
                        SurfaceStageTimings stages=LastSurfaceStages;
                        double otherSurface=Math.Max(0,LastSurfaceMs-stages.Total);
                        stageTimes[0].Add(stages.topologyMs);stageTimes[1].Add(stages.soilVerticesMs);
                        stageTimes[2].Add(stages.normalsMs);stageTimes[3].Add(stages.strainMs);
                        stageTimes[4].Add(stages.blockDeformationMs);stageTimes[5].Add(stages.blockUploadMs);
                        stageTimes[6].Add(stages.seamsMs);stageTimes[7].Add(stages.boundaryWallsMs);
                        stageTimes[8].Add(stages.scarpMs);stageTimes[9].Add(otherSurface);
                        stageTimes[10].Add(stages.seamGeometryMs);stageTimes[11].Add(stages.seamUploadMs);
                        stageTimes[12].Add(stages.openSeams);stageTimes[13].Add(stages.visibleSeams);
                        stageTimes[14].Add(stages.totalSeams);
                        stageRows.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0:F4},{1:F5},{2:F5},{3:F5},{4:F5},{5:F5},{6:F5},{7:F5},{8:F5},{9:F5},{10:F5},{11:F5},{12:F5},{13},{14},{15}",
                            t,stages.topologyMs,stages.soilVerticesMs,stages.normalsMs,stages.strainMs,
                            stages.blockDeformationMs,stages.blockUploadMs,stages.seamsMs,
                            stages.boundaryWallsMs,stages.scarpMs,otherSurface,stages.seamGeometryMs,
                            stages.seamUploadMs,stages.openSeams,stages.visibleSeams,stages.totalSeams));
                    }
                    yield return null;
                }
            }
            finally { ComponentTimingEnabled=false; }
            var report=new Report{
                unityVersion=Application.unityVersion,graphicsDevice=SystemInfo.graphicsDeviceName,
                scope="Windows Player, 1280x720 offscreen camera, impact t10-24s; component times bracket four SetState calls; surface stages break down topology, soil vertices, normals, velocity strain, block deformation, mesh upload, seam, boundary and scarp; other surface is residual SetState time; other script includes RefreshWorld overhead, reflection and transform sync; render submission is CPU-side Camera.Render, not isolated GPU time; frame intervals include runtime scheduling; no image encoding and no IMGUI",
                samples=480,colliderCount=FindObjectsOfType<Collider>().Length,rigidbodyCount=FindObjectsOfType<Rigidbody>().Length,
                scriptUpdateMs=Stats(script),renderSubmissionMs=Stats(render),frameIntervalMs=Stats(frame),
                surfaceMs=Stats(surfaceTimes),treesMs=Stats(treeTimes),debrisMs=Stats(debrisTimes),
                housesMs=Stats(houseTimes),otherScriptMs=Stats(otherTimes),
                surfaceStages=new SurfaceStageReport{
                    topologyMs=Stats(stageTimes[0]),soilVerticesMs=Stats(stageTimes[1]),
                    normalsMs=Stats(stageTimes[2]),strainMs=Stats(stageTimes[3]),
                    blockDeformationMs=Stats(stageTimes[4]),blockUploadMs=Stats(stageTimes[5]),
                    seamsMs=Stats(stageTimes[6]),boundaryWallsMs=Stats(stageTimes[7]),
                    scarpMs=Stats(stageTimes[8]),otherSurfaceMs=Stats(stageTimes[9]),
                    seamGeometryMs=Stats(stageTimes[10]),seamUploadMs=Stats(stageTimes[11]),
                    openSeams=Stats(stageTimes[12]),visibleSeams=Stats(stageTimes[13]),
                    totalSeams=Stats(stageTimes[14])}};
            File.WriteAllLines(Path.Combine(directory,"impact_frames.csv"),rows);
            File.WriteAllLines(Path.Combine(directory,"impact_component_frames.csv"),componentRows);
            File.WriteAllLines(Path.Combine(directory,"impact_surface_stages.csv"),stageRows);
            File.WriteAllText(Path.Combine(directory,"impact_summary.json"),JsonUtility.ToJson(report,true));
            view.targetTexture=null;target.Release();Destroy(target);
            UnityEngine.Debug.Log("IMPACT_BENCHMARK_OK samples=480 scriptP95="+report.scriptUpdateMs.p95.ToString("F3"));
            Application.Quit(0);
        }
    }
}
