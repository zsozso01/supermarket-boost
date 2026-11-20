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

        public static bool InMenu => GameData.Instance == null && !NetworkClient.active && !NetworkServer.active;

        public static void Tick(Plugin plugin)
        {
            if (!InMenu) { _continuing = false; return; }
            if (Time.unscaledTime > _nextScan) { _nextScan = Time.unscaledTime + 3f; ScanSaves(); }

            // Debug aid for testing without clicking: Continue once automatically.
            if (Plugin.AutoContinueOnce.Value && !_autoContinueDone && _latestSave != null && Time.unscaledTime > 8f)
            {
                _autoContinueDone = true;
                Plugin.AutoContinueOnce.Value = false;
                plugin.Toast(Continue());
            }
        }

        private static void ScanSaves()
        {
            _latestSave = null;
            try
            {
                var dir = new DirectoryInfo(Application.persistentDataPath);
                var latest = dir.GetFiles("StoreFile*.es3")
                    .Where(f => Regex.IsMatch(f.Name, @"^StoreFile\d+\.es3$"))
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();
                if (latest != null) { _latestSave = latest.Name; _latestTime = latest.LastWriteTime; }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Save scan failed: {e.Message}"); }
        }

        public static string Continue()
        {
            if (_continuing || Time.unscaledTime < _continueLock) return null;
            if (_latestSave == null) return "No saved store found";
            if (SteamLobby.Instance == null) return "Steam is not ready yet";

            _continuing = true;
            _continueLock = Time.unscaledTime + 20f; // never start two lobbies
            var globals = FsmVariables.GlobalVariables;
            globals.GetFsmString("CurrentFilename").Value = _latestSave;
            globals.GetFsmBool("LoadingFromAutosave").Value = false;
            Plugin.Log.LogInfo($"Continue: loading {_latestSave} as a friends-only lobby");
            // 1 = friends-only Steam lobby (0 would be offline/private).
            SteamLobby.Instance.HostLobby(1, 0, 0, 0);
            return "Loading your store - friends can join from Steam";
        }

        public static void OnGUI()
        {
            if (!InMenu) return;
            Init();

            // Lay out at 1080p and scale to the real resolution, so it fits on any screen.
            float scale = Screen.height / 1080f;
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = 430f, x = 28f, bottom = 1080f - 28f;

            string title = $"Supermarket Boost v{Plugin.Version}  -  mods loaded";
            var badge = new Rect(x, bottom - 64f, w, 64f);
            GUI.DrawTexture(badge, _bg);
            GUI.Label(new Rect(badge.x + 14, badge.y + 8, w - 28, 28), title, _badge);
            GUI.Label(new Rect(badge.x + 14, badge.y + 36, w - 28, 22), Updater.Status, _small);

            if (_latestSave != null)
            {
                var r = new Rect(x, bottom - 64f - 12f - 72f, w, 72f);
                if (GUI.Button(r, "", _button)) Continue();
                GUI.Label(new Rect(r.x + 16, r.y + 10, w - 32, 30), _continuing ? "Loading..." : "CONTINUE  (friends can join)", _badge);
                GUI.Label(new Rect(r.x + 16, r.y + 42, w - 32, 22),
                    $"{Path.GetFileNameWithoutExtension(_latestSave)}  -  last played {_latestTime:MMM d, HH:mm}", _small);
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
