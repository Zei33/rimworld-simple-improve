using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SimpleImprove.Core
{
    /// <summary>
    /// MapComponent that handles persistent storage of target quality data for improved buildings.
    /// This ensures that target quality settings persist across save/load cycles even when
    /// SimpleImproveComp components are added dynamically via Harmony patches.
    /// </summary>
    public class SimpleImproveMapComponent : MapComponent
    {
        /// <summary>
        /// Dictionary mapping thing IDs to their target quality settings.
        /// Uses thingIDNumber which is unique and persists across save/load.
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
        /// Sets the target quality for a specific thing.
        /// </summary>
        /// <param name="thingID">The unique ID of the thing.</param>
        /// <param name="quality">The target quality to set, or null to clear target quality.</param>
        public void SetTargetQuality(int thingID, QualityCategory? quality)
        {
            if (quality.HasValue)
            {
                targetQualities[thingID] = quality.Value;
            }
            else
            {
                targetQualities.Remove(thingID);
            }
        }

        /// <summary>
        /// Gets the target quality for a specific thing.
        /// </summary>
        /// <param name="thingID">The unique ID of the thing.</param>
        /// <returns>The target quality, or null if no target is set.</returns>
        public QualityCategory? GetTargetQuality(int thingID)
        {
            if (targetQualities.TryGetValue(thingID, out var quality))
            {
                return quality;
            }
            return null;
        }

        /// <summary>
        /// Removes the target quality setting for a specific thing.
        /// </summary>
        /// <param name="thingID">The unique ID of the thing.</param>
        public void RemoveTargetQuality(int thingID)
        {
            targetQualities.Remove(thingID);
        }

        /// <summary>
        /// Gets all thing IDs that have target quality settings.
        /// Used for cleanup and validation operations.
        /// </summary>
        /// <returns>An enumerable of thing IDs with target quality settings.</returns>
        public IEnumerable<int> GetAllTrackedThingIDs()
        {
            return targetQualities.Keys.ToList(); // ToList to avoid modification during iteration
        }

        /// <summary>
        /// Cleans up orphaned entries for things that no longer exist on the map.
        /// This prevents the dictionary from growing indefinitely with dead references.
        /// </summary>
        public void CleanupOrphanedEntries()
        {
            var validThingIDs = new HashSet<int>();
            
            // Collect all valid thing IDs from spawned things with improvement designations
            foreach (var thing in map.listerThings.AllThings.OfType<ThingWithComps>())
            {
                if (thing.Faction == Faction.OfPlayer && 
                    thing.TryGetComp<CompQuality>() != null &&
                    thing.def.blueprintDef != null)
                {
                    validThingIDs.Add(thing.thingIDNumber);
                }
            }

            // Remove entries for things that no longer exist
            var keysToRemove = targetQualities.Keys.Where(id => !validThingIDs.Contains(id)).ToList();
            foreach (var key in keysToRemove)
            {
                targetQualities.Remove(key);
            }

            if (keysToRemove.Count > 0 && Prefs.DevMode)
            {
                Log.Message($"[SimpleImprove] Cleaned up {keysToRemove.Count} orphaned target quality entries");
            }
        }

        /// <summary>
        /// Saves and loads the target quality data to/from the save file.
        /// This method is automatically called by RimWorld's save/load system.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            
            // Save/load the target qualities dictionary
            Scribe_Collections.Look(ref targetQualities, "targetQualities", 
                LookMode.Value, LookMode.Value);
                
            // Initialize dictionary if it was null after loading
            if (targetQualities == null)
            {
                targetQualities = new Dictionary<int, QualityCategory>();
            }
        }

        /// <summary>
        /// Called after the map has finished loading.
        /// Performs cleanup of orphaned entries to maintain data integrity.
        /// </summary>
        public override void FinalizeInit()
        {
            base.FinalizeInit();

            // Clean up any orphaned entries after loading
            CleanupOrphanedEntries();

            EnableImprovingForColonyMechs();
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

        /// <summary>
        /// Called periodically to perform maintenance operations.
        /// Performs periodic cleanup to prevent memory bloat.
        /// </summary>
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            
            // Perform cleanup every 2 hours of game time (120,000 ticks)
            if (Find.TickManager.TicksGame % 120000 == 0)
            {
                CleanupOrphanedEntries();
            }
        }

        /// <summary>
        /// Gets a debug string showing the current state of the component.
        /// Used for debugging and development purposes.
        /// </summary>
        /// <returns>A string containing debug information.</returns>
        public string GetDebugString()
        {
            return $"SimpleImproveMapComponent: {targetQualities.Count} tracked items\n" +
                   string.Join("\n", targetQualities.Select(kvp => $"  Thing {kvp.Key}: {kvp.Value}"));
        }
    }
}