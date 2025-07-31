using Verse;
using SimpleImprove.Core;

namespace SimpleImprove.Utils
{
    /// <summary>
    /// Custom ThingOwner that restricts material storage based on improvement requirements.
    /// Only allows storage of materials that are actually needed for the improvement process.
    /// Provides intelligent filtering to prevent unnecessary material accumulation.
    /// </summary>
    public class MaterialStorage : ThingOwner<Thing>
    {
        /// <summary>
        /// Reference to the improvement component that owns this storage.
        /// </summary>
        private readonly SimpleImproveComp improveComp;
        
        /// <summary>
        /// Flag to temporarily bypass storage restrictions during special operations.
        /// </summary>
        private bool allowAllOperations = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="MaterialStorage"/> class.
        /// </summary>
        /// <param name="comp">The improvement component that owns this storage.</param>
        public MaterialStorage(SimpleImproveComp comp) : base(null, false)
        {
            improveComp = comp;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MaterialStorage"/> class by transferring contents from an existing owner.
        /// Used when upgrading or replacing storage containers.
        /// </summary>
        /// <param name="oldOwner">The existing thing owner to transfer contents from.</param>
        /// <param name="comp">The improvement component that owns this storage.</param>
        public MaterialStorage(ThingOwner<Thing> oldOwner, SimpleImproveComp comp) : base(null, false)
        {
            improveComp = comp;
            
            // Force transfer all contents
            allowAllOperations = true;
            oldOwner.TryTransferAllToContainer(this);
            allowAllOperations = false;
        }

        /// <summary>
        /// Determines how many items of the specified type can be accepted into storage.
        /// Only allows items that are actually needed for the improvement process.
        /// </summary>
        /// <param name="item">The item to check acceptance for.</param>
        /// <param name="canMergeWithExistingStacks">Whether to consider merging with existing stacks.</param>
        /// <returns>The number of items that can be accepted, or 0 if the item is not needed.</returns>
        public override int GetCountCanAccept(Thing item, bool canMergeWithExistingStacks = true)
        {
            if (allowAllOperations)
                return base.GetCountCanAccept(item, canMergeWithExistingStacks);
            
            if (!improveComp.IsMarkedForImprovement)
                return 0;
            
            var neededMaterial = improveComp.GetRemainingMaterialCost()
                .Find(m => m.thingDef == item.def);
            
            return neededMaterial?.count ?? 0;
        }

        /// <summary>
        /// Attempts to add a specific count of items to the storage.
        /// Respects improvement requirements unless special operations are allowed.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <param name="count">The number of items to add.</param>
        /// <param name="canMergeWithExistingStacks">Whether to merge with existing stacks.</param>
        /// <returns>The number of items actually added.</returns>
        public override int TryAdd(Thing item, int count, bool canMergeWithExistingStacks = true)
        {
            if (!allowAllOperations && !improveComp.IsMarkedForImprovement)
                return 0;
            
            return base.TryAdd(item, count, canMergeWithExistingStacks);
        }

        /// <summary>
        /// Attempts to add an entire item to the storage.
        /// Respects improvement requirements unless special operations are allowed.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <param name="canMergeWithExistingStacks">Whether to merge with existing stacks.</param>
        /// <returns><c>true</c> if the item was added successfully; otherwise, <c>false</c>.</returns>
        public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
        {
            if (!allowAllOperations && !improveComp.IsMarkedForImprovement)
                return false;
            
            return base.TryAdd(item, canMergeWithExistingStacks);
        }
    }
}