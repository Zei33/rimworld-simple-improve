using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using SimpleImprove.Core;

namespace SimpleImprove.Jobs
{
    /// <summary>
    /// Work giver that identifies improvement tasks for pawns to perform.
    /// Handles both material hauling and actual improvement work based on current needs.
    /// </summary>
    public class WorkGiver_Improve : WorkGiver_Scanner
    {
        /// <summary>
        /// Gets the type of things this work giver can potentially work on.
        /// Targets artificial buildings that can have quality improvements.
        /// </summary>
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

        /// <summary>
        /// Gets the path end mode for reaching work targets.
        /// Uses Touch mode for direct interaction with buildings.
        /// </summary>
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        /// <summary>
        /// Determines if the specified pawn has a job to do on the given thing.
        /// Checks for improvement marking and excludes things being deconstructed or uninstalled.
        /// </summary>
        /// <param name="pawn">The pawn to check for available work.</param>
        /// <param name="thing">The thing to check for work availability.</param>
        /// <param name="forced">Whether this is a forced assignment.</param>
        /// <returns><c>true</c> if the pawn has work to do on the thing; otherwise, <c>false</c>.</returns>
        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            // Check if the thing is being deconstructed or uninstalled
            if (thing.Map.designationManager.DesignationOn(thing, DesignationDefOf.Deconstruct) != null ||
                thing.Map.designationManager.DesignationOn(thing, DesignationDefOf.Uninstall) != null)
            {
                return false;
            }

            var improveComp = thing.TryGetComp<SimpleImproveComp>();
            if (improveComp == null || !improveComp.IsMarkedForImprovement)
                return false;

            return JobOnThing(pawn, thing, forced) != null;
        }

        /// <summary>
        /// Gets the specific job that the pawn should do on the given thing.
        /// Prioritizes material hauling first, then actual improvement work based on requirements.
        /// </summary>
        /// <param name="pawn">The pawn to assign work to.</param>
        /// <param name="thing">The thing to work on.</param>
        /// <param name="forced">Whether this is a forced assignment.</param>
        /// <returns>A job for the pawn to perform, or null if no suitable job is available.</returns>
        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            var improveComp = thing.TryGetComp<SimpleImproveComp>();
            if (improveComp == null || !improveComp.IsMarkedForImprovement)
                return null;

            // Check if materials are needed (only if materials are required by settings)
            if (SimpleImproveMod.Settings.RequireMaterials)
            {
                var remainingMaterials = improveComp.GetRemainingMaterialCost();
                if (remainingMaterials.Any())
                {
                    foreach (var material in remainingMaterials)
                    {
                        if (pawn.Map.itemAvailability.ThingsAvailableAnywhere(material.thingDef, material.count, pawn))
                        {
                            var foundMaterial = FindClosestMaterial(pawn, material);
                            if (foundMaterial != null)
                            {
                                var haulJob = JobMaker.MakeJob(SimpleImproveDefOf.Job_HaulToImprove);
                                haulJob.targetA = foundMaterial;
                                haulJob.targetB = thing;
                                haulJob.count = material.count;
                                haulJob.haulMode = HaulMode.ToContainer;

                                if (pawn.HasReserved(thing) || pawn.CanReserve(thing, ignoreOtherReservations: forced))
                                {
                                    return haulJob;
                                }
                            }
                        }
                    }

                    JobFailReason.Is($"{"MissingMaterials".Translate(remainingMaterials.Select(m => $"{m.count}x {m.thingDef.label}").ToCommaList())}");
                    return null;
                }
            }

            // Check if pawn can do improvement work
            if (!pawn.workSettings.WorkIsActive(SimpleImproveDefOf.WorkType_Improving))
            {
                JobFailReason.Is("NotAssignedToWorkType".Translate(SimpleImproveDefOf.WorkType_Improving.gerundLabel).CapitalizeFirst());
                return null;
            }

            if (!GenConstruct.CanConstruct(thing, pawn, true, forced))
                return null;

            // Check skill requirement based on target quality
            var qualityComp = thing.TryGetComp<CompQuality>();
            if (qualityComp != null)
            {
                var targetQuality = improveComp.TargetQuality;
                
                // If no target quality is set (Any improvement), allow any skill level
                if (targetQuality.HasValue)
                {
                    var pawnSkill = pawn.skills.GetSkill(SkillDefOf.Construction).Level;
                    var requiredSkill = SimpleImproveMod.Settings.GetSkillRequirement(targetQuality.Value, pawn);
                    
                    if (requiredSkill > pawnSkill)
                    {
                        var baseRequiredSkill = SimpleImproveMod.Settings.GetSkillRequirement(targetQuality.Value);
                        
                        if (baseRequiredSkill > pawnSkill)
                        {
                            // Even with bonuses, skill is too low
                            if (ModsConfig.IdeologyActive)
                            {
                                JobFailReason.Is($"Skill too low for {targetQuality.Value.GetLabel()} target: need {requiredSkill} (or {baseRequiredSkill} with inspiration/role)");
                            }
                            else
                            {
                                JobFailReason.Is($"Skill too low for {targetQuality.Value.GetLabel()} target: need {requiredSkill} (or {baseRequiredSkill} with inspiration)");
                            }
                        }
                        else
                        {
                            // Skill is high enough with bonuses
                            if (ModsConfig.IdeologyActive)
                            {
                                JobFailReason.Is($"Need inspiration or production role for {targetQuality.Value.GetLabel()} target (skill {requiredSkill} required)");
                            }
                            else
                            {
                                JobFailReason.Is($"Need inspiration for {targetQuality.Value.GetLabel()} target (skill {requiredSkill} required)");
                            }
                        }
                        
                        return null;
                    }
                }
            }

            var improveJob = JobMaker.MakeJob(SimpleImproveDefOf.Job_Improve, thing);
            if (pawn.HasReserved(thing) || pawn.CanReserve(thing, ignoreOtherReservations: forced))
            {
                return improveJob;
            }

            return null;
        }

        /// <summary>
        /// Finds the closest material of the specified type that the pawn can access.
        /// Validates that the material is not forbidden and can be reserved by the pawn.
        /// </summary>
        /// <param name="pawn">The pawn that needs to access the material.</param>
        /// <param name="material">The material requirement to find.</param>
        /// <returns>The closest accessible material thing, or null if none is available.</returns>
        private Thing FindClosestMaterial(Pawn pawn, ThingDefCountClass material)
        {
            bool Validator(Thing thing)
            {
                if (thing.def != material.thingDef)
                    return false;

                if (thing.IsForbidden(pawn))
                    return false;

                if (!pawn.HasReserved(thing) && !pawn.CanReserve(thing))
                    return false;

                return true;
            }

            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(material.thingDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                Validator
            );
        }
    }
}