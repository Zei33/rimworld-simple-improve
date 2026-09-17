# SimpleImprove Features

## Core Functionality

### Quality Improvement System
- Mark any furniture or constructed item with quality for improvement
- Pawns with sufficient construction skill will gather materials and attempt to improve quality
- Quality is re-rolled based on pawn's skill level
- If the new quality is not better, materials are consumed but quality remains unchanged
- If improvement succeeds, the item gains the new quality level

### Material Requirements
- **Configurable Material Costs**: Option to require materials like normal construction, or just time and labor
- **Material Cost Percentage**: Adjustable cost from 5% of the original build cost upwards (default 100%)
- **Traditional Mode** (materials required):
  - Improvement requires materials equal to a percentage of the item's original build cost (default the same as normal build cost)
  - Material cost can be adjusted from 5% (very cheap) upwards. 
  - Default setting of 100% means improvements cost the same as building the item from scratch
  - Materials are stored in the item temporarily during the improvement process
  - If improvement is cancelled, materials are dropped nearby
- **Labor-Only Mode** (materials disabled):
  - Improvements only require pawn work time
  - No material gathering or storage needed
  - Eliminates material waste from failed improvement attempts
  - Focus purely on skill development and time investment

### Skill Requirements and Quality Standards

#### Quality Standards Presets
Choose from pre-configured skill requirement levels:

- **🌱 Apprentice**: Very low skill requirements - allows any pawn to attempt improvements with high failure rates
- **📚 Novice**: Low skill requirements - most pawns can attempt improvements with moderate success rates
- **⚖️ Default**: Balanced skill requirements - ensures reasonable success chances for skilled pawns
- **🎯 Master**: High skill requirements - only skilled pawns can attempt improvements with high success rates
- **🏆 Artisan**: Very high skill requirements - only master craftsmen can attempt improvements with very high success rates
- **🛠️ Custom**: Set your own minimum skill requirements for each quality tier

#### Default Skill Requirements (Default Preset)
- Awful: 0
- Poor: 0  
- Normal: 4
- Good: 10
- Excellent: 14
- Masterwork: 18
- Legendary quality cannot be achieved through normal improvement (requires special circumstances)

### Target Quality Persistence
- **Cross-Save Persistence**: Target quality settings automatically survive save/load cycles
- **MapComponent Storage**: Uses RimWorld's native save system for reliable data persistence
- **Automatic Restoration**: Target qualities restored when loading saves with improvement designations
- **Data Integrity**: Automatic validation and cleanup prevent corruption from destroyed items
- **Mod Safety**: Save files remain valid and uncorrupted even if mod is disabled/uninstalled
- **Performance Optimized**: Efficient storage with minimal memory footprint and periodic cleanup
- **Backward Compatibility**: Works seamlessly with saves created before this persistence system

### Pawn Modifiers
- **Inspired Creativity**: Boosts quality roll by 2 tiers
- **Production Specialist Role** (Ideology DLC): Boosts quality roll by 1 tier
- These modifiers stack and can enable reaching Legendary quality

## User Interface

### Multi-Building Selection
- **Intelligent Grouping**: When multiple buildings are selected, they are automatically grouped by improvement state:
  - **Unmarked Buildings**: All buildings not yet marked for improvement
  - **Same Target Quality**: Buildings marked for the same quality target (e.g., all targeting "Excellent")
  - **Different Target Quality**: Separate groups for each different quality target
- **Consolidated Controls**: Instead of showing duplicate buttons, one representative button appears per group
- **Smart Quality Options**: Available quality targets are filtered based on selection composition:
  - When mixing marked and unmarked buildings, options for unmarked buildings are limited to qualities higher than the highest-quality building in that group
  - When selecting from existing quality target groups, choosing a new quality applies to ALL selected buildings
  - Buildings at or above the selected target quality are automatically excluded from marking
