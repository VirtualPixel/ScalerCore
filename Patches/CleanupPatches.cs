using HarmonyLib;

namespace ScalerCore
{
    // RunManager.ChangeLevel early-returns on non-host during gameplay,
    // so this postfix only fires on host/singleplayer. Non-host clients
    // run CleanupAll via the UpdateLevelRPC patch instead.
    [HarmonyPatch(typeof(RunManager), "ChangeLevel")]
    internal static class LevelChangePatch
    {
        static void Postfix()
        {
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
