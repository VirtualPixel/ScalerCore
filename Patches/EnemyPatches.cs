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
            if (EnemyPatchHelpers.TryGetScaled(__instance, out _))
                speed *= ShrinkConfig.EnemyShrinkSpeedFactor;
        }
    }

    [HarmonyPatch(typeof(EnemyNavMeshAgent), nameof(EnemyNavMeshAgent.UpdateAgent))]
    internal static class NavUpdateSpeedPatch
    {
        static void Prefix(EnemyNavMeshAgent __instance, ref float speed)
        {
            if (EnemyPatchHelpers.TryGetScaled(__instance, out _))
                speed *= ShrinkConfig.EnemyShrinkSpeedFactor;
        }
    }

    [HarmonyPatch(typeof(EnemyFloater), nameof(EnemyFloater.UpdateState))]
    internal static class FloaterChargeMoveInPatch
    {
        static readonly AccessTools.FieldRef<EnemyFloater, PlayerAvatar> _targetPlayer =
            AccessTools.FieldRefAccess<EnemyFloater, PlayerAvatar>("targetPlayer");

        static void Postfix(EnemyFloater __instance)
        {
            var ctrl = __instance.enemy?.Rigidbody?.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;

            var player = _targetPlayer(__instance);
            if (!player) return;

            float dist = Vector3.Distance(__instance.feetTransform.position, player.transform.position);
            if (dist <= ShrinkConfig.Factor * 4f) return;

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
            __instance.playerDamage = Mathf.RoundToInt(__instance.playerDamage * ShrinkConfig.Factor);
            __instance.playerTumbleImpactHurtDamage = Mathf.RoundToInt(__instance.playerTumbleImpactHurtDamage * ShrinkConfig.Factor);
            __instance.playerTumbleForce  *= ShrinkConfig.Factor;
            __instance.playerTumbleTorque *= ShrinkConfig.Factor;
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
