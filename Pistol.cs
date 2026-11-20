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
        private Light _flash;
        private GameObject _flashMesh;
        private float _flashUntil, _recoil, _slideBack;

        private static Material _polymer, _steel, _flashMat;

        public static PistolModel Build(Transform parent, string name)
        {
            if (_polymer == null)
            {
                _polymer = GameHelpers.MakeMaterial(new Color(0.07f, 0.07f, 0.08f), 0.25f);
                _steel = GameHelpers.MakeMaterial(new Color(0.16f, 0.16f, 0.17f), 0.7f);
                _flashMat = GameHelpers.MakeMaterial(new Color(1f, 0.8f, 0.3f), 0f, new Color(4f, 2.5f, 0.8f));
            }

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var m = root.AddComponent<PistolModel>();
            m.Kick = new GameObject("Kick").transform;
            m.Kick.SetParent(root.transform, false);

            m.Slide = new GameObject("Slide").transform;
            m.Slide.SetParent(m.Kick, false);
            GameHelpers.Box(m.Slide, new Vector3(0f, 0.022f, 0.01f), new Vector3(0.028f, 0.032f, 0.18f), _steel);
            GameHelpers.Box(m.Slide, new Vector3(0f, 0.041f, 0.09f), new Vector3(0.005f, 0.007f, 0.006f), _steel);  // front sight
            GameHelpers.Box(m.Slide, new Vector3(0f, 0.041f, -0.07f), new Vector3(0.02f, 0.007f, 0.006f), _steel);  // rear sight
            GameHelpers.Box(m.Kick, new Vector3(0f, -0.004f, 0.005f), new Vector3(0.026f, 0.022f, 0.165f), _polymer); // frame
            var grip = GameHelpers.Box(m.Kick, new Vector3(0f, -0.058f, -0.058f), new Vector3(0.027f, 0.095f, 0.048f), _polymer);
            grip.transform.localRotation = Quaternion.Euler(-16f, 0f, 0f);
            GameHelpers.Box(m.Kick, new Vector3(0f, -0.03f, 0.012f), new Vector3(0.012f, 0.006f, 0.05f), _polymer);  // trigger guard
            GameHelpers.Box(m.Kick, new Vector3(0f, -0.024f, -0.004f), new Vector3(0.006f, 0.018f, 0.006f), _steel); // trigger

            m.Muzzle = new GameObject("Muzzle").transform;
            m.Muzzle.SetParent(m.Kick, false);
            m.Muzzle.localPosition = new Vector3(0f, 0.022f, 0.115f);
            m._flash = m.Muzzle.gameObject.AddComponent<Light>();
            m._flash.type = LightType.Point;
            m._flash.color = new Color(1f, 0.75f, 0.35f);
            m._flash.range = 6f;
            m._flash.intensity = 6f;
            m._flash.enabled = false;
            m._flashMesh = GameHelpers.Box(m.Muzzle, new Vector3(0f, 0f, 0.025f), new Vector3(0.05f, 0.05f, 0.06f), _flashMat);
            m._flashMesh.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            m._flashMesh.SetActive(false);

            // Grip points for the character's two-bone IK (right hand on the grip, left hand cupping it).
            var r = new GameObject("RightHandIK").transform;
            r.SetParent(m.Kick, false);
            r.localPosition = new Vector3(0.012f, -0.055f, -0.06f);
            r.localRotation = Quaternion.Euler(0f, 0f, -90f);
            var l = new GameObject("LeftHandIK").transform;
            l.SetParent(m.Kick, false);
            l.localPosition = new Vector3(-0.03f, -0.07f, -0.045f);
            l.localRotation = Quaternion.Euler(0f, 0f, 90f);
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

    /// <summary>Positions the held pistol: in view for the owner, in the hands (aimed with the head) for others.</summary>
    internal class PistolHolder : MonoBehaviour
    {
        public PlayerNetwork Player;
        private PlayerSyncCharacter _sync;
        public static float ReloadPose; // 0..1, local player only

        private void LateUpdate()
        {
            if (Player == null) return;
            if (Player.isLocalPlayer)
            {
                var cam = Camera.main;
                if (cam == null) return;
                var pose = Quaternion.Euler(Mathf.Sin(ReloadPose * Mathf.PI) * 55f, 0f, Mathf.Sin(ReloadPose * Mathf.PI) * 35f);
                transform.position = cam.transform.TransformPoint(new Vector3(0.17f, -0.16f - 0.1f * Mathf.Sin(ReloadPose * Mathf.PI), 0.36f));
                transform.rotation = cam.transform.rotation * Quaternion.Euler(0f, -3f, 0f) * pose;
                return;
            }

            if (_sync == null) _sync = Player.GetComponent<PlayerSyncCharacter>();
            float pitch = _sync != null ? -_sync.headAngle : 0f; // headAngle is +up
            var body = Player.transform;
            var aim = Quaternion.AngleAxis(pitch, body.right) * body.forward;
            transform.position = body.position + Vector3.up * 1.32f + body.right * 0.12f + aim * 0.42f;
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
                Fire(cam, player);
            }
        }

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

        private static void Fire(Camera cam, PlayerNetwork player)
        {
            var model = player.instantiatedOBJ != null ? player.instantiatedOBJ.GetComponent<PistolModel>() : null;
            model?.Fire();
            if (_audio == null) PlayAt(ShotFx.Clip, 0f);
            _audio.pitch = Random.Range(0.93f, 1.07f);
            _audio.PlayOneShot(ShotFx.Clip, Plugin.PistolVolume.Value);

            var shot = Targeting.Trace(cam.transform.position, cam.transform.forward, Plugin.PistolRange.Value, player);
            Vector3 muzzle = model != null ? model.Muzzle.position : cam.transform.position;
            ShotFx.Play(muzzle, shot.Point, shot.Normal, shot.Kind, spatial: false);
            if (shot.Kind == HitKind.Npc || shot.Kind == HitKind.Player) Blood.Spray(shot.Point, cam.transform.forward);

            if (Net.ServerHasMod) Net.SendShot(shot);
            else if (shot.Kind == HitKind.Npc && NetworkClient.spawned.TryGetValue(shot.Target, out var id) && id != null)
            {
                // No modded host: fall back to the game's own broom-hit command, ragdoll locally.
                var npc = id.GetComponent<NPC_Info>();
                if (npc != null)
                {
                    npc.CmdAnimationPlay(0);
                    if (Plugin.Ragdolls.Value) Ragdoll.For(npc.gameObject).Hit(shot.Point, cam.transform.forward, 1f, false);
                }
            }
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
            var r = new Rect(Screen.width - 260, Screen.height - 70, 240, 40);
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
                if (RayHitsBody(origin, dir, BodyFeet(npc.gameObject), ref best, out var p))
                {
                    shot.Point = p; shot.Normal = -dir; shot.Kind = HitKind.Npc; shot.Target = npc.netId;
                }

            foreach (var pl in Object.FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None))
                if (pl != self && RayHitsBody(origin, dir, BodyFeet(pl.gameObject), ref best, out var p))
                {
                    shot.Point = p; shot.Normal = -dir; shot.Kind = HitKind.Player; shot.Target = pl.netId;
                }

            return shot;
        }

        // While someone is ragdolled their body is not where their root is: aim at the body.
        private static Vector3 BodyFeet(GameObject go)
        {
            var r = go.GetComponent<Ragdoll>();
            if (r != null && r.IsDown && r.Pelvis != null) return r.Pelvis.position - Vector3.up * 0.9f;
            return go.transform.position;
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
