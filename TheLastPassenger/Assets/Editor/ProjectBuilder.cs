using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.Animations;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TLP.EditorTools
{
    /// <summary>
    /// Command-line project setup and builds. Example:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod TLP.EditorTools.ProjectBuilder.BuildAll
    /// </summary>
    public static class ProjectBuilder
    {
        const string MatDir = "Assets/Materials";
        const string ScenePath = "Assets/Scenes/BusRoute_Main.unity";

        [MenuItem("The Last Passenger/1. Setup Scene and Materials")]
        public static void Setup()
        {
            AssetSetup.ConfigureImports();
            var mats = CreateMaterials();
            CreateScene(mats);
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
            Debug.Log("[TLP] Setup complete: " + mats.Count + " materials.");
        }

        // ------------------------------------------------------------------ materials

        static Material Std(List<Material> list, string name, Color c, float smooth = 0.2f, float metal = 0f, Color? emission = null, int mode = 0)
        {
            var m = new Material(Shader.Find("Standard")) { name = name, color = c };
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", metal);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            if (mode == 2 || mode == 3)
            {
                m.SetFloat("_Mode", mode);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)(mode == 3 ? BlendMode.One : BlendMode.SrcAlpha));
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                if (mode == 2) m.EnableKeyword("_ALPHABLEND_ON"); else m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 3000;
            }
            m.enableInstancing = true;
            return Save(list, m);
        }

        static Material Save(List<Material> list, Material m)
        {
            string path = MatDir + "/" + m.name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing)
            {
                existing.shader = m.shader;
                existing.CopyPropertiesFromMaterial(m);
                existing.shaderKeywords = m.shaderKeywords;
                existing.renderQueue = m.renderQueue;
                existing.globalIlluminationFlags = m.globalIlluminationFlags;
                EditorUtility.SetDirty(existing);
                list.Add(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(m, path);
            list.Add(m);
            return m;
        }

        static Color C(float r, float g, float b, float a = 1) => new Color(r, g, b, a);

        static List<Material> CreateMaterials()
        {
            Directory.CreateDirectory(MatDir);
            var L = new List<Material>();
            // road & city
            Std(L, "Road", C(0.05f, 0.05f, 0.055f), 0.78f);
            Std(L, "Concrete", C(0.3f, 0.29f, 0.27f), 0.12f);
            Std(L, "Kerb", C(0.42f, 0.4f, 0.37f), 0.1f);
            Std(L, "KerbStripe", C(0.62f, 0.55f, 0.12f), 0.2f);
            Std(L, "LaneLine", C(0.62f, 0.62f, 0.56f), 0.4f);
            Std(L, "Building1", C(0.24f, 0.21f, 0.19f), 0.08f);
            Std(L, "Building2", C(0.29f, 0.26f, 0.21f), 0.08f);
            Std(L, "Building3", C(0.2f, 0.24f, 0.25f), 0.08f);
            Std(L, "Building4", C(0.32f, 0.27f, 0.3f), 0.08f);
            Std(L, "WindowLit", C(0.3f, 0.2f, 0.1f), 0.5f, 0, C(1.3f, 0.85f, 0.42f));
            Std(L, "WindowCool", C(0.2f, 0.25f, 0.3f), 0.5f, 0, C(0.55f, 0.8f, 0.95f));
            Std(L, "WindowDark", C(0.025f, 0.028f, 0.035f), 0.9f);
            Std(L, "Shutter", C(0.32f, 0.33f, 0.35f), 0.45f, 0.5f);
            Std(L, "Sodium", C(0.4f, 0.25f, 0.1f), 0.3f, 0, C(3f, 1.7f, 0.55f));
            Std(L, "Pole", C(0.17f, 0.17f, 0.18f), 0.4f, 0.4f);
            Std(L, "HospitalWall", C(0.62f, 0.62f, 0.57f), 0.1f);
            Std(L, "BlueGate", C(0.08f, 0.2f, 0.5f), 0.35f, 0.3f);
            Std(L, "Poster", C(0.55f, 0.22f, 0.14f), 0.1f);
            Std(L, "PosterCrash", C(0.22f, 0.22f, 0.21f), 0.1f);
            Std(L, "Water", C(0.015f, 0.03f, 0.04f), 0.96f);
            Std(L, "Barrier", C(0.72f, 0.58f, 0.08f), 0.3f);
            Std(L, "BarrierRed", C(0.62f, 0.07f, 0.05f), 0.3f, 0, C(0.25f, 0.01f, 0.0f));
            // bus
            Std(L, "BusCream", C(0.74f, 0.68f, 0.54f), 0.35f);
            Std(L, "BusMaroon", C(0.38f, 0.07f, 0.09f), 0.45f);
            Std(L, "Interior", C(0.5f, 0.58f, 0.52f), 0.3f);
            Std(L, "Ceiling", C(0.62f, 0.66f, 0.6f), 0.2f);
            Std(L, "Floor", C(0.17f, 0.15f, 0.13f), 0.25f);
            Std(L, "Seat", C(0.36f, 0.07f, 0.08f), 0.6f);
            Std(L, "SeatFrame", C(0.22f, 0.22f, 0.24f), 0.55f, 0.6f);
            Std(L, "Rail", C(0.72f, 0.72f, 0.74f), 0.75f, 0.9f);
            Std(L, "Glass", C(0.04f, 0.06f, 0.07f, 0.12f), 0.55f, 0, null, 3);
            Std(L, "TubeOn", C(0.6f, 0.75f, 0.7f), 0.3f, 0, C(1.8f, 2.4f, 2.1f));
            Std(L, "TubeOff", C(0.25f, 0.27f, 0.26f), 0.3f);
            Std(L, "Dash", C(0.1f, 0.1f, 0.11f), 0.35f);
            Std(L, "Black", C(0.015f, 0.015f, 0.015f), 0.2f);
            Std(L, "Rubber", C(0.045f, 0.045f, 0.045f), 0.15f);
            Std(L, "MirrorFrame", C(0.06f, 0.06f, 0.06f), 0.4f);
            Std(L, "Headlight", C(0.9f, 0.9f, 0.8f), 0.8f, 0, C(2f, 1.85f, 1.5f));
            Std(L, "Taillight", C(0.4f, 0.02f, 0.02f), 0.6f, 0, C(1.6f, 0.05f, 0.02f));
            Std(L, "Towel", C(0.82f, 0.82f, 0.77f), 0.05f);
            Std(L, "Paper", C(0.85f, 0.82f, 0.7f), 0.05f);
            Std(L, "Wood", C(0.33f, 0.21f, 0.11f), 0.2f);
            Std(L, "Steel", C(0.62f, 0.62f, 0.62f), 0.7f, 0.9f);
            Std(L, "Badge", C(0.75f, 0.58f, 0.22f), 0.7f, 0.9f);
            Std(L, "Phone", C(0.04f, 0.04f, 0.05f), 0.8f);
            Std(L, "PhoneScreen", C(0.1f, 0.15f, 0.2f), 0.9f, 0, C(0.45f, 0.65f, 0.9f));
            Std(L, "Wet", C(0.04f, 0.06f, 0.07f), 0.97f);
            Std(L, "Handprint", C(0.55f, 0.6f, 0.65f, 0.4f), 0.9f, 0, null, 2);
            Std(L, "Windmill", C(0.85f, 0.2f, 0.28f), 0.2f);
            // people
            Std(L, "Skin", C(0.42f, 0.28f, 0.2f), 0.35f);
            Std(L, "SkinDark", C(0.32f, 0.21f, 0.15f), 0.35f);
            Std(L, "Hair", C(0.025f, 0.022f, 0.02f), 0.5f);
            Std(L, "HairGrey", C(0.55f, 0.55f, 0.53f), 0.3f);
            Std(L, "Meena", C(0.08f, 0.36f, 0.38f), 0.25f);
            Std(L, "Selvam", C(0.72f, 0.7f, 0.63f), 0.15f);
            Std(L, "Kavin", C(0.17f, 0.19f, 0.32f), 0.15f);
            Std(L, "Jeans", C(0.12f, 0.16f, 0.25f), 0.15f);
            Std(L, "Lakshmi", C(0.5f, 0.3f, 0.05f), 0.3f);
            Std(L, "Bala", C(0.8f, 0.82f, 0.85f), 0.15f);
            Std(L, "BalaShorts", C(0.08f, 0.1f, 0.28f), 0.15f);
            Std(L, "Khaki", C(0.5f, 0.45f, 0.3f), 0.15f);
            Std(L, "Ghost", C(0.01f, 0.01f, 0.012f), 0.6f);

            var mirror = new Material(Shader.Find("Unlit/Texture")) { name = "Mirror" };
            Save(L, mirror);
            var text = new Material(Shader.Find("TLP/Text3D")) { name = "Text" };
            Save(L, text);
            var rainShader = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Standard Unlit");
            var rain = new Material(rainShader) { name = "Rain" };
            if (rain.HasProperty("_TintColor")) rain.SetColor("_TintColor", C(0.5f, 0.55f, 0.6f, 0.35f));
            rain.color = C(0.7f, 0.75f, 0.8f, 0.35f);
            Save(L, rain);
            ApplyTextures(L);
            AssetDatabase.SaveAssets();
            return L;
        }

        // ------------------------------------------------------------------ textures (ambientCG, CC0)

        static void Tex(List<Material> L, string mat, string tex, float metresPerTile, float bump = 1f, Color? tint = null)
        {
            var m = L.FirstOrDefault(x => x.name == mat);
            var c = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetSetup.TexDir + "/" + tex + "_Color.jpg");
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetSetup.TexDir + "/" + tex + "_Normal.jpg");
            if (!m || !c) { Debug.LogWarning("[TLP] texture missing for " + mat + " / " + tex); return; }
            m.mainTexture = c;
            if (tint.HasValue) m.color = tint.Value;
            m.mainTextureScale = Vector2.one / metresPerTile;
            if (n)
            {
                m.SetTexture("_BumpMap", n);
                m.SetFloat("_BumpScale", bump);
                m.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(m);
        }

        static void ApplyTextures(List<Material> L)
        {
            Tex(L, "Road", "Asphalt025B", 3f, 1.2f, C(0.42f, 0.42f, 0.45f));
            Tex(L, "Concrete", "Concrete034", 2.5f, 1f, C(0.5f, 0.49f, 0.46f));
            Tex(L, "Kerb", "Concrete034", 1.5f, 1f, C(0.6f, 0.58f, 0.55f));
            Tex(L, "KerbStripe", "Concrete034", 1.5f, 1f, C(0.75f, 0.66f, 0.2f));
            foreach (var b in new[] { "Building1", "Building2", "Building3", "Building4", "HospitalWall" }) Tex(L, b, "Plaster001", 3f);
            Tex(L, "Shutter", "CorrugatedSteel009", 1.2f, 1f, C(0.7f, 0.7f, 0.72f));
            Tex(L, "BusCream", "PaintedMetal004", 1.2f, 0.6f, C(0.95f, 0.88f, 0.7f));
            Tex(L, "BusMaroon", "PaintedMetal004", 1.2f, 0.6f, C(0.55f, 0.1f, 0.12f));
            Tex(L, "Interior", "Plaster001", 1.2f, 0.4f, C(0.55f, 0.66f, 0.58f));
            Tex(L, "Dash", "PaintedMetal004", 0.8f, 0.5f, C(0.16f, 0.16f, 0.17f));
            Tex(L, "Barrier", "PaintedMetal004", 1.0f, 0.6f);
            Tex(L, "Ceiling", "Plaster001", 1.5f, 0.5f, C(0.7f, 0.74f, 0.68f));
            Tex(L, "Seat", "Leather033C", 0.6f, 1f, C(0.55f, 0.1f, 0.11f));
            Tex(L, "Floor", "Rubber001", 0.8f, 1f, C(0.75f, 0.72f, 0.68f));
            Tex(L, "Wood", "Fabric085", 0.3f, 1f, C(0.5f, 0.33f, 0.18f));
            Tex(L, "Towel", "Fabric085", 0.25f);
        }

        // ------------------------------------------------------------------ animation

        static RuntimeAnimatorController CreateAnimator()
        {
            const string path = "Assets/Animation/Passenger.controller";
            Directory.CreateDirectory("Assets/Animation");
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing && existing.layers.Length > 0 && existing.layers[0].stateMachine.states.Length >= 6) return existing; // keep its GUID stable
            AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Pose", AnimatorControllerParameterType.Int);
            var sm = ctrl.layers[0].stateMachine;
            var clips = AssetSetup.Clips();
            string[] names = { "Idle_Loop", "Walk_Loop", "Sitting_Idle_Loop", "Sitting_Talking_Loop", "Idle_Talking_Loop", "Driving_Loop" };
            for (int i = 0; i < names.Length; i++)
            {
                var clip = clips.FirstOrDefault(c => c.name.EndsWith("|" + names[i]) || c.name == names[i]);
                if (!clip) { Debug.LogWarning("[TLP] missing clip " + names[i]); continue; }
                var st = sm.AddState(names[i]);
                st.motion = clip;
                if (i == 0) sm.defaultState = st;
                var tr = sm.AddAnyStateTransition(st);
                tr.AddCondition(AnimatorConditionMode.Equals, i, "Pose");
                tr.duration = i == 1 ? 0.15f : 0.3f;
                tr.hasExitTime = false;
                tr.canTransitionToSelf = false;
            }
            AssetDatabase.SaveAssets();
            return ctrl;
        }

        // ------------------------------------------------------------------ scene

        static void CreateScene(List<Material> mats)
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.025f, 0.03f, 0.042f);
            RenderSettings.fogDensity = 0.028f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.075f, 0.09f);
            RenderSettings.skybox = null;
            Lightmapping.bakedGI = false;
            Lightmapping.realtimeGI = false;

            var game = new GameObject("Game");
            var m = game.AddComponent<Mats>();
            m.list = mats.ToArray();
            var ga = game.AddComponent<GameAssets>();
            ga.models = Directory.GetFiles(AssetSetup.CharDir, "*.fbx").Concat(Directory.GetFiles(AssetSetup.KenneyDir, "*.fbx"))
                .Select(p => AssetDatabase.LoadAssetAtPath<GameObject>(p.Replace('\\', '/'))).Where(g => g).ToArray();
            ga.animator = CreateAnimator();
            ga.tamil = AssetDatabase.LoadAssetAtPath<Font>(AssetSetup.Root + "/Fonts/NotoSansTamil-Bold.ttf");
            game.AddComponent<GameBoot>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Agneay B Nair";
            PlayerSettings.productName = "The Last Passenger";
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true; // itch.io serves files without Content-Encoding headers
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.show = false;
        }

        // ------------------------------------------------------------------ builds

        static void Build(BuildTarget target, string path, BuildOptions opts)
        {
            Setup();
            // Building over an existing player produced a corrupt level0, so always start from an empty folder.
            var outDir = target == BuildTarget.WebGL ? path : Path.GetDirectoryName(path);
            if (Directory.Exists(outDir)) FileUtil.DeleteFileOrDirectory(outDir);
            // A stale cached level0 (the scene is regenerated on every build) caused "level0 is corrupted" crashes.
            foreach (var cache in new[] { "Library/PlayerDataCache", "Library/Bee/artifacts", "Library/BuildPlayerData" })
                if (Directory.Exists(cache)) FileUtil.DeleteFileOrDirectory(cache);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = target,
                options = opts | BuildOptions.CleanBuildCache, // incremental builds produced a corrupt level0
            });
            var s = report.summary;
            Debug.Log("[TLP] Build " + target + " -> " + path + " : " + s.result + ", " + (s.totalSize / (1024 * 1024)) + " MB, " + s.totalErrors + " errors, " + s.totalTime);
            if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }

        [MenuItem("The Last Passenger/Build Windows")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "../Builds/Windows/TheLastPassenger.exe", BuildOptions.None);

        [MenuItem("The Last Passenger/Build Windows (Development, test bot)")]
        public static void BuildWindowsDev() => Build(BuildTarget.StandaloneWindows64, "../Builds/WindowsDev/TheLastPassenger.exe", BuildOptions.Development);

        [MenuItem("The Last Passenger/Build WebGL")]
        public static void BuildWebGL() => Build(BuildTarget.WebGL, "../Builds/WebGL", BuildOptions.None);

        public static void BuildAll()
        {
            BuildWindows();
            BuildWebGL();
        }
    }
}
