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

                // Check skill requirement during work as safety net
                if (qualityComp != null)
                {
                    var pawnSkill = actor.skills.GetSkill(SkillDefOf.Construction).Level;
                    var requiredSkill = SimpleImproveMod.Settings.GetSkillRequirement(qualityComp.Quality, actor);

                    if (requiredSkill > pawnSkill)
                    {
                        ReadyForNextToil();
                        return;
                    }
                }

                // Learn construction skill
                actor.skills.Learn(SkillDefOf.Construction, 0.25f);

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
            improveToil.FailOn(() => !GenConstruct.CanConstruct(TargetThingA, pawn));
            improveToil.WithProgressBar(TargetIndex.A, () => {
                var comp = TargetComp;
                return comp?.WorkDone / comp?.WorkToBuild ?? 0f;
            });
            improveToil.defaultCompleteMode = ToilCompleteMode.Delay;
            improveToil.defaultDuration = 5000;
            improveToil.activeSkill = () => SkillDefOf.Construction;

            improveToil.finishActions.Add(() =>
            {
                pawn.Map.reservationManager.Release(job.targetA, pawn, job);
            });

            yield return improveToil;
        }
    }
}