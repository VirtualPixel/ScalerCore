using HarmonyLib;
using Photon.Pun;

namespace ScalerCore
{
    [HarmonyPatch(typeof(PhysGrabObject), "Start")]
    internal static class AttachToValuablePatch
    {
        static void Postfix(PhysGrabObject __instance)
        {
            if (__instance.GetComponent<ScaleController>() != null) return;
            bool isValuable = __instance.GetComponent<ValuableObject>() != null;
            bool isItem     = __instance.GetComponent<ItemAttributes>() != null;
            if (!isValuable && !isItem) return;
            __instance.gameObject.AddComponent<ScaleController>();
        }
    }

    [HarmonyPatch(typeof(EnemyRigidbody), "Awake")]
    internal static class AttachToEnemyPatch
    {
        static void Postfix(EnemyRigidbody __instance)
        {
            if (__instance.GetComponent<ScaleController>() != null) return;
            __instance.gameObject.AddComponent<ScaleController>();
        }
    }

    [HarmonyPatch(typeof(PlayerAvatar), "Start")]
    internal static class AttachToPlayerPatch
    {
        static void Postfix(PlayerAvatar __instance)
        {
            if (__instance.GetComponent<ScaleController>() != null) return;
            __instance.gameObject.AddComponent<ScaleController>();
            __instance.GetComponent<PhotonView>()?.RefreshRpcMonoBehaviourCache();
        }
    }

    [HarmonyPatch(typeof(PhysGrabHinge), "Awake")]
    internal static class AttachToDoorPatch
    {
        static void Postfix(PhysGrabHinge __instance)
        {
            if (__instance.GetComponent<ScaleController>() != null) return;
            // Scale the root so the Rigidbody collider shrinks with the door.
            // If ScaleTarget is set to a visual child, the collider on the root stays
            // at full size and leaves a grab-able ghost. Root scaling avoids that.
            __instance.gameObject.AddComponent<ScaleController>();
        }
    }
}
