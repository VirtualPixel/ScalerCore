using HarmonyLib;
using ScalerCore.Utilities;
using UnityEngine;

namespace ScalerCore
{
    // Scaled enemies' abilities scale with them. Enemy code passes hardcoded
    // literals to ParticleScriptExplosion.Spawn (EnemyBang's head goes up with
    // size: 1f, damage: 30, force: 2f no matter what size the enemy is), so no
    // field scaling can ever reach it; intercept the spawn instead. A grown
    // Bang head goes up with a bigger, harder, deeper boom; a shrunk one pops.
    //
    // Items are deliberately NOT handled here: their explosion fields scale at
    // shrink time through ItemHandler's field scan, and feeding already-scaled
    // fields through this prefix would double-apply.
    [HarmonyPatch(typeof(ParticleScriptExplosion), nameof(ParticleScriptExplosion.Spawn))]
    internal static class EnemyExplosionScalePatch
    {
        static ScaleController? EnemyController(ParticleScriptExplosion spawner)
        {
            var ep = spawner.GetComponentInParent<EnemyParent>();
            if (ep == null) return null;
            var ctrl = ep.GetComponentInChildren<ScaleController>();
            return ctrl != null && ctrl.IsScaled ? ctrl : null;
        }

        static void Prefix(ParticleScriptExplosion __instance,
            ref float size, ref int damage, ref int enemyDamage, ref float forceMulti)
        {
            var ctrl = EnemyController(__instance);
            if (ctrl == null) return;
            float f = ctrl._options.Factor;
            size *= f;
            damage = Mathf.RoundToInt(damage * f);
            enemyDamage = Mathf.RoundToInt(enemyDamage * f);
            forceMulti *= f;
            Plugin.Log.LogDebug($"[SC] enemy explosion scaled x{f:F2} (size={size:F2} dmg={damage} force={forceMulti:F2})");
        }

        static void Postfix(ParticleScriptExplosion __instance, ParticlePrefabExplosion __result)
        {
            var ctrl = EnemyController(__instance);
            if (ctrl == null || __result == null) return;
            // The boom matches the body: big enemy, bassy explosion.
            AudioPitchHelper.PitchOneShot(__result, ctrl._options.Factor);
        }
    }
}
