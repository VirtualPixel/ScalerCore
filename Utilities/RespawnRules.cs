namespace ScalerCore.Utilities
{
    // Whether a scale change goes out as a native respawn (visible to everyone, mod or not),
    // waits until the object is out of everyone's hands, or stays on the shrink RPC that only
    // ScalerCore clients understand. Pure so the table has tests.
    internal static class RespawnRules
    {
        // Off until it has been through a real room. The respawn is the only path an unmodded
        // client can see, so it will be the default once it has stood up in play; until then
        // it is a switch for the people testing it.
        public const bool NativeSyncDefault = false;

        public enum Verdict { Never, WhenFree, Now }

        public static Verdict Decide(bool nativeSync, bool multiplayer, bool isMaster, bool hasSceneView,
            bool hasPrefabPath, bool kindEligible, bool held, bool inInventory, bool driven, bool whileHeld)
        {
            if (!nativeSync || !multiplayer || !isMaster || !hasSceneView || !hasPrefabPath || !kindEligible)
                return Verdict.Never;
            if (held || inInventory || driven)
                return whileHeld ? Verdict.WhenFree : Verdict.Never;
            return Verdict.Now;
        }

        // "Valuable Goblet(Clone)" -> "Valuables/Valuable Goblet". Every spawned object carries
        // the prefab's name plus Unity's clone suffix; the game itself rebuilds resource paths
        // this way when an enemy drops a valuable.
        public static string? PathFromName(string? goName, string folder)
        {
            if (string.IsNullOrWhiteSpace(goName)) return null;
            string name = goName!.Trim();
            while (name.EndsWith("(Clone)"))
                name = name.Substring(0, name.Length - "(Clone)".Length).TrimEnd();
            if (name.Length == 0) return null;
            return folder + "/" + name;
        }
    }
}
