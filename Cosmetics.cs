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
            ClearBlockers(canvas.position);
            if (!FindSpot(canvas, out var spot)) return "Could not find a safe spot in the cosmetics room";

            _returnPos = fpc.transform.position;
            _returnRot = fpc.transform.rotation;
            var look = canvas.position - spot; look.y = 0f;
            Teleport(fpc, spot, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : fpc.transform.rotation);
            Plugin.Log.LogInfo($"Cosmetics room: canvas {canvas.position}, player to {spot}");
            return $"Cosmetics room! Press {Plugin.CosmeticsKey.Value} or the button again to go back";
        }

        /// <summary>The cosmetics room has an elephant that blocks the way out: hide it (locally).</summary>
        public static void ClearBlockers(Vector3 near)
        {
            foreach (var anim in Resources.FindObjectsOfTypeAll<Animator>())
            {
                if (anim == null || !anim.gameObject.scene.IsValid() || !anim.gameObject.activeInHierarchy) continue;
                bool elephant = anim.name.IndexOf("elephant", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                (anim.runtimeAnimatorController != null && anim.runtimeAnimatorController.name.IndexOf("Elephant", System.StringComparison.OrdinalIgnoreCase) >= 0) ||
                                anim.transform.Find("ELEPHANT_") != null || anim.transform.Find("root/ELEPHANT_") != null;
                if (!elephant || Vector3.Distance(anim.transform.position, near) > 30f) continue;

                // Its blocking collider is a separate object: remember where the elephant was.
                var area = new Bounds(anim.transform.position, Vector3.one * 0.5f);
                foreach (var r in anim.GetComponentsInChildren<Renderer>()) area.Encapsulate(r.bounds);
                Blocked.Add(area);

                if (anim.GetComponentInParent<Mirror.NetworkIdentity>() != null)
                {
                    foreach (var c in anim.GetComponentsInChildren<Collider>()) c.enabled = false;
                    foreach (var r in anim.GetComponentsInChildren<Renderer>()) r.enabled = false;
                }
                else anim.gameObject.SetActive(false);
                Plugin.Log.LogInfo($"Removed blocking elephant '{anim.name}' at {anim.transform.position}");
            }
            ClearInvisibleBarriers();
        }

        private static readonly System.Collections.Generic.List<Bounds> Blocked = new System.Collections.Generic.List<Bounds>();

        /// <summary>Disable elephant-sized colliders where an elephant stood (not floors or walls).</summary>
        private static void ClearInvisibleBarriers()
        {
            if (Blocked.Count == 0) return;
            foreach (var area in Blocked)
            {
                var grown = area; grown.Expand(0.6f);
                foreach (var c in Physics.OverlapBox(grown.center, grown.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (c == null || !c.enabled) continue;
                    var b = c.bounds;
                    bool structural = b.size.x > Mathf.Max(4f, area.size.x * 1.6f) || b.size.z > Mathf.Max(4f, area.size.z * 1.6f) ||
                                      b.size.y > Mathf.Max(4f, area.size.y * 1.6f) || b.size.y < 0.05f;
                    if (structural) continue;
                    if (c.GetComponentInParent<PlayerNetwork>() != null || c.GetComponentInParent<NPC_Info>() != null) continue;
                    if (!grown.Contains(b.center)) continue;
                    c.enabled = false;
                    Plugin.Log.LogInfo($"Removed invisible barrier '{c.name}' ({b.size}) at {b.center}");
                }
            }
            Blocked.Clear();
        }

        private static float _nextBlockerCheck;

        /// <summary>Also clear it if someone walks into the room the normal way.</summary>
        public static void Tick()
        {
            if (GameData.Instance == null || Time.unscaledTime < _nextBlockerCheck) return;
            _nextBlockerCheck = Time.unscaledTime + 3f;
            var fpc = FirstPersonController.Instance;
            if (fpc == null) return;
            foreach (var r in Resources.FindObjectsOfTypeAll<TheCoolRoom>())
                if (r.gameObject.scene.IsValid() && r.skinCanvasOBJ != null &&
                    Vector3.Distance(fpc.transform.position, r.skinCanvasOBJ.transform.position) < 20f)
                    ClearBlockers(r.skinCanvasOBJ.transform.position);
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
