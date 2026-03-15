using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace ScalerCore
{
    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.Hurt))]
    internal static class PlayerBonkPatch
    {
        static void Postfix(PlayerHealth __instance)
        {
            var ctrl = __instance.GetComponentInParent<ScaleController>()
                    ?? __instance.GetComponentInChildren<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;
            ctrl.RequestBonkExpand();
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

    // --- cart pull distance: scale when player is shrunken ---

    [HarmonyPatch(typeof(PhysGrabCart), "CartSteer")]
    internal static class CartHandledDistancePatch
    {
        static float ScalePullDist(float d, PhysGrabCart cart)
        {
            // Shrunken cart being pushed by a full-size player.
            var cartCtrl = cart.GetComponent<ScaleController>();
            if (cartCtrl != null && cartCtrl.IsScaled)
                return d * Mathf.Lerp(ShrinkConfig.Factor, 1f, 0.15f);

            // Full-size cart being pushed by a shrunken player.
            // Check the ACTUAL grabbing players, not PhysGrabber.instance (which is
            // the host's grabber and would apply the host's shrink state to everyone).
            var pgo = cart.GetComponent<PhysGrabObject>();
            if (pgo != null)
            {
                foreach (var grabber in pgo.playerGrabbing)
                {
                    if (grabber?.playerAvatar == null) continue;
                    var ctrl = grabber.playerAvatar.GetComponent<ScaleController>();
                    if (ctrl != null && ctrl.IsScaled)
                        return d * Mathf.Lerp(1f, ShrinkConfig.Factor, 0.15f);
                }
            }

            return d;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var lerpMethod = AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp),
                new[] { typeof(float), typeof(float), typeof(float) });
            var scaleMethod = AccessTools.Method(typeof(CartHandledDistancePatch), nameof(ScalePullDist));

            foreach (var code in instructions)
            {
                yield return code;
                if (code.opcode == OpCodes.Call && (MethodInfo)code.operand == lerpMethod)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, scaleMethod);
                }
            }
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
                ScaleController.FootstepPitchMult = ShrinkConfig.ShrunkFootstepPitchMult;
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
