using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ScalerCore
{
    [BepInPlugin("Vippy.ScalerCore", "ScalerCore", BuildInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        static Harmony? _harmony;
        internal static ManualLogSource Log = null!;

        /// <summary>
        /// When true, all players start shrunken with inverted mode.
        /// Shrink guns temporarily grow them; damage while grown shrinks back.
        /// </summary>
        internal static bool ChallengeMode;

        void Awake()
        {
            Log = Logger;

            ChallengeMode = Config.Bind("Challenge", "ShrinkChallengeMode", false,
                "All players start shrunken. Shrink guns temporarily grow you back to full size. " +
                "Taking damage while full size shrinks you back down. Enemies behave normally.").Value;

            _harmony = new Harmony("Vippy.ScalerCore");
            _harmony.PatchAll();
        }
    }
}
