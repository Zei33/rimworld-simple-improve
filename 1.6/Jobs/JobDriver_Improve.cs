using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using SimpleImprove.Core;

namespace SimpleImprove.Jobs
{
    /// <summary>
    /// Job driver for performing improvement work on buildings and furniture.
    /// Handles the actual construction work that improves an item's quality.
    /// </summary>
    public class JobDriver_Improve : JobDriver
    {
        /// <summary>
        /// Gets the SimpleImprove component of the target thing.
        /// Provides convenient access to the improvement functionality.
        /// </summary>
        private SimpleImproveComp TargetComp 
        {
            get
            {
                var thing = job.GetTarget(TargetIndex.A).Thing;
                return thing?.TryGetComp<SimpleImproveComp>();
            }
        }

        /// <summary>
        /// Attempts to make reservations for the job before it starts.
        /// Reserves the target building to prevent other pawns from interfering.
        /// </summary>
        /// <param name="errorOnFailed">Whether to log an error if reservation fails.</param>
        /// <returns><c>true</c> if reservations were successful; otherwise, <c>false</c>.</returns>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (pawn.HasReserved(job.targetA, job))
                return true;

            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        /// <summary>
        /// Creates the sequence of toils (work steps) for the improvement job.
        /// Sets up movement, work execution, and completion handling.
        /// </summary>
        /// <returns>An enumerable sequence of toils to perform.</returns>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);

            // Go to the item
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            // Do the improvement work
            var improveToil = new Toil();
            improveToil.initAction = () =>
            {
                GenClamor.DoClamor(improveToil.actor, 15f, ClamorDefOf.Construction);
            };

            improveToil.tickAction = () =>
            {
                var targetThing = job.GetTarget(TargetIndex.A).Thing;
                if (targetThing == null)
                {
                    ReadyForNextToil();
                    return;
                }
                
                var comp = targetThing.TryGetComp<SimpleImproveComp>();
                if (comp == null || !comp.IsMarkedForImprovement)
                {
                    ReadyForNextToil();
                    return;
                }

                var actor = improveToil.actor;
                var qualityComp = comp.parent.TryGetComp<CompQuality>();

                // The same decision WorkGiver_Improve.JobOnThing makes, deliberately through the
                // same function so the two cannot drift. It is a safety net rather than the gate: the
                // work giver already refused an unreadable skill, so reaching that case here means the
                // job arrived some other way, and this job ends at CompleteImprovement, whose quality
                // roll dereferences pawn.skills without a guard for a non-mechanoid.
                //
                // A null requirement is the "any improvement" case, where the player aimed at no
                // particular quality and any readable skill will do.
                var workerSkill = WorkerSkill.Of(actor);
                var targetQuality = qualityComp != null ? comp.TargetQuality : null;
                int? requiredSkill = targetQuality.HasValue
                    ? SimpleImproveMod.Settings.GetSkillRequirement(targetQuality.Value, actor)
                    : (int?)null;

                if (WorkerSkill.FirstBlocker(workerSkill, requiredSkill) != ImproveSkillBlocker.None)
                {
                    ReadyForNextToil();
                    return;
                }

                // Learn construction skill. Null on every non-humanlike worker, so a mech earns no XP
                // and keeps working, which is exactly what JobDriver_ConstructFinishFrame does at its
                // own per-tick XP grant.
                actor.skills?.Learn(SkillDefOf.Construction, 0.25f);

                // Calculate work speed
                var speed = actor.GetStatValue(StatDefOf.ConstructionSpeed) * 1.7f;
                if (comp.parent.Stuff != null)
                {
                    speed *= comp.parent.Stuff.GetStatValueAbstract(StatDefOf.ConstructionSpeedFactor);
                }

                // Check for construction failure
                if (actor.Faction == Faction.OfPlayer && !TutorSystem.TutorialMode)
                {
                    var successChance = actor.GetStatValue(StatDefOf.ConstructSuccessChance);
                    var failChance = 1f - Mathf.Pow(successChance, speed / comp.WorkToBuild);

                    if (Rand.Value < failChance)
                    {
                        comp.FailImprovement(actor);
                        ReadyForNextToil();
                        return;
                    }
                }

                // Do work
                comp.WorkDone += speed;

                // Check if complete
                if (comp.WorkLeft <= 0)
                {
                    comp.CompleteImprovement(actor);
                    ReadyForNextToil();
                }
            };

            improveToil.WithEffect(TargetThingA.def.repairEffect, TargetIndex.A);
            improveToil.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            improveToil.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            // checkSkills: false must match WorkGiver_Improve.JobOnThing. This overload defaults it
            // to true, so leaving it implicit would let the work giver hand out a job that this toil
            // then failed on the first tick, for any thing whose def carries a
            // constructionSkillPrerequisite above the pawn's Construction level.
            improveToil.FailOn(() => !GenConstruct.CanConstruct(TargetThingA, pawn, checkSkills: false));
            improveToil.WithProgressBar(TargetIndex.A, () => {
                var comp = TargetComp;
                return comp?.WorkDone / comp?.WorkToBuild ?? 0f;
            });
            improveToil.defaultCompleteMode = ToilCompleteMode.Delay;
            improveToil.defaultDuration = 5000;
            improveToil.activeSkill = () => SkillDefOf.Construction;

            yield return improveToil;
        }
    }
}