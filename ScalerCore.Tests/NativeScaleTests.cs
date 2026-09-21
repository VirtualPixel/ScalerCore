using ScalerCore.Utilities;
using Xunit;
using Verdict = ScalerCore.Utilities.RespawnRules.Verdict;

namespace ScalerCore.Tests
{
    // A natively scaled respawn is the one path an unmodded client sees. If the data does not
    // read back, the clone spawns at the wrong size with no session behind it and nothing says why.
    public class NativeScaleTests
    {
        [Fact]
        public void OptionsSurviveTheWire()
        {
            var o = ScaleOptions.Growth;
            o.Factor = 2.5f; o.RestoreSpeed = 3f; o.EnemyHeightFactorCap = 1.2f; o.SuppressCameraShake = true; o.RejectExternalApply = true;
            var back = ScaleOptionsCodec.Unpack(ScaleOptionsCodec.PackFloats(o), ScaleOptionsCodec.PackBools(o), ScaleOptions.Default);
            Assert.Equal(o.Factor, back.Factor);
            Assert.Equal(o.RestoreSpeed, back.RestoreSpeed);
            Assert.Equal(o.EnemyHeightFactorCap, back.EnemyHeightFactorCap);
            Assert.True(back.SuppressCameraShake);
            Assert.True(back.RejectExternalApply);
            Assert.Equal(o.MassCap, back.MassCap);
        }

        [Fact]
        public void TheRespawnFlagSurvivesTheWireAndAn106HostLeavesItOff()
        {
            var o = ScaleOptions.Growth;
            o.KeepThroughRespawn = true;
            var back = ScaleOptionsCodec.Unpack(ScaleOptionsCodec.PackFloats(o), ScaleOptionsCodec.PackBools(o), ScaleOptions.Default);
            Assert.True(back.KeepThroughRespawn);

            // The seven-flag array a 1.0.6 host sends: the slot is not there and must read false,
            // never leave a monster stuck at boss size through every respawn of the level.
            var old = ScaleOptionsCodec.Unpack(
                ScaleOptionsCodec.PackFloats(o),
                new[] { false, false, false, false, true, true, false },
                ScaleOptions.Default);
            Assert.True(old.IgnoreBonkExpand);
            Assert.True(old.RejectExternalApply);
            Assert.False(old.KeepThroughRespawn);
        }

        [Fact]
        public void AShortArrayFromAnOldHostStillReads()
        {
            var o = ScaleOptions.Default;
            var back = ScaleOptionsCodec.Unpack(new[] { 0.5f, 2f, 50f, 0.75f, 1.5f, 1.5f, 5f }, new[] { true, false }, o);
            Assert.Equal(0.5f, back.Factor);
            Assert.True(back.PreserveMass);
            Assert.Equal(1f, back.AudioPresence);
            Assert.False(back.SuppressCameraShake);
        }

        [Fact]
        public void InstantiationDataRoundTrips()
        {
            var o = ScaleOptions.Default;
            o.Factor = 0.4f;
            var data = NativeScaleData.Pack(new[] { 0.4f, 0.4f, 0.4f }, new[] { 1f, 1f, 1f }, 1f, 12.5f,
                ScaleOptionsCodec.PackFloats(o), ScaleOptionsCodec.PackBools(o));
            // The game reads exactly these three as floats.
            Assert.IsType<float>(data[0]);
            Assert.Equal(0.4f, (float)data[2]);
            Assert.True(NativeScaleData.TryUnpack(data, out var original, out float from, out float remaining, out var opts, out var flags));
            Assert.Equal(new[] { 1f, 1f, 1f }, original);
            Assert.Equal(1f, from);
            Assert.Equal(12.5f, remaining);
            Assert.Equal(0.4f, ScaleOptionsCodec.Unpack(opts, flags, ScaleOptions.Default).Factor);
        }

