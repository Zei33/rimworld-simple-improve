using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using SimpleImprove.Core;

namespace SimpleImprove.Patches
{
    /// <summary>
    /// Harmony patch that enables merging the Improve work type into Construction.
    /// This patch dynamically modifies work type assignments at runtime based on settings.
    /// </summary>
    [HarmonyPatch]
    public static class WorkTypeMergePatch
    {
        /// <summary>
        /// Cached reference to the Construction work type for performance.
        /// </summary>
        private static WorkTypeDef constructionWorkType;
        
        /// <summary>
        /// Cached reference to the Improving work type for performance.
        /// </summary>
        private static WorkTypeDef improvingWorkType;
        
        /// <summary>
        /// Flag to track if the work type has been merged to avoid redundant operations.
        /// </summary>
        private static bool workTypeMerged = false;
        
        /// <summary>
        /// Flag to track initialization state to ensure proper timing.
        /// </summary>
        private static bool initialized = false;

        /// <summary>
        /// Initializes cached work type references.
        /// Called during mod startup to cache references.
        /// </summary>
        public static void Initialize()
        {
            if (initialized) return;
            
            try
            {
                constructionWorkType = WorkTypeDefOf.Construction;
                improvingWorkType = SimpleImproveDefOf.WorkType_Improving;
                
                if (constructionWorkType == null)
                {
                    Log.Error("[SimpleImprove] Could not find Construction work type");
                    return;
                }
                
                if (improvingWorkType == null)
                {
                    Log.Error("[SimpleImprove] Could not find Improving work type");
                    return;
                }
                
                initialized = true;
                
                // Apply initial merge state based on settings
                UpdateWorkTypeMergeState();
                
                if (Prefs.DevMode)
                {
                    Log.Message("[SimpleImprove] Work type merging system initialized successfully");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[SimpleImprove] Error initializing work type merging: {ex}");
            }
        }
        
        /// <summary>
        /// Updates the work type merge state based on current settings.
        /// This method should be called when settings change.
        /// </summary>
        public static void UpdateWorkTypeMergeState()
        {
            if (!initialized || constructionWorkType == null || improvingWorkType == null)
                return;
                
            bool shouldMerge = SimpleImproveMod.Settings?.MergeIntoConstruction ?? false;
            
            if (shouldMerge && !workTypeMerged)
            {
                MergeWorkTypes();
            }
            else if (!shouldMerge && workTypeMerged)
            {
                SeparateWorkTypes();
            }
        }
        
        /// <summary>
        /// Merges the Improve work type into Construction by modifying work giver assignments.
        /// </summary>
        private static void MergeWorkTypes()
        {
            if (workTypeMerged) return;
            
            try
            {
                // Find the WorkGiver_Improve definition
                var improveWorkGiver = DefDatabase<WorkGiverDef>.GetNamed("WorkGiver_Improve", false);
                if (improveWorkGiver == null) 
                {
                    Log.Warning("[SimpleImprove] Could not find WorkGiver_Improve definition for merging");
                    return;
                }
            
                // Change the work type reference to Construction
                improveWorkGiver.workType = constructionWorkType;
                
                // Remove from improving work type's list if present
                if (improvingWorkType.workGiversByPriority.Contains(improveWorkGiver))
                {
                    improvingWorkType.workGiversByPriority.Remove(improveWorkGiver);
                }
                
                // Add to construction work type's list if not already present
                if (!constructionWorkType.workGiversByPriority.Contains(improveWorkGiver))
                {
                    // Insert at appropriate priority position (after existing construction work givers)
                    var insertIndex = constructionWorkType.workGiversByPriority.Count;
                    constructionWorkType.workGiversByPriority.Insert(insertIndex, improveWorkGiver);
                }
                
                // Hide the Improving work type column when merged
                improvingWorkType.visible = false;
                
                // Mark all pawns' work settings as dirty to rebuild work giver caches
                InvalidateAllPawnWorkSettings();
                
                // Force work tab UI refresh to reflect visibility changes
                ForceWorkTabRefresh();
                
                workTypeMerged = true;
                
                if (Prefs.DevMode)
                {
                    Log.Message("[SimpleImprove] Merged Improve work type into Construction and hid work column");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[SimpleImprove] Error merging work types: {ex}");
            }
        }
        
        /// <summary>
        /// Separates the Improve work type from Construction by restoring original assignments.
        /// </summary>
        private static void SeparateWorkTypes()
        {
            if (!workTypeMerged) return;
            
            try
            {
                // Find the WorkGiver_Improve definition
                var improveWorkGiver = DefDatabase<WorkGiverDef>.GetNamed("WorkGiver_Improve", false);
                if (improveWorkGiver == null) 
                {
                    Log.Warning("[SimpleImprove] Could not find WorkGiver_Improve definition for separation");
                    return;
                }
            
                // Restore original work type reference
                improveWorkGiver.workType = improvingWorkType;
                
                // Remove from construction work type's list
                if (constructionWorkType.workGiversByPriority.Contains(improveWorkGiver))
                {
                    constructionWorkType.workGiversByPriority.Remove(improveWorkGiver);
                }
                
                // Add back to improving work type's list if not already present
                if (!improvingWorkType.workGiversByPriority.Contains(improveWorkGiver))
                {
                    improvingWorkType.workGiversByPriority.Add(improveWorkGiver);
                }
                
                // Show the Improving work type column when separated
                improvingWorkType.visible = true;
                
                // Mark all pawns' work settings as dirty to rebuild work giver caches
                InvalidateAllPawnWorkSettings();
                
                // Force work tab UI refresh to reflect visibility changes
                ForceWorkTabRefresh();
                
                workTypeMerged = false;
                
                if (Prefs.DevMode)
                {
                    Log.Message("[SimpleImprove] Separated Improve work type from Construction and restored work column");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[SimpleImprove] Error separating work types: {ex}");
            }
        }
        
        /// <summary>
        /// Invalidates work settings for all pawns to force cache rebuilding.
        /// This ensures that work giver lists are updated with the new assignments.
        /// </summary>
        private static void InvalidateAllPawnWorkSettings()
        {
            try
            {
                // Get all maps and invalidate pawn work settings
                if (Current.Game?.Maps != null)
                {
                    foreach (var map in Current.Game.Maps)
                    {
                        if (map.mapPawns?.AllPawnsSpawned != null)
                        {
                            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                            {
                                if (pawn.workSettings != null)
                                {
                                    // Access the private workGiversDirty field using reflection
                                    var workGiversDirtyField = typeof(Pawn_WorkSettings).GetField("workGiversDirty", 
                                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                    workGiversDirtyField?.SetValue(pawn.workSettings, true);
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[SimpleImprove] Error invalidating pawn work settings: {ex}");
            }
        }
        
        /// <summary>
        /// Forces the work tab UI to refresh by calling SetDirty on the work table.
        /// This ensures that column visibility changes are immediately reflected in the UI.
        /// </summary>
        private static void ForceWorkTabRefresh()
        {
            try
            {
                // Clear the cached visibility state on the work type to force a refresh
                if (improvingWorkType != null)
                {
                    var cachedFrameVisibleCurrentlyField = typeof(WorkTypeDef).GetField("cachedFrameVisibleCurrently", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    cachedFrameVisibleCurrentlyField?.SetValue(improvingWorkType, -1);
                }
                
                // If the work tab is currently open, force it to refresh
                var workWindow = Find.WindowStack?.WindowOfType<MainTabWindow_Work>();
                if (workWindow != null)
                {
                    // Use reflection to call the protected SetDirty method
                    var setDirtyMethod = typeof(MainTabWindow_PawnTable).GetMethod("SetDirty", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    setDirtyMethod?.Invoke(workWindow, null);
                    
                    if (Prefs.DevMode)
                    {
                        Log.Message("[SimpleImprove] Forced work tab refresh via SetDirty");
                    }
                }
                else if (Prefs.DevMode)
                {
                    Log.Message("[SimpleImprove] Work tab not currently open, cleared visibility cache only");
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[SimpleImprove] Error refreshing work tab: {ex}");
            }
        }
        
        // Note: Property getter patch removed - direct field modification is sufficient
        
        // Initialization is now handled manually from ModEntry
    }
    
    /// <summary>
    /// Extension patch for settings changes to update work type merging in real-time.
    /// </summary>
    [HarmonyPatch(typeof(SimpleImproveSettings), nameof(SimpleImproveSettings.ExposeData))]
    public static class SettingsChangePatch
    {
        /// <summary>
        /// Updates work type merging when settings are loaded.
        /// </summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Update merge state when settings are loaded
            LongEventHandler.ExecuteWhenFinished(() => {
                WorkTypeMergePatch.UpdateWorkTypeMergeState();
            });
        }
    }
}