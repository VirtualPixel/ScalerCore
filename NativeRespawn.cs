using System.Collections.Generic;
using System.Reflection;
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
        // A cart and a vehicle carry things, and the swap below only knows how to bring a single
        // object's value and battery across. Respawning one leaves the new body empty and orphans
        // whatever it was holding, so those two stay on the shrink RPC: modded clients see them
        // scale, unmodded clients keep the old resting jitter, which beats an emptied hauler.
        internal static bool KindEligible(ScaleController ctrl) =>
            ctrl.Handler is ValuableHandler or ItemHandler or CosmeticHandler;

        internal static bool Held(ScaleController ctrl) =>
            ctrl._physGrabObject != null && (ctrl._physGrabObject.grabbed || ctrl._physGrabObject.playerGrabbing.Count > 0);

        // The game's own answer to "is this in a cart", not a measurement of our own: the impact
        // detector raises inCart while the object sits in the cart's In Cart volume and holds the
        // cart it is in, and the cart re-reads that volume into itemsInCart twice a second. Both
        // are read here because the detector's flag runs on a timer and the list is the one other
        // mods work from.
        internal static bool InCart(ScaleController ctrl)
        {
            var pgo = ctrl._physGrabObject;
            if (pgo == null) return false;
            var detector = pgo.impactDetector;
            if (detector == null) return false;
            if (detector.inCart) return true;
            var cart = detector.currentCart;
            return cart != null && cart.itemsInCart.Contains(pgo);
        }

        // Assemblies whose components a respawn can rebuild, because the prefab has them: the
        // game, Unity, Photon, and us. Anything else on the object is another mod's, and the
        // prefab it respawns from has no such component and nothing it had learned since it
        // spawned. Content another mod added to the game is in the same boat: the prefab brings
        // its own components back, but they come back new, so it stays on the RPC path too.
        static readonly Dictionary<Assembly, bool> KnownAssemblies = new();

        static bool IsRebuildable(Assembly assembly)
        {
            if (KnownAssemblies.TryGetValue(assembly, out bool known)) return known;
            if (assembly == typeof(NativeRespawn).Assembly)
            {
                KnownAssemblies[assembly] = true;
                return true;
            }
            string name = assembly.GetName().Name;
            bool ours = name.StartsWith("Assembly-CSharp")      // the game, and firstpass
                     || name.StartsWith("UnityEngine")
                     || name.StartsWith("Unity.")
                     || name.StartsWith("Photon")
                     || name.StartsWith("System")
                     || name == "mscorlib"
                     || name == "netstandard";
            KnownAssemblies[assembly] = ours;
            return ours;
        }

        // Cached per object: a mod attaches what it attaches when the object spawns, and this
        // walks every component on it and under it.
        internal static bool HasForeignComponent(ScaleController ctrl)
        {
            if (ctrl._foreignChecked) return ctrl._foreignComponent != null;
            ctrl._foreignChecked = true;
            foreach (var mb in ctrl.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue; // a script the object lost, nothing to carry
                var type = mb.GetType();
                if (IsRebuildable(type.Assembly)) continue;
                ctrl._foreignComponent = $"{type.FullName} ({type.Assembly.GetName().Name})";
                break;
            }
            return ctrl._foreignComponent != null;
        }

        // Everything that keeps an object off the respawn path, in one place, with the reason.
        internal static RespawnRules.Exclusion ExclusionFor(ScaleController ctrl)
        {
            var reason = RespawnRules.Excluded(KindEligible(ctrl), InCart(ctrl), HasForeignComponent(ctrl), Held(ctrl));
            if (reason != RespawnRules.Exclusion.None && ctrl._loggedExclusion != reason)
            {
                ctrl._loggedExclusion = reason;
                if (reason != RespawnRules.Exclusion.Kind)
                    Plugin.Log.LogDebug($"[SC] {ctrl._displayName} stays on the shrink RPC: {Because(reason, ctrl)}");
            }
            return reason;
        }

        static string Because(RespawnRules.Exclusion reason, ScaleController ctrl) => reason switch
        {
            RespawnRules.Exclusion.Kind    => "nothing to respawn it from",
            RespawnRules.Exclusion.InCart  => "it is in a cart, and a swap would orphan it in the cart's list and in every mod keeping one",
            RespawnRules.Exclusion.Foreign => $"another mod put {ctrl._foreignComponent} on it, and the prefab has no such thing",
            RespawnRules.Exclusion.Held    => "someone is holding it, the swap waits until they are not",
            _                              => "no reason",
        };

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
                RespawnRules.RespawnAllowed(ExclusionFor(ctrl)),
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
            var toggle = go.GetComponent<ItemToggle>();
            string? instanceName = attrs != null ? attrs.instanceName : null;

            // The battery level lives in the run's stats under the item's own name, and that is
            // where the game reads it from whenever an item takes that name. Writing the live
            // level there before the swap, through the game's own synced setter, is what gets it
            // onto every machine when the clone claims the name back a frame later.
            if (battery != null && !string.IsNullOrEmpty(instanceName))
                SemiFunc.StatSetBattery(instanceName!, Mathf.RoundToInt(battery.batteryLife));

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
                cloneValuable.dollarValueOverride = valuable.dollarValueOverride;
                // Set before the clone's own value coroutine gets there, so it leaves the
                // number alone and only sends it on to the other machines.
                cloneValuable.dollarValueSet = true;
                // Whatever the object has been through: a valuable that was dropped once is
                // worth less than its preset, and the break logic is harder on it from here.
                var detector = go.GetComponent<PhysGrabObjectImpactDetector>();
                var cloneDetector = clone.GetComponent<PhysGrabObjectImpactDetector>();
                if (detector != null && cloneDetector != null)
                    cloneDetector.durability = detector.durability;
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

            // The rest of it the game hands out in its own Start, so it cannot be set here and
            // made to stick. The controller puts it back once the clone has been through that.
            var freshCtrl = clone.GetComponent<ScaleController>();
            if (freshCtrl != null)
            {
                freshCtrl._carry = new RespawnCarry
                {
                    InstanceName = instanceName,
                    Discovered   = valuable != null && valuable.discovered,
                    ToggleOn     = toggle != null && toggle.toggleState,
                };
                // The old body is still here for this one call, which is the point of it.
                ScaleManager.NoteRespawn(go, clone, freshCtrl);
            }
            else
            {
                Plugin.Log.LogWarning($"[SC] native respawn of {ctrl._displayName}: the clone came up without a ScaleController, " +
                                      "so nothing carried over and no mod was told where it went");
            }

            Plugin.Log.LogInfo($"[SC] native respawn {ctrl._displayName}: {path} at scale {targetScale.x:F2} (from x{fromFactor:F2}), " +
                               $"{(remaining > 0f ? remaining.ToString("F0") + "s left" : "no timer")}, view {clone.GetComponent<PhotonView>()?.ViewID}");
            PhotonNetwork.Destroy(go);
            return true;
        }
    }

    // The part of the original that the clone cannot be handed at spawn, because the game
    // writes over it in its own Start: the item's name in the run's stats, which is also where
    // its battery level is kept, whether the valuable was already on everyone's map, and
    // whether the device was switched on. ScaleController applies it once the game is done.
    internal sealed class RespawnCarry
    {
        internal string? InstanceName;
        internal bool Discovered;
        internal bool ToggleOn;
    }
}
