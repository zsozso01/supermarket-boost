using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SupermarketBoost
{
    // Custom Mirror messages. Mirror disconnects a peer that receives a message type it does not know,
    // so the host only sends these to clients that said hello (i.e. have the mod). A modded client
    // joining an UNmodded host would be kicked by its hello, so that case is documented / configurable.

    public struct SbHelloMsg : NetworkMessage { public int Version; }
    public struct SbSpeedMsg : NetworkMessage { public float Speed; }

    public enum HitKind : byte { None = 0, World = 1, Npc = 2, Player = 3 }

    /// <summary>A gunshot (Fx = true) or a broom whack (Fx = false), relayed by the host to everyone.</summary>
    public struct SbShotMsg : NetworkMessage
    {
        public uint Shooter;    // filled in by the server
        public Vector3 Origin;
        public Vector3 Point;
        public Vector3 Normal;
        public HitKind Kind;
        public uint Target;     // netId of the NPC / player hit
        public bool Fx;         // play gun sound / tracer / blood
        public bool KnockOut;   // host decided: stays down
        public float Force;     // ragdoll push, 1 = gun
    }

    [HarmonyPatch]
    internal static class Net
    {
        public const int ProtocolVersion = 3;

        private static bool _serverReg, _clientReg;
        private static readonly HashSet<int> ModdedConns = new HashSet<int>();
        public static bool ServerHasMod { get; private set; }

        // Host-side hit counting for knockouts.
        private static readonly Dictionary<uint, (int hits, float last)> Hits = new Dictionary<uint, (int, float)>();
        private static readonly HashSet<uint> KnockedOut = new HashSet<uint>();
        private static bool _suppressBroomHook;

        static Net()
        {
            Writer<SbHelloMsg>.write = (w, m) => w.WriteInt(m.Version);
            Reader<SbHelloMsg>.read = r => new SbHelloMsg { Version = r.ReadInt() };

            Writer<SbSpeedMsg>.write = (w, m) => w.WriteFloat(m.Speed);
            Reader<SbSpeedMsg>.read = r => new SbSpeedMsg { Speed = r.ReadFloat() };

            Writer<SbShotMsg>.write = (w, m) =>
            {
                w.WriteUInt(m.Shooter);
                w.WriteVector3(m.Origin);
                w.WriteVector3(m.Point);
                w.WriteVector3(m.Normal);
                w.WriteByte((byte)m.Kind);
                w.WriteUInt(m.Target);
                w.WriteBool(m.Fx);
                w.WriteBool(m.KnockOut);
                w.WriteFloat(m.Force);
            };
            Reader<SbShotMsg>.read = r => new SbShotMsg
            {
                Shooter = r.ReadUInt(),
                Origin = r.ReadVector3(),
                Point = r.ReadVector3(),
                Normal = r.ReadVector3(),
                Kind = (HitKind)r.ReadByte(),
                Target = r.ReadUInt(),
                Fx = r.ReadBool(),
                KnockOut = r.ReadBool(),
                Force = r.ReadFloat(),
            };
        }

        /// <summary>Call every frame: (re)registers handlers whenever Mirror starts a server / client.</summary>
        public static void Tick()
        {
            if (!Plugin.MultiplayerSync.Value) return;

            if (NetworkServer.active && !_serverReg)
            {
                NetworkServer.RegisterHandler<SbHelloMsg>(OnServerHello, false);
                NetworkServer.RegisterHandler<SbShotMsg>(OnServerShot, false);
                _serverReg = true;
            }
            else if (!NetworkServer.active && _serverReg)
            {
                _serverReg = false;
                ModdedConns.Clear();
                Hits.Clear();
                KnockedOut.Clear();
            }

            if (NetworkClient.isConnected && !_clientReg)
            {
                NetworkClient.RegisterHandler<SbHelloMsg>(_ => { ServerHasMod = true; Plugin.Log.LogInfo("Host has Supermarket Boost: sync on"); }, false);
                NetworkClient.RegisterHandler<SbShotMsg>(OnClientShot, false);
                NetworkClient.RegisterHandler<SbSpeedMsg>(m => { if (!NetworkServer.active) DaySpeed.Speed = m.Speed; }, false);
                _clientReg = true;
                NetworkClient.Send(new SbHelloMsg { Version = ProtocolVersion });
            }
            else if (!NetworkClient.active && _clientReg)
            {
                _clientReg = false;
                ServerHasMod = false;
            }
        }

        public static void SendShot(SbShotMsg shot)
        {
            shot.Fx = true;
            shot.Force = 1f;
            if (ServerHasMod) NetworkClient.Send(shot);
        }

        public static void BroadcastSpeed(float speed)
        {
            if (NetworkServer.active && _serverReg) SendToModded(new SbSpeedMsg { Speed = speed });
        }

        // ---------- server ----------

        private static void OnServerHello(NetworkConnectionToClient conn, SbHelloMsg msg)
        {
            Plugin.Log.LogInfo($"Player {conn.connectionId} has Supermarket Boost (protocol {msg.Version}, mine {ProtocolVersion})");
            if (msg.Version != ProtocolVersion)
            {
                Plugin.Instance?.Toast("A player has a different Supermarket Boost version - restart both games to update", 8f);
                return;
            }
            ModdedConns.Add(conn.connectionId);
            conn.Send(new SbHelloMsg { Version = ProtocolVersion });
            if (DaySpeed.Speed > 1f) conn.Send(new SbSpeedMsg { Speed = DaySpeed.Speed });
        }

        private static void OnServerShot(NetworkConnectionToClient conn, SbShotMsg msg)
        {
            if (conn.identity == null) return;
            msg.Shooter = conn.identity.netId;
            msg.Fx = true;
            msg.Force = 1f;

            if (msg.Kind == HitKind.Npc && NetworkServer.spawned.TryGetValue(msg.Target, out var id) && id != null)
            {
                var npc = id.GetComponent<NPC_Info>();
                if (npc != null) msg.KnockOut = ServerNpcHit(npc);
            }
            else if (msg.Kind == HitKind.Player && !Plugin.PlayerRagdolls.Value)
            {
                msg.Kind = HitKind.None;
            }

            SendToModded(msg);
        }

        /// <summary>Applies a gunshot to an NPC on the host. Returns true if it is (now) knocked out.</summary>
        private static bool ServerNpcHit(NPC_Info npc)
        {
            uint key = npc.netId;
            if (KnockedOut.Contains(key)) return true;

            bool thief = npc.isAThief && npc.productsIDCarrying.Count > 0;
            int perShot = thief ? Mathf.Max(1, Plugin.PistolHitsPerShot.Value) : 1;
            _suppressBroomHook = true;
            try { for (int i = 0; i < perShot; i++) npc.AuxiliarAnimationPlay(0); }
            finally { _suppressBroomHook = false; }

            float now = Time.time;
            Hits.TryGetValue(key, out var h);
            int hits = (now - h.last > 30f ? 0 : h.hits) + 1;
            Hits[key] = (hits, now);

            if (hits < Plugin.ShotsToKnockOut.Value || !CanKnockOut(npc)) return false;
            KnockOutOnServer(npc);
            return true;
        }

        /// <summary>
        /// Only NPCs whose removal can't jam anything: shoppers still collecting items, thieves and people
        /// already walking out. Customers in a checkout line or at a self-checkout, and staff, always get up.
        /// </summary>
        private static bool CanKnockOut(NPC_Info npc)
        {
            if (npc.isEmployee || !npc.isCustomer) return false;
            if (npc.selfcheckoutAssigned) return false;
            if (npc.isAThief) return true;
            return npc.productsIDToBuy.Count > 0 || npc.state == 99;
        }

        private static void KnockOutOnServer(NPC_Info npc)
        {
            KnockedOut.Add(npc.netId);
            _suppressBroomHook = true;
            try
            {
                // Thieves drop everything they stole.
                for (int guard = 0; guard < 40 && npc.isAThief && npc.productsIDCarrying.Count > 0; guard++)
                    npc.AuxiliarAnimationPlay(0);
            }
            finally { _suppressBroomHook = false; }
            npc.state = -1; // the game's customer AI skips NPCs in state -1
            Plugin.Instance.StartCoroutine(RemoveBody(npc.gameObject));
        }

        private static IEnumerator RemoveBody(GameObject body)
        {
            yield return new WaitForSecondsRealtime(Plugin.KnockOutBodySeconds.Value);
            if (body != null && NetworkServer.active) NetworkServer.Destroy(body);
        }

        // Broom (and security guard) hits: the game runs AuxiliarAnimationPlay on the host. Ragdoll for everyone.
        [HarmonyPostfix, HarmonyPatch(typeof(NPC_Info), nameof(NPC_Info.AuxiliarAnimationPlay))]
        private static void BroomHit(NPC_Info __instance)
        {
            if (_suppressBroomHook || !NetworkServer.active || !_serverReg || !Plugin.BroomRagdolls.Value) return;
            Vector3 pos = __instance.transform.position + Vector3.up * 1.1f;
            Vector3 from = pos - __instance.transform.forward;
            float best = 6f;
            foreach (var p in Object.FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None))
            {
                float d = Vector3.Distance(p.transform.position, __instance.transform.position);
                if (d < best) { best = d; from = p.transform.position + Vector3.up * 1.1f; }
            }
            SendToModded(new SbShotMsg
            {
                Origin = from, Point = pos, Kind = HitKind.Npc, Target = __instance.netId,
                Fx = false, KnockOut = KnockedOut.Contains(__instance.netId), Force = 0.45f,
            });
        }

        private static void SendToModded<T>(T msg) where T : struct, NetworkMessage
        {
            foreach (var conn in NetworkServer.connections.Values)
                if (conn != null && ModdedConns.Contains(conn.connectionId))
                    conn.Send(msg);
        }

        // ---------- client ----------

        private static void OnClientShot(SbShotMsg msg)
        {
            var local = GameHelpers.LocalPlayer;
            bool mine = local != null && local.netId == msg.Shooter;
            Vector3 dir = (msg.Point - msg.Origin).normalized;

            if (msg.Fx && !mine)
            {
                Vector3 muzzle = msg.Origin;
                if (NetworkClient.spawned.TryGetValue(msg.Shooter, out var sid) && sid != null)
                {
                    var shooter = sid.GetComponent<PlayerNetwork>();
                    var model = shooter != null && shooter.instantiatedOBJ != null ? shooter.instantiatedOBJ.GetComponent<PistolModel>() : null;
                    if (model != null) { model.Fire(); muzzle = model.Muzzle.position; }
                }
                ShotFx.Play(muzzle, msg.Point, msg.Normal, msg.Kind, spatial: true);
                if (msg.Kind == HitKind.Npc || msg.Kind == HitKind.Player) Blood.Spray(msg.Point, dir);
            }

            if ((msg.Kind == HitKind.Npc || msg.Kind == HitKind.Player) &&
                NetworkClient.spawned.TryGetValue(msg.Target, out var id) && id != null && Plugin.Ragdolls.Value)
            {
                var rd = Ragdoll.For(id.gameObject);
                bool wasOut = rd.KnockedOut;
                rd.Hit(msg.Point, dir, msg.Force, msg.KnockOut);
                if (msg.KnockOut && !wasOut && rd.Pelvis != null) Blood.Pool(rd.Pelvis.position);
            }
        }
    }
}
