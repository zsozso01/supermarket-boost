using System.Collections;
using Mirror;
using StarterAssets;
using UnityEngine;

namespace SupermarketBoost
{
    /// <summary>Builds the pistol mesh (first-person and third-person use the same one).</summary>
    internal class PistolModel
    {
        public GameObject Root;
        public Transform Kick;
        public Transform Muzzle;
        private Light _flash;
        private GameObject _flashMesh;
        private float _flashUntil;
        private float _recoil;

        private static Material _polymer, _steel, _flashMat;

        public static PistolModel Build(Transform parent, string name)
        {
            if (_polymer == null)
            {
                _polymer = GameHelpers.MakeMaterial(new Color(0.07f, 0.07f, 0.08f), 0.25f);
                _steel = GameHelpers.MakeMaterial(new Color(0.16f, 0.16f, 0.17f), 0.7f);
                _flashMat = GameHelpers.MakeMaterial(new Color(1f, 0.8f, 0.3f), 0f, new Color(4f, 2.5f, 0.8f));
            }

            var m = new PistolModel { Root = new GameObject(name) };
            m.Root.transform.SetParent(parent, false);
            m.Kick = new GameObject("Kick").transform;
            m.Kick.SetParent(m.Root.transform, false);

            GameHelpers.Box(m.Kick, new Vector3(0f, 0.022f, 0.01f), new Vector3(0.028f, 0.032f, 0.18f), _steel);      // slide
            GameHelpers.Box(m.Kick, new Vector3(0f, -0.004f, 0.005f), new Vector3(0.026f, 0.022f, 0.165f), _polymer); // frame
            var grip = GameHelpers.Box(m.Kick, new Vector3(0f, -0.058f, -0.058f), new Vector3(0.027f, 0.095f, 0.048f), _polymer);
            grip.transform.localRotation = Quaternion.Euler(-16f, 0f, 0f);
            GameHelpers.Box(m.Kick, new Vector3(0f, -0.03f, 0.012f), new Vector3(0.012f, 0.006f, 0.05f), _polymer);   // trigger guard
            GameHelpers.Box(m.Kick, new Vector3(0f, -0.024f, -0.004f), new Vector3(0.006f, 0.018f, 0.006f), _steel); // trigger
            GameHelpers.Box(m.Kick, new Vector3(0f, 0.041f, 0.09f), new Vector3(0.005f, 0.007f, 0.006f), _steel);    // front sight
            GameHelpers.Box(m.Kick, new Vector3(0f, 0.041f, -0.07f), new Vector3(0.02f, 0.007f, 0.006f), _steel);    // rear sight

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
            return m;
        }

        public void Fire()
        {
            _recoil = 1f;
            _flashUntil = Time.time + 0.05f;
        }

        /// <summary>Animate recoil and flash; call every frame.</summary>
        public void Animate(float kickBack = 0.05f, float kickUp = 18f)
        {
            if (Kick == null) return;
            _recoil = Mathf.MoveTowards(_recoil, 0f, Time.deltaTime * 6f);
            Kick.localPosition = new Vector3(0f, 0f, -kickBack * _recoil);
            Kick.localRotation = Quaternion.Euler(-kickUp * _recoil, 0f, 0f);
            bool on = Time.time < _flashUntil;
            _flash.enabled = on;
            _flashMesh.SetActive(on);
        }
    }

    /// <summary>
    /// Local player's pistol. G (with empty hands) draws it; the action button shoots.
    /// Hits are decided here (on the shooter's screen) and sent to the host, which applies the gameplay
    /// effect once and relays the shot to everyone for sound, flash, tracer, ragdoll and knockback.
    /// </summary>
    internal static class Pistol
    {
        private static bool _drawn;
        private static PistolModel _model;
        private static AudioSource _audio;
        private static float _nextShot;

        public static bool Drawn => _drawn && _model?.Root != null;

        public static void Tick(Plugin plugin)
        {
            if (!Plugin.PistolEnabled.Value) return;
            var player = GameHelpers.LocalPlayer;
            var cam = Camera.main;
            if (player == null || cam == null) { if (_drawn) Holster(); return; }

            if (Plugin.PistolKey.Value.IsDown() && GameHelpers.InputAllowed)
            {
                if (_drawn) Holster();
                else if (player.equippedItem != 0) plugin.Toast("Empty your hands first");
                else Draw(cam);
                return;
            }

            if (!_drawn) return;
            if (_model?.Root == null || _model.Root.transform.parent != cam.transform) { Holster(); Draw(cam); }
            if (player.equippedItem != 0) { Holster(); return; }

            _model.Animate();

            if (GameHelpers.InputAllowed && Time.time >= _nextShot && GameHelpers.MainActionDown())
            {
                _nextShot = Time.time + 0.25f;
                Fire(plugin, cam, player);
            }
        }

