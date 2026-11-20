using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace SupermarketBoost
{
    // Custom Mirror messages. Mirror disconnects a peer that receives a message type it does not know,
    // so the host only sends these to clients that said hello (i.e. have the mod). A modded client
    // joining an UNmodded host would be kicked by its hello, so that case is documented / configurable.

    public struct SbHelloMsg : NetworkMessage { public int Version; }
    public struct SbGunStateMsg : NetworkMessage { public uint Player; public bool Drawn; }

    public struct SbSpeedMsg : NetworkMessage { public float Speed; }

    public enum HitKind : byte { None = 0, World = 1, Npc = 2, Player = 3 }

    public struct SbShotMsg : NetworkMessage
    {
        public uint Shooter;    // filled in by the server
        public Vector3 Origin;
        public Vector3 Point;
        public Vector3 Normal;
        public HitKind Kind;
        public uint Target;     // netId of the NPC / player hit
    }

    internal static class Net
    {
        public const int ProtocolVersion = 2;

        private static bool _serverReg, _clientReg;
        private static readonly HashSet<int> ModdedConns = new HashSet<int>();
        private static readonly Dictionary<uint, bool> GunStates = new Dictionary<uint, bool>();
        public static bool ServerHasMod { get; private set; }

        static Net()
        {
            Writer<SbHelloMsg>.write = (w, m) => w.WriteInt(m.Version);
            Reader<SbHelloMsg>.read = r => new SbHelloMsg { Version = r.ReadInt() };

            Writer<SbGunStateMsg>.write = (w, m) => { w.WriteUInt(m.Player); w.WriteBool(m.Drawn); };
            Reader<SbGunStateMsg>.read = r => new SbGunStateMsg { Player = r.ReadUInt(), Drawn = r.ReadBool() };

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
            };
            Reader<SbShotMsg>.read = r => new SbShotMsg
            {
                Shooter = r.ReadUInt(),
                Origin = r.ReadVector3(),
                Point = r.ReadVector3(),
                Normal = r.ReadVector3(),
                Kind = (HitKind)r.ReadByte(),
                Target = r.ReadUInt(),
            };
        }

        /// <summary>Call every frame: (re)registers handlers whenever Mirror starts a server / client.</summary>
        public static void Tick()
        {
            if (!Plugin.MultiplayerSync.Value) return;

            if (NetworkServer.active && !_serverReg)
            {
                NetworkServer.RegisterHandler<SbHelloMsg>(OnServerHello, false);
                NetworkServer.RegisterHandler<SbGunStateMsg>(OnServerGunState, false);
                NetworkServer.RegisterHandler<SbShotMsg>(OnServerShot, false);
                _serverReg = true;
            }
            else if (!NetworkServer.active && _serverReg)
            {
                _serverReg = false;
                ModdedConns.Clear();
                GunStates.Clear();
            }

            if (NetworkClient.isConnected && !_clientReg)
            {
                NetworkClient.RegisterHandler<SbHelloMsg>(_ => ServerHasMod = true, false);
                NetworkClient.RegisterHandler<SbGunStateMsg>(OnClientGunState, false);
                NetworkClient.RegisterHandler<SbShotMsg>(OnClientShot, false);
                NetworkClient.RegisterHandler<SbSpeedMsg>(m => { if (!NetworkServer.active) DaySpeed.Speed = m.Speed; }, false);
                _clientReg = true;
                NetworkClient.Send(new SbHelloMsg { Version = ProtocolVersion });
            }
            else if (!NetworkClient.active && _clientReg)
            {
                _clientReg = false;
                ServerHasMod = false;
                RemoteGuns.Clear();
            }
        }

        // ---------- client -> server ----------

        public static void SendGunState(bool drawn)
        {
            if (ServerHasMod) NetworkClient.Send(new SbGunStateMsg { Drawn = drawn });
        }

        public static void SendShot(SbShotMsg shot)
        {
            if (ServerHasMod) NetworkClient.Send(shot);
        }

        // ---------- server ----------

        private static void OnServerHello(NetworkConnectionToClient conn, SbHelloMsg msg)
        {
            ModdedConns.Add(conn.connectionId);
            conn.Send(new SbHelloMsg { Version = ProtocolVersion });
            foreach (var kv in GunStates)
                if (kv.Value) conn.Send(new SbGunStateMsg { Player = kv.Key, Drawn = true });
            if (DaySpeed.Speed > 1f) conn.Send(new SbSpeedMsg { Speed = DaySpeed.Speed });
        }

        private static void OnServerGunState(NetworkConnectionToClient conn, SbGunStateMsg msg)
        {
            if (conn.identity == null) return;
            msg.Player = conn.identity.netId;
            GunStates[msg.Player] = msg.Drawn;
            SendToModded(msg);
        }

        private static void OnServerShot(NetworkConnectionToClient conn, SbShotMsg msg)
        {
            if (conn.identity == null) return;
            msg.Shooter = conn.identity.netId;

            // Gameplay effects happen once, on the host.
            if (msg.Kind == HitKind.Npc && NetworkServer.spawned.TryGetValue(msg.Target, out var id) && id != null)
            {
                var npc = id.GetComponent<NPC_Info>();
                if (npc != null)
                {
                    bool thief = npc.isAThief && npc.productsIDCarrying.Count > 0;
                    int hits = thief ? Mathf.Max(1, Plugin.PistolHitsPerShot.Value) : 1;
                    // Same call the broom / security guard use: thieves drop goods, everyone shows a hit reaction.
                    for (int i = 0; i < hits; i++) npc.AuxiliarAnimationPlay(0);
                }
            }

            SendToModded(msg);
        }

        public static void BroadcastSpeed(float speed)
        {
            if (NetworkServer.active && _serverReg) SendToModded(new SbSpeedMsg { Speed = speed });
        }

        private static void SendToModded<T>(T msg) where T : struct, NetworkMessage
        {
            foreach (var conn in NetworkServer.connections.Values)
                if (conn != null && ModdedConns.Contains(conn.connectionId))
                    conn.Send(msg);
        }

        // ---------- client ----------

        internal static readonly Dictionary<uint, RemoteGun> RemoteGuns = new Dictionary<uint, RemoteGun>();

        private static void OnClientGunState(SbGunStateMsg msg)
        {
            var local = GameHelpers.LocalPlayer;
            if (local != null && local.netId == msg.Player) return; // own gun is the first-person model

            RemoteGuns.TryGetValue(msg.Player, out var gun);
            if (msg.Drawn)
            {
                if (gun == null && NetworkClient.spawned.TryGetValue(msg.Player, out var id) && id != null)
                    RemoteGuns[msg.Player] = RemoteGun.Create(id.gameObject);
            }
            else if (gun != null)
            {
                Object.Destroy(gun.gameObject);
                RemoteGuns.Remove(msg.Player);
            }
        }

        private static void OnClientShot(SbShotMsg msg)
        {
            var local = GameHelpers.LocalPlayer;
            bool mine = local != null && local.netId == msg.Shooter;

            if (!mine)
            {
                Vector3 muzzle = msg.Origin;
                if (RemoteGuns.TryGetValue(msg.Shooter, out var gun) && gun != null)
                {
                    gun.Fire();
                    muzzle = gun.Muzzle.position;
                }
                ShotFx.Play(muzzle, msg.Point, msg.Normal, msg.Kind, spatial: true);
            }

            if (msg.Kind == HitKind.Npc && NetworkClient.spawned.TryGetValue(msg.Target, out var id) && id != null)
            {
                var npc = id.GetComponent<NPC_Info>();
                if (npc != null && Plugin.Ragdolls.Value)
                    Ragdoll.For(npc).Hit(msg.Point, (msg.Point - msg.Origin).normalized);
            }
            else if (msg.Kind == HitKind.Player && local != null && local.netId == msg.Target)
            {
                Knockback.Apply((msg.Point - msg.Origin).normalized);
            }
        }
    }
}
