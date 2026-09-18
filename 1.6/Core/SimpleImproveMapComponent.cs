using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SimpleImprove.Core
{
    /// <summary>
    /// Holds the target quality store that saves written before version 1.0.9 used, and switches
    /// improvement work on for colony mechs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store is a migration shim and nothing writes to it any more. Target quality now lives on
    /// <see cref="SimpleImproveComp"/> and is scribed with the rest of that component's state, which
    /// is where it belongs and where it can survive a building that is not on a map. This dictionary
    /// exists only so that a save written by an earlier version does not lose the targets the player
    /// set; <see cref="SimpleImproveComp.PostSpawnSetup"/> drains an entry as each building spawns.
    /// </para>
    /// <para>
    /// It had to live here in the first place because the improvement component used to be attached
    /// at runtime by a Harmony patch and therefore could not persist anything of its own. That was
    /// fixed by declaring the component on the defs, which is what makes the field on the component
    /// possible now.
    /// </para>
    /// <para>
    /// The store lives for exactly one load. <see cref="FinalizeInit"/> discards whatever is left
    /// once every building on the map has spawned and had its chance to claim an entry, so the
    /// dictionary is empty from the first save onwards and can never speak again. That bound is
    /// load bearing rather than tidiness, and <see cref="DiscardUnclaimedTargetQualities"/> says
    /// why.
    /// </para>
    /// <para>
    /// This is not the orphan sweep that used to run here, which is the defect being fixed. That
    /// one ran on every load forever, and deleted the target of any marked building that happened
    /// to be minified, in a caravan or in any other container at the time. This runs once, after
    /// the targets that can be rescued have been, and the state it discards belongs to buildings
    /// that are not marked any more anyway.
    /// </para>
    /// <para>
    /// The whole store can be removed in a later version, once saves that predate the move are no
    /// longer a realistic concern.
    /// </para>
    /// </remarks>
    public class SimpleImproveMapComponent : MapComponent
    {
        /// <summary>
        /// Target qualities from a save written before the field moved onto the component, keyed by
        /// <c>thingIDNumber</c>.
        /// </summary>
        private Dictionary<int, QualityCategory> targetQualities = new Dictionary<int, QualityCategory>();

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleImproveMapComponent"/> class.
        /// </summary>
        /// <param name="map">The map this component belongs to.</param>
        public SimpleImproveMapComponent(Map map) : base(map)
        {
        }

        /// <summary>
        /// Decides whether a spawning building should adopt a target quality from the legacy store.
        /// </summary>
        /// <param name="respawningAfterLoad">Whether this spawn is a save being loaded.</param>
        /// <param name="markedForImprovement">Whether the building is marked, as just loaded.</param>
        /// <param name="targetAlreadyOnComp">Whether the component already carries a target.</param>
        /// <returns><c>true</c> when the legacy store should be consulted.</returns>
        /// <remarks>
        /// <para>
        /// The load test is what makes this a migration rather than a second source of truth, and it
        /// is narrower than it looks. An ordinary spawn does not consult the store at all, and that
        /// includes reinstalling a minified building, because <c>Frame.CompleteConstruction</c>
        /// reaches <c>GenSpawn.Spawn</c> without the <c>respawningAfterLoad</c> argument and its
        /// default is false. So the only thing that can claim an entry is a building standing on a
        /// map at the moment a save is loaded.
        /// </para>
        /// <para>
        /// The mark test holds the invariant that an unmarked building has no target. It matters for
        /// real saves rather than in principle: before version 1.0.9 cancelling an improvement
        /// cleared the flag and left the target in this dictionary, so an old save can easily hold
        /// an entry for a building the player unmarked days earlier. The flag is readable here,
        /// because <c>PostExposeData</c> has already run by the time anything spawns.
        /// </para>
        /// <para>
        /// The component test stops the store overwriting a value the player set after the move. A
        /// save written by this version carries the target on the component and leaves the
        /// dictionary empty, so it is redundant today, but the three together hold for a save
        /// written by either version.
        /// </para>
        /// </remarks>
        public static bool ShouldMigrateTargetQuality(
            bool respawningAfterLoad, bool markedForImprovement, bool targetAlreadyOnComp)
        {
            return respawningAfterLoad && markedForImprovement && !targetAlreadyOnComp;
        }

        /// <summary>
        /// Reads a target quality out of the legacy store and removes it.
        /// </summary>
        /// <param name="thingID">The <c>thingIDNumber</c> of the building claiming its target.</param>
        /// <returns>The stored target quality, or <c>null</c> when there is no entry.</returns>
        /// <remarks>
        /// Removing on read makes the claim a one-way move. The reason is load-clear-save-load, not
        /// reinstalling: a reinstall spawns with <c>respawningAfterLoad</c> false and never reaches
        /// the store at all. What it stops is the player loading an old save, clearing the target
        /// back to "any improvement", saving and loading again, where the comp's target is null once
        /// more and a surviving entry would be applied over the choice they just made.
        /// <see cref="DiscardUnclaimedTargetQualities"/> closes the same hole from the other end, so
        /// this is now belt and braces, but each of the two is correct on its own terms and neither
        /// is a reason to drop the other.
        /// </remarks>
        public QualityCategory? TakeTargetQuality(int thingID)
        {
            if (!targetQualities.TryGetValue(thingID, out var quality))
            {
                return null;
            }

            targetQualities.Remove(thingID);
            return quality;
        }

        /// <summary>
        /// Saves and loads the legacy target quality store.
        /// </summary>
        /// <remarks>
        /// This runs as part of <c>Map.ExposeComponents</c>, which <c>Verse.Game.LoadGame</c> reaches
        /// through its <c>maps</c> collection before <c>Scribe.loader.FinalizeLoading</c> and well
        /// before <c>Map.FinalizeLoading</c> spawns any things. The dictionary is therefore fully
        /// populated by the time the first building asks it for a target.
        /// </remarks>
        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref targetQualities, "targetQualities",
                LookMode.Value, LookMode.Value);

            if (targetQualities == null)
            {
                targetQualities = new Dictionary<int, QualityCategory>();
            }
        }

        /// <summary>
        /// Called after the map has finished loading, and after every thing on it has spawned.
        /// </summary>
        public override void FinalizeInit()
        {
            base.FinalizeInit();

            DiscardUnclaimedTargetQualities();
            EnableImprovingForColonyMechs();
        }

        /// <summary>
        /// Throws away any legacy target quality that no building claimed during this load.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The timing is the whole of it. <c>Map.FinalizeLoading</c> spawns every thing on the map
        /// and then calls <c>Map.FinalizeInit</c> as its last statement, which is what reaches
        /// <c>MapComponentUtility.FinalizeInit</c> and therefore this. So every building that could
        /// claim an entry already has, and what is left belongs to something that is not on this map.
        /// </para>
        /// <para>
        /// Keeping the remainder looks kinder and is not. An entry cannot be claimed later: a
        /// reinstall spawns with <c>respawningAfterLoad</c> false, so the only other chance it gets
        /// is the next load, by which time the player may have set a target of their own. The comp
        /// writes nothing when the target is null, and null is also what "any improvement" means, so
        /// a surviving entry would be applied over a deliberate choice with no way to tell the two
        /// apart. That is a wrong setting applied silently, which is worse than a lost one.
        /// </para>
        /// <para>
        /// Nothing marked is being discarded either. A building that is off a map is a building that
        /// went into a container, and <c>ThingOwner.NotifyAdded</c> calls
        /// <c>RemoveAllDesignationsOn</c> for every holder that
        /// <c>ThingOwnerUtility.IsEnclosingContainer</c> accepts, which includes <c>MinifiedThing</c>.
        /// That fires <c>Notify_Removing</c> and this mod's prefix clears the mark. So the entries
        /// swept here are targets belonging to unmarked buildings, and an unmarked building has no
        /// target by construction.
        /// </para>
        /// </remarks>
        private void DiscardUnclaimedTargetQualities()
        {
            targetQualities.Clear();
        }

        /// <summary>
        /// Switches improvement work on for colony mechs that still have it stored at priority zero.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The def patch that lets a mech hold the work type cannot raise a priority already saved
        /// against it, and the player has nowhere to raise it either: RimWorld's only per-work-type
        /// priority UI is the Work tab, which lists humanlike colonists and never mechs.
        /// <see cref="ImproveWorkers.ShouldEnableForMech"/> carries the full reasoning and why this is
        /// not overriding a choice the player made.
        /// </para>
        /// <para>
        /// Runs on every map load rather than once behind a scribed flag. It is idempotent, it costs
        /// one pass over the colony mechs on the map, and a one-shot flag would miss a mech that was
        /// away in a caravan when the flag was set.
        /// </para>
        /// </remarks>
        private void EnableImprovingForColonyMechs()
        {
            if (map?.mapPawns == null)
            {
                return;
            }

            // Copied, because SpawnedColonyMechs hands back a shared buffer that it clears on the
            // next access rather than a list of its own.
            foreach (var mech in map.mapPawns.SpawnedColonyMechs.ToList())
            {
                if (mech?.workSettings == null || !mech.workSettings.EverWork)
                {
                    continue;
                }

                // IsColonyMech is what SpawnedColonyMechs already filters on, and is passed anyway so
                // the predicate states its own precondition rather than inheriting it from the caller.
                if (!ImproveWorkers.ShouldEnableForMech(
                        mech.IsColonyMech,
                        mech.WorkTypeIsDisabled(SimpleImproveDefOf.WorkType_Improving),
                        mech.workSettings.GetPriority(SimpleImproveDefOf.WorkType_Improving)))
                {
                    continue;
                }

                mech.workSettings.SetPriority(
                    SimpleImproveDefOf.WorkType_Improving, ImproveWorkers.DefaultMechPriority);
            }
        }
    }
}