- **Visual Indicators**: Button labels show the number of buildings in each group (e.g., "Improve (3)")

### Designators
- **Mark for Improvement**: Select items to queue for quality improvement
- **Cancel Improvement**: Remove items from the improvement queue
- Both support drag selection for multiple items

### Item Gizmos
- **Smart Consolidated Buttons**: When multiple buildings are selected, the mod intelligently groups them and shows consolidated improvement buttons instead of duplicates
- **Quality Target Selection**: Dropdown menu allows choosing specific quality targets (Poor, Normal, Good, Excellent, Masterwork) or "Any improvement"
- **Context-Aware Options**: Available quality options adapt based on selection:
  - **All Unmarked**: Shows all quality options above each building's current quality
  - **Mixed Selection**: Unmarked buildings show limited options based on highest quality in selection
  - **Different Targets**: Separate buttons for each target quality group, with cross-group quality setting affecting all selected buildings
- Shows current improvement status and target quality

### Enhanced Settings Menu
- **Quality Standards Presets**: Quick selection from pre-configured difficulty levels
- **Preset Tooltips**: Detailed explanations of each preset's skill requirements and strategy
- **Custom Configuration**: Full control over individual skill requirements when using Custom preset
- **Advanced Settings**: 
  - Toggle material requirements on/off
  - Adjust material cost percentage (5% or higher, default 100%)
- **Quality Distribution Calculator**: Test different skill configurations and success rates
- **Interactive Preview**: Real-time display of current skill requirements and success rates
- **Settings Migration**: Automatic upgrade from legacy settings format

### Visual Feedback
- Text motes show improvement results:
  - "Improvement failed! (quality)" when quality doesn't improve
  - "Improved to [quality]!" when successful
- Progress bar shows work completion
- Material requirements displayed in item inspection

## Work System

### Work Type
- New work type, `WorkType_Improving`, shown in the Work tab as the column **Improve**. The header is
  `WorkTypeDef.labelShort` capitalised, so name it that way in anything a player reads
- Uses Construction skill
- Separate priority from regular construction
- **Starts switched off in an existing save.** `Pawn_WorkSettings.priorities` is a `DefMap`, and
  `DefMap.ExposeData` pads a work type it has not seen before with `new V()`, which for an int is 0.
  Nothing downstream raises it: `Pawn_WorkSettings.ExposeData` only ever calls `Disable` on load, and
  `EnableAndInitialize`, which would assign priority 3, is reached only from `PawnGenerator` for a
  new pawn, from `ResurrectionUtility`, from `LordToil_Siege`, and from `Pawn.SetFaction` when a pawn
  joins the player faction. None of those fire for a pawn already in the colony. So every existing
  pawn arrives with the work type off.
- For a pawn generated after the mod was added, `EnableAndInitialize` assigns priority 3 only to the
  six work types with the highest average relevant skill, because `LimitInitialActiveWorks` is
  `!pawn.RaceProps.IsMechanoid`. A new colonist weak at Construction can still arrive with Improving
  off.
- `LimitInitialActiveWorks` is false for mechanoids, so a constructoid gestated after the mod was
  added gets the work type at priority 3 with no cap. One that already existed arrives at 0, and
  cannot be raised by the player, which is why the mod raises it itself. See below.

### Colony mechs
- Constructoids can be assigned Improving. `1.6/Patches/MechWorkTypes.xml` appends the work type to
  `Mech_Constructoid`'s `mechEnabledWorkTypes`, which is required because
  `Verse.Pawn.GetDisabledWorkTypes` treats that list as a whitelist for colony mechs and disables
  every work type absent from it.
- A mech is judged on `RaceProperties.mechFixedSkillLevel`, which defaults to 10 and which no shipped
  def overrides, so a constructoid is treated as a Construction 10 worker. Under the Default preset
  that clears the Good requirement of 10 and not the Excellent requirement of 14, so the mod hands it
  buildings marked for any improvement or for a target up to Good. That gates which jobs it is given,
  not what it rolls: the roll is `GenerateQualityCreatedByPawn` at skill 10, exactly as for a
  colonist at that level, so a result above the target is still possible.
