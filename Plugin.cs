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

        static ConfigEntry<bool> _challengeMode = null!;

        /// <summary>
        /// When true, all players start shrunken with inverted mode.
        /// Reads live from config so it can be toggled without restarting.
        /// </summary>
        internal static bool ChallengeMode => _challengeMode.Value;

        void Awake()
        {
            Log = Logger;

            _challengeMode = Config.Bind("Challenge", "ShrinkChallengeMode", false,
                "All players start shrunken. Shrink guns temporarily grow you back to full size. " +
                "Taking damage while full size shrinks you back down. Enemies behave normally.");

            _harmony = new Harmony("Vippy.ScalerCore");
            _harmony.PatchAll();
        }
    }
}
