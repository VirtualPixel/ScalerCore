using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ScalerCore
{
    [BepInPlugin("Vippy.ScalerCore", "ScalerCore", BuildInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        static Harmony? _harmony;
        internal static ManualLogSource Log = null!;

        void Awake()
        {
            Log = Logger;
            _harmony = new Harmony("Vippy.ScalerCore");
            _harmony.PatchAll();
        }
    }
}
