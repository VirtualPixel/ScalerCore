using Photon.Pun;
using ScalerCore.Handlers;
using ScalerCore.Utilities;
using UnityEngine;

namespace ScalerCore
{
    // The one way a scale reaches a player who does not run ScalerCore.
    //
    // The game's PhysGrabObject.Awake applies the Photon instantiation data of a spawned object
    // as its transform scale, on every machine, and that is a vanilla behaviour, not ours. So a
    // resting item, valuable, cart or vehicle is scaled by destroying it and spawning it again
    // through the game's own network instantiate with the scale in that data. An unmodded client
    // gets the object at the right size with the right colliders, instead of a full-size body
    // fighting the host's small one through the transform stream (the shake and the walk-through
    // walls people saw). A modded client reads the rest of the data and runs the usual session
    // (audio, mass, pocketing, the animation in), so for them nothing looks different.
    //
    // Players, enemies and doors have no prefab to respawn from and stay on the shrink RPC.
    // Something in a hand, an inventory or a seat is not swapped under the player: the RPC does
    // the scale on the spot and the swap waits until the object is free.
    internal static class NativeRespawn
    {
        internal static bool KindEligible(ScaleController ctrl) =>
            ctrl.Handler is ValuableHandler or ItemHandler or CartHandler or VehicleHandler or CosmeticHandler;

        internal static bool Held(ScaleController ctrl) =>
            ctrl._physGrabObject != null && (ctrl._physGrabObject.grabbed || ctrl._physGrabObject.playerGrabbing.Count > 0);

        internal static bool Driven(ScaleController ctrl)
        {
            var vehicle = ctrl.GetComponent<ItemVehicle>();
            return vehicle != null && vehicle.Driver != null;
        }

        internal static RespawnRules.Verdict Decide(ScaleController ctrl)
        {
            var pv = ctrl.GetComponent<PhotonView>();
            bool sceneView = pv != null && pv.ViewID != 0 && pv.IsRoomView;
            return RespawnRules.Decide(
                ScaleManager.NativeSync,
                SemiFunc.IsMultiplayer() && PhotonNetwork.InRoom,
                PhotonNetwork.IsMasterClient,
                sceneView,
                ResolvePrefabPath(ctrl) != null,
                KindEligible(ctrl),
                Held(ctrl),
                ctrl.InInventory,
                Driven(ctrl),
                ScaleManager.NativeSyncWhileHeld);
        }

        // Items know their prefab; everything else is named after it, and the game rebuilds
        // resource paths from the name the same way when an enemy drops a valuable. A REPOLib
        // prefab answers Resources.Load through REPOLib's own patch, so modded content passes too.
        internal static string? ResolvePrefabPath(ScaleController ctrl)
        {
            if (ctrl._prefabPath != null) return ctrl._prefabPath.Length > 0 ? ctrl._prefabPath : null;
            string? found = null;
            var attrs = ctrl.GetComponent<ItemAttributes>();
            string? itemPath = attrs != null && attrs.item != null && attrs.item.prefab != null ? attrs.item.prefab.ResourcePath : null;
            foreach (var candidate in new[]
                     {
                         itemPath,
                         RespawnRules.PathFromName(ctrl.gameObject.name, "Valuables"),
                         RespawnRules.PathFromName(ctrl.gameObject.name, "Items"),
                     })
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                var prefab = Resources.Load<GameObject>(candidate);
                if (prefab != null && prefab.GetComponent<PhysGrabObject>() != null)
                {
                    found = candidate;
                    break;
                }
            }
            ctrl._prefabPath = found ?? "";
            return found;
        }