        [Fact]
        public void VanillaOrForeignDataIsNotOurs()
        {
            Assert.False(NativeScaleData.TryUnpack(null, out _, out _, out _, out _, out _));
            Assert.False(NativeScaleData.TryUnpack(new object[] { 3f, 3f, 3f }, out _, out _, out _, out _, out _));
            Assert.False(NativeScaleData.TryUnpack(new object[] { 3f, 3f, 3f, "something else", 1f, 1f, 1f, 0f, 0f, new float[0], new bool[0] }, out _, out _, out _, out _, out _));
        }

        [Theory]
        [InlineData("Valuable Goblet(Clone)", "Valuables/Valuable Goblet")]
        [InlineData("Valuable Goblet(Clone)(Clone)", "Valuables/Valuable Goblet")]
        [InlineData("  Item Grenade Duct Taped (Clone)", "Valuables/Item Grenade Duct Taped")]
        public void PrefabPathComesOffTheCloneName(string name, string expected) =>
            Assert.Equal(expected, RespawnRules.PathFromName(name, "Valuables"));

        [Fact]
        public void EmptyNamesGiveNoPath()
        {
            Assert.Null(RespawnRules.PathFromName("(Clone)", "Valuables"));
            Assert.Null(RespawnRules.PathFromName(null, "Valuables"));
        }

        // Off until it has been played: a default that flipped on by itself would put every
        // room on a path nobody has watched. The table below is what it does once switched on.
        [Fact]
        public void TheRespawnShipsOff()
        {
            Assert.False(RespawnRules.NativeSyncDefault);
            Assert.Equal(RespawnRules.Verdict.Now,
                RespawnRules.Decide(true, true, true, true, true, true, false, false, false, true));
        }

        [Fact]
        public void RespawnDecisionTable()
        {
            // Everything lined up: respawn now.
            Assert.Equal(RespawnRules.Verdict.Now, RespawnRules.Decide(true, true, true, true, true, true, false, false, false, true));
            // Any missing prerequisite: the RPC path, never a respawn.
            Assert.Equal(RespawnRules.Verdict.Never, RespawnRules.Decide(false, true, true, true, true, true, false, false, false, true));
            Assert.Equal(RespawnRules.Verdict.Never, RespawnRules.Decide(true, false, true, true, true, true, false, false, false, true));
            Assert.Equal(RespawnRules.Verdict.Never, RespawnRules.Decide(true, true, false, true, true, true, false, false, false, true));
            Assert.Equal(RespawnRules.Verdict.Never, RespawnRules.Decide(true, true, true, false, true, true, false, false, false, true));
            Assert.Equal(RespawnRules.Verdict.Never, RespawnRules.Decide(true, true, true, true, false, true, false, false, false, true));
            Assert.Equal(RespawnRules.Verdict.Never, RespawnRules.Decide(true, true, true, true, true, false, false, false, false, true));
            // In someone's hands, inventory or seat: the RPC now, the respawn when it is free.
            Assert.Equal(RespawnRules.Verdict.WhenFree, RespawnRules.Decide(true, true, true, true, true, true, true, false, false, true));
            Assert.Equal(RespawnRules.Verdict.WhenFree, RespawnRules.Decide(true, true, true, true, true, true, false, true, false, true));
            Assert.Equal(RespawnRules.Verdict.WhenFree, RespawnRules.Decide(true, true, true, true, true, true, false, false, true, true));
            // Unless the mod says held objects never respawn.
            Assert.Equal(RespawnRules.Verdict.Never, RespawnRules.Decide(true, true, true, true, true, true, true, false, false, false));
        }

        // An object that was respawned answers to the id it had before, because that is the id
        // a mod took off it. ShrinkCart keeps a set of them for the items it shrank in a cart.
        [Fact]
        public void AnOldIdLeadsToWhatReplacedIt()
        {
            var map = new RedirectMap<string>();
            map.Add(oldId: 11, freshId: 22, "the clone");
            Assert.Equal("the clone", map.Resolve(11));
            Assert.True(map.TryResolve(11, out string fresh));
            Assert.Equal("the clone", fresh);
            // The body that is standing there now answers for itself.
            Assert.Equal("the clone", map.Resolve(22));
            // An id that was never swapped is nobody's business: the object has itself.
            Assert.Null(map.Resolve(33));
            Assert.False(map.TryResolve(33, out _));
        }

