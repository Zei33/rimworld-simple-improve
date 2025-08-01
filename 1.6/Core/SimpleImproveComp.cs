using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using SimpleImprove.Utils;

namespace SimpleImprove.Core
{
    /// <summary>
    /// Main component for the SimpleImprove mod functionality.
    /// Handles marking items for improvement, material storage, work tracking, and quality enhancement.
    /// Implements <see cref="IConstructible"/> to integrate with RimWorld's construction system.
    /// </summary>
    public class SimpleImproveComp : ThingComp, IConstructible
    {
        /// <summary>
        /// Indicates whether this item is currently marked for improvement.
        /// </summary>
        private bool isMarkedForImprovement;
        
        /// <summary>
        /// Container for storing materials needed for the improvement process.
        /// </summary>
        private ThingOwner materialContainer;
        
        /// <summary>
        /// Amount of work completed towards the improvement.
        /// </summary>
        private float workDone;
        
        /// <summary>
        /// Cached list of materials needed for improvement to avoid repeated calculations.
        /// </summary>
        private List<ThingDefCountClass> cachedMaterialsNeeded = new List<ThingDefCountClass>();
        
        /// <summary>
        /// The target quality level that the improvement should aim for.
        /// If null, any improvement is acceptable (original behavior).
        /// </summary>
        private QualityCategory? targetQuality;
	
        /// <summary>
        /// Gets or sets whether this item is marked for improvement.
        /// Setting this property will automatically handle designation management and material cleanup.
        /// </summary>
        /// <value>
        /// <c>true</c> if the item is marked for improvement; otherwise, <c>false</c>.
        /// </value>
        public bool IsMarkedForImprovement
        {
            get => isMarkedForImprovement;
            set
            {
                if (value == isMarkedForImprovement) return;
                
                if (!value && isMarkedForImprovement)
                {
                    // Clear materials and designation when unmarking
                    GetMaterialContainer().TryDropAll(parent.Position, parent.Map, ThingPlaceMode.Near);
                    parent.Map?.designationManager.TryRemoveDesignationOn(parent, SimpleImproveDefOf.Designation_Improve);
                }
                else if (value && !isMarkedForImprovement)
                {
                    // Add designation when marking
                    parent.Map?.designationManager.AddDesignation(new Designation(parent, SimpleImproveDefOf.Designation_Improve));
                }
                
                isMarkedForImprovement = value;
            }
        }
        
        /// <summary>
        /// Sets the improvement flag directly without triggering designation management logic.
        /// This method is used internally to avoid recursive designation removal.
        /// </summary>
        /// <param name="value">The value to set for the improvement flag.</param>
        /// <remarks>
        /// This method is primarily used by <see cref="Patches.DesignationCancelPatch"/> to avoid
        /// recursive designation removal when canceling improvements.
        /// </remarks>
        public void SetMarkedForImprovementDirect(bool value)
        {
            isMarkedForImprovement = value;
        }

        /// <summary>
        /// Gets or sets the amount of work completed towards the improvement.
        /// </summary>
        /// <value>The work done in work units.</value>
        public float WorkDone
        {
            get => workDone;
            set => workDone = value;
        }

        /// <summary>
        /// Gets the total amount of work required to complete the improvement.
        /// This value is based on the parent thing's WorkToBuild stat.
        /// </summary>
        /// <value>The total work required in work units.</value>
        public float WorkToBuild => parent.def.GetStatValueAbstract(StatDefOf.WorkToBuild, parent.Stuff);
        
        /// <summary>
        /// Gets the remaining work needed to complete the improvement.
        /// </summary>
        /// <value>The remaining work in work units.</value>
        public float WorkLeft => WorkToBuild - workDone;
        
        /// <summary>
        /// Gets or sets the target quality level for improvement.
        /// If null, any improvement is acceptable (original behavior).
        /// </summary>
        /// <value>The target quality category, or null for any improvement.</value>
        public QualityCategory? TargetQuality
        {
            get => targetQuality;
            set => targetQuality = value;
        }

        /// <summary>
        /// Gets the material container for storing improvement materials.
        /// Creates a new <see cref="MaterialStorage"/> container if one doesn't exist.
        /// </summary>
        /// <returns>The material container for this improvement component.</returns>
        public ThingOwner GetMaterialContainer()
        {
            if (materialContainer == null)
            {
                materialContainer = new MaterialStorage(this);
            }
            return materialContainer;
        }

