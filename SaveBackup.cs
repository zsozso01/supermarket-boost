using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SupermarketBoost
{
    /// <summary>
    /// Copies the game's save folder to a sibling folder the game never touches:
    ///   LocalLow/DDTNL/Supermarket Together Backups (SupermarketBoost)/
    /// once when the game starts (before any save is loaded) and before every autosave / save-and-quit.
    /// Old copies are pruned so it never grows big (saves are ~1-2 MB).
    /// </summary>
    internal static class SaveBackup
    {
        private static string _saveDir;
        private static string _backupRoot;
        private static DateTime _lastBackup = DateTime.MinValue;

        public static void Init()
        {
            try
            {
                _saveDir = Application.persistentDataPath;
                _backupRoot = Path.Combine(Path.GetDirectoryName(_saveDir) ?? _saveDir,
                    Path.GetFileName(_saveDir) + " Backups (SupermarketBoost)");
                Backup("game-start");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Save backup failed: {e}");
            }
        }

        public static void Backup(string reason)
        {
            if (_saveDir == null || !Directory.Exists(_saveDir)) return;
            if ((DateTime.Now - _lastBackup).TotalSeconds < 60 && reason != "game-start") return;
            _lastBackup = DateTime.Now;

            string dest = Path.Combine(_backupRoot, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss} {reason}");
            Directory.CreateDirectory(dest);
            CopyDir(_saveDir, dest);
            Plugin.Log.LogInfo($"Saves backed up to {dest}");
            Prune();
        }

        private static void CopyDir(string from, string to)
        {
            foreach (var file in Directory.GetFiles(from))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("Player", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".log")) continue;
                File.Copy(file, Path.Combine(to, name), true);
            }
            foreach (var dir in Directory.GetDirectories(from))
            {
                string sub = Path.Combine(to, Path.GetFileName(dir));
                Directory.CreateDirectory(sub);
                CopyDir(dir, sub);
            }
        }

        private static void Prune()
        {
            int keep = Mathf.Max(5, Plugin.BackupsToKeep.Value);
            var dirs = new DirectoryInfo(_backupRoot).GetDirectories()
                .OrderByDescending(d => d.Name)
                .Skip(keep);
            foreach (var d in dirs)
            {
                try { d.Delete(true); } catch { /* in use, try next time */ }
            }
        }
    }

    // Autosave(bool) runs at day end and on save-and-quit; back up before it overwrites anything.
    [HarmonyPatch(typeof(GameData), "Autosave")]
    internal static class BackupBeforeAutosavePatch
    {
        private static void Prefix()
        {
            try { SaveBackup.Backup("before-save"); }
            catch (Exception e) { Plugin.Log.LogError($"Save backup failed: {e}"); }
        }
    }
}
