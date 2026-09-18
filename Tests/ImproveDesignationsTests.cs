using System.Collections.Generic;
using NUnit.Framework;
using SimpleImprove.Core;
using Verse;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers the two decisions the designation-driven work giver rests on:
    /// <see cref="ImproveDesignations.ScanTargets"/>, which is the entire search set, and
    /// <see cref="ImproveDesignations.RepairNeeded"/>, which is what stops a building that moved map
    /// falling out of that set forever.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ScanTargets</c> is unusual for this harness in being testable end to end rather than over
    /// primitives. <c>new Designation(LocalTargetInfo, DesignationDef)</c> works outside the game, and
    /// so does <c>new Thing()</c>, so the real projection runs here on real designations.
    /// </para>
    /// <para>
    /// What is still out of reach is where the designations come from.
    /// <c>DesignationManager</c> cannot be constructed at all (its <c>DefMap</c> throws without an
    /// initialised def database), so <c>SpawnedDesignationsOfDef</c>, <c>AnySpawnedDesignationOfDef</c>
    /// and therefore <c>ShouldSkip</c> itself have no coverage here. Neither does the
    /// <c>PostSpawnSetup</c> that calls <c>RepairNeeded</c>. <c>Thing.Spawned</c> reads
    /// <c>Find.Maps</c> and is false for every thing this harness can build, which is why no test here
    /// asserts anything about it: such a test would pass against a projection that returned nothing.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class ImproveDesignationsTests
    {
        private static DesignationDef ImproveDef()
        {
            return new DesignationDef { defName = "Designation_Improve" };
        }

        private static Designation On(Thing thing)
        {
            return new Designation(new LocalTargetInfo(thing), ImproveDef());
        }

        private static Designation OnCell()
        {
            return new Designation(new LocalTargetInfo(new IntVec3(3, 0, 7)), ImproveDef());
        }

        [Test]
        public void EveryDesignatedBuildingReachesTheSearchSet()
        {
            var first = new Building();
            var second = new Building();

            var targets = ImproveDesignations.ScanTargets(new List<Designation> { On(first), On(second) });

            Assert.That(targets, Is.EqualTo(new Thing[] { first, second }));
        }

        [Test]
        public void ADesignationOverACellContributesNoNullToTheSearchSet()
        {
            // SpawnedDesignationsOfDef admits a designation whose target is not a thing at all: its
            // filter is `!target.HasThing || target.Thing.Map == map`, and the first half passes a
            // cell-targeted one straight through. LocalTargetInfo.Thing is then null.
            //
            // A null in the search set is not a harmless no-op. JobGiver_Work wraps the whole scan in
            // a try that ends in Log.Error, not Log.ErrorOnce, so it would print for every pawn on
            // every job search and the work giver would yield nothing at all.
            var building = new Building();

            var targets = ImproveDesignations.ScanTargets(new List<Designation> { OnCell(), On(building), OnCell() });

            Assert.That(targets, Is.EqualTo(new Thing[] { building }));
            Assert.That(targets, Has.No.Null);
        }

        [Test]
        public void TheSearchSetIsAListSoTheGlobalSearchTakesItsFastPath()
        {
            // Not a style preference. GenClosest.ClosestThing_Global receives the set as a non-generic
            // IEnumerable and type-tests for four typed lists, IList<Thing> among them, to get a Count
            // and an indexed loop. A yield-return iterator misses that, and so would a HashSet<Thing>,
            // which implements neither IList<T> nor the non-generic ICollection.
            var targets = ImproveDesignations.ScanTargets(new List<Designation> { On(new Building()) });

            Assert.That(targets, Is.InstanceOf<IList<Thing>>());
        }

        [Test]
        public void TheSearchSetDoesNotHoldTheDesignationEnumeratorOpen()
        {
            // The real source is DesignationManager.SpawnedDesignationsOfDef, which yields over the
            // live list for that def, and mutating a List while a foreach over it is open throws.
            // Nothing in the scan currently removes a designation, so this pins a property that
            // forecloses the hazard rather than one that fixes a live bug: the projection has
            // finished reading its source before it returns.
            var source = new List<Designation> { On(new Building()), On(new Building()) };

            var targets = ImproveDesignations.ScanTargets(source);
            source.Clear();

            // Has.Count rather than .Count, so that turning this back into a yield-return iterator
            // fails here as a test rather than as a compile error in this file.
            Assert.That(targets, Has.Count.EqualTo(2));
        }

        [Test]
        public void AnEmptySetIsEmptyRatherThanNull()
        {
            // ShouldSkip should mean this never runs with nothing designated, but the float menu calls
            // PotentialWorkThingsGlobal before ShouldSkip and null-tests the result, and GenClosest
            // calls EnumerableNullOrEmpty on it. Returning an empty list rather than null keeps both
            // on their cheap path.
            Assert.That(ImproveDesignations.ScanTargets(new List<Designation>()), Is.Empty);
            Assert.That(ImproveDesignations.ScanTargets(null), Is.Empty);
        }

        [Test]
        public void AMarkedBuildingThatArrivesWithoutItsDesignationGetsOneBack()
        {
            // The case the whole repair exists for. A gravship jump despawns the building before
            // sweeping the substructure, so the sweep cannot find it through thingGrid and the
            // designation is left on the old map; the component and its flag travel with the building.
            // Without this the building is marked, says so in its inspect string, refuses to be marked
            // again, and is never improved.
            Assert.That(
                ImproveDesignations.RepairNeeded(markedInComponent: true, designationOnThisMap: false),
                Is.EqualTo(ImproveDesignationRepair.AddDesignation));
        }

        [Test]
        public void TheOrdinaryCaseIsLeftAlone()
        {
            // This runs on every spawn of every improvable building, including every one on every map
            // load, so the common path has to be a no-op. AddDesignation unforbids its target and
            // throws motes at it, which would be visible if this were loose.
            Assert.That(
                ImproveDesignations.RepairNeeded(markedInComponent: true, designationOnThisMap: true),
                Is.EqualTo(ImproveDesignationRepair.None));
            Assert.That(
                ImproveDesignations.RepairNeeded(markedInComponent: false, designationOnThisMap: false),
                Is.EqualTo(ImproveDesignationRepair.None));
        }

        [Test]
        public void AnUnmarkedBuildingIsNeverGivenADesignationBack()
        {
            // Direction matters. Repairing towards "marked" for a building whose flag is clear would
            // resurrect an improvement the player cancelled, which is the one outcome worse than the
            // stray icon this deliberately leaves in place.
            Assert.That(
                ImproveDesignations.RepairNeeded(markedInComponent: false, designationOnThisMap: false),
                Is.Not.EqualTo(ImproveDesignationRepair.AddDesignation));
        }

        [Test]
        public void TheStrayDesignationCaseIsDeliberatelyNotRepaired()
        {
            // Records a decision rather than an omission, so that changing it is a decision too.
            // A designation with the flag clear costs a stray icon and a one-element scan, not lost
            // work, and it takes two moves between maps to reach. Removing a designation fires
            // Notify_Removing, which this mod prefixes to drop staged materials, and doing that from
            // inside another thing's SpawnSetup is more risk than the case is worth.
            Assert.That(
                ImproveDesignations.RepairNeeded(markedInComponent: false, designationOnThisMap: true),
                Is.EqualTo(ImproveDesignationRepair.None));
        }
    }
}
