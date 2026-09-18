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
            // BuildJob is the expensive half and must be reached only through the memo, or the
            // double evaluation issue #5 filed comes straight back. The one reference to it is the
            // constructor handing it to the memo as its builder; a direct call from anywhere else,
            // HasJobOnThing, JobOnThing or JobFor included, is a bypass. Asked of the whole assembly
            // rather than of three named methods, so a call hidden in a lambda is found too, and
            // asserted as the exact caller list, which is its own positive control: a rename of
            // BuildJob empties the list and fails rather than passing vacuously.
            Assert.That(
                ILCalls.CalledAnywhereInTheMod(typeof(WorkGiver_Improve), "BuildJob").Select(ILCalls.Describe).ToList(),
                Is.EqualTo(new List<string> { "SimpleImprove.Jobs.WorkGiver_Improve..ctor" }),
                "BuildJob is reached from somewhere other than the memo's builder, so that caller "
                + "bypasses the memo and builds the job itself.");
        }

        [Test]
        public void TheJobDecisionGoesThroughTheMemoAndNothingElse()
        {
            // JobFor needs Find.TickManager and a spawned pawn, so it cannot run here. Everything it
            // used to decide itself, forgetting, taking, building and keeping, is JobMemo.Answer
            // now, which runs in JobMemoTests. What is left for this to hold is that JobFor reads
            // the tick and asks Answer, and asks nothing else. The whole sequence is asserted rather
            // than a whitelist, so a call arriving is seen as well as one leaving: a BuildJob, a
            // TryTake or a cache Clear written back in here would each be a second path around the
            // rules the memo keeps.
            //
            // Generic types are named by their definition rather than their closed form, because
            // the closed form spells out Assembly-CSharp's version and this would fail on every
            // RimWorld patch release.
            //
            // What it cannot see: which pawn and forced value are passed to Answer. Passing a
            // constant false would hand a right-click the job the background scan built, and build
            // every refusal without its reason. That is an argument, which reading IL does not reach,
            // and it is what in-game checks 5 and 14 would show, since every greyed reason is
            // written only when forced.
            List<string> calls = CallsIn(typeof(WorkGiver_Improve), "JobFor").Select(Generic).ToList();

            Assert.That(
                calls,
                Is.EqualTo(new List<string>
                {
                    "Verse.Find.get_TickManager",
                    "Verse.TickManager.get_TicksGame",
                    "SimpleImprove.Core.JobMemo`3.Answer",
                }));
        }

        [Test]
        public void TheMemoIsBuiltOverThisGiversJobBuilderAndItsCacheClear()
        {
            // The other half of JobFor's single call. The memo builds with whatever the constructor
            // hands it and forgets through whatever the constructor hands it, so a constructor that
            // bound a different builder, or an empty forget in place of the cache clear, would leave
            // JobFor's call list untouched and every memo test green. The constructor's whole call
            // sequence is asserted.
            //
            // The HashSet constructor is the unreachable-material cache's field initialiser, which
            // the compiler emits ahead of the base constructor call. The two ldftn entries are the
            // method groups turned into delegates: BuildJob for Func, and the cache's Clear for
            // Action, which is what makes the cache share the memo's key.
            ConstructorInfo constructor = typeof(WorkGiver_Improve).GetConstructor(Type.EmptyTypes);

            Assert.That(constructor, Is.Not.Null, "WorkGiver_Improve has no parameterless constructor.");

            Assert.That(
                ILCalls.CalledBy(constructor).Select(Generic).ToList(),
                Is.EqualTo(new List<string>
                {
                    "System.Collections.Generic.HashSet`1..ctor",
                    "RimWorld.WorkGiver_Scanner..ctor",
                    "SimpleImprove.Jobs.WorkGiver_Improve.BuildJob",
                    "System.Func`4..ctor",
                    "System.Collections.Generic.HashSet`1.Clear",
                    "System.Action..ctor",
                    "SimpleImprove.Core.JobMemo`3..ctor",
                }));
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
            // being the memo's forget callback. If that clear ever stops happening, one pawn's
            // unreachable materials are served to every other pawn for the rest of the session.
            // TheMemoIsBuiltOverThisGiversJobBuilderAndItsCacheClear pins the binding, and
            // JobMemoTests pins that the memo calls it on every key change and only then.
            Assert.That(
                ILCalls.CalledBy(typeof(WorkGiver_Improve).GetConstructor(Type.EmptyTypes)).Select(ILCalls.Describe)
                    .Any(call => call.Contains("HashSet") && call.EndsWith(".Clear")),
                Is.True,
                "The unreachable-material cache is no longer the memo's forget callback, so it "
                + "outlives the key it silently depends on.");
        }

        [Test]
        public void TheTargetReservationIsTestedOnceAndBeforeTheMaterialSearch()
        {
            // The reservation is on the BUILDING, so it does not change from one material to the
            // next. It used to be asked once per material inside the loop and again at the bottom of
            // the method to gate the improve job. That was repeated work and nothing worse: the
            // "MissingMaterials" it fell through to was written only on the unforced path, where
            // nothing reads a fail reason, and on the forced path the test ignores other pawns'
            // reservations and cannot refuse for one.
            //
            // IL order is not execution order, so this pins that the reservation call appears
            // before the material search rather than that it runs first, which is as much as
            // reading a body can say. What it does catch is the call being moved back inside the
            // loop, or a second copy reappearing.
            List<string> calls = CallNamesIn(typeof(WorkGiver_Improve), "BuildJob");

            int reserve = calls.FindIndex(call => call.EndsWith(".CanReserve"));
            int search = calls.IndexOf("SimpleImprove.Jobs.WorkGiver_Improve.FindClosestMaterial");

            Assert.That(reserve, Is.GreaterThanOrEqualTo(0),
                "BuildJob no longer reserves the building at all. Calls: " + string.Join(", ", calls));
            Assert.That(search, Is.GreaterThanOrEqualTo(0),
                "BuildJob no longer searches for material.");
            Assert.That(reserve, Is.LessThan(search),
                "The target reservation moved back below the material search, so every candidate "
                + "another pawn holds pays for a material search before it is refused.");

            Assert.That(
                calls.Count(call => call.EndsWith(".CanReserve")), Is.EqualTo(1),
                "The building is reserved-tested more than once in one decision.");
        }

        [Test]
        public void TheJobDecisionGivesOnlyTheseReasons()
        {
            // Every string BuildJob loads, in IL order, in full. A fail reason is a translated string,
            // so a new reason, or an old one put back, is a new literal here and fails this test.
            // The one this pins the absence of is SimpleImprove_TargetReserved: it was set after a
            // reservation test that, on the only path whose reasons anybody reads, ignores other
            // pawns' reservations, so it could not be true when shown. The reservation still refuses
            // the background scan; it sets no reason because nothing on that path would read one.
            //
            // "{0}" is the format string of the interpolation wrapped around the MissingMaterials
            // reason. It carries no meaning and would go if that wrapper were simplified; the list
            // then needs updating, which is the cost of asserting the whole sequence rather than
            // filtering it to keys and going blind to anything else.
            MethodInfo method = typeof(WorkGiver_Improve).GetMethod(
                "BuildJob", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            Assert.That(method, Is.Not.Null, "WorkGiver_Improve has no BuildJob to read.");

            Assert.That(
                ILCalls.Read(method).Strings,
                Is.EqualTo(new List<string>
                {
                    "{0}",
                    "MissingMaterials",
                    "NotAssignedToWorkType",
                    "SimpleImprove_NoConstructionSkill",
                    "SimpleImprove_SkillTooLowDespiteBonus",
                    "SimpleImprove_SkillTooLow"
                }));
        }

        private static List<string> CallNamesIn(Type declaringType, string methodName)
        {
            return CallsIn(declaringType, methodName).Select(ILCalls.Describe).ToList();
        }

        private static IList<MethodBase> CallsIn(Type declaringType, string methodName)
        {
            MethodInfo method = declaringType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly);

            Assert.That(method, Is.Not.Null, declaringType.FullName + " has no " + methodName + " to read.");

            return ILCalls.CalledBy(method);
        }

        /// <summary>
        /// Names a called method by its declaring type's generic definition, so that the name does
        /// not embed the version of whichever assembly supplied the type arguments.
        /// </summary>
        /// <param name="method">The method to name.</param>
        /// <returns>The declaring type's definition name and the method name.</returns>
        private static string Generic(MethodBase method)
        {
            Type type = method.DeclaringType;

            if (type != null && type.IsGenericType)
            {
                type = type.GetGenericTypeDefinition();
            }

            return type == null ? method.Name : type.FullName + "." + method.Name;
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
