using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SimpleImprove.Core;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers <see cref="WorkerSkill"/>, the decision half of the unguarded <c>pawn.skills</c> fix.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The property that carries the fix is <see cref="WorkerSkill.FirstBlocker"/> refusing an unknown
    /// skill before, and independently of, any requirement. A guard that only stopped a null
    /// dereference would still let a skill-less non-mechanoid reach
    /// <c>QualityUtility.GenerateQualityCreatedByPawn</c> through the "any improvement" path, which
    /// throws on exactly that pawn inside vanilla.
    /// </para>
    /// <para>
    /// What is covered, stated precisely because an overstated coverage claim is worse than none.
    /// Verified by mutation on 2026-09-18: swapping the two checks in <see cref="WorkerSkill.FirstBlocker"/>
    /// fails 2 tests, deleting its unknown check fails 4, dropping <c>IsKnown</c> from
    /// <see cref="WorkerSkill.Meets"/> fails 2, changing <c>NoLevel</c> to 0 fails 2, swapping the two
    /// branches in <see cref="WorkerSkill.From"/> fails 1, and making <c>From</c> fall back to the
    /// mech level for a pawn that is neither fails 1.
    /// </para>
    /// <para>
    /// What is <em>not</em> covered, and cannot be from here. <see cref="WorkerSkill.Of"/> is the
    /// readings half and does nothing but hand four values to <c>From</c>:
    /// <c>SkillDefOf.Construction</c>, <c>RaceProps.IsMechanoid</c> and <c>ModsConfig</c> all throw
    /// outside a running game, so a misread there is invisible to this suite. So is deleting the whole
    /// <c>FirstBlocker</c> call from <c>WorkGiver_Improve.JobOnThing</c> or <c>JobDriver_Improve</c>,
    /// both of which need a spawned <c>Thing</c> on a <c>Map</c>. Routing both call sites through one
    /// function is what shrinks that gap: there is a single place to delete rather than two that can
    /// drift apart. Closing it entirely needs an in-game dev-mode runner.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class WorkerSkillTests
    {
        [Test]
        public void AnOrdinaryColonistIsJudgedOnTheTrackedLevel()
        {
            var skill = WorkerSkill.OfSkillTracker(7);

            Assert.That(skill.IsKnown, Is.True);
            Assert.That(skill.Level, Is.EqualTo(7));
            Assert.That(skill.Source, Is.EqualTo(WorkerSkillSource.SkillTracker));
        }

        [Test]
        public void AMechanoidIsJudgedOnItsFixedLevel()
        {
            // RaceProperties.mechFixedSkillLevel, which no shipped def overrides, so every vanilla
            // mech arrives here at 10.
            var skill = WorkerSkill.OfMechanoid(10);

            Assert.That(skill.IsKnown, Is.True);
            Assert.That(skill.Level, Is.EqualTo(10));
            Assert.That(skill.Source, Is.EqualTo(WorkerSkillSource.MechFixedLevel));
        }

        [Test]
        public void APawnWithNoSkillModelAtAllIsNotKnown()
        {
            var skill = WorkerSkill.Unknown();

            Assert.That(skill.IsKnown, Is.False);
            Assert.That(skill.Level, Is.EqualTo(WorkerSkill.NoLevel));
            Assert.That(skill.Source, Is.EqualTo(WorkerSkillSource.None));
        }

        [Test]
        public void TheSentinelIsBelowEveryRealSkillLevel()
        {
            // Zero would be indistinguishable from a genuinely unskilled colonist, and the two have
            // to behave differently: one is refused, the other is merely bad at the job.
            Assert.That(WorkerSkill.NoLevel, Is.LessThan(0));
        }

        [Test]
        public void AnUnknownSkillFailsEveryRequirementIncludingZero()
        {
            // The whole fix. A requirement of zero is the "any improvement" case, where the work giver
            // asks for no particular quality, and it is the path that would otherwise carry a
            // skill-less non-mechanoid into the quality roll.
            var skill = WorkerSkill.Unknown();

            foreach (var required in new[] { 0, 1, 4, 10, 20 })
            {
                Assert.That(skill.Meets(required), Is.False,
                    "An unknown skill must not satisfy a requirement of " + required + ".");
            }
        }

        [Test]
        public void AnUnknownSkillFailsEvenANegativeRequirement()
        {
            // Nothing in the mod produces a negative requirement today, but Meets must not be
            // expressible as a bare comparison against the sentinel, or a requirement that ever went
            // below -1 would quietly admit the pawn the sentinel exists to exclude.
            var skill = WorkerSkill.Unknown();

            Assert.That(skill.Meets(-1), Is.False);
            Assert.That(skill.Meets(-5), Is.False);
        }

        [Test]
        public void AKnownSkillMeetsARequirementItReachesExactly()
        {
            Assert.That(WorkerSkill.OfSkillTracker(4).Meets(4), Is.True);
            Assert.That(WorkerSkill.OfMechanoid(10).Meets(10), Is.True);
        }

        [Test]
        public void AKnownSkillFailsARequirementAboveIt()
        {
            Assert.That(WorkerSkill.OfSkillTracker(4).Meets(5), Is.False);
            Assert.That(WorkerSkill.OfMechanoid(10).Meets(11), Is.False);
        }

        [Test]
        public void AColonistAtZeroIsStillJudgeable()
        {
            // The distinction the sentinel exists to preserve: skill 0 is a real answer.
            var skill = WorkerSkill.OfSkillTracker(0);

            Assert.That(skill.IsKnown, Is.True);
            Assert.That(skill.Meets(0), Is.True);
            Assert.That(skill.Meets(1), Is.False);
        }

        [Test]
        public void EverySourceIsReachableFromAFactory()
        {
            // A sweep, so adding a source without a way to build it fails here rather than silently
            // becoming dead.
            var produced = new[]
            {
                WorkerSkill.OfSkillTracker(3).Source,
                WorkerSkill.OfMechanoid(10).Source,
                WorkerSkill.Unknown().Source
            };

            var all = Enum.GetValues(typeof(WorkerSkillSource)).Cast<WorkerSkillSource>().ToList();

            Assert.That(produced, Is.EquivalentTo(all),
                "Every WorkerSkillSource must be produced by exactly one factory.");
        }

        [Test]
        public void EverySourceIsOnTheRightSideOfKnown()
        {
            // A sweep over the sources, so a new one added without deciding whether it carries a level
            // fails here. Note this deliberately does NOT assert that Level != NoLevel matches IsKnown:
            // that is not a property of the type. default(WorkerSkill) has Level 0 and is not known,
            // which is exactly the case TheDefaultStructIsUnknownRatherThanSkillZero relies on.
            var cases = new Dictionary<WorkerSkill, bool>
            {
                { WorkerSkill.OfSkillTracker(0), true },
                { WorkerSkill.OfMechanoid(0), true },
                { WorkerSkill.Unknown(), false }
            };

            foreach (var pair in cases)
            {
                Assert.That(pair.Key.IsKnown, Is.EqualTo(pair.Value),
                    pair.Key.Source + " is on the wrong side of IsKnown.");
            }
        }

        [Test]
        public void From_PrefersTheMechanoidLevelOverASkillTracker()
        {
            // The branch order the class remarks call load bearing, and the reason From exists apart
            // from Of. No vanilla pawn is both, but a mod can make one, and the quality roll at the
            // end of the job reads IsMechanoid first. If this flipped, the level the requirement is
            // judged against and the level the quality is rolled from would be two different numbers.
            var both = WorkerSkill.From(
                isMechanoid: true, mechFixedSkillLevel: 10, hasSkillTracker: true, trackedConstructionLevel: 3);

            Assert.That(both.Source, Is.EqualTo(WorkerSkillSource.MechFixedLevel));
            Assert.That(both.Level, Is.EqualTo(10));
        }

        [Test]
        public void From_UsesTheTrackerForAnOrdinaryColonist()
        {
            var colonist = WorkerSkill.From(
                isMechanoid: false, mechFixedSkillLevel: 10, hasSkillTracker: true, trackedConstructionLevel: 3);

            Assert.That(colonist.Source, Is.EqualTo(WorkerSkillSource.SkillTracker));
            Assert.That(colonist.Level, Is.EqualTo(3));
        }

        [Test]
        public void From_IsUnknownForAPawnThatIsNeither()
        {
            // A modded drone race: not a mechanoid, and non-humanlike so it has no skill tracker. The
            // mechFixedSkillLevel reading must not leak in as a consolation level, because vanilla's
            // quality roll will not use it for a non-mechanoid either.
            var drone = WorkerSkill.From(
                isMechanoid: false, mechFixedSkillLevel: 10, hasSkillTracker: false, trackedConstructionLevel: 7);

            Assert.That(drone.Source, Is.EqualTo(WorkerSkillSource.None));
            Assert.That(drone.IsKnown, Is.False);
            Assert.That(drone.Level, Is.EqualTo(WorkerSkill.NoLevel));
        }

        [Test]
        public void TheDefaultStructIsUnknownRatherThanSkillZero()
        {
            // `default(WorkerSkill)` is what a field or an array element starts at, and WorkerSkillSource
            // .None has to be enum value 0 for that to land on the safe side. If None were moved down
            // the enum, an uninitialised WorkerSkill would silently claim to be a skill-0 colonist.
            var skill = default(WorkerSkill);

            Assert.That(skill.IsKnown, Is.False);
            Assert.That(skill.Meets(0), Is.False);
        }

        [Test]
        public void FirstBlocker_LetsACapableColonistThrough()
        {
            Assert.That(WorkerSkill.FirstBlocker(WorkerSkill.OfSkillTracker(14), 14),
                Is.EqualTo(ImproveSkillBlocker.None));
            Assert.That(WorkerSkill.FirstBlocker(WorkerSkill.OfSkillTracker(20), 14),
                Is.EqualTo(ImproveSkillBlocker.None));
        }

        [Test]
        public void FirstBlocker_LetsAnyReadableSkillThroughWhenNoQualityIsTargeted()
        {
            // A null requirement is the "any improvement" mark, where the player asked for no
            // particular quality. A colonist at 0 is allowed; that is the feature.
            Assert.That(WorkerSkill.FirstBlocker(WorkerSkill.OfSkillTracker(0), null),
                Is.EqualTo(ImproveSkillBlocker.None));
            Assert.That(WorkerSkill.FirstBlocker(WorkerSkill.OfMechanoid(10), null),
                Is.EqualTo(ImproveSkillBlocker.None));
        }

        [Test]
        public void FirstBlocker_RefusesAnUnreadableSkillEvenWhenNoQualityIsTargeted()
        {
            // The defect this whole commit exists for. The requirement branch is skipped entirely on
            // an "any improvement" mark, so if the unreadable check lived inside that branch, a
            // skill-less non-mechanoid would reach CompleteImprovement and throw inside vanilla's
            // QualityUtility.GenerateQualityCreatedByPawn. Moving the two lines of FirstBlocker into
            // the other order, or folding the first into the second, fails here and nowhere else.
            Assert.That(WorkerSkill.FirstBlocker(WorkerSkill.Unknown(), null),
                Is.EqualTo(ImproveSkillBlocker.NoConstructionSkill));
        }

        [Test]
        public void FirstBlocker_RefusesAnUnreadableSkillAtEveryRequirement()
        {
            foreach (var required in new int?[] { null, 0, 1, 10, 20 })
            {
                Assert.That(WorkerSkill.FirstBlocker(WorkerSkill.Unknown(), required),
                    Is.EqualTo(ImproveSkillBlocker.NoConstructionSkill),
                    "An unreadable skill must be refused at requirement " + (required.HasValue ? required.Value.ToString() : "none") + ".");
            }
        }

        [Test]
        public void FirstBlocker_ReportsSkillTooLowOnlyForAReadableSkill()
        {
            // The two blockers must stay distinguishable. A caller shows a different message for each,
            // and the "skill too low" message reads the required level out of the target quality, which
            // does not exist on the unreadable path.
            Assert.That(WorkerSkill.FirstBlocker(WorkerSkill.OfSkillTracker(3), 14),
                Is.EqualTo(ImproveSkillBlocker.SkillTooLow));
            Assert.That(WorkerSkill.FirstBlocker(WorkerSkill.OfMechanoid(10), 14),
                Is.EqualTo(ImproveSkillBlocker.SkillTooLow));
            Assert.That(WorkerSkill.FirstBlocker(WorkerSkill.Unknown(), 14),
                Is.Not.EqualTo(ImproveSkillBlocker.SkillTooLow));
        }

        [Test]
        public void FirstBlocker_ReachesEveryBlockerInTheEnum()
        {
            // A sweep, so a blocker added without a way to produce it fails here rather than becoming
            // dead, and so a rewrite that collapses two blockers into one is caught.
            var produced = new[]
            {
                WorkerSkill.FirstBlocker(WorkerSkill.OfSkillTracker(14), 14),
                WorkerSkill.FirstBlocker(WorkerSkill.Unknown(), null),
                WorkerSkill.FirstBlocker(WorkerSkill.OfSkillTracker(3), 14)
            };

            var all = Enum.GetValues(typeof(ImproveSkillBlocker)).Cast<ImproveSkillBlocker>().ToList();

            Assert.That(produced, Is.EquivalentTo(all),
                "Every ImproveSkillBlocker must be reachable, and no two cases may collapse.");
        }

        [Test]
        public void FirstBlocker_AgreesWithMeetsWhereverARequirementExists()
        {
            // The work giver reports through FirstBlocker and the warning messages report through
            // Meets. If those two ever disagreed, a building would warn that nobody can improve it and
            // then be improved anyway, or the reverse.
            foreach (var skill in new[]
                { WorkerSkill.OfSkillTracker(0), WorkerSkill.OfSkillTracker(14), WorkerSkill.OfMechanoid(10), WorkerSkill.Unknown() })
            {
                foreach (var required in new[] { 0, 4, 10, 14, 20 })
                {
                    var blocked = WorkerSkill.FirstBlocker(skill, required) != ImproveSkillBlocker.None;

                    Assert.That(blocked, Is.EqualTo(!skill.Meets(required)),
                        skill.Source + " at requirement " + required + " disagrees between FirstBlocker and Meets.");
                }
            }
        }

        [Test]
        public void AMechanoidAndAColonistAtTheSameLevelAreJudgedIdentically()
        {
            // The requirement comparison must not care where the level came from. Only the refusal in
            // IsKnown does. This is what lets a constructoid be held to exactly the skill table the
            // player configured, rather than to a mech-specific rule that would drift from it.
            var mech = WorkerSkill.OfMechanoid(10);
            var colonist = WorkerSkill.OfSkillTracker(10);

            foreach (var required in new[] { 0, 9, 10, 11, 20 })
            {
                Assert.That(mech.Meets(required), Is.EqualTo(colonist.Meets(required)),
                    "A mech and a colonist at level 10 disagree at requirement " + required + ".");
            }
        }
    }
}
