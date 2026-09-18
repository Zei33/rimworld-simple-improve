using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        [Test]
        public void BothScanEntryPointsAnswerFromTheSameDecision()
        {
            // The invariant JobGiver_Work relies on is HasJobOnThing(p, t, false) ==
            // (JobOnThing(p, t, false) != null). Breaking it does not merely waste a call. The scan
            // picks a winner with HasJobOnThing and then calls JobOnThing on it with no re-check in
            // between; when they disagree, JobGiver_Work leaves bestTargetOfLastPriority set, breaks
            // at the next priorityInType boundary and returns NoJob, and workGiversInOrderNormal is
            // one flat list across every work type, so the pawn stops looking for ANY work for as
            // long as one marked building cannot be worked.
            //
            // Neither method can be run here, since both need a spawned pawn on a map. What can be
            // read is that both route through one private decision, which is the invariant expressed
            // as structure: two methods that return the same computation cannot disagree about it.
            // This is a guard against a future "optimisation" that gives HasJobOnThing its own
            // cheaper predicate, which is exactly what issue #5 originally proposed.
            //
            // The runtime diagnostic for that regression is unusable, which is why this test carries
            // the weight. JobGiver_Work reports a desync with Log.ErrorOnce on the literal key
            // 6112651, shared by every work giver in the game, and Log.Clear does not reset usedKeys.
            // Whichever mod desyncs first in a session consumes the key and every later desync,
            // including this mod's, is silent for the rest of the process.
            Assert.That(
                CallNamesIn(typeof(WorkGiver_Improve), "HasJobOnThing"),
                Does.Contain("SimpleImprove.Jobs.WorkGiver_Improve.JobFor"),
                "HasJobOnThing no longer answers from JobFor, so it can now disagree with JobOnThing.");

            Assert.That(
                CallNamesIn(typeof(WorkGiver_Improve), "JobOnThing"),
                Does.Contain("SimpleImprove.Jobs.WorkGiver_Improve.JobFor"),
                "JobOnThing no longer answers from JobFor, so it can now disagree with HasJobOnThing.");
        }

        [Test]
        public void OnlyTheMemoReachesTheJobDecisionDirectly()
        {
            // BuildJob is the expensive half and must be reached only through JobFor, or the memo is
            // bypassed and the double evaluation issue #5 filed comes straight back. Asserting the
            // negative on both entry points needs a positive control, or a rename of BuildJob would
            // make all three assertions vacuously true at once.
            Assert.That(
                CallNamesIn(typeof(WorkGiver_Improve), "JobFor"),
                Does.Contain("SimpleImprove.Jobs.WorkGiver_Improve.BuildJob"),
                "JobFor does not reach BuildJob, so the two assertions below are testing nothing.");

            Assert.That(
                CallNamesIn(typeof(WorkGiver_Improve), "HasJobOnThing"),
                Does.Not.Contain("SimpleImprove.Jobs.WorkGiver_Improve.BuildJob"),
                "HasJobOnThing bypasses the memo and builds the job itself.");

            Assert.That(
                CallNamesIn(typeof(WorkGiver_Improve), "JobOnThing"),
                Does.Not.Contain("SimpleImprove.Jobs.WorkGiver_Improve.BuildJob"),
                "JobOnThing bypasses the memo and builds the job itself.");
        }

        [Test]
        public void TheMemoIsKeyedOnThePawnTheForcedFlagAndTheTick()
        {
            // WorkGiverDef.Worker constructs one WorkGiver per def and caches it in an [Unsaved]
            // field, so this object is shared by every pawn in the game and the memo fields are
            // effectively process-wide. Dropping any part of the key would serve one pawn's decision
            // to another, or last tick's decision to this tick.
            //
            // Reading the field set rather than the behaviour is weak, and it is what is available:
            // JobFor needs Find.TickManager, which does not exist in this harness. It still catches
            // the specific regression of someone simplifying the key away.
            var fields = typeof(WorkGiver_Improve)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(f => f.Name)
                .ToList();

            Assert.That(fields, Does.Contain("memoPawn"), "The memo no longer keys on the pawn.");
            Assert.That(fields, Does.Contain("memoForced"), "The memo no longer keys on the forced flag.");
            Assert.That(fields, Does.Contain("memoTick"), "The memo no longer keys on the tick.");

            Assert.That(
                CallNamesIn(typeof(WorkGiver_Improve), "JobFor"),
                Does.Contain("Verse.TickManager.get_TicksGame"),
                "JobFor no longer reads the tick, so the memo can outlive the state it was built on.");
        }

        [Test]
        public void AMemoHitRemovesTheEntryItServes()
        {
            // JobMaker hands out pooled Job objects from SimplePool<Job> and Pawn_JobTracker returns
            // them to that pool when it declines or finishes one. The only job that leaves this class
            // is the one a memo hit returns, so it has to stop being reachable from the memo at that
            // moment; otherwise a later hit in the same tick could serve a Job the pool has already
            // handed to somebody else. Entries for candidates that never won are never given to
            // anyone and so are never pooled.
            // ILCalls.Describe spells a constructed generic with its full type arguments, so the
            // name here is "System.Collections.Generic.Dictionary`2[[Verse.Thing, ...],[Verse.AI.Job,
            // ...]].Remove" including assembly versions. Matching the whole string would make this
            // test fail on a RimWorld patch release, so it matches the two parts that carry meaning.
            List<string> calls = CallNamesIn(typeof(WorkGiver_Improve), "JobFor");

            Assert.That(
                calls.Any(call => call.Contains("Dictionary") && call.EndsWith(".Remove")),
                Is.True,
                "JobFor no longer removes the entry it serves, so a served Job stays reachable from "
                + "the memo after Pawn_JobTracker may have returned it to SimplePool<Job>. Calls "
                + "found: " + string.Join(", ", calls));
        }

        [Test]
        public void TheMaterialSearchAsksThePawnForItsNormalDangerThreshold()
        {
            // The old call was the bare TraverseParms.For(pawn), whose maxDanger default is
            // Danger.Deadly. That is the LOOSEST setting, not an unset one, so the giver accepted
            // material only a deadly route reached while using the normal threshold for the building
            // itself. Reading the IL cannot see arguments, so it cannot check that the ternary is the
            // right way round; what it can see is that NormalMaxDanger is reached at all, which is
            // true only of the two-argument form. Reverting to the bare overload makes that call
            // vanish and fails here.
            Assert.That(
                CallNamesIn(typeof(WorkGiver_Improve), "FindClosestMaterial"),
                Does.Contain("Verse.DangerUtility.NormalMaxDanger"),
                "FindClosestMaterial no longer asks the pawn for its normal danger threshold, so it "
                + "is back to accepting material across a deadly-danger route.");
        }

        [Test]
        public void TheMaterialSearchIsBoundedByAPerTickCache()
        {
            // Without this, a build with nothing reachable runs an unbounded ClosestThingReachable
            // for every outstanding material for every marked building, on every pawn's job search.
            // That is the multiplier issue #5 and #6 both point at, and it is the no-work case that
            // costs the most, exactly as it was for the region scan in #3.
            List<string> search = CallNamesIn(typeof(WorkGiver_Improve), "FindClosestMaterial");

            Assert.That(
                search.Any(call => call.Contains("HashSet") && call.EndsWith(".Contains")),
                Is.True,
                "FindClosestMaterial no longer consults the unreachable-material cache before "
                + "searching. Calls found: " + string.Join(", ", search));

            Assert.That(
                search.Any(call => call.Contains("HashSet") && call.EndsWith(".Add")),
                Is.True,
                "FindClosestMaterial no longer records a failed search, so the cache can never hit.");

            // The cache carries no pawn, forced flag or tick of its own; it borrows the memo's key by
            // being cleared with it. If that clear ever stops happening, one pawn's unreachable
            // materials are served to every other pawn for the rest of the session.
            Assert.That(
                CallNamesIn(typeof(WorkGiver_Improve), "JobFor")
                    .Any(call => call.Contains("HashSet") && call.EndsWith(".Clear")),
                Is.True,
                "JobFor no longer clears the unreachable-material cache, so it outlives the key it "
                + "silently depends on.");
        }

        private static List<string> CallNamesIn(Type declaringType, string methodName)
        {
            MethodInfo method = declaringType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly);

            Assert.That(method, Is.Not.Null, declaringType.FullName + " has no " + methodName + " to read.");

            return ILCalls.CalledBy(method).Select(ILCalls.Describe).ToList();
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
