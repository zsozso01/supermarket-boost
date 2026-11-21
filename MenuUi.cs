using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SupermarketBoost
{
    /// <summary>
    /// Main menu, built from the game's own UI pieces so it looks native:
    ///  - "Continue" at the top of the menu (hosts your latest store for friends),
    ///  - "Join (friend)" when a Steam friend is in a store,
    ///  - "Supermarket Boost" opens a panel listing every feature, hotkey and setting,
    ///  - the ads / news boxes are hidden (Menu.CleanMainMenu).
    /// </summary>
    internal static class MenuUi
    {
        private static GameObject _menu, _continue, _join, _mods, _panel;
        private static TMP_Text _continueText, _joinText, _modsText, _panelBody;
        private static ulong _joinLobby;
        private static string _joinName;
        private const float Step = 100f;

        public static bool Built => _menu != null;

        public static void Tick()
        {
            if (!MainMenu.InMenu) { _menu = null; return; }
            if (_menu == null) Build();
            if (_menu == null) return;

            if (_continue != null)
            {
                _continue.SetActive(MainMenu.LatestSave != null);
                _continueText.text = MainMenu.Busy ? "Loading..." :
                    $"Continue <size=55%><color=#FFFFFFB0>{MainMenu.LatestLabel}</color></size>";
            }
            if (_join != null)
            {
                bool any = MainMenu.Friend(out _joinName, out _joinLobby);
                _join.SetActive(any);
                if (any) _joinText.text = $"Join {_joinName} <size=55%><color=#FFFFFFB0>in their store</color></size>";
            }
            if (_mods != null) _modsText.text = "Mod Menu <size=55%><color=#FFFFFFB0>Supermarket Boost</color></size>";
            Relayout();

            if (_panel != null && _panel.activeSelf && Input.GetKeyDown(KeyCode.Escape)) _panel.SetActive(false);
        }

        private static void Build()
        {
            var canvas = GameObject.Find("Intro_Canvas");
            var menu = canvas != null ? canvas.transform.Find("MainMenu") : null;
            var template = menu != null ? menu.Find("LoadStore_Button") : null;
            if (template == null) return;
            _menu = menu.gameObject;

            if (Plugin.CleanMainMenu.Value)
                foreach (var name in new[] { "DLCNotice", "UpdatesNotice", "Discord_Container" })
                {
                    var t = canvas.transform.Find(name);
                    if (t != null) t.gameObject.SetActive(false);
                }

            _continue = MakeButton(template, "SB_Continue_Button", out _continueText, () => Plugin.Instance.Toast(MainMenu.Continue()));
            _join = MakeButton(template, "SB_JoinFriend_Button", out _joinText, () => { if (MainMenu.JoinLobby(_joinLobby)) Plugin.Instance.Toast($"Joining {_joinName}..."); });
            _mods = MakeButton(template, "SB_Mods_Button", out _modsText, OpenPanel);
            _panel = null;
            Plugin.Log.LogInfo("Main menu: Continue / Supermarket Boost buttons added");
        }

        private static GameObject MakeButton(Transform template, string name, out TMP_Text text, UnityEngine.Events.UnityAction onClick)
        {
            var go = Object.Instantiate(template.gameObject, template.parent);
            go.name = name;
            // Keep the hover sound FSM, drop the click behaviour (it would open the Load Store menu).
            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName != "UI_HoverSound") Object.DestroyImmediate(fsm);
            var b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener(onClick);
            text = go.GetComponentInChildren<TMP_Text>(true);
            var loc = text.GetComponent("SetLocalizationString");
            if (loc != null) Object.DestroyImmediate(loc);
            text.richText = true;
            text.enableWordWrapping = false;
            var tt = template.GetComponentInChildren<TMP_Text>(true);
            text.enableAutoSizing = false;
            text.fontSize = tt != null ? tt.fontSize : text.fontSize;
            text.overflowMode = TextOverflowModes.Overflow;
            return go;
        }

        /// <summary>Continue, (Join), New Store, Load Store, Join Store, Options, Supermarket Boost, Exit.</summary>
        private static void Relayout()
        {
            var order = new List<string> { "SB_Continue_Button", "SB_JoinFriend_Button", "NewStore_Button", "LoadStore_Button",
                                            "JoinStore_Button", "Options_Button", "SB_Mods_Button", "Exit_Button" };
            float y = 20f;
            foreach (var n in order)
            {
                var t = _menu.transform.Find(n) as RectTransform;
                if (t == null || !t.gameObject.activeSelf) continue;
                var p = t.anchoredPosition;
                if (!Mathf.Approximately(p.y, y)) t.anchoredPosition = new Vector2(p.x, y);
                y -= Step;
            }
        }

        // ---------- the feature list ----------

        private static void OpenPanel()
        {
            if (_panel == null) BuildPanel();
            if (_panel == null) return;
            var lines = FeatureText().Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].StartsWith(" • ")) lines[i] = "• <indent=1.1em>" + lines[i].Substring(3).TrimEnd('\r') + "</indent>";
            _panelBody.text = string.Join("\n", lines);
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
        }

        private static void BuildPanel()
        {
            var canvas = GameObject.Find("Intro_Canvas");
            var load = canvas != null ? canvas.transform.Find("Menu_LoadStore") : null;
            var srcBck = load != null ? load.Find("BCK") : null;
            var srcBox = load != null ? load.Find("Container") : null;
            var srcTitle = srcBox != null ? srcBox.Find("SelectAStore_Text") : null;
            var srcBack = srcBox != null ? srcBox.Find("Back_Button") : null;
            if (srcBox == null || srcTitle == null || srcBack == null) { Plugin.Log.LogWarning("Mod panel: menu pieces not found"); return; }

            _panel = new GameObject("SB_ModsPanel", typeof(RectTransform));
            _panel.transform.SetParent(canvas.transform, false);
            Stretch(_panel.GetComponent<RectTransform>());

            if (srcBck != null)
            {
                var bck = Object.Instantiate(srcBck.gameObject, _panel.transform);
                bck.SetActive(true);
                bck.AddComponent<Button>().onClick.AddListener(() => _panel.SetActive(false)); // click outside closes
            }

            var box = Object.Instantiate(srcBox.gameObject, _panel.transform);
            box.name = "Box";
            foreach (var fsm in box.GetComponents<PlayMakerFSM>()) Object.DestroyImmediate(fsm);
            for (int i = box.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(box.transform.GetChild(i).gameObject);
            box.SetActive(true);
            var boxRt = box.GetComponent<RectTransform>();
            boxRt.sizeDelta = new Vector2(1500f, 1180f);
            boxRt.anchoredPosition = Vector2.zero;

            var title = Object.Instantiate(srcTitle.gameObject, box.transform).GetComponent<TMP_Text>();
            title.name = "Title";
            StripLocalization(title);
            title.text = $"Supermarket Boost  v{Plugin.Version}";
            title.enableAutoSizing = false;
            title.fontSize = 48f;
            var trt = title.rectTransform;
            trt.anchoredPosition = new Vector2(0f, -24f);
            trt.sizeDelta = new Vector2(1400f, 70f);

            _panelBody = Object.Instantiate(srcTitle.gameObject, box.transform).GetComponent<TMP_Text>();
            _panelBody.name = "Body";
            StripLocalization(_panelBody);
            _panelBody.alignment = TextAlignmentOptions.TopLeft;
            _panelBody.enableWordWrapping = true;
            _panelBody.richText = true;
            _panelBody.enableAutoSizing = false;
            _panelBody.fontSize = 29f;
            var brt = _panelBody.rectTransform;
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.offsetMin = new Vector2(60f, 90f);
            brt.offsetMax = new Vector2(-60f, -110f);

            var back = Object.Instantiate(srcBack.gameObject, box.transform);
            back.name = "Close_Button";
            foreach (var fsm in back.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName != "UI_HoverSound") Object.DestroyImmediate(fsm);
            var bb = back.GetComponent<Button>();
            bb.onClick = new Button.ButtonClickedEvent();
            bb.onClick.AddListener(() => _panel.SetActive(false));
            var bt = back.GetComponentInChildren<TMP_Text>(true);
            if (bt != null) { StripLocalization(bt); bt.text = "Close"; }
            back.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 20f);

            _panel.SetActive(false);
        }

        private static void StripLocalization(Component c)
        {
            var loc = c.GetComponent("SetLocalizationString");
            if (loc != null) Object.DestroyImmediate(loc);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Pct(float v) => Mathf.RoundToInt(v * 100f) + "%";

        internal static string FeatureText()
        {
            const string H = "<color=#FFD24A><b>", HE = "</b></color>";
            const string K = "<color=#7FE7DC>", KE = "</color>";
            string steps = string.Join(" / ", System.Array.ConvertAll(Plugin.Steps, s => "x" + F(s)));
            string breakdowns = Plugin.DisableBreakdowns.Value ? "off" : $"{F(1f / Mathf.Max(0.1f, Plugin.BreakdownIntervalMultiplier.Value))}x as often";
            return
$@"{H}Store{HE}
 • Customers: {Pct(Plugin.CapMultiplier.Value)} of the normal crowd, arriving {F(1f / Mathf.Max(0.05f, Plugin.SpawnDelayMultiplier.Value))}x as often
 • Thieves: {Pct(Plugin.ThiefChanceMultiplier.Value)} of normal     • Machine breakdowns: {breakdowns}
 • Game speed {steps}: {K}{Plugin.SpeedKey.Value}{KE} or the button above the office's open-store switch (host). Players keep walking at normal speed; back to x1 when the store closes
 • Cosmetics room (skins and hats): {K}{Plugin.CosmeticsKey.Value}{KE} or the office button, press again to return

{H}Pistol{HE}
 • {K}{Plugin.PistolKey.Value}{KE} draw / holster (empty hands)   {K}Left click{KE} shoot   {K}{Plugin.ReloadKey.Value}{KE} reload   ({Plugin.MagSize.Value}-round magazine)
 • Shot people ragdoll; {Plugin.ShotsToKnockOut.Value} shots knock out shoppers and thieves (thieves drop what they stole). Staff and people in a checkout line always get up
 • Broom hits ragdoll too. Blood sprays, stains and pools

{H}Playing with friends{HE}
 • {K}Continue{KE} hosts your latest store as a friends-only lobby: friends join from Steam (Join Game / invite) or the Join button here
 • {K}{Plugin.InviteKey.Value}{KE} in a store opens Steam's invite dialog. Invites accepted inside a store join after you return to this menu
 • Everyone in the lobby needs Supermarket Boost

{H}Saves{HE}
 • Your saves are backed up at every start and before every save (last {Plugin.BackupsToKeep.Value} kept)

<size=80%><color=#FFFFFFA0>Settings: BepInEx/config/{Plugin.Guid}.cfg   •   {Updater.Status}</color></size>";
        }
    }
}
