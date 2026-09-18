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
    /// Implements <see cref="IConstructible"/> to integrate with RimWorld's construction system, and
    /// <see cref="IThingHolder"/> because it holds the materials hauled towards an improvement.
    /// </summary>
    /// <remarks>
    /// The component is declared on the relevant defs by <see cref="ImprovableDefs"/>, applied once
    /// per play-data load by <c>CompInjectionPatch</c>, which is what allows <c>PostExposeData</c>
    /// below to actually round-trip. Do not mark
    /// this class sealed: <c>ThingWithComps.GetComp&lt;T&gt;</c> fails fast on a sealed type
    /// parameter that its <c>compsByType</c> dictionary does not contain.
    /// </remarks>
    public class SimpleImproveComp : ThingComp, IConstructible, IThingHolder
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
        /// The quality the improvement is aiming at, or <c>null</c> for any improvement at all.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This lived in <see cref="SimpleImproveMapComponent"/> until version 1.0.9, keyed by
        /// <c>thingIDNumber</c>, because the improvement component was attached at runtime and could
        /// not persist anything of its own. It is a field again now that the component is declared on
        /// the defs, and <see cref="SimpleImproveMapComponent"/> says what remains of the old store.
        /// </para>
        /// <para>
        /// The side store was not only redundant, it was unreachable for exactly the buildings that
        /// most needed it. Its accessors went through <c>parent?.Map?.GetComponent</c>, so for any
        /// building that was not on a map the getter returned null and the setter silently discarded
        /// the write. That includes every thing during loading, because <c>Thing.ExposeData</c> forces
        /// <c>mapIndexOrState</c> to -1 on <c>LoadingVars</c> and things are only spawned later in
        /// <c>Map.FinalizeLoading</c>, which is the direct reason the value could not be read back
        /// from <see cref="PostExposeData"/> while it lived there.
        /// </para>
        /// </remarks>
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
                    // Clear materials, target and designation when unmarking. The drop goes through
                    // ReturnStoredMaterialsWhileSpawned rather than being spelled out here, because
                    // the map can be null: both quality float menus build their options as closures
                    // over a captured component and revalidate nothing when clicked, and the game
                    // ticks while the menu is open.
                    ReturnStoredMaterialsWhileSpawned();
                    targetQuality = null;
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
        /// <para>
        /// This method is primarily used by <see cref="Patches.DesignationCancelPatch"/> to avoid
        /// recursive designation removal when cancelling improvements.
        /// <c>DesignationManager.RemoveDesignation</c> fires <c>Designation.Notify_Removing</c>
        /// before it removes the entry from its indexes, so going back through the property would
        /// find the designation still there and recurse.
        /// </para>
        /// <para>
        /// Clearing the flag also clears the target quality, which keeps the invariant that an
        /// unmarked building has no target. Without it the target outlives the mark, and the stale
        /// value is not merely untidy: a building can still show it in the inspect pane while
        /// holding materials, and reinstating the mark would silently reinstate a target the player
        /// last saw cancelled. This is not designation management, so it does not reopen the
        /// recursion this method exists to avoid.
        /// </para>
        /// </remarks>
        public void SetMarkedForImprovementDirect(bool value)
        {
            isMarkedForImprovement = value;

            if (!value)
            {
                targetQuality = null;
            }
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
        /// Gets the directly held things for this component, or <c>null</c> when nothing has ever
        /// been hauled here.
        /// </summary>
        /// <returns>The material container, never <c>null</c>.</returns>
        /// <remarks>
        /// <para>
        /// This must never return null, and an earlier version of it did. Declaring the component on
        /// the defs puts every quality building into <c>ThingRequestGroup.ThingHolder</c>, because
        /// <c>ThingOwnerUtility.ThisOrAnyCompIsThingHolder</c> scans <c>def.comps</c> for a compClass
        /// implementing <see cref="IThingHolder"/>. Almost every vanilla traversal that then reaches a
        /// child holder does null-check the result. Exactly one does not:
        /// <c>ContainingSelectionUtility.SelectableContainedThings</c> walks
        /// <c>ThingWithComps.AllComps</c> and does
        /// <c>foreach (Thing t in (IEnumerable&lt;Thing&gt;)holder.GetDirectlyHeldThings())</c> with no
        /// guard, and a null cast to <c>IEnumerable&lt;Thing&gt;</c> is still null. It is reached from
        /// <c>GenUI.ThingsUnderMouse</c> and <c>Selector.SelectableObjectsUnderMouse</c>, so returning
        /// null threw on ordinary mouse-over, selection and right-click of any improvable building
        /// with nothing staged in it. <c>Verse.Building</c> is not itself an <see cref="IThingHolder"/>,
        /// so the component branch is the one that runs.
        /// </para>
        /// <para>
        /// The reasoning that returned null was not wrong about cost, only about safety, so the cost
        /// is handled where it actually arises. Allocating a small empty <c>ThingOwner</c> per
        /// improvable building the first time something walks it is cheap; writing one into every save
        /// for every quality building is not, and <see cref="PostExposeData"/> now scribes the
        /// container only when it holds something rather than whenever it exists.
        /// </para>
        /// <para>
        /// Vanilla <c>Frame</c> is the precedent: its <c>resourceContainer</c> is built in the
        /// constructor and is never null.
        /// </para>
        /// </remarks>
        public ThingOwner GetDirectlyHeldThings() => GetMaterialContainer();

        /// <summary>
        /// Appends any holders nested inside the stored materials.
        /// </summary>
        /// <param name="outChildren">The list to append child holders to.</param>
        /// <remarks>
        /// Reads the field rather than <see cref="GetMaterialContainer"/> so that walking the holder
        /// tree does not create a container on every quality building that has never been marked.
        /// <c>ThingOwnerUtility.AppendThingHoldersFromThings</c> is what reaches a comp's contents at
        /// all: it walks <c>ThingWithComps.AllComps</c> looking for <see cref="IThingHolder"/>, so
        /// before this component declared the interface its stored materials were invisible to every
        /// traversal the game makes from a map.
        /// </remarks>
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            if (materialContainer == null)
            {
                return;
            }

            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, materialContainer);
        }

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
            
            // Colonists and colony mechs. FreeColonistsSpawned alone excludes mechs, which would
            // warn that nothing can reach the target while a constructoid stood there able to.
            var allPawns = ImproveWorkers.PotentialOnMap(map);
            var capablePawns = allPawns.Where(pawn =>
                ImproveWorkers.IsAssignedToImproving(pawn) &&
                WorkerSkill.Of(pawn).Meets(SimpleImproveMod.Settings.GetSkillRequirement(targetQuality, pawn))
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
            // to avoid double-clearing materials that were already destroyed above. The target goes
            // with the flag: it has been reached, and leaving it behind would re-aim the building at
            // it the moment anything marked it again.
            isMarkedForImprovement = false;
            targetQuality = null;
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
        /// Returns the staged materials to the map the building is standing on.
        /// </summary>
        /// <remarks>
        /// <para>
        /// One of the two owners of returning materials, the other being
        /// <see cref="PostDeSpawn"/>. This one is for the building that stays where it is and only
        /// loses its mark: the cancel gizmo, the cancel designator and vanilla's own
        /// <c>Designator_Cancel</c>, none of which despawn anything.
        /// <see cref="StoredMaterials.OnUnmark"/> carries when it fires and why the map has to be
        /// tested.
        /// </para>
        /// <para>
        /// The field is read rather than <see cref="GetMaterialContainer"/>, which would allocate a
        /// container for every quality building that has never been marked, purely to find it empty.
        /// </para>
        /// </remarks>
        internal void ReturnStoredMaterialsWhileSpawned()
        {
            if (StoredMaterials.OnUnmark(materialContainer?.Any == true, parent.Map != null)
                != MaterialReturn.DropOnMap)
            {
                return;
            }

            materialContainer.TryDropAll(parent.Position, parent.Map, ThingPlaceMode.Near);
        }

        /// <summary>
        /// Returns the staged materials to the map the building is leaving.
        /// </summary>
        /// <param name="map">The map being left. <c>parent.Map</c> is already null by now.</param>
        /// <param name="mode">Why the building is being despawned.</param>
        /// <remarks>
        /// <para>
        /// The single owner of returning materials on every path that removes a building from a map:
        /// deconstruct, uninstall, minify, fire, damage, a wall being smoothed over, and any other
        /// destroy mode. <see cref="StoredMaterials.OnDeSpawn"/> carries the ordering that makes this
        /// first, why the gravship is the thing being tested for, and why that is deliberately not
        /// the <c>mode != DestroyMode.WillReplace</c> guard the seven equivalent vanilla components
        /// use.
        /// </para>
        /// <para>
        /// The <paramref name="map"/> parameter is the only usable map reference in here.
        /// <c>ThingWithComps.DeSpawn</c> captures it before calling <c>base.DeSpawn(mode)</c> and
        /// runs the comps afterwards, so <c>parent.Map</c> has already gone to null.
        /// <c>parent.Position</c> is still good: <c>Thing.DeSpawn</c> never writes
        /// <c>positionInt</c>.
        /// </para>
        /// <para>
        /// This does not remove the improvement designation, and must not. Doing so would call
        /// <c>DesignationManager.RemoveDesignation</c>, which fires <c>Designation.Notify_Removing</c>
        /// and so reaches this mod's own prefix, which clears the mark.
        /// <see cref="PostSpawnSetup"/> only restores a missing designation while the mark is still
        /// set, so clearing it here would silently lose the mark on every gravship jump: the pair
        /// has to survive together, and the designation is already the half that does not.
        /// </para>
        /// </remarks>
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);

            if (StoredMaterials.OnDeSpawn(
                    materialContainer?.Any == true,
                    map != null,
                    parent.BeingTransportedOnGravship) != MaterialReturn.DropOnMap)
            {
                return;
            }

            materialContainer.TryDropAll(parent.Position, map, ThingPlaceMode.Near);
        }

        /// <summary>
        /// Restores the improvement designation when the building arrives on a map without it, and
        /// adopts a target quality left behind in the pre-1.0.9 map component store.
        /// </summary>
        /// <param name="respawningAfterLoad">Whether this spawn is a save being loaded.</param>
        /// <remarks>
        /// <para>
        /// The work giver is designation-driven, so a marked building that has lost its designation is
        /// a building that never gets improved again. Nothing in the game restores it:
        /// <c>Thing.DeSpawn</c> does not touch the designation manager, and <c>Building.DeSpawn</c>
        /// only clears defs that set <c>removeIfBuildingDespawned</c>, which this one does not.
        /// <see cref="ImproveDesignations.RepairNeeded"/> carries the two ordinary ways a building
        /// reaches a new map without its designation, and why the opposite case is left alone.
        /// </para>
        /// <para>
        /// Running on a load as well as on a fresh spawn is deliberate, and it is what repairs a save
        /// that already carries the divergence. The ordering holds:
        /// <c>Scribe.loader.FinalizeLoading</c> runs before a map's <c>FinalizeLoading</c> respawns
        /// its things, so the designation manager is already indexed and answerable by the time this
        /// is asked.
        /// </para>
        /// <para>
        /// <c>AddDesignation</c> unforbids its target, before indexing it, via
        /// <c>SetForbidden(false, warnOnFail: false)</c>. Marking by hand goes through the same call
        /// and so has always done that, but this fires with no player input and no message, so the
        /// forbidden state is captured and put back. Only three shipped defs can even notice
        /// (<c>PlantPot</c>, <c>PlantPot_Bonsai</c> and <c>GibbetCage</c> are the only improvable ones
        /// that also carry <c>CompForbiddable</c>; the Forbid designator refuses anything that is not
        /// an item), which makes this cheap rather than unnecessary: restoring a value is three lines,
        /// and silently discarding a state the player set is the kind of thing nobody reports and
        /// nobody can explain.
        /// </para>
        /// <para>
        /// The other side effect, <c>FleckMaker.ThrowMetaPuffs</c>, is left alone. It is a puff of
        /// motes on a building whose mark is genuinely being restored, which is honest feedback.
        /// </para>
        /// <para>
        /// The migration at the top is the other half of moving target quality onto this component,
        /// and the ordering that makes it safe is not obvious. <c>Game.LoadGame</c> runs the
        /// <c>maps</c> collection through the scribe, and <c>Map.ExposeData</c> reaches
        /// <c>ExposeComponents</c> from inside it, so every map component has loaded its own data
        /// before <c>Scribe.loader.FinalizeLoading</c>, which is itself before
        /// <c>Map.FinalizeLoading</c> spawns the first thing. The old store is therefore fully
        /// populated by the time the first building asks it for a target.
        /// <see cref="SimpleImproveMapComponent.ShouldMigrateTargetQuality"/> says why both of its
        /// conditions are there and <see cref="SimpleImproveMapComponent.TakeTargetQuality"/> says
        /// why it removes what it reads.
        /// </para>
        /// <para>
        /// Only a building standing on a map when a save loads can claim an entry, and that is the
        /// whole of what the migration reaches. A reinstall does not: <c>Frame.CompleteConstruction</c>
        /// calls <c>GenSpawn.Spawn</c> without the <c>respawningAfterLoad</c> argument, whose default
        /// is false. Anything still in the store when the map has finished loading is discarded by
        /// <see cref="SimpleImproveMapComponent.DiscardUnclaimedTargetQualities"/>, which explains
        /// why keeping it would be worse than losing it and why nothing marked is in there.
        /// </para>
        /// <para>
        /// The <c>DesignationOn</c> lookup is paid on every spawn of every improvable building,
        /// including every one on a map load, and not only for marked ones. That is deliberate.
        /// Short-circuiting on <c>isMarkedForImprovement</c> here would move half the decision out of
        /// <see cref="ImproveDesignations.RepairNeeded"/> and into a call site the test harness cannot
        /// reach, which is how the skill guard in this mod came to have no coverage. The cost is a
        /// missing key in a <c>Dictionary&lt;Thing, List&lt;Designation&gt;&gt;</c>, against a fix that
        /// removed a thirty-region search per pawn per job search.
        /// </para>
        /// </remarks>
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            if (SimpleImproveMapComponent.ShouldMigrateTargetQuality(
                    respawningAfterLoad, isMarkedForImprovement, targetQuality.HasValue))
            {
                targetQuality = parent.Map.GetComponent<SimpleImproveMapComponent>()
                    ?.TakeTargetQuality(parent.thingIDNumber);
            }

            var repair = ImproveDesignations.RepairNeeded(
                isMarkedForImprovement,
                parent.Map.designationManager.DesignationOn(parent, SimpleImproveDefOf.Designation_Improve) != null);

            if (repair != ImproveDesignationRepair.AddDesignation)
            {
                return;
            }

            // AddDesignation unforbids the target on the way past. Put it back.
            var forbiddable = parent.TryGetComp<CompForbiddable>();
            var wasForbidden = forbiddable != null && forbiddable.Forbidden;

            parent.Map.designationManager.AddDesignation(
                new Designation(parent, SimpleImproveDefOf.Designation_Improve));

            if (wasForbidden)
            {
                forbiddable.Forbidden = true;
            }
        }

        /// <summary>
        /// Decides whether the material container is worth writing into the save.
        /// </summary>
        /// <param name="container">The container, which may be <c>null</c>.</param>
        /// <returns><c>true</c> only when there is something in it.</returns>
        /// <remarks>
        /// The test is "holds something", not "exists", and the difference is the whole point.
        /// <see cref="GetDirectlyHeldThings"/> has to allocate on demand, because one vanilla caller
        /// throws on a null, so any traversal that walks past a building now leaves it with an empty
        /// container. Writing on non-null would therefore put an empty node into every save for every
        /// quality building on the map, which is the cost the null return was protecting against in
        /// the first place.
        /// </remarks>
        internal static bool ShouldScribeContainer(ThingOwner container)
        {
            return container != null && container.Any;
        }

        /// <summary>
        /// Saves and loads component data for game save files.
        /// </summary>
        /// <remarks>
        /// <para>
        /// These keys are written flat onto the parent thing's node, because
        /// <c>ThingWithComps.ExposeData</c> calls each comp's <c>PostExposeData</c> directly rather
        /// than wrapping it. That is why saves written before the component was declared on the def
        /// still carry readable values here, and why this fix recovers work and materials from an
        /// existing save rather than only preventing the next loss.
        /// </para>
        /// <para>
        /// <c>targetQuality</c> is scribed with no default argument, which is both what vanilla does
        /// for a nullable and the only spelling that behaves. Every <c>Scribe_Values.Look</c> call on
        /// a <c>Nullable&lt;T&gt;</c> field in the whole game assembly omits it, among them
        /// <c>ThingStuffPairWithQuality</c>, <c>ScenPart_ThingCount</c> and <c>SketchThing</c>, all
        /// three on this same <c>QualityCategory?</c>. Passing one would not break the round trip but
        /// it would change the file: <c>Scribe_Values.Look</c> only skips a null when the default is
        /// also null, so a non-null default writes an explicit <c>IsNull="True"</c> node for every
        /// building that has no target. A sentinel is not available either way, since
        /// <c>QualityCategory</c> is byte-backed with no unset member and <c>Awful</c> is zero.
        /// </para>
        /// </remarks>
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref isMarkedForImprovement, "isMarkedForImprovement", false);
            Scribe_Values.Look(ref workDone, "workDone", 0f);
            Scribe_Values.Look(ref targetQuality, "targetQuality");

            // Only write the container when it actually holds something. The component is on every
            // improvable building def, so scribing unconditionally would add a node to every quality
            // building in every save, the overwhelming majority of which have never been marked. An
            // absent node leaves the field null on load and GetMaterialContainer creates it on demand.
            //
            // The test is "holds something", not "exists", because GetDirectlyHeldThings has to
            // allocate on demand to avoid throwing in ContainingSelectionUtility. Any traversal that
            // walks past a building now gives it an empty container, and gating on non-null would put
            // every one of those into the save.
            if (Scribe.mode != LoadSaveMode.Saving || ShouldScribeContainer(materialContainer))
            {
                Scribe_Deep.Look(ref materialContainer, "materialContainer", this);
            }
        }

        /// <summary>
        /// Destroys anything still staged in the building when it is destroyed.
        /// </summary>
        /// <param name="mode">The mode of destruction.</param>
        /// <param name="previousMap">The map the thing was held on before destruction.</param>
        /// <remarks>
        /// <para>
        /// This used to be where materials came back, under <c>DestroyMode.Deconstruct</c> alone,
        /// and that is now <see cref="PostDeSpawn"/>'s job for every mode.
        /// <c>Thing.Destroy</c> despawns before it does anything else, so by the time this runs the
        /// container has already been emptied onto the map for any building that was standing on one.
        /// </para>
        /// <para>
        /// What is left is the building that is destroyed without ever being despawned, because it
        /// was inside something: a minified building in a stockpile that burns, or one carried by a
        /// caravan. Dropping is not available there and never was. Destroying the contents rather
        /// than letting the container fall out of scope with things still in it is the tidier of the
        /// two, and it is what vanilla containers do when they cannot place what they hold.
        /// </para>
        /// <para>
        /// Note that <paramref name="previousMap"/> is <c>MapHeld</c> rather than <c>Map</c>:
        /// <c>ThingWithComps.Destroy</c> captures it by walking the holder chain, so it can be
        /// non-null for a building that was never spawned. It is not used here, because the
        /// building's own <c>Position</c> is meaningless once it has been inside a container and a
        /// drop would land somewhere arbitrary.
        /// </para>
        /// </remarks>
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);

            // The field, not GetMaterialContainer: the component is on every improvable building def,
            // so the lazy getter would build a container for each one as it is destroyed purely to
            // find it empty.
            if (materialContainer?.Any == true)
            {
                materialContainer.ClearAndDestroyContents();
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
            
            // Field, not GetMaterialContainer: this runs for every quality building the player
            // selects, and the lazy getter would allocate a container for each one.
            if (!isMarkedForImprovement && materialContainer?.Any != true)
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