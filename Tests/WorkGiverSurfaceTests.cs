using System.Linq;
using NUnit.Framework;
using RimWorld;
using SimpleImprove.Jobs;
using Verse;
using Verse.AI;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Pins the properties <c>WorkGiver_Improve</c> declares to the game, as opposed to what its
    /// methods do with them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This fixture exists because of one asymmetry. Re-adding a
    /// <c>PotentialWorkThingRequest</c> override would silently restore the thirty-region search this
    /// mod's most-reported defect was, and it would break nothing: every job would still be found,
    /// every test would still pass, and the only symptom would be a frame rate players describe
    /// thirteen months apart. A performance fix whose absence has no functional signature needs its
    /// declaration asserted, or nothing is holding it in place.
    /// </para>
    /// <para>
    /// A work giver's declared surface turns out to be reachable here even though none of its methods
    /// are: <c>new WorkGiver_Improve()</c> constructs, and reading a property that returns a struct
    /// touches no static game state. <c>ShouldSkip</c>, <c>PotentialWorkThingsGlobal</c>,
    /// <c>HasJobOnThing</c> and <c>JobOnThing</c> all need a spawned pawn on a map and remain out of
    /// reach; the decisions inside the last two are covered through <c>ImproveDesignations</c> and
    /// <c>WorkerSkill</c> instead.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class WorkGiverSurfaceTests
    {
        [Test]
        public void TheWorkGiverDeclaresNoThingRequestSoNoRegionSearchIsBuilt()
        {
            // GenClosest.ClosestThingReachable decides whether to run its breadth-first region walk
            // from the request alone: `!thingReq.IsUndefined && thingReq.CanBeFoundInRegion`. Leaving
            // the request undefined is the only way to skip that walk. Supplying
            // PotentialWorkThingsGlobal does NOT remove it; with a region-storable request the set
            // becomes a conditional fallback that runs after the walk, and when the walk exhausts the
            // reachable regions under budget it is not read at all.
            var request = new WorkGiver_Improve().PotentialWorkThingRequest;

            Assert.That(request.IsUndefined, Is.True,
                "WorkGiver_Improve declares a ThingRequest again. That reinstates the 30-region scan "
                + "over every artificial building on every pawn's job search, which is issue #3.");
            Assert.That(request.group, Is.EqualTo(ThingRequestGroup.Undefined));
            Assert.That(request.singleDef, Is.Null);
        }

        [Test]
        public void TheWorkGiverOverridesShouldSkip()
        {
            // The base returns false, so an inherited ShouldSkip is indistinguishable from a missing
            // one at every call site. It is checked in JobGiver_Work.PawnCanUseWorkGiver before the
            // scanner cast and before any search set is read, and it is the only exit that costs
            // nothing in the state a colony spends most of its time in.
            var declaring = typeof(WorkGiver_Improve)
                .GetMethod(nameof(WorkGiver_Improve.ShouldSkip), new[] { typeof(Pawn), typeof(bool) })
                .DeclaringType;

            Assert.That(declaring, Is.EqualTo(typeof(WorkGiver_Improve)),
                "ShouldSkip is inherited again, so the no-work case costs a full scan.");
        }

        [Test]
        public void TheWorkGiverOverridesPotentialWorkThingsGlobal()
        {
            // With the request undefined this is not a fallback, it is the entire search set. It is
            // also what FloatMenuOptionProvider_WorkGivers matches a right-clicked building against,
            // since an undefined request makes ThingRequest.Accepts false for everything. Inheriting
            // the base null here would leave the giver unable to find any work at all.
            var declaring = typeof(WorkGiver_Improve)
                .GetMethod(nameof(WorkGiver_Improve.PotentialWorkThingsGlobal), new[] { typeof(Pawn) })
                .DeclaringType;

            Assert.That(declaring, Is.EqualTo(typeof(WorkGiver_Improve)));
        }

        [Test]
        public void TheWorkGiverLeavesTheRegionBudgetAlone()
        {
            // Only meaningful alongside the undefined request: with no region walk to bound, a value
            // here would be dead weight that reads as if it were tuning something.
            Assert.That(new WorkGiver_Improve().MaxRegionsToScanBeforeGlobalSearch, Is.EqualTo(-1));
        }

        [Test]
        public void TheWorkGiverStillReachesItsTargetsByTouch()
        {
            // Unchanged by this fix and asserted so that it stays that way. Improvement happens at the
            // building, so anything else would have pawns working from across the room.
            Assert.That(new WorkGiver_Improve().PathEndMode, Is.EqualTo(PathEndMode.Touch));
        }

        [Test]
        public void TheWorkGiverDefPointsAtThisClass()
        {
            // The whole fixture is worthless if the shipped def names a different giverClass, which is
            // a rename away and would fail nothing else.
            var giverClass = System.Xml.Linq.XDocument
                .Load(System.IO.Path.Combine(RepoRoot(), "1.6/Defs/WorkGiverDefs/WorkGivers_Improve.xml"))
                .Root.Elements("WorkGiverDef")
                .Select(d => (string)d.Element("giverClass"))
                .ToList();

            Assert.That(giverClass, Does.Contain(typeof(WorkGiver_Improve).FullName));
        }

        private static string RepoRoot()
        {
            var directory = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null
                   && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "rimworld-simple-improve.sln")))
            {
                directory = directory.Parent;
            }

            Assert.That(directory, Is.Not.Null, "Could not find the repository root from the test directory.");
            return directory.FullName;
        }
    }
}
