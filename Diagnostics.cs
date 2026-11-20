using UnityEngine;

namespace SupermarketBoost
{
    /// <summary>One-off, read-only checks written to the BepInEx log (Debug.DiagnosticsOnce).</summary>
    internal static class Diagnostics
    {
        private static float _start = -1f;

        public static void Tick()
        {
            if (!Plugin.DiagnosticsOnce.Value || NPC_Manager.Instance == null || GameData.Instance == null) return;
            if (_start < 0f) { _start = Time.unscaledTime; return; }
            if (Time.unscaledTime - _start < 5f) return;
            Plugin.DiagnosticsOnce.Value = false;


            foreach (var r in Resources.FindObjectsOfTypeAll<TheCoolRoom>())
            {
                if (!r.gameObject.scene.IsValid()) continue;
                var c = r.skinCanvasOBJ ? r.skinCanvasOBJ.transform : null;
                Plugin.Log.LogInfo($"Cosmetics room '{r.name}' active={r.gameObject.activeInHierarchy} at {r.transform.position}, " +
                                   $"canvas {(c ? c.position.ToString() : "none")} fwd {(c ? c.forward.ToString() : "-")}, " +
                                   $"spot found={(c != null && Cosmetics.ProbeSpot(c, out var s) ? s.ToString() : "no")}");
            }
            Plugin.Log.LogInfo("Diagnostics done");
        }
    }
}
