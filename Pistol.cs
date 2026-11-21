using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using StarterAssets;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace SupermarketBoost
{
    /// <summary>Builds the pistol mesh, with RightHandIK / LeftHandIK grip points for the game's hand IK.</summary>
    internal class PistolModel : MonoBehaviour
    {
        public Transform Kick, Slide, Muzzle;
        // Grip points for the hand IK (Kick-local). Tuned in game with the dev harness.
        // Hand grip points (Kick-local): wrist position, and the direction the knuckles point.
        // Two-handed "thumbs forward" grip: right hand high on the grip, left hand wrapping it from the left.
        public static Vector3 RightIkPos = new Vector3(0.02f, 0.012f, -0.115f), RightKnuckles = new Vector3(0f, -0.31f, 0.95f);
        public static Vector3 LeftIkPos = new Vector3(-0.032f, -0.015f, -0.09f), LeftKnuckles = new Vector3(0.1f, -0.31f, 0.95f);
        public static Vector3 RightIkRot => GripRot(true, RightKnuckles).eulerAngles;
        public static Vector3 LeftIkRot => GripRot(false, LeftKnuckles).eulerAngles;

        /// <summary>
        /// IK target rotation for a Bip01 hand (bone: knuckles along -X, palm side +Y) so the knuckles point
        /// along <paramref name="knuckles"/> and the palm faces the grip (right hand) / the right hand (left).
        /// </summary>
        public static float RollR = 270f, RollL = 90f; // roll about the knuckle direction: fingers wrap down the grip

        public static Quaternion GripRot(bool right, Vector3 knuckles)
        {
            var f = knuckles.normalized;
            var extra = Quaternion.AngleAxis(right ? RollR : RollL, f);
            var palm = right ? Quaternion.AngleAxis(90f, f) * Vector3.left
                             : Quaternion.AngleAxis(-90f, f) * new Vector3(0.9f, 0.35f, 0f).normalized;
            return extra * Quaternion.LookRotation(f, palm) * Quaternion.Inverse(Quaternion.LookRotation(Vector3.left, Vector3.up));
        }
        private Light _flash;
        private GameObject _flashMesh;
        private float _flashUntil, _recoil, _slideBack;

        private static Material _polymer, _steel, _flashMat, _grip, _barrel, _dark, _dot;
        private static Mesh _slideMesh, _slideDark, _frameMesh, _gripMesh, _barrelMesh, _darkMesh, _dotMesh;

        private static Texture2D StippleTexture()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SB_Stipple", wrapMode = TextureWrapMode.Repeat };
            var rng = new System.Random(7);
            var px = new Color[n * n];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0.085f, 0.085f, 0.095f);
            for (int k = 0; k < 900; k++) // raised stipple dots, lit from the top
            {
                int cx = rng.Next(n), cy = rng.Next(n);
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (dx * dx + dy * dy > 4) continue;
                        int x = (cx + dx + n) % n, y = (cy + dy + n) % n;
                        float shade = 0.085f + 0.05f * (dy + 2) / 4f - 0.02f;
                        px[y * n + x] = new Color(shade, shade, shade + 0.008f);
                    }
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        private static void BuildMeshes()
        {
            _polymer = GameHelpers.MakeMaterial(new Color(0.075f, 0.075f, 0.085f), 0.3f);
            _steel = GameHelpers.MakeMaterial(new Color(0.2f, 0.205f, 0.215f), 0.6f);
            _barrel = GameHelpers.MakeMaterial(new Color(0.42f, 0.42f, 0.44f), 0.85f);
            _dark = GameHelpers.MakeMaterial(new Color(0.015f, 0.015f, 0.018f), 0.1f);
            _dot = GameHelpers.MakeMaterial(new Color(0.85f, 1f, 0.75f), 0.2f, new Color(0.45f, 0.9f, 0.35f));
            _flashMat = GameHelpers.MakeMaterial(new Color(1f, 0.8f, 0.3f), 0f, new Color(4f, 2.5f, 0.8f));
            _grip = GameHelpers.MakeMaterial(Color.white, 0.15f);
            var stipple = StippleTexture();
            _grip.mainTexture = stipple;
            if (_grip.HasProperty("_BaseMap")) _grip.SetTexture("_BaseMap", stipple);

            // Coordinates: x right, y up, z forward (muzzle), metres. Bore axis at y = 0.023.
            // Slide: beveled, slight taper at the nose.
            var slide = new MeshKit().Extrude(new[]
            {
                new Vector2(-0.085f, 0.007f), new Vector2(0.099f, 0.007f), new Vector2(0.099f, 0.033f),
                new Vector2(0.093f, 0.040f), new Vector2(-0.081f, 0.040f), new Vector2(-0.085f, 0.036f),
            }, 0.0128f, 0.0032f);
            // Rear sight (two ears with a notch) and front sight post.
            slide.Box(new Vector3(-0.0105f, 0.039f, -0.081f), new Vector3(-0.0022f, 0.0465f, -0.071f), 0.0012f);
            slide.Box(new Vector3(0.0022f, 0.039f, -0.081f), new Vector3(0.0105f, 0.0465f, -0.071f), 0.0012f);
            slide.Box(new Vector3(-0.0018f, 0.039f, 0.084f), new Vector3(0.0018f, 0.0465f, 0.091f), 0.0008f);
            // Slide stop lever (left).
            slide.Box(new Vector3(-0.0142f, 0.003f, -0.012f), new Vector3(-0.0122f, 0.009f, 0.016f), 0.0008f);
            _slideMesh = slide.ToMesh("SB_PistolSlide");

            // Serrations, ejection port and muzzle bore: dark inlays on the slide.
            var dark = new MeshKit();
            for (int i = 0; i < 8; i++)
            {
                float z = -0.079f + i * 0.0036f;
                foreach (float sx in new[] { -1f, 1f })
                    dark.Box(new Vector3(sx > 0 ? 0.0126f : -0.0131f, 0.011f, z), new Vector3(sx > 0 ? 0.0131f : -0.0126f, 0.036f, z + 0.0016f), 0.0002f);
            }
            dark.Box(new Vector3(-0.004f, 0.0398f, 0.004f), new Vector3(0.0131f, 0.0406f, 0.042f), 0.0003f);   // port, top
            dark.Box(new Vector3(0.0126f, 0.026f, 0.004f), new Vector3(0.0133f, 0.0402f, 0.042f), 0.0003f);    // port, side
            dark.Disc(new Vector3(0f, 0.023f, 0.1012f), Vector3.forward, 0.0042f);                             // bore
            _slideDark = dark.ToMesh("SB_PistolSlideDark");

            // Frame: dust cover with rail, trigger guard and beavertail.
            var frame = new MeshKit().Extrude(new[]
            {
                new Vector2(-0.094f, 0.003f), new Vector2(-0.084f, 0.009f), new Vector2(0.091f, 0.009f),
                new Vector2(0.091f, -0.005f), new Vector2(0.085f, -0.011f), new Vector2(0.036f, -0.011f),
                new Vector2(0.030f, -0.013f), new Vector2(-0.044f, -0.013f), new Vector2(-0.070f, -0.012f),
                new Vector2(-0.088f, -0.004f),
            }, 0.0118f, 0.0028f);
            for (int i = 0; i < 3; i++) // rail slots
            {
                float z = 0.046f + i * 0.013f;
                frame.Box(new Vector3(-0.0122f, -0.0105f, z), new Vector3(0.0122f, -0.0068f, z + 0.0045f), 0.0006f);
            }
            frame.Sweep(new[] // trigger guard
            {
                new Vector3(0f, -0.010f, 0.034f), new Vector3(0f, -0.022f, 0.037f), new Vector3(0f, -0.032f, 0.031f),
                new Vector3(0f, -0.037f, 0.018f), new Vector3(0f, -0.037f, -0.004f), new Vector3(0f, -0.034f, -0.030f),
                new Vector3(0f, -0.030f, -0.044f),
            }, new[] { 0.0045f, 0.0045f, 0.0045f, 0.0045f, 0.0045f, 0.0045f, 0.0045f },
               new[] { 0.0034f, 0.0034f, 0.0034f, 0.0034f, 0.0034f, 0.0034f, 0.0034f }, 3f, 12);
            frame.Sweep(new[] // trigger blade
            {
                new Vector3(0f, -0.011f, 0.004f), new Vector3(0f, -0.017f, 0.007f), new Vector3(0f, -0.023f, 0.006f),
                new Vector3(0f, -0.028f, 0.002f),
            }, new[] { 0.0028f, 0.0028f, 0.0028f, 0.0024f }, new[] { 0.0018f, 0.0018f, 0.0018f, 0.0016f }, 3f, 10);
            // Takedown lever.
            frame.Box(new Vector3(-0.0128f, -0.003f, 0.022f), new Vector3(0.0128f, 0.0005f, 0.030f), 0.0006f);
            // Magazine base plate.
            Vector3 gTop = new Vector3(0f, -0.010f, -0.058f), gDir = new Vector3(0f, -0.95f, -0.31f).normalized;
            Vector3 gBot = gTop + gDir * 0.085f;
            frame.Sweep(new[] { gBot + gDir * 0.0005f, gBot + gDir * 0.0035f, gBot + gDir * 0.0075f },
                new[] { 0.0142f, 0.0150f, 0.0144f }, new[] { 0.0270f, 0.0282f, 0.0270f }, 3.5f, 24);
            _frameMesh = frame.ToMesh("SB_PistolFrame");

            // Grip: rounded, with a palm swell, stippled.
            var path = new Vector3[8];
            var hx = new float[8];
            var hn = new float[8];
            for (int i = 0; i < 8; i++)
            {
                float t = i / 7f;
                path[i] = gTop + gDir * (0.086f * t) + new Vector3(0f, 0f, 0.0015f * Mathf.Sin(t * Mathf.PI));
                hx[i] = 0.0128f + 0.0016f * Mathf.Sin(t * Mathf.PI * 0.9f);
                hn[i] = 0.0255f + 0.0012f * Mathf.Sin(t * Mathf.PI) - 0.001f * t;
            }
            _gripMesh = new MeshKit().Sweep(path, hx, hn, 3.2f, 28, true, 18f).ToMesh("SB_PistolGrip");

            // Barrel crown and the barrel hood showing through the ejection port.
            var barrel = new MeshKit().Cylinder(new Vector3(0f, 0.023f, 0f), 0.0062f, 0.090f, 0.1008f, 24);
            barrel.Box(new Vector3(-0.0025f, 0.0407f, 0.008f), new Vector3(0.0105f, 0.0411f, 0.034f), 0.0003f);
            _barrelMesh = barrel.ToMesh("SB_PistolBarrel");

            // Three-dot sights.
            var dots = new MeshKit();
            dots.Disc(new Vector3(-0.0064f, 0.0438f, -0.0812f), Vector3.back, 0.0011f, 10);
            dots.Disc(new Vector3(0.0064f, 0.0438f, -0.0812f), Vector3.back, 0.0011f, 10);
            dots.Disc(new Vector3(0f, 0.0440f, 0.0838f), Vector3.back, 0.0011f, 10);
            _dotMesh = dots.ToMesh("SB_PistolDots");
        }

        private static void Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        public static PistolModel Build(Transform parent, string name)
        {
            if (_slideMesh == null) BuildMeshes();

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var m = root.AddComponent<PistolModel>();
            m.Kick = new GameObject("Kick").transform;
            m.Kick.SetParent(root.transform, false);

            m.Slide = new GameObject("Slide").transform;
            m.Slide.SetParent(m.Kick, false);
            Part(m.Slide, "SlideBody", _slideMesh, _steel);
            Part(m.Slide, "SlideDetail", _slideDark, _dark);
            Part(m.Slide, "Sights", _dotMesh, _dot);
            Part(m.Kick, "Frame", _frameMesh, _polymer);
            Part(m.Kick, "Grip", _gripMesh, _grip);
            Part(m.Kick, "Barrel", _barrelMesh, _barrel);

            m.Muzzle = new GameObject("Muzzle").transform;
            m.Muzzle.SetParent(m.Kick, false);
            m.Muzzle.localPosition = new Vector3(0f, 0.023f, 0.104f);
            m._flash = m.Muzzle.gameObject.AddComponent<Light>();
            m._flash.type = LightType.Point;
            m._flash.color = new Color(1f, 0.75f, 0.35f);
            m._flash.range = 6f;
            m._flash.intensity = 6f;
            m._flash.enabled = false;
            m._flashMesh = GameHelpers.Box(m.Muzzle, new Vector3(0f, 0f, 0.022f), new Vector3(0.035f, 0.035f, 0.045f), _flashMat);
            m._flashMesh.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            m._flashMesh.SetActive(false);

            // Grip points for the character's two-bone IK (right hand on the grip, left hand cupping it).
            var r = new GameObject("RightHandIK").transform;
            r.SetParent(m.Kick, false);
            r.localPosition = RightIkPos;
            r.localRotation = GripRot(true, RightKnuckles);
            var l = new GameObject("LeftHandIK").transform;
            l.SetParent(m.Kick, false);
            l.localPosition = LeftIkPos;
            l.localRotation = GripRot(false, LeftKnuckles);
            return m;
        }

        public void Fire()
        {
            _recoil = 1f;
            _slideBack = 1f;
            _flashUntil = Time.time + 0.05f;
        }

        private void Update()
        {
            _recoil = Mathf.MoveTowards(_recoil, 0f, Time.unscaledDeltaTime * 6f);
            _slideBack = Mathf.MoveTowards(_slideBack, 0f, Time.unscaledDeltaTime * 12f);
            Kick.localPosition = new Vector3(0f, 0f, -0.04f * _recoil);
            Kick.localRotation = Quaternion.Euler(-20f * _recoil, 0f, 0f);
            Slide.localPosition = new Vector3(0f, 0f, -0.03f * _slideBack);
            bool on = Time.time < _flashUntil;
            _flash.enabled = on;
            _flashMesh.SetActive(on);
        }
    }

    /// <summary>
    /// Makes the pistol a real game item: an extra slot in PlayerNetwork.equippedPrefabs, equipped through the
    /// game's own synced equippedItem, held with the game's hand IK, visible to everyone.
    /// </summary>
    [HarmonyPatch]
    internal static class PistolItem
    {
        public static int Index = -1;
        private static GameObject _placeholder;

        // PlayerSyncCharacter private fields used for the hand IK.
        private static readonly AccessTools.FieldRef<PlayerSyncCharacter, PlayerNetwork> PNet = AccessTools.FieldRefAccess<PlayerSyncCharacter, PlayerNetwork>("pNetwork");
        private static readonly AccessTools.FieldRef<PlayerSyncCharacter, TwoBoneIKConstraint> RCon = AccessTools.FieldRefAccess<PlayerSyncCharacter, TwoBoneIKConstraint>("rightHandConstraint");
        private static readonly AccessTools.FieldRef<PlayerSyncCharacter, TwoBoneIKConstraint> LCon = AccessTools.FieldRefAccess<PlayerSyncCharacter, TwoBoneIKConstraint>("leftHandConstraint");
        private static readonly AccessTools.FieldRef<PlayerSyncCharacter, Transform> RTarget = AccessTools.FieldRefAccess<PlayerSyncCharacter, Transform>("rightHandOBJ");
        private static readonly AccessTools.FieldRef<PlayerSyncCharacter, Transform> LTarget = AccessTools.FieldRefAccess<PlayerSyncCharacter, Transform>("leftHandOBJ");

        public static bool Holds(PlayerNetwork p) => p != null && Index >= 0 && p.equippedItem == Index;

        // Every player gets the extra slot (same index on every modded game).
        [HarmonyPostfix, HarmonyPatch(typeof(PlayerNetwork), "Start")]
        private static void AddSlot(PlayerNetwork __instance)
        {
            var arr = __instance.equippedPrefabs;
            if (arr == null) return;
            if (_placeholder == null)
            {
                _placeholder = new GameObject("SupermarketBoost_PistolSlot");
                _placeholder.SetActive(false);
                Object.DontDestroyOnLoad(_placeholder);
            }
            if (System.Array.IndexOf(arr, _placeholder) >= 0) return;
            int idx = arr.Length;
            var bigger = new GameObject[idx + 1];
            arr.CopyTo(bigger, 0);
            bigger[idx] = _placeholder;
            __instance.equippedPrefabs = bigger;
            if (Index < 0) { Index = idx; Plugin.Log.LogInfo($"Pistol registered as item {Index}"); }
        }

        // The game's ChangeEquipment coroutine has no case for our slot: supply our own.
        [HarmonyPrefix, HarmonyPatch(typeof(PlayerNetwork), "ChangeEquipment")]
        private static bool Equip(PlayerNetwork __instance, int newEquippedItem, ref IEnumerator __result)
        {
            if (Index < 0 || newEquippedItem != Index) return true;
            __result = EquipRoutine(__instance);
            return false;
        }

        private static IEnumerator EquipRoutine(PlayerNetwork p)
        {
            var parent = p.equippedParentOBJ;
            while (parent.transform.childCount > 0)
            {
                Object.Destroy(parent.transform.GetChild(0).gameObject);
                yield return null;
            }
            var model = PistolModel.Build(parent.transform, "SupermarketBoost_Pistol");
            p.instantiatedOBJ = model.gameObject;
            var holder = model.gameObject.AddComponent<PistolHolder>();
            holder.Player = p;
        }

        // Hands on the gun, using the same IK the game uses for boxes and brooms.
        [HarmonyPostfix, HarmonyPatch(typeof(PlayerSyncCharacter), "LateUpdate")]
        private static void HandIK(PlayerSyncCharacter __instance)
        {
            var p = PNet(__instance);
            if (!Holds(p) || p.instantiatedOBJ == null) return;
            var r = RTarget(__instance); var l = LTarget(__instance);
            var rc = RCon(__instance); var lc = LCon(__instance);
            var ri = p.instantiatedOBJ.transform.Find("Kick/RightHandIK");
            var li = p.instantiatedOBJ.transform.Find("Kick/LeftHandIK");
            if (r && ri) { r.SetPositionAndRotation(ri.position, ri.rotation); if (rc) rc.weight = 1f; }
            if (l && li) { l.SetPositionAndRotation(li.position, li.rotation); if (lc) lc.weight = 1f; }
            if (__instance.characterOBJ != null) __instance.characterOBJ.GetComponent<FingerCurl>()?.Mark();
        }

        // Fingers wrap the grip: added once per character, curls after the animator and IK have run.
        [HarmonyPostfix, HarmonyPatch(typeof(PlayerNetwork), "Start")]
        private static void AddCurl(PlayerNetwork __instance)
        {
            var sync = __instance.GetComponent<PlayerSyncCharacter>();
            __instance.StartCoroutine(AttachCurl(sync));
        }

        private static IEnumerator AttachCurl(PlayerSyncCharacter sync)
        {
            while (sync != null && sync.characterOBJ == null) yield return null;
            if (sync != null && sync.characterOBJ.GetComponent<FingerCurl>() == null) sync.characterOBJ.AddComponent<FingerCurl>();
        }

        // PlayerNetwork.Update prints "Equipped item error" every frame for items it doesn't know.
        [HarmonyTranspiler, HarmonyPatch(typeof(PlayerNetwork), "Update")]
        private static IEnumerable<CodeInstruction> QuietUpdate(IEnumerable<CodeInstruction> code)
        {
            var print = AccessTools.Method(typeof(MonoBehaviour), nameof(MonoBehaviour.print));
            var quiet = AccessTools.Method(typeof(PistolItem), nameof(QuietPrint));
            foreach (var ins in code)
                yield return ins.Calls(print) ? new CodeInstruction(System.Reflection.Emit.OpCodes.Call, quiet).MoveLabelsFrom(ins) : ins;
        }

        public static void QuietPrint(object message)
        {
            if (message is string s && s.StartsWith("Equipped item error") && Index >= 0) return;
            Debug.Log(message);
        }

        // The game answers the action button with "Need a broom for this" (message14) on thieves; not with a gun.
        [HarmonyPrefix, HarmonyPatch(typeof(GameCanvas), nameof(GameCanvas.CreateCanvasNotification))]
        private static bool NoBroomNag(string hash) => !(hash == "message14" && Holds(GameHelpers.LocalPlayer));
    }

    /// <summary>Curls the Bip01 finger bones while the pistol is held (the game's rig has no grip pose).</summary>
    [DefaultExecutionOrder(32000)]
    internal class FingerCurl : MonoBehaviour
    {
        public static float Curl = 75f, ThumbCurl = 10f, Axis = 1f; // tuned in game
        private readonly List<Transform> _fingers = new List<Transform>(), _thumbs = new List<Transform>();
        private int _markedFrame = -10;

        public void Mark() => _markedFrame = Time.frameCount;

        private void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Bip01 R Finger") && !t.name.StartsWith("Bip01 L Finger")) continue;
                if (t.name.EndsWith("Nub")) continue;
                if (t.name.Substring(13).StartsWith("Finger0")) _thumbs.Add(t); else _fingers.Add(t);
            }
        }

        private Quaternion[] _base, _last;
        private bool _curled;

        // The animator may or may not write finger bones every frame: curl from the animated pose when it
        // does, from the remembered pose when it doesn't, and put it back when the pistol is put away.
        private void LateUpdate()
        {
            int n = _fingers.Count + _thumbs.Count;
            if (_base == null) { _base = new Quaternion[n]; _last = new Quaternion[n]; }
            bool hold = Time.frameCount - _markedFrame <= 1;
            if (!hold && !_curled) return;
            for (int i = 0; i < n; i++)
            {
                var t = i < _fingers.Count ? _fingers[i] : _thumbs[i - _fingers.Count];
                var cur = t.localRotation;
                if (!_curled || cur != _last[i]) _base[i] = cur; // fresh pose from the animator
                if (!hold) { t.localRotation = _base[i]; continue; }
                float a = i < _fingers.Count ? Curl : ThumbCurl;
                t.localRotation = _last[i] = _base[i] * Quaternion.Euler(0f, 0f, -a * Axis);
            }
            _curled = hold;
        }
    }

    /// <summary>Positions the held pistol: in view for the owner, in the hands (aimed with the head) for others.</summary>
    internal class PistolHolder : MonoBehaviour
    {
        public PlayerNetwork Player;
        private PlayerSyncCharacter _sync;
        public static float ReloadPose; // 0..1, local player only
        public static Vector3 HoldOffset = new Vector3(0.1f, 1.5f, 0.45f); // right, up, forward from the feet

        private static PistolHolder _local;
        private static bool _hooked;

        private bool InView => Player != null && Player.isLocalPlayer && !DevHarness.ForceThirdPerson;

        private void OnEnable()
        {
            if (_hooked) return;
            _hooked = true;
            // The game's camera controller and Cinemachine move the camera late in the frame, after this
            // script's LateUpdate: a gun placed there trails the camera by a frame and jumps around while
            // walking or turning. Place it again right before the camera renders, from its final pose.
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += (ctx, cam) =>
            {
                if (_local != null && _local.InView && cam == Camera.main && !DevHarness.NoRenderHook) _local.PlaceInView(cam);
            };
        }

        public void PlaceInView(Camera cam)
        {
            var pose = Quaternion.Euler(Mathf.Sin(ReloadPose * Mathf.PI) * 55f, 0f, Mathf.Sin(ReloadPose * Mathf.PI) * 35f);
            transform.SetPositionAndRotation(
                cam.transform.TransformPoint(new Vector3(0.13f, -0.1f - 0.1f * Mathf.Sin(ReloadPose * Mathf.PI), 0.33f)),
                cam.transform.rotation * Quaternion.Euler(-1.5f, -4f, 0f) * pose);
        }

        private void LateUpdate()
        {
            if (Player == null) return;
            if (InView)
            {
                _local = this;
                var cam = Camera.main;
                if (cam != null) PlaceInView(cam);
                return;
            }

            if (_sync == null) _sync = Player.GetComponent<PlayerSyncCharacter>();
            float pitch = _sync != null ? -_sync.headAngle : 0f; // headAngle is +up
            var body = Player.transform;
            var aim = Quaternion.AngleAxis(pitch, body.right) * body.forward;
            transform.position = body.position + Vector3.up * HoldOffset.y + body.right * HoldOffset.x + aim * HoldOffset.z;
            transform.rotation = Quaternion.LookRotation(aim, Vector3.up);
        }
    }

    /// <summary>Local player's pistol: draw (G), shoot (action button), reload (R), ammo.</summary>
    internal static class Pistol
    {
        public static int Mag, Reserve;
        private static float _nextShot, _reloadStart = -1f;
        private static bool _wasOpen;
        private static AudioSource _audio;
        private const float ReloadTime = 1.4f;

        public static bool Drawn => PistolItem.Holds(GameHelpers.LocalPlayer);
        public static bool Reloading => _reloadStart >= 0f;

        public static void Init()
        {
            Mag = Plugin.MagSize.Value;
            Reserve = Plugin.ReserveAmmo.Value;
        }

        public static void Tick(Plugin plugin)
        {
            if (!Plugin.PistolEnabled.Value) return;
            var player = GameHelpers.LocalPlayer;
            var cam = Camera.main;

            // Fresh ammo every morning when the store opens.
            bool open = GameData.Instance != null && GameData.Instance.isSupermarketOpen;
            if (open && !_wasOpen) { Mag = Plugin.MagSize.Value; Reserve = Plugin.ReserveAmmo.Value; }
            _wasOpen = open;

            if (player == null || cam == null || PistolItem.Index < 0) return;

            if (Plugin.PistolKey.Value.IsDown() && GameHelpers.InputAllowed)
            {
                if (Drawn) player.CmdChangeEquippedItem(0);
                else if (player.equippedItem != 0) plugin.Toast("Empty your hands first");
                else if (!Net.ServerHasMod) plugin.Toast("The host needs Supermarket Boost for the pistol");
                else player.CmdChangeEquippedItem(PistolItem.Index);
                _reloadStart = -1f;
                return;
            }
            if (!Drawn) { _reloadStart = -1f; PistolHolder.ReloadPose = 0f; return; }

            if (Reloading)
            {
                float t = (Time.time - _reloadStart) / ReloadTime;
                PistolHolder.ReloadPose = Mathf.Clamp01(t);
                if (t >= 1f)
                {
                    int need = Plugin.MagSize.Value - Mag;
                    int take = Plugin.InfiniteReserve.Value ? need : Mathf.Min(need, Reserve);
                    Mag += take;
                    if (!Plugin.InfiniteReserve.Value) Reserve -= take;
                    _reloadStart = -1f;
                    PistolHolder.ReloadPose = 0f;
                }
                return;
            }

            if (GameHelpers.InputAllowed && Plugin.ReloadKey.Value.IsDown()) { StartReload(plugin); return; }

            if (GameHelpers.InputAllowed && Time.time >= _nextShot && GameHelpers.MainActionDown())
            {
                if (Mag <= 0) { Click(); StartReload(plugin); return; }
                _nextShot = Time.time + 0.16f;
                Mag--;
                Fire(cam, player, cam.transform.forward);
            }
        }

        /// <summary>Test harness: fire one round from the camera at a point (uses the real shot path).</summary>
        public static SbShotMsg DevFire(Vector3 target)
        {
            var cam = Camera.main;
            var player = GameHelpers.LocalPlayer;
            Mag--;
            return Fire(cam, player, (target - cam.transform.position).normalized);
        }

        public static void DevReload() => StartReload(Plugin.Instance);

        private static void StartReload(Plugin plugin)
        {
            if (Mag >= Plugin.MagSize.Value) return;
            if (!Plugin.InfiniteReserve.Value && Reserve <= 0) { plugin.Toast("Out of ammo - more tomorrow morning"); return; }
            _reloadStart = Time.time;
            PlayAt(ShotFx.ReloadClip, 0.6f);
        }

        private static void Click() => PlayAt(ShotFx.ClickClip, 0.5f);

        private static void PlayAt(AudioClip clip, float vol)
        {
            if (_audio == null)
            {
                var go = new GameObject("SupermarketBoost_PistolAudio");
                Object.DontDestroyOnLoad(go);
                _audio = go.AddComponent<AudioSource>();
                _audio.spatialBlend = 0f;
            }
            _audio.pitch = 1f;
            _audio.PlayOneShot(clip, vol * Plugin.PistolVolume.Value);
        }

        private static SbShotMsg Fire(Camera cam, PlayerNetwork player, Vector3 dir)
        {
            var model = player.instantiatedOBJ != null ? player.instantiatedOBJ.GetComponent<PistolModel>() : null;
            model?.Fire();
            if (_audio == null) PlayAt(ShotFx.Clip, 0f);
            _audio.pitch = Random.Range(0.93f, 1.07f);
            _audio.PlayOneShot(ShotFx.Clip, Plugin.PistolVolume.Value);

            var shot = Targeting.Trace(cam.transform.position, dir, Plugin.PistolRange.Value, player);
            Vector3 muzzle = model != null ? model.Muzzle.position : cam.transform.position;
            ShotFx.Play(muzzle, shot.Point, shot.Normal, shot.Kind, spatial: false);
            if (shot.Kind == HitKind.Npc || shot.Kind == HitKind.Player) Blood.Spray(shot.Point, dir);

            if (Net.ServerHasMod) Net.SendShot(shot);
            else if (shot.Kind == HitKind.Npc && NetworkClient.spawned.TryGetValue(shot.Target, out var id) && id != null)
            {
                // No modded host: fall back to the game's own broom-hit command, ragdoll locally.
                var npc = id.GetComponent<NPC_Info>();
                if (npc != null)
                {
                    npc.CmdAnimationPlay(0);
                    if (Plugin.Ragdolls.Value) Ragdoll.For(npc.gameObject).Hit(shot.Point, dir, 1f, false);
                }
            }
            return shot;
        }

        public static void OnGUI(GUIStyle style)
        {
            if (!Drawn || !GameHelpers.InputAllowed) return;
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.DrawTexture(new Rect(cx - 9, cy - 1, 6, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + 3, cy - 1, 6, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1, cy - 9, 2, 6), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1, cy + 3, 2, 6), Texture2D.whiteTexture);

            string ammo = Reloading ? "RELOADING..." :
                $"{Mag} / {(Plugin.InfiniteReserve.Value ? "∞" : Reserve.ToString())}" + (Mag == 0 ? "   [R] reload" : "");
            var r = new Rect(Screen.width - 190, Screen.height - 64, 180, 40);
            GUI.color = Color.black;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), ammo, style);
            GUI.color = Mag == 0 && !Reloading ? new Color(1f, 0.4f, 0.3f) : Color.white;
            GUI.Label(r, ammo, style);
            GUI.color = old;
        }
    }

    /// <summary>Hit detection that does not depend on how the game set up NPC colliders.</summary>
    internal static class Targeting
    {
        private const float BodyRadius = 0.32f;
        private const float BodyHeight = 1.75f;

        public static SbShotMsg Trace(Vector3 origin, Vector3 dir, float range, PlayerNetwork self)
        {
            var shot = new SbShotMsg { Origin = origin, Point = origin + dir * range, Kind = HitKind.None };
            float best = range;

            foreach (var hit in Physics.RaycastAll(origin, dir, range, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= best) continue;
                if (hit.collider.transform.IsChildOf(self.transform)) continue;
                if (hit.collider.GetComponentInParent<NPC_Info>() != null) continue;
                if (hit.collider.GetComponentInParent<PlayerNetwork>() != null) continue;
                if (hit.collider.GetComponentInParent<PistolModel>() != null) continue;
                best = hit.distance;
                shot.Point = hit.point;
                shot.Normal = hit.normal;
                shot.Kind = HitKind.World;
            }

            foreach (var npc in Object.FindObjectsByType<NPC_Info>(FindObjectsSortMode.None))
                if (HitsBody(npc.gameObject, origin, dir, ref best, out var p))
                {
                    shot.Point = p; shot.Normal = -dir; shot.Kind = HitKind.Npc; shot.Target = npc.netId;
                }

            foreach (var pl in Object.FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None))
                if (pl != self && HitsBody(pl.gameObject, origin, dir, ref best, out var p))
                {
                    shot.Point = p; shot.Normal = -dir; shot.Kind = HitKind.Player; shot.Target = pl.netId;
                }

            return shot;
        }

        // Standing people: a body-sized capsule. Ragdolled people lie somewhere else: test their limbs.
        private static bool HitsBody(GameObject go, Vector3 o, Vector3 d, ref float best, out Vector3 point)
        {
            point = default;
            var r = go.GetComponent<Ragdoll>();
            if (r == null || !r.IsDown || r.Colliders.Count == 0) return RayHitsBody(o, d, go.transform.position, ref best, out point);
            bool hit = false;
            var ray = new Ray(o, d);
            foreach (var c in r.Colliders)
            {
                if (c == null) continue;
                bool was = c.enabled;
                c.enabled = true;
                // Slightly generous: limbs are thin and lying on the floor.
                if (c.Raycast(ray, out var h, best) && h.distance > 0.2f) { best = h.distance; point = h.point; hit = true; }
                else if (c.bounds.IntersectRay(ray, out float t) && t > 0.2f && t < best && (c.ClosestPoint(o + d * t) - (o + d * t)).sqrMagnitude < 0.01f)
                { best = t; point = o + d * t; hit = true; }
                c.enabled = was;
            }
            return hit;
        }

        private static bool RayHitsBody(Vector3 o, Vector3 d, Vector3 feet, ref float best, out Vector3 point)
        {
            point = default;
            Vector3 a = feet + Vector3.up * BodyRadius;
            Vector3 b = feet + Vector3.up * (BodyHeight - BodyRadius);
            Vector3 u = d, v = b - a, w = o - a;
            float uv = Vector3.Dot(u, v), vv = Vector3.Dot(v, v), uw = Vector3.Dot(u, w), vw = Vector3.Dot(v, w);
            float den = vv - uv * uv;
            float s = den > 1e-5f ? Mathf.Clamp01((vw - uv * uw) / den) : 0f;
            Vector3 onSeg = a + v * s;
            float t = Mathf.Max(0f, Vector3.Dot(onSeg - o, u));
            Vector3 onRay = o + u * t;
            if ((onRay - onSeg).sqrMagnitude > BodyRadius * BodyRadius) return false;
            if (t <= 0.2f || t >= best) return false;
            best = t;
            point = onRay;
            return true;
        }
    }

    /// <summary>Sounds, tracer and bullet hole, played on every client.</summary>
    internal static class ShotFx
    {
        private static Material _tracerMat, _holeMat;

        public static void Play(Vector3 muzzle, Vector3 point, Vector3 normal, HitKind kind, bool spatial)
        {
            if (spatial) AudioSource.PlayClipAtPoint(Clip, muzzle, Plugin.PistolVolume.Value);

            if (_tracerMat == null)
            {
                _tracerMat = GameHelpers.MakeMaterial(new Color(1f, 0.9f, 0.5f), 0f, new Color(3f, 2.4f, 1f));
                _holeMat = GameHelpers.MakeMaterial(new Color(0.03f, 0.03f, 0.03f), 0f);
            }

            Vector3 delta = point - muzzle;
            if (delta.sqrMagnitude > 0.01f)
            {
                var tracer = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(tracer.GetComponent<Collider>());
                tracer.GetComponent<Renderer>().sharedMaterial = _tracerMat;
                tracer.transform.position = muzzle + delta * 0.5f;
                tracer.transform.rotation = Quaternion.LookRotation(delta);
                tracer.transform.localScale = new Vector3(0.008f, 0.008f, delta.magnitude);
                Object.Destroy(tracer, 0.05f);
            }

            if (kind == HitKind.World && normal != Vector3.zero)
            {
                var hole = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(hole.GetComponent<Collider>());
                hole.transform.position = point + normal * 0.003f;
                hole.transform.rotation = Quaternion.LookRotation(-normal);
                hole.transform.localScale = Vector3.one * 0.035f;
                hole.GetComponent<Renderer>().sharedMaterial = _holeMat;
                Object.Destroy(hole, 10f);
            }
        }

        private static AudioClip _clip, _click, _reload;

        public static AudioClip Clip => _clip != null ? _clip : _clip = Synth("Shot", 0.45f, (t, n) =>
            n * Mathf.Exp(-t * 22f) + Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 14f) * 0.8f + n * Mathf.Exp(-t * 6f) * 0.15f);

        public static AudioClip ClickClip => _click != null ? _click : _click = Synth("Click", 0.06f, (t, n) =>
            n * Mathf.Exp(-t * 120f) * 0.7f);

        // Mag out ... mag in ... slide.
        public static AudioClip ReloadClip => _reload != null ? _reload : _reload = Synth("Reload", 1.3f, (t, n) =>
            Burst(t, 0.15f, n) + Burst(t, 0.75f, n) * 1.2f + Burst(t, 1.1f, n) * 1.4f);

        private static float Burst(float t, float at, float n) => t < at ? 0f : n * Mathf.Exp(-(t - at) * 90f);

        private static AudioClip Synth(string name, float seconds, System.Func<float, float, float> f)
        {
            const int rate = 44100;
            int count = (int)(rate * seconds);
            var data = new float[count];
            var rng = new System.Random(19);
            float lp = 0f;
            for (int i = 0; i < count; i++)
            {
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * 0.35f;
                data[i] = Mathf.Clamp(f(i / (float)rate, lp) * 0.9f, -1f, 1f);
            }
            var clip = AudioClip.Create("SupermarketBoost_" + name, count, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
