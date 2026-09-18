using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SimpleImprove.Core;
using SimpleImprove.Jobs;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Pins that every place deciding whether to do or offer improvement work asks whether the mark
    /// still has work ahead of it, by reading the compiled IL of bodies this harness cannot run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ImproveTargetTests"/> runs the decision. None of its call sites can run here: the
    /// work giver and both job drivers need a spawned pawn on a map, the setter's marking branch
    /// needs a designation manager, and the gizmo and selection code read <c>Faction.OfPlayer</c>.
    /// Deleting any one of those calls would pass that fixture entire, which is what this one is for.
    /// </para>
    /// <para>
    /// What IL reading cannot see, and so what still belongs to an in-game check: inverting the
    /// <c>if</c> around a call, or discarding its result. <see cref="ILCalls"/> carries the rest of
    /// what it does and does not establish.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class StrandedMarkWiringTests
    {
        private const string Outstanding = "SimpleImprove.Core.SimpleImproveComp.get_HasOutstandingImprovement";
        private const string OutstandingFor = "SimpleImprove.Core.SimpleImproveComp.IsOutstandingFor";
        private const string BareFlag = "get_IsMarkedForImprovement";

        [Test]
        public void TheWorkGiverRefusesAStrandedMarkBeforeItBuildsAnyJob()
        {
            // The order is the load-bearing part. The haul branch comes first in BuildJob, so a
            // test that came after it would still send the colony to deliver the full cost to a
            // building that can never use it. IL order is not execution order, so this pins that
            // the test appears before the material search and before either job is made, which is
            // what moving it down below the haul branch would change.
            List<string> calls = CallNamesIn(typeof(WorkGiver_Improve), "BuildJob");

            int outstanding = calls.IndexOf(Outstanding);
            int search = calls.IndexOf("SimpleImprove.Jobs.WorkGiver_Improve.FindClosestMaterial");
            int firstJob = calls.IndexOf("Verse.JobMaker.MakeJob");

            Assert.That(outstanding, Is.GreaterThanOrEqualTo(0),
                "BuildJob no longer asks whether the mark has work ahead of it. Calls: " + string.Join(", ", calls));
            Assert.That(search, Is.GreaterThanOrEqualTo(0), "BuildJob no longer searches for material. Calls: " + string.Join(", ", calls));
            Assert.That(firstJob, Is.GreaterThanOrEqualTo(0), "BuildJob no longer makes a job. Calls: " + string.Join(", ", calls));
            Assert.That(outstanding, Is.LessThan(search));
            Assert.That(outstanding, Is.LessThan(firstJob));
        }

        [Test]
        public void EveryWorkDecisionAsksWhetherTheMarkIsOutstanding()
        {
            // The whole caller set, not a filtered one: the property is this fix's own and has
            // exactly these three readers. The two driver readers are lambdas inside MakeNewToils,
            // which the compiler hoists out of the method that appears to contain them: the job
            // driver's captures a local, so it lands in a nested display class, and the haul
            // driver's captures only `this`, so it becomes an instance method on the driver itself.
            // That difference is why they are matched on the generated name rather than on nesting.
            List<MethodBase> callers = Callers(typeof(SimpleImproveComp), "get_HasOutstandingImprovement");
            List<string> names = callers.Select(ILCalls.Describe).ToList();

            Assert.That(callers, Has.Count.EqualTo(3), "Callers: " + string.Join(", ", names));
            Assert.That(names, Does.Contain("SimpleImprove.Jobs.WorkGiver_Improve.BuildJob"));
            Assert.That(
                callers.Any(m => IsLambdaIn(m, typeof(JobDriver_Improve), "MakeNewToils")), Is.True,
                "JobDriver_Improve no longer stops on the outstanding mark. Callers: " + string.Join(", ", names));
            Assert.That(
                callers.Any(m => IsLambdaIn(m, typeof(JobDriver_HaulToImprove), "MakeNewToils")), Is.True,
                "JobDriver_HaulToImprove no longer fails on the outstanding mark. Callers: " + string.Join(", ", names));
        }

        [Test]
        public void NothingInTheJobsReadsTheBareFlagExceptTheDepositCheck()
        {
            // The negative half of the test above, over the same territory: SimpleImprove.Jobs,
            // nested lambda types included. Narrowed to that namespace on purpose and not for
            // convenience. Outside it the flag is bookkeeping (the setter, the designation repair,
            // the container's acceptance, the gizmo's grouping) and is read correctly; inside it
            // every read is a decision about doing work, and a bare read there is exactly this
            // defect. The one exception is the deposit toil's error check, which guards the
            // container, whose acceptance MaterialStorage keys on the flag. It cannot fire for a
            // stranded building, because the same driver's fail condition ends the job first.
            List<MethodBase> readers = Callers(typeof(SimpleImproveComp), BareFlag)
                .Where(m => m.DeclaringType.Namespace == "SimpleImprove.Jobs")
                .ToList();
            List<string> names = readers.Select(ILCalls.Describe).ToList();

            Assert.That(readers, Has.Count.EqualTo(1), "Readers in SimpleImprove.Jobs: " + string.Join(", ", names));
            Assert.That(IsLambdaIn(readers[0], typeof(JobDriver_HaulToImprove), "DepositHauledThingInContainer"), Is.True,
                "The one remaining bare read is not the deposit check: " + names[0]);
        }

        [Test]
        public void TheSetterJudgesAMarkBeforeItAddsTheDesignation()
        {
            // Every transition from unmarked to marked goes through this branch, whoever the caller
            // is, so this is what makes "nothing can mark a building with no work ahead of it" hold
            // for any third party writing the public property, not only for the group menu's
            // TryMarkFor. The designator and the single-building menu that used to be the other two
            // callers were deleted on 2026-09-18; this is what would hold a new one.
            List<string> calls = CallNamesIn(typeof(SimpleImproveComp), "set_IsMarkedForImprovement");

            int judge = calls.IndexOf(OutstandingFor);
            int add = calls.IndexOf("Verse.DesignationManager.AddDesignation");

            Assert.That(judge, Is.GreaterThanOrEqualTo(0),
                "The setter marks without asking whether the mark has work ahead of it. Calls: " + string.Join(", ", calls));
            Assert.That(add, Is.GreaterThanOrEqualTo(0), "The setter no longer adds a designation at all.");
            Assert.That(judge, Is.LessThan(add));
        }

        [Test]
        public void TheGroupMenuMarksOnlyThroughTryMarkFor()
        {
            // Both branches, the all-groups one and the single-group one. Writing the target and
            // the flag directly is what the "any improvement" option did with no quality test at
            // all, and it is how a building that reached Legendary while the menu was open got
            // marked again. The count is two because there are two loops; one of them reverting
            // is a real regression, not noise.
            List<string> calls = CallNamesIn(typeof(SimpleImproveComp), "ApplyQualityTargetToGroup");

            Assert.That(calls.Count(c => c == "SimpleImprove.Core.SimpleImproveComp.TryMarkFor"), Is.EqualTo(2),
                "Calls: " + string.Join(", ", calls));
            Assert.That(calls, Does.Not.Contain("SimpleImprove.Core.SimpleImproveComp.set_IsMarkedForImprovement"));
            Assert.That(calls, Does.Not.Contain("SimpleImprove.Core.SimpleImproveComp.set_TargetQuality"));
        }

        [Test]
        public void TheGizmoAndTheSelectionFilterAskCanBeOfferedOfTheBuildingsOwnQuality()
        {
            // The gizmo and the selection filter decide whether "Any improvement" is on screen, and
            // the marking path decides whether clicking it does anything. ImproveTargetTests holds
            // CanBeOffered equal to what the marking path asks, for every quality. This holds the two
            // call sites to asking it, and to asking it of the building's own quality.
            //
            // Both used to ask IsOutstanding themselves with a null target, and this test used to
            // claim they "cannot drift" because of that. They could: reading IL sees the call and not
            // its arguments, so either could have asked about Masterwork and passed; both
            // mutations were measured passing. CanBeOffered takes no target, so the only argument left is the
            // quality, and the call immediately before it has to be the read of that quality: a
            // constant in its place removes the read. What this still cannot see is the `if`
            // around the call being inverted, which would hide the Improve button from every
            // building below Legendary and so fails on sight in game.
            foreach (var site in new[]
                     {
                         (Type: typeof(SimpleImproveComp), Method: "CanBeOfferedImprovement"),
                         (Type: typeof(ImproveSelection), Method: "EligibleIn"),
                     })
            {
                List<string> calls = CallNamesIn(site.Type, site.Method);
                string all = site.Type.Name + "." + site.Method + " calls: " + string.Join(", ", calls);

                int offered = calls.IndexOf("SimpleImprove.Core.ImproveTarget.CanBeOffered");

                Assert.That(offered, Is.GreaterThan(0), all);
                Assert.That(calls.Count(c => c == "SimpleImprove.Core.ImproveTarget.CanBeOffered"), Is.EqualTo(1), all);
                Assert.That(calls[offered - 1], Is.EqualTo("RimWorld.CompQuality.get_Quality"), all);
                Assert.That(calls, Does.Not.Contain("SimpleImprove.Core.ImproveTarget.IsOutstanding"), all);
                Assert.That(calls, Does.Not.Contain(OutstandingFor), all);
            }
        }

        [Test]
        public void TheLoopsStopRuleIsTheSameFunctionTheWorkGiverUses()
        {
            // ImproveTargetTests holds the behaviour. This holds that it is the same function rather
            // than a second comparison that agrees for now, because a stop rule looser than the
            // giver's is how the loop would strand a building by itself.
            Assert.That(
                CallNamesIn(typeof(SimpleImproveComp), "ShouldContinueImproving"),
                Does.Contain("SimpleImprove.Core.ImproveTarget.IsOutstanding"));
        }

        /// <summary>
        /// Determines whether a method is a compiler-generated lambda body written inside a given
        /// method of a given type.
        /// </summary>
        /// <param name="method">The method to test.</param>
        /// <param name="owner">The type the lambda was written in.</param>
        /// <param name="sourceMethod">The method the lambda was written in.</param>
        /// <returns><c>true</c> when the compiler generated <paramref name="method"/> from such a lambda.</returns>
        /// <remarks>
        /// The compiler names a hoisted lambda <c>&lt;Source&gt;b__N_M</c>, where the numbers are
        /// ordinals that move whenever the file gains a method, so only the part in angle brackets
        /// is stable. The lambda sits either on the owner itself or on a display class nested in it,
        /// depending on what it captures.
        /// </remarks>
        private static bool IsLambdaIn(MethodBase method, Type owner, string sourceMethod)
        {
            bool ownedByType = method.DeclaringType == owner || method.DeclaringType.DeclaringType == owner;
            return ownedByType && method.Name.StartsWith("<" + sourceMethod + ">b__", StringComparison.Ordinal);
        }

        private static List<MethodBase> Callers(Type declaringType, string name)
        {
            return ILCalls.CalledAnywhereInTheMod(declaringType, name).ToList();
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
    }
}
