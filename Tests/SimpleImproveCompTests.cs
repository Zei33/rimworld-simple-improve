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
        public void GetDirectlyHeldThingsReportsNullUntilSomethingIsActuallyHauled()
        {
            // The regression this guards is expensive and completely silent. Declaring the
            // component on the defs puts every quality building into ThingRequestGroup.ThingHolder,
            // and vanilla map traversals call GetDirectlyHeldThings on each of them. The wealth
            // recount in Map.FinalizeInit does exactly that on every load. If this allocated, every
            // quality building on the map would get a container it never uses, and would then write
            // an empty one into every save, because the scribe guard tests the field for null.
            var comp = new SimpleImproveComp();

            Assert.That(comp.GetDirectlyHeldThings(), Is.Null);
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
        public void GetDirectlyHeldThingsReportsTheContainerOnceItExists()
        {
            var comp = new SimpleImproveComp();
            var container = comp.GetMaterialContainer();

            Assert.That(comp.GetDirectlyHeldThings(), Is.SameAs(container));
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
        public void GetChildHoldersDoesNotAllocateAContainerEither()
        {
            var comp = new SimpleImproveComp();
            var children = new System.Collections.Generic.List<IThingHolder>();

            comp.GetChildHolders(children);

            Assert.That(comp.GetDirectlyHeldThings(), Is.Null);
            Assert.That(children, Is.Empty);
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
