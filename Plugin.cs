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
        public const string Version = "1.6.0"; // keep in sync with <Version> in the .csproj

        internal static ManualLogSource Log;
        internal static Plugin Instance;

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

        internal static ConfigEntry<int> MagSize;
        internal static ConfigEntry<int> ReserveAmmo;
        internal static ConfigEntry<bool> InfiniteReserve;
        internal static ConfigEntry<float> ThiefChanceMultiplier;
        internal static ConfigEntry<int> ConfigVersion;
        internal static ConfigEntry<KeyboardShortcut> ReloadKey;
        internal static ConfigEntry<int> ShotsToKnockOut;
        internal static ConfigEntry<float> KnockOutBodySeconds;
        internal static ConfigEntry<bool> BroomRagdolls;
        internal static ConfigEntry<bool> PlayerRagdolls;
        internal static ConfigEntry<float> PlayerRagdollSeconds;
        internal static ConfigEntry<bool> BloodEnabled;
        internal static ConfigEntry<float> BloodSeconds;
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
            Instance = this;

            BackupsToKeep = Config.Bind("Saves", "BackupsToKeep", 30,
                "Save-folder copies kept in 'LocalLow/DDTNL/Supermarket Together Backups (SupermarketBoost)'. One is made at every game start and before every save.");
            SaveBackup.Init(); // first thing, before anything can touch a save

            CapMultiplier = Config.Bind("Customers", "CapMultiplier", 1.4f,
                "Multiplies the max number of customers in the store at once (1.0 = game default).");
            ExtraCustomers = Config.Bind("Customers", "ExtraCustomers", 0,
                "Flat number of extra customers added to the cap after the multiplier.");
            HardCap = Config.Bind("Customers", "HardCap", 120,
                "Upper limit for the customer cap, to keep performance sane.");
            SpawnDelayMultiplier = Config.Bind("Customers", "SpawnDelayMultiplier", 0.75f,
                "Multiplies the wait between customer arrivals (0.5 = twice as often, 1.0 = game default).");
            ThiefChanceMultiplier = Config.Bind("Customers", "ThiefChanceMultiplier", 0.4f,
                "Share of would-be thieves that actually steal (1.0 = game default, 0 = no thieves).");

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

            MagSize = Config.Bind("Pistol", "MagazineSize", 15, "Rounds per magazine.");
            ReserveAmmo = Config.Bind("Pistol", "ReserveAmmo", 60, "Spare rounds; refilled every morning when the store opens.");
            InfiniteReserve = Config.Bind("Pistol", "UnlimitedSpareAmmo", true, "true = never run out of spare magazines (you still reload).");
            ReloadKey = Config.Bind("Pistol", "ReloadHotkey", new KeyboardShortcut(KeyCode.R), "Reload.");
            ShotsToKnockOut = Config.Bind("Pistol", "ShotsToKnockOut", 2,
                "Shots (within 30 s) that keep a shopper or thief down for good. Staff and customers in a checkout line always get up.");
            KnockOutBodySeconds = Config.Bind("Pistol", "KnockedOutBodySeconds", 30f, "How long a knocked-out body stays before it is cleared.");
            Ragdolls = Config.Bind("Ragdolls", "Enabled", true, "Shot (and broomed) people go ragdoll.");
            BroomRagdolls = Config.Bind("Ragdolls", "BroomRagdolls", true, "Broom hits also ragdoll (gently).");
            PlayerRagdolls = Config.Bind("Ragdolls", "PlayerRagdolls", true, "Shooting another player ragdolls them.");
            RagdollSeconds = Config.Bind("Ragdolls", "NpcSeconds", 2.5f, "Minimum time an NPC stays down.");
            PlayerRagdollSeconds = Config.Bind("Ragdolls", "PlayerSeconds", 2.5f, "Minimum time a shot player stays down.");
            RagdollForce = Config.Bind("Ragdolls", "Force", 35f, "How hard bullets push ragdolls.");
            BloodEnabled = Config.Bind("Blood", "Enabled", true, "Blood spray, splats and pools.");
            BloodSeconds = Config.Bind("Blood", "Seconds", 90f, "How long blood stays on the floor.");
            MultiplayerSync = Config.Bind("Pistol", "MultiplayerSync", true,
                "Show pistols, shots and ragdolls to other players. Everyone in the lobby needs this mod: " +
                "a player with the mod who joins a host WITHOUT it gets disconnected. Set false to play with unmodded hosts.");

            AutoUpdate = Config.Bind("Updates", "AutoUpdate", true,
                "Downloads new versions from GitHub on start; they apply at the next launch.");

            // Older versions wrote other defaults into the config file; move untouched values to the new ones.
            ConfigVersion = Config.Bind("Internal", "ConfigVersion", 0, "Do not edit.");
            if (ConfigVersion.Value < 2)
            {
                if (Mathf.Approximately(CapMultiplier.Value, 2f)) CapMultiplier.Value = 1.4f;
                if (Mathf.Approximately(SpawnDelayMultiplier.Value, 0.5f)) SpawnDelayMultiplier.Value = 0.75f;
                if (ShotsToKnockOut.Value == 3) ShotsToKnockOut.Value = 2;
                ConfigVersion.Value = 2;
            }

            Pistol.Init();
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
            if (CosmeticsKey.Value.IsDown() && GameData.Instance != null) Toast(Cosmetics.Toggle());
            DaySpeed.Tick(this);
            Pistol.Tick(this);
            MainMenu.Tick(this);
            SteamJoin.Tick(this);
            Diagnostics.Tick();
            Cosmetics.Tick();
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

            Pistol.OnGUI(_style);
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

        private static void Postfix(NPC_Manager __instance, ref IEnumerator __result)
        {
            __result = Wrap(__result, __instance);
        }

        private static IEnumerator Wrap(IEnumerator inner, NPC_Manager mgr)
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

            // The customer just spawned is the last child. Fewer of them turn out to be thieves.
            var parent = mgr != null ? mgr.customersnpcParentOBJ : null;
            if (parent != null && parent.transform.childCount > 0)
            {
                var npc = parent.transform.GetChild(parent.transform.childCount - 1).GetComponent<NPC_Info>();
                if (npc != null && npc.isAThief && Random.value > Plugin.ThiefChanceMultiplier.Value) npc.isAThief = false;
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
