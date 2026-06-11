using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
namespace ScalerCore
{
    // --- tumble link: attach PlayerShrinkLink when tumble GO is created ---
    // PlayerAvatar.tumble is null at PlayerHandler.Setup() time; the tumble GO
    // is instantiated later in LevelGenerator.PlayerSpawn → PlayerTumble.SetupRPC.

    [HarmonyPatch(typeof(PlayerTumble), "SetupDone")]
    internal static class PlayerTumbleLinkPatch
    {
        static void Postfix(PlayerTumble __instance)
        {
            var ctrl = __instance.playerAvatar?.GetComponent<ScaleController>();
            if (ctrl == null) return;

            // Link the tumble GO itself and every child collider so any
            // raycast or overlap hit can resolve to the ScaleController.
            AttachLink(__instance.gameObject, ctrl);
            foreach (var col in __instance.GetComponentsInChildren<Collider>(true))
                AttachLink(col.gameObject, ctrl);

            Plugin.Log.LogDebug($"[SC] PlayerShrinkLink (tumble) attached → {__instance.gameObject.name}  avatar={ctrl.gameObject.name}");
        }

        static void AttachLink(GameObject go, ScaleController ctrl)
        {
            var link = go.GetComponent<PlayerShrinkLink>() ?? go.AddComponent<PlayerShrinkLink>();
            link.Controller = ctrl;
        }
    }

