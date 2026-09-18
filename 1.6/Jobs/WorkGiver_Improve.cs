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
        /// Gets the path end mode for reaching work targets.
        /// Uses Touch mode for direct interaction with buildings.
        /// </summary>
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        // PotentialWorkThingRequest is deliberately NOT overridden, and leaving it at the Undefined
        // default is half of the performance fix rather than an omission.
        //
        // GenClosest.ClosestThingReachable decides whether to run its 30-region breadth-first search
        // from the request alone: `!thingReq.IsUndefined && thingReq.CanBeFoundInRegion`. Declaring
        // ThingRequestGroup.BuildingArtificial, as this class used to, therefore bought a region walk
        // calling the validator on every wall segment, door and frame in range, and supplying
        // PotentialWorkThingsGlobal alongside it would not have removed that walk. The set would only
        // have become the conditional fallback after it.
        //
        // The no-work case was the expensive one, which is what made this the mod's most-reported
        // problem: GenClosest.ValidateThing only shrinks closestDistSquared for a candidate the
        // validator accepted, so with nothing marked the distance cull never engaged and every
        // building on the map was fed through. Marking something made the scan cheaper.
        //
        // Undefined makes ThingRequest.Accepts return false for everything, which sounds like it
        // would break right-click prioritise and does not.
        // FloatMenuOptionProvider_WorkGivers.ScannerShouldSkip tests
        // `Accepts(t) || (PotentialWorkThingsGlobal(pawn) != null && PotentialWorkThingsGlobal(pawn).Contains(t))`,
        // so the set below is what the menu matches against instead. That is why the set must stay
        // exactly the designated buildings: narrowing it would remove menu options too.

        /// <summary>
        /// Skips this work giver outright when nothing on the map is marked for improvement.
        /// </summary>
        /// <param name="pawn">The pawn looking for work.</param>
        /// <param name="forced">Whether the player is prioritising this by hand.</param>
        /// <returns><c>true</c> when there is no improvement work anywhere on the map.</returns>
        /// <remarks>
        /// <para>
        /// This is the cheap exit the class did not have. <c>JobGiver_Work.PawnCanUseWorkGiver</c>
        /// calls it as the fourth of six gates, before the scanner cast, before
        /// <see cref="PotentialWorkThingsGlobal"/> is read and before any search is constructed, so
        /// returning true here costs one walk of a single designation bucket and nothing else. It is
        /// the same shape as all twenty vanilla uses of
        /// <c>AnySpawnedDesignationOfDef</c>, every one of which sits in a <c>ShouldSkip</c>.
        /// </para>
        /// <para>
        /// <paramref name="forced"/> is ignored on purpose. With no designation on the map there is no
        /// work whoever asks, and this cannot suppress a right-click that would otherwise have worked:
        /// the menu only reaches a scanner through the set in
        /// <see cref="PotentialWorkThingsGlobal"/>, which is derived from the same designations, so a
        /// building that can be clicked is a building that makes this return false.
        /// </para>
        /// <para>
        /// <c>pawn.Map</c> is not guarded, matching all twenty vanilla call sites. Both callers reach
        /// here with a spawned pawn: the think tree runs on a spawned pawn, and the float menu rejects
        /// a pawn whose map is not the current one before any provider runs.
        /// </para>
        /// <para>
        /// The designations this counts are narrowed to the pawn's own map by vanilla, which matters
        /// once a building can leave one. <c>AnySpawnedDesignationOfDef</c> tests
        /// <c>!target.HasThing || target.Thing.Map == map</c>, so a designation stranded on the map a
        /// gravship left cannot hold this open for the map it arrived at.
        /// </para>
        /// </remarks>
        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return !pawn.Map.designationManager.AnySpawnedDesignationOfDef(SimpleImproveDefOf.Designation_Improve);
        }

        /// <summary>
        /// Gets the buildings this work giver will consider, which is exactly the marked ones.
        /// </summary>
        /// <param name="pawn">The pawn looking for work.</param>
        /// <returns>The buildings currently marked for improvement on the pawn's map.</returns>
        /// <remarks>
        /// With <c>PotentialWorkThingRequest</c> left undefined, this is the whole search set rather
        /// than a fallback after a region walk. <see cref="ImproveDesignations.ScanTargets"/> carries
        /// why it is materialised into a list and why the null test in it is load-bearing.
        /// </remarks>
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return ImproveDesignations.ScanTargets(
                pawn.Map.designationManager.SpawnedDesignationsOfDef(SimpleImproveDefOf.Designation_Improve));
        }

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

            // Check if pawn can do improvement work. ImproveWorkers carries why this is not a bare
            // workSettings.WorkIsActive call.
            if (!ImproveWorkers.IsAssignedToImproving(pawn))
            {
                JobFailReason.Is("NotAssignedToWorkType".Translate(SimpleImproveDefOf.WorkType_Improving.gerundLabel).CapitalizeFirst());
                return null;
            }

            // checkSkills is deliberately false. It gates ThingDef.constructionSkillPrerequisite,
            // which exists to gate BUILDING a thing from scratch, not working on one that already
            // exists. Leaving it true made improvement inherit the build requirement: a DiningChair
            // declares 4 and an Armchair 5, so a Construction 3 pawn was refused with "Construction
            // skill too low" while a Stool, which declares none, was accepted. That is the chair bug
            // users reported. Confirmed in game on 2026-09-17 as a clean staircase across
            // Stool/DiningChair/Armchair at Construction 3, 4 and 5.
            //
            // Vanilla's own work giver for an already-built Building, RimWorld.WorkGiver_Repair,
            // never calls CanConstruct at all and imposes no such prerequisite. This mod has its own
            // skill model keyed to the target quality, just below, which is the gate that should
            // apply. Note the third parameter is checkSkills, not forced; they are easy to transpose.
            //
            // Everything else CanConstruct does is still wanted and still runs: FirstBlockingThing
            // (so a pawn sitting on the furniture still blocks it), reachability, reservation,
            // burning and the Ideology building restriction.
            if (!GenConstruct.CanConstruct(thing, pawn, checkSkills: false, forced: forced))
                return null;

            // The skill gate. Both halves live in WorkerSkill.FirstBlocker so their order is a
            // decision the test suite can see: an unreadable skill is refused whether or not a target
            // quality is set, because the improvement ends at
            // QualityUtility.GenerateQualityCreatedByPawn, which reads
            // `pawn.RaceProps.IsMechanoid ? mechFixedSkillLevel : pawn.skills.GetSkill(...).Level`
            // with no null guard on the second branch. A modded drone race is exactly that shape and
            // would throw inside vanilla rather than here.
            //
            // A null requirement means the player marked the building for any improvement at all, in
            // which case no particular quality is being aimed at and any readable skill will do.
            WorkerSkill workerSkill = WorkerSkill.Of(pawn);
            var qualityComp = thing.TryGetComp<CompQuality>();
            var targetQuality = qualityComp != null ? improveComp.TargetQuality : null;
            int? requiredSkill = targetQuality.HasValue
                ? SimpleImproveMod.Settings.GetSkillRequirement(targetQuality.Value, pawn)
                : (int?)null;

            switch (WorkerSkill.FirstBlocker(workerSkill, requiredSkill))
            {
                case ImproveSkillBlocker.NoConstructionSkill:
                    JobFailReason.Is("SimpleImprove_NoConstructionSkill".Translate(pawn.LabelShort));
                    return null;

                case ImproveSkillBlocker.SkillTooLow:
                    {
                        var pawnSkill = workerSkill.Level;
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