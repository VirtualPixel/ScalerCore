using System;
using UnityEngine;
using ScalerCore.Handlers;
using ScalerCore.Utilities;

namespace ScalerCore
{
    /// <summary>
    /// Public API facade for ScalerCore.
    /// All external callers (guns, patches, other mods) go through here.
    /// Delegates to ScaleController, no new logic.
    /// <para>
    /// A native respawn (see <see cref="NativeSync"/>) replaces the GameObject an object lives
    /// on. <see cref="ObjectRespawned"/> fires when that happens, and every entry point here
    /// that takes a GameObject keeps answering for the old one for the rest of the level, so a
    /// mod that kept a reference is not left talking to something that was destroyed.
    /// </para>
    /// </summary>
    public static class ScaleManager
    {
        /// <summary>
        /// Raised when a native respawn has replaced an object: the old GameObject (still alive,
        /// destroyed immediately after this returns), the fresh one, and the controller on it,
        /// which has already read its size and session out of the instantiation data. The host
        /// is the only machine that raises it, in the same frame it asks for the swap.
        /// <para>
        /// This is the hook for a mod that keys objects by something the swap does not carry:
        /// an instance id, a PhotonView id, a component it added itself. Read what you need off
        /// the old object here, it is the last moment it exists. ScalerCore's own entry points
        /// need no help; they redirect an old GameObject to the fresh controller until the level
        /// changes. A subscriber that throws is logged and skipped, the respawn goes on.
        /// </para>
        /// </summary>
        public static event Action<GameObject, GameObject, ScaleController>? ObjectRespawned;

        // Old instance id to the controller that stands in its place. See RedirectMap.
        static readonly RedirectMap<ScaleController> Redirects = new();

        /// <summary>
        /// Whether scaling may hit dead Semibot heads. Policy belongs to the
        /// calling mod (bind a config there and assign this), not to ScalerCore:
        /// the library carries no user-facing settings of its own. Default off;
        /// a pea-sized head is easy to lose and reviving from one is its own problem.
        /// </summary>
        public static bool AllowDeadHeads { get; set; }

        /// <summary>
        /// Scale resting items, valuables, carts and vehicles by respawning them through the
        /// game's own network instantiate with the scale in the instantiation data, which the
        /// game applies on every client whether or not it runs ScalerCore. Players without the
        /// mod see the object at its real size with the right colliders instead of a full-size
        /// body shaking against the host's small one. Players and enemies stay on the RPC path.
        /// A respawn replaces the GameObject. Everything here follows the swap on its own, and
        /// <see cref="ObjectRespawned"/> says when one happened so a mod can move its own
        /// bookkeeping. Off until it has been through a real room; the player has it under
        /// Experimental in the config, a consuming mod can set it, and a scalercore_nativesync_off
        /// file in BepInEx/config turns it off for a session whatever the setting says.
        /// </summary>
        public static bool NativeSync { get; set; } = Utilities.RespawnRules.NativeSyncDefault;

        /// <summary>
        /// With <see cref="NativeSync"/>, an object in a hand, an inventory slot or a seat is
        /// scaled on the spot over the RPC and respawned the moment it is free. Off keeps such
        /// objects on the RPC path for good.
        /// </summary>
        public static bool NativeSyncWhileHeld { get; set; } = true;

        /// <summary>
        /// Scale an object using the provided options.
        /// Gets existing ScaleController or returns if none attached.
        /// Skips the target if its handler type is not included in <see cref="ScaleOptions.AllowedTargets"/>,
        /// or if the controller's current session was locked via <see cref="ScaleOptions.RejectExternalApply"/>.
        /// </summary>
        public static void Apply(GameObject target) => Apply(target, ScaleOptions.Default);

        /// <summary>
        /// Scale an object using the provided options.
        /// </summary>
        public static void Apply(GameObject target, ScaleOptions options)
        {
            var ctrl = Resolve(target, nameof(Apply));
            if (ctrl == null) return;
            if (Locked(ctrl, nameof(Apply))) return;
            if (!Allowed(ctrl, options.AllowedTargets, nameof(Apply))) return;
            ctrl.DispatchShrink(options);
        }

        /// <summary>
        /// Scale an object only if it isn't already scaled.
        /// Unlike Apply(), this won't toggle or rescale, it's a no-op if the object is already scaled.
        /// Ideal for cart mods and other continuous triggers that fire every frame.
        /// </summary>
        public static bool ApplyIfNotScaled(GameObject target) => ApplyIfNotScaled(target, ScaleOptions.Default);

