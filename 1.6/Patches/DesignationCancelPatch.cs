using HarmonyLib;
using Verse;
using Verse.AI;
using SimpleImprove.Core;

namespace SimpleImprove.Patches
{
    /// <summary>
    /// Harmony patch that handles cleanup when improvement designations are removed.
    /// Ensures materials are properly dropped and jobs are canceled when improvements are canceled.
    /// </summary>
    [HarmonyPatch(typeof(Designation), "Notify_Removing")]
    public static class DesignationCancelPatch
    {
        /// <summary>
        /// Harmony prefix method that intercepts designation removal notifications.
        /// Handles cleanup for improvement designations specifically.
        /// </summary>
        /// <param name="__instance">The designation being removed.</param>
        /// <returns>Always returns <c>true</c> to continue normal execution.</returns>
        public static bool Prefix(Designation __instance)
        {
            if (__instance.def == SimpleImproveDefOf.Designation_Improve && __instance.target.HasThing)
            {
                var improveComp = __instance.target.Thing.TryGetComp<SimpleImproveComp>();
                if (improveComp != null && improveComp.IsMarkedForImprovement)
                {
                    // Drop any stored materials
                    improveComp.GetDirectlyHeldThings().TryDropAll(
                        improveComp.parent.Position, 
                        improveComp.parent.Map, 
                        ThingPlaceMode.Near
                    );
                    
                    // Cancel any running improvement jobs for this building
                    CancelImprovementJobs(improveComp.parent);
                    
                    // Clear the improvement flag
                    // Note: Using reflection to directly set the private field to avoid
                    // recursive designation removal
                    var field = typeof(SimpleImproveComp).GetField("isMarkedForImprovement", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    field?.SetValue(improveComp, false);
                }
            }
            
            // Continue with normal execution
            return true;
        }
        
        /// <summary>
        /// Cancels any active improvement or hauling jobs targeting the specified building.
        /// Forces interruption of jobs to prevent pawns from continuing work on canceled improvements.
        /// </summary>
        /// <param name="target">The building whose improvement jobs should be canceled.</param>
        private static void CancelImprovementJobs(Thing target)
        {
            if (target?.Map?.mapPawns?.AllPawnsSpawned == null) return;
            
            foreach (var pawn in target.Map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.CurJob != null && 
                    (pawn.CurJob.def == SimpleImproveDefOf.Job_Improve || pawn.CurJob.def == SimpleImproveDefOf.Job_HaulToImprove) &&
                    (pawn.CurJob.targetA.Thing == target || pawn.CurJob.targetB.Thing == target))
                {
                    pawn.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
                }
            }
        }
    }
}