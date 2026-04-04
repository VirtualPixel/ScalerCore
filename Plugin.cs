using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ScalerCore
{
    [BepInPlugin("Vippy.ScalerCore", "ScalerCore", BuildInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        static Harmony? _harmony;
        internal static ManualLogSource Log = null!;

        static ConfigEntry<bool> _challengeMode = null!;

        internal static bool ChallengeMode => _challengeMode.Value;

        void Awake()
        {
            Log = Logger;

            _challengeMode = Config.Bind("Challenge", "ShrinkChallengeMode", false,
                "All players start shrunken. Shrink guns temporarily grow you back to full size. " +
                "Taking damage while full size shrinks you back down. Enemies behave normally.");

            // Apply/cancel lobby voice pitch immediately when the setting changes.
            _challengeMode.SettingChanged += (_, _) =>
            {
                if (!SemiFunc.RunIsLobbyMenu()) return;
                foreach (var vc in Object.FindObjectsOfType<PlayerVoiceChat>())
                {
                    if (_challengeMode.Value)
                        vc.OverridePitch(1.3f, 0.2f, 0.5f, 9999f);
                    else
                        vc.OverridePitchCancel();
                }
            };

            _harmony = new Harmony("Vippy.ScalerCore");
            _harmony.PatchAll();
        }
    }
}
