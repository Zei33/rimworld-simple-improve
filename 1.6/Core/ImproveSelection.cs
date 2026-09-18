using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SimpleImprove.Core
{
    /// <summary>
    /// Works out, once per frame, how the current selection divides into improvement groups.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to live in <see cref="SimpleImproveComp"/> and run once per selected comp per
    /// frame, each run rescanning the whole selection with three <c>TryGetComp</c> calls per
    /// element. The comp-lookup count was therefore quadratic in the selection, and
    /// <c>Selector</c> caps a selection at 200, so a drag over a room of furniture reached
    /// 200 x 200 x 3 lookups plus six LINQ enumerators and two lists per comp, every frame the
    /// selection was drawn. No millisecond figure is claimed here. The quadratic shape is from the
    /// source; nothing in this project runs RimWorld headlessly, and an earlier internal note
    /// quoting a measured 16 ms at the cap was never measured.
    /// </para>
    /// <para>
    /// The analysis never reads a particular comp. The only per-comp part of drawing the gizmos is
    /// the <c>Representative == this</c> test, which is why one shared cache is correct here rather
    /// than a field on each comp.
    /// </para>
    /// <para>
    /// The attribute is here for the two texture fields, not for any work done at startup. With
    /// dev mode on, <c>StaticConstructorOnStartupUtility.ReportProbablyMissingAttributes</c> warns
    /// about every type holding a static <c>Texture</c> field without it, whether or not the field
    /// is ever filled off the main thread. Both are filled lazily from the gizmo code, which runs
    /// on the main thread, so the warning was a false positive; the attribute answers it. All it
    /// adds is that the static initialisers below run once from <c>CallAll</c> at startup, and
    /// they only allocate an empty list.
    /// </para>
    /// </remarks>
    [StaticConstructorOnStartup]
    public static class ImproveSelection
    {
        /// <summary>The frame <see cref="cachedGroups"/> was built on.</summary>
        private static int cachedFrame = -1;

        /// <summary>The selection <see cref="cachedGroups"/> was built from, element by element.</summary>
        private static readonly List<object> cachedSelection = new List<object>();

        /// <summary>The last analysis, or <c>null</c> when there is none.</summary>
        private static List<ImproveGroup> cachedGroups;

        /// <summary>Backing field for <see cref="Icon"/>.</summary>
        private static Texture2D icon;

        /// <summary>
        /// The improve gizmo's texture, resolved once rather than per gizmo per frame.
        /// </summary>
        /// <remarks>
        /// <c>ContentFinder&lt;T&gt;.Get</c> is not the single dictionary lookup it looks like. It
        /// walks every running mod in reverse load order, doing a type-test chain and a
        /// <c>TryGetValue</c> for each, before it falls through to <c>Resources.Load</c>. On a heavy
        /// mod list that is a hundred lookups, and it was being paid for every gizmo built.
        /// <para>
        /// The refresh test is an explicit <c>== null</c> and must stay one. Changing the game's
        /// language, or hot-reloading content in dev mode, calls <c>UnityEngine.Object.Destroy</c> on
        /// every cached texture. A destroyed Unity object is "fake null": it satisfies the overloaded
        /// <c>==</c> operator but is not reference-null, so <c>??</c> and <c>?.</c> would see a live
        /// object, never refetch, and draw a destroyed texture.
        /// </para>
        /// </remarks>
        public static Texture2D Icon
        {
            get
            {
                if (icon == null)
                {
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Improve", true);
                }

                return icon;
            }
        }

        /// <summary>Backing field for <see cref="CancelIcon"/>.</summary>
        private static Texture2D cancelIcon;

        /// <summary>
        /// The texture vanilla uses for every cancel button, resolved once.
        /// </summary>
        /// <remarks>
        /// The path is vanilla's own. All three vanilla cancel gizmos yielded from a comp
        /// (<c>CompPlantable</c>, <c>CompHoldingPlatformTarget</c> and <c>UnfinishedThing</c>) use
        /// <c>UI/Designators/Cancel</c>, and the first two hold it in a static exactly like this.
        /// <para>
        /// Holding one shared reference is not only about the lookup cost here. <c>Command.GroupsWith</c>
        /// merges two gizmos only when their hotkey, label, group key and <c>icon</c> all match, and
        /// <c>icon</c> is compared by reference. Fetching the texture per gizmo would still return the
        /// same object today, but a static makes the merge independent of that.
        /// </para>
        /// <para>
        /// The <c>== null</c> test is deliberate for the same fake-null reason as <see cref="Icon"/>.
        /// </para>
        /// </remarks>
        public static Texture2D CancelIcon
        {
            get
            {
                if (cancelIcon == null)
                {
                    cancelIcon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", true);
                }

                return cancelIcon;
            }
        }

        /// <summary>
        /// Gets the improvement groups for whatever is selected right now.
        /// </summary>
        /// <returns>The groups, which callers must treat as read-only.</returns>
        /// <remarks>
        /// <para>
        /// The returned list is shared by every comp that asks in the same frame, and
        /// <c>CreateGroupGizmo</c> closes over both it and one of its groups in an action that runs
        /// when the player picks a float menu option many frames later. Do not sort, filter or add to
        /// it: before the cache each comp had its own list, so a mutation was contained, and now one
        /// mutation would corrupt every comp's view.
        /// </para>
        /// </remarks>
        public static List<ImproveGroup> Current()
        {
            List<object> live = Find.Selector.SelectedObjectsListForReading;

            if (cachedGroups != null && StillCurrent(cachedFrame, Time.frameCount, cachedSelection, live))
            {
                return cachedGroups;
            }

            cachedFrame = Time.frameCount;
            cachedSelection.Clear();
            cachedSelection.AddRange(live);
            cachedGroups = GroupsFor(EligibleIn(live));

            return cachedGroups;
        }

        /// <summary>
        /// Decides whether a cached analysis may still be served.
        /// </summary>
        /// <param name="cachedOnFrame">The frame the cache was built on.</param>
        /// <param name="frameNow">The current frame.</param>
        /// <param name="snapshot">The selection the cache was built from.</param>
        /// <param name="live">The selection now.</param>
        /// <returns><c>true</c> when the cache describes exactly the current selection.</returns>
        /// <remarks>
        /// <para>
        /// Keying on the frame ALONE is the defect the obvious implementation ships, and it is worth
        /// spelling out why, because the reasoning that suggests it is sound and the conclusion is
        /// wrong. The selection can change part-way through a frame:
        /// <c>UIRoot_Play.UIRootOnGUI</c> draws the gizmo grid and then, later in the same pass,
        /// runs <c>Selector.SelectorOnGUI</c>, whose mouse-up branch calls <c>SelectUnderMouse</c>
        /// or <c>SelectInsideDragBox</c>. The next pass has the same <c>Time.frameCount</c> and a
        /// different selection. Vanilla's <c>GizmoGridDrawer</c> correctly rebuilds and calls back
        /// into the comps, and a frame-only cache would hand it the previous selection's groups: for
        /// one frame the gizmo would show the old group's label, count and representative, and the
        /// float menu closure would capture the wrong comps.
        /// </para>
        /// <para>
        /// So this is the same key <c>GizmoGridDrawer</c> uses for the gizmo objects themselves, the
        /// frame plus an element-by-element comparison. That equivalence is the safety argument: this
        /// cache can never outlive vanilla's, so it can never serve a value vanilla would not have
        /// served anyway.
        /// </para>
        /// <para>
        /// The opposite worry, that a cache built in the Layout pass would wrongly serve the Repaint
        /// pass, is not a hazard and must not drive the design. <c>Time.frameCount</c> is stable
        /// across the passes of one frame, which is precisely what three separate vanilla constructs
        /// rely on to spot a duplicate event, and vanilla already builds its gizmo objects in Layout
        /// and draws them in Repaint.
        /// </para>
        /// <para>
        /// The elements are compared, never the list. <c>Selector</c> allocates its list once and
        /// only ever clears, adds to and removes from it, so the reference is the same object for the
        /// whole session no matter how the selection changes, which makes reference identity useless
        /// as a key.
        /// </para>
        /// </remarks>
        internal static bool StillCurrent(
            int cachedOnFrame, int frameNow, List<object> snapshot, List<object> live)
        {
            if (cachedOnFrame != frameNow || snapshot.Count != live.Count)
            {
                return false;
            }

            for (int i = 0; i < live.Count; i++)
            {
                if (!ReferenceEquals(snapshot[i], live[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Picks the improvable components out of a selection in one pass.
        /// </summary>
        /// <param name="selection">The selected objects.</param>
        /// <returns>The components eligible to carry a gizmo.</returns>
        /// <remarks>
        /// One indexed loop and one <c>TryGetComp</c> per comp type per object. The version this
        /// replaces called <c>TryGetComp&lt;CompQuality&gt;</c> twice on every object and
        /// <c>TryGetComp</c> a third time for the improve comp, inside a LINQ chain, and the whole
        /// chain ran once per selected comp. The cache still misses once per frame whenever the
        /// selection is being dragged, so the cost of a single pass is not academic.
        /// </remarks>
        internal static List<SimpleImproveComp> EligibleIn(List<object> selection)
        {
            var eligible = new List<SimpleImproveComp>();
            Faction player = Faction.OfPlayer;

            for (int i = 0; i < selection.Count; i++)
            {
                if (!(selection[i] is Thing thing) || thing.Faction != player)
                {
                    continue;
                }

                if (thing.def.blueprintDef == null)
                {
                    continue;
                }

                // The same question the gizmo asks, through the one function that holds it equal to
                // what the marking path asks of "any improvement". A building this admits is one the
                // menu will offer "Any improvement" on, so the two have to agree or that option
                // would silently do nothing.
                CompQuality quality = thing.TryGetComp<CompQuality>();
                if (quality == null || !ImproveTarget.CanBeOffered(quality.Quality))
                {
                    continue;
                }

                SimpleImproveComp improve = thing.TryGetComp<SimpleImproveComp>();
                if (improve != null)
                {
                    eligible.Add(improve);
                }
            }

            return eligible;
        }

        /// <summary>
        /// Divides eligible components into the groups a gizmo is drawn for.
        /// </summary>
        /// <param name="eligible">The components to group.</param>
        /// <returns>One group of unmarked components, then one per distinct target quality.</returns>
        /// <remarks>
        /// Unmarked components form a single group. Marked ones are grouped by target quality, with
        /// a null target treated as its own group rather than folded in with any particular quality.
        /// The first component of each group is its representative, which is the one that will draw
        /// the group's gizmo.
        /// </remarks>
        internal static List<ImproveGroup> GroupsFor(List<SimpleImproveComp> eligible)
        {
            var groups = new List<ImproveGroup>();

            if (eligible.Count == 0)
            {
                return groups;
            }

            var unmarked = eligible.Where(c => !c.IsMarkedForImprovement).ToList();
            if (unmarked.Count > 0)
            {
                groups.Add(new ImproveGroup
                {
                    Comps = unmarked,
                    IsMarked = false,
                    TargetQuality = null,
                    Representative = unmarked[0],
                    GroupKey = "unmarked"
                });
            }

            groups.AddRange(eligible
                .Where(c => c.IsMarkedForImprovement)
                .GroupBy(c => c.TargetQuality?.ToString() ?? "any")
                .Select(g => new ImproveGroup
                {
                    Comps = g.ToList(),
                    IsMarked = true,
                    TargetQuality = g.First().TargetQuality,
                    Representative = g.First(),
                    GroupKey = $"marked_{g.Key}"
                }));

            return groups;
        }
    }
}
