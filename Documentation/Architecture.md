# SimpleImprove Mod Architecture

## Overview

SimpleImprove is a RimWorld mod that allows players to improve the quality of furniture and other constructed items. The mod is designed with clean architecture principles, separating concerns into distinct modules.

## Project Structure

```
1.6/
├── ModEntry.cs              # Main entry point and mod initialization
├── Core/                    # Core functionality
│   ├── SimpleImproveSettings.cs      # Mod settings and configuration
│   ├── SimpleImproveComp.cs         # Component attached to improvable items
│   ├── SimpleImproveMapComponent.cs # Map-level persistent storage for target quality data
│   ├── CompProperties_SimpleImprove.cs # Component properties
│   └── SimpleImproveDefOf.cs        # Def references
├── Designators/            # UI designators for marking items
│   ├── Designator_MarkForImprovement.cs
│   └── Designator_CancelImprovement.cs
├── Jobs/                   # Job system implementation
│   ├── WorkGiver_Improve.cs         # Assigns improvement work
│   ├── JobDriver_HaulToImprove.cs  # Hauls materials to items
│   └── JobDriver_Improve.cs         # Performs improvement work
├── Utils/                  # Utility classes
│   └── MaterialStorage.cs           # Custom material container
├── Patches/                # Harmony patches
│   ├── DesignationCancelPatch.cs    # Handles designation removal
│   ├── DynamicComponentPatch.cs     # Runtime component addition for mod compatibility
│   └── Patches_Improve.xml          # XML patches (deprecated)
└── Defs/                   # XML definitions
    ├── DesignationDefs/
    ├── JobDefs/
    ├── WorkGiverDefs/
    └── WorkTypeDefs/
```

## Key Components

### ModEntry
- Entry point for the mod
- Initializes Harmony patches
- Manages mod settings

### SimpleImproveComp
- ThingComp dynamically attached to all items with quality
- Manages improvement state and work tracking
- Handles material storage via custom MaterialStorage class
- Implements IConstructible interface
- **Intelligent Gizmo Consolidation**: Analyzes current selection to group buildings by improvement state
- **Multi-Building Selection Support**: Provides consolidated UI controls when multiple buildings are selected
- **Context-Aware Quality Options**: Filters available quality targets based on complex selection rules
- **Persistent Target Quality**: Reads target quality settings from SimpleImproveMapComponent for cross-save persistence

### SimpleImproveMapComponent
- **Map-Level Persistent Storage**: Stores target quality data that survives save/load cycles
- **Dictionary-Based Storage**: Uses `Dictionary<int, QualityCategory>` mapping thing IDs to target qualities
- **Automatic Save/Load**: Integrates with RimWorld's native `ExposeData()` system for seamless persistence
- **Memory Management**: Periodic cleanup every 2 hours removes orphaned entries for destroyed items
- **Data Integrity**: Validates and cleans up entries on map finalization and component destruction
- **Performance Optimized**: Efficient O(1) lookups by thing ID with minimal memory overhead
- **Mod Safety**: Graceful degradation if mod is disabled - no save corruption or data loss

### Dynamic Component Addition
- Uses optimized Harmony patch on GetGizmos with intelligent caching
- Each building processed exactly once using thingIDNumber cache
- Multiple early exits minimize overhead for irrelevant objects
- Adds SimpleImproveComp on-demand when first accessing building UI
- Ensures compatibility with mods that add CompQuality after our patches
- Automatically detects and enhances any building with quality
- Performance monitoring in dev mode with periodic statistics
- No per-mod compatibility patches needed
- **Enhanced Restoration**: `RestoreComponentsAfterLoad()` automatically restores target quality settings from MapComponent after save/load
- **Data Validation**: Cross-references designation data with MapComponent storage for consistency
- **Comprehensive Logging**: Debug messages track component restoration and data integrity

### Settings System
- Configurable skill requirements per quality level
- Quality distribution calculator
- Support for pawn modifiers (inspirations, roles)

