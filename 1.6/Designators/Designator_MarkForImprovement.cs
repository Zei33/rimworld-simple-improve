using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using SimpleImprove.Core;

namespace SimpleImprove.Designators
{
    /// <summary>
    /// Designator for marking buildings and furniture for quality improvement.
    /// Allows players to designate items to have their quality enhanced by skilled workers.
    /// </summary>
    public class Designator_MarkForImprovement : Designator
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Designator_MarkForImprovement"/> class.
        /// Sets up the designator's appearance, sounds, and behavior properties.
        /// </summary>
        public Designator_MarkForImprovement()
        {
            defaultLabel = "Designate for improvement";
            defaultDesc = "Mark items to have their quality improved by skilled workers";
            icon = ContentFinder<Texture2D>.Get("UI/Designators/Upgrade");
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
        /// Determines whether the specified cell can be designated for improvement.
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
        /// Designates all eligible things in the specified cell for improvement.
        /// </summary>
        /// <param name="cell">The cell containing things to mark for improvement.</param>
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
        /// Determines whether the specified thing can be designated for improvement.
        /// Checks for improvement component, quality component, and other eligibility criteria.
        /// </summary>
        /// <param name="thing">The thing to check for designation eligibility.</param>
        /// <returns>An acceptance report indicating whether the thing can be designated and why.</returns>
        public override AcceptanceReport CanDesignateThing(Thing thing)
        {
            var improveComp = thing.TryGetComp<SimpleImproveComp>();
            if (improveComp == null)
                return "Item cannot be improved";

            if (improveComp.IsMarkedForImprovement)
                return "Already marked for improvement";

            var qualityComp = thing.TryGetComp<CompQuality>();
            if (qualityComp == null)
                return "Item has no quality";

            if (qualityComp.Quality == QualityCategory.Legendary)
                return "Already at maximum quality";

            if (thing.def.blueprintDef == null)
                return "Item cannot be constructed";

            return AcceptanceReport.WasAccepted;
        }

        /// <summary>
        /// Marks the specified thing for improvement.
        /// Sets the improvement flag and checks for skill warnings.
        /// </summary>
        /// <param name="thing">The thing to mark for improvement.</param>
        public override void DesignateThing(Thing thing)
        {
            var improveComp = thing.TryGetComp<SimpleImproveComp>();
            if (improveComp != null)
            {
                improveComp.IsMarkedForImprovement = true;
                
                // Check if any pawn can do this improvement and show warning if not
                CheckAndShowSkillWarning(thing);
            }
        }
        
        /// <summary>
        /// Checks if any colonist can perform the improvement and shows a warning if none are capable.
        /// Considers skill requirements, work assignments, and potential bonuses from inspirations or roles.
        /// </summary>
        /// <param name="thing">The thing being marked for improvement.</param>
        private void CheckAndShowSkillWarning(Thing thing)
        {
            var qualityComp = thing.TryGetComp<CompQuality>();
            if (qualityComp == null) return;
            
            var requiredSkill = SimpleImproveMod.Settings.GetSkillRequirement(qualityComp.Quality);
            if (requiredSkill <= 0) return;
            
            var allPawns = thing.Map.mapPawns.FreeColonistsSpawned;
            var capablePawns = allPawns.Where(pawn => 
                pawn.workSettings.WorkIsActive(SimpleImproveDefOf.WorkType_Improving) &&
                pawn.skills.GetSkill(SkillDefOf.Construction).Level >= SimpleImproveMod.Settings.GetSkillRequirement(qualityComp.Quality, pawn)
            ).ToList();
            
            if (!capablePawns.Any())
            {
                var baseRequiredSkill = SimpleImproveMod.Settings.GetSkillRequirement(qualityComp.Quality);
                string message;
                
                if (ModsConfig.IdeologyActive)
                {
                    message = $"Warning: No colonist can improve this {thing.def.label} (requires Construction {requiredSkill}, or {baseRequiredSkill} with inspiration/production role).";
                }
                else
                {
                    message = $"Warning: No colonist can improve this {thing.def.label} (requires Construction {requiredSkill}, or {baseRequiredSkill} with inspiration).";
                }
                
                Messages.Message(message, thing, MessageTypeDefOf.CautionInput);
            }
        }
    }
}