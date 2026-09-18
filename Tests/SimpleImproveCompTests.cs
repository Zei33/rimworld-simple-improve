using NUnit.Framework;
using RimWorld;
using SimpleImprove.Core;
using Verse;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers the parts of <see cref="SimpleImproveComp"/> that do not need a spawned parent.
    /// </summary>
    /// <remarks>
    /// Almost all of this component needs a <c>Thing</c> on a <c>Map</c>, so the reachable surface
    /// is small. What is here matters out of proportion to its size: it is the container's
    /// allocation behaviour, which the holder-tree change made load bearing.
    /// </remarks>
    [TestFixture]
    public class SimpleImproveCompTests
    {
        [Test]
        public void GetDirectlyHeldThingsNeverReportsNull()
        {
            // This assertion was the other way round until 2026-09-18, and the null it demanded threw
            // on ordinary mouse-over.
            //
            // Declaring the component on the defs puts every quality building into
            // ThingRequestGroup.ThingHolder, and almost every vanilla traversal that reaches a child
            // holder null-checks the result. Exactly one does not:
            // ContainingSelectionUtility.SelectableContainedThings walks ThingWithComps.AllComps and
            // does foreach (Thing t in (IEnumerable<Thing>)holder.GetDirectlyHeldThings()) with no
            // guard. A null cast to IEnumerable<Thing> is still null, so the foreach throws. It is
            // reached from GenUI.ThingsUnderMouse and Selector.SelectableObjectsUnderMouse, i.e. from
            // hovering, selecting or right-clicking any improvable building with nothing staged in it.
            //
            // The cost that motivated the null is real and is handled in PostExposeData instead,
            // which scribes the container only when it holds something.
            var comp = new SimpleImproveComp();

            Assert.That(comp.GetDirectlyHeldThings(), Is.Not.Null);
        }

        [Test]
        public void GetDirectlyHeldThingsIsTheSameContainerTheModHaulsInto()
        {
            // If these ever diverged, materials would be staged in one container and read from
            // another, and the mod would report an empty building it had just filled.
            var comp = new SimpleImproveComp();

            Assert.That(comp.GetDirectlyHeldThings(), Is.SameAs(comp.GetMaterialContainer()));
        }

        [Test]
        public void AnEmptyContainerIsNotWrittenIntoTheSave()
        {
            // The half of the crash fix that has no other observable. Now that
            // GetDirectlyHeldThings allocates on demand, every quality building a traversal walks
            // past ends up with an empty container, so a scribe guard testing for non-null would put
            // an empty node into every save for every quality building on the map. That is silent,
            // cumulative, and exactly the cost the old null return existed to avoid.
            var comp = new SimpleImproveComp();

            Assert.That(SimpleImproveComp.ShouldScribeContainer(comp.GetMaterialContainer()), Is.False);
        }

        [Test]
        public void AnAbsentContainerIsNotWrittenIntoTheSave()
        {
            Assert.That(SimpleImproveComp.ShouldScribeContainer(null), Is.False);
        }

        [Test]
        public void GetMaterialContainerCreatesTheContainerOnDemand()
        {
            var comp = new SimpleImproveComp();

            var container = comp.GetMaterialContainer();

            Assert.That(container, Is.Not.Null);
            Assert.That(container, Is.InstanceOf<ThingOwner>());
        }

        [Test]
        public void GetMaterialContainerReturnsTheSameContainerEveryTime()
        {
            var comp = new SimpleImproveComp();

            Assert.That(comp.GetMaterialContainer(), Is.SameAs(comp.GetMaterialContainer()));
        }

        [Test]
        public void TheContainerIsOwnedByTheComponentSoTheHolderTreeCanResolveALocation()
        {
            // Built with the component as owner rather than null, which is what lets
            // ThingOwnerUtility.GetRootMap and GetRootPosition walk up to the parent thing. Those
            // two have an explicit branch for a holder that is a ThingComp.
            var comp = new SimpleImproveComp();

            Assert.That(comp.GetMaterialContainer().Owner, Is.SameAs(comp));
        }

        [Test]
        public void GetChildHoldersDoesNotAllocateAContainer()
        {
            // Still worth pinning now that GetDirectlyHeldThings does allocate. GetChildHolders is
            // called by ThingOwnerUtility.AppendThingHoldersFromThings while walking the holder tree,
            // it has no unguarded caller forcing its hand, and it reports nothing for an empty
            // container anyway, so there is no reason for it to build one.
            //
            // Read through the field rather than through GetDirectlyHeldThings, which would create
            // the very container this is checking for the absence of.
            var comp = new SimpleImproveComp();
            var children = new System.Collections.Generic.List<IThingHolder>();

            comp.GetChildHolders(children);

            Assert.That(MaterialContainerField(comp), Is.Null);
            Assert.That(children, Is.Empty);
        }

        private static object MaterialContainerField(SimpleImproveComp comp)
        {
            return typeof(SimpleImproveComp)
                .GetField("materialContainer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(comp);
        }

        [Test]
        public void ANewComponentIsNotMarkedAndHasNoWork()
        {
            var comp = new SimpleImproveComp();

            Assert.That(comp.IsMarkedForImprovement, Is.False);
            Assert.That(comp.WorkDone, Is.EqualTo(0f));
        }

        [Test]
        public void ANewComponentHasNoTargetQuality()
        {
            // Null is "any improvement is acceptable", which is a real setting and not an absence.
            Assert.That(new SimpleImproveComp().TargetQuality, Is.Null);
        }

        [Test]
        public void TargetQualityRoundTripsWithoutAMap()
        {
            // The defect this replaces, and the reason it can be tested at all now. The target used
            // to live in a map component reached through parent?.Map?.GetComponent, so the getter
            // returned null and the setter silently discarded the write for any building that was
            // not standing on a map. That includes every building during loading, because
            // Thing.ExposeData forces mapIndexOrState to -1 and things only spawn later in
            // Map.FinalizeLoading, which is why the value could not be scribed from the component.
            var comp = new SimpleImproveComp();

            comp.TargetQuality = QualityCategory.Masterwork;

            Assert.That(comp.TargetQuality, Is.EqualTo(QualityCategory.Masterwork));
        }

        [Test]
        public void AwfulIsStoredRatherThanReadingAsNoTarget()
        {
            // QualityCategory is byte backed and Awful is zero, so this is the case a non-nullable
            // field could not express. Nothing in the UI offers Awful as a target today, but the
            // field is the thing being tested, not the menu in front of it.
            var comp = new SimpleImproveComp();

            comp.TargetQuality = QualityCategory.Awful;

            Assert.That(comp.TargetQuality, Is.Not.Null);
            Assert.That(comp.TargetQuality, Is.EqualTo(QualityCategory.Awful));
        }

        [Test]
        public void ClearingTheMarkDirectlyAlsoClearsTheTarget()
        {
            // The invariant is that an unmarked building has no target. Without it a stale target
            // outlives the mark: it can still show in the inspect pane while the building holds
            // materials, and re-marking would silently re-aim at a quality the player last saw
            // cancelled. This is the path vanilla's Designator_Cancel takes, through the mod's
            // Notify_Removing prefix.
            var comp = new SimpleImproveComp();
            comp.TargetQuality = QualityCategory.Legendary;
            comp.SetMarkedForImprovementDirect(true);

            comp.SetMarkedForImprovementDirect(false);

            Assert.That(comp.TargetQuality, Is.Null);
        }

        [Test]
        public void SettingTheMarkDirectlyLeavesTheTargetAlone()
        {
            // The gizmos set a target and then mark, in that order, so clearing on the way up would
            // throw away the thing the player just chose.
            var comp = new SimpleImproveComp();
            comp.TargetQuality = QualityCategory.Good;

            comp.SetMarkedForImprovementDirect(true);

            Assert.That(comp.TargetQuality, Is.EqualTo(QualityCategory.Good));
        }

        [Test]
        public void TheComponentDeclaresPostDeSpawn()
        {
            // A declaration test rather than a behaviour one, for the same reason
            // WorkGiverSurfaceTests exists: the fix has no functional signature this harness can
            // reach. PostDeSpawn needs a spawned Thing on a Map, so deleting the override is
            // invisible to every other test here, and its absence is precisely the defect. Before
            // it existed, uninstalling a marked building left its hauled materials inside a
            // component that nothing on the map could see.
            var declared = typeof(SimpleImproveComp).GetMethod(
                "PostDeSpawn",
                new[] { typeof(Map), typeof(DestroyMode) });

            Assert.That(declared, Is.Not.Null,
                "SimpleImproveComp no longer has a PostDeSpawn(Map, DestroyMode) at all.");
            Assert.That(declared.DeclaringType, Is.EqualTo(typeof(SimpleImproveComp)),
                "SimpleImproveComp stopped overriding PostDeSpawn, which strands hauled materials "
                + "in the component on every uninstall. That is issue #11.");
        }

        [Test]
        public void TheComponentIsNotSealed()
        {
            // ThingWithComps.GetComp<T> short-circuits to null for a sealed type parameter that its
            // compsByType dictionary does not contain. Nothing depends on that today, which is
            // exactly why it is worth a test rather than only a comment.
            Assert.That(typeof(SimpleImproveComp).IsSealed, Is.False);
        }
    }
}
