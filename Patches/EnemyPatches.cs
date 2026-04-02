#pragma warning disable Harmony003
using HarmonyLib;
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
            __instance.playerKill = false;
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

    /// <summary>
    /// Loom's UpdateHandPositionTo moves wrists to world-space player positions
    /// via SmoothDamp, ignoring body scale. Scale the hand target toward the body
    /// center so arms reach proportionally when shrunken.
    /// </summary>
    [HarmonyPatch(typeof(EnemyShadow), "UpdateHandPositionTo")]
    internal static class LoomArmReachPatch
    {
        static void Prefix(EnemyShadow __instance, ref Vector3 _handTarget)
        {
            var ctrl = __instance.enemy?.Rigidbody?.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;

            Vector3 body = __instance.transform.position;
            Vector3 toTarget = _handTarget - body;
            _handTarget = body + toTarget * ctrl._options.Factor;
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
            ScaleManager.RestoreImmediate(ctrl.gameObject);
        }
    }
}
