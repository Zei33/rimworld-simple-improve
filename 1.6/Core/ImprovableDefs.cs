using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SimpleImprove.Core
{
    /// <summary>
    /// Decides which defs carry <see cref="SimpleImproveComp"/>, and declares it on them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declaring the component through <c>def.comps</c> is what makes it persist.
    /// <c>Verse.ThingWithComps.ExposeData</c> calls <c>InitializeComps</c> on <c>LoadingVars</c>, and
    /// that rebuilds the comps list strictly from <c>def.comps</c>, so a component attached at
    /// runtime does not exist when the <c>PostExposeData</c> loop runs. Everything it wrote,
    /// including the deep-scribed container holding the hauled materials, was orphaned in the save
    /// and silently discarded on load.
    /// </para>
    /// <para>
    /// This is C# rather than an XML PatchOperation because
    /// <c>LoadedModManager.LoadAllActiveMods</c> applies patches before <c>XmlInheritance.Resolve</c>,
    /// so an XPath sees only nodes as they were authored. Of the 43 quality-bearing building defs the
    /// game ships, 41 inherit the quality comp from one of five abstract parents
    /// (<c>FurnitureWithQualityBase</c>, <c>BedWithQualityBase</c>, <c>ArtBuildingBase</c>,
    /// <c>MusicalInstrumentBase</c>, <c>RitualSeatBase</c>) and only two, <c>Sarcophagus</c> and
    /// <c>GibbetCage</c>, declare it themselves. An XPath over <c>comps</c> would therefore match
    /// seven nodes, would reach the eight quality buildings that have no blueprint and cannot be
    /// improved, could not filter on category because that is declared further up the chain again,
    /// and would be blind to any def another mod gives quality to. Note also that there is no
    /// <c>CompProperties_Quality</c> type: vanilla writes
    /// <c>&lt;li&gt;&lt;compClass&gt;CompQuality&lt;/compClass&gt;&lt;/li&gt;</c>.
    /// </para>
    /// </remarks>
    public static class ImprovableDefs
    {
        /// <summary>
        /// Decides whether a def should carry the improvement component.
        /// </summary>
        /// <param name="def">The def to test.</param>
        /// <returns><c>true</c> when the component belongs on this def.</returns>
        /// <remarks>
        /// This mirrors what the old runtime patch checked, minus its faction test, which is a
        /// property of a thing rather than of a def. Widening the set of defs that carry the
        /// component does not widen what the player can mark, because
        /// <see cref="SimpleImproveComp.CompGetGizmosExtra"/> re-checks the faction, the quality
        /// component and the blueprint before it yields anything.
        /// </remarks>
        public static bool Qualifies(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }

            if (def.category != ThingCategory.Building)
            {
                return false;
            }

            // Improving reuses the construction system, which needs a blueprint to cost against.
            // This is what excludes sculptures and the Royalty instruments: they are crafted from a
            // recipe and have no designationCategory, so the game generates them no blueprint.
            if (def.blueprintDef == null)
            {
                return false;
            }

            // HasComp<T> matches subclasses, so a mod's own CompQuality derivative counts.
            if (!def.HasComp<CompQuality>())
            {
                return false;
            }

            // Idempotent. Defs are rebuilt from XML on every play-data load, but a def that already
            // declares the component, or a subclass of it, must not get a second copy: GetComp<T>
            // returns only the first entry for a type and the duplicate would be unreachable.
            if (def.HasAssignableCompFrom(typeof(SimpleImproveComp)))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Declares the improvement component on every def that qualifies.
        /// </summary>
        /// <param name="defs">The defs to consider.</param>
        /// <returns>The number of defs the component was added to.</returns>
        public static int DeclareCompOn(IEnumerable<ThingDef> defs)
        {
            if (defs == null)
            {
                return 0;
            }

            var declared = 0;

            foreach (var def in defs)
            {
                if (!Qualifies(def))
                {
                    continue;
                }

                // A fresh properties instance per def rather than one shared instance, so that
                // nothing done to one def's properties can reach every other def.
                def.comps.Add(new CompProperties_SimpleImprove());
                declared++;
            }

            return declared;
        }
    }
}
