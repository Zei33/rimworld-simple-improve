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
  - Normal: 3
  - Good: 8
  - Excellent: 18
  - Masterwork: 21
- Legendary quality cannot be achieved through normal improvement

### Pawn Modifiers
- **Inspired Creativity**: Boosts quality roll by 2 tiers
- **Production Specialist Role** (Ideology DLC): Boosts quality roll by 1 tier
- These modifiers stack and can enable reaching Legendary quality

## User Interface

### Designators
- **Mark for Improvement**: Select items to queue for quality improvement
- **Cancel Improvement**: Remove items from the improvement queue
- Both support drag selection for multiple items

### Item Gizmos
- Toggle button on each improvable item for quick marking/unmarking
- Shows current improvement status

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
- Only patches designation removal notifications
- All other functionality uses standard RimWorld systems

### Mod Support
- Automatically works with any modded items that have quality
- Respects custom material costs
- Compatible with modded inspirations and roles

## Limitations

- Cannot improve items without a blueprint definition
- Cannot improve items already at Legendary quality
- Quality can never decrease (worst case: materials wasted)
- Requires pawns to have manipulation capacity