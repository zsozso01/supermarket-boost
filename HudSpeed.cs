using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SupermarketBoost
{
    /// <summary>
    /// While the game is sped up, the in-game clock box (top left) grows just enough to show ">> x2"
    /// under the weekday. Built from the clock's own pieces; restored at x1.
    /// </summary>
    internal static class HudSpeed
    {
        private const float Extra = 18f, FontSize = 18f; // canvas units
        private static readonly string[] BoxParts = { "BCKS/BCK (5)", "BCKS/FranchiseBar", "BCKS/BCK (1)" };
        private static readonly List<(RectTransform rt, Vector2 pos, Vector2 size)> Parts = new List<(RectTransform, Vector2, Vector2)>();
        private static TMP_Text _text;
        private static GameObject _hud;
        private static bool _grown;
        private static float _nextTry;

        public static void Tick()
        {
            var gd = GameData.Instance;
            bool show = gd != null && gd.isSupermarketOpen && DaySpeed.Speed > 1f;
            if (_hud == null)
            {
                _text = null; Parts.Clear(); _grown = false;
                if (!show || Time.unscaledTime < _nextTry) return;
                _nextTry = Time.unscaledTime + 2f;
                if (!Build()) return;
            }
            if (show != _grown) Grow(show);
            if (show) _text.text = $">> x{DaySpeed.Speed:0.#}";
        }

        private static bool Build()
        {
            var hud = GameObject.Find("GameCanvas/GameData");
            var label = hud != null ? hud.transform.Find("WeekDay_Text") as RectTransform : null;
            if (label == null) return false;
            foreach (var path in BoxParts)
                if (hud.transform.Find(path) is RectTransform rt) Parts.Add((rt, rt.anchoredPosition, rt.sizeDelta));
            if (Parts.Count == 0) return false;
            _hud = hud;

            _text = Object.Instantiate(label.gameObject, label.parent).GetComponent<TMP_Text>();
            _text.name = "SB_Speed_Text";
            // Same rect and alignment as the weekday, one (smaller) line lower.
            _text.rectTransform.anchoredPosition = label.anchoredPosition - new Vector2(0f, Extra);
            _text.enableAutoSizing = false;
            _text.fontSizeMax = _text.fontSize = FontSize;
            _text.color = new Color(1f, 0.82f, 0.29f);
            _text.raycastTarget = false;
            _text.gameObject.SetActive(false);
            Plugin.Log.LogInfo("Game speed line added to the clock box");
            return true;
        }

        private static void Grow(bool on)
        {
            _grown = on;
            foreach (var (rt, pos, size) in Parts)
            {
                if (rt == null) continue;
                rt.sizeDelta = on ? size + new Vector2(0f, Extra) : size;
                rt.anchoredPosition = on ? pos - new Vector2(0f, Extra * 0.5f) : pos;
            }
            if (_text != null) _text.gameObject.SetActive(on);
        }
    }
}
