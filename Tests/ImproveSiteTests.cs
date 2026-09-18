using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using RimWorld;
using SimpleImprove.Core;
using SimpleImprove.Jobs;
using Verse;
using Verse.AI;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers the replacement for <c>GenConstruct.CanConstruct</c>, which is issue #7.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Almost none of <see cref="ImproveSite.CanWorkOn"/> can be run here. Four of its six checks need
    /// a spawned <c>Thing</c> on a <c>Map</c>, and the fifth reads <c>Find.IdeoManager</c>. The one
    /// that can be run is the guard that has to come first, and running it is the whole point: it is
    /// reachable precisely because it short-circuits before anything that needs the game.
    /// </para>
    /// <para>
    /// The rest is held structurally, by reading the compiled IL. That is not a substitute for
    /// behaviour and is not claimed as one. What it holds is the thing this defect was: which vanilla
    /// methods the mod hands a completed building to. Deleting the call that caused two crash reports
    /// and putting five replacements in its place is a change whose regression is "somebody puts the
    /// old call back", and that is a question about the call list rather than about behaviour.
    /// <see cref="ILCalls"/> says what this technique does and does not establish.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class ImproveSiteTests
    {
        /// <summary>
        /// Every call <c>ImproveSite.CanWorkOn</c> makes into the game assembly, in IL order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the mod's entire contact surface with vanilla for this decision, which is the
        /// thing issue #7 was about. It is asserted whole rather than as a whitelist of the five
        /// interesting names: a whitelist pins that the named calls are present and in order and is
        /// blind to anything else arriving or leaving, including a sixth vanilla check quietly added
        /// or an argument conversion quietly dropped.
        /// </para>
        /// <para>
        /// Five checks, seventeen calls. The extras are what the five are made of.
        /// <c>LocalTargetInfo.op_Implicit</c> is the conversion that lets a <c>Thing</c> be passed
        /// where <c>CanReserveAndReach</c> declares a <c>LocalTargetInfo</c>, and it is the argument
        /// shape made visible. <c>MembersCanBuild</c> appears twice because vanilla asks it twice for
        /// two reasons: once of the worker's own ideoligion, to decide, and once per ideoligion on
        /// the map, to name the ones that would allow it. Losing the second is not a wrong answer, it
        /// is the player being told a building is forbidden without being told who could build it.
        /// The trailing five are the fail reason being built.
        /// </para>
        /// <para>
        /// Calls into mscorlib are filtered out rather than listed, and that is not tidiness. Three
        /// of them are members of generic types closed over <c>RimWorld.Ideo</c>, so their full names
        /// embed the game assembly's version and the list would need editing on every RimWorld patch.
        /// </para>
        /// </remarks>
        private static readonly string[] VanillaCallsInOrder =
        {
            "RimWorld.GenConstruct.FirstBlockingThing",
            "RimWorld.GenConstruct.CanTouchTargetFromValidCell",
            "Verse.LocalTargetInfo.op_Implicit",
            "Verse.DangerUtility.NormalMaxDanger",
            "Verse.AI.ReservationUtility.CanReserveAndReach",
            "RimWorld.FireUtility.IsBurning",
            "Verse.Pawn.get_Ideo",
            "Verse.Pawn.get_Ideo",
            "RimWorld.Ideo.MembersCanBuild",
            "Verse.Find.get_IdeoManager",
            "RimWorld.IdeoManager.get_IdeosListForReading",
            "RimWorld.Ideo.MembersCanBuild",
            "Verse.GenText.ToCommaList",
            "Verse.NamedArgument.op_Implicit",
            "Verse.TranslatorFormattedStringExtensions.Translate",
            "Verse.TaggedString.op_Implicit",
            "Verse.AI.JobFailReason.Is",
        };

        [Test]
        public void ABuildingWithNoBlueprintIsRefusedBeforeVanillaIsAsked()
        {
            // The guard is not tidiness. GenConstruct.FirstBlockingThing reaches BlocksConstruction
            // for every other thing sharing a cell, and that dereferences
            // BlueprintDefOf(constructible).entityDefToBuild, where BlueprintDefOf returns
            // def.blueprintDef for anything that is neither a blueprint nor a frame. So a marked
            // building whose def has no blueprint throws inside vanilla the moment a pawn stands on
            // it or an item is dropped on it.
            //
            // The null worker is the load-bearing part of this test rather than laziness. Every check
            // after the guard dereferences it, so returning false without throwing is what proves
            // nothing else ran. Moving the guard down, or deleting it, turns this into an NRE.
            //
            // So is the size. FormatterServices.GetUninitializedObject bypasses field initialisers,
            // so ThingDef.size arrives as (0, 0) rather than its declared IntVec2.One, and a thing
            // that occupies no cells walks out of FirstBlockingThing's loop before touching the null
            // Map. The first draft of this test did not set it, and the mutation that moves the guard
            // below FirstBlockingThing passed. One cell is what makes vanilla actually get asked.
            var def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
            def.size = IntVec2.One;

            var building = new Thing { def = def };

            // Assertions rather than Assume, which matters more than it looks. NUnit turns a failed
            // Assume into Inconclusive, the runner drops an inconclusive test from the totals, and
            // the run still prints "Passed!" with the count one lower. So a premise going stale
            // would delete the fixture's only executable test and say nothing. Measured: with these
            // as Assume, retiring the size premise turns a real failure into "Passed! 227".
            Assert.That(def.blueprintDef, Is.Null, "GetUninitializedObject should leave blueprintDef null.");
            Assert.That(building.OccupiedRect().Count(), Is.EqualTo(1), "The building must occupy a cell.");
            Assert.That(building.Map, Is.Null, "An unspawned thing has no map, which is what would throw.");

            Assert.That(ImproveSite.CanWorkOn(building, null, false), Is.False);
        }

        [Test]
        public void NothingInTheModHandsACompletedBuildingToCanConstructAnyMore()
        {
            // The defect itself, and the only assertion here that would have failed before the fix.
            // It is asked of the whole assembly rather than of the two methods that used to make the
            // call, because one of them made it from inside a FailOn lambda and the compiler hoists
            // that into a nested type where a search of the method that appears to make the call
            // finds nothing.
            IList<MethodBase> callers = ILCalls.CalledAnywhereInTheMod(typeof(GenConstruct), "CanConstruct");

            Assert.That(
                callers.Select(ILCalls.Describe).ToList(),
                Is.Empty,
                "GenConstruct.CanConstruct is being called again. Every vanilla caller passes a "
                + "Blueprint or a Frame; this mod would be passing a completed Building, which is the "
                + "argument shape two players reported as a crash. ImproveSite.CanWorkOn is the "
                + "replacement.");
        }

        [Test]
        public void TheScanThatSaysCanConstructIsGoneCanSeeAVanillaCallAtAll()
        {
            // The positive control, and without it the test above is worthless. A walker that quietly
            // returned nothing, a namespace filter that matched no types, or an assembly that failed
            // to load would all produce an empty caller list and a green test. So the same scan, over
            // the same assembly, looking for the same kind of target in the same class, has to find
            // something it is supposed to find.
            IList<MethodBase> blocking =
                ILCalls.CalledAnywhereInTheMod(typeof(GenConstruct), nameof(GenConstruct.FirstBlockingThing));

            Assert.That(
                blocking.Select(ILCalls.Describe).ToList(),
                Does.Contain("SimpleImprove.Core.ImproveSite.CanWorkOn"),
                "The IL scan cannot find a call this mod definitely makes, so its report that "
                + "CanConstruct is absent means nothing.");

            Assert.That(
                ILCalls.AllModMethods().Count(),
                Is.GreaterThan(100),
                "The scan is seeing far too few methods to be reading the whole mod.");

            // A scan that reaches nothing is caught by the two assertions above. A scan that reaches
            // most of the mod but not the part the defect lived in is not, and it is the likelier
            // mistake: the assertion above is anchored in SimpleImprove.Core while both former call
            // sites are in SimpleImprove.Jobs, one of them inside a hoisted lambda. Those are the two
            // pieces of territory the negative test is actually guarding, so the control has to prove
            // the scan covers them specifically.
            Assert.That(
                ILCalls.AllModMethods().Select(method => method.DeclaringType.Namespace).Distinct().ToList(),
                Does.Contain("SimpleImprove.Jobs"),
                "The scan is not reaching SimpleImprove.Jobs, where both former call sites live.");

            Assert.That(
                ILCalls.CalledAnywhereInTheMod(typeof(ImproveSite), nameof(ImproveSite.CanWorkOn))
                    .Any(method => method.DeclaringType.IsNested && method.Name.Contains("b__")),
                Is.True,
                "The scan is not reading hoisted lambda bodies, which is where the job driver's "
                + "half of the call lives.");
        }

        [Test]
        public void TheReplacementStillMakesEveryCheckVanillaWouldHave()
        {
            // Constraint 8 in the defect register, in the only form a test here can hold it: a
            // hand-rolled replacement has to reproduce all five checks, and dropping one is silent.
            // Losing FirstBlockingThing would let a pawn improve the chair somebody is sitting on,
            // which was confirmed in game as wanted; losing the Ideology check would quietly ignore a
            // restriction the player's ideoligion imposes.
            //
            // The order is asserted too, because it is player-visible. Only the Ideology branch sets
            // a JobFailReason, so a building that is both blocked and forbidden reports nothing today
            // and would report "only members of X can build" if the checks were reordered.
            Assembly game = typeof(Thing).Assembly;

            List<string> vanilla = CallsIn(typeof(ImproveSite), nameof(ImproveSite.CanWorkOn))
                .Where(called => called.DeclaringType.Assembly == game)
                .Select(ILCalls.Describe)
                .ToList();

            Assert.That(vanilla, Is.EqualTo(VanillaCallsInOrder.ToList()));
        }

        [Test]
        public void BothCallSitesGoThroughTheSameReplacement()
        {
            // The work giver decides whether to hand out the job and the job driver re-checks on
            // every tick of it, so the two have to agree or the driver fails a job the giver just
            // gave. They agreed before this change by both calling CanConstruct with checkSkills
            // false, and the driver's half is easy to miss: the call lives in a lambda, and issue #7
            // named only the work giver.
            List<string> callers = ILCalls
                .CalledAnywhereInTheMod(typeof(ImproveSite), nameof(ImproveSite.CanWorkOn))
                .Select(ILCalls.Describe)
                .ToList();

            Assert.That(callers, Has.Count.EqualTo(2), "Expected exactly the work giver and the job driver.");
            Assert.That(callers, Has.Some.StartsWith("SimpleImprove.Jobs.WorkGiver_Improve"));
            Assert.That(callers, Has.Some.StartsWith("SimpleImprove.Jobs.JobDriver_Improve"));
        }

        [Test]
        public void TheWorkGiverStillChecksTheSiteBeforeItChecksTheSkill()
        {
            // Both gates have to survive and the order matters for what the player is told: the site
            // checks set no fail reason at all, so running the skill gate first would replace silence
            // with "skill too low" for a building nobody can reach.
            List<string> calls = CallNamesIn(typeof(WorkGiver_Improve), nameof(WorkGiver_Improve.JobOnThing));

            int site = calls.IndexOf("SimpleImprove.Core.ImproveSite.CanWorkOn");
            int skill = calls.IndexOf("SimpleImprove.Core.WorkerSkill.Of");

            Assert.That(site, Is.GreaterThanOrEqualTo(0), "The work giver no longer checks the site at all.");
            Assert.That(skill, Is.GreaterThanOrEqualTo(0), "The work giver no longer reads the worker's skill.");
            Assert.That(site, Is.LessThan(skill));
        }

        [Test]
        public void EveryMethodInTheModCanBeWalkedToItsEnd()
        {
            // The check that stops all of the above passing for the wrong reason. A walker that
            // misreads an instruction length starts decoding operand bytes as opcodes, and the calls
            // it reports after that point are noise; the usual symptom is a short list, which reads
            // as "this method does not call that" and passes. Walking every method in the mod and
            // insisting each ends exactly on its last byte is what makes a desynchronised walk a
            // failure rather than a false negative. ILCalls throws rather than returning short.
            var failures = new List<string>();

            foreach (MethodBase method in ILCalls.AllModMethods())
            {
                try
                {
                    ILCalls.CalledBy(method);
                }
                catch (ILWalkException exception)
                {
                    failures.Add(ILCalls.Describe(method) + ": " + exception.Message);
                }
            }

            Assert.That(failures, Is.Empty);
        }

        [Test]
        public void TheWalkerHandlesTheOneInstructionThatIsNotAFixedLength()
        {
            // Every IL instruction has an operand length the runtime's own opcode table knows, except
            // switch, whose operand is a count followed by that many jump targets. Getting it wrong
            // advances the walk too little, and it then reads jump-target bytes as opcodes.
            //
            // Three of the mod's own compiled methods do contain a jump table, measured on
            // 2026-09-18: SimpleImproveSettings.GetPresetDisplayName, whose switch over the six
            // contiguous members of QualityStandardsPreset compiles to one, and the two MakeNewToils
            // iterator state machines. So a broken switch length is already caught incidentally by
            // the tests that walk the whole mod. This one exists to fail on it directly, with a
            // message about the switch, rather than as a desync report pointing at an unrelated
            // method, and to keep the branch exercised by code this fixture owns if those three ever
            // stop being jump tables. The work giver's switch over WorkerSkill.FirstBlocker is not
            // one of them: few enough cases and the compiler emits a comparison chain.
            WalkResult walk = ILCalls.Read(
                typeof(ImproveSiteTests).GetMethod(
                    nameof(DenseSwitch), BindingFlags.NonPublic | BindingFlags.Static));

            // Without this the test can quietly stop testing anything. Whether a switch statement
            // becomes a jump table is the compiler's choice, not the source's, so a change to the
            // case values or to a Roslyn heuristic would leave a passing test that exercises the
            // fixed-length path and never reaches the branch it is named after.
            Assert.That(
                walk.Opcodes, Does.Contain(ILCalls.Switch),
                "DenseSwitch no longer compiles to a jump table, so this test exercises nothing. "
                + "Make the case list dense again.");

            // The exact list rather than "contains", which costs nothing and pins an invented call.
            // It is not what catches a broken switch length, though, and an earlier version of this
            // comment claimed it was. Measured both ways: with the operand length wrongly fixed at
            // four, the walk reads the jump table as instructions, drifts back into alignment
            // because small jump offsets decode as short operand-free instructions, ends exactly on
            // the last byte and returns this same one-element list. Neither assertion form fails.
            // What fails is the branch-target check inside the walk, which throws before either
            // assertion is reached.
            Assert.That(
                walk.Calls.Select(ILCalls.Describe).ToList(),
                Is.EqualTo(new List<string> { "System.Math.Abs" }),
                "The walk desynchronised inside a switch: it read the jump table as instructions.");
        }

        [Test]
        public void AWalkThatLandsInTheMiddleOfAnInstructionIsAFailure()
        {
            // The branch-target check is the only thing that catches a misaligned walk once the walk
            // happens to end on the last byte, and every other test here depends on it while none of
            // them exercises it: deleting it on its own is silent across the whole suite. So it gets
            // hand-built bytes, which is the only way to produce a body that is deliberately wrong.
            // A real method cannot be used and neither can DynamicMethod, whose GetMethodBody throws.
            //
            //   0: br.s +2      two bytes, so the next instruction is at 2 and the target is 2 + 2
            //   2: ldc.i4 0     five bytes, occupying 2 to 6
            //   7: ret
            //
            // Offset 4 is inside the ldc.i4 operand. Nothing begins there, so the walk must refuse
            // it even though it decodes cleanly to the last byte.
            var landsMidOperand = new byte[] { 0x2B, 0x02, 0x20, 0x00, 0x00, 0x00, 0x00, 0x2A };
            MethodBase context = typeof(ImproveSiteTests).GetMethod(
                nameof(DenseSwitch), BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(
                () => ILCalls.Walk(landsMidOperand, context),
                Throws.TypeOf<ILWalkException>().With.Message.Contains("offset 4"));

            // The control, so the test above cannot pass because the walk refuses everything. The
            // same body with the branch aimed at the ret instead walks without complaint.
            var landsOnRet = new byte[] { 0x2B, 0x05, 0x20, 0x00, 0x00, 0x00, 0x00, 0x2A };

            Assert.That(() => ILCalls.Walk(landsOnRet, context), Throws.Nothing);
        }

        /// <summary>
        /// A method whose body contains a real IL <c>switch</c> instruction.
        /// </summary>
        /// <param name="value">The branch to take.</param>
        /// <returns>A number nobody reads.</returns>
        /// <remarks>
        /// Dense and wide enough that the compiler emits a jump table rather than a comparison chain.
        /// The <c>Math.Abs</c> call after it is the thing a desynchronised walk loses.
        /// </remarks>
        private static int DenseSwitch(int value)
        {
            int result;

            switch (value)
            {
                case 0: result = 10; break;
                case 1: result = 11; break;
                case 2: result = 12; break;
                case 3: result = 13; break;
                case 4: result = 14; break;
                case 5: result = 15; break;
                case 6: result = 16; break;
                case 7: result = 17; break;
                case 8: result = 18; break;
                case 9: result = 19; break;
                default: result = -1; break;
            }

            return Math.Abs(result);
        }

        private static List<string> CallNamesIn(Type declaringType, string methodName)
        {
            return CallsIn(declaringType, methodName).Select(ILCalls.Describe).ToList();
        }

        private static List<MethodBase> CallsIn(Type declaringType, string methodName)
        {
            MethodInfo method = declaringType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly);

            Assert.That(method, Is.Not.Null, declaringType.FullName + " has no " + methodName + " to read.");

            return ILCalls.CalledBy(method).ToList();
        }
    }
}
