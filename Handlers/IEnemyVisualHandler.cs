using System.Collections.Generic;

namespace ScalerCore.Handlers
{
    /// <summary>
    /// Per-enemy-type strategy for visual scaling.
    /// Plugs into EnemyHandler to handle AnimTarget/mesh adjustments
    /// that vary across enemy types (sinkers, heart hugger, shadow, etc.).
    /// </summary>
    internal interface IEnemyVisualHandler
    {
        /// <summary>
        /// Called once during EnemyHandler.Setup to capture per-enemy visual state.
        /// Returns an opaque state object stored alongside EnemyHandler.State.
        /// </summary>
        object? Setup(ScaleController ctrl, EnemyHandler.State state, EnemyParent ep);

        /// <summary>
        /// Called every LateUpdate while the enemy is scaled or transitioning.
        /// Apply visual scaling (AnimTarget, mesh offsets, etc.) using the current ratio.
        /// </summary>
        void OnLateUpdate(ScaleController ctrl, EnemyHandler.State state, object? visualState, float ratio);

        /// <summary>
        /// Called when the enemy is restored to full size.
        /// Reset any visual overrides back to their original values.
        /// </summary>
        void OnRestore(ScaleController ctrl, EnemyHandler.State state, object? visualState);
    }

    /// <summary>
    /// Maps enemy internal names (e.g. "Tumbler") to their IEnemyVisualHandler.
    /// Falls back to DefaultVisualHandler when no override is registered.
    /// </summary>
    internal static class EnemyVisualRegistry
    {
        static readonly Dictionary<string, IEnemyVisualHandler> _overrides = new();
        static readonly IEnemyVisualHandler _default = new EnemyVisuals.DefaultVisualHandler();
        static bool _registered;

        /// <summary>
        /// Extracts the enemy name from an EnemyParent's GameObject name.
        /// "Enemy - Tumbler(Clone)" → "Tumbler"
        /// </summary>
        internal static string ExtractEnemyName(EnemyParent ep)
        {
            string raw = ep.gameObject.name;

            // Strip "(Clone)" suffix if present.
            int cloneIdx = raw.IndexOf("(Clone)");
            if (cloneIdx >= 0)
                raw = raw.Substring(0, cloneIdx);

            // Strip "Enemy - " prefix if present.
            const string prefix = "Enemy - ";
            if (raw.StartsWith(prefix))
                raw = raw.Substring(prefix.Length);

            return raw.Trim();
        }

        /// <summary>
        /// Returns the visual handler for the given enemy name,
        /// or the default handler if no override is registered.
        /// </summary>
        internal static IEnemyVisualHandler Resolve(string enemyName)
        {
            EnsureRegistered();
            return _overrides.TryGetValue(enemyName, out var handler) ? handler : _default;
        }

        /// <summary>
        /// Registers all known per-enemy visual overrides. Safe to call multiple times.
        /// </summary>
        static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;

            var sinker      = new EnemyVisuals.SinkerVisualHandler();
            var floater     = new EnemyVisuals.SinkerVisualHandler(groundOnGrow: false);
            var heartHug    = new EnemyVisuals.HeartHuggerVisualHandler();
            var loom        = new EnemyVisuals.LoomVisualHandler();
            var tricycle    = new EnemyVisuals.TricycleVisualHandler();
            var birthdayBoy = new EnemyVisuals.BirthdayBoyVisualHandler();

            _overrides["Tumbler"]      = sinker;
            _overrides["Floater"]      = floater;
            _overrides["Runner"]       = sinker;
            _overrides["Slow Walker"]  = sinker;
            _overrides["Elsa"]         = sinker;
            _overrides["Heart Hugger"] = heartHug;
            _overrides["Shadow"]       = loom;
            _overrides["Tricycle"]     = tricycle;
            _overrides["Birthday boy"] = birthdayBoy;
        }
    }
}
