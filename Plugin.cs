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

        void Awake()
        {
            Log = Logger;

            RepoXRSupport = Config.Bind(
                "Compatibility", "RepoXR VR support", true,
                "Shrink the VR headset viewpoint and hand rig to match player size when RepoXR is installed. Disable if VR scaling misbehaves.");


            Harmony = new Harmony("Vippy.ScalerCore");
            Harmony.PatchAll();

            gameObject.AddComponent<AprilFools.MapCollapse>();
        }
    }
}
