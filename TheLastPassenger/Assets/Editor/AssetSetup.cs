using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TLP.EditorTools
{
    /// <summary>Import settings for the third-party packs, plus a dev-time inspector that renders previews.</summary>
    public static class AssetSetup
    {
        public const string Root = "Assets/ThirdParty";
        public const string CharDir = Root + "/Quaternius/Characters";
        public const string AnimFbx = Root + "/Quaternius/Animations/UAL1_Standard.fbx";
        public const string KenneyDir = Root + "/Kenney";
        public const string TexDir = Root + "/ambientCG";

        public static void ConfigureImports()
        {
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(CharDir, "*.fbx"))
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                bool dirty = mi.animationType != ModelImporterAnimationType.Human || !mi.bakeAxisConversion || mi.importAnimation;
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.bakeAxisConversion = true;
                mi.importAnimation = false;
                mi.importCameras = false; mi.importLights = false;
                if (dirty) mi.SaveAndReimport();
            }

            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(AnimFbx);
                bool first = mi.animationType != ModelImporterAnimationType.Human || !mi.bakeAxisConversion;
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.bakeAxisConversion = true;
                mi.importAnimation = true;
                mi.importCameras = false; mi.importLights = false;
                if (first) mi.SaveAndReimport();
                var clips = mi.defaultClipAnimations;
                foreach (var c in clips)
                {
                    bool loop = c.name.EndsWith("_Loop") || c.name.ToLower().Contains("idle") || c.name.ToLower().Contains("walk") || c.name.ToLower().Contains("sit");
                    c.loopTime = loop;
                    c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true;
                    c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
                }
                bool already = mi.clipAnimations != null && mi.clipAnimations.Length == clips.Length && mi.clipAnimations.Length > 0;
                if (!already) { mi.clipAnimations = clips; mi.SaveAndReimport(); }
            }

            foreach (var path in Directory.GetFiles(KenneyDir, "*.fbx"))
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                if (mi.animationType == ModelImporterAnimationType.None && !mi.importAnimation) continue;
                mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
                mi.importCameras = false; mi.importLights = false;
                mi.SaveAndReimport();
            }

            foreach (var path in Directory.GetFiles(TexDir, "*.jpg"))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                bool normal = path.EndsWith("_Normal.jpg");
                if (ti.maxTextureSize == 1024 && ti.textureType == (normal ? TextureImporterType.NormalMap : TextureImporterType.Default)) continue;
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.maxTextureSize = 1024;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.mipmapEnabled = true;
                ti.anisoLevel = 4;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
        }

        public static AnimationClip[] Clips() =>
            AssetDatabase.LoadAllAssetsAtPath(AnimFbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();

        /// <summary>Dev aid: dump clip names, materials and bounds, and render previews of every model.</summary>
        public static void Inspect()
        {
            ConfigureImports();
            var log = new List<string>();
            log.Add("CLIPS: " + string.Join(", ", Clips().Select(c => c.name + "(" + c.length.ToString("0.0") + ")")));
            string outDir = System.Environment.GetEnvironmentVariable("TLP_PREVIEW_DIR") ?? "../preview";
            Directory.CreateDirectory(outDir);

            var models = Directory.GetFiles(CharDir, "*.fbx").Concat(Directory.GetFiles(KenneyDir, "*.fbx")).Select(p => p.Replace('\\', '/')).ToList();
            var rt = new RenderTexture(400, 600, 24);
            var camGo = new GameObject("PreviewCam"); var cam = camGo.AddComponent<Camera>();
            cam.targetTexture = rt; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.3f, 0.3f, 0.35f);
            var lightGo = new GameObject("PreviewLight"); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(30, 150, 0);
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
            foreach (var path in models)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var go = Object.Instantiate(prefab);
                var rends = go.GetComponentsInChildren<Renderer>();
                var b = rends.Length > 0 ? rends[0].bounds : new Bounds();
                foreach (var r in rends) b.Encapsulate(r.bounds);
                var mats = rends.SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.name + (m.mainTexture ? "[tex " + m.mainTexture.name + "]" : "") + " " + ColorUtility.ToHtmlStringRGB(m.color)).Distinct();
                var anim = go.GetComponent<Animator>();
                log.Add(Path.GetFileName(path) + "  size=" + b.size.ToString("0.00") + " center=" + b.center.ToString("0.00") + " humanoid=" + (anim && anim.avatar && anim.avatar.isHuman) + "  mats: " + string.Join(" | ", mats));
                float h = Mathf.Max(b.size.y, b.size.x * 0.8f);
                cam.transform.position = b.center + new Vector3(0, 0, h * 1.6f);
                cam.transform.LookAt(b.center);
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(400, 600, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 400, 600), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".png"), tex.EncodeToPNG());
                Object.DestroyImmediate(go);
            }
            File.WriteAllLines(Path.Combine(outDir, "inspect.txt"), log);
            Debug.Log("[TLP] Inspect done");
        }
    }
}
