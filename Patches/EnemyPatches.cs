#pragma warning disable Harmony003
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace ScalerCore.Patches
{
    internal static class EnemyPatchHelpers
    {
        internal static bool TryGetScaled(EnemyNavMeshAgent agent, out ScaleController? ctrl)
        {
            ctrl = agent.GetComponentInParent<EnemyParent>()
                        ?.GetComponentInChildren<ScaleController>();
            return ctrl != null && ctrl.IsScaled;
        }
    }

    [HarmonyPatch(typeof(EnemyNavMeshAgent), nameof(EnemyNavMeshAgent.OverrideAgent))]
    internal static class NavOverrideSpeedPatch
    {
        static void Prefix(EnemyNavMeshAgent __instance, ref float speed)
        {
            if (EnemyPatchHelpers.TryGetScaled(__instance, out var ctrl))
                speed *= ctrl!._options.SpeedFactor;
        }
    }

    [HarmonyPatch(typeof(EnemyNavMeshAgent), nameof(EnemyNavMeshAgent.UpdateAgent))]
    internal static class NavUpdateSpeedPatch
    {
        static void Prefix(EnemyNavMeshAgent __instance, ref float speed)
        {
            if (EnemyPatchHelpers.TryGetScaled(__instance, out var ctrl))
                speed *= ctrl!._options.SpeedFactor;
        }
    }

    [HarmonyPatch(typeof(EnemyFloater), nameof(EnemyFloater.UpdateState))]
    internal static class FloaterChargeMoveInPatch
    {
        static void Postfix(EnemyFloater __instance)
        {
            var ctrl = __instance.enemy?.Rigidbody?.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;

            var player = __instance.targetPlayer;
            if (!player) return;

            float dist = Vector3.Distance(__instance.feetTransform.position, player.transform.position);
            if (dist <= ctrl._options.Factor * 4f) return;

            __instance.enemy?.NavMeshAgent.SetDestination(player.transform.position);
        }
    }

    [HarmonyPatch(typeof(HurtCollider), "PlayerHurt")]
    internal static class KnockbackPatch
    {
        static void Prefix(HurtCollider __instance, out (bool playerKill, int playerDamage, int tumbleDamage, float force, float torque) __state)
        {
            __state = (__instance.playerKill, __instance.playerDamage, __instance.playerTumbleImpactHurtDamage, __instance.playerTumbleForce, __instance.playerTumbleTorque);
            ScaleController? ctrl = null;
            if (__instance.enemyHost != null)
            {
                ctrl = __instance.enemyHost.Rigidbody?.GetComponent<ScaleController>();
            }
            else
            {
                var parent = __instance.GetComponentInParent<EnemyParent>();
                if (parent != null)
                    ctrl = parent.GetComponentInChildren<ScaleController>();
            }
            if (ctrl == null || !ctrl.IsScaled) return;
            // Only disable instakill when shrinking . enlarged enemies should keep it.
            if (ctrl._options.Factor < 1f) __instance.playerKill = false;
            __instance.playerDamage = Mathf.RoundToInt(__instance.playerDamage * ctrl!._options.Factor);
            __instance.playerTumbleImpactHurtDamage = Mathf.RoundToInt(__instance.playerTumbleImpactHurtDamage * ctrl!._options.Factor);
            __instance.playerTumbleForce  *= ctrl!._options.Factor;
            __instance.playerTumbleTorque *= ctrl!._options.Factor;
        }

        static void Postfix(HurtCollider __instance, (bool playerKill, int playerDamage, int tumbleDamage, float force, float torque) __state)
        {
            __instance.playerKill = __state.playerKill;
            __instance.playerDamage = __state.playerDamage;
            __instance.playerTumbleImpactHurtDamage = __state.tumbleDamage;
            __instance.playerTumbleForce  = __state.force;
            __instance.playerTumbleTorque = __state.torque;
        }
    }

    /// <summary>
    /// HeartHugger's gas guider pulls the player to a point 1.5 units in front of
    /// the head, which is too far for a shrunken enemy. After the game's Update
    /// positions the guider, scale the distance from the head so the player is
    /// pulled to the correct proportional distance from the tiny mouth.
    /// </summary>
    [HarmonyPatch(typeof(EnemyHeartHuggerGasGuider), "Update")]
    internal static class HeartHuggerGasPullPatch
    {
        static void Postfix(EnemyHeartHuggerGasGuider __instance)
        {
            if (__instance.enemyHeartHugger == null) return;
            var ctrl = __instance.enemyHeartHugger.enemy?.Rigidbody?.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;

            // After the game lerps the guider toward head + 1.5 forward,
            // scale the offset so the target is proportionally closer.
            Vector3 headPos = __instance.enemyHeartHugger.headCenterTransform.position;
            Vector3 toGuider = __instance.transform.position - headPos;
            __instance.transform.position = headPos + toGuider * ctrl._options.Factor;
        }
    }

    [HarmonyPatch(typeof(EnemyHealth), nameof(EnemyHealth.Hurt))]
    internal static class EnemyBonkPatch
    {
        static void Postfix(EnemyHealth __instance)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            var ctrl = __instance.enemy?.Rigidbody?.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;
            // Only bonk-restore shrunken enemies. Enlarged enemies stay big when hurt.
            if (ctrl._options.Factor >= 1f) return;
            // Direct dispatch (not via ScaleManager) so RejectExternalApply doesn't gate
            // internal bonk handling. Matches ValuableHandler/CosmeticHandler.
            ctrl.DispatchExpandNow();
        }
    }

    // --- shrunken players use crouch detection for enemy vision ---
    // EnemyVision.Vision() is a coroutine that reads PlayerAvatar.isCrouching to
    // decide detection distance and cone angle. Instead of toggling isCrouching on
    // the player (which causes animation flicker), we transpile the coroutine's
    // MoveNext to replace the field read with a helper that also returns true for
    // shrunken players.

    [HarmonyPatch]
    internal static class EnemyVisionShrunkCrouchPatch
    {
        static MethodBase TargetMethod()
        {
            // The Vision() coroutine compiles to a nested state-machine class.
            // Find its MoveNext method by looking for the nested type whose name
            // starts with "<Vision>".
            var nested = typeof(EnemyVision).GetNestedTypes(BindingFlags.NonPublic)
                .FirstOrDefault(t => t.Name.StartsWith("<Vision>"));
            if (nested == null)
            {
                Plugin.Log.LogWarning("[SC] EnemyVisionShrunkCrouchPatch: could not find Vision coroutine state machine");
                return null!;
            }
            return AccessTools.Method(nested, "MoveNext");
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var isCrouchingField = AccessTools.Field(typeof(PlayerAvatar), nameof(PlayerAvatar.isCrouching));
            var helper = AccessTools.Method(typeof(EnemyVisionShrunkCrouchPatch), nameof(IsEffectivelyCrouching));

            foreach (var code in instructions)
            {
                if (code.opcode == OpCodes.Ldfld && code.operand is FieldInfo fi && fi == isCrouchingField)
                {
                    // Stack already has the PlayerAvatar instance on top.
                    // Replace the field load with a static call that checks
                    // both isCrouching and shrunken state.
                    yield return new CodeInstruction(OpCodes.Call, helper);
                }
                else
                {
                    yield return code;
                }
            }
        }

        internal static bool IsEffectivelyCrouching(PlayerAvatar pa)
        {
            if (pa.isCrouching) return true;
            var ctrl = pa.GetComponent<ScaleController>();
            return ctrl != null && ctrl.IsScaled && ctrl._options.Factor < 1f;
        }
    }
}
