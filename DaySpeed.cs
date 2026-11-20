using UnityEngine;

namespace SupermarketBoost
{
    /// <summary>
    /// Game speed (Time.timeScale) for the whole simulation: customers, NPCs, machines and the clock.
    /// The host picks it; it is sent to every modded player so animations match. Player movement is
    /// kept at real-time speed by PlayerRealtimePatch. Resets to x1 when the store closes.
    /// Also owns the office buttons (game speed + cosmetics room) above the open-store switch.
    /// </summary>
    internal static class DaySpeed
    {
        public static float Speed = 1f;
        private static int _index;
        private static bool _applied;

        private static WallButton _speedBtn, _cosmeticsBtn;
        private static float _nextSearch;
        private static Plugin _plugin;

        public static string Cycle()
        {
            if (GameData.Instance == null) return null;
            if (!GameHelpers.IsHost) return "Only the host can change the game speed";
            if (!GameData.Instance.isSupermarketOpen) return "Open the store first";

            var steps = Plugin.Steps;
            _index = (_index + 1) % steps.Length;
            SetSpeed(steps[_index]);
            return Speed <= 1f ? "Game speed: normal" : $"Game speed: x{Speed:0.#}";
        }

        /// <summary>Host: set and broadcast. Clients: called from the network message.</summary>
        public static void SetSpeed(float speed)
        {
            Speed = Mathf.Clamp(speed, 1f, 8f);
            if (Speed <= 1f) _index = 0;
            if (GameHelpers.IsHost) Net.BroadcastSpeed(Speed);
            RefreshLabel();
        }

        public static void Tick(Plugin plugin)
        {
            _plugin = plugin;
            var gd = GameData.Instance;

            // Host: drop back to normal when the day ends.
            if (Speed > 1f && GameHelpers.IsHost && (gd == null || !gd.isSupermarketOpen)) SetSpeed(1f);
            if (gd == null && Speed > 1f) Speed = 1f;

            // Apply on every client (the game's own night fast-forward also uses timeScale; leave that alone).
            if (Speed > 1f && gd != null && gd.isSupermarketOpen)
            {
                Time.timeScale = Speed;
                _applied = true;
            }
            else if (_applied)
            {
                Time.timeScale = 1f;
                _applied = false;
            }

            if (Plugin.SpeedButtonEnabled.Value) TickButtons();
        }

        public static bool PlayerShouldBeRealtime => _applied && Plugin.PlayersAtNormalSpeed.Value;

        private static void TickButtons()
        {
            // Only in a real game (the main menu has its own background store), and rebuild
            // whenever the switch object changes (new scene / new session).
            if (GameData.Instance == null) return;
            if (Time.unscaledTime > _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 2f;
                var sw = FindSwitch();
                if (sw != null && (sw != _builtFor || _speedBtn?.Root == null))
                {
                    if (_speedBtn?.Root != null) Object.Destroy(_speedBtn.Root);
                    if (_cosmeticsBtn?.Root != null) Object.Destroy(_cosmeticsBtn.Root);
                    BuildButtons(sw);
                }
            }
            if (_speedBtn?.Root == null) return;

            _speedBtn.Animate();
            _cosmeticsBtn?.Animate();

            if (!GameHelpers.InputAllowed || !GameHelpers.MainActionDown()) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 4f, ~0, QueryTriggerInteraction.Collide)) return;
            if (_speedBtn.Owns(hit.collider)) _speedBtn.Press();
            else if (_cosmeticsBtn != null && _cosmeticsBtn.Owns(hit.collider)) _cosmeticsBtn.Press();
        }

        private static Transform FindSwitch()
        {
            var go = GameObject.Find("OpenSupermarketSwitch");
            if (go != null) return go.transform;
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t.name == "OpenSupermarketSwitch" && t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy)
                    return t;
            return null;
        }

        private static Transform _builtFor;

        private static void BuildButtons(Transform sw)
        {
            _builtFor = sw;

            // Measure the whole switch box (its own renderers and colliders) to sit clearly above it.
            var bounds = new Bounds(sw.position, Vector3.zero);
            foreach (var r in sw.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            foreach (var c in sw.GetComponentsInChildren<Collider>()) bounds.Encapsulate(c.bounds);
            float top = Mathf.Max(bounds.max.y, sw.position.y + 0.25f);

            Vector3 basePos = new Vector3(sw.position.x, top + 0.17f, sw.position.z);
            int layer = sw.gameObject.layer;

            _speedBtn = WallButton.Create("SupermarketBoost_SpeedButton", basePos, sw.rotation, layer,
                "GAME SPEED x1", new Color(0.95f, 0.65f, 0.1f), () => _plugin?.Toast(Cycle()));
            _cosmeticsBtn = WallButton.Create("SupermarketBoost_CosmeticsButton", basePos + Vector3.up * 0.17f, sw.rotation, layer,
                "COSMETICS ROOM", new Color(0.75f, 0.35f, 0.95f), () => _plugin?.Toast(Cosmetics.Toggle()));
            // Parent to the switch's parent so they live and die with the store scene.
            _speedBtn.Root.transform.SetParent(sw.parent, true);
            _cosmeticsBtn.Root.transform.SetParent(sw.parent, true);
            RefreshLabel();
            Plugin.Log.LogInfo($"Office buttons placed at {basePos} (switch bounds {bounds})");
        }

        private static void RefreshLabel()
        {
            if (_speedBtn == null) return;
            _speedBtn.SetLabel(Speed <= 1f ? "GAME SPEED x1" : $"GAME SPEED x{Speed:0.#}");
            _speedBtn.SetColor(Speed > 1f ? new Color(0.1f, 0.9f, 0.4f) : new Color(0.95f, 0.65f, 0.1f));
        }
    }
}
