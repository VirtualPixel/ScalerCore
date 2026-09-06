using ScalerCore.Utilities;
using Xunit;

namespace ScalerCore.Tests
{
    // The level-change cleanup is a postfix on RunManager.ChangeLevel, and a postfix runs
    // whether or not the game took the early return at the top of that method. On a
    // non-host in a level the game returns without doing anything, so the cleanup must
    // not run there either, or that one client restores every scale and un-shrinks the
    // collapsing level while everyone else keeps going.
    public class LevelChangeGateTests
    {
        [Fact]
        public void NonHostInALevelDoesNotProceed() =>
            Assert.False(LevelChangeGate.Proceeds(menuLevel: false, masterOrSingleplayer: false, restarting: false));

        [Fact]
        public void HostInALevelProceeds() =>
            Assert.True(LevelChangeGate.Proceeds(menuLevel: false, masterOrSingleplayer: true, restarting: false));

        [Fact]
        public void AnyoneOnTheMenuLevelProceeds() =>
            Assert.True(LevelChangeGate.Proceeds(menuLevel: true, masterOrSingleplayer: false, restarting: false));

        [Fact]
        public void NobodyProceedsWhileRestarting()
        {
            Assert.False(LevelChangeGate.Proceeds(menuLevel: true, masterOrSingleplayer: true, restarting: true));
            Assert.False(LevelChangeGate.Proceeds(menuLevel: false, masterOrSingleplayer: true, restarting: true));
        }
    }
}
