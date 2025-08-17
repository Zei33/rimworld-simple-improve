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
    /// Represents a group of buildings with similar improvement states for consolidated gizmo display.
    /// </summary>
    public class ImproveGroup
    {
        public List<SimpleImproveComp> Comps { get; set; } = new List<SimpleImproveComp>();
        public bool IsMarked { get; set; }
        public QualityCategory? TargetQuality { get; set; }
        public SimpleImproveComp Representative { get; set; }
        public string GroupKey { get; set; }
        
        public QualityCategory HighestCurrentQuality => Comps.Max(c => c.parent.TryGetComp<CompQuality>()?.Quality ?? QualityCategory.Normal);
        public QualityCategory LowestCurrentQuality => Comps.Min(c => c.parent.TryGetComp<CompQuality>()?.Quality ?? QualityCategory.Normal);
    }

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
        /// Note: This is now stored in the MapComponent for persistence across save/load.
        /// </summary>
        // Removed private field - now uses MapComponent storage
	
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
            get
            {
                var mapComp = GetSimpleImproveMapComponent();
                return mapComp?.GetTargetQuality(parent.thingIDNumber);
            }
            set
            {
                var mapComp = GetSimpleImproveMapComponent();
                mapComp?.SetTargetQuality(parent.thingIDNumber, value);
            }
        }

        /// <summary>
        /// Gets the SimpleImproveMapComponent for this map.
        /// This component handles persistent storage of target quality data.
        /// </summary>
        /// <returns>The SimpleImproveMapComponent, or null if not available.</returns>
        private SimpleImproveMapComponent GetSimpleImproveMapComponent()
        {
            return parent?.Map?.GetComponent<SimpleImproveMapComponent>();
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
        /// Returns empty list if materials are not required by settings.
        /// </summary>
        /// <returns>A list of materials and their required counts for improvement.</returns>
        public List<ThingDefCountClass> GetTotalMaterialCost()
        {
            cachedMaterialsNeeded.Clear();
            
            // If materials are not required, return empty list
            if (!SimpleImproveMod.Settings.RequireMaterials)
            {
                return cachedMaterialsNeeded;
            }
            
            var baseCost = parent.def.CostListAdjusted(parent.Stuff, false);
            var returnedFraction = parent.def.resourcesFractionWhenDeconstructed;
            
            foreach (var material in baseCost)
            {
                // Apply the material cost multiplier to the full build cost first
                var adjustedBuildCost = Mathf.CeilToInt(material.count * SimpleImproveMod.Settings.MaterialCostMultiplier);
                
                if (adjustedBuildCost > 0)
                {
                    cachedMaterialsNeeded.Add(new ThingDefCountClass(material.thingDef, adjustedBuildCost));
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
        /// Returns empty list if materials are not required by settings.
        /// </summary>
        /// <returns>A list of materials still needed and their counts.</returns>
        public List<ThingDefCountClass> GetRemainingMaterialCost()
        {
            var totalCost = GetTotalMaterialCost();
            var remaining = new List<ThingDefCountClass>();
            
            // If materials are not required, return empty list
            if (!SimpleImproveMod.Settings.RequireMaterials)
            {
                return remaining;
            }
            
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
        /// <returns>The number of items still needed, or 0 if none are needed or materials are not required.</returns>
        public int ThingCountNeeded(ThingDef stuff)
        {
            // If materials are not required, return 0
            if (!SimpleImproveMod.Settings.RequireMaterials)
            {
                return 0;
            }
            
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
                // Improvement failed - show failure message and handle based on target quality
                MoteMaker.ThrowText(parent.DrawPos, parent.Map, 
                    "SimpleImprove_ImprovementFailed".Translate(newQuality.GetLabel()), 6f);
                
                // Clear materials on failure if required by settings
                if (SimpleImproveMod.Settings.RequireMaterials)
                {
                    GetMaterialContainer().ClearAndDestroyContents();
                }
            } 
            else 
            {
                // Improvement succeeded - clear materials and apply the new quality
                if (SimpleImproveMod.Settings.RequireMaterials)
                {
                    GetMaterialContainer().ClearAndDestroyContents();
                }

				// Apply the new quality first
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
        }
        
        /// <summary>
        /// Determines whether improvement should continue based on the target quality setting.
        /// </summary>
        /// <param name="currentQuality">The current quality level of the item.</param>
        /// <returns>True if improvement should continue, false if it should stop.</returns>
        private bool ShouldContinueImproving(QualityCategory currentQuality)
        {
            // If no target is set, stop after any improvement (original behavior)
            if (TargetQuality == null)
                return false;
                
            // If current quality is below target, continue improving
            return currentQuality < TargetQuality.Value;
        }
        
        /// <summary>
        /// Checks if any pawns can achieve the target quality and shows a warning if none are capable.
        /// Considers skill requirements, work assignments, and potential bonuses from inspirations or roles.
        /// </summary>
        /// <param name="targetQuality">The target quality to check skill requirements for.</param>
        private void CheckAndShowTargetQualitySkillWarning(QualityCategory targetQuality)
        {
            var baseRequiredSkill = SimpleImproveMod.Settings.GetSkillRequirement(targetQuality);
            if (baseRequiredSkill <= 0) return;
            
            var map = parent.Map;
            if (map?.mapPawns?.FreeColonistsSpawned == null) return;
            
            var allPawns = map.mapPawns.FreeColonistsSpawned;
            var capablePawns = allPawns.Where(pawn => 
                pawn.workSettings.WorkIsActive(SimpleImproveDefOf.WorkType_Improving) &&
                pawn.skills.GetSkill(SkillDefOf.Construction).Level >= SimpleImproveMod.Settings.GetSkillRequirement(targetQuality, pawn)
            ).ToList();
            
            if (!capablePawns.Any())
            {
                var bestCaseRequiredSkill = SimpleImproveMod.Settings.GetBestCaseSkillRequirement(targetQuality, map);
                string message;
                
                if (ModsConfig.IdeologyActive)
                {
                    message = "SimpleImprove_TargetQualitySkillWarningIdeology".Translate(targetQuality.GetLabel(), baseRequiredSkill, bestCaseRequiredSkill);
                }
                else
                {
                    message = "SimpleImprove_TargetQualitySkillWarning".Translate(targetQuality.GetLabel(), baseRequiredSkill, bestCaseRequiredSkill);
                }
                
                Messages.Message(message, parent, MessageTypeDefOf.CautionInput);
            }
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
            
            // Always clear materials on construction failure (this is actual construction failure, not quality failure)
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
        /// Note: Target quality is now stored in SimpleImproveMapComponent for persistence.
        /// </summary>
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isMarkedForImprovement, "isMarkedForImprovement", false);
            Scribe_Values.Look(ref workDone, "workDone", 0f);
            // Note: targetQuality is now stored in SimpleImproveMapComponent
            Scribe_Deep.Look(ref materialContainer, "materialContainer", this);
        }

        /// <summary>
        /// Called when the parent thing is destroyed.
        /// Drops any stored materials if the item is being deconstructed and cleans up target quality data.
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
            
            // Clean up target quality data from the MapComponent
            var mapComp = previousMap?.GetComponent<SimpleImproveMapComponent>();
            mapComp?.RemoveTargetQuality(parent.thingIDNumber);
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
            
            var totalCost = GetTotalMaterialCost();
            var remaining = GetRemainingMaterialCost();
            var allSatisfied = true;
            
            // Show material requirements if materials are required by settings
            if (SimpleImproveMod.Settings.RequireMaterials)
            {
                sb.AppendLine("ContainedResources".Translate() + ":");
                
                foreach (var material in totalCost)
                {
                    var currentCount = material.count - remaining.FirstOrDefault(r => r.thingDef == material.thingDef)?.count ?? material.count;
                    sb.AppendLine($"  {material.thingDef.LabelCap}: {currentCount} / {material.count}");
                    
                    if (currentCount < material.count)
                        allSatisfied = false;
                }
            }
            else
            {
                // If materials are not required but there are stored materials, show them
                if (GetMaterialContainer().Any)
                {
                    sb.AppendLine("Stored materials (not required):");
                    var storedMaterials = GetMaterialContainer().GroupBy(t => t.def)
                        .Select(g => new { Def = g.Key, Count = g.Sum(t => t.stackCount) });
                    
                    foreach (var stored in storedMaterials)
                    {
                        sb.AppendLine($"  {stored.Def.LabelCap}: {stored.Count}");
                    }
                }
                
                // When materials are not required, improvement is always ready
                allSatisfied = true;
            }
            
            if (allSatisfied)
            {
                sb.AppendLine($"WorkLeft".Translate() + ": " + Mathf.CeilToInt(WorkLeft / 60f));
                
                // Show skill requirement based on target quality
                if (TargetQuality.HasValue)
                {
                    var skillReq = SimpleImproveMod.Settings.GetSkillRequirement(TargetQuality.Value);
                    if (skillReq > 0)
                    {
                        sb.AppendLine($"Minimum skill required for {TargetQuality.Value.GetLabel()}: {skillReq}");
                    }
                }
                else
                {
                    // For "Any" improvement, show that no specific skill is required
                    sb.AppendLine("Any skill level accepted (marked for any improvement)");
                }
                
                // Show note if materials are disabled
                if (!SimpleImproveMod.Settings.RequireMaterials)
                {
                    sb.AppendLine("Materials not required (disabled in settings)");
                }
            }
            
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Gets all selected things that have SimpleImproveComp and are eligible for improvement.
        /// </summary>
        /// <returns>List of SimpleImproveComp components from selected buildings.</returns>
        private List<SimpleImproveComp> GetSelectedImproveComps()
        {
            return Find.Selector.SelectedObjects.OfType<Thing>()
                .Where(t => t.Faction == Faction.OfPlayer && 
                           t.TryGetComp<CompQuality>() != null && 
                           t.TryGetComp<CompQuality>().Quality != QualityCategory.Legendary &&
                           t.def.blueprintDef != null)
                .Select(t => t.TryGetComp<SimpleImproveComp>())
                .Where(c => c != null)
                .ToList();
        }

        /// <summary>
        /// Analyzes the current selection and groups buildings by their improvement state.
        /// Implements the grouping rules for consolidated gizmo display.
        /// </summary>
        /// <returns>List of improvement groups.</returns>
        private List<ImproveGroup> AnalyzeSelection()
        {
            var selectedComps = GetSelectedImproveComps();
            if (!selectedComps.Any()) return new List<ImproveGroup>();

            var groups = new List<ImproveGroup>();

            // Group unmarked buildings
            var unmarkedComps = selectedComps.Where(c => !c.IsMarkedForImprovement).ToList();
            if (unmarkedComps.Any())
            {
                groups.Add(new ImproveGroup
                {
                    Comps = unmarkedComps,
                    IsMarked = false,
                    TargetQuality = null,
                    Representative = unmarkedComps.First(),
                    GroupKey = "unmarked"
                });
            }

            // Group marked buildings by target quality
            var markedComps = selectedComps.Where(c => c.IsMarkedForImprovement).ToList();
            var markedGroups = markedComps
                .GroupBy(c => c.TargetQuality?.ToString() ?? "any")
                .Select(g => new ImproveGroup
                {
                    Comps = g.ToList(),
                    IsMarked = true,
                    TargetQuality = g.First().TargetQuality,
                    Representative = g.First(),
                    GroupKey = $"marked_{g.Key}"
                })
                .ToList();

            groups.AddRange(markedGroups);

            return groups;
        }

        /// <summary>
        /// Gets the available quality options for a group based on the selection rules.
        /// </summary>
        /// <param name="group">The improvement group to get options for.</param>
        /// <param name="allGroups">All groups in the current selection for context.</param>
        /// <returns>List of available quality categories.</returns>
        private List<QualityCategory> GetAvailableQualityOptions(ImproveGroup group, List<ImproveGroup> allGroups)
        {
            var options = new List<QualityCategory>();
            
            if (!group.IsMarked)
            {
                // For unmarked buildings, show options based on highest quality building
                var hasMarkedBuildings = allGroups.Any(g => g.IsMarked);
                if (hasMarkedBuildings)
                {
                    // When some buildings are marked, limit options for unmarked based on highest quality
                    var highestQuality = group.HighestCurrentQuality;
                    
                    // Only show qualities higher than the highest current quality
                    var qualityTargets = new[]
                    {
                        QualityCategory.Poor,
                        QualityCategory.Normal, 
                        QualityCategory.Good,
                        QualityCategory.Excellent,
                        QualityCategory.Masterwork,
                        QualityCategory.Legendary
                    };
                    
                    options.AddRange(qualityTargets.Where(q => q > highestQuality));
                }
                else
                {
                    // All buildings unmarked - show all options above current for each building
                    var allQualities = group.Comps.SelectMany(c =>
                    {
                        var currentQuality = c.parent.TryGetComp<CompQuality>()?.Quality ?? QualityCategory.Normal;
                        return new[]
                        {
                            QualityCategory.Poor,
                            QualityCategory.Normal, 
                            QualityCategory.Good,
                            QualityCategory.Excellent,
                            QualityCategory.Masterwork,
                            QualityCategory.Legendary
                        }.Where(q => q > currentQuality);
                    }).Distinct().OrderBy(q => q);
                    
                    options.AddRange(allQualities);
                }
            }
            else
            {
                // For marked buildings, show all options above the lowest current quality in the group
                var lowestQuality = group.LowestCurrentQuality;
                var qualityTargets = new[]
                {
                    QualityCategory.Poor,
                    QualityCategory.Normal, 
                    QualityCategory.Good,
                    QualityCategory.Excellent,
                    QualityCategory.Masterwork,
                    QualityCategory.Legendary
                };
                
                options.AddRange(qualityTargets.Where(q => q > lowestQuality));
            }

            return options;
        }

        /// <summary>
        /// Provides UI gizmos (buttons) for the player interface.
        /// Shows consolidated "Improve" dropdown buttons based on selection grouping.
        /// </summary>
        /// <returns>An enumerable of gizmos to display in the UI.</returns>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer) yield break;
            
            var compQuality = parent.TryGetComp<CompQuality>();
            if (compQuality == null || compQuality.Quality == QualityCategory.Legendary) yield break;
            
            if (parent.def.blueprintDef == null) yield break; // Items without blueprints can't be improved
            
            var groups = AnalyzeSelection();
            
            // Only yield gizmos if this comp is the representative for its group
            foreach (var group in groups)
            {
                if (group.Representative == this)
                {
                    yield return CreateGroupGizmo(group, groups);
                }
            }
        }

        /// <summary>
        /// Creates a gizmo for a group of buildings with similar improvement state.
        /// </summary>
        /// <param name="group">The improvement group to create a gizmo for.</param>
        /// <param name="allGroups">All groups in the selection for context.</param>
        /// <returns>A command gizmo for the group.</returns>
        private Command_Action CreateGroupGizmo(ImproveGroup group, List<ImproveGroup> allGroups)
        {
            return new Command_Action
            {
                defaultLabel = GetGroupGizmoLabel(group),
                defaultDesc = GetGroupGizmoDesc(group),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Improve", true),
                action = () => ShowGroupQualityTargetFloatMenu(group, allGroups),
                groupKey = GetGroupGizmoKey(group)
            };
        }

        /// <summary>
        /// Gets the label for a group gizmo based on the group's state.
        /// </summary>
        /// <param name="group">The improvement group.</param>
        /// <returns>The label text for the gizmo.</returns>
        private string GetGroupGizmoLabel(ImproveGroup group)
        {
            var count = group.Comps.Count;
            var countText = count > 1 ? $" ({count})" : "";

            if (!group.IsMarked)
            {
                return "SimpleImprove_GizmoLabel".Translate() + countText;
            }
            
            if (group.TargetQuality.HasValue)
            {
                return "SimpleImprove_GizmoLabelWithTarget".Translate(group.TargetQuality.Value.GetLabel()) + countText;
            }
            
            return "SimpleImprove_GizmoLabelAny".Translate() + countText;
        }

        /// <summary>
        /// Gets the description for a group gizmo.
        /// </summary>
        /// <param name="group">The improvement group.</param>
        /// <returns>The description text for the gizmo.</returns>
        private string GetGroupGizmoDesc(ImproveGroup group)
        {
            var count = group.Comps.Count;
            if (count == 1)
            {
                return "SimpleImprove_GizmoTooltip".Translate();
            }
            
            return "SimpleImprove_GizmoTooltip".Translate() + $" ({count} items)";
        }

        /// <summary>
        /// Gets the group key for gizmo grouping.
        /// </summary>
        /// <param name="group">The improvement group.</param>
        /// <returns>The group key for the gizmo.</returns>
        private int GetGroupGizmoKey(ImproveGroup group)
        {
            // Use a base key and add variation based on group type
            return 2003114091 + group.GroupKey.GetHashCode();
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
            
            if (TargetQuality.HasValue)
            {
                return "SimpleImprove_GizmoLabelWithTarget".Translate(TargetQuality.Value.GetLabel());
            }
            
            return "SimpleImprove_GizmoLabelAny".Translate();
        }
        
        /// <summary>
        /// Shows the float menu for selecting quality targets for a group of buildings.
        /// </summary>
        /// <param name="group">The improvement group to show options for.</param>
        /// <param name="allGroups">All groups in the selection for context.</param>
        private void ShowGroupQualityTargetFloatMenu(ImproveGroup group, List<ImproveGroup> allGroups)
        {
            var options = new List<FloatMenuOption>();

            // Add "Cancel improvement" option if any buildings in group are marked
            if (group.IsMarked)
            {
                options.Add(new FloatMenuOption("SimpleImprove_CancelImprovement".Translate(), () =>
                {
                    foreach (var comp in group.Comps)
                    {
                        comp.IsMarkedForImprovement = false;
                    }
                }));
            }

            // Add "Any improvement" option
            var anyLabel = "SimpleImprove_TargetAny".Translate();
            if (group.IsMarked && group.TargetQuality == null)
            {
                anyLabel += " ✓";
            }
            options.Add(new FloatMenuOption(anyLabel, () =>
            {
                ApplyQualityTargetToGroup(group, allGroups, null);
            }));

            // Add specific quality targets based on the complex rules
            var availableQualities = GetAvailableQualityOptions(group, allGroups);
            
            foreach (var quality in availableQualities)
            {
                var label = quality.GetLabel().CapitalizeFirst();
                if (group.IsMarked && group.TargetQuality == quality)
                {
                    label += " ✓";
                }
                
                options.Add(new FloatMenuOption(label, () =>
                {
                    ApplyQualityTargetToGroup(group, allGroups, quality);
                }));
            }

            if (options.Count > (group.IsMarked ? 2 : 1)) // More than just "Any" option
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
            else if (!group.IsMarked)
            {
                // No valid targets, just mark for any improvement
                ApplyQualityTargetToGroup(group, allGroups, null);
            }
        }

        /// <summary>
        /// Applies a quality target to a group of buildings, implementing the complex selection rules.
        /// </summary>
        /// <param name="group">The group being modified.</param>
        /// <param name="allGroups">All groups for context.</param>
        /// <param name="targetQuality">The target quality to apply.</param>
        private void ApplyQualityTargetToGroup(ImproveGroup group, List<ImproveGroup> allGroups, QualityCategory? targetQuality)
        {
            // Show warning if Legendary quality is selected
            if (targetQuality == QualityCategory.Legendary)
            {
                Messages.Message("SimpleImprove_LegendaryWarning".Translate(), MessageTypeDefOf.CautionInput);
            }
            
            // Check if any pawns can achieve the target quality and show warning if not
            if (targetQuality.HasValue)
            {
                CheckAndShowTargetQualitySkillWarning(targetQuality.Value);
            }
            
            // Special case: if this action comes from a marked group and we're setting a new quality,
            // apply to ALL selected buildings, not just the group
            if (group.IsMarked && allGroups.Count > 1)
            {
                // Apply to all buildings in all groups
                foreach (var g in allGroups)
                {
                    foreach (var comp in g.Comps)
                    {
                        var currentQuality = comp.parent.TryGetComp<CompQuality>()?.Quality ?? QualityCategory.Normal;
                        
                        // Only mark buildings that can achieve the target quality
                        if (targetQuality == null || currentQuality < targetQuality.Value)
                        {
                            comp.TargetQuality = targetQuality;
                            comp.IsMarkedForImprovement = true;
                        }
                    }
                }
            }
            else
            {
                // Apply only to the specific group
                foreach (var comp in group.Comps)
                {
                    var currentQuality = comp.parent.TryGetComp<CompQuality>()?.Quality ?? QualityCategory.Normal;
                    
                    // Only mark buildings that can achieve the target quality
                    if (targetQuality == null || currentQuality < targetQuality.Value)
                    {
                        comp.TargetQuality = targetQuality;
                        comp.IsMarkedForImprovement = true;
                    }
                }
            }
        }

        /// <summary>
        /// Shows the float menu for selecting quality targets (legacy single-building method).
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
            if (TargetQuality == null && isMarkedForImprovement)
            {
                anyLabel += " ✓";
            }
            options.Add(new FloatMenuOption(anyLabel, () =>
            {
                TargetQuality = null;
                IsMarkedForImprovement = true;
            }));
            
            // Add specific quality targets (only those higher than current)
            var qualityTargets = new[]
            {
                QualityCategory.Poor,
                QualityCategory.Normal, 
                QualityCategory.Good,
                QualityCategory.Excellent,
                QualityCategory.Masterwork,
                QualityCategory.Legendary
            };
            
            foreach (var quality in qualityTargets)
            {
                if (quality <= currentQuality) continue; // Can't target lower quality
                
                var label = quality.GetLabel().CapitalizeFirst();
                if (TargetQuality == quality && isMarkedForImprovement)
                {
                    label += " ✓";
                }
                
                options.Add(new FloatMenuOption(label, () =>
                {
                    // Show warning if Legendary quality is selected
                    if (quality == QualityCategory.Legendary)
                    {
                        Messages.Message("SimpleImprove_LegendaryWarning".Translate(), MessageTypeDefOf.CautionInput);
                    }
                    
                    TargetQuality = quality;
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
                TargetQuality = null;
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