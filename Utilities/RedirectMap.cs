using System.Collections.Generic;

namespace ScalerCore.Utilities
{
    // Where an object went when it was replaced. A native respawn destroys the old GameObject
    // and spawns a new one, so a mod holding the old reference, or the instance id it took off
    // it, is holding something that no longer exists. Every id an object has ever had leads to
    // whatever stands in its place now, through as many respawns as the level has had.
    //
    // Unity hands out an instance id once per session and never reuses it, so an id that is not
    // in here has never been swapped and can be read off the object itself. Cleared with the
    // rest of the registry on level change.
    internal sealed class RedirectMap<T> where T : class
    {
        // A chain this long is a bug somewhere else; stop walking rather than spin.
        const int MaxHops = 16;

        readonly Dictionary<int, int> _moved = new();
        readonly Dictionary<int, T> _targets = new();

        internal int Count => _moved.Count;

        internal void Add(int oldId, int freshId, T fresh)
        {
            if (oldId == freshId) return;
            _moved[oldId] = freshId;
            _targets[freshId] = fresh;
            // The old id is a signpost now, not a destination.
            _targets.Remove(oldId);
        }

        internal T? Resolve(int id)
        {
            int hops = 0;
            while (_moved.TryGetValue(id, out int next) && hops++ < MaxHops)
                id = next;
            return _targets.TryGetValue(id, out var fresh) ? fresh : null;
        }

        internal bool TryResolve(int id, out T fresh)
        {
            var found = Resolve(id);
            fresh = found!;
            return found != null;
        }

        internal void Clear()
        {
            _moved.Clear();
            _targets.Clear();
        }
    }
}
