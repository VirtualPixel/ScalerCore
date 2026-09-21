using ScalerCore.Handlers.EnemyVisuals;
using Xunit;

namespace ScalerCore.Tests
{
    // Covers the "shrunken enemies float" report. Up to 1.0.4 the grounding nudge was written
    // as spawnLocalY + pivotToFeet * (ratio - 1), an absolute height taken once when the
    // controller registered the enemy. That is correct only for a mesh nothing else ever
    // moves. Every enemy whose mesh transform the game rewrites per frame (the NavMeshAgent
    // owns whichever transform it sits on, Enemy.Update interpolates the whole enemy toward
    // the host's position on remote clients, EnemyBowtieAnim and EnemyValuableThrowerAnim
    // copy followTarget.position onto their own transform) got pinned at its spawn height and
    // floated the moment it reached a floor at a different level. WalksToANewFloorHeight is
    // the regression: it fails against the old formula and passes against GroundingTrack.
    public class GroundingTrackTests
    {
        const float PivotToFeet = 0.9f;
        const float Shrunk = 0.4f;

        // What the nudge is worth: the feet have to come down by this much for a shrunken
        // mesh, because scaling toward a pivot above them lifted them by the same amount.
        static float Nudge(float ratio) => PivotToFeet * (ratio - 1f);

        // Four enemies carry a transform at their soles and navigate off it: the agent is
        // warped to it, the navmesh is sampled at it, and one of them writes its own position
        // from it. The lowest rendered point is something else entirely, and grounding to the
        // renderer leaves that transform off the floor by the difference times the growth,
        // which is a grown enemy walking through the floor or above it.
        [Fact]
        public void TheGroundMarkerBeatsTheLowestRenderedPoint()
        {
            // Pivot a metre up, soles on the floor, and something on the model hanging 40cm
            // lower than the soles.
            float measured = GroundingTrack.PivotToFeet(
                pivotY: 1f, lowestRenderedY: -0.4f, markerY: 0f, hasMarker: true, ratio: 1f);
            Assert.Equal(1f, measured, 5);

            // Doubled, the mesh scales about a pivot that stays put and the soles drop a metre.
            // The lift has to be exactly that metre to put them back on the floor.
            var track = new GroundingTrack();
            Assert.Equal(1f, track.Next(0f, measured, 2f), 5);
        }

        [Fact]
        public void WithoutAMarkerTheRenderersAreAllThereIs()
        {
            Assert.Equal(1.4f, GroundingTrack.PivotToFeet(
                pivotY: 1f, lowestRenderedY: -0.4f, markerY: 0f, hasMarker: false, ratio: 1f), 5);
        }

        // The measurement is taken whenever the enemy is first scaled, so it has to read the
        // same at any size.
        [Fact]
        public void MeasuringMidGrowthGivesTheFullSizeDistance()
        {
            Assert.Equal(1f, GroundingTrack.PivotToFeet(
                pivotY: 2f, lowestRenderedY: -0.8f, markerY: 0f, hasMarker: true, ratio: 2f), 5);
        }

        [Fact]
        public void RewritingOurOwnValueIsStable()
        {
            var track = new GroundingTrack();
            const float restingY = 2f;

            float y = track.Next(restingY, PivotToFeet, Shrunk);
            Assert.Equal(restingY + Nudge(Shrunk), y, 5);

            // A mesh nobody else touches reads back exactly what we wrote, so the next
            // hundred frames have to land on the same height instead of walking downward.
            for (int frame = 0; frame < 100; frame++)
                y = track.Next(y, PivotToFeet, Shrunk);

            Assert.Equal(restingY + Nudge(Shrunk), y, 5);
        }

        [Fact]
        public void WalksToANewFloorHeight()
        {
            var track = new GroundingTrack();
            float y = track.Next(2f, PivotToFeet, Shrunk);

            // The enemy takes the stairs: something else writes the mesh height every frame,
            // and each of those heights is the pose the mesh is supposed to hold.
            float[] floors = { 2.25f, 2.5f, 2.75f, 3f, 5.5f, 1.25f };
            foreach (float floorY in floors)
            {
                y = track.Next(floorY, PivotToFeet, Shrunk);
                Assert.Equal(floorY + Nudge(Shrunk), y, 5);
            }
        }

        [Fact]
        public void TransitionKeepsUpWithAChangingRatio()
        {
            var track = new GroundingTrack();
            const float restingY = 1.5f;
            float y = restingY;

            // Shrink animation: the ratio moves every frame while the mesh stays put, so the
            // nudge has to be re-derived from the same resting height, not compounded.
            foreach (float ratio in new[] { 1f, 0.85f, 0.7f, 0.55f, Shrunk })
            {
                y = track.Next(y, PivotToFeet, ratio);
                Assert.Equal(restingY + Nudge(ratio), y, 5);
            }
        }

        [Fact]
        public void ReleaseHandsBackTheHeightTheGameHad()
        {
            var track = new GroundingTrack();
            const float restingY = 2f;
            float y = track.Next(restingY, PivotToFeet, Shrunk);

            Assert.True(track.TryRelease(y, out float restored));
            Assert.Equal(restingY, restored, 5);
        }

        [Fact]
        public void ReleaseLeavesAHeightSomebodyElseWrote()
        {
            var track = new GroundingTrack();
            track.Next(2f, PivotToFeet, Shrunk);

            // The game moved the mesh after our last write, so there is no nudge of ours left
            // to take out and the current height has to survive the restore untouched.
            Assert.False(track.TryRelease(7.5f, out _));
        }

        [Fact]
        public void ForgottenTrackReadsThePoseFresh()
        {
            var track = new GroundingTrack();
            float y = track.Next(2f, PivotToFeet, Shrunk);

            // Expand puts the mesh back and the next shrink starts over from wherever the
            // enemy is standing by then, not from where the last session left off.
            track.Forget();
            y = track.Next(9f, PivotToFeet, Shrunk);
            Assert.Equal(9f + Nudge(Shrunk), y, 5);
        }
    }
}