        /// <summary>
        /// Gets the directly held things for this component.
        /// This is required for implementing the material storage interface.
        /// </summary>
        /// <returns>The material container containing held items.</returns>
        public ThingOwner GetDirectlyHeldThings() => GetMaterialContainer();

        /// <summary>
        /// Calculates the total material cost required for improvement.
        /// This accounts for the fraction of materials that would be returned if the item were deconstructed.
        /// </summary>
        /// <returns>A list of materials and their required counts for improvement.</returns>
        public List<ThingDefCountClass> GetTotalMaterialCost()
        {
            cachedMaterialsNeeded.Clear();
            
            var baseCost = parent.def.CostListAdjusted(parent.Stuff, false);
            var returnedFraction = parent.def.resourcesFractionWhenDeconstructed;
            
            foreach (var material in baseCost)
            {
                var requiredCount = material.count - Mathf.FloorToInt(material.count * returnedFraction);
                if (requiredCount > 0)
                {
                    cachedMaterialsNeeded.Add(new ThingDefCountClass(material.thingDef, requiredCount));
                }
            }
            
            return cachedMaterialsNeeded;
        }

        /// <summary>
        /// Gets the total material cost for construction.
        /// This method is required by the <see cref="IConstructible"/> interface.
        /// </summary>
        /// <returns>A list of materials and their required counts.</returns>
        public List<ThingDefCountClass> TotalMaterialCost()
        {
            return GetTotalMaterialCost();
        }

        /// <summary>
        /// Calculates the remaining materials needed for improvement.
        /// This subtracts any materials already stored in the container from the total required.
        /// </summary>
        /// <returns>A list of materials still needed and their counts.</returns>
        public List<ThingDefCountClass> GetRemainingMaterialCost()
        {
            var totalCost = GetTotalMaterialCost();
            var remaining = new List<ThingDefCountClass>();
            
            foreach (var material in totalCost)
            {
                var currentCount = GetMaterialContainer().TotalStackCountOfDef(material.thingDef);
                var needed = material.count - currentCount;
                
                if (needed > 0)
                {
                    remaining.Add(new ThingDefCountClass(material.thingDef, needed));
                }
            }
            
            return remaining;
        }

        /// <summary>
        /// Gets the number of items of a specific type still needed for improvement.
        /// </summary>
        /// <param name="stuff">The type of material to check.</param>
        /// <returns>The number of items still needed, or 0 if none are needed.</returns>
        public int ThingCountNeeded(ThingDef stuff)
        {
            var material = cachedMaterialsNeeded.FirstOrDefault(m => m.thingDef == stuff);
            if (material == null) return 0;
            
            return material.count - GetMaterialContainer().TotalStackCountOfDef(stuff);
        }

        /// <summary>
        /// Completes the improvement process for the item.
        /// Generates a new quality based on the worker's skill and applies it if it's better than the current quality.
        /// If a target quality is set, continues improving until the target is reached.
        /// </summary>
        /// <param name="worker">The pawn performing the improvement work.</param>
        public void CompleteImprovement(Pawn worker)
        {
            workDone = 0;
            GetMaterialContainer().ClearAndDestroyContents();
            
            var compQuality = parent.TryGetComp<CompQuality>();
            if (compQuality == null)
            {
                Log.Error($"[SimpleImprove] Attempted to improve {parent.Label} but it has no quality component!");
                return;
            }
            
            var currentQuality = compQuality.Quality;
            var newQuality = QualityUtility.GenerateQualityCreatedByPawn(worker, SkillDefOf.Construction);
            
            if (newQuality <= currentQuality)
            {
                // Improvement failed - show failure message and continue trying if target not met
                MoteMaker.ThrowText(parent.DrawPos, parent.Map, 
                    "SimpleImprove_ImprovementFailed".Translate(newQuality.GetLabel()), 6f);
                
                // Check if we should continue improving based on target quality
                if (ShouldContinueImproving(currentQuality))
                {
                    return; // Keep trying - don't clear the improvement flag
                }
                else
                {
                    // No target set or target already met - stop improving
                    ClearImprovementAndFinish();
                    return;
                }
            }
            
            // Improvement succeeded - apply the new quality
            compQuality.SetQuality(newQuality, ArtGenerationContext.Colony);
            QualityUtility.SendCraftNotification(parent, worker);
            
            // Handle art generation if applicable (excellent quality and above becomes art)
            var compArt = parent.TryGetComp<CompArt>();
            if (compArt != null && compArt.CanShowArt)
            {
                if (!compArt.Active)
                {
                    compArt.InitializeArt(ArtGenerationContext.Colony);
                }
                compArt.JustCreatedBy(worker);
            }
            
            MoteMaker.ThrowText(parent.DrawPos, parent.Map, 
                "SimpleImprove_ImprovedTo".Translate(newQuality.GetLabel()), 6f);
            
            // Check if we should continue improving based on target quality
            if (ShouldContinueImproving(newQuality))
            {
                return; // Keep improving - don't clear the improvement flag
            }
            
            // Target reached or no target set - finish improvement
            ClearImprovementAndFinish();
        }
        
