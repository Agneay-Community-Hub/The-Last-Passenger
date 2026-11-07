using System.Collections.Generic;
using UnityEngine;

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
}
