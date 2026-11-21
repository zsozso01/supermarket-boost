using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HutongGames.PlayMaker;
using Mirror;
using UnityEngine;

namespace SupermarketBoost
{
    /// <summary>
    /// Main menu additions: a "mods loaded" badge and a Continue button that loads your most recent
    /// store as a friends-only Steam lobby, so friends can join from Steam without a code.
    /// </summary>
    internal static class MainMenu
    {
        private static GUIStyle _badge, _small, _button;
        private static Texture2D _bg;
        private static string _latestSave;
        private static DateTime _latestTime;
        private static float _nextScan;
        private static bool _continuing;
        private static bool _autoContinueDone;
        private static float _continueLock;

        // The main menu is its own scene (A_Intro) without SteamLobby / GameData: Continue and Join go
        // through the menu's own PlayMaker buttons, exactly like clicking them.
        public static bool InMenu =>
            !NetworkClient.active && !NetworkServer.active &&
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "A_Intro";

        public static string LatestSave => _latestSave;
        public static string LatestLabel => _latestLabel;
        public static bool Busy => _continuing;

        public static bool Friend(out string name, out ulong lobby)
        {
            name = null; lobby = 0;
            if (Friends.Count == 0) return false;
            name = Friends[0].Name; lobby = Friends[0].Lobby;
            return true;
        }

        public static void Tick(Plugin plugin)
        {
            if (!InMenu) { _continuing = false; return; }
            if (Time.unscaledTime > _nextScan) { _nextScan = Time.unscaledTime + 3f; ScanSaves(); ScanFriends(); }

            // Debug aid for testing without clicking: Continue once automatically.
            if (Plugin.AutoContinueOnce.Value && !_autoContinueDone && _latestSave != null && Time.unscaledTime > 8f)
            {
                _autoContinueDone = true;
                Plugin.AutoContinueOnce.Value = false;
                plugin.Toast(Continue());
            }
        }

        private static string _latestLabel, _labelFor;

