# SimpleImprove Features

## Core Functionality

### Quality Improvement System
- Mark any furniture or constructed item with quality for improvement
- Pawns with sufficient construction skill will gather materials and attempt to improve quality
- Quality is re-rolled based on pawn's skill level
- If the new quality is not better, materials are consumed but quality remains unchanged
- If improvement succeeds, the item gains the new quality level

### Material Requirements
- Improvement requires the same materials as initial construction
- Materials are adjusted based on the item's `resourcesFractionWhenDeconstructed` value
- Materials are stored in the item temporarily during the improvement process
- If improvement is cancelled, materials are dropped nearby

### Skill Requirements
- Configurable minimum skill levels for each quality tier
- Default values based on 5% success chance:
  - Awful: 0
  - Poor: 0  
  - Normal: 4
  - Good: 10
  - Excellent: 14
  - Masterwork: 18
- Legendary quality cannot be achieved through normal improvement

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

### Settings Menu
- Adjust minimum skill requirements for each quality level
- Quality distribution calculator to help determine optimal skill requirements
- Test different success chance thresholds

### Visual Feedback
- Text motes show improvement results:
  - "Improvement failed! (quality)" when quality doesn't improve
  - "Improved to [quality]!" when successful
- Progress bar shows work completion
- Material requirements displayed in item inspection

## Work System

### Work Type
- New "Improving" work type
- Uses Construction skill
- Separate priority from regular construction

### Job Flow
1. **Material Hauling**: Pawns gather required materials
2. **Improvement Work**: Pawns work on the item
3. **Quality Roll**: New quality determined based on skill
4. **Result Application**: Quality updated if improved

### Construction Mechanics
- Uses same construction speed stats as building
- Can fail based on construction success chance
- Pawns gain construction experience while improving

## Compatibility

### Harmony Patches
- Minimal patches for maximum compatibility
- Only patches designation removal notifications and component restoration
- All other functionality uses standard RimWorld systems
- Enhanced component restoration ensures data consistency after save/load

### Save File Integrity
- **MapComponent Storage**: Uses RimWorld's native save system for maximum compatibility
- **Clean Separation**: Persistent data stored separately from dynamic components
- **Graceful Degradation**: Save files remain valid if mod is disabled or uninstalled
- **No Save Corruption**: Robust cleanup prevents orphaned data from causing issues
- **Version Tolerance**: Works with saves created across different mod versions

### Mod Support
- Automatically works with any modded items that have quality
- Respects custom material costs
- Compatible with modded inspirations and roles
- Enhanced dynamic component system works with mods that add quality to items post-load

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