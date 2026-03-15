using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ScalerCore
{
    [BepInPlugin("Vippy.ScalerCore", "ScalerCore", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        static Harmony? _harmony;
        internal static ManualLogSource Log = null!;

        void Awake()
        {
            Log = Logger;
            ShrinkConfig.Init(Config);

            _harmony = new Harmony("Vippy.ScalerCore");
            _harmony.PatchAll();
        }
    }
}
