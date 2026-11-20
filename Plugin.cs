using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SupermarketBoost
{
    // Supermarket Together tweaks:
    //  - more customers, arriving more often (prices / wallets / basket sizes untouched)
    //  - fewer machine breakdowns
    //  - day speed: F6 or the button above the office's open-store switch (host)
    //  - pistol (G) that hits thieves like the broom does
    //  - self-update from GitHub releases
    // Settings: BepInEx/config/codescrambler.supermarkettogether.boost.cfg
    [BepInPlugin(Guid, "Supermarket Boost", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "codescrambler.supermarkettogether.boost";
        public const string Version = "1.4.1"; // keep in sync with <Version> in the .csproj

        internal static ManualLogSource Log;

        internal static ConfigEntry<float> CapMultiplier;
        internal static ConfigEntry<int> ExtraCustomers;
        internal static ConfigEntry<int> HardCap;
        internal static ConfigEntry<float> SpawnDelayMultiplier;

        internal static ConfigEntry<bool> DisableBreakdowns;
        internal static ConfigEntry<float> BreakdownIntervalMultiplier;

        internal static ConfigEntry<KeyboardShortcut> SpeedKey;
        internal static ConfigEntry<string> SpeedSteps;
        internal static ConfigEntry<bool> SpeedButtonEnabled;
        internal static ConfigEntry<bool> PlayersAtNormalSpeed;
        internal static ConfigEntry<KeyboardShortcut> CosmeticsKey;
        internal static ConfigEntry<KeyboardShortcut> InviteKey;
        internal static ConfigEntry<bool> AutoContinueOnce;
        internal static ConfigEntry<bool> DiagnosticsOnce;
        internal static float[] Steps = { 1f, 2f, 4f };

        internal static ConfigEntry<bool> PistolEnabled;
        internal static ConfigEntry<KeyboardShortcut> PistolKey;
        internal static ConfigEntry<int> PistolHitsPerShot;
        internal static ConfigEntry<float> PistolRange;
        internal static ConfigEntry<float> PistolVolume;

        internal static ConfigEntry<float> PlayerKnockback;
        internal static ConfigEntry<bool> Ragdolls;
        internal static ConfigEntry<float> RagdollSeconds;
        internal static ConfigEntry<float> RagdollForce;
        internal static ConfigEntry<bool> MultiplayerSync;

        internal static ConfigEntry<bool> AutoUpdate;
        internal static ConfigEntry<int> BackupsToKeep;

        private string _toast;
        private float _toastUntil;

        private void Awake()
        {
            Log = Logger;

            BackupsToKeep = Config.Bind("Saves", "BackupsToKeep", 30,
                "Save-folder copies kept in 'LocalLow/DDTNL/Supermarket Together Backups (SupermarketBoost)'. One is made at every game start and before every save.");
            SaveBackup.Init(); // first thing, before anything can touch a save

            CapMultiplier = Config.Bind("Customers", "CapMultiplier", 2.0f,
                "Multiplies the max number of customers in the store at once (1.0 = game default).");
            ExtraCustomers = Config.Bind("Customers", "ExtraCustomers", 0,
                "Flat number of extra customers added to the cap after the multiplier.");
            HardCap = Config.Bind("Customers", "HardCap", 120,
                "Upper limit for the customer cap, to keep performance sane.");
            SpawnDelayMultiplier = Config.Bind("Customers", "SpawnDelayMultiplier", 0.5f,
                "Multiplies the wait between customer arrivals (0.5 = twice as often, 1.0 = game default).");

            DisableBreakdowns = Config.Bind("Breakdowns", "DisableBreakdowns", false,
                "true = machines never break down.");
            BreakdownIntervalMultiplier = Config.Bind("Breakdowns", "IntervalMultiplier", 3.0f,
                "Multiplies the in-game time between breakdowns (3.0 = about a third as many, 1.0 = game default).");

            SpeedKey = Config.Bind("DaySpeed", "Hotkey", new KeyboardShortcut(KeyCode.F6),
                "Cycles the game speed while the store is open (host only).");
            SpeedSteps = Config.Bind("DaySpeed", "Steps", "1,2,4",
                "Speeds the hotkey / button cycles through, comma separated.");
            SpeedButtonEnabled = Config.Bind("DaySpeed", "OfficeButton", true,
                "Adds GAME SPEED and COSMETICS ROOM buttons above the open-store switch in the office.");
            PlayersAtNormalSpeed = Config.Bind("DaySpeed", "PlayersAtNormalSpeed", true,
                "While the game is sped up, players still walk at normal speed.");
            CosmeticsKey = Config.Bind("Cosmetics", "Hotkey", new KeyboardShortcut(KeyCode.F8),
                "Teleports you to the cosmetics (skins & hats) room and back.");
            InviteKey = Config.Bind("Multiplayer", "InviteHotkey", new KeyboardShortcut(KeyCode.F7),
                "Opens Steam's invite-friends dialog for your current game.");
            DiagnosticsOnce = Config.Bind("Debug", "DiagnosticsOnce", false,
                "Testing aid: logs NPC skeletons and the cosmetics room location once in a game, then switches itself off.");
            AutoContinueOnce = Config.Bind("Debug", "AutoContinueOnce", false,
                "Testing aid: presses Continue automatically once at the main menu, then switches itself off.");
            Steps = ParseSteps(SpeedSteps.Value);

            PistolEnabled = Config.Bind("Pistol", "Enabled", true, "Adds the pistol.");
            PistolKey = Config.Bind("Pistol", "Hotkey", new KeyboardShortcut(KeyCode.G),
                "Draws / holsters the pistol (hands must be empty). Shoot with the normal action button.");
            PistolHitsPerShot = Config.Bind("Pistol", "HitsPerShot", 2,
                "How many broom hits one shot counts as (each hit makes a thief drop some goods).");
            PistolRange = Config.Bind("Pistol", "Range", 40f, "Range in metres.");
            PistolVolume = Config.Bind("Pistol", "Volume", 0.6f, "Gunshot volume (0-1).");

            PlayerKnockback = Config.Bind("Pistol", "PlayerKnockback", 9f,
                "How hard a shot pushes another player (0 = not at all).");
            Ragdolls = Config.Bind("Pistol", "Ragdolls", true, "Shot NPCs go ragdoll, then get back up.");
            RagdollSeconds = Config.Bind("Pistol", "RagdollSeconds", 2.5f, "Minimum time an NPC stays down.");
            RagdollForce = Config.Bind("Pistol", "RagdollForce", 35f, "How hard bullets push ragdolls.");
            MultiplayerSync = Config.Bind("Pistol", "MultiplayerSync", true,
                "Show pistols, shots and ragdolls to other players. Everyone in the lobby needs this mod: " +
                "a player with the mod who joins a host WITHOUT it gets disconnected. Set false to play with unmodded hosts.");

            AutoUpdate = Config.Bind("Updates", "AutoUpdate", true,
                "Downloads new versions from GitHub on start; they apply at the next launch.");

            new Harmony(Guid).PatchAll();
            Log.LogInfo($"Supermarket Boost v{Version} loaded");

            if (AutoUpdate.Value)
                StartCoroutine(Updater.Check(this, Info.Location));
        }

        private static float[] ParseSteps(string s)
        {
            var list = new List<float>();
            foreach (var p in s.Split(','))
                if (float.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 1f)
                    list.Add(v);
            if (list.Count == 0) list.Add(1f);
            return list.ToArray();
        }

        private void Update()
        {
            Net.Tick();
            if (SpeedKey.Value.IsDown()) Toast(DaySpeed.Cycle());
            if (CosmeticsKey.Value.IsDown() && GameHelpers.InputAllowed) Toast(Cosmetics.Toggle());
            DaySpeed.Tick(this);
            Pistol.Tick(this);
            MainMenu.Tick(this);
            SteamJoin.Tick(this);
            Diagnostics.Tick();
        }

        internal void Toast(string msg, float seconds = 2.5f)
        {
            if (string.IsNullOrEmpty(msg)) return;
            _toast = msg;
            _toastUntil = Time.unscaledTime + seconds;
        }

        private GUIStyle _style;

        private void OnGUI()
        {
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            MainMenu.OnGUI();

            string text = null;
            if (Time.unscaledTime < _toastUntil) text = _toast;
            else if (DaySpeed.Speed > 1f && GameData.Instance != null && GameData.Instance.isSupermarketOpen)
                text = $"Game speed x{DaySpeed.Speed:0.#}";
            if (text != null) Shadowed(new Rect(20, 20, 900, 40), text, Color.yellow);

            if (Pistol.Drawn && GameHelpers.InputAllowed)
            {
                float cx = Screen.width / 2f, cy = Screen.height / 2f;
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.85f);
                GUI.DrawTexture(new Rect(cx - 9, cy - 1, 6, 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx + 3, cy - 1, 6, 2), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - 1, cy - 9, 2, 6), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - 1, cy + 3, 2, 6), Texture2D.whiteTexture);
                GUI.color = old;
            }
        }

        private void Shadowed(Rect r, string text, Color c)
        {
            var old = GUI.color;
            GUI.color = Color.black;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, _style);
            GUI.color = c;
            GUI.Label(r, text, _style);
            GUI.color = old;
        }
    }

    // ---------- Customers ----------

    // The game sets GameData.maxCustomersNPCs when the store opens each day.
    [HarmonyPatch(typeof(GameData), nameof(GameData.AuxiliarSupermarketOpen))]
    internal static class CustomerCapPatch
    {
        private static void Postfix(GameData __instance)
        {
            int original = __instance.maxCustomersNPCs;
            int boosted = Mathf.RoundToInt(original * Plugin.CapMultiplier.Value) + Plugin.ExtraCustomers.Value;
            boosted = Mathf.Clamp(boosted, original, Mathf.Max(original, Plugin.HardCap.Value));
            __instance.maxCustomersNPCs = boosted;
            Plugin.Log.LogInfo($"Customer cap {original} -> {boosted}");
        }
    }

    // SpawnCustomerNPC is a coroutine that waits a random few seconds, then spawns one customer.
    // Wrap it and shorten that wait.
    [HarmonyPatch(typeof(NPC_Manager), "SpawnCustomerNPC")]
    internal static class SpawnDelayPatch
    {
        private static readonly FieldInfo SecondsField = AccessTools.Field(typeof(WaitForSeconds), "m_Seconds");

        private static void Postfix(ref IEnumerator __result)
        {
            __result = Wrap(__result);
        }

        private static IEnumerator Wrap(IEnumerator inner)
        {
            while (inner.MoveNext())
            {
                object current = inner.Current;
                if (current is WaitForSeconds wait && SecondsField != null)
                {
                    float seconds = (float)SecondsField.GetValue(wait);
                    yield return new WaitForSeconds(Mathf.Max(0.25f, seconds * Plugin.SpawnDelayMultiplier.Value));
                }
                else
                {
                    yield return current;
                }
            }
        }
    }

    // ---------- Breakdowns ----------

    // From day 21 the game breaks a random self-checkout / baler / etc. every few in-game hours.
    [HarmonyPatch(typeof(GameData), "BreakingManager")]
    internal static class DisableBreakdownsPatch
    {
        private static bool Prefix() => !Plugin.DisableBreakdowns.Value;
    }

    // GetNextTimeAdd returns the in-game hours until the next breakdown.
    [HarmonyPatch(typeof(GameData), "GetNextTimeAdd")]
    internal static class BreakdownIntervalPatch
    {
        private static void Postfix(ref float __result)
        {
            __result *= Mathf.Max(0.1f, Plugin.BreakdownIntervalMultiplier.Value);
        }
    }
}
