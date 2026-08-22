using HarmonyLib;
using ScalerCore.Utilities;
using UnityEngine;

namespace ScalerCore
{
    // Scaled things' explosions scale with them. Every caller passes hardcoded literals to
    // ParticleScriptExplosion.Spawn (EnemyBang's head goes up with size 1, damage 30, force 2
    // no matter what size the enemy is; a grenade is 1.2 / 75 / 160), and the size, damage and
    // enemy-damage numbers only ever land on the ParticlePrefabExplosion the game instantiates
    // at detonation, so no amount of field scaling on the thrower can reach them. Intercepting
    // the spawn is the only place with the numbers in hand, and it is per-blast, so nothing
    // shared gets written. A grown Bang head goes up with a bigger, harder, deeper boom; a
    // shrunk grenade pops.
    [HarmonyPatch(typeof(ParticleScriptExplosion), nameof(ParticleScriptExplosion.Spawn))]
    internal static class ExplosionScalePatch
    {
        static ScaleController? ScaledSpawner(ParticleScriptExplosion spawner)
        {
            ScaleController? ctrl = null;

            // An enemy keeps its controller on the EnemyRigidbody, which is a sibling of
            // whatever owns the explosion rather than an ancestor of it. Go via the component
            // instead of taking the first ScaleController under EnemyParent: a duplicate under
            // one parent is a case the controller already warns about.
            var ep = spawner.GetComponentInParent<EnemyParent>();
            if (ep != null)
                ctrl = ep.GetComponentInChildren<EnemyRigidbody>(includeInactive: true)?.GetComponent<ScaleController>();

            // Items, valuables and vehicles carry it on the object itself.
            ctrl ??= spawner.GetComponentInParent<ScaleController>();

            return ctrl != null && ctrl.IsScaled ? ctrl : null;
        }

        static void Prefix(ParticleScriptExplosion __instance, out ScaleController? __state,
            ref float size, ref int damage, ref int enemyDamage, ref float forceMulti)
        {
            __state = ScaledSpawner(__instance);
            if (__state == null) return;
            float f = __state._options.Factor;
            size *= f;
            damage = Mathf.RoundToInt(damage * f);
            enemyDamage = Mathf.RoundToInt(enemyDamage * f);
            forceMulti *= f;
            Plugin.Log.LogDebug($"[SC] explosion scaled x{f:F2} on {__state._displayName} (size={size:F2} dmg={damage} force={forceMulti:F2})");
        }

        static void Postfix(ScaleController? __state, ParticlePrefabExplosion __result)
        {
            if (__state == null || __result == null) return;
            // The boom matches the body: big thing, bassy explosion.
            AudioPitchHelper.PitchOneShot(__result, __state._options.Factor, __state._options.AudioPresence);
        }
    }
}