        /// <summary>"Osan - day 25" from the save itself (the game's own key names and settings).</summary>
        private static string ReadLabel(string file)
        {
            if (_labelFor == file) return _latestLabel;
            _labelFor = file;
            string label = Path.GetFileNameWithoutExtension(file);
            try
            {
                var settings = new ES3Settings(file, ES3.EncryptionType.AES, "g#asojrtg@omos)^yq");
                string name = ES3.Load<string>("StoreName", file, "", settings);
                if (!string.IsNullOrEmpty(name)) label = name;
                if (ES3.KeyExists("Day", file, settings)) label += $"  -  day {ES3.Load<int>("Day", file, settings)}";
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Could not read store name from {file}: {e.Message}"); }
            return label;
        }

        private static void ScanSaves()
        {
            try
            {
                var dir = new DirectoryInfo(Application.persistentDataPath);
                var latest = dir.GetFiles("StoreFile*.es3")
                    .Where(f => Regex.IsMatch(f.Name, @"^StoreFile\d+\.es3$"))
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();
                if (latest == null) _latestSave = null;
                if (latest != null)
                {
                    _latestSave = latest.Name; _latestTime = latest.LastWriteTime;
                    _latestLabel = ReadLabel(latest.Name);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Save scan failed: {e.Message}"); }
        }

        private struct FriendGame { public string Name; public ulong Lobby; }
        private static readonly System.Collections.Generic.List<FriendGame> Friends = new System.Collections.Generic.List<FriendGame>();

        /// <summary>Friends who are in a Supermarket Together lobby right now (from Steam, no codes).</summary>
        private static void ScanFriends()
        {
            Friends.Clear();
            if (!SteamManager.Initialized) return;
            try
            {
                var app = Steamworks.SteamUtils.GetAppID();
                int count = Steamworks.SteamFriends.GetFriendCount(Steamworks.EFriendFlags.k_EFriendFlagImmediate);
                for (int i = 0; i < count && Friends.Count < 4; i++)
                {
                    var id = Steamworks.SteamFriends.GetFriendByIndex(i, Steamworks.EFriendFlags.k_EFriendFlagImmediate);
                    if (!Steamworks.SteamFriends.GetFriendGamePlayed(id, out var info)) continue;
                    if (info.m_gameID.AppID() != app || !info.m_steamIDLobby.IsValid()) continue;
                    Friends.Add(new FriendGame { Name = Steamworks.SteamFriends.GetFriendPersonaName(id), Lobby = info.m_steamIDLobby.m_SteamID });
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Friend scan failed: {e.Message}"); }
        }

        public static string Continue()
        {
            if (_continuing || Time.unscaledTime < _continueLock) return null;
            if (_latestSave == null) return "No saved store found";
            if (!InMenu) return null;
            _continuing = true;
            _continueLock = Time.unscaledTime + 20f;
            Plugin.Instance.StartCoroutine(ContinueRoutine(_latestSave));
            return "Loading your store - friends can join from Steam";
        }

        private static GameObject Find(string path)
        {
            var canvas = GameObject.Find("Intro_Canvas");
            var t = canvas != null ? canvas.transform.Find(path) : null;
            return t != null ? t.gameObject : null;
        }

        private static PlayMakerFSM Fsm(GameObject go, string name) =>
            go == null ? null : go.GetComponents<PlayMakerFSM>().FirstOrDefault(f => f.FsmName == name);

        private static System.Collections.IEnumerator ContinueRoutine(string file)
        {
            var menu = Find("Menu_LoadStore");
            if (menu == null) { Fail("Load menu not found"); yield break; }
            menu.SetActive(true); // what the game's Load Store button does; builds one entry per save

            // Entries are built over a few frames; the latest manual save's Continue button, or the autosave one.
            float end = Time.unscaledTime + 6f;
            GameObject slot = null, auto = null;
            while (Time.unscaledTime < end && slot == null)
            {
                yield return null;
                foreach (var b in menu.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                {
                    if (b.name != "Continue_Button") continue;
                    var beh = Fsm(b.gameObject, "Behaviour");
                    if (beh == null || beh.FsmVariables.GetFsmString("FilenameToLoad").Value != file) continue;
                    if (b.transform.parent.name == "Autosave_CityLoad") auto = b.gameObject; else slot = b.gameObject;
                }
            }
            if (slot == null) { Fail($"no load entry for {file}"); yield break; }

            var globals = FsmVariables.GlobalVariables;
            globals.GetFsmString("CurrentFilename").Value = "";
            Press(slot);
            yield return new WaitForSecondsRealtime(1.5f);

            // If the game found a newer autosave of this store it waits for a choice: take the autosave.
            if (globals.GetFsmString("CurrentFilename").Value != file && auto != null && auto.activeInHierarchy)
            {
                Plugin.Log.LogInfo("Continue: a newer autosave exists, loading it");
                Press(auto);
                yield return new WaitForSecondsRealtime(1.5f);
            }
            if (globals.GetFsmString("CurrentFilename").Value == file)
                Plugin.Log.LogInfo($"Continue: loading {file} as a friends-only lobby");
            else Fail("the game did not start loading");
        }

        private static void Press(GameObject button)
        {
            // 1 = friends-only Steam lobby (0 private, 2 public), read by the button's Behaviour FSM.
            var mm = Fsm(button, "Behaviour_Matchmaking");
            if (mm != null) mm.FsmVariables.GetFsmInt("LobbyTypeIndex").Value = 1;
            button.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        }

        private static void Fail(string why)
        {
            Plugin.Log.LogWarning("Continue failed: " + why);
            Plugin.Instance?.Toast("Continue failed (" + why + ") - use Load Store", 6f);
            _continuing = false;
            _continueLock = 0f;
        }

        private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        private static string Encode(ulong v)
        {
            if (v == 0) return "0";
            var sb = new System.Text.StringBuilder();
            while (v > 0) { sb.Insert(0, Alphabet[(int)(v % 36)]); v /= 36; }
            return sb.ToString();
        }

        /// <summary>Join a Steam lobby from the main menu through the game's own "join by code" box.</summary>
        public static bool JoinLobby(ulong lobby)
        {
            if (!InMenu) return false;
            var join = Find("Menu_JoinStore");
            if (join == null) { Plugin.Log.LogWarning("Join menu not found"); return false; }
            _continuing = true;
            _continueLock = Time.unscaledTime + 20f;
            Plugin.Instance.StartCoroutine(JoinRoutine(join, lobby));
            return true;
        }

        private static System.Collections.IEnumerator JoinRoutine(GameObject join, ulong lobby)
        {
            join.SetActive(true);
            yield return null;
            yield return null;
            var field = join.transform.Find("JoinContainer/InputField (TMP)")?.GetComponent<TMPro.TMP_InputField>();
            var button = join.transform.Find("JoinContainer/JoinButton")?.GetComponent<UnityEngine.UI.Button>();
            if (field == null || button == null) { Fail("join box not found"); yield break; }
            field.text = Encode(lobby);
            Plugin.Log.LogInfo($"Joining lobby {lobby} (code {field.text}) from the main menu");
            button.onClick.Invoke();
            yield return new WaitForSecondsRealtime(8f);
            if (InMenu) { _continuing = false; _continueLock = 0f; }
        }

        private static bool _loggedDraw;

        /// <summary>Fallback only: drawn if the native menu buttons could not be added (e.g. after a game update).</summary>
        public static void OnGUI()
        {
            if (!InMenu || MenuUi.Built) return;
            Init();
            if (!_loggedDraw) { _loggedDraw = true; Plugin.Log.LogInfo($"Main menu panel shown ({Screen.width}x{Screen.height}, save={_latestSave})"); }

            // Lay out at 1080p and scale to the real resolution, so it fits on any screen.
            float scale = Screen.height / 1080f;
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            // x: right of the game's own announcement box in the bottom-left corner.
            float w = 430f, x = 380f, bottom = 1080f - 40f;

            string title = $"Supermarket Boost v{Plugin.Version}  -  mods loaded";
            var badge = new Rect(x, bottom - 64f, w, 64f);
            GUI.DrawTexture(badge, _bg);
            GUI.Label(new Rect(badge.x + 14, badge.y + 8, w - 28, 28), title, _badge);
            GUI.Label(new Rect(badge.x + 14, badge.y + 36, w - 28, 22), Updater.Status, _small);

            float y = bottom - 64f - 12f;
            foreach (var f in Friends)
            {
                var fr = new Rect(x, y - 52f, w, 52f);
                if (GUI.Button(fr, "", _button) && !_continuing)
                {
                    Plugin.Log.LogInfo($"Joining {f.Name}'s store from the main menu");
                    if (JoinLobby(f.Lobby)) Plugin.Instance?.Toast($"Joining {f.Name}...");
                }
                GUI.Label(new Rect(fr.x + 16, fr.y + 12, w - 32, 30), $"JOIN  {f.Name}'s store", _badge);
                y -= 52f + 8f;
            }

            if (_latestSave != null)
            {
                var r = new Rect(x, y - 72f, w, 72f);
                if (GUI.Button(r, "", _button)) Plugin.Instance?.Toast(Continue());
                GUI.Label(new Rect(r.x + 16, r.y + 10, w - 32, 30), _continuing ? "Loading..." : "CONTINUE  (friends can join)", _badge);
                GUI.Label(new Rect(r.x + 16, r.y + 42, w - 32, 22),
                    $"{_latestLabel}  -  last played {_latestTime:MMM d, HH:mm}", _small);
            }
            GUI.matrix = oldMatrix;
        }

        private static void Init()
        {
            if (_badge != null) return;
            _bg = new Texture2D(1, 1);
            _bg.SetPixel(0, 0, new Color(0.06f, 0.09f, 0.16f, 0.88f));
            _bg.Apply();
            var hover = new Texture2D(1, 1);
            hover.SetPixel(0, 0, new Color(0.08f, 0.45f, 0.42f, 0.95f));
            hover.Apply();
            var normal = new Texture2D(1, 1);
            normal.SetPixel(0, 0, new Color(0.05f, 0.3f, 0.3f, 0.92f));
            normal.Apply();

            _badge = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            _badge.normal.textColor = new Color(0.18f, 0.83f, 0.75f);
            _small = new GUIStyle(GUI.skin.label) { fontSize = 15, clipping = TextClipping.Clip };
            _small.normal.textColor = new Color(0.8f, 0.85f, 0.9f);
            _button = new GUIStyle(GUI.skin.button);
            _button.normal.background = normal;
            _button.hover.background = hover;
            _button.active.background = hover;
        }
    }
}
