using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace SupermarketBoost
{
    /// <summary>
    /// Per-limb runtime ragdoll for NPCs, built from the skeleton the first time an NPC is shot.
    /// Bones are found through Unity's humanoid mapping when available, otherwise by bone names
    /// (Mixamo, Unity, Blender, 3ds Max naming all work).
    ///
    /// Anti-glitch rules:
    ///  - The NPC's network root never moves while ragdolled: the host only stops its NavMeshAgent.
    ///    The limbs are simulated locally on every client (cosmetic only).
    ///  - On recovery the host warps the agent to where its ragdoll landed and resumes its path;
    ///    every client blends the limbs back to the animated pose over 0.6 s in world space.
    ///  - Ragdoll colliders exist only while ragdolled and ignore the NPC's own colliders.
    ///  - The Animator keeps its state while disabled, so the NPC carries on where it left off.
    /// </summary>
    internal class Ragdoll : MonoBehaviour
    {
        private enum State { Idle, Ragdolled, Blending }
        private enum Role { Hips, Spine, Head, UpperLeg, LowerLeg, Foot, UpperArm, LowerArm, Hand }

        private static readonly AccessTools.FieldRef<NPC_Info, Animator> AnimRef =
            AccessTools.FieldRefAccess<NPC_Info, Animator>("npcAnimator");
        private static readonly AccessTools.FieldRef<NPC_Info, GameObject> CharRef =
            AccessTools.FieldRefAccess<NPC_Info, GameObject>("characterOBJ");
        private static bool _loggedRig;

        private NPC_Info _npc;
        private Animator _anim;
        private GameObject _char;
        private bool _built, _perLimb;
        private State _state;
        private float _until, _hardUntil, _blendT;

        private readonly List<Transform> _bones = new List<Transform>();   // parent-first; [0] = hips / model
        private readonly List<Rigidbody> _bodies = new List<Rigidbody>();
        private readonly List<Collider> _cols = new List<Collider>();
        private SkinnedMeshRenderer[] _skins;
        private Vector3[] _fromPos;
        private Quaternion[] _fromRot;
        private float _agentSpeed;

        private const float BlendTime = 0.6f;

        public static Ragdoll For(NPC_Info npc)
        {
            var r = npc.GetComponent<Ragdoll>();
            if (r == null) { r = npc.gameObject.AddComponent<Ragdoll>(); r._npc = npc; }
            return r;
        }

        public void Hit(Vector3 point, Vector3 dir)
        {
            if (!_built && !Build()) return;
            if (_state != State.Ragdolled) Begin();

            float now = Time.time;
            _until = now + Plugin.RagdollSeconds.Value;
            _hardUntil = now + Plugin.RagdollSeconds.Value + 4f;

            Rigidbody nearest = _bodies[0];
            float best = float.MaxValue;
            foreach (var rb in _bodies)
            {
                float d = (rb.worldCenterOfMass - point).sqrMagnitude;
                if (d < best) { best = d; nearest = rb; }
            }
            float force = Plugin.RagdollForce.Value;
            nearest.AddForceAtPosition(dir * force * (_perLimb ? 0.6f : 1f), point, ForceMode.Impulse);
            _bodies[0].AddForce((dir * 0.5f + Vector3.up * 0.3f) * force, ForceMode.Impulse);
        }

        // ---------- building ----------

        private bool Build()
        {
            _built = true;
            _anim = AnimRef(_npc);
            _char = CharRef(_npc);
            if (_char == null) return false;
            if (_anim != null) _anim.keepAnimatorStateOnDisable = true;
            _skins = _char.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            var ignore = new List<Collider>(GetComponentsInChildren<Collider>(true));
            var bones = FindBones(_anim, _char.transform);
            if (!_loggedRig) { _loggedRig = true; LogRig(bones); }

            _perLimb = bones.ContainsKey((Role.Hips, 0)) && bones.ContainsKey((Role.Spine, 0)) &&
                       bones.ContainsKey((Role.UpperLeg, -1)) && bones.ContainsKey((Role.UpperLeg, 1)) &&
                       bones.ContainsKey((Role.LowerLeg, -1)) && bones.ContainsKey((Role.LowerLeg, 1));
            if (_perLimb)
            {
                try { BuildLimbs(bones); }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning($"Per-limb ragdoll failed, using simple fall: {e.Message}");
                    Cleanup();
                    _perLimb = false;
                }
            }
            if (!_perLimb) BuildPlank();

            foreach (var c in _cols)
            {
                foreach (var other in ignore) if (other != null) Physics.IgnoreCollision(c, other);
                // Limbs overlap at the joints (torso vs thighs etc.); colliding them launches the body.
                foreach (var own in _cols) if (own != c) Physics.IgnoreCollision(c, own);
                c.enabled = false;
            }
            foreach (var rb in _bodies)
            {
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.solverIterations = 12;
                rb.maxDepenetrationVelocity = 3f;
                rb.angularDrag = 0.3f;
                rb.maxAngularVelocity = 20f;
            }
            _fromPos = new Vector3[_bones.Count];
            _fromRot = new Quaternion[_bones.Count];
            return true;
        }

        private static Dictionary<(Role, int), Transform> FindBones(Animator _anim, Transform root)
        {
            var map = new Dictionary<(Role, int), Transform>();
            if (_anim != null && _anim.isHuman)
            {
                void H(Role r, int side, HumanBodyBones b) { var t = _anim.GetBoneTransform(b); if (t) map[(r, side)] = t; }
                H(Role.Hips, 0, HumanBodyBones.Hips);
                H(Role.Spine, 0, HumanBodyBones.Spine);
                H(Role.Head, 0, HumanBodyBones.Head);
                H(Role.UpperLeg, -1, HumanBodyBones.LeftUpperLeg); H(Role.UpperLeg, 1, HumanBodyBones.RightUpperLeg);
                H(Role.LowerLeg, -1, HumanBodyBones.LeftLowerLeg); H(Role.LowerLeg, 1, HumanBodyBones.RightLowerLeg);
                H(Role.Foot, -1, HumanBodyBones.LeftFoot); H(Role.Foot, 1, HumanBodyBones.RightFoot);
                H(Role.UpperArm, -1, HumanBodyBones.LeftUpperArm); H(Role.UpperArm, 1, HumanBodyBones.RightUpperArm);
                H(Role.LowerArm, -1, HumanBodyBones.LeftLowerArm); H(Role.LowerArm, 1, HumanBodyBones.RightLowerArm);
                H(Role.Hand, -1, HumanBodyBones.LeftHand); H(Role.Hand, 1, HumanBodyBones.RightHand);
                if (map.Count >= 7) return map;
                map.Clear();
            }

            // Name-based: pick the shallowest bone for each role/side.
            var depth = new Dictionary<(Role, int), int>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<Renderer>() != null) continue;
                if (!Classify(t.name, out var role, out int side)) continue;
                int d = 0; for (var p = t; p != null && p != root; p = p.parent) d++;
                var key = (role, side);
                if (!depth.TryGetValue(key, out int old) || d < old) { depth[key] = d; map[key] = t; }
            }
            return map;
        }

        private static readonly Regex Split = new Regex("[^A-Za-z0-9]+|(?<=[a-z0-9])(?=[A-Z])");
        private static readonly HashSet<string> Noise = new HashSet<string> { "mixamorig", "bip001", "bip01", "bip", "def", "bone", "jnt", "b", "c" };

        private static bool Classify(string name, out Role role, out int side)
        {
            role = Role.Hips; side = 0;
            var tokens = Split.Split(name).Where(s => s.Length > 0).Select(s => s.ToLowerInvariant()).Where(s => !Noise.Contains(s)).ToList();
            if (tokens.Any(t => t == "l" || t == "left")) side = -1;
            else if (tokens.Any(t => t == "r" || t == "right")) side = 1;
            string b = string.Concat(tokens.Where(t => t != "l" && t != "r" && t != "left" && t != "right"));
            b = b.TrimEnd('0', '1');
            if (b.Contains("twist") || b.Contains("end") || b.Contains("top") || b.Contains("nub") || b.Contains("ik") || b.Contains("pole")) return false;

            switch (b)
            {
                case "hips": case "hip": case "pelvis": if (side != 0) return false; role = Role.Hips; return true;
                case "spine": case "spine0": case "chest": case "torso": case "upperchest": if (side != 0) return false; role = Role.Spine; return true;
                case "head": if (side != 0) return false; role = Role.Head; return true;
                case "upleg": case "upperleg": case "thigh": case "legupper": role = Role.UpperLeg; break;
                case "leg": case "lowerleg": case "calf": case "shin": case "leglower": case "knee": role = Role.LowerLeg; break;
                case "foot": case "ankle": role = Role.Foot; break;
                case "arm": case "upperarm": case "armupper": role = Role.UpperArm; break;
                case "forearm": case "lowerarm": case "armlower": case "elbow": role = Role.LowerArm; break;
                case "hand": case "wrist": role = Role.Hand; break;
                default: return false;
            }
            return side != 0;
        }

        private void BuildLimbs(Dictionary<(Role, int), Transform> m)
        {
            Transform G(Role r, int s = 0) => m.TryGetValue((r, s), out var t) ? t : null;
            var hips = G(Role.Hips); var spine = G(Role.Spine); var head = G(Role.Head);
            var lul = G(Role.UpperLeg, -1); var rul = G(Role.UpperLeg, 1);
            var lll = G(Role.LowerLeg, -1); var rll = G(Role.LowerLeg, 1);
            float width = Mathf.Max(0.12f, Vector3.Distance(lul.position, rul.position));

            var hipsRb = AddBody(hips, null, 12f, () =>
            {
                var c = hips.gameObject.AddComponent<SphereCollider>();
                c.radius = Local(hips, width * 0.55f);
                return c;
            });

            Transform spineEnd = head ?? ChildToward(spine);
            var spineRb = AddCapsuleBody(spine, spineEnd, hipsRb, 12f, width * 1.1f, 1f, twist: 20f, swing: 25f);

            if (head != null)
                AddBody(head, spineRb, 4f, () =>
                {
                    var c = head.gameObject.AddComponent<SphereCollider>();
                    c.radius = Local(head, width * 0.6f);
                    c.center = head.InverseTransformPoint(head.position + Vector3.up * width * 0.45f);
                    return c;
                }, twist: 30f, swing: 35f);

            foreach (int s in new[] { -1, 1 })
            {
                var ul = G(Role.UpperLeg, s); var ll = G(Role.LowerLeg, s);
                var foot = G(Role.Foot, s) ?? ChildToward(ll);
                var ulRb = AddCapsuleBody(ul, ll, hipsRb, 6f, width * 0.9f, 1f, twist: 15f, swing: 50f);
                if (foot != null) AddCapsuleBody(ll, foot, ulRb, 4f, width * 0.7f, 1.12f, twist: 5f, swing: 70f, hinge: true);

                var ua = G(Role.UpperArm, s); var la = G(Role.LowerArm, s); var hand = G(Role.Hand, s);
                if (ua != null && la != null)
                {
                    var uaRb = AddCapsuleBody(ua, la, spineRb, 2f, width * 0.6f, 1f, twist: 40f, swing: 80f);
                    var end = hand ?? ChildToward(la);
                    if (end != null) AddCapsuleBody(la, end, uaRb, 1.5f, width * 0.5f, 1.35f, twist: 10f, swing: 80f, hinge: true);
                }
            }
        }

        private void BuildPlank()
        {
            _bones.Add(_char.transform);
            var rb = _char.AddComponent<Rigidbody>();
            rb.mass = 60f;
            var c = _char.AddComponent<CapsuleCollider>();
            c.center = new Vector3(0f, 0.9f, 0f); c.height = 1.8f; c.radius = 0.3f;
            _bodies.Add(rb); _cols.Add(c);
        }

        private static Transform ChildToward(Transform t)
        {
            if (t == null || t.childCount == 0) return null;
            Transform best = null; float d = 0f;
            foreach (Transform c in t) { float dd = (c.position - t.position).sqrMagnitude; if (dd > d) { d = dd; best = c; } }
            return best;
        }

        private static float Local(Transform t, float world) => world / Mathf.Max(1e-4f, t.lossyScale.x);

        private Rigidbody AddBody(Transform bone, Rigidbody parent, float mass, System.Func<Collider> makeCollider,
            float twist = 30f, float swing = 40f, bool hinge = false)
        {
            var col = makeCollider();
            var rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            if (parent != null)
            {
                var j = bone.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = parent;
                j.enableProjection = true;
                j.projectionDistance = 0.05f;
                // Knees/elbows: little twist, wide bend in one swing axis, small in the other.
                j.lowTwistLimit = new SoftJointLimit { limit = -twist };
                j.highTwistLimit = new SoftJointLimit { limit = twist };
                j.swing1Limit = new SoftJointLimit { limit = swing };
                j.swing2Limit = new SoftJointLimit { limit = hinge ? 15f : swing * 0.7f };
            }
            _bones.Add(bone); _bodies.Add(rb); _cols.Add(col);
            return rb;
        }

        private Rigidbody AddCapsuleBody(Transform bone, Transform child, Rigidbody parent, float mass, float worldDiameter,
            float lengthScale, float twist = 30f, float swing = 40f, bool hinge = false)
        {
            return AddBody(bone, parent, mass, () =>
            {
                var c = bone.gameObject.AddComponent<CapsuleCollider>();
                Vector3 local = child != null ? bone.InverseTransformPoint(child.position) * lengthScale : Vector3.up * Local(bone, 0.3f);
                Vector3 abs = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
                c.direction = abs.x > abs.y ? (abs.x > abs.z ? 0 : 2) : (abs.y > abs.z ? 1 : 2);
                float length = c.direction == 0 ? abs.x : c.direction == 1 ? abs.y : abs.z;
                c.center = local * 0.5f;
                c.radius = Local(bone, worldDiameter * 0.5f);
                c.height = length + c.radius * 2f;
                return c;
            }, twist, swing, hinge);
        }

        private void Cleanup()
        {
            foreach (var j in GetComponentsInChildren<CharacterJoint>(true)) DestroyImmediate(j);
            foreach (var rb in _bodies) if (rb) DestroyImmediate(rb);
            foreach (var c in _cols) if (c) DestroyImmediate(c);
            _bones.Clear(); _bodies.Clear(); _cols.Clear();
        }

        /// <summary>Diagnostics: report how a character prefab's skeleton maps to ragdoll limbs.</summary>
        public static void DescribePrefab(GameObject prefab)
        {
            var anim = prefab.GetComponentInChildren<Animator>(true);
            var bones = FindBones(anim, prefab.transform);
            var sb = new StringBuilder($"Rig check '{prefab.name}': human={(anim && anim.isHuman)} mapped={bones.Count}: ");
            foreach (var kv in bones) sb.Append($"{kv.Key.Item1}{(kv.Key.Item2 < 0 ? "L" : kv.Key.Item2 > 0 ? "R" : "")}={kv.Value.name} ");
            Plugin.Log.LogInfo(sb.ToString());
            if (bones.Count < 7)
            {
                var names = new StringBuilder("  bones: ");
                int n = 0;
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) { if (n++ > 70) break; names.Append(t.name).Append(", "); }
                Plugin.Log.LogInfo(names.ToString());
            }
        }

        private void LogRig(Dictionary<(Role, int), Transform> bones)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Ragdoll rig: animator={(_anim ? _anim.name : "none")} human={(_anim && _anim.isHuman)}");
            foreach (var kv in bones) sb.AppendLine($"  {kv.Key.Item1} {kv.Key.Item2}: {kv.Value.name}");
            int lines = 0;
            void Dump(Transform t, int d)
            {
                if (lines++ > 80 || d > 9) return;
                sb.AppendLine(new string(' ', d * 2 + 2) + t.name);
                foreach (Transform c in t) Dump(c, d + 1);
            }
            Dump(_char.transform, 0);
            Plugin.Log.LogInfo(sb.ToString());
        }

        // ---------- runtime ----------

        private void Begin()
        {
            _state = State.Ragdolled;
            if (_anim) _anim.enabled = false;
            foreach (var s in _skins) if (s) s.updateWhenOffscreen = true;
            foreach (var c in _cols) c.enabled = true;
            foreach (var rb in _bodies)
            {
                rb.isKinematic = false;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            if (_npc.isServer)
            {
                var agent = GetComponent<NavMeshAgent>();
                if (agent != null && agent.isOnNavMesh)
                {
                    if (agent.speed > 0.01f) _agentSpeed = agent.speed;
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }
            }
        }

        private void FixedUpdate()
        {
            if (_state != State.Ragdolled) return;
            float now = Time.time;
            foreach (var rb in _bodies)
            {
                if (rb.velocity.sqrMagnitude > 15f * 15f) rb.velocity = rb.velocity.normalized * 15f;
                if ((rb.position - transform.position).sqrMagnitude > 6f * 6f) { Recover(); return; }
            }
            bool settled = _bodies[0].velocity.sqrMagnitude < 0.25f;
            if ((now > _until && settled) || now > _hardUntil) Recover();
        }

        private void Recover()
        {
            for (int i = 0; i < _bones.Count; i++)
            {
                _fromPos[i] = _bones[i].position;
                _fromRot[i] = _bones[i].rotation;
            }
            Vector3 landed = _bones[0].position;

            foreach (var rb in _bodies) rb.isKinematic = true;
            foreach (var c in _cols) c.enabled = false;
            if (_anim) _anim.enabled = true;

            if (_npc.isServer)
            {
                var agent = GetComponent<NavMeshAgent>();
                if (agent != null)
                {
                    bool hadPath = agent.isOnNavMesh && agent.hasPath;
                    Vector3 dest = agent.destination;
                    if (NavMesh.SamplePosition(landed, out var nh, 2.5f, NavMesh.AllAreas) &&
                        Mathf.Abs(nh.position.y - transform.position.y) < 0.6f)
                        agent.Warp(nh.position);
                    if (agent.isOnNavMesh)
                    {
                        if (hadPath) agent.SetDestination(dest);
                        agent.isStopped = false;
                        if (_agentSpeed > 0.01f && agent.speed < 0.01f) agent.speed = _agentSpeed;
                    }
                }
            }

            _state = State.Blending;
            _blendT = 0f;
        }

        private void LateUpdate()
        {
            if (_state != State.Blending) return;
            _blendT += Time.deltaTime / BlendTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_blendT));

            int n = _bones.Count;
            var targetPos = new Vector3[n];
            var targetRot = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                if (_perLimb)
                {
                    // The Animator has just written this frame's pose: blend toward it.
                    targetPos[i] = _bones[i].position;
                    targetRot[i] = _bones[i].rotation;
                }
                else
                {
                    // Plank: the model's resting pose is identity under the NPC root (upright).
                    var parent = _bones[i].parent;
                    targetPos[i] = parent.position;
                    targetRot[i] = parent.rotation;
                }
            }
            for (int i = 0; i < n; i++)
            {
                if (i == 0) _bones[i].position = Vector3.Lerp(_fromPos[i], targetPos[i], t);
                _bones[i].rotation = Quaternion.Slerp(_fromRot[i], targetRot[i], t);
            }

            if (_blendT >= 1f)
            {
                if (!_perLimb) { _char.transform.localPosition = Vector3.zero; _char.transform.localRotation = Quaternion.identity; }
                foreach (var s in _skins) if (s) s.updateWhenOffscreen = false;
                _state = State.Idle;
            }
        }
    }
}
