using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ScalerCore.Utilities;

namespace ScalerCore
{
    [BepInPlugin("Vippy.ScalerCore", "ScalerCore", BuildInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        internal static Harmony Harmony = null!;
        internal static ManualLogSource Log = null!;

        // Mass-drift hunting instrumentation. Heavy (per-physics-call logs, a
        // stack walk in the patches), so it only arms when the dev sets
        // SCALERCORE_DIAG=1 in the environment. Ships silent, costs nothing.
        internal static readonly bool DiagMass =
            System.Environment.GetEnvironmentVariable("SCALERCORE_DIAG") == "1";

        // Extraction-volume debug overlay. A dev tool, not a player setting, so it
        // never appears in the config UI. Arms two ways, both invisible to normal
        // users: the SCALERCORE_DEBUG=1 env var (good for CLI launches), or a
        // "scalercore_debug" sentinel file in BepInEx/config (good for GUI launches
        // through Gale, where env vars don't reach the game). Delete the file to
        // disable. A shipped profile has neither, so it stays dormant.
        internal static readonly bool DebugDraw =
            System.Environment.GetEnvironmentVariable("SCALERCORE_DEBUG") == "1"
            || System.IO.File.Exists(System.IO.Path.Combine(Paths.ConfigPath, "scalercore_debug"));

        void Awake()
        {
            Log = Logger;

            Harmony = new Harmony("Vippy.ScalerCore");
            Harmony.PatchAll();

            gameObject.AddComponent<AprilFools.MapCollapse>();
            if (DebugDraw) gameObject.AddComponent<ExtractionVolumeDebug>();
        }
    }
}
