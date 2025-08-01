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
- Manages improvement state and target quality settings
- Handles material storage via custom MaterialStorage class
- Implements IConstructible interface
- **Intelligent Gizmo Consolidation**: Analyzes current selection to group buildings by improvement state
- **Multi-Building Selection Support**: Provides consolidated UI controls when multiple buildings are selected
- **Context-Aware Quality Options**: Filters available quality targets based on complex selection rules

### Dynamic Component Addition
- Uses optimized Harmony patch on GetGizmos with intelligent caching
- Each building processed exactly once using thingIDNumber cache
- Multiple early exits minimize overhead for irrelevant objects
- Adds SimpleImproveComp on-demand when first accessing building UI
- Ensures compatibility with mods that add CompQuality after our patches
- Automatically detects and enhances any building with quality
- Performance monitoring in dev mode with periodic statistics
- No per-mod compatibility patches needed

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
- Components are added via XML patches to all items with quality

### Job Driver Pattern
- Follows RimWorld's job system architecture
- Separates material hauling from improvement work
- Uses toils for granular control over work steps

### Settings Pattern
- Centralized settings management
- Persistent storage via RimWorld's settings system
- Runtime modifiable without restarts

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