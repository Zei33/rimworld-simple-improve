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
│   ├── ImprovableDefs.cs            # Decides which defs carry the component, and declares it
│   ├── SimpleImproveMapComponent.cs # Colony mech work priorities, and the pre-1.0.9 target store
│   ├── WorkerSkill.cs               # A worker's Construction level, and the skill gate over it
│   ├── ImproveWorkers.cs            # Which pawns can be given improvement work
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
│   ├── CompInjectionPatch.cs        # Declares the component on every play-data load
│   └── DesignationCancelPatch.cs    # Handles designation removal
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
- ThingComp declared on every improvable building def, so the game's own save system round-trips it
- Manages improvement state and work tracking
- Handles material storage via custom MaterialStorage class
- Implements IConstructible interface
- **Intelligent Gizmo Consolidation**: Analyzes current selection to group buildings by improvement state
- **Multi-Building Selection Support**: Provides consolidated UI controls when multiple buildings are selected
- **Context-Aware Quality Options**: Filters available quality targets based on complex selection rules
- **Target Quality**: Held as a field on the component and saved with the building, so it survives a building that is not on a map

### SimpleImproveMapComponent
- **Colony Mech Work Priorities**: On every map load, switches improvement work on for colony mechs
  that still have it stored at priority zero. There is no UI that lets a player do this for a mech
- **Legacy Target Quality Store**: A `Dictionary<int, QualityCategory>` keyed on thing ID, kept only
  so that saves written before version 1.0.9 do not lose the targets the player set. Nothing writes
  to it; each building takes its own entry as it spawns, and the entry is removed when taken
- **No Orphan Sweep**: The sweep this class used to run every two game days deleted any entry it
  could not match to a spawned thing on that map, which silently discarded the target of every
  marked building that happened to be minified, in a caravan or in any other container at the time.
  Leaving the unclaimed entries costs an int and a byte each
- **Performance Optimized**: Efficient O(1) lookups by thing ID with minimal memory overhead
- **Mod Safety**: Graceful degradation if mod is disabled - no save corruption or data loss

### Component Declaration

`ImprovableDefs` decides which defs carry the improvement component: category `Building`, an implied
blueprint, and a `CompQuality`. In the shipped game that is 35 defs, out of 43 that carry quality at
all. Because the component ends up in `def.comps`, `ThingWithComps` builds it like any other
component and `PostExposeData` round-trips through the save file.

This replaced a Harmony prefix on `ThingWithComps.GetGizmos` that attached the component at runtime.
A component attached that way does not survive a save. `ThingWithComps.ExposeData` calls
`InitializeComps` on load, which rebuilds the component list from `def.comps` alone, so the hauled
materials and the accumulated work were written to the file and never read back.

`CompInjectionPatch` applies it, as a postfix on `DefGenerator.GenerateImpliedDefs_PostResolve`. That
point is after blueprint generation and after reference resolution, so every def is final, and it
runs once per play-data load rather than once per process. The difference matters: changing language
rebuilds every def from XML, and a static constructor would not run again, so the mod would quietly
stop working until the game was restarted.

The declaration is made in C# rather than as an XML `PatchOperation` because patches are applied
before def inheritance is resolved. Of the 43 quality buildings, 41 inherit the quality component
from one of five abstract parents and only `Sarcophagus` and `GibbetCage` declare it themselves, so
an XPath over `comps` would match seven nodes and nothing that inherits from them. It would also
reach the eight quality buildings that have no blueprint and cannot be improved, it could not filter
on category because that is declared further up the chain again, and it would not see a def that
another mod gives quality to.

### Settings System
- **Quality Standards Presets**: Pre-configured skill requirement levels (Apprentice, Novice, Default, Master, Artisan, Custom)
- **Settings Migration**: Automatic upgrade from Version 1 to Version 2 settings format
- **Material Requirements Toggle**: Optional material costs for improvements
- **Material Cost Percentage**: Adjustable improvement costs from 10% to 500% of the original build cost
- **Quality Distribution Calculator**: Testing tool for different skill configurations
- **Support for Pawn Modifiers**: Inspirations and ideological roles

### Job System
- WorkGiver_Improve: Finds items needing improvement and assigns work
- JobDriver_HaulToImprove: Handles material hauling
- JobDriver_Improve: Performs the actual improvement work

