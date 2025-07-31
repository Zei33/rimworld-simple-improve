using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using SimpleImprove.Core;

namespace SimpleImprove.Jobs
{
    /// <summary>
    /// Job driver for hauling materials to buildings marked for improvement.
    /// Handles moving required materials from storage to the improvement site.
    /// </summary>
    public class JobDriver_HaulToImprove : JobDriver
    {
        /// <summary>
        /// Gets the thing to be carried (the material being hauled).
        /// </summary>
        private Thing ThingToCarry => job.GetTarget(TargetIndex.A).Thing;
        
        /// <summary>
        /// Gets the container (the building being improved that will receive the materials).
        /// </summary>
        private Thing Container => job.GetTarget(TargetIndex.B).Thing;

        /// <summary>
        /// Gets a human-readable report of what the pawn is currently doing.
        /// Used for displaying job progress in the UI.
        /// </summary>
        /// <returns>A localized string describing the hauling activity.</returns>
        public override string GetReport()
        {
            var thing = pawn.carryTracker.CarriedThing ?? TargetThingA;
            if (thing == null || !job.targetB.HasThing)
            {
                return "ReportHaulingUnknown".Translate();
            }

            return "ReportHaulingTo".Translate(thing.Label, job.targetB.Thing.LabelShort.Named("DESTINATION"), thing.Named("THING"));
        }

        /// <summary>
        /// Attempts to make reservations for both the material to haul and the destination building.
        /// Ensures no other pawns interfere with the hauling operation.
        /// </summary>
        /// <param name="errorOnFailed">Whether to log an error if reservation fails.</param>
        /// <returns><c>true</c> if both reservations were successful; otherwise, <c>false</c>.</returns>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.HasReserved(job.targetA, job))
            {
                if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed))
                    return false;
            }

            if (!pawn.HasReserved(job.targetB, job))
            {
                if (!pawn.Reserve(job.targetB, job, 1, -1, null, errorOnFailed))
                {
                    pawn.Map.reservationManager.Release(job.targetA, pawn, job);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Creates the sequence of toils (work steps) for the hauling job.
        /// Sets up movement, pickup, carrying, and deposition of materials.
        /// </summary>
        /// <returns>An enumerable sequence of toils to perform.</returns>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Fail conditions
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDestroyedNullOrForbidden(TargetIndex.B);
            this.FailOn(() => {
                var container = Container;
                if (container == null) return true;
                var improveComp = container.TryGetComp<SimpleImproveComp>();
                return improveComp == null || !improveComp.IsMarkedForImprovement;
            });

            // Core toils
            var gotoThing = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            
            var uninstallIfMinifiable = Toils_Construct.UninstallIfMinifiable(TargetIndex.A)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            
            var startCarrying = Toils_Haul.StartCarryThing(TargetIndex.A, putRemainderInQueue: false, subtractNumTakenFromJobCount: true);
            
            var jumpIfAlsoCollecting = Toils_Haul.JumpIfAlsoCollectingNextTargetInQueue(gotoThing, TargetIndex.A);
            
            var carryToContainer = Toils_Haul.CarryHauledThingToContainer();

            // Execute toils
            yield return Toils_Jump.JumpIf(jumpIfAlsoCollecting, () => pawn.IsCarryingThing(ThingToCarry));
            yield return gotoThing;
            yield return uninstallIfMinifiable;
            yield return startCarrying;
            yield return jumpIfAlsoCollecting;
            yield return carryToContainer;
            yield return DepositHauledThingInContainer(TargetIndex.B);
        }

        /// <summary>
        /// Creates a toil for depositing the hauled materials into the improvement container.
        /// Handles transferring the correct amount of materials and validates the operation.
        /// </summary>
        /// <param name="containerIndex">The target index of the container to deposit into.</param>
        /// <returns>A toil that performs the material deposition.</returns>
        private Toil DepositHauledThingInContainer(TargetIndex containerIndex)
        {
            var toil = new Toil();
            toil.initAction = () =>
            {
                if (pawn.carryTracker.CarriedThing == null)
                {
                    Log.Error($"{pawn} tried to deposit materials but is not carrying anything.");
                    return;
                }

                var targetThing = job.GetTarget(containerIndex).Thing;
                if (targetThing == null)
                {
                    Log.Error($"{pawn} tried to deposit materials but target thing is null.");
                    return;
                }
                
                var improveComp = targetThing.TryGetComp<SimpleImproveComp>();
                if (improveComp == null || !improveComp.IsMarkedForImprovement)
                {
                    Log.Error($"{pawn} tried to deposit materials into {targetThing.Label} but it is not marked for improvement.");
                    return;
                }

                var container = improveComp.GetDirectlyHeldThings();
                if (container == null)
                {
                    Log.Error($"Could not get material container for {targetThing.Label}");
                    return;
                }

                var carried = pawn.carryTracker.CarriedThing;
                var carryingCount = carried.stackCount;
                var neededCount = improveComp.ThingCountNeeded(carried.def);
                var transferCount = UnityEngine.Mathf.Min(carryingCount, neededCount);

                pawn.carryTracker.innerContainer.TryTransferToContainer(carried, container, transferCount);
            };

            return toil;
        }
    }
}