        /// <summary>
        /// Scale an object only if it isn't already scaled.
        /// Returns true if the object was scaled, false if it was already scaled, has no controller,
        /// is locked by another mod, or is filtered out by AllowedTargets.
        /// </summary>
        public static bool ApplyIfNotScaled(GameObject target, ScaleOptions options)
        {
            var ctrl = Resolve(target, nameof(ApplyIfNotScaled));
            if (ctrl == null) return false;
            if (ctrl.IsScaled)
            {
                Plugin.Log.LogDebug($"[SC] {nameof(ApplyIfNotScaled)} refused on {ctrl.DisplayName}: already scaled at x{ctrl.CurrentOptions.Factor}");
                return false;
            }
            if (Locked(ctrl, nameof(ApplyIfNotScaled))) return false;
            if (!Allowed(ctrl, options.AllowedTargets, nameof(ApplyIfNotScaled))) return false;
            ctrl.DispatchShrink(options);
            return true;
        }

        /// <summary>
        /// <see cref="Apply(GameObject, ScaleOptions)"/> without the lock check.
        /// Still respects <see cref="ScaleOptions.AllowedTargets"/>.
        /// </summary>
        public static void ForceApply(GameObject target, ScaleOptions options)
        {
            var ctrl = Resolve(target, nameof(ForceApply));
            if (ctrl == null) return;
            if (!Allowed(ctrl, options.AllowedTargets, nameof(ForceApply))) return;
            ctrl.DispatchShrink(options);
        }

        /// <summary>
        /// Get the ScaleController for a game object, resolving through PlayerShrinkLink if
        /// needed, and through a native respawn: an object that was swapped this level answers
        /// with the controller on the body that replaced it. Returns null if no controller is
        /// attached. Every other entry point here goes through this, so they all follow a swap.
        /// </summary>
        public static ScaleController? GetController(GameObject target)
        {
            // Deliberately not "target == null": a destroyed GameObject compares equal to null
            // and is exactly the case the redirect is here for. The id survives the destroy.
            if (ReferenceEquals(target, null)) return null;
            var moved = Redirects.Resolve(target.GetInstanceID());
            if (moved != null) return moved;
            if (target == null) return null;
            return target.GetComponent<ScaleController>()
                ?? target.GetComponent<PlayerShrinkLink>()?.Controller;
        }

        // Host side, from NativeRespawn.Swap, with the old body still alive.
        internal static void NoteRespawn(GameObject old, GameObject fresh, ScaleController controller)
        {
            Redirects.Add(old.GetInstanceID(), fresh.GetInstanceID(), controller);
            var subscribers = ObjectRespawned;
            if (subscribers == null) return;
            foreach (var subscriber in subscribers.GetInvocationList())
            {
                // The one call in here that lands in another mod's code. One of them throwing
                // is their bug to fix, and no reason for the rest to miss the swap.
                try
                {
                    ((Action<GameObject, GameObject, ScaleController>)subscriber)(old, fresh, controller);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"[SC] {subscriber.Method.DeclaringType?.FullName}.{subscriber.Method.Name} threw on ObjectRespawned for {old.name}: {e}");
                }
            }
        }

        internal static void ClearRedirects() => Redirects.Clear();

        /// <summary>
        /// Restore with animation (timer expiry, gun toggle).
        /// No-op if the session was locked via <see cref="ScaleOptions.RejectExternalApply"/>.
        /// </summary>
        public static void Restore(GameObject target)
        {
            var ctrl = GetController(target);
            if (ctrl == null) return;
            if (IsLockedFromExternal(ctrl)) return;
            ctrl.DispatchExpand();
        }

        /// <summary>
        /// Restore immediately without animation (bonk/damage).
        /// No-op if the session was locked via <see cref="ScaleOptions.RejectExternalApply"/>.
        /// </summary>
        public static void RestoreImmediate(GameObject target)
        {
            var ctrl = GetController(target);
            if (ctrl == null) return;
            if (IsLockedFromExternal(ctrl)) return;
            ctrl.DispatchExpandNow();
        }

        /// <summary><see cref="Restore"/> without the lock check.</summary>
        public static void ForceRestore(GameObject target)
        {
            var ctrl = GetController(target);
            if (ctrl == null) return;
            ctrl.DispatchExpand();
        }