        private static void Fire(Plugin plugin, Camera cam, PlayerNetwork player)
        {
            _model.Fire();
            _audio.pitch = Random.Range(0.93f, 1.07f);
            _audio.Play();

            var shot = Targeting.Trace(cam.transform.position, cam.transform.forward, Plugin.PistolRange.Value, player);
            ShotFx.Play(_model.Muzzle.position, shot.Point, shot.Normal, shot.Kind, spatial: false);

            if (shot.Kind == HitKind.Npc && NetworkClient.spawned.TryGetValue(shot.Target, out var id) && id != null)
            {
                var npc = id.GetComponent<NPC_Info>();
                if (npc != null && npc.isAThief && npc.productsIDCarrying.Count > 0) plugin.Toast("Thief hit!");
            }

            if (Net.ServerHasMod)
            {
                Net.SendShot(shot);
            }
            else if (shot.Kind == HitKind.Npc && NetworkClient.spawned.TryGetValue(shot.Target, out var id2) && id2 != null)
            {
                // No modded host to relay through: fall back to the game's own broom-hit command.
                var npc = id2.GetComponent<NPC_Info>();
                if (npc != null)
                {
                    npc.CmdAnimationPlay(0);
                    if (Plugin.Ragdolls.Value) Ragdoll.For(npc).Hit(shot.Point, cam.transform.forward);
                }
            }
        }

        private static void Holster()
        {
            if (_drawn) Net.SendGunState(false);
            _drawn = false;
            if (_model?.Root != null) Object.Destroy(_model.Root);
            _model = null;
        }

        private static void Draw(Camera cam)
        {
            _drawn = true;
            _model = PistolModel.Build(cam.transform, "SupermarketBoost_Pistol");
            _model.Root.transform.localPosition = new Vector3(0.17f, -0.16f, 0.36f);
            _model.Root.transform.localRotation = Quaternion.Euler(0f, -3f, 0f);

            _audio = _model.Root.AddComponent<AudioSource>();
            _audio.clip = ShotFx.Clip;
            _audio.spatialBlend = 0f;
            _audio.volume = Plugin.PistolVolume.Value;
            _audio.playOnAwake = false;

            Net.SendGunState(true);
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
                if (hit.collider.GetComponentInParent<NPC_Info>() != null) continue;      // handled below
                if (hit.collider.GetComponentInParent<PlayerNetwork>() != null) continue; // handled below
                best = hit.distance;
                shot.Point = hit.point;
                shot.Normal = hit.normal;
                shot.Kind = HitKind.World;
            }

            foreach (var npc in Object.FindObjectsByType<NPC_Info>(FindObjectsSortMode.None))
                if (RayHitsBody(origin, dir, npc.transform.position, ref best, out var p))
                {
                    shot.Point = p; shot.Normal = -dir; shot.Kind = HitKind.Npc; shot.Target = npc.netId;
                }

            foreach (var pl in Object.FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None))
                if (pl != self && RayHitsBody(origin, dir, pl.transform.position, ref best, out var p))
                {
                    shot.Point = p; shot.Normal = -dir; shot.Kind = HitKind.Player; shot.Target = pl.netId;
                }