        /// <summary>
        /// Determines whether improvement should continue based on the target quality setting.
        /// </summary>
        /// <param name="currentQuality">The current quality level of the item.</param>
        /// <returns>True if improvement should continue, false if it should stop.</returns>
        private bool ShouldContinueImproving(QualityCategory currentQuality)
        {
            // If no target is set, stop after any improvement (original behavior)
            if (targetQuality == null)
                return false;
                
            // If current quality is below target, continue improving
            return currentQuality < targetQuality.Value;
        }
        
        /// <summary>
        /// Clears the improvement flag and removes the designation to finish the improvement process.
        /// </summary>
        private void ClearImprovementAndFinish()
        {
            // Clear the improvement flag directly and remove designation without triggering setter
            // to avoid double-clearing materials that were already destroyed above
            isMarkedForImprovement = false;
            parent.Map?.designationManager.TryRemoveDesignationOn(parent, SimpleImproveDefOf.Designation_Improve);
        }

        /// <summary>
        /// Handles failure of the improvement process.
        /// Clears work done, destroys materials, and shows appropriate failure messages.
        /// </summary>
        /// <param name="worker">The pawn who was performing the improvement work.</param>
        public void FailImprovement(Pawn worker)
        {
            workDone = 0;
            GetMaterialContainer().ClearAndDestroyContents();
            
            MoteMaker.ThrowText(parent.DrawPos, parent.Map, "TextMote_ConstructionFail".Translate(), 6f);
            
            if (parent.Faction == Faction.OfPlayer && WorkToBuild > 1400f)
            {
                Messages.Message("MessageConstructionFailed".Translate(parent.Label, worker.LabelShort, worker.Named("WORKER")), 
                    new TargetInfo(parent.Position, parent.Map), MessageTypeDefOf.NegativeEvent);
            }
        }

