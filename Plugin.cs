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

        // Voice buffer instrumentation for the "voices cut out while shrunk" report.
        // Arms like DebugDraw so testers on Gale can turn it on without env vars, and
        // carries a value so the noisy Photon-logger tier is opt-in: "photon" adds
        // Photon Voice's own underrun lines, anything else is just our lag sampler.
        // See Utilities/VoiceBufferDiag.
        internal static readonly string VoiceDiag = ReadVoiceDiagArm();

        static string ReadVoiceDiagArm()
        {
            string fromEnv = System.Environment.GetEnvironmentVariable("SCALERCORE_VOICEDIAG") ?? "";
            if (fromEnv != "") return fromEnv.Trim().ToLowerInvariant();

            string sentinel = System.IO.Path.Combine(Paths.ConfigPath, "scalercore_voicediag");
            if (!System.IO.File.Exists(sentinel)) return "";
            // An empty sentinel still arms the sampler: a tester told to "make the file"
            // shouldn't have to know it needs contents.
            string firstLine = System.IO.File.ReadAllText(sentinel).Trim().ToLowerInvariant();
            return firstLine == "" ? "1" : firstLine;
        }

        void Awake()
        {
            Log = Logger;

            Harmony = new Harmony("Vippy.ScalerCore");
            Harmony.PatchAll();

            // Probe for RepoXR now, while the game is still loading, so the one-time
            // assembly check happens here instead of stalling the first in-game scale.
            Compat.RepoXRBridge.Warm();

            gameObject.AddComponent<AprilFools.MapCollapse>();
            if (DebugDraw) gameObject.AddComponent<ExtractionVolumeDebug>();
            if (VoiceDiag != "")
            {
                gameObject.AddComponent<VoiceBufferDiag>();
                Log.LogInfo($"[SC-VOICE] voice buffer diagnostics armed (mode: {VoiceDiag})");
            }
        }
    }
}
