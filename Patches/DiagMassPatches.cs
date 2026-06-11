using System.Diagnostics;
using HarmonyLib;
using ScalerCore.Handlers;

namespace ScalerCore.Patches
{
    // Diagnostic: figure out why shrunken valuables aren't losing weight in the cart.
    // Logs every game-side mass mutation on a scaled valuable with the caller's stack frame,
    // so we can see exactly who's fighting ScalerCore's shrink-time _rb.mass write.
    [HarmonyPatch(typeof(PhysGrabObject), nameof(PhysGrabObject.OverrideMass))]
    internal static class DiagOverrideMassPatch
    {
        static void Prefix(PhysGrabObject __instance, float value, float time)
        {
            var ctrl = __instance.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;
            if (ctrl.TargetType != ScaleTargets.Valuables) return;

            Plugin.Log.LogInfo(
                $"[SC-DIAG] PGO.OverrideMass({value:F3}, t={time:F2}s)  on {__instance.gameObject.name}" +
                $"  rbMassNow={__instance.rb.mass:F3}  pgo.massOriginal={__instance.massOriginal:F3}" +
                $"  pgo.alterMass={__instance.alterMassValue:F3}  pgo.timerAlter={__instance.timerAlterMass:F2}" +
                $"  caller={DiagCaller.Resolve()}");
        }
    }

    [HarmonyPatch(typeof(PhysGrabObject), nameof(PhysGrabObject.ResetMass))]
    internal static class DiagResetMassPatch
    {
        static void Prefix(PhysGrabObject __instance)
        {
            var ctrl = __instance.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;
            if (ctrl.TargetType != ScaleTargets.Valuables) return;

            Plugin.Log.LogInfo(
                $"[SC-DIAG] PGO.ResetMass()  on {__instance.gameObject.name}" +
                $"  rbMassBefore={__instance.rb.mass:F3}  → pgo.massOriginal={__instance.massOriginal:F3}" +
                $"  caller={DiagCaller.Resolve()}");
        }
    }

    internal static class DiagCaller
    {
        // Walk past Harmony's stack frames to find the actual game-code caller.
        public static string Resolve()
        {
            var st = new StackTrace(2, false);
            for (int i = 0; i < st.FrameCount; i++)
            {
                var m = st.GetFrame(i).GetMethod();
                if (m == null) continue;
                string ns = m.DeclaringType?.Namespace ?? "";
                string typeName = m.DeclaringType?.Name ?? "?";
                if (ns.StartsWith("HarmonyLib")) continue;
                if (ns.StartsWith("System.")) continue;
                if (typeName.Contains("DiagOverrideMassPatch") || typeName.Contains("DiagResetMassPatch")) continue;
                return $"{typeName}.{m.Name}";
            }
            return "<unknown>";
        }
    }
}
