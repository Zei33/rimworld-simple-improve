using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RimWorld;
using SimpleImprove.Core;
using Verse;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers the decision that stops a stranded mark being worked: whether a mark still has an
    /// improvement ahead of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defect this is for: a building marked for any improvement and then raised to Legendary by
    /// something else was still handed out by the work giver and worked by the job driver, both of
    /// which asked only whether it was marked. At Legendary no roll beats the current quality, so
    /// <c>CompleteImprovement</c> always failed and, with materials required, destroyed the whole
    /// delivered cost without clearing the mark. The colony paid it again every cycle.
    /// </para>
    /// <para>
    /// Unusually for this mod, most of the decision runs here and not only its pure core.
    /// <c>ImproveTarget.IsOutstanding</c> is a function over two values. The component's half reads
    /// its own flag, its own target and its parent's <c>CompQuality</c>, and a <c>Building</c> with
    /// a hand-built comp list turns out to be enough for that: <c>ThingWithComps.GetComp</c> takes a
    /// type-test fast path below three comps, and <c>Thing.Map</c> is null without touching
    /// <c>Find</c> while <c>mapIndexOrState</c> is -1. What cannot run is every call site, which is
    /// what <see cref="StrandedMarkWiringTests"/> pins by reading IL instead.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class ImproveTargetTests
    {
        private static readonly QualityCategory[] AllQualities =
            Enum.GetValues(typeof(QualityCategory)).Cast<QualityCategory>().ToArray();

        [Test]
        public void TheSweepCoversEveryQuality()
        {
            // Every sweep below is over this array, so a short one would make them all pass on
            // less than they claim. Seven is Awful to Legendary.
            Assert.That(AllQualities, Has.Length.EqualTo(7));
            Assert.That(AllQualities.Max(), Is.EqualTo(QualityCategory.Legendary));
        }

        [Test]
        public void AnAnyMarkIsOutstandingUntilLegendary()
        {
            // The defect state is the last element: a Legendary building marked for any
            // improvement, which nothing can improve.
            foreach (var current in AllQualities)
            {
                Assert.That(
                    ImproveTarget.IsOutstanding(current, null),
                    Is.EqualTo(current != QualityCategory.Legendary),
                    "An any-improvement mark on a " + current + " building.");
            }
        }

        [Test]
        public void NothingIsOutstandingAtLegendaryWhateverTheMarkAimsAt()
        {
            foreach (var target in AllQualities.Cast<QualityCategory?>().Concat(new QualityCategory?[] { null }))
            {
                Assert.That(
                    ImproveTarget.IsOutstanding(QualityCategory.Legendary, target), Is.False,
                    "A Legendary building aimed at " + (target?.ToString() ?? "any improvement") + ".");
            }
        }

        [Test]
        public void ATargetIsOutstandingOnlyWhileTheBuildingIsBelowIt()
        {
            Assert.That(ImproveTarget.IsOutstanding(QualityCategory.Normal, QualityCategory.Good), Is.True,
                "Below the target: this is the ordinary case and must still be worked.");
            Assert.That(ImproveTarget.IsOutstanding(QualityCategory.Good, QualityCategory.Good), Is.False,
                "At the target: the mod's own loop stops here, so there is nothing left to do.");
            Assert.That(ImproveTarget.IsOutstanding(QualityCategory.Excellent, QualityCategory.Good), Is.False,
                "Past the target: rolling again would only chase a quality the player did not ask for.");
        }

        [Test]
        public void ALegendaryTargetIsStillOutstandingAtMasterwork()
        {
            // The top of the scale must not be off by one. A Masterwork building aimed at Legendary
            // is exactly what the Legendary warning exists for, and it is real work.
            Assert.That(ImproveTarget.IsOutstanding(QualityCategory.Masterwork, QualityCategory.Legendary), Is.True);
            Assert.That(ImproveTarget.IsOutstanding(QualityCategory.Masterwork, null), Is.True);
        }

        [Test]
        public void NoTargetIsOutstandingWhereAnyImprovementIsNot()
        {
            // The gizmo decides whether to offer the improve control by asking about an
            // any-improvement mark, and shows the stranded cancel button where that is not
            // outstanding. That is only sound if no set target can be outstanding where an any mark
            // is not; otherwise a building with work still ahead of its mark would lose its improve
            // control and be shown as stranded.
            foreach (var current in AllQualities)
            {
                foreach (var target in AllQualities)
                {
                    if (ImproveTarget.IsOutstanding(current, target))
                    {
                        Assert.That(ImproveTarget.IsOutstanding(current, null), Is.True,
                            current + " aimed at " + target + " is outstanding while any improvement is not.");
                    }
                }
            }
        }

        [Test]
        public void OfferingImprovementIsTheSameQuestionAsAcceptingAnAnyMark()
        {
            // The gizmo and the selection filter ask CanBeOffered; the marking path asks
            // IsOutstanding with no target when "Any improvement" is clicked. Held equal for every
            // quality here, because the call sites cannot run and IL reading cannot see an argument:
            // this is where "cannot drift" is actually tested rather than asserted by structure.
            foreach (var current in AllQualities)
            {
                Assert.That(
                    ImproveTarget.CanBeOffered(current),
                    Is.EqualTo(ImproveTarget.IsOutstanding(current, null)),
                    "Offering and accepting disagree on a " + current + " building.");
            }
        }

        [Test]
        public void EverythingBelowLegendaryCanBeOfferedImprovement()
        {
            // Pinned on its own as well as against IsOutstanding, so that both moving together
            // cannot pass. A Masterwork building losing its Improve button would take its "Any
            // improvement" and its Legendary option with it, and show the stranded cancel button in
            // their place.
            foreach (var current in AllQualities)
            {
                Assert.That(
                    ImproveTarget.CanBeOffered(current),
                    Is.EqualTo(current != QualityCategory.Legendary),
                    "A " + current + " building.");
            }
        }

        [Test]
        public void AnUnmarkedBuildingHasNothingOutstanding()
        {
            SimpleImproveComp comp = OnBuilding(QualityCategory.Normal, marked: false, target: null);

            Assert.That(comp.HasOutstandingImprovement, Is.False);
        }

        [Test]
        public void AMarkedBuildingBelowWhatItAimsAtHasWorkOutstanding()
        {
            Assert.That(OnBuilding(QualityCategory.Normal, marked: true, target: null).HasOutstandingImprovement, Is.True);
            Assert.That(OnBuilding(QualityCategory.Normal, marked: true, target: QualityCategory.Good).HasOutstandingImprovement, Is.True);
        }

        [Test]
        public void ALegendaryBuildingMarkedForAnyImprovementHasNothingOutstanding()
        {
            // The defect, on a component rather than in the abstract. This is the state the work
            // giver used to hand out forever.
            SimpleImproveComp comp = OnBuilding(QualityCategory.Legendary, marked: true, target: null);

            Assert.That(comp.IsMarkedForImprovement, Is.True, "The premise is a building that is still marked.");
            Assert.That(comp.HasOutstandingImprovement, Is.False);
        }

        [Test]
        public void AMarkWhoseTargetTheBuildingHasReachedOrPassedHasNothingOutstanding()
        {
            Assert.That(OnBuilding(QualityCategory.Good, marked: true, target: QualityCategory.Good).HasOutstandingImprovement, Is.False);
            Assert.That(OnBuilding(QualityCategory.Excellent, marked: true, target: QualityCategory.Good).HasOutstandingImprovement, Is.False);
        }

        [Test]
        public void ABuildingWithNoQualityHasNothingOutstanding()
        {
            // CompleteImprovement logs an error and returns on such a building without clearing the
            // mark, so treating it as outstanding would loop with a red error on every cycle.
            SimpleImproveComp comp = OnBuilding(null, marked: true, target: null);

            Assert.That(comp.HasOutstandingImprovement, Is.False);
        }

        [Test]
        public void TryMarkForReaimsAMarkedBuildingAtATargetStillAheadOfIt()
        {
            // The one path through TryMarkFor that can succeed here: the building is already marked,
            // so the setter's transition branch, which needs a map, is never reached.
            SimpleImproveComp comp = OnBuilding(QualityCategory.Normal, marked: true, target: null);

            Assert.That(comp.TryMarkFor(QualityCategory.Excellent), Is.True);
            Assert.That(comp.IsMarkedForImprovement, Is.True);
            Assert.That(comp.TargetQuality, Is.EqualTo(QualityCategory.Excellent));
        }

        [Test]
        public void TryMarkForWillNotReaimAMarkedBuildingAtATargetItHasPassed()
        {
            // The setter never sees a re-aim, since the flag does not change, so this check in
            // TryMarkFor is the only thing between a group menu and a Masterwork building aimed at
            // Good. The group offers every quality above its LOWEST member, so that option is on
            // screen whenever a group mixes qualities.
            SimpleImproveComp comp = OnBuilding(QualityCategory.Masterwork, marked: true, target: null);

            Assert.That(comp.TryMarkFor(QualityCategory.Good), Is.False);
            Assert.That(comp.TargetQuality, Is.Null, "The existing any-improvement mark must be left as it was.");
            Assert.That(comp.IsMarkedForImprovement, Is.True);
        }

        [Test]
        public void TryMarkForWillNotReaimAStrandedBuildingAtAnyImprovement()
        {
            SimpleImproveComp comp = OnBuilding(QualityCategory.Legendary, marked: true, target: QualityCategory.Legendary);

            Assert.That(comp.TryMarkFor(null), Is.False);
            Assert.That(comp.TargetQuality, Is.EqualTo(QualityCategory.Legendary));
        }

        [Test]
        public void TryMarkForWritesNothingForALegendaryBuilding()
        {
            // The race this closes: the group menu captured this building while it was below
            // Legendary, a pawn finished it at Legendary, and the player then clicked "Any
            // improvement". Off a map the setter would refuse the mark anyway, so the outcome alone
            // cannot show that TryMarkFor's own check ran. The stray target is what can: it is left
            // alone only if TryMarkFor refused before writing anything.
            SimpleImproveComp comp = OnBuilding(QualityCategory.Legendary, marked: false, target: QualityCategory.Masterwork);

            Assert.That(comp.TryMarkFor(null), Is.False);
            Assert.That(comp.IsMarkedForImprovement, Is.False);
            Assert.That(comp.TargetQuality, Is.EqualTo(QualityCategory.Masterwork),
                "TryMarkFor wrote a target before deciding, so its own check did not refuse first.");
        }

        [Test]
        public void TryMarkForLeavesNoTargetBehindWhenTheSetterRefuses()
        {
            // An unmarked building with no map: the target is still ahead of it, so TryMarkFor gets
            // as far as the setter, and the setter refuses because there is no designation manager
            // to write to. That is the minified-while-the-menu-was-open case. The building stays
            // unmarked, and an unmarked building must not carry a target.
            SimpleImproveComp comp = OnBuilding(QualityCategory.Normal, marked: false, target: null);

            Assert.That(comp.parent.Map, Is.Null, "The premise is a building with no map.");
            Assert.That(comp.TryMarkFor(QualityCategory.Good), Is.False);
            Assert.That(comp.IsMarkedForImprovement, Is.False);
            Assert.That(comp.TargetQuality, Is.Null);
        }

        [Test]
        public void TheLoopNeverKeepsAMarkTheWorkGiverWouldRefuse()
        {
            // After a successful roll CompleteImprovement keeps the mark only if
            // ShouldContinueImproving says so. If that ever kept a mark with nothing outstanding,
            // the mod's own loop would strand a building with no help from anything else. Swept over
            // every target and every quality a roll can land on.
            foreach (var target in AllQualities.Cast<QualityCategory?>().Concat(new QualityCategory?[] { null }))
            {
                var comp = new SimpleImproveComp { TargetQuality = target };

                foreach (var rolled in AllQualities)
                {
                    if (ShouldContinueImproving(comp, rolled))
                    {
                        Assert.That(ImproveTarget.IsOutstanding(rolled, target), Is.True,
                            "The loop keeps a " + rolled + " building aimed at "
                            + (target?.ToString() ?? "any improvement") + ", which the work giver refuses.");
                    }
                }
            }
        }

        [Test]
        public void TheLoopCarriesOnExactlyWhileASetTargetIsAhead()
        {
            // The other direction. Stopping early is a player-visible regression of its own: a
            // building aimed at Excellent that rolled Good must stay marked.
            var comp = new SimpleImproveComp { TargetQuality = QualityCategory.Excellent };

            Assert.That(ShouldContinueImproving(comp, QualityCategory.Good), Is.True);
            Assert.That(ShouldContinueImproving(comp, QualityCategory.Excellent), Is.False);
            Assert.That(ShouldContinueImproving(comp, QualityCategory.Masterwork), Is.False);
        }

        [Test]
        public void AnAnyImprovementMarkIsFinishedByItsFirstSuccess()
        {
            // Unchanged behaviour, pinned because the new rule makes it look inconsistent: an any
            // mark is outstanding below Legendary, yet one success ends it. "Any improvement" means
            // one improvement.
            var comp = new SimpleImproveComp();

            foreach (var rolled in AllQualities)
            {
                Assert.That(ShouldContinueImproving(comp, rolled), Is.False, "Rolled " + rolled + ".");
            }
        }

        /// <summary>
        /// Builds an improvement component standing on a building, in a given state, without a map.
        /// </summary>
        /// <param name="quality">The building's quality, or <c>null</c> for a building with no <c>CompQuality</c>.</param>
        /// <param name="marked">Whether the component should read as marked.</param>
        /// <param name="target">The target quality, or <c>null</c> for any improvement.</param>
        /// <returns>The component.</returns>
        /// <remarks>
        /// <para>
        /// The comp list is written by reflection because <c>InitializeComps</c> needs a def with
        /// real <c>CompProperties</c>. One or two comps, deliberately: <c>GetComp</c> below three
        /// type-tests the list directly, and at three or more it consults <c>compsByType</c>, which
        /// this does not build. Never none: that fast path reads <c>comps[0]</c> whenever the count
        /// is below three, so an empty list throws rather than answering null. The improve comp is
        /// always in the list, so this rig never builds one.
        /// </para>
        /// <para>
        /// The flag is written through its field for the same reason <c>ImproveSelectionTests</c>
        /// gives: the setter adds a designation, which needs a map. Every premise is asserted rather
        /// than assumed, because a rig that silently stopped attaching the quality comp would turn
        /// half this fixture into tests of the no-quality branch.
        /// </para>
        /// </remarks>
        private static SimpleImproveComp OnBuilding(QualityCategory? quality, bool marked, QualityCategory? target)
        {
            var building = new Building();
            var comps = new List<ThingComp>();
            CompQuality compQuality = null;

            if (quality.HasValue)
            {
                compQuality = new CompQuality { parent = building };
                Field(typeof(CompQuality), "qualityInt").SetValue(compQuality, quality.Value);
                comps.Add(compQuality);
            }

            var improve = new SimpleImproveComp { parent = building, TargetQuality = target };
            comps.Add(improve);

            Field(typeof(ThingWithComps), "comps").SetValue(building, comps);
            Field(typeof(SimpleImproveComp), "isMarkedForImprovement").SetValue(improve, marked);

            Assert.That(building.TryGetComp<SimpleImproveComp>(), Is.SameAs(improve));
            Assert.That(building.TryGetComp<CompQuality>(), Is.SameAs(compQuality));
            if (quality.HasValue)
            {
                Assert.That(compQuality.Quality, Is.EqualTo(quality.Value));
            }

            Assert.That(improve.IsMarkedForImprovement, Is.EqualTo(marked));
            Assert.That(building.Map, Is.Null);

            return improve;
        }

        private static FieldInfo Field(Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, type.FullName + "." + name + " was renamed, so this rig builds nothing.");
            return field;
        }

        private static bool ShouldContinueImproving(SimpleImproveComp comp, QualityCategory rolled)
        {
            MethodInfo method = typeof(SimpleImproveComp).GetMethod(
                "ShouldContinueImproving", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(method, Is.Not.Null, "SimpleImproveComp.ShouldContinueImproving was renamed.");

            return (bool)method.Invoke(comp, new object[] { rolled });
        }
    }
}
