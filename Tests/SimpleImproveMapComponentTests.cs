using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RimWorld;
using SimpleImprove.Core;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers what is left of the target quality store in <see cref="SimpleImproveMapComponent"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The component constructs with a null map because <c>MapComponent</c>'s constructor only
    /// assigns the field. Everything that reaches through <c>map</c> is therefore out of scope here,
    /// which is <c>EnableImprovingForColonyMechs</c>; the decision inside it is covered separately in
    /// <c>ImproveWorkersTests</c>.
    /// </para>
    /// <para>
    /// The store is now a one-way migration shim. Nothing writes to it, so these tests seed the
    /// private dictionary by reflection rather than through a setter the shipped mod does not have
    /// and should not grow one for.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class SimpleImproveMapComponentTests
    {
        private static SimpleImproveMapComponent NewComponent()
        {
            return new SimpleImproveMapComponent(null);
        }

        /// <summary>
        /// Puts an entry into the legacy store the way a loaded save would.
        /// </summary>
        private static SimpleImproveMapComponent WithStoredTarget(int thingID, QualityCategory quality)
        {
            var component = NewComponent();
            StoreOf(component)[thingID] = quality;
            return component;
        }

        private static Dictionary<int, QualityCategory> StoreOf(SimpleImproveMapComponent component)
        {
            return (Dictionary<int, QualityCategory>)typeof(SimpleImproveMapComponent)
                .GetField("targetQualities", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(component);
        }

        [Test]
        public void ConstructsWithoutAMap()
        {
            Assert.That(NewComponent(), Is.Not.Null);
        }

        [Test]
        public void AnUnknownThingHasNothingStored()
        {
            Assert.That(NewComponent().TakeTargetQuality(1234), Is.Null);
        }

        [Test]
        public void TakingAStoredTargetReturnsIt()
        {
            var component = WithStoredTarget(1234, QualityCategory.Excellent);

            Assert.That(component.TakeTargetQuality(1234), Is.EqualTo(QualityCategory.Excellent));
        }

        [Test]
        public void TakingAStoredTargetRemovesIt()
        {
            // Makes the claim a one-way move. The case it stops is load, clear the target back to
            // "any improvement", save, load again: the comp's target is null once more, and a
            // surviving entry would be applied over the choice the player just made. It is NOT the
            // reinstall case, which an earlier version of this comment claimed: a reinstall spawns
            // with respawningAfterLoad false and never reaches the store at all.
            var component = WithStoredTarget(1234, QualityCategory.Excellent);

            component.TakeTargetQuality(1234);

            Assert.That(component.TakeTargetQuality(1234), Is.Null);
            Assert.That(StoreOf(component), Is.Empty);
        }

        [Test]
        public void TakingOneTargetLeavesTheOthersAlone()
        {
            var component = WithStoredTarget(1, QualityCategory.Good);
            StoreOf(component)[2] = QualityCategory.Masterwork;

            component.TakeTargetQuality(1);

            Assert.That(component.TakeTargetQuality(2), Is.EqualTo(QualityCategory.Masterwork));
        }

        [Test]
        public void TakingAnUnknownThingIsHarmless()
        {
            Assert.That(() => NewComponent().TakeTargetQuality(999), Throws.Nothing);
        }

        [Test]
        public void AwfulSurvivesTheRoundTripRatherThanReadingAsUnset()
        {
            // QualityCategory is byte backed and Awful is zero, so a non-nullable store cannot tell
            // "aimed at Awful" from "no target". This one is stored as a value in a dictionary and
            // taken back as a nullable, which can.
            var component = WithStoredTarget(1234, QualityCategory.Awful);

            Assert.That(component.TakeTargetQuality(1234), Is.EqualTo(QualityCategory.Awful));
        }

        [Test]
        public void TheStoreIsOnlyConsultedWhenASaveIsBeingLoaded()
        {
            // An ordinary spawn does not consult it, and that includes reinstalling a minified
            // building: Frame.CompleteConstruction reaches GenSpawn.Spawn without the
            // respawningAfterLoad argument, whose default is false.
            Assert.That(
                SimpleImproveMapComponent.ShouldMigrateTargetQuality(
                    respawningAfterLoad: false, markedForImprovement: true, targetAlreadyOnComp: false),
                Is.False);
        }

        [Test]
        public void AnUnmarkedBuildingIsNeverGivenATarget()
        {
            // The invariant, tested against the case that actually occurs rather than in principle.
            // Before version 1.0.9 cancelling an improvement cleared the flag and left the target in
            // this dictionary, so an old save can easily hold an entry for a building the player
            // unmarked days before upgrading. Without this condition the migration hands it back.
            Assert.That(
                SimpleImproveMapComponent.ShouldMigrateTargetQuality(
                    respawningAfterLoad: true, markedForImprovement: false, targetAlreadyOnComp: false),
                Is.False);
        }

        [Test]
        public void TheStoreNeverOverwritesATargetTheComponentAlreadyCarries()
        {
            Assert.That(
                SimpleImproveMapComponent.ShouldMigrateTargetQuality(
                    respawningAfterLoad: true, markedForImprovement: true, targetAlreadyOnComp: true),
                Is.False);
        }

        [Test]
        public void AnOldSaveMigratesTheTargetOfAMarkedBuilding()
        {
            Assert.That(
                SimpleImproveMapComponent.ShouldMigrateTargetQuality(
                    respawningAfterLoad: true, markedForImprovement: true, targetAlreadyOnComp: false),
                Is.True);
        }

        [TestCase(false, false, false, false)]
        [TestCase(false, false, true, false)]
        [TestCase(false, true, false, false)]
        [TestCase(false, true, true, false)]
        [TestCase(true, false, false, false)]
        [TestCase(true, false, true, false)]
        [TestCase(true, true, false, true)]
        [TestCase(true, true, true, false)]
        public void TheMigrationDecisionInFull(
            bool respawningAfterLoad, bool markedForImprovement, bool targetAlreadyOnComp, bool expected)
        {
            Assert.That(
                SimpleImproveMapComponent.ShouldMigrateTargetQuality(
                    respawningAfterLoad, markedForImprovement, targetAlreadyOnComp),
                Is.EqualTo(expected));
        }

        [Test]
        public void TheStoreIsEmptiedOnceTheMapHasFinishedLoading()
        {
            // The bound on the store's life, and the half of the fix a decision function cannot
            // express. Map.FinalizeLoading spawns every thing on the map and then calls FinalizeInit
            // as its last statement, so anything still here belongs to a building that is not on
            // this map and will never get another chance to claim it. Keeping it is worse than
            // losing it: the comp writes nothing when the target is null, and null is also what
            // "any improvement" means, so a surviving entry would be applied over a deliberate
            // choice on the next load with no way to tell the two apart.
            //
            // This runs the real method. MapComponent.FinalizeInit is an empty virtual and
            // EnableImprovingForColonyMechs returns early on a null map, so the whole override is
            // reachable here, which is rare in this suite.
            var component = WithStoredTarget(1, QualityCategory.Excellent);
            StoreOf(component)[2] = QualityCategory.Legendary;

            component.FinalizeInit();

            Assert.That(StoreOf(component), Is.Empty);
            Assert.That(component.TakeTargetQuality(1), Is.Null);
        }

        [Test]
        public void FinalizingAnAlreadyEmptyStoreIsHarmless()
        {
            // Every map generated after the upgrade, and every load after the first one.
            Assert.That(() => NewComponent().FinalizeInit(), Throws.Nothing);
        }
    }
}