- Mechs get no skill bonus to `ConstructionSpeed`. `StatWorker` applies `noSkillFactor`, which
  defaults to 1, so a constructoid works at the base rate rather than at a skill 10 rate.
- **The def patch alone is inert for a mech that predates it**, and this is the part that makes the
  fix actually do something. Removing the work type from the disabled list does not raise the
  priority already stored against the mech. `Pawn_WorkSettings.ExposeData` wrote 0 there on every
  load while the type was still disabled, nothing re-runs `EnableAndInitialize` for a pawn that
  already has settings, and `GetPriority`'s "any non-zero counts as 3" shortcut is gated on
  `RaceProps.Humanlike`, so a mech is held to the stored 0 exactly.
- **The player cannot fix it either.** RimWorld's only per-work-type priority UI is the Work tab, and
  `MainTabWindow_PawnTable.Pawns` is `mapPawns.FreeColonists`, which filters on `RaceProps.Humanlike`
  and never lists a mech. Biotech's Mechs tab offers a work mode and no per-work-type column.
- So `SimpleImproveMapComponent.EnableImprovingForColonyMechs` raises it, on every map load, only for
  colony mechs, only from 0, and only when the work type is not disabled for them. That last guard is
  required: `Pawn_WorkSettings.SetPriority` logs a red error and refuses a non-zero priority on a
  disabled work type. Raising it is not overriding a player's choice, because no UI lets a player
  make that choice for a mech. If RimWorld ever adds one, this becomes wrong and should be revisited.
- Constructoid only, of the seven vanilla mechs that declare `mechEnabledWorkTypes`. It is the one
  that already carries Construction.
- `1.6/Patches/ProjectRimFactoryDrones.xml` does the same for Project RimFactory construction drones,
  using the patch a player wrote and donated. It is unverified against that mod and is a silent no-op
  when the mod is absent.

### Job Flow
1. **Material Hauling**: Pawns gather required materials (if materials are enabled in settings)
2. **Improvement Work**: Pawns work on the item using their construction skill
3. **Quality Roll**: New quality determined based on pawn skill and quality generation system
4. **Result Application**: Quality updated if improved; materials consumed if improvement failed (when materials required)

### Construction Mechanics
- Uses same construction speed stats as building
- Can fail based on construction success chance
- Pawns gain construction experience while improving

## Compatibility

### Harmony Patches
- Two patches: one declares the improvement component on the defs, one handles designation removal
- Neither changes the behaviour of the method it attaches to
- All other functionality uses standard RimWorld systems

### Save File Integrity
- **MapComponent Storage**: Uses RimWorld's native save system for maximum compatibility
- **Component Storage**: Work progress and hauled materials save and load with the building
- **Graceful Degradation**: Save files remain valid if mod is disabled or uninstalled
- **No Save Corruption**: Robust cleanup prevents orphaned data from causing issues
- **Version Tolerance**: Works with saves created across different mod versions

### Mod Support
- Automatically works with any modded items that have quality
- Respects custom material costs
- Compatible with modded inspirations and roles
- Modded buildings are picked up at startup, once every mod's defs have loaded

## Limitations

- Cannot improve items without a blueprint definition
- Cannot improve items already at Legendary quality
- Quality can never decrease (worst case: materials wasted)
- Requires pawns to have manipulation capacity

## Known Issues Resolved

- **Target Quality Persistence**: ✅ **FIXED** - Target quality settings now persist correctly across save/load cycles
  - Previous issue: Target quality would reset to "Any" after loading a save
  - Solution: MapComponent-based persistent storage system ensures settings survive save/load
  - Impact: Players can now set specific quality targets and have them maintained between game sessions