using NUnit.Framework;
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
        public void TheComponentIsNotSealed()
        {
            // ThingWithComps.GetComp<T> short-circuits to null for a sealed type parameter that its
            // compsByType dictionary does not contain. Nothing depends on that today, which is
            // exactly why it is worth a test rather than only a comment.
            Assert.That(typeof(SimpleImproveComp).IsSealed, Is.False);
        }
    }
}
