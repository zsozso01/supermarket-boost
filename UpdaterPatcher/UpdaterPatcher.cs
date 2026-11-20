using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Mono.Cecil;

namespace SupermarketBoost.UpdaterPatcher
{
    /// <summary>
    /// BepInEx preloader patcher. Runs before plugins load, so the plugin DLL is not yet locked:
    /// if the plugin downloaded SupermarketBoost.dll.new last session, move it into place.
    /// </summary>
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs { get; } = new string[0];

        public static void Initialize()
        {
            try
            {
                foreach (var pending in Directory.GetFiles(Paths.PluginPath, "SupermarketBoost.dll.new", SearchOption.AllDirectories))
                {
                    string target = pending.Substring(0, pending.Length - ".new".Length);
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(pending, target);
                    Console.WriteLine("[SupermarketBoost.Updater] Applied update: " + target);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[SupermarketBoost.Updater] Could not apply update: " + e.Message);
            }
        }

        public static void Patch(AssemblyDefinition assembly) { }
    }
}
