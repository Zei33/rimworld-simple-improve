using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using NUnit.Framework;
using RimWorld;
using SimpleImprove.Core;
using Verse;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers <see cref="ImprovableDefs"/>, which decides where the improvement component is
    /// declared. That declaration is the whole of the fix for the save/load defect: a component
    /// attached at runtime instead is rebuilt out of existence by
    /// <c>ThingWithComps.InitializeComps</c> on load, taking the hauled materials with it.
    /// </summary>
    [TestFixture]
    public class ImprovableDefsTests
    {
        /// <summary>
        /// A quality component declared the way the shipped XML declares it: a plain properties
        /// node carrying a compClass, not a CompProperties_Quality, which does not exist.
        /// </summary>
        private static CompProperties Quality()
        {
            return new CompProperties { compClass = typeof(CompQuality) };
        }

        /// <summary>
        /// Builds a ThingDef without running its constructor.
        /// </summary>
        /// <remarks>
        /// `new ThingDef()` cannot be used out here. Its constructor reaches Verse.BaseContent,
        /// whose static constructor initialises Verse.ShaderDatabase, which calls
        /// UnityEngine.Resources.Load, a native method that does not exist outside the player. The
        /// object this returns has every field at its type default, so nothing may read a field
        /// this helper has not explicitly set. That is fine for predicate logic and is the only
        /// thing defs built this way should ever be used for; they must never stand in for real
        /// def data.
        /// </remarks>
        private static ThingDef BareDef(string defName)
        {
            var def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
            def.defName = defName;
            def.comps = new List<CompProperties>();
            return def;
        }

        private static ThingDef ImprovableBuilding(params CompProperties[] comps)
        {
            var def = BareDef("TestBuilding");
            def.category = ThingCategory.Building;
            def.blueprintDef = BareDef("Blueprint_TestBuilding");

            def.comps.Add(Quality());
            def.comps.AddRange(comps);
            return def;
        }

        [Test]
        public void Qualifies_AcceptsABuildableQualityBuilding()
        {
            Assert.That(ImprovableDefs.Qualifies(ImprovableBuilding()), Is.True);
        }

        [Test]
        public void Qualifies_RejectsNull()
        {
            Assert.That(ImprovableDefs.Qualifies(null), Is.False);
        }

        [Test]
        public void Qualifies_RejectsItemsEvenWhenTheyCarryQuality()
        {
            // Apparel and weapons carry quality too, 141 of them in the shipped game. Improving
            // only ever meant buildings.
            var def = ImprovableBuilding();
            def.category = ThingCategory.Item;

            Assert.That(ImprovableDefs.Qualifies(def), Is.False);
        }

        [Test]
        public void Qualifies_RejectsAQualityBuildingWithNoBlueprint()
        {
            // Sculptures and the Royalty instruments. They are crafted from a recipe and have no
            // designationCategory, so the game generates them no blueprint and there is nothing to
            // cost an improvement against.
            var def = ImprovableBuilding();
            def.blueprintDef = null;

            Assert.That(ImprovableDefs.Qualifies(def), Is.False);
        }

        [Test]
        public void Qualifies_RejectsABuildingWithoutQuality()
        {
            var def = ImprovableBuilding();
            def.comps.Clear();

            Assert.That(ImprovableDefs.Qualifies(def), Is.False);
        }

        [Test]
        public void Qualifies_AcceptsAModsOwnCompQualitySubclass()
        {
            var def = ImprovableBuilding();
            def.comps.Clear();
            def.comps.Add(new CompProperties { compClass = typeof(DerivedQualityComp) });

            Assert.That(ImprovableDefs.Qualifies(def), Is.True);
        }

        [Test]
        public void Qualifies_RejectsADefThatAlreadyDeclaresTheComponent()
        {
            var def = ImprovableBuilding(new CompProperties_SimpleImprove());

            Assert.That(ImprovableDefs.Qualifies(def), Is.False);
        }

        [Test]
        public void DeclareCompOn_AddsTheComponentExactlyOnce()
        {
            var def = ImprovableBuilding();

            var declared = ImprovableDefs.DeclareCompOn(new[] { def });

            Assert.That(declared, Is.EqualTo(1));
            Assert.That(def.comps.Count(c => c is CompProperties_SimpleImprove), Is.EqualTo(1));
        }

        [Test]
        public void DeclareCompOn_IsIdempotentAcrossRepeatedLoads()
        {
            // This is the property that matters most. The defs are rebuilt and this runs again on
            // every play-data load, which a player triggers just by changing language, so a second
            // pass over the same def must not add a second component. GetComp<T> returns only the
            // first entry for a type, so a duplicate would be silently unreachable.
            var def = ImprovableBuilding();

            ImprovableDefs.DeclareCompOn(new[] { def });
            var second = ImprovableDefs.DeclareCompOn(new[] { def });

            Assert.That(second, Is.EqualTo(0));
            Assert.That(def.comps.Count(c => c is CompProperties_SimpleImprove), Is.EqualTo(1));
        }

        [Test]
        public void DeclareCompOn_SkipsDefsThatDoNotQualifyAndCountsOnlyTheOnesItChanged()
        {
            var improvable = ImprovableBuilding();
            var sculpture = ImprovableBuilding();
            sculpture.blueprintDef = null;
            var apparel = ImprovableBuilding();
            apparel.category = ThingCategory.Item;

            var declared = ImprovableDefs.DeclareCompOn(new[] { improvable, sculpture, apparel });

            Assert.That(declared, Is.EqualTo(1));
            Assert.That(sculpture.comps.Any(c => c is CompProperties_SimpleImprove), Is.False);
            Assert.That(apparel.comps.Any(c => c is CompProperties_SimpleImprove), Is.False);
        }

        [Test]
        public void DeclareCompOn_GivesEachDefItsOwnPropertiesInstance()
        {
            var first = ImprovableBuilding();
            var second = ImprovableBuilding();

            ImprovableDefs.DeclareCompOn(new[] { first, second });

            var a = first.comps.OfType<CompProperties_SimpleImprove>().Single();
            var b = second.comps.OfType<CompProperties_SimpleImprove>().Single();
            Assert.That(a, Is.Not.SameAs(b));
        }

        [Test]
        public void DeclareCompOn_ToleratesANullSequence()
        {
            Assert.That(ImprovableDefs.DeclareCompOn(null), Is.EqualTo(0));
        }

        /// <summary>
        /// Stands in for a quality component subclass supplied by another mod.
        /// </summary>
        private class DerivedQualityComp : CompQuality
        {
        }
    }
}
