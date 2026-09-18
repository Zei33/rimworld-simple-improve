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

        /// <summary>
        /// Gets whether the group's gizmo says how many buildings it covers.
        /// </summary>
        /// <value><c>true</c> for two buildings or more; <c>false</c> for one.</value>
        /// <remarks>
        /// The label and the tooltip both read this, so they cannot disagree about where the count
        /// starts: the label gains its count and the tooltip switches to the group sentence at the
        /// same size. It is a property on the group rather than a comparison at each call site
        /// because the call sites need a spawned selection and cannot be run by the test suite,
        /// while this can, and a comparison moved from two to three would otherwise describe a pair
        /// of buildings as one.
        /// </remarks>
        public bool ShowsCount => Comps.Count > 1;
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
                    // the map can be null: the group quality float menu builds its options as
                    // closures over captured components and revalidates nothing when clicked, and
                    // the game ticks while the menu is open.
                    ReturnStoredMaterialsWhileSpawned();
                    targetQuality = null;
                    parent.Map?.designationManager.TryRemoveDesignationOn(parent, SimpleImproveDefOf.Designation_Improve);
                }
                else if (value && !isMarkedForImprovement)
                {
                    // Refuse to mark when there is no designation manager to write to. The `?.` here
                    // used to swallow a null map and then set the flag anyway, leaving the component
                    // marked with no designation; since the work giver became designation-driven,
                    // that building is invisible to it, so the mark silently did nothing. The null
                    // map is reachable for the same reason the unmark path documents above: the
                    // group float menu captures this component in a closure and revalidates nothing
                    // when clicked, while the game ticks.
                    //
                    // The guard is on this branch only. Unmarking off-map must still clear the flag
                    // and the target, or a building minified while its menu was open would come back
                    // marked with materials it cannot use.
                    if (parent.Map == null)
                    {
                        return;
                    }

                    // Refuse a mark with nothing ahead of it: a Legendary building marked for any
                    // improvement, or a target the building already has. The work giver refuses
                    // such a mark too, so it would sit there doing nothing, and before the giver
                    // refused it the colony delivered and destroyed the full cost on every cycle.
                    // The group float menu reached it by racing the game: it captures its
                    // components when it opens, the game keeps ticking, and a pawn could finish the
                    // building at Legendary before the player clicked "Any improvement".
                    //
                    // This is the one place every transition from unmarked to marked passes, so it
                    // holds for every caller, including a third party writing the public property.
                    // The target has to be written before the flag, as TryMarkFor does, because it
                    // is part of what is being judged. Re-aiming a building that is already marked
                    // never reaches this branch, which is why TryMarkFor asks the same question
                    // itself rather than relying on this one.
                    if (!IsOutstandingFor(targetQuality))
                    {
                        return;
                    }

                    // DesignationOn first, because DesignationManager.AddDesignation logs a red error
                    // and returns on a double add. A save written by 1.0.5 or 1.0.6 can carry a
                    // designation whose component flag is false, and re-marking it hit exactly that.
                    if (parent.Map.designationManager.DesignationOn(parent, SimpleImproveDefOf.Designation_Improve) == null)
                    {
                        parent.Map.designationManager.AddDesignation(
                            new Designation(parent, SimpleImproveDefOf.Designation_Improve));
                    }
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
        /// <remarks>
        /// <para>
        /// The floor is deliberate and is NOT a transcription error against vanilla. Vanilla divides
        /// by this stat unfloored in both <c>JobDriver_ConstructFinishFrame</c> and
        /// <c>Frame.PercentComplete</c>, so an auditor diffing against the game will want to remove
        /// it. Vanilla gets away with it; this mod would not, because it divides in two places and
        /// one of them decides whether the improvement FAILS.
        /// </para>
        /// <para>
        /// At zero, <c>speed / WorkToBuild</c> is positive infinity,
        /// <c>Mathf.Pow(successChance, infinity)</c> is zero for any chance below one, so the fail
        /// roll is <c>Rand.Value &lt; 1f</c> and always true. The pawn destroys the staged materials
        /// every tick it works and the work giver re-issues the job, while the progress bar reads
        /// 0/0 as NaN. The stat cannot go negative, since <c>StatWorker.FinalizeValue</c> clamps at
        /// the def's <c>minValue</c> of 0, so zero is the only bad value.
        /// </para>
        /// <para>
        /// No shipped improvable def is at risk: the nine defs declaring
        /// <c>&lt;WorkToBuild&gt;0&lt;/WorkToBuild&gt;</c> are all spots (sleeping, marriage, party,
        /// crafting, butcher, meditation, ritual, caravan packing) and none carries
        /// <c>CompQuality</c>, so none qualifies. It is reachable anyway: a custom scenario's
        /// <c>ScenPart_StatFactor</c> accepts 0% and is applied before the clamp, a modded stuff can
        /// carry a <c>WorkToBuild</c> stat factor of zero, and a modded improvable def can simply
        /// declare it. Flooring in the property rather than at the two call sites is what keeps
        /// <see cref="WorkLeft"/> and the inspect string agreeing with them.
        /// </para>
        /// </remarks>
        public float WorkToBuild =>
            Mathf.Max(parent.def.GetStatValueAbstract(StatDefOf.WorkToBuild, parent.Stuff), 1f);
        
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
        /// <remarks>
        /// The setter checks nothing, and the mod itself never writes through it: the group menu
        /// marks and re-aims through <see cref="TryMarkFor"/>, which refuses a target the building
        /// is already at or past. A caller writing this directly on a building that is already
        /// marked re-aims it with no such check. Nothing is destroyed if the new target is behind
        /// the building, because the work giver and both job drivers ask
        /// <see cref="HasOutstandingImprovement"/> and leave such a mark alone, but the mark then
        /// does nothing until it is re-aimed or cancelled.
        /// </remarks>
        public QualityCategory? TargetQuality
        {
            get => targetQuality;
            set => targetQuality = value;
        }

        /// <summary>
        /// Gets whether this building is marked and the mark still has work ahead of it.
        /// </summary>
        /// <value>
        /// <c>true</c> when the building is marked for improvement and is below the quality the mark
        /// aims at; <c>false</c> when it is unmarked, or marked with nothing left to do.
        /// </value>
        /// <remarks>
        /// <para>
        /// This, not <see cref="IsMarkedForImprovement"/>, is the question anything deciding whether
        /// to do improvement work must ask. The flag says a mark exists; it does not say the mark
        /// can still be satisfied. The work giver and both job drivers used to read the flag alone,
        /// so a building marked for any improvement and then raised to Legendary by something else
        /// was hauled for and worked on every cycle, and <see cref="CompleteImprovement"/> can only
        /// fail at Legendary, destroying the delivered materials each time and leaving the mark in
        /// place for the next cycle.
        /// </para>
        /// <para>
        /// The work giver refuses on this and the job drivers stop on it, and all three read the
        /// same property, so a job the giver hands out cannot fail its first tick in the driver and
        /// <c>HasJobOnThing</c> still answers from the same decision as <c>JobOnThing</c>.
        /// </para>
        /// <para>
        /// A mark this refuses is left in place on purpose rather than cleared. The work giver is
        /// where it is noticed, and a scan validator must not mutate anything: clearing the mark
        /// removes a designation, returns the staged materials onto the map and ends other pawns'
        /// jobs through the <c>Notify_Removing</c> prefix. The player clears it instead, and that
        /// path returns the materials: vanilla's Cancel works on any marked building, a Legendary
        /// one gets the cancel button <see cref="CompGetGizmosExtra"/> shows when improvement can no
        /// longer be offered, and one whose target is merely passed keeps its ordinary improve menu,
        /// which leads with Cancel improvement and can re-aim it higher.
        /// </para>
        /// </remarks>
        public bool HasOutstandingImprovement => isMarkedForImprovement && IsOutstandingFor(targetQuality);

        /// <summary>
        /// Determines whether a mark aimed at a target would still have work ahead of it on this
        /// building.
        /// </summary>
        /// <param name="target">The quality the mark aims at, or <c>null</c> for any improvement.</param>
        /// <returns>
        /// <c>true</c> when the building carries a quality and that quality is below
        /// <paramref name="target"/>, or below Legendary for a <c>null</c> target.
        /// </returns>
        /// <remarks>
        /// A building with no <c>CompQuality</c> has nothing to improve and is refused.
        /// <see cref="ImprovableDefs"/> declares this component only on defs that carry one, so that
        /// is a guard against another mod stripping comps rather than a state vanilla produces;
        /// without it <see cref="CompleteImprovement"/> would log an error and return on every
        /// cycle while the mark stayed in place.
        /// </remarks>
        internal bool IsOutstandingFor(QualityCategory? target)
        {
            CompQuality compQuality = parent.TryGetComp<CompQuality>();
            return compQuality != null && ImproveTarget.IsOutstanding(compQuality.Quality, target);
        }

        /// <summary>
        /// Marks this building for improvement towards a target, or re-aims its existing mark, if
        /// that target is still ahead of it.
        /// </summary>
        /// <param name="target">The quality to aim at, or <c>null</c> for any improvement.</param>
        /// <returns>
        /// <c>true</c> when the building ends up marked and aimed at <paramref name="target"/>;
        /// <c>false</c> when nothing was changed.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The one way the group float menu marks anything, and the check has to live here as well
        /// as in the <see cref="IsMarkedForImprovement"/> setter. The setter only judges a building
        /// going from unmarked to marked; re-aiming one that is already marked never reaches that
        /// branch, and an already marked building sits in a group whose menu offers every quality
        /// above the group's lowest, so without this a Masterwork building could be re-aimed at
        /// Good. The "any improvement" option used to skip the quality test entirely, which is how
        /// a building that reached Legendary while the menu was open got marked again.
        /// </para>
        /// <para>
        /// The target is written before the flag because the setter judges the target it finds.
        /// If the setter then refuses, which it does when the building has no map to carry a
        /// designation, the target is put back to <c>null</c>: the building was unmarked, and an
        /// unmarked building has no target. Leaving it would re-aim the building at a stale quality
        /// the next time anything marked it without choosing one.
        /// </para>
        /// </remarks>
        internal bool TryMarkFor(QualityCategory? target)
        {
            if (!IsOutstandingFor(target))
            {
                return false;
            }

            targetQuality = target;
            IsMarkedForImprovement = true;

            if (!isMarkedForImprovement)
            {
                targetQuality = null;
                return false;
            }

            return true;
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
        /// <remarks>
        /// This used to return a <c>cachedMaterialsNeeded</c> field, cleared on entry and refilled,
        /// so every caller held a live alias to state the next call emptied. Nothing was saving an
        /// allocation by it, because the loop below allocates a fresh <c>ThingDefCountClass</c> per
        /// entry per call either way, and the alias was a real hazard one line wide:
        /// <c>CompInspectStringExtra</c> took this list, then called
        /// <see cref="GetRemainingMaterialCost"/>, which re-entered here and cleared the very list it
        /// was about to iterate. It survived only because the refill reproduced identical content.
        /// </remarks>
        public List<ThingDefCountClass> GetTotalMaterialCost()
        {
            var needed = new List<ThingDefCountClass>();

            // If materials are not required, return empty list
            if (!SimpleImproveMod.Settings.RequireMaterials)
            {
                return needed;
            }

            var baseCost = parent.def.CostListAdjusted(parent.Stuff, false);

            foreach (var material in baseCost)
            {
                // Apply the material cost multiplier to the full build cost first
                var adjustedBuildCost = Mathf.CeilToInt(material.count * SimpleImproveMod.Settings.MaterialCostMultiplier);

                if (adjustedBuildCost > 0)
                {
                    needed.Add(new ThingDefCountClass(material.thingDef, adjustedBuildCost));
                }
            }

            return needed;
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
            
            // GetTotalMaterialCost(), not a field. This read cachedMaterialsNeeded without ever
            // populating it, so the answer was whatever the last call on this component instance had
            // left behind. Its only consumer is JobDriver_HaulToImprove, which sizes the deposit with
            // it one line before the transfer that would have repopulated it, so on a cold component
            // the count was 0, the transfer moved nothing, and the pawn walked away still carrying.
            var material = GetTotalMaterialCost().FirstOrDefault(m => m.thingDef == stuff);
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
        /// <remarks>
        /// With a target set this is <see cref="ImproveTarget.IsOutstanding"/> itself, and it has to
        /// be. The work giver refuses any mark that is not outstanding, so if the rule for carrying
        /// on after a success ever kept a mark the giver would refuse, the loop would strand the
        /// building on its own. Asking the same function is what makes that impossible rather than
        /// merely true today. With no target the mark is finished by any success, although more
        /// improvement is still possible: "any improvement" means one.
        /// </remarks>
        private bool ShouldContinueImproving(QualityCategory currentQuality)
        {
            return TargetQuality.HasValue && ImproveTarget.IsOutstanding(currentQuality, TargetQuality);
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
        /// loses its mark: the mod's own cancel options and vanilla's
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
                    sb.AppendLine("SimpleImprove_StoredMaterialsNotRequired".Translate());
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
                        sb.AppendLine("SimpleImprove_MinimumSkillFor".Translate(
                            TargetQuality.Value.GetLabel(), skillReq));
                    }
                }
                else
                {
                    // For "Any" improvement, show that no specific skill is required
                    sb.AppendLine("SimpleImprove_AnySkillAccepted".Translate());
                }
                
                // Show note if materials are disabled
                if (!SimpleImproveMod.Settings.RequireMaterials)
                {
                    sb.AppendLine("SimpleImprove_MaterialsNotRequired".Translate());
                }
            }
            
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Analyzes the current selection and groups buildings by their improvement state.
        /// </summary>
        /// <returns>List of improvement groups, which must be treated as read-only.</returns>
        /// <remarks>
        /// The work moved to <see cref="ImproveSelection"/>, which does it once per frame for the
        /// whole selection instead of once per selected comp. This method ran the full scan every
        /// time it was called, and it is called from <see cref="CompGetGizmosExtra"/>, which vanilla
        /// calls for each selected thing, so the comp-lookup count was quadratic in the size of the
        /// selection. <see cref="ImproveSelection.Current"/> carries the cache key and why it is not
        /// simply the frame number.
        /// </remarks>
        private List<ImproveGroup> AnalyzeSelection()
        {
            return ImproveSelection.Current();
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
            if (!CanBeOfferedImprovement())
            {
                // The building cannot be offered improvement any more, but it may still be carrying a
                // mark from when it could. Issue #23 said that yielding nothing here left no button
                // to remove the mark, and that was never true. Designation_Improve leaves
                // designateCancelable at its default of true, so vanilla's reverse Designator_Cancel
                // draws its own "Cancel" on the gizmo bar of every marked building, this one
                // included, and has in every version of this mod. What this button adds is the
                // explanation: the Improve button has gone, and this says why. Its C hotkey never
                // fires, because vanilla's Cancel is ordered first (-20 against 0) and a hotkey binds
                // only to the first gizmo drawn with it. That is harmless: C then reaches vanilla's
                // Cancel, which clears the same mark through the Notify_Removing prefix.
                //
                // Nobody works on such a building any more either. The work giver and both job
                // drivers ask HasOutstandingImprovement, which is false for a mark with nothing
                // ahead of it, so the mark only waits here to be cancelled.
                if (isMarkedForImprovement)
                {
                    yield return CreateStrandedCancelGizmo();
                }

                yield break;
            }

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
        /// Decides whether this building can be offered an improvement gizmo at all.
        /// </summary>
        /// <returns><c>true</c> when the full improve control belongs on this building.</returns>
        /// <remarks>
        /// <para>
        /// Only two of these four gates are reachable on a building that is currently marked, and it
        /// is worth recording which, because the issue assumed all four were and that made the fix
        /// look bigger than it is.
        /// </para>
        /// <para>
        /// The two comp tests are effectively dead. <see cref="ImprovableDefs"/> declares this
        /// component only on a def that has BOTH a <c>blueprintDef</c> and a <c>CompQuality</c>, and
        /// <c>ThingWithComps.InitializeComps</c> rebuilds a thing's comps strictly from
        /// <c>def.comps</c> on load. So a def that loses either one loses this component too, its
        /// scribed state is orphaned XML that nothing reads, and this method never runs. They are
        /// kept as cheap guards against another mod stripping comps after injection, not because the
        /// state the issue described can occur.
        /// </para>
        /// <para>
        /// The faction test is reachable only through dev tools or another mod: no vanilla path turns
        /// a spawned player building into a non-player one. The quality test is reachable the same
        /// two ways and no others. The mod's own loop clears the mark when it reaches Legendary,
        /// vanilla sets an existing building's quality only from the dev tools' Set Quality action,
        /// and the one in-game route, clicking "Any improvement" in a group menu left open while a
        /// pawn finished the building at Legendary, is closed: marking goes through
        /// <see cref="TryMarkFor"/> and the <see cref="IsMarkedForImprovement"/> setter, and both
        /// refuse a mark with nothing ahead of it.
        /// </para>
        /// <para>
        /// The quality test is <see cref="ImproveTarget.CanBeOffered"/> rather than a comparison
        /// against Legendary, so that offering improvement and accepting a mark are the same
        /// question. If they drifted, the menu would offer "Any improvement" on a building the
        /// marking path then silently refused. It takes the building's quality and nothing else, so
        /// there is no target argument here that could drift either.
        /// </para>
        /// </remarks>
        private bool CanBeOfferedImprovement()
        {
            if (parent.Faction != Faction.OfPlayer)
            {
                return false;
            }

            if (parent.def.blueprintDef == null)
            {
                return false;
            }

            CompQuality quality = parent.TryGetComp<CompQuality>();
            return quality != null && ImproveTarget.CanBeOffered(quality.Quality);
        }

        /// <summary>
        /// Creates the cancel-only button for a building that is marked but can no longer be improved.
        /// </summary>
        /// <returns>A command that clears the mark.</returns>
        /// <remarks>
        /// <para>
        /// The shape is vanilla's. <c>CompPlantable</c> yields a cancel-only <c>Command_Action</c>
        /// from <c>CompGetGizmosExtra</c> gated purely on "the state that needs cancelling exists",
        /// as an independent branch rather than an else-arm of the start button, and
        /// <c>CompHoldingPlatformTarget</c> does the same twice. There is no vanilla gizmo anywhere
        /// that removes a <c>Designation</c>, so the precedent is the shape, not the effect.
        /// </para>
        /// <para>
        /// This deliberately does NOT go through the group machinery, and does not need to. Vanilla
        /// merges gizmos itself: <c>Command.GroupsWith</c> returns true when the hotkey, label, icon
        /// reference and group key all match, and <c>GizmoGridDrawer</c> then draws one button and
        /// fires every member's action on click, because <c>alsoClickIfOtherInGroupClicked</c>
        /// defaults true. Selecting six stranded buildings therefore shows one "Cancel improvement"
        /// button that clears all six, beside vanilla's own "Cancel", which merges the same way, and
        /// there is no representative to pick. The label is what that depends on: a per-building
        /// count or name in it would split the merge into one button per building. The icon is a
        /// shared static too, for the reasons <see cref="ImproveSelection.CancelIcon"/> gives, but a
        /// fetch per button would return the same texture today, so the merge does not rest on it.
        /// </para>
        /// <para>
        /// Setting the property rather than the field is deliberate. The setter returns the staged
        /// materials itself and then removes the designation, and removing it runs the
        /// <c>Notify_Removing</c> prefix, which ends any improve or haul job still aimed at the
        /// building. Setting the field alone would leave the designation and the materials behind.
        /// </para>
        /// </remarks>
        private Command_Action CreateStrandedCancelGizmo()
        {
            return new Command_Action
            {
                defaultLabel = "SimpleImprove_CancelImprovement".Translate(),
                defaultDesc = "SimpleImprove_CancelImprovementStranded".Translate(),
                icon = ImproveSelection.CancelIcon,
                hotKey = KeyBindingDefOf.Designator_Cancel,
                action = () => IsMarkedForImprovement = false
            };
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
                icon = ImproveSelection.Icon,
                action = () => ShowGroupQualityTargetFloatMenu(group, allGroups),
                groupKey = GetGroupGizmoKey(group)
            };
        }

        /// <summary>
        /// Gets the label for a group gizmo based on the group's state.
        /// </summary>
        /// <param name="group">The improvement group.</param>
        /// <returns>The label text for the gizmo.</returns>
        /// <remarks>
        /// The count goes through <c>SimpleImprove_GizmoLabelCount</c> rather than being appended in
        /// code. It used to be a literal <c>" (N)"</c> joined after <c>Translate()</c>, which carries
        /// no word to translate but did fix the punctuation, so Chinese and Japanese, whose labels
        /// close in fullwidth parentheses, got an ASCII pair after them with a space in between. The
        /// key lets each language choose. It must not carry anything that varies per building other
        /// than the count, because only the group's representative draws this gizmo and nothing
        /// merges it; the stranded cancel button, which does rely on vanilla merging identical
        /// labels, is a different gizmo.
        /// </remarks>
        private string GetGroupGizmoLabel(ImproveGroup group)
        {
            TaggedString label;

            if (!group.IsMarked)
            {
                label = "SimpleImprove_GizmoLabel".Translate();
            }
            else if (group.TargetQuality.HasValue)
            {
                label = "SimpleImprove_GizmoLabelWithTarget".Translate(group.TargetQuality.Value.GetLabel());
            }
            else
            {
                label = "SimpleImprove_GizmoLabelAny".Translate();
            }

            if (!group.ShowsCount)
            {
                return label;
            }

            return "SimpleImprove_GizmoLabelCount".Translate(label, group.Comps.Count);
        }

        /// <summary>
        /// Gets the description for a group gizmo.
        /// </summary>
        /// <param name="group">The improvement group.</param>
        /// <returns>The description text for the gizmo.</returns>
        /// <remarks>
        /// The group case is one whole translated sentence with the count as <c>{0}</c>. It used to
        /// be the single-building sentence with the literal English " (N items)" appended after
        /// <c>Translate()</c>, so the word "items" showed in every language. A whole sentence also
        /// lets Chinese and Japanese use fullwidth parentheses with no space before them, which a
        /// suffix joined in code could not. The count wording starts from vanilla's
        /// <c>CountToDesignate</c> ("{0} affected"), the game's own phrase for how many things an
        /// order applies to, in a form that does not change with the number: this branch runs only
        /// for two or more, and Russian and Polish would otherwise need different noun forms for 2
        /// to 4 and for 5 upwards. Russian writes it as a label and a count, <c>выделено: {0}</c>,
        /// where vanilla's cursor label has <c>{0} выделено</c>.
        /// </remarks>
        private string GetGroupGizmoDesc(ImproveGroup group)
        {
            if (!group.ShowsCount)
            {
                return "SimpleImprove_GizmoTooltip".Translate();
            }

            return "SimpleImprove_GizmoTooltipGroup".Translate(group.Comps.Count);
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
        /// <remarks>
        /// Every building goes through <see cref="TryMarkFor"/>, which skips one the target is not
        /// ahead of. This runs from a float menu option many frames after the menu captured its
        /// components, with the game ticking in between, so the quality it judges has to be read at
        /// the click rather than trusted from when the menu opened. The "any improvement" option
        /// used to skip that test altogether.
        /// </remarks>
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
                        comp.TryMarkFor(targetQuality);
                    }
                }
            }
            else
            {
                // Apply only to the specific group
                foreach (var comp in group.Comps)
                {
                    comp.TryMarkFor(targetQuality);
                }
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