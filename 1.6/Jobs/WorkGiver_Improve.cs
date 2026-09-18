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
        /// The jobs <see cref="JobFor"/> has built and not yet handed out, for one pawn, one
        /// <c>forced</c> value and one tick.
        /// </summary>
        /// <remarks>
        /// An instance field on what is effectively a singleton. <c>WorkGiverDef.Worker</c> constructs
        /// one <see cref="WorkGiver"/> per def and caches it in an <c>[Unsaved]</c> field, so every
        /// pawn in the game shares this object, which is why the memo is keyed on the pawn rather
        /// than assumed to belong to one. <see cref="JobMemo{TAsker, TSubject, TAnswer}"/> carries the
        /// rules it keeps and why. It is built in the constructor rather than by an initialiser
        /// because it is handed two of this instance's members, <see cref="BuildJob"/> and the clear
        /// of <see cref="unreachableMaterials"/>.
        /// </remarks>
        private readonly JobMemo<Pawn, Thing, Job> memo;

        /// <summary>
        /// Material defs the search below already failed to find, under the memo's current key.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This shares <see cref="memo"/>'s key and is cleared whenever the memo is rekeyed, because
        /// its <c>Clear</c> is the memo's forget callback. That is what makes a bare <c>ThingDef</c>
        /// enough of a key: by the time anything reads it, the pawn, the <c>forced</c> value and the
        /// tick have already been established as current. The search
        /// root is the pawn's position, the map is the pawn's map, and the requested count never
        /// enters the search at all, so two calls for the same def under one key ask the same
        /// question.
        /// </para>
        /// <para>
        /// They do not always get the same answer, and an earlier version of this comment said they
        /// did. The tick does not move while the game is paused, so a miss recorded on one right-click
        /// is still served on the next one in the same paused tick even if the player unforbade a
        /// stack in between. That is vanilla's behaviour for the same search: vanilla's
        /// <c>noReachableResourceCache</c>, described below, is consulted on forced calls too, and
        /// <c>ItemAvailability.ThingsAvailableAnywhere</c>, which this method is only reached through,
        /// caches per tick and is cleared only by <c>ItemAvailability.Tick</c>. What the player sees
        /// is a "Missing" reason that is out of date until the game ticks, never a missing reason.
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
        /// Initializes a new instance of the <see cref="WorkGiver_Improve"/> class.
        /// </summary>
        /// <remarks>
        /// The game calls this once per def, through <c>Activator.CreateInstance</c> in
        /// <c>WorkGiverDef.Worker</c>. It binds the memo to this instance's job builder and to the
        /// clear of the unreachable-material cache, so that the memo decides both when a job is built
        /// and when that cache is dropped, and nothing in <see cref="JobFor"/> is left to get either
        /// wrong.
        /// </remarks>
        public WorkGiver_Improve()
        {
            memo = new JobMemo<Pawn, Thing, Job>(BuildJob, unreachableMaterials.Clear);
        }

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
        /// A job built here is held for the next call about the same building, and for the building
        /// a caller goes on to use, that call comes straight away: <c>JobGiver_Work</c> asks
        /// <see cref="JobOnThing"/> about the winner as soon as its scan picks one, and
        /// <c>FloatMenuOptionProvider_WorkGivers</c> asks it in the same expression as
        /// <see cref="HasJobOnThing"/>. That hand-over is what makes the pair agree, and all of it
        /// is <see cref="JobMemo{TAsker, TSubject, TAnswer}.Answer"/>: this method only supplies the
        /// tick, which the memo cannot read for itself, so that the hand-over can be run by the test
        /// suite where this method cannot.
        /// </para>
        /// <para>
        /// The memo is dropped whole whenever the pawn, the <c>forced</c> value or the tick changes,
        /// which bounds how long anything is held. It does not make a held answer true, and an
        /// earlier version of this comment claimed that within one tick the inputs a decision reads
        /// are stable. They are not while the game is paused. The tick does not move then, and the
        /// float menu, the one caller that passes <c>forced: true</c>, runs exactly then: it asks
        /// every provider when it opens and again every fourth frame while it is open
        /// (<c>FloatMenuMap.DoWindowContents</c>), and between two right-clicks the player can change
        /// the Work tab, the mod settings or a mark.
        /// </para>
        /// <para>
        /// So a refusal is never held. A refusal matters to the player only with its reason, the
        /// reason is written to <c>JobFailReason</c> as a side effect of building the answer, and
        /// the float menu never asks <see cref="JobOnThing"/> after a false
        /// <see cref="HasJobOnThing"/>, so a held null was never collected. The memo as first
        /// written held it, and the next right-click on the same building in the same paused tick
        /// got it back with no reason written, which the menu shows as no line at all. Every
        /// refusal is now built by the call that returns it, so every forced refusal writes its own
        /// reason, and <see cref="JobMemo{TAsker, TSubject, TAnswer}.Keep"/> carries why that beat
        /// remembering the reason alongside it.
        /// </para>
        /// </remarks>
        private Job JobFor(Pawn pawn, Thing thing, bool forced)
        {
            return memo.Answer(pawn, forced, Find.TickManager.TicksGame, thing);
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
        /// <para>
        /// The outstanding test comes first because it is the cheapest thing here that can refuse:
        /// two comp lookups, a bool and a comparison, against two dictionary lookups into the
        /// designation manager. The old order paid for both designation lookups on every candidate
        /// before asking the question that rejects most of them.
        /// </para>
        /// <para>
        /// It asks whether the mark still has work ahead of it, not merely whether there is a mark,
        /// and that has to happen before the haul branch below. This used to test the flag alone, so
        /// a building marked for any improvement and then raised to Legendary by something else was
        /// hauled for, worked and rolled on every cycle. At Legendary no roll can beat the current
        /// quality, so <c>CompleteImprovement</c> took its failure branch every time and, with
        /// materials required, destroyed the whole delivered cost without clearing the mark. A
        /// target the building had already reached cost the same on every roll that did not beat
        /// it. The refusal sets no <c>JobFailReason</c>, like vanilla's own no-work case in
        /// <c>WorkGiver_Repair</c>: there is nothing to do here rather than something in the way,
        /// and the building's own gizmo is the way out, the stranded cancel button at Legendary,
        /// whose description says why, or Cancel improvement in the ordinary menu otherwise.
        /// </para>
        /// <para>
        /// It refuses rather than clearing the mark. This runs as the scan validator, and clearing
        /// removes a designation, drops the staged materials and ends other pawns' jobs through the
        /// <c>Notify_Removing</c> prefix, none of which may happen from inside another pawn's job
        /// search or a float menu being built. <see cref="SimpleImproveComp.HasOutstandingImprovement"/>
        /// says where the mark is cleared instead.
        /// </para>
        /// <para>
        /// The rest of the order is what the player reads in the float menu, because each refusal
        /// writes its own reason or none, and the first refusal wins. The ideoligion is asked before
        /// the haul branch, because it is the one refusal that never clears. The four access checks
        /// are asked after the haul branch and after the work type test, because they write no
        /// reason, and asking them earlier would turn "No path", "Missing" and "Not assigned" into
        /// no line at all.
        /// </para>
        /// </remarks>
        private Job BuildJob(Pawn pawn, Thing thing, bool forced)
        {
            var improveComp = thing.TryGetComp<SimpleImproveComp>();
            if (improveComp == null || !improveComp.HasOutstandingImprovement)
                return null;

            // Not while something else is already going to take this building apart.
            if (thing.Map.designationManager.DesignationOn(thing, DesignationDefOf.Deconstruct) != null ||
                thing.Map.designationManager.DesignationOn(thing, DesignationDefOf.Uninstall) != null)
            {
                return null;
            }

            // The reservation is on the BUILDING, so it does not change from one material to the
            // next. It used to be asked once per material inside the loop and again at the bottom
            // of this method, and asking it once up here is the whole of that change. An earlier
            // version of this comment also said the old placement told the player the wrong thing,
            // "MissingMaterials" for a building another pawn held. No player could ever see that.
            //
            // Only the unforced background scan can be refused here because another pawn holds the
            // building, and nothing reads a fail reason on that path: JobGiver_Work never touches
            // JobFailReason, and the one caller that reads it after asking a work giver,
            // FloatMenuOptionProvider_WorkGivers, always passes forced: true. With forced, CanReserve
            // is asked with ignoreOtherReservations: true, which skips every test of another
            // claimant, so it refuses only an unspawned pawn, a pawn or target on another map, or a
            // destroyed target. A right-click on a building somebody else holds therefore gets the
            // job. Vanilla labels the option "Prioritize improving <building>: Reserved by <pawn>",
            // and taking it hands the job over (confirmed in game on 2026-09-18).
            //
            // So no reason is set here. The one that used to be set when forced could not describe
            // a reservation, and it was removed along with its key.
            if (!pawn.HasReserved(thing) && !pawn.CanReserve(thing, ignoreOtherReservations: forced))
            {
                return null;
            }

            // Before any haul, because this is the one refusal that never clears. It used to be asked
            // last, inside ImproveSite.CanWorkOn, so a colonist whose ideoligion forbids the building
            // (a pew, a kneel sheet, a slab bed) was handed a haul job for it by the background scan
            // and by a right-click, and carried the whole cost to something it could never improve.
            // Vanilla's delivery givers refuse that pawn, inside the CanConstruct call they make
            // before offering a delivery.
            //
            // Only this check moves. The four access checks stay below the haul branch, where the
            // readings they produce by writing no reason are the ones players see; ImproveSite
            // carries the detail.
            if (!ImproveSite.IdeoligionAllows(thing, pawn))
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

                    // Only the float menu ever reads this. JobFailReason has two readers in the
                    // game, FloatMenuOptionProvider_WorkGivers and CompTechprint's own menu, both
                    // clear the static before asking, and the first is the only caller that passes
                    // forced: true.
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
            // ImproveSite asks the same five questions of the same five vanilla methods; what it does
            // not do is route them through CanConstruct, which every vanilla caller hands a Blueprint
            // or a Frame and which this mod was handing a completed Building. ImproveSite carries why
            // that mattered and what dropping the call gives up. The four access questions are asked
            // here, in vanilla's order; the fifth, the ideoligion, was asked above the haul branch.
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
            if (!ImproveSite.CanAccess(thing, pawn, forced))
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
        /// <see cref="ImproveSite.CanAccess"/>, which already decides reachability to the building
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