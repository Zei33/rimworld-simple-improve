using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RimWorld;
using SimpleImprove.Core;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers the per-frame selection cache, which is issue #19.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ImproveSelection.Current"/> itself cannot run here: it reads <c>Find.Selector</c>
    /// and <c>UnityEngine.Time</c>. The two decisions inside it can, because both were extracted to
    /// take their inputs as arguments rather than read them, which is the shape this workspace
    /// already uses for the Chrono Save conditions.
    /// </para>
    /// <para>
    /// The cache key is the part worth testing, because the wrong version of it is the one a reader
    /// would reach for. Keying on the frame alone looks sufficient and is not.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class ImproveSelectionTests
    {
        [Test]
        public void ANewFrameInvalidatesTheCache()
        {
            var one = new List<object> { "a" };

            Assert.That(ImproveSelection.StillCurrent(10, 11, one, one), Is.False);
            Assert.That(ImproveSelection.StillCurrent(10, 10, one, one), Is.True);
        }

        [Test]
        public void ASelectionThatChangesWithinOneFrameInvalidatesTheCache()
        {
            // This is the whole reason the key is not just Time.frameCount, and the case that a
            // frame-only cache gets wrong. Within a single frame, UIRoot_Play draws the gizmo grid
            // and then runs Selector.SelectorOnGUI, whose mouse-up branch calls SelectUnderMouse or
            // SelectInsideDragBox. The next pass has the SAME frameCount and a different selection.
            // Vanilla's GizmoGridDrawer notices, because it compares the selected objects element by
            // element as well as checking the frame, and calls back into the comps. A frame-only
            // cache would hand it the previous selection's groups: for one frame the gizmo shows the
            // old group's label, count and representative, and the float menu closure captures the
            // wrong comps.
            object first = new object();
            object second = new object();

            var before = new List<object> { first };
            var after = new List<object> { second };

            Assert.That(
                ImproveSelection.StillCurrent(7, 7, before, after), Is.False,
                "The selection changed inside one frame and the cache still claimed to be current.");
        }

        [Test]
        public void AShorterOrLongerSelectionInvalidatesTheCache()
        {
            object shared = new object();
            var one = new List<object> { shared };
            var two = new List<object> { shared, new object() };

            Assert.That(ImproveSelection.StillCurrent(3, 3, one, two), Is.False);
            Assert.That(ImproveSelection.StillCurrent(3, 3, two, one), Is.False);
        }

        [Test]
        public void TheCacheComparesIdentityRatherThanEquality()
        {
            // Two distinct objects that compare equal are still a different selection. Selector holds
            // object references, and a Thing's Equals is reference equality anyway, so this is really
            // guarding against someone replacing ReferenceEquals with == or Equals and quietly
            // changing what "the same selection" means for any type that overrides it.
            var left = new List<object> { new string('a', 3) };
            var right = new List<object> { new string('a', 3) };

            Assert.That(left[0], Is.EqualTo(right[0]), "The premise of this test is two equal values.");
            Assert.That(ReferenceEquals(left[0], right[0]), Is.False, "which are not the same object.");

            Assert.That(ImproveSelection.StillCurrent(1, 1, left, right), Is.False);
        }

        [Test]
        public void AnEmptySelectionProducesNoGroups()
        {
            Assert.That(ImproveSelection.GroupsFor(new List<SimpleImproveComp>()), Is.Empty);
        }

        [Test]
        public void UnmarkedBuildingsShareOneGroup()
        {
            var comps = new List<SimpleImproveComp> { Comp(false, null), Comp(false, null), Comp(false, null) };

            var groups = ImproveSelection.GroupsFor(comps);

            Assert.That(groups.Count, Is.EqualTo(1));
            Assert.That(groups[0].IsMarked, Is.False);
            Assert.That(groups[0].Comps.Count, Is.EqualTo(3));
            Assert.That(groups[0].Representative, Is.SameAs(comps[0]),
                "The representative must be the first of the group, since that is the comp whose "
                + "CompGetGizmosExtra will draw the group's gizmo.");
        }

        [Test]
        public void MarkedBuildingsAreGroupedByTargetQualityAndAnyIsItsOwnGroup()
        {
            var unmarked = Comp(false, null);
            var anyA = Comp(true, null);
            var anyB = Comp(true, null);
            var excellent = Comp(true, QualityCategory.Excellent);
            var legendaryTarget = Comp(true, QualityCategory.Legendary);

            var groups = ImproveSelection.GroupsFor(
                new List<SimpleImproveComp> { unmarked, anyA, excellent, anyB, legendaryTarget });

            Assert.That(groups.Count, Is.EqualTo(4), "unmarked, any, Excellent, Legendary.");

            var keys = groups.Select(g => g.GroupKey).ToList();
            Assert.That(keys, Is.EquivalentTo(new[]
            {
                "unmarked", "marked_any", "marked_Excellent", "marked_Legendary"
            }));

            var any = groups.Single(g => g.GroupKey == "marked_any");
            Assert.That(any.Comps, Is.EquivalentTo(new[] { anyA, anyB }));
            Assert.That(any.TargetQuality, Is.Null,
                "A null target is its own group and must not be folded in with a quality.");
            Assert.That(any.Representative, Is.SameAs(anyA));

            Assert.That(
                groups.Single(g => g.GroupKey == "marked_Excellent").TargetQuality,
                Is.EqualTo(QualityCategory.Excellent));
        }

        [Test]
        public void EveryEligibleCompLandsInExactlyOneGroup()
        {
            // The gizmo is drawn by each group's representative, so a comp in two groups would draw
            // two buttons for one building and a comp in none would be silently unreachable through
            // its own UI, which is the shape of issue #23.
            var comps = new List<SimpleImproveComp>
            {
                Comp(false, null), Comp(true, null), Comp(true, QualityCategory.Good),
                Comp(false, null), Comp(true, QualityCategory.Good)
            };

            var placed = ImproveSelection.GroupsFor(comps).SelectMany(g => g.Comps).ToList();

            Assert.That(placed.Count, Is.EqualTo(comps.Count));
            Assert.That(placed, Is.EquivalentTo(comps));
        }

        /// <summary>
        /// Builds a component in a given state without going near a map.
        /// </summary>
        /// <param name="marked">Whether it should read as marked for improvement.</param>
        /// <param name="target">The target quality, or <c>null</c> for any improvement.</param>
        /// <returns>The component.</returns>
        /// <remarks>
        /// The marked flag is written through its field rather than its property. The setter adds and
        /// removes the designation, which needs a spawned parent on a map; the getter is a plain
        /// field read, which is all the grouping uses.
        /// </remarks>
        private static SimpleImproveComp Comp(bool marked, QualityCategory? target)
        {
            var comp = new SimpleImproveComp { TargetQuality = target };

            FieldInfo field = typeof(SimpleImproveComp).GetField(
                "isMarkedForImprovement", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.That(field, Is.Not.Null,
                "SimpleImproveComp.isMarkedForImprovement was renamed, so this fixture is building "
                + "components that are all unmarked and most of its assertions mean nothing.");

            field.SetValue(comp, marked);
            return comp;
        }
    }
}
