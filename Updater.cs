using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace SupermarketBoost
{
    /// <summary>
    /// On game start, asks GitHub for the newest release of this mod. If it is newer, downloads
    /// SupermarketBoost.dll next to the current one as SupermarketBoost.dll.new. The small updater
    /// in BepInEx/patchers swaps it in at the next launch, before plugins are loaded.
    /// </summary>
    internal static class Updater
    {
        public const string Repo = "zsozso01/supermarket-boost";
        private const string AssetName = "SupermarketBoost.dll";

        public static string Status = "Checking for updates...";

        public static IEnumerator Check(Plugin plugin, string pluginDllPath)
        {
            // Plain release-asset links (no GitHub API, so no rate limits):
            //   .../releases/latest/download/version.txt  -> e.g. "1.4.1"
            //   .../releases/latest/download/SupermarketBoost.dll
            string baseUrl = $"https://github.com/{Repo}/releases/latest/download/";
            string text;
            using (var req = UnityWebRequest.Get(baseUrl + "version.txt"))
            {
                req.SetRequestHeader("User-Agent", "SupermarketBoost-Updater");
                req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Plugin.Log.LogInfo($"Update check skipped: {req.error}");
                    Status = "Update check failed (offline?)";
                    yield break;
                }
                text = req.downloadHandler.text.Trim();
            }

            if (!Version.TryParse(text.TrimStart('v'), out var latest)) { Status = "Update check failed"; yield break; }
            var current = new Version(Plugin.Version);
            if (latest <= current)
            {
                Plugin.Log.LogInfo($"Up to date (v{current})");
                Status = "Up to date";
                yield break;
            }

            Plugin.Log.LogInfo($"Downloading Supermarket Boost v{latest}");
            using (var req = UnityWebRequest.Get(baseUrl + AssetName))
            {
                req.SetRequestHeader("User-Agent", "SupermarketBoost-Updater");
                req.timeout = 60;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Plugin.Log.LogWarning($"Update download failed: {req.error}");
                    Status = "Update download failed";
                    yield break;
                }
                var bytes = req.downloadHandler.data;
                if (bytes == null || bytes.Length < 4096 || bytes[0] != 'M' || bytes[1] != 'Z')
                {
                    Plugin.Log.LogWarning("Update download was not a DLL, ignored");
                    yield break;
                }
                File.WriteAllBytes(pluginDllPath + ".new", bytes);
            }

            Status = $"v{latest} downloaded - restart the game to use it";
            plugin.Toast($"Supermarket Boost v{latest} downloaded - restart the game to use it", 8f);
        }
    }
}
