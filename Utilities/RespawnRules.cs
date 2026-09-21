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

        /// <summary>Why an object was kept off the respawn path. See <see cref="Excluded"/>.</summary>
        public enum Exclusion
        {
            None,
            // Players, enemies, doors: nothing to respawn from, and the RPC is the whole story.
            Kind,
            // Sitting in a cart. The cart lists what it carries and other mods key off that list,
            // so an object that leaves and comes back under a new id is an orphan in someone's
            // bookkeeping and, for the cart itself, a half second of a hole in its haul.
            InCart,
            // Something on the object comes from another mod's assembly. A respawn spawns the
            // prefab, which has no such component and none of the state that went with it.
            Foreign,
            // In a hand, an inventory slot or a seat. Not a refusal: the RPC does the size now
            // and the swap waits until it is free.
            Held,
        }

        // Whether the reasons above let an object be respawned at all. Held is the one that
        // clears later, so it leaves the object eligible and Decide holds the swap instead.
        public static bool RespawnAllowed(Exclusion reason) =>
            reason is Exclusion.None or Exclusion.Held;

        // The order is the order the reasons last: the permanent ones first, so an object held
        // over a cart reads as a cart item rather than waiting for a hand that will not help.
        public static Exclusion Excluded(bool kindEligible, bool inCart, bool foreignComponent, bool held)
        {
            if (!kindEligible) return Exclusion.Kind;
            if (inCart) return Exclusion.InCart;
            if (foreignComponent) return Exclusion.Foreign;
            if (held) return Exclusion.Held;
            return Exclusion.None;
        }

        public static Verdict Decide(bool nativeSync, bool multiplayer, bool isMaster, bool hasSceneView,
            bool hasPrefabPath, bool eligible, bool held, bool inInventory, bool driven, bool whileHeld)
        {
            if (!nativeSync || !multiplayer || !isMaster || !hasSceneView || !hasPrefabPath || !eligible)
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
