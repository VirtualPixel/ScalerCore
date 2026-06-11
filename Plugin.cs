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
        internal static Harmony Harmony = null!;
        internal static ManualLogSource Log = null!;

        // When RepoXR is installed and VR is active, scale the headset viewpoint and hand
        // rig so a shrunken player is actually small in VR. Off leaves RepoXR's camera
        // untouched (the player still shrinks on everyone else's screen).
        internal static ConfigEntry<bool> RepoXRSupport = null!;

        // Dead Semibot heads are grabbable physics props, so the shrink ray CAN
        // hit them; whether it SHOULD is a lobby-taste question (a pea-sized head
        // is easy to lose and a pain to revive from). Ships off.
        internal static ConfigEntry<bool> ShrinkDeadHeads = null!;

        void Awake()
        {
            Log = Logger;

            RepoXRSupport = Config.Bind(
                "Compatibility", "RepoXR VR support", true,
                "Shrink the VR headset viewpoint and hand rig to match player size when RepoXR is installed. Disable if VR scaling misbehaves.");

            ShrinkDeadHeads = Config.Bind(
                "Targets", "Shrink dead heads", false,
                "Let scaling hit dead Semibot heads. Off by default: a shrunk head is easy to lose and reviving from a pea is its own problem.");

            Harmony = new Harmony("Vippy.ScalerCore");
            Harmony.PatchAll();

            gameObject.AddComponent<AprilFools.MapCollapse>();
        }
    }
}
