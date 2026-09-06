using HarmonyLib;
using ScalerCore.Utilities;

namespace ScalerCore
{
    // RunManager.ChangeLevel returns on its first line for a non-host in a level, but a
    // postfix runs regardless, so the cleanup mirrors that check before doing anything.
    // Non-host clients get their real cleanup from the UpdateLevelRPC patch below.
    [HarmonyPatch(typeof(RunManager), "ChangeLevel")]
    internal static class LevelChangePatch
    {
        static void Prefix(RunManager __instance, out bool __state)
        {
            __state = LevelChangeGate.Proceeds(SemiFunc.MenuLevel(), SemiFunc.IsMasterClientOrSingleplayer(), __instance.restarting);
        }

        static void Postfix(bool __state)
        {
            if (!__state) return;
            ScaleManager.CleanupAll();
            AprilFools.MapCollapse.OnLevelChange();
        }
    }

    [HarmonyPatch(typeof(RunManagerPUN), "UpdateLevelRPC")]
    internal static class LevelChangeNonHostPatch
    {
        static void Postfix()
        {
            if (SemiFunc.IsMasterClientOrSingleplayer()) return;
            ScaleManager.CleanupAll();
            AprilFools.MapCollapse.OnLevelChange();
        }
    }
}
