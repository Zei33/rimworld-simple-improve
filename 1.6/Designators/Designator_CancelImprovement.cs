using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using SimpleImprove.Core;

namespace SimpleImprove.Designators
{
    /// <summary>
    /// Designator for canceling improvement orders on buildings and furniture.
    /// Allows players to remove improvement designations and recover stored materials.
    /// </summary>
    public class Designator_CancelImprovement : Designator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Designator_CancelImprovement"/> class.
        /// Sets up the designator's appearance, sounds, and behavior properties.
        /// </summary>
        public Designator_CancelImprovement()
        {
            defaultLabel = "Cancel improvement";
            defaultDesc = "Cancel improvement orders and return any stored materials";
            icon = ContentFinder<Texture2D>.Get("UI/Designators/CancelImprove");
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            useMouseIcon = true;
            soundSucceeded = SoundDefOf.Designate_Haul;
        }

        /// <summary>
        /// Gets the draw style category for this designator.
        /// Determines how the designator appears visually in the game.
        /// </summary>
        public override DrawStyleCategoryDef DrawStyleCategory => DefDatabase<DrawStyleCategoryDef>.GetNamed("Orders");

        /// <summary>
        /// Determines whether the specified cell can be designated for improvement cancellation.
        /// </summary>
        /// <param name="cell">The cell to check for designation eligibility.</param>
        /// <returns>An acceptance report indicating whether the cell can be designated.</returns>
        public override AcceptanceReport CanDesignateCell(IntVec3 cell)
        {
            if (!cell.InBounds(Map) || cell.Fogged(Map))
                return false;

            var things = Map.thingGrid.ThingsListAt(cell);
            return things.Any(thing => CanDesignateThing(thing).Accepted);
        }

        /// <summary>
        /// Designates all eligible things in the specified cell for improvement cancellation.
        /// </summary>
        /// <param name="cell">The cell containing things to cancel improvement on.</param>
        public override void DesignateSingleCell(IntVec3 cell)
        {
            if (!cell.InBounds(Map) || cell.Fogged(Map))
                return;

            var things = Map.thingGrid.ThingsListAt(cell);
            foreach (var thing in things)
            {
                if (CanDesignateThing(thing).Accepted)
                {
                    DesignateThing(thing);
                }
            }
        }

        /// <summary>
        /// Determines whether the specified thing can be designated for improvement cancellation.
        /// </summary>
        /// <param name="thing">The thing to check for designation eligibility.</param>
        /// <returns>An acceptance report indicating whether the thing can be designated.</returns>
        public override AcceptanceReport CanDesignateThing(Thing thing)
        {
            var designation = Map.designationManager.DesignationOn(thing, SimpleImproveDefOf.Designation_Improve);
            if (designation != null)
                return AcceptanceReport.WasAccepted;

            return AcceptanceReport.WasRejected;
        }

        /// <summary>
        /// Cancels the improvement designation on the specified thing.
        /// Removes the improvement flag and cleans up any stored materials.
        /// </summary>
        /// <param name="thing">The thing to cancel improvement on.</param>
        public override void DesignateThing(Thing thing)
        {
            var improveComp = thing.TryGetComp<SimpleImproveComp>();
            if (improveComp != null && improveComp.IsMarkedForImprovement)
            {
                improveComp.IsMarkedForImprovement = false;
            }
        }
    }
}