### Material Management
- **Flexible Material Requirements**: Configurable system allows disabling material costs entirely
- **Adjustable Material Costs**: Material cost percentage setting scales costs between 10% and 500% of the original build cost
- **Custom MaterialStorage Class**: Restricts what can be stored when materials are required
- **Smart Material Handling**: Only accepts materials needed for improvement when enabled
- **Cost Calculation**: Material costs are calculated as a percentage of the full original build cost (e.g. 85 wood at 50% becomes 43 wood, rounded up)
- **Returning Materials**: Staged materials are dropped when the improvement is cancelled, and
  when the building leaves the map for any reason: deconstructed, uninstalled, minified, burnt down
  or replaced. The one exception is a gravship jump, where they travel with the building
- **Settings Integration**: Material requirement checks throughout the system respect user preferences

### Persistent Storage System
- **One Owner**: The marked flag, work progress, hauled materials and target quality are all fields
  on the component, written flat onto the building's own save node by `PostExposeData`
- **Off-Map Buildings**: The state travels with the building rather than with the map, so a
  minified or caravanned building keeps its work progress and its hauled materials. It does not keep
  its mark or its target: putting a building in a container makes vanilla remove its designations,
  which clears both. That is unchanged behaviour, and it is why a reinstalled building comes back
  unmarked
- **Designation Repair**: The designation and the marked flag have to agree, because the work giver
  is designation-driven. `PostSpawnSetup` restores a designation that a gravship jump left behind
- **Older Saves**: A save written before version 1.0.9 has its target qualities migrated out of the
  map component as each building spawns
- **Robustness**: Handles edge cases like mid-save thing destruction and map transitions

### Quality Standards Preset System
- **QualityStandardsPreset Enum**: Defines preset difficulty levels (Apprentice through Artisan)
- **Preset Configuration**: Each preset defines different minimum skill requirements for quality improvements:
  - **Apprentice**: Very low thresholds - allows low-skill pawns to attempt improvements with high failure rates
  - **Novice**: Low thresholds - moderate skill requirements with reasonable failure rates
  - **Default**: Balanced thresholds - ensures reasonable success chances for skilled pawns
  - **Master**: High thresholds - requires skilled pawns, minimizes material waste
  - **Artisan**: Very high thresholds - only master craftsmen can attempt, maximizes success rates
  - **Custom**: User-defined skill requirements for each quality tier
- **Settings Migration**: Automatic upgrade from legacy settings to preset system
- **UI Integration**: Enhanced settings interface with preset selection and tooltips
- **Validation System**: Ensures skill requirements remain within valid ranges

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
- The component is declared on the defs at startup, so it saves and loads with the building
- All of the per-building state, including target quality, is scribed by the component

### MapComponent Pattern
- **Per-Map Work**: RimWorld constructs the component for every map and calls `FinalizeInit` after
  the map loads, which is where the colony mech priorities are corrected
- **Migration Only**: The target quality dictionary it still carries is read on load and never
  written, and can be deleted once saves predating version 1.0.9 stop mattering

### Job Driver Pattern
- Follows RimWorld's job system architecture
- Separates material hauling from improvement work
- Uses toils for granular control over work steps

### Settings Pattern
- Centralized settings management
- Persistent storage via RimWorld's settings system
- Runtime modifiable without restarts

### Single-Storage Pattern
- **Component**: Holds the marked flag, work progress, hauled materials and target quality, all
  saved with the building

There used to be a split, with target quality in the map component, because the component was
attached at runtime by a Harmony patch and could not persist anything of its own. Declaring it on
the defs removed the reason for the split, and version 1.0.9 removed the split.

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
10. **Quality Standards Presets**: Pre-configured skill requirement levels for different playstyles and strategies
11. **Flexible Material Requirements**: Optional material costs - improvements can require only time/labor if preferred
12. **Enhanced Settings UI**: Improved interface with tooltips, presets, and better organization
13. **Settings Migration System**: Automatic upgrade from legacy settings format to new preset system
14. **Robust Data Management**: Automatic cleanup and validation prevent data corruption and memory leaks
15. **Enhanced Mod Compatibility**: Dual-storage pattern provides better compatibility with other mods
16. **Save File Integrity**: Clean separation ensures saves remain valid even if mod is disabled
17. **Adjustable Material Costs**: Material cost percentage setting (10% to 500%) allows fine-tuning improvement expenses for different playstyles