        // Spawn the clone first, at the same pose, and only then destroy the original, so a
        // failed spawn leaves the object exactly as it was and the caller falls back to the RPC.
        internal static bool Swap(ScaleController ctrl, Vector3 targetScale, ScaleOptions options, float remaining, float fromFactor)
        {
            string? path = ResolvePrefabPath(ctrl);
            if (path == null) return false;
            var go = ctrl.gameObject;
            var t = go.transform;
            var rb = ctrl._rb;
            Vector3 pos = t.position;
            Quaternion rot = t.rotation;
            Vector3 vel = rb != null ? rb.velocity : Vector3.zero;
            Vector3 ang = rb != null ? rb.angularVelocity : Vector3.zero;

            var valuable = go.GetComponent<ValuableObject>();
            var attrs = go.GetComponent<ItemAttributes>();
            var battery = go.GetComponent<ItemBattery>();

            object[] data = NativeScaleData.Pack(
                new[] { targetScale.x, targetScale.y, targetScale.z },
                new[] { ctrl.OriginalScale.x, ctrl.OriginalScale.y, ctrl.OriginalScale.z },
                fromFactor, remaining,
                ScaleOptionsCodec.PackFloats(options), ScaleOptionsCodec.PackBools(options));

            GameObject clone;
            try
            {
                clone = PhotonNetwork.InstantiateRoomObject(path, pos, rot, 0, data);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[SC] native respawn of {ctrl._displayName} failed at spawn ({path}): {e.Message}");
                return false;
            }
            if (clone == null)
            {
                Plugin.Log.LogWarning($"[SC] native respawn of {ctrl._displayName}: the game refused to spawn {path}, keeping the RPC path");
                return false;
            }

            // ItemAttributes.Start on the host moves a fresh item so its ItemVolume child sits on
            // the spawn point (and teleports the transform view there). The original lost that
            // child at its own Start, so its root is the real spot: spawn the clone offset by its
            // own volume so the game's shift lands the root back exactly where the original was.
            var cloneVolume = clone.GetComponentInChildren<ItemVolume>();
            if (cloneVolume != null && clone.GetComponent<ItemAttributes>() != null)
            {
                Vector3 offset = cloneVolume.transform.position - clone.transform.position;
                clone.transform.position = pos + offset;
                var shiftRb = clone.GetComponent<Rigidbody>();
                if (shiftRb != null) shiftRb.position = pos + offset;
            }

            // What the original knew that the prefab does not.
            var cloneRb = clone.GetComponent<Rigidbody>();
            if (cloneRb != null)
            {
                cloneRb.velocity = vel;
                cloneRb.angularVelocity = ang;
            }
            var cloneValuable = clone.GetComponent<ValuableObject>();
            if (valuable != null && cloneValuable != null)
            {
                cloneValuable.dollarValueOriginal = valuable.dollarValueOriginal;
                cloneValuable.dollarValueCurrent = valuable.dollarValueCurrent;
                cloneValuable.dollarValueSet = true;
                cloneValuable.discovered = valuable.discovered;
                // Its Start adds the value to the haul goal again; the original already counted.
                if (RoundDirector.instance != null)
                    RoundDirector.instance.haulGoalMax -= (int)valuable.dollarValueCurrent;
            }
            var cloneAttrs = clone.GetComponent<ItemAttributes>();
            if (attrs != null && cloneAttrs != null && attrs.value > 0)
            {
                cloneAttrs.value = attrs.value;
                var clonePv = clone.GetComponent<PhotonView>();
                if (clonePv != null && clonePv.ViewID != 0)
                    clonePv.RPC("GetValueRPC", RpcTarget.Others, attrs.value);
            }
            var cloneBattery = clone.GetComponent<ItemBattery>();
            if (battery != null && cloneBattery != null)
                cloneBattery.SetBatteryLife(Mathf.RoundToInt(battery.batteryLife));

            Plugin.Log.LogInfo($"[SC] native respawn {ctrl._displayName}: {path} at scale {targetScale.x:F2} (from x{fromFactor:F2}), " +
                               $"{(remaining > 0f ? remaining.ToString("F0") + "s left" : "no timer")}, view {clone.GetComponent<PhotonView>()?.ViewID}");
            PhotonNetwork.Destroy(go);
            return true;
        }
    }
}
