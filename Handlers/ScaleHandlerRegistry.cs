using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScalerCore.Handlers
{
    public static class ScaleHandlerRegistry
    {
        struct Entry
        {
            public IScaleHandler Handler;
            public Func<GameObject, bool> Predicate;
            public int Priority;
        }

        static readonly List<Entry> _entries = new();
        static bool _builtinsRegistered;

        /// <summary>
        /// Register a handler with a predicate that determines whether it applies to a given GameObject.
        /// Higher priority wins when multiple predicates match. Built-in handlers use priority 0.
        /// External mods should use priority > 0 to override defaults.
        /// </summary>
        public static void Register(IScaleHandler handler, Func<GameObject, bool> predicate, int priority = 0)
        {
            _entries.Add(new Entry { Handler = handler, Predicate = predicate, Priority = priority });
            // Keep sorted descending by priority so Resolve returns highest-priority match first.
            _entries.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        /// <summary>
        /// Resolve which handler applies to a game object. Returns null if no handler matches
        /// (e.g. doors use base ScaleController only).
        /// </summary>
        public static IScaleHandler? Resolve(GameObject target)
        {
            EnsureBuiltins();
            foreach (var entry in _entries)
            {
                if (entry.Predicate(target))
                    return entry.Handler;
            }
            return null;
        }

        static void EnsureBuiltins()
        {
            if (_builtinsRegistered) return;
            _builtinsRegistered = true;

            Register(new EnemyHandler(),
                go => go.GetComponent<EnemyRigidbody>() != null, 0);
            Register(new PlayerHandler(),
                go => go.GetComponent<PlayerAvatar>() != null, 0);
            Register(new ValuableHandler(),
                go => go.GetComponent<ValuableObject>() != null, 0);
            // Items: has ItemAttributes but NOT ValuableObject (valuables matched above with higher list position).
            Register(new ItemHandler(),
                go => go.GetComponent<ItemAttributes>() != null && go.GetComponent<ValuableObject>() == null, 0);
        }
    }
}