        [Fact]
        public void EveryIdAnObjectEverHadLeadsToTheLastOne()
        {
            var map = new RedirectMap<string>();
            map.Add(11, 22, "shrunk");
            map.Add(22, 33, "shrunk again");
            map.Add(33, 44, "back to size");
            Assert.Equal("back to size", map.Resolve(11));
            Assert.Equal("back to size", map.Resolve(22));
            Assert.Equal("back to size", map.Resolve(33));
            Assert.Equal(3, map.Count);
        }

        [Fact]
        public void TheLevelChangeTakesTheIdsWithIt()
        {
            var map = new RedirectMap<string>();
            map.Add(11, 22, "the clone");
            map.Clear();
            Assert.Null(map.Resolve(11));
            Assert.Equal(0, map.Count);
        }

        [Fact]
        public void AnIdThatLeadsBackToItselfDoesNotSpin()
        {
            var map = new RedirectMap<string>();
            map.Add(11, 22, "there");
            map.Add(22, 11, "back");
            Assert.NotNull(map.Resolve(11));
        }

        // What is kept off the respawn path and why. A cart item and anything wearing another
        // mod's component stay on the shrink RPC for good; a held object only waits.
        [Fact]
        public void TheExclusionTable()
        {
            var kind    = RespawnRules.Exclusion.Kind;
            var inCart  = RespawnRules.Exclusion.InCart;
            var foreign = RespawnRules.Exclusion.Foreign;
            var held    = RespawnRules.Exclusion.Held;

            Assert.Equal(RespawnRules.Exclusion.None, RespawnRules.Excluded(kindEligible: true, inCart: false, foreignComponent: false, held: false));
            Assert.Equal(kind,    RespawnRules.Excluded(false, false, false, false));
            Assert.Equal(inCart,  RespawnRules.Excluded(true,  true,  false, false));
            Assert.Equal(foreign, RespawnRules.Excluded(true,  false, true,  false));
            Assert.Equal(held,    RespawnRules.Excluded(true,  false, false, true));
            // A cart item in someone's hand is a cart item: waiting for the hand would not help.
            Assert.Equal(inCart,  RespawnRules.Excluded(true,  true,  false, true));
            Assert.Equal(inCart,  RespawnRules.Excluded(true,  true,  true,  false));
            Assert.Equal(kind,    RespawnRules.Excluded(false, true,  true,  true));
        }

        [Fact]
        public void OnlyTheHandLetsGo()
        {
            Assert.True(RespawnRules.RespawnAllowed(RespawnRules.Exclusion.None));
            Assert.True(RespawnRules.RespawnAllowed(RespawnRules.Exclusion.Held));
            Assert.False(RespawnRules.RespawnAllowed(RespawnRules.Exclusion.Kind));
            Assert.False(RespawnRules.RespawnAllowed(RespawnRules.Exclusion.InCart));
            Assert.False(RespawnRules.RespawnAllowed(RespawnRules.Exclusion.Foreign));
        }

        // The two tables together: an excluded object never reaches the respawn, whatever the
        // rest of the room says, and a held one comes back to it.
        [Fact]
        public void AnExcludedObjectNeverRespawnsAndAHeldOneWaits()
        {
            Verdict For(RespawnRules.Exclusion reason, bool held) => RespawnRules.Decide(
                nativeSync: true, multiplayer: true, isMaster: true, hasSceneView: true, hasPrefabPath: true,
                eligible: RespawnRules.RespawnAllowed(reason), held: held, inInventory: false, driven: false, whileHeld: true);

            Assert.Equal(Verdict.Now,      For(RespawnRules.Exclusion.None,    held: false));
            Assert.Equal(Verdict.Never,    For(RespawnRules.Exclusion.InCart,  held: false));
            Assert.Equal(Verdict.Never,    For(RespawnRules.Exclusion.Foreign, held: false));
            Assert.Equal(Verdict.WhenFree, For(RespawnRules.Exclusion.Held,    held: true));
        }
    }
}