    // Auto-cancel the shrink on death so the revive comes back at normal size.
    // Without this, a shrunken player who dies (e.g. fell into a kill plane during
    // bonk immunity, so bonk-expand was blocked) gets revived with IsScaled still
    // true on remote clients. Local visual ends up at scale 1 because the game's
    // revive animation drives the visual mesh; remotes never get the memo and
    // keep rendering the player shrunken. Host-only: it broadcasts RPC_Expand.
    // Skips inverted/challenge mode so dying small in that mode stays small.
    [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.PlayerDeathRPC))]
    internal static class PlayerDeathExpandPatch
    {
        static void Postfix(PlayerAvatar __instance)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            var ctrl = __instance.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled || ctrl._invertedActive) return;
            ctrl.DispatchExpand();
        }
    }

    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.Hurt))]
    internal static class PlayerBonkPatch
    {
        static void Prefix(PlayerHealth __instance, out int __state)
        {
            __state = __instance.health;
        }

        static void Postfix(PlayerHealth __instance, int __state)
        {
            // Only trigger bonk if health actually decreased. This filters out
            // zero-damage tumble/jump collisions that call Hurt but don't deal damage.
            if (__instance.health >= __state) return;

            var ctrl = __instance.GetComponentInParent<ScaleController>()
                    ?? __instance.GetComponentInChildren<ScaleController>();
            if (ctrl == null) return;

            if (ctrl.IsScaled)
            {
                // Normal: bonk expands. Inverted at home (small): bonk does nothing.
                if (!ctrl._invertedActive)
                    ctrl.RequestBonkExpand();
            }
            else if (ctrl._invertedActive)
            {
                // Inverted, currently full size (temporarily grown): bonk re-shrinks.
                ctrl.RequestInvertedReshrink();
            }
        }
    }

    // --- block pocketing injected-equippable items while the player is shrunken ---

    [HarmonyPatch(typeof(ItemEquippable), nameof(ItemEquippable.RequestEquip))]
    internal static class ShrunkEquipBlockPatch
    {
        static bool Prefix(ItemEquippable __instance)
        {
            // Only block items where WE injected ItemEquippable via PocketHelper.
            // Normal pocketable items (grenades, orbs, etc.) should always work.
            var itemCtrl = __instance.GetComponent<ScaleController>();
            if (itemCtrl == null) return true;

            // Check handler state to see if we added the equippable.
            bool weAdded = false;
            if (itemCtrl.HandlerState is Handlers.CartHandler.State cartState)
                weAdded = cartState.AddedEquippable;
            else if (itemCtrl.HandlerState is Handlers.ItemHandler.State itemState)
                weAdded = itemState.AddedEquippable;
            else if (itemCtrl.HandlerState is Handlers.VehicleHandler.State vehicleState)
                weAdded = vehicleState.AddedEquippable;
            if (!weAdded) return true;

            // We injected this equippable. Block if the player is also shrunken.
            var player = PlayerAvatar.instance;
            if (player == null) return true;
            var playerCtrl = player.GetComponent<ScaleController>();
            if (playerCtrl != null && playerCtrl.IsScaled) return false;

            return true;
        }
    }

    // --- wall-jump fix: verify grounded with a downward ray when shrunken ---
    // The game's OverlapSphere ground check doesn't care about direction, so
    // touching a wall counts as "grounded" when shrunken. This postfix does
    // a quick downward raycast to confirm there's actually floor below.

    [HarmonyPatch(typeof(PlayerCollisionGrounded), "Update")]
    internal static class GroundedWallJumpFix
    {
        static void Postfix(PlayerCollisionGrounded __instance)
        {
            if (!__instance.Grounded) return;
            var player = PlayerAvatar.instance;
            if (player == null) return;
            var ctrl = player.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;

            // Short ray downward from the ground check position.
            // If it doesn't hit anything within the sphere radius, we're on a wall, not the floor.
            float checkDist = __instance.Collider.radius * 1.5f;
            if (!Physics.Raycast(__instance.transform.position, Vector3.down, checkDist, __instance.LayerMask, QueryTriggerInteraction.Ignore))
            {
                __instance.Grounded = false;
                __instance.GroundedTimer = 0f;
                __instance.CollisionController.Grounded = false;
            }
        }
    }

    // --- held-item grip distance: scale when player is shrunken ---

    [HarmonyPatch(typeof(PhysGrabber), nameof(PhysGrabber.OverrideGrabDistance))]
    internal static class GrabDistanceScalePatch
    {
        static void Prefix(ref float dist)
        {
            if (PhysGrabber.instance?.playerAvatar == null) return;
            var ctrl = PhysGrabber.instance.playerAvatar.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;
            // 0.7× keeps overrideGrabDistance above minDistanceFromPlayer (Factor).
            // Below that threshold the puller is clamped to minDistance anyway.
            dist *= 0.7f;
        }
    }

    // --- gun vertical hold offset: cancel when player is shrunken ---
    // ItemGun calls OverrideGrabVerticalPosition(-0.2f) every frame while held.
    // VisionTransform is already scaled to the correct height for the shrunken player;
    // applying the full -0.2 world-unit offset on top of that pushes the gun to
    // waist/floor level, making it look jammed against the screen.

    [HarmonyPatch(typeof(PhysGrabObject), nameof(PhysGrabObject.OverrideGrabVerticalPosition))]
    internal static class GrabVerticalPositionScalePatch
    {
        static void Prefix(ref float pos)
        {
            var ctrl = PhysGrabber.instance?.playerAvatar?.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;
            pos = 0f;
        }
    }

    // forceGrabPoint items get positioned with a hardcoded -up*0.3 world-unit offset
    // in StartGrabbingPhysObject. Doesn't scale with the player, so shrunken players
    // hold guns at waist height. Lift back proportionally so the residual offset is
    // 0.3*factor instead of a flat 0.3.

    [HarmonyPatch(typeof(PhysGrabber), "StartGrabbingPhysObject")]
    internal static class ForceGrabPointVerticalScalePatch
    {
        static void Postfix(PhysGrabber __instance, PhysGrabObject ___grabbedPhysGrabObject)
        {
            var pa = __instance.playerAvatar;
            if (pa == null) return;
            var ctrl = pa.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;
            if (___grabbedPhysGrabObject == null) return;
            var fgp = ___grabbedPhysGrabObject.forceGrabPoint;
            if (fgp == null || !fgp.gameObject.activeSelf) return;
            var overrideTransform = pa.localCamera?.GetOverrideTransform();
            if (overrideTransform == null) return;

            float lift = 0.3f * (1f - ctrl._options.Factor);
            Vector3 delta = overrideTransform.up * lift;
            __instance.physGrabPointPuller.position += delta;
            __instance.physGrabPointPlane.position  += delta;
        }
    }

    // --- cart pull distance: scale when player is shrunken ---

    [HarmonyPatch(typeof(PhysGrabCart), "CartSteer")]
    internal static class CartHandledDistancePatch
    {
        static float ScalePullDist(float d, PhysGrabCart cart, PhysGrabber grabber)
        {
            // Shrunken cart being pushed by a full-size player.
            var cartCtrl = cart.GetComponent<ScaleController>();
            if (cartCtrl != null && cartCtrl.IsScaled)
                return d * Mathf.Lerp(cartCtrl._options.Factor, 1f, 0.15f);

            // Full-size cart being pushed by a shrunken player.
            // Check the ACTUAL grabbing players, not PhysGrabber.instance (which is
            // the host's grabber and would apply the host's shrink state to everyone).

            if (grabber == null) return d;
            var ctrl = grabber.playerAvatar.GetComponent<ScaleController>();
            if (ctrl != null && ctrl.IsScaled)
                return d * Mathf.Lerp(1f, ctrl._options.Factor, 0.15f);

            return d;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase originalMethod)
        {
            var lerpMethod = AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp),
                new[] { typeof(float), typeof(float), typeof(float) });
            var scaleMethod = AccessTools.Method(typeof(CartHandledDistancePatch), nameof(ScalePullDist));

            var locals = originalMethod.GetMethodBody().LocalVariables;
            int grabberIndex = -1;
            int found = 0;
            foreach (var local in locals)
            {
                if (local.LocalType == typeof(PhysGrabber))
                { 
                    found++;
                    if (found == 2)
                    {
                        grabberIndex = local.LocalIndex;
                        break;
                    }
                }
            }

            if (grabberIndex == -1)
            {
                Plugin.Log.LogWarning("[SC] CartSteer transpiler: second PhysGrabber local not found. " +
                    "Cart pull-distance scaling is DISABLED this session; if this appears after a game update, report it.");
                foreach (var code in instructions)
                    yield return code;
                yield break;
            }

            int injected = 0;
            foreach (var code in instructions)
            {
                yield return code;
                if (code.opcode == OpCodes.Call && (MethodInfo)code.operand == lerpMethod)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldloc, grabberIndex);
                    yield return new CodeInstruction(OpCodes.Call, scaleMethod);
                    injected++;
                }
            }

            // A wrong-typed local would fail IL verification at patch time and
            // Harmony reports that itself. The silent failure mode is the Lerp
            // calls disappearing in a game update: the loop above then matches
            // nothing and the feature dies quietly unless we shout here.
            if (injected == 0)
                Plugin.Log.LogWarning("[SC] CartSteer transpiler: no Mathf.Lerp callsites matched. " +
                    "Cart pull-distance scaling is DISABLED this session; if this appears after a game update, report it.");
        }
    }

    // --- camera glitch: scale quad to fill screen when player is shrunken ---
    // CameraGlitch is a world-space quad that scales by FOV ratio. When the player
    // is shrunken the quad doesn't cover the full viewport. Multiply by inverse
    // player scale so the quad grows to compensate.

    [HarmonyPatch(typeof(CameraGlitch), "Update")]
    internal static class CameraGlitchScalePatch
    {
        static void Postfix(CameraGlitch __instance)
        {
            var player = PlayerAvatar.instance;
            if (player == null) return;
            var ctrl = player.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;

            float factor = ctrl.OriginalScale.x > 0f
                ? ctrl._t.localScale.x / ctrl.OriginalScale.x
                : 1f;
            if (factor >= 1f || factor <= 0f) return;

            // scale up the quad by 1/factor so it fills the shrunken player's view
            __instance.transform.localScale /= factor;
        }
    }

    // --- footstep pitch: shrunken players get higher-pitched footstep sounds ---

    [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.Footstep))]
    internal static class FootstepPitchPatch
    {
        static void Prefix(PlayerAvatar __instance)
        {
            var ctrl = __instance.GetComponent<ScaleController>();
            if (ctrl != null && ctrl.IsScaled)
                ScaleController.FootstepPitchMult = ctrl._options.FootstepPitchMultiplier;
        }

        static void Postfix()
        {
            ScaleController.FootstepPitchMult = 1f;
        }
    }

    [HarmonyPatch(typeof(Sound), nameof(Sound.Play),
        new[] { typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(float) })]
    internal static class SoundPlayPitchPatch
    {
        static void Postfix(AudioSource __result)
        {
            if (ScaleController.FootstepPitchMult != 1f && __result != null)
                __result.pitch *= ScaleController.FootstepPitchMult;
        }
    }
}
