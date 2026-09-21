using HarmonyLib;
using Photon.Pun;

namespace ScalerCore
{
    // An item's name in the run's stats is its identity: the battery level is filed under it,
    // so is the inventory slot it was in, and the item itself is in the save under that name.
    // The game hands a fresh item one in ItemAttributes' Start, and for a natively respawned
    // item that would be a brand new one, which leaves the original's name taken by nothing and
    // writes a second copy of the item into the run. So a clone takes the name the object it
    // replaced was carrying instead, by the game's own setter, which is the one that pushes the
    // name and the battery level out to every client.
    [HarmonyPatch(typeof(StatsManager), nameof(StatsManager.ItemFetchName))]
    internal static class RespawnKeepsTheItemNamePatch
    {
        static bool Prefix(ItemAttributes itemAttributes, int photonViewID)
        {
            if (itemAttributes == null) return true;
            var ctrl = itemAttributes.GetComponent<ScaleController>();
            string? carried = ctrl != null && ctrl._carry != null ? ctrl._carry.InstanceName : null;
            if (string.IsNullOrEmpty(carried)) return true;

            ctrl!._carry!.InstanceName = null;
            if (PunManager.instance == null) return true;
            PunManager.instance.SetItemName(carried!, itemAttributes, photonViewID);
            Plugin.Log.LogDebug($"[SC] {itemAttributes.gameObject.name} came back from a respawn as {carried}, not as a new item");
            return false;
        }
    }
}
