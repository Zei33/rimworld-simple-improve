using NUnit.Framework;
using SimpleImprove.Core;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers <see cref="ImproveWorkers.ShouldEnableForMech"/>, the decision that makes the mech work
    /// type fix do anything at all in an existing save.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The def patch on its own is inert for every mech that predates it. Adding the work type to
    /// <c>mechEnabledWorkTypes</c> stops it being disabled, but the priority already stored against
    /// the mech stays at 0, nothing re-runs <c>EnableAndInitialize</c> for a pawn that already has
    /// settings, and <c>GetPriority</c>'s non-zero shortcut is gated on <c>RaceProps.Humanlike</c>.
    /// So the mech keeps the 0 and never takes the job. This predicate is what corrects it.
    /// </para>
    /// <para>
    /// The readings it decides over are not reachable here. <c>IsColonyMech</c> goes through
    /// <c>ModsConfig</c>, <c>WorkTypeIsDisabled</c> through <c>GetDisabledWorkTypes</c>, and
    /// <c>GetPriority</c> needs a real <c>Pawn_WorkSettings</c>. They are taken in
    /// <c>SimpleImproveMapComponent.EnableImprovingForColonyMechs</c> and passed in as three
    /// primitives, which is the only reason any of this has coverage.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class ImproveWorkersTests
    {
        [Test]
        public void AMechThatHasTheWorkTypeAtZeroIsSwitchedOn()
        {
            Assert.That(
                ImproveWorkers.ShouldEnableForMech(isColonyMech: true, workTypeIsDisabled: false, currentPriority: 0),
                Is.True);
        }

        [Test]
        public void AColonistIsNeverTouched()
        {
            // The asymmetry the whole correction rests on. A colonist at 0 may well have been set to 0
            // by the player in the Work tab, and overriding that would be taking a decision away from
            // them. A mech cannot have been, because no UI in the game can set it.
            Assert.That(
                ImproveWorkers.ShouldEnableForMech(isColonyMech: false, workTypeIsDisabled: false, currentPriority: 0),
                Is.False);
        }

        [Test]
        public void AMechAlreadyAboveZeroIsLeftAlone()
        {
            // Idempotence. This runs on every map load, so raising an already raised priority, or
            // resetting one that something else set higher, would compound every time.
            foreach (var priority in new[] { 1, 2, 3, 4 })
            {
                Assert.That(
                    ImproveWorkers.ShouldEnableForMech(isColonyMech: true, workTypeIsDisabled: false, priority),
                    Is.False,
                    "A mech already at priority " + priority + " must be left alone.");
            }
        }

        [Test]
        public void AMechTheWorkTypeIsStillDisabledForIsLeftAlone()
        {
            // Not merely pointless, actively noisy: Pawn_WorkSettings.SetPriority logs a red error and
            // refuses when asked for a non-zero priority on a disabled work type, so an unguarded call
            // would spam the log once per mech per load for anyone whose mech still cannot do it.
            Assert.That(
                ImproveWorkers.ShouldEnableForMech(isColonyMech: true, workTypeIsDisabled: true, currentPriority: 0),
                Is.False);
        }

        [Test]
        public void TheDisabledTestBeatsEveryOtherReason()
        {
            // The one combination that must never come out true, whatever else holds.
            foreach (var priority in new[] { 0, 1, 3 })
            {
                Assert.That(
                    ImproveWorkers.ShouldEnableForMech(isColonyMech: true, workTypeIsDisabled: true, priority),
                    Is.False);
                Assert.That(
                    ImproveWorkers.ShouldEnableForMech(isColonyMech: false, workTypeIsDisabled: true, priority),
                    Is.False);
            }
        }

        [Test]
        public void TheMechPriorityMatchesWhatTheGameWouldHaveAssigned()
        {
            // Pawn_WorkSettings.EnableAndInitialize assigns 3 to every work type it switches on, so a
            // corrected mech is indistinguishable from one gestated after the mod was added. Any other
            // number would be this mod inventing a priority the game never uses.
            Assert.That(ImproveWorkers.DefaultMechPriority, Is.EqualTo(3));
        }

        [Test]
        public void ANegativePriorityIsNotTreatedAsSwitchedOff()
        {
            // Nothing produces one, but "switched off" is exactly zero rather than "not positive", and
            // writing it as currentPriority <= 0 would silently start rewriting values the game itself
            // does not produce.
            Assert.That(
                ImproveWorkers.ShouldEnableForMech(isColonyMech: true, workTypeIsDisabled: false, currentPriority: -1),
                Is.False);
        }
    }
}
