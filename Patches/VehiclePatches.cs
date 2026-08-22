using HarmonyLib;

namespace ScalerCore
{
    // ItemVehicle.DriverFullyMounted gates both throttle and steering. It returns true
    // only once the player's tumble body has reached within 0.05 world units of
    // firstMountTransform, a hardcoded threshold that doesn't scale with the vehicle.
    // On a shrunken vehicle the player either can't fit that close, or oscillates
    // outside the 0.5 reset threshold, so steering stays clamped to 0 and the vehicle
    // accelerates straight forward without responding to input. Force true when SHRUNK
    // and there's a seated player: a grown vehicle has the opposite problem, the vanilla
    // mount logic works fine on it, and forcing the flag there just skips the settle.
    [HarmonyPatch(typeof(ItemVehicle), "DriverFullyMounted", MethodType.Getter)]
    internal static class DriverFullyMountedScalePatch
    {
        static void Postfix(ItemVehicle __instance, ref bool __result)
        {
            if (__result) return;
            if (__instance.seats == null || __instance.seats.Length == 0) return;
            if (__instance.seats[0].seatedPlayer == null) return;

            var ctrl = __instance.GetComponent<ScaleController>();
            if (ctrl != null && ctrl.IsScaled && ctrl.CurrentOptions.Factor < 1f)
                __result = true;
        }
    }
}
