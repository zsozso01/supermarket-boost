using StarterAssets;
using UnityEngine;

namespace SupermarketBoost
{
    /// <summary>
    /// The game's hidden skin/hat room (TheCoolRoom). The office button or F8 teleports you in;
    /// pressing again (anywhere) takes you back to where you were.
    /// </summary>
    internal static class Cosmetics
    {
        private static Vector3? _returnPos;
        private static Quaternion _returnRot;

        public static string Toggle()
        {
            var fpc = FirstPersonController.Instance;
            if (fpc == null) return null;

            if (_returnPos.HasValue)
            {
                Teleport(fpc, _returnPos.Value, _returnRot);
                _returnPos = null;
                return "Back to the store";
            }

            TheCoolRoom room = null;
            foreach (var r in Resources.FindObjectsOfTypeAll<TheCoolRoom>())
                if (r.gameObject.scene.IsValid()) { room = r; break; }
            if (room == null || room.skinCanvasOBJ == null) return "Cosmetics room not found on this map";

            var canvas = room.skinCanvasOBJ.transform;
            if (!FindSpot(canvas, out var spot)) return "Could not find a safe spot in the cosmetics room";

            _returnPos = fpc.transform.position;
            _returnRot = fpc.transform.rotation;
            var look = canvas.position - spot; look.y = 0f;
            Teleport(fpc, spot, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : fpc.transform.rotation);
            Plugin.Log.LogInfo($"Cosmetics room: canvas {canvas.position}, player to {spot}");
            return $"Cosmetics room! Press {Plugin.CosmeticsKey.Value} or the button again to go back";
        }

        public static bool ProbeSpot(Transform canvas, out Vector3 spot) => FindSpot(canvas, out spot);

        private static bool FindSpot(Transform canvas, out Vector3 spot)
        {
            // World-space UI is read from its -Z side, so try in front of it first.
            Vector3[] dirs = { -canvas.forward, canvas.forward, -canvas.right, canvas.right };
            foreach (var d in dirs)
                foreach (float dist in new[] { 1.4f, 2f, 1f })
                {
                    Vector3 probe = canvas.position + new Vector3(d.x, 0f, d.z).normalized * dist + Vector3.up * 0.5f;
                    if (!Physics.Raycast(probe, Vector3.down, out var floor, 3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    Vector3 feet = floor.point + Vector3.up * 0.05f;
                    bool blocked = Physics.CheckCapsule(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.5f, 0.3f, ~0, QueryTriggerInteraction.Ignore);
                    if (!blocked) { spot = feet; return true; }
                }
            spot = default;
            return false;
        }

        private static void Teleport(FirstPersonController fpc, Vector3 pos, Quaternion rot)
        {
            var cc = fpc.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            fpc.transform.SetPositionAndRotation(pos, rot);
            if (cc != null) cc.enabled = true;
        }
    }
}
