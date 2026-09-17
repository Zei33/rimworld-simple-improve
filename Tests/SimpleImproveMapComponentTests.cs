using System.Linq;
using NUnit.Framework;
using RimWorld;
using SimpleImprove.Core;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers the target quality store in <see cref="SimpleImproveMapComponent"/>.
    /// </summary>
    /// <remarks>
    /// The component constructs with a null map because <c>MapComponent</c>'s constructor only
    /// assigns the field. Everything that reaches through <c>map</c> is therefore out of scope
    /// here, which is <c>CleanupOrphanedEntries</c> and the tick that calls it.
    ///
    /// This store exists only because the improvement component used to be attached at runtime and
    /// could not persist anything. Now that it is declared on the defs and round-trips properly,
    /// this whole class is redundant and is scheduled for removal in simple-improve#13. These tests
    /// describe what it does today so that the removal can be shown to lose nothing.
    /// </remarks>
    [TestFixture]
    public class SimpleImproveMapComponentTests
    {
        private static SimpleImproveMapComponent NewComponent()
        {
            return new SimpleImproveMapComponent(null);
        }

        [Test]
        public void ConstructsWithoutAMap()
        {
            Assert.That(NewComponent(), Is.Not.Null);
        }

        [Test]
        public void AnUnknownThingHasNoTargetQuality()
        {
            Assert.That(NewComponent().GetTargetQuality(1234), Is.Null);
        }

        [Test]
        public void ATargetQualityRoundTrips()
        {
            var component = NewComponent();
            component.SetTargetQuality(1234, QualityCategory.Excellent);

            Assert.That(component.GetTargetQuality(1234), Is.EqualTo(QualityCategory.Excellent));
        }

        [Test]
        public void SettingANullTargetClearsTheEntryRatherThanStoringIt()
        {
            // Null means "any improvement is acceptable", which the store expresses by absence.
            var component = NewComponent();
            component.SetTargetQuality(1234, QualityCategory.Excellent);

            component.SetTargetQuality(1234, null);

            Assert.That(component.GetTargetQuality(1234), Is.Null);
            Assert.That(component.GetAllTrackedThingIDs(), Does.Not.Contain(1234));
        }

        [Test]
        public void SettingATargetTwiceOverwritesRatherThanDuplicating()
        {
            var component = NewComponent();
            component.SetTargetQuality(1234, QualityCategory.Good);
            component.SetTargetQuality(1234, QualityCategory.Legendary);

            Assert.That(component.GetTargetQuality(1234), Is.EqualTo(QualityCategory.Legendary));
            Assert.That(component.GetAllTrackedThingIDs().Count(), Is.EqualTo(1));
        }

        [Test]
        public void TargetsAreKeptPerThing()
        {
            var component = NewComponent();
            component.SetTargetQuality(1, QualityCategory.Good);
            component.SetTargetQuality(2, QualityCategory.Masterwork);

            Assert.That(component.GetTargetQuality(1), Is.EqualTo(QualityCategory.Good));
            Assert.That(component.GetTargetQuality(2), Is.EqualTo(QualityCategory.Masterwork));
        }

        [Test]
        public void RemovingATargetForgetsIt()
        {
            var component = NewComponent();
            component.SetTargetQuality(1234, QualityCategory.Good);

            component.RemoveTargetQuality(1234);

            Assert.That(component.GetTargetQuality(1234), Is.Null);
        }

        [Test]
        public void RemovingAnUnknownThingIsHarmless()
        {
            Assert.That(() => NewComponent().RemoveTargetQuality(999), Throws.Nothing);
        }

        [Test]
        public void GetAllTrackedThingIDsIsSafeToRemoveFromWhileIterating()
        {
            // The method takes a copy for exactly this reason, and the orphan sweep depends on it.
            var component = NewComponent();
            component.SetTargetQuality(1, QualityCategory.Good);
            component.SetTargetQuality(2, QualityCategory.Good);

            Assert.That(() =>
            {
                foreach (var id in component.GetAllTrackedThingIDs())
                {
                    component.RemoveTargetQuality(id);
                }
            }, Throws.Nothing);

            Assert.That(component.GetAllTrackedThingIDs(), Is.Empty);
        }
    }
}
