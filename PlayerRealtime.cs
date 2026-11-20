using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using StarterAssets;
using UnityEngine;

namespace SupermarketBoost
{
    /// <summary>
    /// While the game is sped up, the local player's walking, jumping and gravity use real time,
    /// so players move at normal speed while everything else runs faster.
    /// </summary>
    [HarmonyPatch]
    internal static class PlayerRealtimePatch
    {
        public static float PlayerDeltaTime()
        {
            if (!DaySpeed.PlayerShouldBeRealtime) return Time.deltaTime;
            return Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        }

        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(FirstPersonController), "Move");
            yield return AccessTools.Method(typeof(FirstPersonController), "JumpAndGravity");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var deltaTime = AccessTools.PropertyGetter(typeof(Time), nameof(Time.deltaTime));
            var replacement = AccessTools.Method(typeof(PlayerRealtimePatch), nameof(PlayerDeltaTime));
            foreach (var ins in code)
            {
                if (ins.Calls(deltaTime)) yield return new CodeInstruction(OpCodes.Call, replacement).MoveLabelsFrom(ins);
                else yield return ins;
            }
        }
    }
}
