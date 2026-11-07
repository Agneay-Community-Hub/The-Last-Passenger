using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TLP
{
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
            // Textured boxes get UVs in metres so materials tile at real-world scale instead of stretching.
            if (t == PrimitiveType.Cube && r.sharedMaterial && r.sharedMaterial.mainTexture)
                g.GetComponent<MeshFilter>().sharedMesh = BoxMesh(g.transform.lossyScale);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return g;
        }

        static Mesh unitCube;
        static readonly Dictionary<Vector3Int, Mesh> boxMeshes = new Dictionary<Vector3Int, Mesh>();

        /// <summary>A cube whose UVs are measured in metres for the given scale (cached by size, 5 cm steps).</summary>
        public static Mesh BoxMesh(Vector3 scale)
        {
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            var key = new Vector3Int(Mathf.RoundToInt(scale.x * 20), Mathf.RoundToInt(scale.y * 20), Mathf.RoundToInt(scale.z * 20));
            if (boxMeshes.TryGetValue(key, out var m) && m) return m;
            if (!unitCube)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                unitCube = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(tmp);
            }
            m = Object.Instantiate(unitCube);
            m.name = "Box" + key;
            var v = m.vertices; var n = m.normals; var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var w = Vector3.Scale(v[i], scale);
                var an = new Vector3(Mathf.Abs(n[i].x), Mathf.Abs(n[i].y), Mathf.Abs(n[i].z));
                if (an.x > 0.5f) uv[i] = new Vector2(w.z * Mathf.Sign(n[i].x), w.y);
                else if (an.y > 0.5f) uv[i] = new Vector2(w.x, w.z * Mathf.Sign(n[i].y));
                else uv[i] = new Vector2(-w.x * Mathf.Sign(n[i].z), w.y);
            }
            m.uv = uv;
            m.RecalculateTangents();
            boxMeshes[key] = m;
            return m;
        }

        /// <summary>Places an imported model so its bounds sit centred on 'pos' with the bottom on the ground.</summary>
        public static GameObject Model(Transform parent, string model, Vector3 pos, float scale, float yaw = 0)
        {
            var prefab = GameAssets.Model(model);
            var pivot = Empty(parent, "M_" + model, pos, new Vector3(0, yaw, 0));
            if (!prefab) return pivot;
            var g = Object.Instantiate(prefab, pivot.transform, false);
            g.transform.localScale = Vector3.one * scale;
            var rends = g.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) { b.Encapsulate(r.bounds); r.shadowCastingMode = ShadowCastingMode.Off; }
                var localCenter = pivot.transform.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
                g.transform.localPosition -= localCenter;
            }
            return pivot;
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
        public bool marker = true;

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
