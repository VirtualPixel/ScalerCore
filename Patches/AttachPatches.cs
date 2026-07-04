using HarmonyLib;
using Photon.Pun;

namespace ScalerCore
{
    // Awake, not Start: a networked valuable can be spawned and shrunk by the host in the
    // same network pass it is instantiated on a client, and the shrink RPC dispatches before
    // Unity runs Start. Attaching (and registering the RPC cache, in ScaleController.Awake)
    // during the instantiate-time Awake means the controller is present and routable when
    // that RPC lands, instead of a frame too late.
    [HarmonyPatch(typeof(PhysGrabObject), "Awake")]
    internal static class AttachToValuablePatch
    {
        static void Postfix(PhysGrabObject __instance)
        {
            if (__instance.GetComponent<ScaleController>() != null) return;
            bool isValuable = __instance.GetComponent<ValuableObject>() != null;
            bool isItem     = __instance.GetComponent<ItemAttributes>() != null;
            bool isCosmetic = __instance.GetComponent<CosmeticWorldObject>() != null;
            bool isDeadHead = __instance.GetComponent<PlayerDeathHead>() != null;
            bool isRadio    = __instance.GetComponent<ShopRadio>() != null;
            if (!isValuable && !isItem && !isCosmetic && !isDeadHead && !isRadio) return;
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
            __instance.gameObject.AddComponent<ScaleController>();
        }
    }
}
