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
        static void Prefix(HurtCollider __instance, out (float force, float torque) __state)
        {
            __state = (__instance.playerTumbleForce, __instance.playerTumbleTorque);
            if (__instance.enemyHost == null) return;
            var ctrl = __instance.enemyHost.Rigidbody?.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;
            __instance.playerTumbleForce  *= ShrinkConfig.Factor;
            __instance.playerTumbleTorque *= ShrinkConfig.Factor;
        }

        static void Postfix(HurtCollider __instance, (float force, float torque) __state)
        {
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

    [HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.Hurt))]
    internal static class EnemyDamagePatch
    {
        static void Prefix(ref int damage, int enemyIndex)
        {
            if (!AttackerIsScaled(enemyIndex)) return;
            damage = Mathf.RoundToInt(damage * ShrinkConfig.EnemyDamageMult);
        }

        static bool AttackerIsScaled(int enemyIndex)
        {
            if (enemyIndex < 0)
            {
                Plugin.Log.LogInfo($"[SC] EnemyDamagePatch: enemyIndex={enemyIndex} (no attacker / not an enemy hit)");
                return false;
            }
            var enemy = SemiFunc.EnemyGetFromIndex(enemyIndex);
            if (enemy == null)
            {
                Plugin.Log.LogInfo($"[SC] EnemyDamagePatch: enemyIndex={enemyIndex} → enemy not found");
                return false;
            }
            var ctrl = enemy.Rigidbody?.GetComponent<ScaleController>();
            bool shrunken = ctrl != null && ctrl.IsScaled;
            Plugin.Log.LogInfo($"[SC] EnemyDamagePatch: attacker={enemy.gameObject.name}  shrunken={shrunken}");
            return shrunken;
        }
    }
}
