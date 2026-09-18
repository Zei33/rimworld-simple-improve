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
        /// The pawn the memo below was built for, or <c>null</c> when it holds nothing.
        /// </summary>
        /// <remarks>
        /// These four fields are instance fields on what is effectively a singleton.
        /// <c>WorkGiverDef.Worker</c> constructs one <see cref="WorkGiver"/> per def and caches it in
        /// an <c>[Unsaved]</c> field, so every pawn in the game shares this object. That is why the
        /// memo is keyed on the pawn rather than assumed to belong to one.
        /// </remarks>
        private Pawn memoPawn;

        /// <summary>The <c>forced</c> value the memo was built for.</summary>
        private bool memoForced;

        /// <summary>The game tick the memo was built on, or -1 when it holds nothing.</summary>
        private int memoTick = -1;

        /// <summary>The job decided for each thing asked about, including the null decisions.</summary>
        private readonly Dictionary<Thing, Job> memo = new Dictionary<Thing, Job>();

        /// <summary>
        /// Determines if the specified pawn has a job to do on the given thing.
        /// </summary>
        /// <param name="pawn">The pawn to check for available work.</param>
        /// <param name="thing">The thing to check for work availability.</param>
        /// <param name="forced">Whether this is a forced assignment.</param>
        /// <returns><c>true</c> if the pawn has work to do on the thing; otherwise, <c>false</c>.</returns>
        /// <remarks>
        /// <para>
        /// This still answers by building the job, and that is deliberate rather than an omission.
        /// The issue asked for a cheap field-test predicate here, and a cheap predicate that is looser
        /// than <see cref="JobOnThing"/> in any respect is not a wasted call, it is a colony-wide
        /// work stoppage. <c>JobGiver_Work</c> uses this as the scan validator and then calls
        /// <see cref="JobOnThing"/> on the winner with nothing in between that could re-check it. When
        /// the two disagree, <c>JobGiver_Work</c> logs once and then, critically, leaves
        /// <c>bestTargetOfLastPriority</c> and <c>scannerWhoProvidedTarget</c> set. The loop breaks at
        /// the next <c>priorityInType</c> boundary and returns <c>NoJob</c>, and
        /// <c>workGiversInOrderNormal</c> is one flat list across every enabled work type, so the pawn
        /// abandons cleaning, hauling and cooking too, on every job search, for as long as one marked
        /// building cannot be worked.
        /// </para>
        /// <para>
        /// The diagnostic for that is worse than useless. It is <c>Log.ErrorOnce</c> on the literal key
        /// 6112651, which every work giver in the game shares, and <c>Log.Clear</c> does not reset
        /// <c>usedKeys</c>. Whichever mod desyncs first in a session consumes the key and every later
        /// desync is silent, so "no red error in testing" is not evidence of anything.
        /// </para>
        /// <para>
        /// What the issue actually named, the double evaluation, is removed instead by
        /// <see cref="JobFor"/>: both methods answer from one computation, so the scan costs one job
        /// construction per candidate rather than one per candidate plus one more for the winner, and
        /// the two cannot disagree because there is only one answer. The expensive part of that
        /// construction, the material search, is separately bounded by a per-tick cache in
        /// <see cref="FindClosestMaterial"/>.
        /// </para>
        /// <para>
        /// The two cheap tests that used to sit here have moved into <see cref="BuildJob"/> so that
        /// this method and <see cref="JobOnThing"/> apply exactly the same ones. Keeping the
        /// deconstruct and uninstall test here alone made this method stricter than
        /// <see cref="JobOnThing"/>, which is the safe direction but still a disagreement, and a
        /// disagreement in a method pair whose whole contract is that they agree is worth removing
        /// even when it is currently harmless.
        /// </para>
        /// </remarks>
        public override bool HasJobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            return JobFor(pawn, thing, forced) != null;
        }

        /// <summary>
        /// Answers the job question once per pawn, thing, <c>forced</c> value and tick.
        /// </summary>
        /// <param name="pawn">The pawn asking.</param>
        /// <param name="thing">The building being asked about.</param>
        /// <param name="forced">Whether the player is prioritising this by hand.</param>
        /// <returns>The job to do, or <c>null</c> when there is none.</returns>
        /// <remarks>
        /// <para>
        /// The memo is dropped whole whenever the pawn, the <c>forced</c> value or the tick changes,
        /// which covers every way the answer could have moved. Within one tick the inputs a decision
        /// reads are stable: the scan makes no reservations, <c>CanReserve</c> is a query, and the
        /// search root is the pawn's position, which cannot change mid-scan.
        /// </para>
        /// <para>
        /// A hit REMOVES the entry rather than leaving it, and that is a correctness requirement, not
        /// tidiness. <c>JobMaker.MakeJob</c> hands out pooled <c>Job</c> objects from
        /// <c>SimplePool&lt;Job&gt;</c>, and <c>Pawn_JobTracker</c> returns them to that pool when it
        /// declines or finishes one. The only job that ever leaves this class is the one a hit
        /// returns, so removing it on the way out means nothing the memo still holds can be recycled
        /// underneath it. Entries for candidates that did not win are never handed to anybody, so they
        /// are never pooled, and they fall away at the next key change.
        /// </para>
        /// <para>
        /// One consequence worth stating because it looks like a bug. On the float menu path,
        /// <c>FloatMenuOptionProvider_WorkGivers</c> calls <see cref="HasJobOnThing"/> and then
        /// <see cref="JobOnThing"/> in a single expression, so the second call is a memo hit and does
        /// not re-run <see cref="BuildJob"/>, and therefore does not re-set
        /// <c>JobFailReason</c>. The reason set during the first call is still standing, because that
        /// provider clears the reason once per work giver BEFORE the pair and reads it after, and
        /// nothing in between clears it. Do not "fix" this by re-setting the reason on a hit.
        /// </para>
        /// </remarks>
        private Job JobFor(Pawn pawn, Thing thing, bool forced)
        {
            int tick = Find.TickManager.TicksGame;

            if (memoPawn != pawn || memoForced != forced || memoTick != tick)
            {
                memo.Clear();
                memoPawn = pawn;
                memoForced = forced;
                memoTick = tick;
            }
            else if (memo.TryGetValue(thing, out Job remembered))
            {
                memo.Remove(thing);
                return remembered;
            }

            Job job = BuildJob(pawn, thing, forced);
            memo[thing] = job;
            return job;
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
            return JobFor(pawn, thing, forced);
        }

        /// <summary>
        /// Decides the job for one building, with no memoisation of its own.
        /// </summary>
        /// <param name="pawn">The pawn to assign work to.</param>
        /// <param name="thing">The thing to work on.</param>
        /// <param name="forced">Whether this is a forced assignment.</param>
        /// <returns>A job for the pawn to perform, or null if no suitable job is available.</returns>
        /// <remarks>
        /// The marked test comes first because it is the cheapest thing here that can refuse: one
        /// comp lookup and a bool, against two dictionary lookups into the designation manager. The
        /// old order paid for both designation lookups on every candidate before asking the question
        /// that rejects most of them.
        /// </remarks>
        private Job BuildJob(Pawn pawn, Thing thing, bool forced)
        {
            var improveComp = thing.TryGetComp<SimpleImproveComp>();
            if (improveComp == null || !improveComp.IsMarkedForImprovement)
                return null;

            // Not while something else is already going to take this building apart.
            if (thing.Map.designationManager.DesignationOn(thing, DesignationDefOf.Deconstruct) != null ||
                thing.Map.designationManager.DesignationOn(thing, DesignationDefOf.Uninstall) != null)
            {
                return null;
            }

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

                    // Only the float menu ever reads this. FloatMenuOptionProvider_WorkGivers is
                    // the only consumer of JobFailReason in the game, it clears the static once per
                    // work giver before asking, and it is the only caller that passes forced: true.
                    // JobGiver_Work never mentions JobFailReason at all, so a reason built during a
                    // background scan is written to a static nobody reads and then overwritten. The
                    // guard turns that into no work rather than into a formatted, translated,
                    // comma-listed string per marked building per pawn per job search.
                    if (forced)
                    {
                        JobFailReason.Is($"{"MissingMaterials".Translate(remainingMaterials.Select(m => $"{m.count}x {m.thingDef.label}").ToCommaList())}");
                    }

                    return null;
                }
            }

            // Check if pawn can do improvement work. ImproveWorkers carries why this is not a bare
            // workSettings.WorkIsActive call.
            if (!ImproveWorkers.IsAssignedToImproving(pawn))
            {
                if (forced)
                {
                    JobFailReason.Is("NotAssignedToWorkType".Translate(SimpleImproveDefOf.WorkType_Improving.gerundLabel).CapitalizeFirst());
                }

                return null;
            }

            // This used to be GenConstruct.CanConstruct(thing, pawn, checkSkills: false, forced).
            // ImproveSite.CanWorkOn asks the same five questions of the same five vanilla methods in
            // the same order; what it does not do is route them through CanConstruct, which every
            // vanilla caller hands a Blueprint or a Frame and which this mod was handing a completed
            // Building. ImproveSite carries why that mattered and what dropping the call gives up.
            //
            // The skill prerequisite is still bypassed, and still deliberately. checkSkills gated
            // ThingDef.constructionSkillPrerequisite, which exists to gate BUILDING a thing from
            // scratch rather than working on one that already exists. Leaving it on made improvement
            // inherit the build requirement: a DiningChair declares 4 and an Armchair 5, so a
            // Construction 3 pawn was refused with "Construction skill too low" while a Stool, which
            // declares none, was accepted. That is the chair bug users reported, confirmed in game on
            // 2026-09-17 as a clean staircase across Stool/DiningChair/Armchair at Construction 3, 4
            // and 5. It cannot come back by accident now, because the block that read it is not
            // transcribed at all rather than switched off by an argument that is easy to transpose.
            //
            // Vanilla's own work giver for an already-built Building, RimWorld.WorkGiver_Repair,
            // never calls CanConstruct either and imposes no such prerequisite. This mod's own skill
            // model, keyed to the target quality, is just below and is the gate that should apply.
            if (!ImproveSite.CanWorkOn(thing, pawn, forced))
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
                    if (forced)
                    {
                        JobFailReason.Is("SimpleImprove_NoConstructionSkill".Translate(pawn.LabelShort));
                    }

                    return null;

                case ImproveSkillBlocker.SkillTooLow:
                    {
                        if (!forced)
                        {
                            return null;
                        }

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