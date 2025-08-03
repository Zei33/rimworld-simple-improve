# Simple Improve

A RimWorld mod that allows players to improve the quality of buildings with construction skill. Mark buildings for improvement and watch skilled pawns enhance their quality using the same resources required for initial construction.

![RimWorld Version](https://img.shields.io/badge/RimWorld-1.6-brightgreen.svg)
![License](https://img.shields.io/badge/License-GPL%20v3-blue.svg)

## Overview

Simple Improve adds a new construction system to RimWorld that lets you upgrade the quality of furniture and other constructed items. Instead of being stuck with normal-quality furniture forever, you can now invest materials and skilled labor to improve them over time.

## Features

### 🔧 Quality Improvement System
- **Mark any furniture or constructed item** with quality for improvement
- **Skill-based outcomes** - Higher construction skill increases success chances
- **Flexible material requirements** - Option to require materials like normal construction, or just time and labor
- **Smart failure handling** - Failed improvements consume materials (if required) but preserve the item

### 🎯 Flexible Skill Requirements
- **Configurable thresholds** for each quality tier
- **Default settings** based on 5% success chance:
  - Normal: Construction 4
  - Good: Construction 10  
  - Excellent: Construction 14
  - Masterwork: Construction 18
- **Pawn modifiers** supported:
  - **Inspired Creativity** (+2 quality tiers)
  - **Production Specialist** (+1 quality tier)
  - Modifiers can stack to enable Legendary quality

### 🎨 Intuitive User Interface
- **Drag-select designators** for marking multiple items at once
- **Smart multi-building selection** with consolidated controls - no more duplicate buttons!
- **Intelligent quality targeting** with context-aware options based on your selection
- **Cross-group operations** - set quality targets for all selected buildings at once
- **Individual item toggles** via building gizmos
- **Visual feedback** with success/failure messages
- **Enhanced settings menu** with preset system, tooltips, and improved organization
- **Quality distribution calculator** for testing different configurations

### ⚙️ Seamless Integration  
- **New "Improving" work type** with separate priority from construction
- **Automatic material hauling** - Pawns gather resources automatically
- **Experience gain** - Construction skill improves while working
- **Universal mod compatibility** - Works with any mod that adds quality to buildings
- **Optimized performance** - Intelligent caching prevents UI slowdowns

## Installation

### Steam Workshop
1. Subscribe to the mod on Steam Workshop
2. Ensure **Harmony** is installed (required dependency)
3. Enable the mod in your mod list
4. Start or reload your save

### Manual Installation
1. Download the latest release from the [releases page]
2. Extract to your RimWorld `Mods` folder
3. Install [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) if not already installed
4. Enable both mods in your mod list

## Usage

### Marking Items for Improvement

1. **Using Designators**:
   - Open the Architect menu → Improve tab
   - Select "Mark for Improvement" 
   - Click on items or drag to select multiple
   - Use "Cancel Improvement" to remove designations

2. **Using Item Buttons**:
   - Select any improvable item (or multiple items)
   - Click the improvement toggle button in the item's gizmo bar
   - When multiple buildings are selected, the mod shows consolidated buttons grouped by improvement state
   - Choose quality targets from the dropdown menu - options adapt based on your selection

### How Improvement Works

1. **Designation**: Mark items for improvement using the designator or item button
2. **Material Hauling**: Pawns with "Improving" work enabled will gather required materials
3. **Construction Work**: Pawns perform improvement work based on their construction skill
4. **Quality Roll**: New quality is determined using RimWorld's standard quality system
5. **Result**: If quality improves, the new quality is applied; otherwise materials are consumed and the process can retry

### Understanding Success Rates

Quality improvement uses RimWorld's standard construction quality system. Higher construction skill dramatically improves your chances:

| Construction Skill | Good+ Chance | Excellent+ Chance | Masterwork+ Chance |
|-------------------|--------------|-------------------|-------------------|
| 8                 | 40.1%        | 6.6%              | 0.15%             |
| 10                | 56.5%        | 12.7%             | 0.45%             |
| 12                | 74.4%        | 21.8%             | 1.19%             |
| 16                | 90.6%        | 38.2%             | 3.67%             |
| 20                | 97.5%        | 60.1%             | 9.58%             |

Compatible with mods that increase skills above 20.

## Configuration

### Mod Settings

Access the mod settings through:
**Options → Mod Settings → Simple Improve**

#### Quality Standards Presets

Choose from pre-configured pawn skill requirements for different improvement strategies:

- **🌱 Apprentice**: Very low skill requirements - allows any pawn to attempt improvements (high failure rates)
- **📚 Novice**: Low skill requirements - most pawns can attempt improvements with moderate success  
- **⚖️ Default**: Balanced skill requirements - ensures reasonable success chances for most attempts
- **🎯 Master**: High skill requirements - only skilled pawns can attempt improvements (high success rates)
- **🏆 Artisan**: Very high skill requirements - only master craftsmen can attempt improvements (very high success rates)
- **🛠️ Custom**: Set your own minimum skill requirements for each quality tier

#### Advanced Settings

- **Require Materials for Improvement**: Toggle whether improvements need materials like normal construction, or just require work time
- **Skill Requirements**: Adjust minimum construction skill needed for each quality tier (Custom preset only)
- **Quality Calculator**: Test different success rates and skill requirements
- **Success Thresholds**: Set desired success percentages to automatically calculate skill requirements

### Recommended Settings

**For balanced gameplay**: Use the **Default** preset, which provides balanced skill requirements ensuring reasonable success chances for most improvement attempts.

**For fast improvements**: Try **Apprentice** or **Novice** presets to allow lower-skilled pawns to attempt improvements, accepting higher failure rates for faster progression.

**For efficient material usage**: Use **Master** or **Artisan** presets to ensure only highly-skilled pawns attempt improvements, minimizing wasted materials from failed attempts.

**Material-free mode**: Disable "Require Materials for Improvement" in Advanced Settings if you prefer improvements to only cost time and labor, eliminating material waste from failures.

## Technical Details

### Architecture

The mod uses clean, modular architecture with minimal Harmony patches for maximum compatibility:

- **Component System**: Uses RimWorld's ThingComp system for item state management
- **Job System**: Integrates with RimWorld's work system for natural pawn behavior  
- **Material Storage**: Custom storage system restricts hauling to required materials only
- **Settings Framework**: Persistent configuration with runtime updates

### Compatibility

- **Harmony Requirement**: Uses Harmony 2.3.6 for compatibility patches
- **Mod Support**: Automatically works with modded items that have quality
- **Save Compatibility**: Safe to add to existing saves; removes cleanly when disabled

### Performance

- **Efficient Caching**: Caches calculations and lookups where possible
- **Minimal Patches**: Only patches designation removal for clean integration
- **Standard Systems**: Uses RimWorld's built-in systems for work, hauling, and quality

## Building from Source

### Prerequisites
- .NET Framework 4.7.2
- RimWorld 1.6 assemblies
- MonoBleedingEdge 6.12.x

### Build Steps

```bash
# Clone the repository
git clone https://github.com/yourusername/rimworld-simple-improve.git
cd rimworld-simple-improve

# Build using the provided script
./build.sh

# Or build manually
dotnet build SimpleImprove.csproj
```

The built mod will be in the `1.6/Assemblies/` directory.

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Development Guidelines

- Follow C# coding conventions
- Add XML documentation for public APIs
- Test with multiple RimWorld scenarios
- Ensure compatibility with popular mods

## Frequently Asked Questions

**Q: Can I improve items to Legendary quality?**
A: Only with pawn modifiers like Inspired Creativity or Production Specialist roles. The base system caps at Masterwork.

**Q: What happens if improvement fails?**
A: Materials are consumed but the item keeps its original quality. If materials remain, the improvement can be retried.

**Q: Does this work with modded furniture?**
A: Yes! Any item with a quality stat and proper blueprint definition will work automatically.

**Q: Can I control which pawns can attempt improvements?**
A: Yes! Choose from 5 preset skill requirement levels (Apprentice to Artisan) or use Custom mode. Apprentice allows any pawn to try (with high failure rates), while Artisan requires master-level construction skill (ensuring high success rates).

**Q: Do I always need materials for improvements?**
A: No! In the mod settings under Advanced Settings, you can disable "Require Materials for Improvement" to make improvements only require time and labor instead of resources.

## License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

## Credits

- **Author**: Zei33
- **Original Concept**: Inspired by Improve This by [Hex](https://steamcommunity.com/sharedfiles/filedetails/?id=2785022023)

## Support

- **Issues**: Report bugs via GitHub Issues
- **Discussions**: Join the conversation on Steam Workshop
- **Updates**: Watch this repository for new releases and features

---

*Enhance your colony's infrastructure one improvement at a time! 🔨*