        /// <summary>
        /// Replace the active session's options without re-dispatching scale.
        /// Pattern: read CurrentOptions, mutate fields, pass back. Useful when a config slider
        /// for RestoreSpeed/SuppressImpactFlash/SuppressCameraShake moves while a session is live
        /// and the change should apply on the upcoming restore.
        /// Fields consumed once at dispatch (Factor, MassCap, BonkImmuneDuration) don't reapply
        /// retroactively, they take effect on the next Apply.
        /// Returns false on missing controller, no active session, or RejectExternalApply lock.
        /// </summary>
        public static bool UpdateOptions(GameObject target, ScaleOptions options)
        {
            var ctrl = GetController(target);
            if (ctrl == null || !ctrl.IsScaled) return false;
            if (IsLockedFromExternal(ctrl)) return false;
            ctrl._options = options;
            return true;
        }

        /// <summary><see cref="UpdateOptions"/> without the lock check.</summary>
        public static bool ForceUpdateOptions(GameObject target, ScaleOptions options)
        {
            var ctrl = GetController(target);
            if (ctrl == null || !ctrl.IsScaled) return false;
            ctrl._options = options;
            return true;
        }

        /// <summary>
        /// Check if an object is currently scaled.
        /// </summary>
        public static bool IsScaled(GameObject target)
        {
            var ctrl = GetController(target);
            return ctrl != null && ctrl.IsScaled;
        }

        /// <summary>
        /// Cleanup all scaled objects on level change.
        /// </summary>
        public static void CleanupAll()
        {
            ScaleController.CleanupAll();
        }

        // Every refusal above is a quiet one from the caller's side: the mod asked for a size and
        // got a no-op back with no exception and, for the void overloads, no return value either.
        // That is how a boss can stay vanilla-sized for a whole lobby without a single line in the
        // log, so each gate says which one it was and on what.

        // Resolving the controller also readies it. The target-type test below reads Handler,
        // which the controller only fills in its own init, and the dispatch runs that init one
        // step too late: an object asked to scale before its Start has run (a mid-level spawn, a
        // body switched back on, a controller added this frame) would be gated on a null Handler,
        // fall through to the Valuables arm, and be refused for every caller that asked for
        // anything else. Enemies are the ones that asked for something else.
        static ScaleController? Resolve(GameObject target, string call)
        {
            var ctrl = GetController(target);
            if (ctrl == null)
            {
                Plugin.Log.LogDebug($"[SC] {call} refused on {NameFor(target)}: no ScaleController on this object");
                return null;
            }
            ctrl.EnsureReady();
            return ctrl;
        }

        // Something already gone is the interesting half of a refusal, and reading its name
        // off it would throw where the log line was supposed to explain the no-op.
        static string NameFor(GameObject target)
        {
            if (ReferenceEquals(target, null)) return "nothing";
            return target == null ? "an object that no longer exists" : target.name;
        }

        static bool Locked(ScaleController ctrl, string call)
        {
            if (!IsLockedFromExternal(ctrl)) return false;
            Plugin.Log.LogDebug($"[SC] {call} refused on {ctrl.DisplayName}: the live session set RejectExternalApply");
            return true;
        }

        static bool Allowed(ScaleController ctrl, ScaleTargets allowed, string call)
        {
            if (IsTargetAllowed(ctrl, allowed)) return true;
            Plugin.Log.LogDebug($"[SC] {call} refused on {ctrl.DisplayName}: it is a {ctrl.TargetType} target and the caller allowed {allowed}");
            return false;
        }

        // Handlers without a dedicated flag bit (e.g. CosmeticHandler) fall through to Valuables.
        static bool IsTargetAllowed(ScaleController ctrl, ScaleTargets allowed)
        {
            return ctrl.Handler switch
            {
                PlayerHandler                  => (allowed & ScaleTargets.Players)   != 0,
                EnemyHandler                   => (allowed & ScaleTargets.Enemies)   != 0,
                ItemHandler or VehicleHandler  => (allowed & ScaleTargets.Items)     != 0,
                _                              => (allowed & ScaleTargets.Valuables) != 0,
            };
        }

        static bool IsLockedFromExternal(ScaleController ctrl) =>
            ctrl.IsScaled && ctrl._options.RejectExternalApply;
    }
}