        /// <summary>
        /// Saves and loads component data for game save files.
        /// </summary>
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isMarkedForImprovement, "isMarkedForImprovement", false);
            Scribe_Values.Look(ref workDone, "workDone", 0f);
            Scribe_Values.Look(ref targetQuality, "targetQuality", null);
            Scribe_Deep.Look(ref materialContainer, "materialContainer", this);
        }

        /// <summary>
        /// Called when the parent thing is destroyed.
        /// Drops any stored materials if the item is being deconstructed.
        /// </summary>
        /// <param name="mode">The mode of destruction.</param>
        /// <param name="previousMap">The map the thing was on before destruction.</param>
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (mode == DestroyMode.Deconstruct)
            {
                GetMaterialContainer().TryDropAll(parent.Position, previousMap, ThingPlaceMode.Near);
            }
        }

        /// <summary>
        /// Provides additional information for the inspect window.
        /// Shows material requirements, work progress, and skill requirements.
        /// </summary>
        /// <returns>A string containing inspection information for the UI.</returns>
        public override string CompInspectStringExtra()
        {
            var sb = new StringBuilder();
            sb.Append(base.CompInspectStringExtra());
            
            if (!isMarkedForImprovement && !GetMaterialContainer().Any) 
                return sb.ToString();
            
            sb.AppendLineIfNotEmpty();
            sb.AppendLine("ContainedResources".Translate() + ":");
            
            var totalCost = GetTotalMaterialCost();
            var remaining = GetRemainingMaterialCost();
            var allSatisfied = true;
            
            foreach (var material in totalCost)
            {
                var currentCount = material.count - remaining.FirstOrDefault(r => r.thingDef == material.thingDef)?.count ?? material.count;
                sb.AppendLine($"  {material.thingDef.LabelCap}: {currentCount} / {material.count}");
                
                if (currentCount < material.count)
                    allSatisfied = false;
            }
            
            if (allSatisfied)
            {
                sb.AppendLine($"WorkLeft".Translate() + ": " + Mathf.CeilToInt(WorkLeft / 60f));
                
                var quality = parent.TryGetComp<CompQuality>()?.Quality ?? QualityCategory.Normal;
                var skillReq = SimpleImproveMod.Settings.GetSkillRequirement(quality);
                
                if (skillReq > 0)
                {
                    sb.AppendLine($"Minimum skill required: {skillReq}");
                }
                
                if (targetQuality.HasValue)
                {
                    sb.AppendLine($"SimpleImprove_TargetQuality".Translate(targetQuality.Value.GetLabel()));
                }
            }
            
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Provides UI gizmos (buttons) for the player interface.
        /// Shows the "Improve" dropdown button for eligible items.
        /// </summary>
        /// <returns>An enumerable of gizmos to display in the UI.</returns>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer) yield break;
            
            var compQuality = parent.TryGetComp<CompQuality>();
            if (compQuality == null || compQuality.Quality == QualityCategory.Legendary) yield break;
            
            if (parent.def.blueprintDef == null) yield break; // Items without blueprints can't be improved
            
            yield return new Command_Action
            {
                defaultLabel = GetImproveGizmoLabel(),
                defaultDesc = "SimpleImprove_GizmoTooltip".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Improve", true),
                action = () => ShowQualityTargetFloatMenu()
            };
        }
        
        /// <summary>
        /// Gets the label for the improve gizmo based on current state.
        /// </summary>
        /// <returns>The label text for the gizmo.</returns>
        private string GetImproveGizmoLabel()
        {
            if (!isMarkedForImprovement)
            {
                return "SimpleImprove_GizmoLabel".Translate();
            }
            
            if (targetQuality.HasValue)
            {
                return "SimpleImprove_GizmoLabelWithTarget".Translate(targetQuality.Value.GetLabel());
            }
            
            return "SimpleImprove_GizmoLabelAny".Translate();
        }
        
        /// <summary>
        /// Shows the float menu for selecting quality targets.
        /// </summary>
        private void ShowQualityTargetFloatMenu()
        {
            var options = new List<FloatMenuOption>();
            
            var currentQuality = parent.TryGetComp<CompQuality>()?.Quality ?? QualityCategory.Normal;
            
            // Add "Cancel improvement" option if already marked
            if (isMarkedForImprovement)
            {
                options.Add(new FloatMenuOption("SimpleImprove_CancelImprovement".Translate(), () =>
                {
                    IsMarkedForImprovement = false;
                }));
            }
            
            // Add "Any improvement" option
            var anyLabel = "SimpleImprove_TargetAny".Translate();
            if (targetQuality == null && isMarkedForImprovement)
            {
                anyLabel += " ✓";
            }
            options.Add(new FloatMenuOption(anyLabel, () =>
            {
                targetQuality = null;
                IsMarkedForImprovement = true;
            }));
            
            // Add specific quality targets (only those higher than current)
            var qualityTargets = new[]
            {
                QualityCategory.Poor,
                QualityCategory.Normal, 
                QualityCategory.Good,
                QualityCategory.Excellent,
                QualityCategory.Masterwork
            };
            
            foreach (var quality in qualityTargets)
            {
                if (quality <= currentQuality) continue; // Can't target lower quality
                
                var label = quality.GetLabel().CapitalizeFirst();
                if (targetQuality == quality && isMarkedForImprovement)
                {
                    label += " ✓";
                }
                
                options.Add(new FloatMenuOption(label, () =>
                {
                    targetQuality = quality;
                    IsMarkedForImprovement = true;
                }));
            }
            
            if (options.Count > (isMarkedForImprovement ? 2 : 1)) // More than just "Any" option
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
            else if (!isMarkedForImprovement)
            {
                // No valid targets, just mark for any improvement
                targetQuality = null;
                IsMarkedForImprovement = true;
            }
        }

        /// <summary>
        /// Gets the stuff (material) used to build this entity.
        /// Required by the <see cref="IConstructible"/> interface.
        /// </summary>
        /// <returns>The stuff definition of the parent thing.</returns>
        public ThingDef EntityToBuildStuff() => parent.Stuff;
        
        /// <summary>
        /// Indicates whether the construction is completed.
        /// Always returns false for improvement components as they represent ongoing work.
        /// Required by the <see cref="IConstructible"/> interface.
        /// </summary>
        /// <returns>Always returns <c>false</c> for improvement components.</returns>
        public bool IsCompleted() => false;
    }
}