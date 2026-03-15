using HarmonyLib;

namespace ScalerCore
{
    [HarmonyPatch(typeof(RunManager), "ChangeLevel")]
    internal static class LevelChangePatch
    {
        static void Postfix()
        {
            ScaleManager.CleanupAll();
        }
    }
}
