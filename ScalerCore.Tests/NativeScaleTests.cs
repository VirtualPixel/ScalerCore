using ScalerCore.Utilities;
using Xunit;

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
    }
}
