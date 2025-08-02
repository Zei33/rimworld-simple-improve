using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using SimpleImprove.Core;

namespace SimpleImprove.Patches
{
    /// <summary>
    /// Optimized Harmony patch that adds SimpleImproveComp with efficient one-time checking.
    /// Uses GetGizmos with intelligent caching to avoid performance overhead.
    /// 
    /// Performance Features:
    /// - Each thing is processed exactly once using thingIDNumber cache
    /// - Multiple early exits minimize processing for irrelevant objects  
    /// - Adds components only when GetGizmos is first called (on-demand)
    /// - Cache prevents repeated expensive component checks
    /// </summary>
    [HarmonyPatch(typeof(ThingWithComps), "GetGizmos")]
    public static class DynamicComponentPatch
    {
        /// <summary>
        /// Cache of thing IDs that have been processed to avoid repeated work.
        /// Uses thingIDNumber which is unique and fast to access.
        /// </summary>
        private static readonly HashSet<int> processedThings = new HashSet<int>();

        /// <summary>
        /// Highly optimized prefix patch that adds SimpleImproveComp only once per thing.
        /// Multiple early exits ensure minimal performance impact.
        /// </summary>
        /// <param name="__instance">The ThingWithComps instance.</param>
        /// <returns>Always true to continue with original method.</returns>
        public static bool Prefix(ThingWithComps __instance)
        {
            // Early exit: Only process player-owned things
            if (__instance.Faction != Faction.OfPlayer)
                return true;

            // Early exit: Only process buildings and furniture
            if (__instance.def.category != ThingCategory.Building)
                return true;

            // Early exit: Skip if already processed
            var thingID = __instance.thingIDNumber;
            if (processedThings.Contains(thingID))
                return true;

            // Mark as processed immediately to avoid reprocessing
            processedThings.Add(thingID);

            // Check if this thing has CompQuality but not SimpleImproveComp
            var qualityComp = __instance.TryGetComp<CompQuality>();
            if (qualityComp == null)
                return true;

            var improveComp = __instance.TryGetComp<SimpleImproveComp>();
            if (improveComp != null)
                return true; // Already has the component

            // Must have blueprintDef to be improvable
            if (__instance.def.blueprintDef == null)
                return true;

            // Add the improvement component
            AddSimpleImproveComp(__instance);

            return true; // Continue with original GetGizmos method
        }

        /// <summary>
        /// Adds a SimpleImproveComp to the specified thing with proper initialization.
        /// </summary>
        /// <param name="thing">The thing to add the component to.</param>
        private static void AddSimpleImproveComp(ThingWithComps thing)
        {
            try
            {
                // Create the component properties
                var compProperties = new CompProperties_SimpleImprove();
                
                // Create the component
                var improveComp = new SimpleImproveComp();
                improveComp.props = compProperties;
                improveComp.parent = thing;

                // Add to the thing's component list
                thing.AllComps.Add(improveComp);

                // Initialize the component properly
                improveComp.Initialize(compProperties);
                
                // Call PostSpawnSetup if the thing is already spawned
                if (thing.Spawned)
                {
                    improveComp.PostSpawnSetup(false);
                }

                // Only log in dev mode to reduce spam
                if (Prefs.DevMode)
                {
                    Log.Message($"[SimpleImprove] Added component to {thing.def.defName}");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[SimpleImprove] Failed to add SimpleImproveComp to {thing.def.defName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears caches when a new game starts or loads to prevent issues with reused thing IDs.
        /// </summary>
        [HarmonyPatch(typeof(Game), "InitNewGame")]
        [HarmonyPatch(typeof(Game), "LoadGame")]
        public static class GameInitPatch
        {
            public static void Postfix()
            {
                processedThings.Clear();
                
                if (Prefs.DevMode)
                {
                    Log.Message("[SimpleImprove] Cleared caches for new game/load");
                }
            }
        }


    }
}