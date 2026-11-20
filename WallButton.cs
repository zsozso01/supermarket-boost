using System;
using TMPro;
using UnityEngine;

namespace SupermarketBoost
{
    /// <summary>A pressable wall button made of primitives, with a label above the cap.</summary>
    internal class WallButton
    {
        public GameObject Root;
        private Transform _press;
        private TextMeshPro _label;
        private Renderer _cap;
        private float _pressedUntil;
        private Action _onPress;

        private static Material _plateMat;

        public static WallButton Create(string name, Vector3 pos, Quaternion rot, int layer, string label, Color color, Action onPress)
        {
            if (_plateMat == null) _plateMat = GameHelpers.MakeMaterial(new Color(0.12f, 0.13f, 0.15f));

            var b = new WallButton { _onPress = onPress, Root = new GameObject(name) };
            b.Root.transform.SetPositionAndRotation(pos, rot);
            b.Root.layer = layer;

            var plate = GameHelpers.Box(b.Root.transform, Vector3.zero, new Vector3(0.26f, 0.13f, 0.03f), _plateMat, keepCollider: true);
            plate.layer = layer;

            b._press = new GameObject("Press").transform;
            b._press.SetParent(b.Root.transform, false);
            var cap = GameHelpers.Box(b._press, new Vector3(0f, -0.018f, 0f), new Vector3(0.09f, 0.05f, 0.04f), null, keepCollider: true);
            cap.layer = layer;
            b._cap = cap.GetComponent<Renderer>();
            b.SetColor(color);

            // TMP text is readable from the plate's -Z side (the side the switch faces).
            var go = new GameObject("Label");
            go.transform.SetParent(b.Root.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.036f, -0.017f);
            b._label = go.AddComponent<TextMeshPro>();
            b._label.fontSize = 0.28f;
            b._label.alignment = TextAlignmentOptions.Center;
            b._label.color = Color.white;
            b._label.textWrappingMode = TextWrappingModes.NoWrap;
            b._label.overflowMode = TextOverflowModes.Overflow;
            b._label.rectTransform.sizeDelta = new Vector2(0.24f, 0.045f);
            b._label.text = label;
            return b;
        }

        public void SetLabel(string text) { if (_label != null) _label.text = text; }

        public void SetColor(Color c)
        {
            if (_cap != null) _cap.sharedMaterial = GameHelpers.MakeMaterial(c, 0.6f, c * 0.4f);
        }

        public bool Owns(Collider c) => Root != null && c != null && c.transform.IsChildOf(Root.transform);

        public void Press()
        {
            _pressedUntil = Time.unscaledTime + 0.15f;
            _onPress?.Invoke();
        }

        public void Animate()
        {
            if (_press != null)
                _press.localPosition = new Vector3(0f, 0f, Time.unscaledTime < _pressedUntil ? 0.012f : -0.02f);
        }
    }
}
