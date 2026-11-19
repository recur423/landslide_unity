using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LandslideDemo.Editor
{
    /// <summary>Recreates the minimal entry scene and builds its generated runtime world.</summary>
    public static class BuildDemo
    {
        private const string ScenePath = "Assets/Scenes/LandslideDemo.unity";

        [MenuItem("Landslide Demo/Create Demo Scene")]
        public static void CreateScene()
        {
            ConfigureProject();
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scenes"));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Landslide Demo");
            var controller = root.AddComponent<SceneController>();
            controller.TerrainShader = RequireShader("Assets/Shaders/VertexTerrain.shader");
            controller.SoilShader = RequireShader("Assets/Shaders/FracturedSoil.shader");
            controller.StandardShader = Shader.Find("Standard");
            controller.ParticleShader = Shader.Find("Sprites/Default");
            if (controller.StandardShader == null || controller.ParticleShader == null)
                throw new InvalidOperationException("Required built-in rendering shaders were not found.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Unable to save demo scene at " + ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = root;
            Debug.Log("Landslide demo scene created: " + ScenePath);
        }

        [MenuItem("Landslide Demo/Build Windows Player")]
        public static void BuildWindows()
        {
            CreateScene();
            ValidateSurfacePresentation.Run();
            foreach (string shaderPath in new[]{"Assets/Resources/Shaders/OvercastSky.shader", "Assets/Resources/Shaders/PresentationGrade.shader", "Assets/Resources/Shaders/LeafCards.shader", "Assets/Resources/Shaders/RainCracks.shader", "Assets/Resources/Shaders/RainStreak.shader", "Assets/Resources/Shaders/LayeredSlip.shader", "Assets/Resources/Shaders/WatershedDetails.shader"})
            {
                Shader presentationShader=RequireShader(shaderPath);
                if(ShaderUtil.ShaderHasError(presentationShader))throw new InvalidOperationException("Presentation shader failed: "+shaderPath);
            }
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputDirectory = Path.GetFullPath(Path.Combine(projectRoot, "..", "LandslideUnityPlayer"));
            Directory.CreateDirectory(outputDirectory);
            string executable = Path.Combine(outputDirectory, "LandslideDemo.exe");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = executable,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report == null || report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
            {
                string result = report == null ? "no build report" :
                    report.summary.result + ", errors=" + report.summary.totalErrors;
                throw new InvalidOperationException("Windows player build failed: " + result);
            }
            Debug.Log("Windows player built successfully: " + executable +
                      " (" + report.summary.totalSize + " bytes)");
        }

        private static Shader RequireShader(string path)
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null) throw new FileNotFoundException("Required demo shader is missing: " + path);
            return shader;
        }

        private static void ConfigureProject()
        {
            PlayerSettings.companyName = "Landslide Physics Demo";
            PlayerSettings.productName = "Rainfall Landslide Simulation";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            GraphicsSettings.renderPipelineAsset = null;
            QualitySettings.renderPipeline = null;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowDistance = 600f;
            QualitySettings.shadowCascades = 4;
            QualitySettings.antiAliasing = 4;
            QualitySettings.pixelLightCount = 4;
            QualitySettings.vSyncCount = 1;
        }
    }
}
