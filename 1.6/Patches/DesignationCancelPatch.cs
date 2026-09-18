using HarmonyLib;
using Verse;
using Verse.AI;
using SimpleImprove.Core;

namespace SimpleImprove.Patches
{
    /// <summary>
    /// Harmony patch that handles cleanup when improvement designations are removed.
    /// Ensures materials are returned and jobs are cancelled when improvements are cancelled.
    /// </summary>
    /// <remarks>
    /// This is the cleanup for a building that keeps standing and only loses its mark, which is what
    /// the mod's own cancel gizmo and designator do and what vanilla's <c>Designator_Cancel</c> does.
    /// It is also reached, with the building already despawned, from every destroy, every uninstall,
    /// every minify and every map removal, because all four remove designations after the despawn.
    /// It deliberately returns nothing to the map in those cases: <see cref="Core.StoredMaterials"/>
    /// explains why <c>SimpleImproveComp.PostDeSpawn</c> owns them instead.
    /// </remarks>
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
                    // The component decides whether a drop is possible rather than this doing it
                    // unconditionally, which is what used to put a red error on the log for every
                    // stack staged in a building that was being deconstructed. See
                    // StoredMaterials.OnUnmark for the four ways this is reached with the target
                    // already despawned, and PostDeSpawn for who returns the materials in those.
                    improveComp.ReturnStoredMaterialsWhileSpawned();

                    // Cancel any running improvement jobs for this building
                    CancelImprovementJobs(improveComp.parent);

                    // Clear the improvement flag directly without triggering setter
                    // to avoid recursive designation removal
                    improveComp.SetMarkedForImprovementDirect(false);
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