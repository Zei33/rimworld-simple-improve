using System.Collections.Generic;
using NUnit.Framework;
using SimpleImprove.Core;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers the memo that lets <c>WorkGiver_Improve.HasJobOnThing</c> and <c>JobOnThing</c> answer
    /// from one computation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The work giver's own <c>JobFor</c> cannot run here: it reads <c>Find.TickManager</c> and builds
    /// jobs for a spawned pawn. Everything it decides about what to hold, what to hand out, when to
    /// build and when to forget has been moved into <see cref="JobMemo{TAsker, TSubject, TAnswer}"/>,
    /// which takes the tick as an argument and is handed the builder, and that is what runs here.
    /// <c>WorkGiverSurfaceTests</c> holds <c>JobFor</c> to calling <c>Answer</c> and nothing else,
    /// and the constructor to handing it <c>BuildJob</c> and the cache clear, by reading the IL.
    /// </para>
    /// <para>
    /// The memo is generic so that it can be held over plain objects. The game uses it over
    /// <c>Pawn</c>, <c>Thing</c> and <c>Job</c> and the bodies are the same bodies. What differs is
    /// how subjects compare: <c>Thing</c> implements <c>IEquatable&lt;Thing&gt;</c> on its ID and def,
    /// and a test object compares by reference. Nothing here depends on that difference, since every
    /// test uses one object per building.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class JobMemoTests
    {
        private readonly object pawn = new object();
        private readonly object otherPawn = new object();
        private readonly object chair = new object();
        private readonly object table = new object();
        private readonly object job = new object();
        private readonly object otherJob = new object();

        [Test]
        public void ARefusalIsNeverHeld()
        {
            // The regression this fixes, which never shipped. The float menu clears JobFailReason,
            // asks HasJobOnThing, and asks JobOnThing only if that was true, so a held refusal was
            // never collected. The next right-click on the same building in the same paused tick
            // was handed the null back without BuildJob running, so no reason was written, and the
            // provider drops a null job with no reason as nothing to say. A greyed "Cannot improve"
            // line vanished.
            var memo = Bare();
            memo.Rekey(pawn, forced: true, tick: 100);

            memo.Keep(chair, null);

            Assert.That(
                memo.TryTake(chair, out object served), Is.False,
                "A refusal was held, so the next ask in the same tick is answered without BuildJob "
                + "running, and a forced ask loses its fail reason.");
            Assert.That(served, Is.Null);

            // The control. Without it the assertion above passes just as well against a memo that
            // holds nothing at all: the same building under the same key must be held when the
            // decision is a job.
            memo.Keep(chair, job);

            Assert.That(memo.TryTake(chair, out served), Is.True, "A job was not held.");
            Assert.That(served, Is.SameAs(job));
        }

        [Test]
        public void ARefusalReplacesAJobHeldForTheSameBuilding()
        {
            // Unreachable from the work giver today, which only keeps after a miss. It is pinned
            // because the latest decision is the one that stands: a job left behind by an older
            // decision would be handed out as though it were current.
            var memo = Bare();
            memo.Rekey(pawn, forced: false, tick: 100);
            memo.Keep(chair, job);

            memo.Keep(chair, null);

            Assert.That(memo.TryTake(chair, out _), Is.False, "A refusal left an older job behind.");
        }

        [Test]
        public void ARefusalForOneBuildingLeavesTheOthersHeld()
        {
            // The background scan validates a closer candidate after the eventual winner: GenClosest
            // walks the set in order and only asks the validator about a thing nearer than the best
            // so far. A refusal for that closer building must not drop the winner's job, or
            // JobOnThing builds it again, which is issue #5's double evaluation back. Clearing the
            // whole memo on a null passed every other test here.
            var memo = Bare();
            memo.Rekey(pawn, forced: false, tick: 100);
            memo.Keep(chair, job);

            memo.Keep(table, null);

            Assert.That(memo.TryTake(chair, out object served), Is.True,
                "A refusal for the table dropped the job held for the chair.");
            Assert.That(served, Is.SameAs(job));
        }

        [Test]
        public void AJobIsHandedOutOnceAndThenForgotten()
        {
            // JobMaker hands out pooled Job objects from SimplePool<Job>, and Pawn_JobTracker returns
            // them to that pool when it declines or finishes one. A job still reachable from the memo
            // after it was handed out could be served again after the pool had given the same object
            // to somebody else.
            var memo = Bare();
            memo.Rekey(pawn, forced: false, tick: 100);
            memo.Keep(chair, job);
            memo.Keep(table, otherJob);

            Assert.That(memo.TryTake(chair, out object first), Is.True);
            Assert.That(first, Is.SameAs(job));

            Assert.That(
                memo.TryTake(chair, out _), Is.False,
                "A job that was handed out is still held, so it can be handed out twice.");

            // Taking one building's job leaves the other candidates alone. JobGiver_Work validates
            // every candidate before it asks JobOnThing about the winner, so the winner's job has to
            // survive the others being taken. ARefusalForOneBuildingLeavesTheOthersHeld is the
            // other half, a refusal.
            Assert.That(memo.TryTake(table, out object second), Is.True);
            Assert.That(second, Is.SameAs(otherJob));
        }

        [TestCase("pawn")]
        [TestCase("forced")]
        [TestCase("tick")]
        public void ChangingAnyPartOfTheKeyForgetsEverything(string part)
        {
            // The object that owns this memo is shared by every pawn in the game, since
            // WorkGiverDef.Worker makes one per def. Dropping the pawn from the key serves one
            // pawn's job to another; dropping forced serves a background decision to a right-click,
            // built under the wrong danger threshold and reservation rule; dropping the tick serves
            // last tick's decision after the colony has moved on.
            var memo = Bare();
            memo.Rekey(pawn, forced: false, tick: 100);
            memo.Keep(chair, job);

            bool forgot = memo.Rekey(
                part == "pawn" ? otherPawn : pawn,
                part == "forced",
                part == "tick" ? 101 : 100);

            // The return value is what the work giver clears its unreachable-material cache on, so
            // it is asserted rather than assumed: that cache has no key of its own.
            Assert.That(forgot, Is.True, "Rekey did not report a change of " + part + ".");
            Assert.That(
                memo.TryTake(chair, out _), Is.False,
                "A job survived a change of " + part + ".");
        }

        [Test]
        public void TheSameKeyForgetsNothing()
        {
            // The control for the test above, which a memo that forgot on every call would pass.
            // Forgetting on every call is also the double evaluation issue #5 filed, back again:
            // HasJobOnThing's job would be gone before JobOnThing asked for it.
            var memo = Bare();
            memo.Rekey(pawn, forced: true, tick: 100);
            memo.Keep(chair, job);

            bool forgot = memo.Rekey(pawn, forced: true, tick: 100);

            Assert.That(forgot, Is.False, "Rekey reported a change when nothing changed.");
            Assert.That(memo.TryTake(chair, out object served), Is.True, "The same key lost the job.");
            Assert.That(served, Is.SameAs(job));
        }

        [Test]
        public void HasJobOnThingBuildsAndJobOnThingIsHandedTheSameJob()
        {
            // Issue #5 in the form the game produces it: the validator asks, the winner is asked
            // again straight away, and the second ask must be served what the first built rather
            // than build it again. Two one-word edits in the old JobFor passed the whole suite and
            // brought the double build back: keeping a null in place of the job just built, and
            // ignoring what TryTake handed back. Both would fail here.
            var builds = new List<object>();
            var memo = Recording(builds, _ => job);

            object validated = memo.Answer(pawn, false, 100, chair);
            object handedOut = memo.Answer(pawn, false, 100, chair);

            Assert.That(validated, Is.SameAs(job));
            Assert.That(handedOut, Is.SameAs(job), "JobOnThing was not handed the job HasJobOnThing built.");
            Assert.That(builds, Is.EqualTo(new List<object> { chair }), "The winner was built twice.");

            // A third ask comes after the hand-over, so it must build again rather than serve a job
            // the pool may already have given to somebody else.
            memo.Answer(pawn, false, 100, chair);

            Assert.That(builds, Is.EqualTo(new List<object> { chair, chair }),
                "A job was served again after it had been handed out.");
        }

        [Test]
        public void ARefusalIsBuiltAgainOnEveryAsk()
        {
            // The float menu clears JobFailReason and asks HasJobOnThing on every right-click, and a
            // refusal's reason is written only while it is being built. So every ask that ends in a
            // refusal has to build, or the second right-click in a paused tick loses its line.
            var builds = new List<object>();
            var memo = Recording(builds, _ => null);

            Assert.That(memo.Answer(pawn, true, 100, chair), Is.Null);
            Assert.That(memo.Answer(pawn, true, 100, chair), Is.Null);

            Assert.That(builds, Is.EqualTo(new List<object> { chair, chair }),
                "A refusal was served from the memo, so its reason was not written the second time.");
        }

        [Test]
        public void TheBuilderIsHandedTheKeysPawnAndForcedValue()
        {
            // The builder is given the memo's own key rather than arguments passed beside it, so a
            // decision cannot be built for one pawn or forced value and then held under another.
            object askedBy = null;
            bool? askedForced = null;
            var memo = new JobMemo<object, object, object>(
                (asker, subject, forced) =>
                {
                    askedBy = asker;
                    askedForced = forced;
                    return job;
                },
                () => { });

            memo.Answer(otherPawn, true, 100, chair);

            Assert.That(askedBy, Is.SameAs(otherPawn));
            Assert.That(askedForced, Is.True);
        }

        [Test]
        public void TheOwnersStateIsForgottenOnEveryKeyChangeAndOnlyThen()
        {
            // The work giver's unreachable-material cache has no key of its own and borrows this
            // one by being cleared through the forget callback. Clearing it on every ask rather than
            // on a key change is issue #6's multiplier back and invisible to behaviour; it used to
            // be a surviving mutation in JobFor. Never clearing it serves one pawn's unreachable
            // materials to every other pawn.
            var events = new List<string>();
            var memo = new JobMemo<object, object, object>(
                (asker, subject, forced) =>
                {
                    events.Add("build");
                    return null;
                },
                () => events.Add("forget"));

            memo.Answer(pawn, false, 100, chair);
            memo.Answer(pawn, false, 100, table);
            memo.Answer(pawn, false, 101, chair);
            memo.Answer(otherPawn, false, 101, chair);
            memo.Answer(otherPawn, true, 101, chair);

            // The first ask is a key change too, from no key at all, and the forget comes before the
            // build under the new key, never after it: a cache cleared after the build would drop
            // what the build had just learned and keep what the previous pawn had.
            Assert.That(events, Is.EqualTo(new List<string>
            {
                "forget", "build",
                "build",
                "forget", "build",
                "forget", "build",
                "forget", "build",
            }));
        }

        /// <summary>
        /// Makes a memo whose builder must never be called, for tests of the three primitives.
        /// </summary>
        /// <returns>The memo.</returns>
        private static JobMemo<object, object, object> Bare()
        {
            return new JobMemo<object, object, object>(
                (asker, subject, forced) => throw new AssertionException("The primitives never build."),
                () => { });
        }

        /// <summary>
        /// Makes a memo that records every subject it builds for.
        /// </summary>
        /// <param name="builds">The list each built subject is appended to.</param>
        /// <param name="answer">What a build returns for a subject.</param>
        /// <returns>The memo.</returns>
        private static JobMemo<object, object, object> Recording(
            List<object> builds, System.Func<object, object> answer)
        {
            return new JobMemo<object, object, object>(
                (asker, subject, forced) =>
                {
                    builds.Add(subject);
                    return answer(subject);
                },
                () => { });
        }
    }
}
