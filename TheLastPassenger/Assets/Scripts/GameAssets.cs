using System.Collections.Generic;
using UnityEngine;

namespace TLP
{
    /// <summary>Imported models, the character animator and extra fonts, registered by the editor builder.</summary>
    public class GameAssets : MonoBehaviour
    {
        public GameObject[] models;
        public RuntimeAnimatorController animator;
        public Font tamil;
        static GameAssets inst;
        static GameAssets I { get { if (!inst) inst = Object.FindAnyObjectByType<GameAssets>(); return inst; } }
        void Awake() { inst = this; }
        public static GameObject Model(string n)
        {
            if (!I || I.models == null) return null;
            foreach (var m in I.models) if (m && m.name == n) return m;
            return null;
        }
        public static RuntimeAnimatorController Animator => I ? I.animator : null;
        public static Font Tamil => I ? I.tamil : null;
    }
}
