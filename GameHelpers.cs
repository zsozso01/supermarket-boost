using HarmonyLib;
using StarterAssets;
using UnityEngine;

namespace SupermarketBoost
{
    internal static class GameHelpers
    {
        private static PlayerNetwork _local;
        private static float _nextLookup;
        private static readonly AccessTools.FieldRef<PlayerNetwork, Rewired.Player> MainPlayerRef =
            AccessTools.FieldRefAccess<PlayerNetwork, Rewired.Player>("MainPlayer");

        public static PlayerNetwork LocalPlayer
        {
            get
            {
                if (_local != null) return _local;
                if (Time.unscaledTime < _nextLookup) return null;
                _nextLookup = Time.unscaledTime + 1f;
                foreach (var p in Object.FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None))
                    if (p.isLocalPlayer) return _local = p;
                return null;
            }
        }

        /// <summary>True when the local player is walking around (no menu, chat or computer open).</summary>
        public static bool InputAllowed =>
            FirstPersonController.Instance != null && FirstPersonController.Instance.allowPlayerInput;

        /// <summary>The game's own "Main Action" (left click / controller button) for the local player.</summary>
        public static bool MainActionDown()
        {
            var p = LocalPlayer;
            if (p == null) return false;
            var rp = MainPlayerRef(p);
            return rp != null && rp.GetButtonDown("Main Action");
        }

        public static bool IsHost => GameData.Instance != null && GameData.Instance.isServer;

        private static Shader _lit;

        public static Material MakeMaterial(Color color, float smoothness = 0.4f, Color? emission = null)
        {
            if (_lit == null)
                _lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(_lit);
            m.color = color;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
            }
            return m;
        }

        public static GameObject Box(Transform parent, Vector3 localPos, Vector3 scale, Material mat, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!keepCollider) Object.Destroy(go.GetComponent<Collider>());
            return go;
        }
    }
}