            return shot;
        }

        // Ray vs. vertical capsule standing at feet.
        private static bool RayHitsBody(Vector3 o, Vector3 d, Vector3 feet, ref float best, out Vector3 point)
        {
            point = default;
            Vector3 a = feet + Vector3.up * BodyRadius;
            Vector3 b = feet + Vector3.up * (BodyHeight - BodyRadius);
            // closest points between ray (o + t d) and segment (a + s (b - a))
            Vector3 u = d, v = b - a, w = o - a;
            float uu = 1f, uv = Vector3.Dot(u, v), vv = Vector3.Dot(v, v), uw = Vector3.Dot(u, w), vw = Vector3.Dot(v, w);
            float den = uu * vv - uv * uv;
            float s = den > 1e-5f ? Mathf.Clamp01((uu * vw - uv * uw) / den) : 0f;
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

    /// <summary>Sound, tracer and bullet hole, played on every client.</summary>
    internal static class ShotFx
    {
        private static Material _tracerMat, _holeMat;

        public static void Play(Vector3 muzzle, Vector3 point, Vector3 normal, HitKind kind, bool spatial)
        {
            if (spatial)
                AudioSource.PlayClipAtPoint(Clip, muzzle, Plugin.PistolVolume.Value);

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

        private static AudioClip _clip;

        // Synthesised gunshot: noise burst + low thump, so the mod ships with no sound files.
        public static AudioClip Clip
        {
            get
            {
                if (_clip != null) return _clip;
                const int rate = 44100;
                int n = (int)(rate * 0.45f);
                var data = new float[n];
                var rng = new System.Random(19);
                float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)rate;
                    float noise = (float)(rng.NextDouble() * 2 - 1);
                    lp += (noise - lp) * 0.35f;
                    float crack = lp * Mathf.Exp(-t * 22f);
                    float thump = Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 14f) * 0.8f;
                    float tail = lp * Mathf.Exp(-t * 6f) * 0.15f;
                    data[i] = Mathf.Clamp((crack + thump + tail) * 0.9f, -1f, 1f);
                }
                _clip = AudioClip.Create("SupermarketBoost_Shot", n, 1, rate, false);
                _clip.SetData(data, 0);
                return _clip;
            }
        }
    }

    /// <summary>Pistol in another player's hand, with the arm raised to point it forward.</summary>
    internal class RemoteGun : MonoBehaviour
    {
        private PistolModel _model;
        private Animator _anim;
        private Transform _upper, _lower, _hand, _body;
        public Transform Muzzle => _model.Muzzle;

        public static RemoteGun Create(GameObject player)
        {
            var go = new GameObject("SupermarketBoost_RemoteGun");
            var rg = go.AddComponent<RemoteGun>();
            rg._body = player.transform;
            rg._model = PistolModel.Build(go.transform, "Model");
            var audio = go.AddComponent<AudioSource>();
            audio.spatialBlend = 1f;
            return rg;
        }

        public void Fire() => _model.Fire();

        private void LateUpdate()
        {
            if (_body == null) { Destroy(gameObject); return; }

            if (_anim == null)
            {
                var sync = _body.GetComponent<PlayerSyncCharacter>();
                if (sync != null && sync.characterOBJ != null) _anim = sync.characterOBJ.GetComponentInChildren<Animator>();
                if (_anim != null && _anim.isHuman)
                {
                    _upper = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    _lower = _anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
                    _hand = _anim.GetBoneTransform(HumanBodyBones.RightHand);
                }
            }

            Vector3 aim = _body.forward;
            if (_upper != null && _lower != null && _hand != null)
            {
                // Point the whole arm straight forward (runs after the Animator each frame).
                _upper.rotation = Quaternion.FromToRotation(_hand.position - _upper.position, aim) * _upper.rotation;
                _lower.rotation = Quaternion.FromToRotation(_hand.position - _lower.position, aim) * _lower.rotation;
                transform.SetPositionAndRotation(_hand.position + aim * 0.06f, Quaternion.LookRotation(aim));
            }
            else
            {
                transform.SetPositionAndRotation(_body.position + Vector3.up * 1.3f + _body.right * 0.25f + aim * 0.45f,
                    Quaternion.LookRotation(aim));
            }

            _model.Animate(0.04f, 25f);
        }
    }

    /// <summary>Getting shot launches you a bit. Only runs on the player who was hit.</summary>
    internal static class Knockback
    {
        public static void Apply(Vector3 dir)
        {
            var fpc = FirstPersonController.Instance;
            if (fpc == null) return;
            var cc = fpc.GetComponent<CharacterController>();
            if (cc == null) return;
            fpc.StartCoroutine(Push(cc, new Vector3(dir.x, 0f, dir.z).normalized));
        }

        private static IEnumerator Push(CharacterController cc, Vector3 dir)
        {
            float strength = Plugin.PlayerKnockback.Value;
            float t = 0f;
            while (t < 0.35f && cc != null)
            {
                float k = 1f - t / 0.35f;
                cc.Move((dir * strength + Vector3.up * strength * 0.35f) * k * Time.deltaTime);
                t += Time.deltaTime;
                yield return null;
            }
        }
    }
}
