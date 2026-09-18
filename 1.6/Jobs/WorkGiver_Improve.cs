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
        /// Material defs the search below already failed to find, under the memo's current key.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This shares <see cref="memo"/>'s key and is cleared with it, which is what makes it sound
        /// with only a <c>ThingDef</c> for a key: by the time anything reads it, the pawn, the
        /// <c>forced</c> value and the tick have already been established as current, and those are
        /// the only other inputs the search has. The search root is the pawn's position, the map is
        /// the pawn's map, and the requested count never enters the search at all, so two calls for
        /// the same def under one key are the same call.
        /// </para>
        /// <para>
        /// This is vanilla's own answer to the same problem, kept deliberately narrow.
        /// <c>WorkGiver_ConstructDeliverResources</c> carries a per-tick
        /// <c>noReachableResourceCache</c> keyed on the pawn, the def and <c>forced</c>, and records
        /// a miss only when the search returned null. Its members are private static, so this mod
        /// cannot share them and needs its own.
        /// </para>
        /// <para>
        /// Successful finds are deliberately NOT cached, although the same argument would make it
        /// sound within a scan. A cached hit outlives the scan for the rest of the tick, and
        /// <c>Pawn_JobTracker.StartJob</c> reserves as it starts a job, so a pawn that scans twice in
        /// one tick could be handed a stack that was reserved between the two. The failure is benign
        /// and self-correcting, but it is a behaviour vanilla does not have, and the miss path is
        /// where the cost actually is: a build with nothing reachable searches every outstanding
        /// material for every marked building, while a build that finds something returns from the
        /// loop on the first hit.
        /// </para>
        /// </remarks>
        private readonly HashSet<ThingDef> unreachableMaterials = new HashSet<ThingDef>();

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
                unreachableMaterials.Clear();
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

            // The reservation is on the BUILDING, so it does not change from one material to the
            // next. Testing it inside the material loop was both wasteful and, more to the point,
            // the reason the player was told the wrong thing: a pawn that found the material but
            // could not reserve the target fell out of the loop and got "MissingMaterials", which
            // names a blocker that is not the blocker. It also gated the improve job separately at
            // the bottom of this method, so the same question was asked in two places.
            //
            // Narrow in practice, because the forced path passes ignoreOtherReservations: true, so
            // a player who right-clicks gets the job anyway. It is the background scan that was
            // silently mislabelling, and the float menu that was repeating it.
            if (!pawn.HasReserved(thing) && !pawn.CanReserve(thing, ignoreOtherReservations: forced))
            {
                if (forced)
                {
                    JobFailReason.Is("SimpleImprove_TargetReserved".Translate());
                }

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
                            var foundMaterial = FindClosestMaterial(pawn, material, forced);
                            if (foundMaterial != null)
                            {
                                var haulJob = JobMaker.MakeJob(SimpleImproveDefOf.Job_HaulToImprove);
                                haulJob.targetA = foundMaterial;
                                haulJob.targetB = thing;
                                haulJob.count = material.count;
                                haulJob.haulMode = HaulMode.ToContainer;

                                return haulJob;
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

                        // The two numbers used to be the wrong way round, and because a bonus can
                        // only ever subtract, the one offered as the improvement was always the
                        // larger. The greyed-out menu entry read "need <bonused> (or <unbonused>
                        // with inspiration/role)", so a player was told that being inspired would
                        // make the job HARDER.
                        //
                        // requiredSkill already includes whatever bonus this pawn currently has,
                        // because GetSkillRequirement was given the pawn. baseRequiredSkill is the
                        // same lookup with no pawn, so it is the unbonused figure and the one to
                        // lead with.
                        int baseRequiredSkill = SimpleImproveMod.Settings.GetSkillRequirement(targetQuality.Value);

                        // The old else-arm could not run. It was guarded on
                        // baseRequiredSkill > pawnSkill, and reaching this case at all means
                        // requiredSkill > pawnSkill, with baseRequiredSkill >= requiredSkill always,
                        // so the guard was a tautology and the two "Need inspiration for X" messages
                        // were dead in vanilla. They were also the wrong messages to want back: they
                        // said "you have the skill, you just need the bonus", which cannot be true
                        // here, since requiredSkill is already computed WITH this pawn's bonus.
                        //
                        // The real distinction, and the only one these numbers can support, is
                        // whether this pawn is getting a bonus at all. If they are and are still
                        // short, both figures are worth showing. If they are not, the two numbers
                        // are equal and printing both says nothing.
                        if (requiredSkill.Value < baseRequiredSkill)
                        {
                            JobFailReason.Is("SimpleImprove_SkillTooLowDespiteBonus".Translate(
                                targetQuality.Value.GetLabel(), baseRequiredSkill, requiredSkill.Value));
                        }
                        else
                        {
                            JobFailReason.Is("SimpleImprove_SkillTooLow".Translate(
                                targetQuality.Value.GetLabel(), baseRequiredSkill));
                        }

                        return null;
                    }
            }

            return JobMaker.MakeJob(SimpleImproveDefOf.Job_Improve, thing);
        }

        /// <summary>
        /// Finds the closest material of the specified type that the pawn can access.
        /// Validates that the material is not forbidden and can be reserved by the pawn.
        /// </summary>
        /// <param name="pawn">The pawn that needs to access the material.</param>
        /// <param name="material">The material requirement to find.</param>
        /// <param name="forced">Whether the player is prioritising this by hand.</param>
        /// <returns>The closest accessible material thing, or null if none is available.</returns>
        /// <remarks>
        /// <para>
        /// The danger threshold used to be the bare <c>TraverseParms.For(pawn)</c>, and the thing to
        /// understand before reading this as a tightening-for-its-own-sake is that the bare overload
        /// is not "no danger set". Its <c>maxDanger</c> default is <c>Danger.Deadly</c>, the loosest
        /// value there is, so this work giver was accepting material that only a deadly-danger route
        /// reaches while its own target search used the normal threshold. During a toxic fallout or a
        /// fire a pawn could be handed a haul job toward a stack the giver's own danger policy would
        /// have refused for the building.
        /// </para>
        /// <para>
        /// Cite two precedents for the new form rather than "vanilla", because vanilla is not
        /// consistent here: the bare call outnumbers the danger-carrying one in the game's own
        /// assembly, and several shipped haul givers use the Deadly default quite happily. The two
        /// that matter are <c>WorkGiver_ConstructDeliverResources</c>, which is the closest vanilla
        /// analogue and spells this exactly, and this mod's own
        /// <see cref="ImproveSite.CanWorkOn"/>, which already decides reachability to the building
        /// this way. Those two are what makes the old form an inconsistency inside one job rather
        /// than a style choice.
        /// </para>
        /// <para>
        /// What this does NOT do is stop a pawn walking through fire.
        /// <c>maxDanger</c> gates job assignment only; once a job is issued
        /// <c>Pawn_PathFollower</c> builds its path at <c>Danger.Deadly</c> regardless. The change is
        /// about which jobs are offered, and it is player-visible in the tightening direction, so it
        /// belongs in the release notes.
        /// </para>
        /// <para>
        /// <c>9999f</c> is kept. An earlier internal note called it too high; it is what
        /// <c>WorkGiver_ConstructDeliverResources</c> passes and it is also <c>GenClosest</c>'s own
        /// default, so lowering it would make pawns refuse distant material that vanilla
        /// construction accepts.
        /// </para>
        /// <para>
        /// The reservation test now passes <c>ignoreOtherReservations</c> the same way the two tests
        /// on the building itself already did. Without it a right-click prioritise would widen the
        /// danger threshold and the building's reservation but still refuse a stack another pawn had
        /// reserved, so the forced path contradicted itself inside one job.
        /// </para>
        /// </remarks>
        private Thing FindClosestMaterial(Pawn pawn, ThingDefCountClass material, bool forced)
        {
            if (unreachableMaterials.Contains(material.thingDef))
            {
                return null;
            }

            bool Validator(Thing thing)
            {
                if (thing.def != material.thingDef)
                    return false;

                if (thing.IsForbidden(pawn))
                    return false;

                if (!pawn.HasReserved(thing) && !pawn.CanReserve(thing, ignoreOtherReservations: forced))
                    return false;

                return true;
            }

            Thing found = GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(material.thingDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn, forced ? Danger.Deadly : pawn.NormalMaxDanger()),
                9999f,
                Validator
            );

            if (found == null)
            {
                unreachableMaterials.Add(material.thingDef);
            }

            return found;
        }
    }
}