### Job System
- WorkGiver_Improve: Finds items needing improvement and assigns work
- JobDriver_HaulToImprove: Handles material hauling
- JobDriver_Improve: Performs the actual improvement work

### Material Management
- Custom MaterialStorage class restricts what can be stored
- Only accepts materials needed for improvement
- Automatically drops materials when improvement is cancelled

### Persistent Storage System
- **Target Quality Persistence**: Target quality settings survive save/load cycles without data loss
- **Separation of Concerns**: Volatile improvement state (work progress, materials) stored in dynamic components, persistent data (target quality) stored in MapComponent
- **Automatic Cleanup**: Orphaned entries automatically removed when items are destroyed or maps are unloaded
- **Data Integrity**: Validation ensures consistency between designations and stored target quality data
- **Compatibility**: Works seamlessly with save files created before this system was implemented
- **Performance**: Minimal memory footprint with efficient cleanup cycles
- **Robustness**: Handles edge cases like mid-save thing destruction and map transitions

### Gizmo Consolidation System
- **ImproveGroup Class**: Represents a collection of buildings with similar improvement states
- **Selection Analysis**: `AnalyzeSelection()` method groups buildings by:
  - Improvement marking status (marked vs unmarked)
  - Target quality settings (groups buildings with same target quality)
- **Representative Gizmos**: Only the first component in each group yields a gizmo, preventing duplicates
- **Quality Option Filtering**: `GetAvailableQualityOptions()` implements complex rules:
  - For unmarked buildings in mixed selections: limits options based on highest current quality
  - For marked buildings: shows options above lowest quality in group
  - Cross-group actions apply quality settings to all selected buildings
- **Group Actions**: `ApplyQualityTargetToGroup()` handles both single-group and cross-group operations

## Design Patterns

### Component Pattern
- Uses RimWorld's component system to attach functionality to existing items
- Dynamic components added via Harmony patches for maximum mod compatibility
- Separation between volatile state (ThingComp) and persistent data (MapComponent)

### MapComponent Pattern
- **Persistent Storage**: Uses RimWorld's native MapComponent system for reliable save/load
- **Centralized Data**: Single source of truth for target quality settings per map
- **Automatic Lifecycle**: RimWorld manages creation, saving, loading, and cleanup
- **Performance Optimized**: Dictionary-based storage with O(1) access times
- **Memory Safe**: Automatic cleanup prevents memory leaks from destroyed items

### Job Driver Pattern
- Follows RimWorld's job system architecture
- Separates material hauling from improvement work
- Uses toils for granular control over work steps

### Settings Pattern
- Centralized settings management
- Persistent storage via RimWorld's settings system
- Runtime modifiable without restarts

### Dual-Storage Pattern
- **Dynamic Components**: Handle volatile state (work progress, materials, UI state)
- **MapComponent**: Handles persistent state (target quality settings)
- **Automatic Synchronization**: Components read from MapComponent on-demand
- **Clean Separation**: No coupling between storage layers

## Improvements Over Original

1. **Better Code Organization**: Separated into logical modules instead of one large file
2. **Cleaner API**: More intuitive method names and property access
3. **Enhanced Error Handling**: Better logging and user feedback
4. **Modern C# Features**: Uses properties, LINQ, and pattern matching
5. **Fixed UI Labels**: Quality tiers now match their actual names
6. **Improved Performance**: Caches calculations where possible
7. **Smart Multi-Selection**: Consolidated gizmos prevent UI clutter when selecting multiple buildings
8. **Advanced Quality Targeting**: Complex quality selection rules for mixed building selections
9. **Cross-Group Operations**: Quality settings can be applied to all selected buildings simultaneously
11. **Robust Data Management**: Automatic cleanup and validation prevent data corruption and memory leaks
12. **Enhanced Mod Compatibility**: Dual-storage pattern provides better compatibility with other mods
13. **Save File Integrity**: Clean separation ensures saves remain valid even if mod is disabled