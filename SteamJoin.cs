using System;
using Mirror;
using Steamworks;
using UnityEngine;

namespace SupermarketBoost
{
    /// <summary>
    /// Makes Steam's own "Join Game" / invites work without lobby codes:
    ///  - while you are in a lobby, your Steam rich presence advertises it (friends see "Join Game");
    ///  - "Join Game" while the game is running is handled (GameRichPresenceJoinRequested_t);
    ///  - when Steam launches the game to join (+connect_lobby on the command line), we join once
    ///    the main menu is ready. The game itself only handled the in-game lobby invite case.
    ///  - F7 in a game opens Steam's invite dialog for your lobby.
    /// </summary>
    [HarmonyLib.HarmonyPatch]
    internal static class SteamJoin
    {
        // The game's own invite handler (SteamLobby, only alive inside a store) joins the Steam lobby but
        // never connects. Handle invites ourselves: join through the main menu, queued if you are in a store.
        [HarmonyLib.HarmonyPrefix, HarmonyLib.HarmonyPatch(typeof(SteamLobby), "OnJoinRequest")]
        private static bool SkipGameInviteHandler() => false;

        private static void OnInviteAccepted(GameLobbyJoinRequested_t callback)
        {
            Plugin.Log.LogInfo($"Steam invite accepted for lobby {callback.m_steamIDLobby.m_SteamID} (in menu: {MainMenu.InMenu})");
            _pendingLobby = callback.m_steamIDLobby.m_SteamID;
            if (!MainMenu.InMenu)
                Plugin.Instance?.Toast("Invite accepted - save and quit to the main menu, you'll join automatically", 10f);
        }

        private static Callback<GameRichPresenceJoinRequested_t> _rpJoin;
        private static Callback<GameLobbyJoinRequested_t> _invite;
        private static ulong _pendingLobby;
        private static bool _initialized, _presenceSet;
        private static float _nextJoinTry;
        private static float _nextPresence;

        public static void Tick(Plugin plugin)
        {
            if (!SteamManager.Initialized) return;
            if (!_initialized) Init();

            // Join a lobby that Steam asked for, as soon as the main menu can take it.
            if (_pendingLobby != 0 && MainMenu.InMenu && Time.unscaledTime > 6f && Time.unscaledTime > _nextJoinTry)
            {
                _nextJoinTry = Time.unscaledTime + 2f;
                ulong id = _pendingLobby;
                if (MainMenu.JoinLobby(id))
                {
                    _pendingLobby = 0;
                    plugin.Toast("Joining your friend...");
                }
            }

            if (Time.unscaledTime > _nextPresence)
            {
                _nextPresence = Time.unscaledTime + 3f;
                UpdatePresence();
            }

            if (Plugin.InviteKey.Value.IsDown() && NetworkClient.isConnected)
            {
                ulong lobby = SteamLobby.Instance != null ? SteamLobby.Instance.CurrentLobbyID : 0;
                if (lobby == 0) plugin.Toast("This game is private - use Continue in the main menu to host for friends");
                else SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(lobby));
            }
        }

        private static void Init()
        {
            _initialized = true;
            _rpJoin = Callback<GameRichPresenceJoinRequested_t>.Create(OnRichPresenceJoin);
            _invite = Callback<GameLobbyJoinRequested_t>.Create(OnInviteAccepted);

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out var id)) _pendingLobby = id;
            if (_pendingLobby == 0 && SteamApps.GetLaunchCommandLine(out string cmd, 1024) > 0) ParseConnect(cmd);
            if (_pendingLobby != 0) Plugin.Log.LogInfo($"Launched by Steam to join lobby {_pendingLobby}");
        }

        private static void OnRichPresenceJoin(GameRichPresenceJoinRequested_t cb)
        {
            ParseConnect(cb.m_rgchConnect);
            if (_pendingLobby != 0 && !MainMenu.InMenu)
                Plugin.Log.LogInfo("Join request received while in a game: it will happen after you leave to the main menu");
        }

        private static void ParseConnect(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            int i = s.IndexOf("+connect_lobby", StringComparison.Ordinal);
            if (i < 0) return;
            var parts = s.Substring(i + "+connect_lobby".Length).Trim().Split(' ');
            if (parts.Length > 0 && ulong.TryParse(parts[0], out var id)) _pendingLobby = id;
        }

        private static void UpdatePresence()
        {
            ulong lobby = SteamLobby.Instance != null ? SteamLobby.Instance.CurrentLobbyID : 0;
            bool inLobby = lobby != 0 && NetworkClient.isConnected;
            if (inLobby)
            {
                SteamFriends.SetRichPresence("connect", "+connect_lobby " + lobby);
                SteamFriends.SetRichPresence("status", "Running a supermarket");
                _presenceSet = true;
            }
            else if (_presenceSet)
            {
                SteamFriends.ClearRichPresence();
                _presenceSet = false;
            }
        }
    }
}
