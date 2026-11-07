using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TLP
{
    /// <summary>Holds every material the game uses. Populated by the editor builder so the build includes their shaders.</summary>
    public class Mats : MonoBehaviour
    {
        public Material[] list;
        static Dictionary<string, Material> map;

        public static void Init(Material[] mats)
        {
            map = new Dictionary<string, Material>();
            if (mats == null) return;
            foreach (var m in mats) if (m) map[m.name] = m;
        }

        void Awake() { Init(list); }

        public static Material Get(string n)
        {
            if (map == null) { var m = Object.FindAnyObjectByType<Mats>(); Init(m ? m.list : null); }
            if (n != null && map.TryGetValue(n, out var mat)) return mat;
            Debug.LogWarning("[TLP] Missing material " + n);
            foreach (var kv in map) return kv.Value;
            return null;
        }
    }

    /// <summary>Anything the player can look at and press E on.</summary>
    public class Interactable : MonoBehaviour
    {
        public string prompt = "Inspect";
        public bool active = true;
        public Action onUse;
        public Func<string> promptFunc;
        public bool highlight; // story wants the player to notice this

        public string Prompt => promptFunc != null ? promptFunc() : prompt;
        public bool Usable => active && onUse != null && !string.IsNullOrEmpty(Prompt);

        public void Set(string p, Action use) { prompt = p; promptFunc = null; onUse = use; active = true; }
        public void Clear() { onUse = null; promptFunc = null; highlight = false; }
    }

    /// <summary>Tiny procedural-geometry helpers shared by the bus, passengers and roadside scenery.</summary>
    public static class B
    {
        public const int MirrorLayer = 8;   // seen only by the rear-view mirror camera
        static Font font;
        public static Font Font
        {
            get { if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); return font; }
        }

        public static GameObject Prim(PrimitiveType t, Transform parent, string name, Vector3 pos, Vector3 scale, string mat, bool collider = false, Vector3 euler = default)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localEulerAngles = euler;
            g.transform.localScale = scale;
            if (!collider)
            {
                var c = g.GetComponent<Collider>();
                if (c) Object.DestroyImmediate(c);
            }
            var r = g.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = Mats.Get(mat);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return g;
        }

        public static GameObject Box(Transform p, string n, Vector3 pos, Vector3 s, string mat, bool col = false, Vector3 e = default)
            => Prim(PrimitiveType.Cube, p, n, pos, s, mat, col, e);
        public static GameObject Cyl(Transform p, string n, Vector3 pos, Vector3 s, string mat, bool col = false, Vector3 e = default)
            => Prim(PrimitiveType.Cylinder, p, n, pos, s, mat, col, e);
        public static GameObject Sph(Transform p, string n, Vector3 pos, Vector3 s, string mat, bool col = false, Vector3 e = default)
            => Prim(PrimitiveType.Sphere, p, n, pos, s, mat, col, e);
        public static GameObject Cap(Transform p, string n, Vector3 pos, Vector3 s, string mat, bool col = false, Vector3 e = default)
            => Prim(PrimitiveType.Capsule, p, n, pos, s, mat, col, e);
        public static GameObject Quad(Transform p, string n, Vector3 pos, Vector3 s, string mat, Vector3 e = default)
            => Prim(PrimitiveType.Quad, p, n, pos, s, mat, false, e);

        /// <summary>A horizontal rail between two local points.</summary>
        public static GameObject Rail(Transform p, string n, Vector3 a, Vector3 b, float r, string mat)
        {
            var g = Cyl(p, n, (a + b) * 0.5f, new Vector3(r, (b - a).magnitude * 0.5f, r), mat);
            g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
            return g;
        }

        public static GameObject Empty(Transform p, string n, Vector3 pos, Vector3 e = default)
        {
            var g = new GameObject(n);
            g.transform.SetParent(p, false);
            g.transform.localPosition = pos;
            g.transform.localEulerAngles = e;
            return g;
        }

        /// <summary>World text. Readable by a viewer looking along the object's forward axis. height = rough letter height in metres.</summary>
        public static TextMesh Text(Transform p, string n, string text, Vector3 pos, Vector3 e, float height, Color c, TextAnchor a = TextAnchor.MiddleCenter)
        {
            var g = Empty(p, n, pos, e);
            var tm = g.AddComponent<TextMesh>();
            tm.font = Font;
            tm.fontSize = 64;
            tm.characterSize = height / (64f * 0.1f);
            tm.anchor = a;
            tm.alignment = a == TextAnchor.MiddleLeft || a == TextAnchor.UpperLeft || a == TextAnchor.LowerLeft ? TextAlignment.Left : TextAlignment.Center;
            tm.color = c;
            tm.text = text;
            var mr = g.GetComponent<MeshRenderer>();
            var m = Mats.Get("Text");
            if (m && Font) { m.mainTexture = Font.material.mainTexture; mr.sharedMaterial = m; }
            else if (Font) mr.sharedMaterial = Font.material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return tm;
        }

        public static Light PointLight(Transform p, string n, Vector3 pos, Color c, float intensity, float range)
        {
            var g = Empty(p, n, pos);
            var l = g.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        public static void Layer(GameObject g, int layer)
        {
            g.layer = layer;
            foreach (Transform t in g.transform) Layer(t.gameObject, layer);
        }

        public static void SetMat(GameObject g, string mat)
        {
            var m = Mats.Get(mat);
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
                if (!(r is MeshRenderer mr && mr.GetComponent<TextMesh>())) r.sharedMaterial = m;
        }
    }

    // ---------------------------------------------------------------- save data

    [Serializable]
    public class StoryFlags
    {
        public int chapter = 1;
        public int errors;
        public bool routeCard, wetHandle, meenaTicket, newspaper, phone, badge, logbook, finalTicket, balaTalk, driverTalk;
        public bool seenMirrorMeena, seenSilentMan;
    }

    [Serializable]
    public class ManifestRow
    {
        public string id;
        public string name;
        public string seat;
        public string dest;
        public int status;          // 0 = not marked, 1 = aboard, 2 = exited
        public bool remembered;
        public bool noteMirror, noteRear, noteUnknown;
    }

    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public StoryFlags flags = new StoryFlags();
        public List<ManifestRow> rows = new List<ManifestRow>();
        public bool gNoteMirror, gNoteRear, gNoteUnknown;
    }

    [Serializable]
    public class Settings
    {
        public float sensitivity = 2.0f;
        public float volume = 0.8f;
        public int subtitleSize = 1;    // 0 small, 1 medium, 2 large
        public bool invertY;
        public bool reduceMotion;
        public bool tutorial = true;

        public static Settings Load()
        {
            try
            {
                var s = PlayerPrefs.GetString("tlp_settings", "");
                if (!string.IsNullOrEmpty(s)) return JsonUtility.FromJson<Settings>(s);
            }
            catch { }
            return new Settings();
        }

        public void Save() { PlayerPrefs.SetString("tlp_settings", JsonUtility.ToJson(this)); PlayerPrefs.Save(); }
    }
}
