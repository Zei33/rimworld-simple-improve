# SimpleImprove Features

## Core Functionality

### Quality Improvement System
- Mark a building that has a quality rating and a blueprint. The blueprint is what the material
  cost is charged against, so a quality building without one is out of scope, as are weapons and
  apparel
- Any pawn with the work type switched on hauls the materials. The skill requirement is checked
  when the improvement work itself is handed out, so a pawn below the requirement can stock a
  building it cannot then work on
- Quality is re-rolled based on pawn's skill level
- If the new quality is not better, materials are consumed but quality remains unchanged
- If improvement succeeds, the item gains the new quality level

### Material Requirements
- **Configurable Material Costs**: Option to require materials like normal construction, or just time and labor
- **Material Cost Percentage**: Adjustable cost from 10% to 500% of the original build cost (default 100%)
- **Traditional Mode** (materials required):
  - Improvement requires materials equal to a percentage of the item's original build cost (default the same as normal build cost)
  - Material cost can be adjusted anywhere from 10% (very cheap) to 500% (punishing)
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

- **Apprentice**: the loosest thresholds, so nearly any assigned pawn may attempt anything
- **Novice**: low thresholds
- **Default**: balanced thresholds
- **Master**: high thresholds
- **Artisan**: the tightest thresholds

The buttons are plain text; the settings window draws no icons. A preset sets the whole table of
thresholds at once and nothing else. It does not change the odds of a roll succeeding: the roll is
vanilla's and depends on the pawn's actual skill, so a preset decides who is allowed to try rather
than how likely they are to succeed.

**Custom is not a button.** Typing your own number into any of the seven skill boxes switches the
preset to Custom and keeps the rest of your table as it was. The boxes are always editable,
whichever preset is selected; editing one is what puts you into Custom, not the other way round.
Values are clamped to 0 to 20.

#### Default Skill Requirements (Default Preset)
- Awful: 0
- Poor: 0  
- Normal: 4
- Good: 10
- Excellent: 14
- Masterwork: 18
- Legendary: 20

Legendary can be set as a target and the threshold above is enforced, but the vanilla roll clamps
at Masterwork without an inspiration or a production role, so a plain pawn cannot reach it however
skilled. The mod says so when the target is chosen.

### Target Quality Persistence
- **Saved With The Building**: The target is a field on the improvement component, written onto the
  building's own node in the save
- **Survives A Reload**: The target is read back with the building, wherever the building is.
  Before version 1.0.9 it lived in a map component reached through the building's map, so it was
  unreadable and unwritable for anything not standing on one, and a load swept the entry for any
  marked building that happened to be in a container
- **Uninstalling Still Clears It**: Putting a building in a container makes vanilla remove its
  designations, which clears the mark, and the target goes with the mark. A reinstalled building
  comes back unmarked and untargeted, as it did before
- **Older Saves**: Targets set before version 1.0.9 are carried over the first time each building
  loads. A building that is inside something at that moment gets its target back when it is next
  installed
- **Disabling The Mod**: The keys are written flat onto the building and RimWorld ignores nodes it
  has no field for, so a save stays loadable if the mod is removed

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

### Marking
Marking is done from the building's own gizmo. Select one or more improvable buildings and use the
Improve button; the menu it opens also carries Cancel improvement for anything already marked.

**There is no Architect menu tab and no designator tool.** Two designator classes exist in the
source and nothing registers either of them, so neither has ever been reachable. Marking is
therefore per selection rather than by dragging a tool over cells, which in practice means
selecting the buildings the normal way and pressing one button.

The vanilla Architect, Orders, Cancel tool also clears a mark, and returns any staged materials.

### Item Gizmos
- **Smart Consolidated Buttons**: When multiple buildings are selected, the mod intelligently groups them and shows consolidated improvement buttons instead of duplicates
- **Quality Target Selection**: the menu offers "Any improvement" plus every quality above the building's current one, up to and including Legendary, which is accepted with a warning that it needs an inspiration or a production role
- **Context-Aware Options**: Available quality options adapt based on selection:
  - **All Unmarked**: Shows all quality options above each building's current quality
  - **Mixed Selection**: the unmarked buildings are offered only qualities above the highest-quality building among the unmarked ones, not across the whole selection
  - **Different Targets**: Separate buttons for each target quality group, with cross-group quality setting affecting all selected buildings
- Shows current improvement status and target quality

### Settings Menu
Reached through Options, Mod options, Simple Improve. It draws a label naming the current preset,
the seven editable skill boxes, five preset buttons, a materials checkbox, a material cost box and
a reset button. Nothing else.

- **Quality Standards Presets**: five buttons that rewrite the whole threshold table
- **Custom Configuration**: the seven skill boxes are always editable, and typing in one switches
  the preset to Custom while keeping the rest of your table
- **Materials**: a checkbox that turns material requirements on and off, and, while they are on, a
  material cost percentage box accepting 10% to 500%, default 100%

The preset buttons carry no tooltip. The only tooltip in the window is on the material cost field.
There is no quality distribution calculator, no success threshold control, no live success-rate
preview and no Advanced Settings section; earlier versions of this document described all five and
none has ever existed.

- **Settings Migration**: a config written by the mod's version 1 format is upgraded on load, and
  a skill table that no longer matches any preset is matched to the closest one

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
- Three Harmony patches: two postfixes that declare the improvement component on the defs, one at
  def generation and one after every mod's static constructors have run, and one prefix on
  designation removal that returns staged materials and cancels running jobs
- None of the three skips the method it attaches to or changes what it returns
- Two XML PatchOperations add the work type to colony mechs
- Neither changes the behaviour of the method it attaches to
- All other functionality uses standard RimWorld systems

### Save File Integrity
- **Component Storage**: The mark, work progress, hauled materials and target quality all save and
  load with the building, written onto its own node
- **Graceful Degradation**: Save files remain valid if the mod is disabled or uninstalled, because
  RimWorld ignores nodes it has no field for
- **Version Tolerance**: A save written before version 1.0.9 has its target qualities carried over
  the first time each building loads

### Mod Support
- Works with any modded building that carries quality and has a blueprint. Apparel, weapons and
  other quality items are out of scope, as are quality buildings with no blueprint
- Respects custom material costs
- Any Ideology role whose effects include a production quality offset is read, including a modded
  one, and its own offset value is used. Inspirations are matched on vanilla's Inspired Creativity
  specifically, which is also the only inspiration vanilla's own quality roll rewards
- Modded buildings are picked up at startup, once every mod's defs have loaded

## Limitations

- Cannot improve items without a blueprint definition
- Cannot improve items already at Legendary quality
- Quality can never decrease (worst case: materials wasted)
- Requires pawns to have manipulation capacity

## Known Issues Resolved

- **Target Quality Persistence**: Fixed. Target quality settings persist across save and load
  - Previous issue: target quality reset to "any improvement" after loading a save
  - Solution: the target is a field on the improvement component, saved onto the building's own node
  - Impact: a quality target set on a building is still there in the next session