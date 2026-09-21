using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ScalerCore.Utilities
{
    // Calls into the game that bind at runtime instead of compile time.
    //
    // Semiwork moves optional parameters around between game builds. A mod compiled against
    // one build carries the exact overload it saw, and the first time that method runs on a
    // build where the overload changed shape the game throws MissingMethodException and the
    // mod is dead for the session. That is exactly how 1.0.3 died on OverrideSpeed, and 1.0.4
    // was only a rebuild, so the next shuffle would have done it again. Looking the method up
    // by name once and filling whatever trailing parameters this build has with their own
    // defaults keeps one DLL working either way.
    //
    // Cost is one reflection Invoke per call, fine for anything that is not per frame.
    // Per-frame call sites use Bind once and keep the result.
    internal static class GameCall
    {
        static readonly Dictionary<string, MethodInfo?> Methods = new();

        // Invoke type.name on target with the arguments we know about. Missing trailing
        // parameters get the game's own defaults, extra arguments this build no longer takes
        // are dropped. Does nothing when the method is not in this build (logged once).
        public static object? Invoke(object? target, Type type, string name, params object?[] args)
        {
            var method = Lookup(type, name, args);
            return method?.Invoke(target, Fit(method, args));
        }

        // Resolve once and hold the argument array. Write the arguments that change into Args
        // and call Run; the trailing parameters this build has are already sitting at their
        // own defaults.
        public static Binding Bind(Type type, string name, params object?[] args)
        {
            var method = Lookup(type, name, args);
            return new Binding(method, method == null ? args : Fit(method, args));
        }

        internal sealed class Binding
        {
            readonly MethodInfo? _method;

            public readonly object?[] Args;

            internal Binding(MethodInfo? method, object?[] args)
            {
                _method = method;
                Args = args;
            }

            public void Run(object? target) => _method?.Invoke(target, Args);
        }

        static MethodInfo? Lookup(Type type, string name, object?[] args)
        {
            var key = type.FullName + "." + name + "/" + args.Length;
            if (Methods.TryGetValue(key, out var cached)) return cached;

            var method = Resolve(type, name, args);
            Methods[key] = method;
            if (method == null)
                Plugin.Log.LogWarning($"[SC] {type.Name}.{name} is not in this game build, call skipped");
            return method;
        }

        // Grow or shrink the argument list to the parameter list this build has, defaulting
        // anything we did not pass.
        static object?[] Fit(MethodInfo method, object?[] args)
        {
            var pars = method.GetParameters();
            if (pars.Length == args.Length) return args;

            var bound = new object?[pars.Length];
            Array.Copy(args, bound, Math.Min(args.Length, pars.Length));
            for (int i = args.Length; i < pars.Length; i++)
                bound[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue
                         : pars[i].ParameterType.IsValueType ? Activator.CreateInstance(pars[i].ParameterType)
                         : null;
            return bound;
        }

        // Pick the overload whose leading parameters accept our arguments. Prefer one that
        // takes at least as many parameters as we pass, then the one closest in length.
        static MethodInfo? Resolve(Type type, string name, object?[] args)
        {
            MethodInfo? best = null;
            int bestRank = int.MaxValue;
            foreach (var m in type.GetMethods(AccessTools.all))
            {
                if (m.Name != name) continue;
                var p = m.GetParameters();
                if (!LeadingArgsFit(p, args)) continue;
                bool longEnough = p.Length >= args.Length;
                int rank = (longEnough ? 0 : 1000) + Math.Abs(p.Length - args.Length);
                if (rank < bestRank) { best = m; bestRank = rank; }
            }
            return best;
        }

        static bool LeadingArgsFit(ParameterInfo[] p, object?[] args)
        {
            int n = Math.Min(p.Length, args.Length);
            for (int i = 0; i < n; i++)
            {
                var t = p[i].ParameterType;
                if (t.IsByRef) t = t.GetElementType()!;
                if (args[i] == null) { if (t.IsValueType) return false; continue; }
                if (!t.IsInstanceOfType(args[i])) return false;
            }
            return true;
        }
    }
}
