# Simple Improve

A RimWorld 1.6 mod that lets colonists raise the quality of buildings that already have a quality
rating, using the same work and the same roll as building one from scratch.

![RimWorld Version](https://img.shields.io/badge/RimWorld-1.6-brightgreen.svg)
![License](https://img.shields.io/badge/License-GPL%20v3-blue.svg)

## Overview

Mark a building for improvement and a colonist will bring materials to it and work on it. When the
work finishes, the game rolls a new quality exactly as it would for a fresh build. A better result
is kept; a worse one is discarded and the building keeps the quality it had.

Only buildings qualify, and only those that carry a quality rating and have a blueprint, because
the blueprint is what the material cost is charged against. Weapons and apparel are out of scope,
and so are quality buildings with no blueprint.

## Features

### Improving

- Mark one building or a whole selection at once.
- Aim for any improvement at all, or for a specific target quality.
- Failures are not destructive. A failed roll costs the work and the materials, not the building.
- Improvement uses the vanilla quality roll, so the odds are the odds you already know.
- Without an inspiration or a production role the roll tops out at Masterwork, the same as
  building fresh. Legendary can be set as a target and is accepted with a warning saying so.

### Skill requirements

The mod decides which colonists may attempt which target quality. Five presets set the whole table
at once, and every threshold is editable by hand:

- **Apprentice** loose thresholds, so most pawns may try
- **Novice** low thresholds
- **Default** balanced
- **Master** high thresholds
- **Artisan** tight thresholds

Custom is not a button. Typing your own number into any of the seven skill boxes switches the
preset to Custom and keeps the rest of your table as it was. Values are clamped to 0 to 20.

Inspired Creativity lowers a pawn's requirement, as does an Ideology role whose effects include a
production quality offset, including a modded one. They stack. Inspirations are matched on vanilla's
Inspired Creativity specifically, which is also the only inspiration vanilla's own quality roll
rewards.

### Interface

- Select several buildings and one Improve button marks them all.
- Buildings already marked for the same target share a button, so a mixed selection shows one
  button per group rather than one per building.
- The inspect pane shows the materials delivered, the work left and the skill the target needs.

There is no Architect menu tab and no designator tool. Marking is done from the building's own
button.

### Work type

The mod adds a work type called Improve, with its own column in the Work tab, separate from
Construction. See "Switching the work type on" below, because it does not start switched on.

## Installation

Subscribe on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3538863870).

To install by hand, clone this repository and copy it into `RimWorld/Mods/`. There are no tagged
GitHub releases.

Requires the [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077) mod. The
copy in this repository is a compile-time reference and is stripped from the built mod folder.

## Usage

### Switching the work type on

**In a colony that existed before you added the mod, Improve starts switched off for every
colonist.** RimWorld gives a newly added work type priority 0 in an existing save, so nothing will
happen until you set a priority yourself.

1. Open the Work tab.
2. Find the column headed **Improve**.
3. Set a priority for the colonists you want doing it.

For a colonist created after the mod was added, RimWorld switches on only the work types they are
best at, so Improve can still be off for one whose Construction is weak.

Colony mechs are switched on for you when a save loads. The Work tab lists colonists only and never
shows a mech, so there is nowhere to set it by hand. A mech is judged at the fixed skill level
mechanoids use for every job, which is 10, so on the Default preset a constructoid is given
buildings marked for any improvement or for a target up to Good, and not ones marked for Excellent
or above. Of the seven vanilla mechs only the constructoid does construction work.

### Marking a building

1. Select one or more improvable buildings.
2. Click **Improve** in the gizmo bar.
3. Choose **Any improvement**, or a target quality.

To unmark, click **Improve** again and choose **Cancel improvement**. The vanilla Architect,
Orders, Cancel tool also clears the mark, and returns any materials already delivered.

### How the work runs

Any colonist with the work type switched on will haul the materials. The skill requirement is
checked when the improvement work itself is handed out, so a pawn below the requirement can stock a
building it cannot then work on.

Materials are staged in the building until the work completes. Cancelling returns them.

## Configuration

**Options, Mod options, Simple Improve.**

- The five preset buttons, and the seven editable skill thresholds.
- **Require materials for improvement**, a checkbox. Off means improvements cost only work.
- **Material cost percentage**, from 10% to 500%, default 100%, shown while materials are required.
- **Reset to Defaults**.

## Compatibility

- Works with any modded building that carries quality and has a blueprint.
- Does not patch the quality roll. It calls vanilla's, so a mod that changes quality generation
  changes improvement the same way.
- Improvement does not run third-party `GenConstruct.CanConstruct` postfixes, because it no longer
  calls that method. See `docs` in the workspace for why. One visible consequence: with Humanoid
  Alien Races installed, a race forbidden to construct something can now improve it.

## Technical notes

Three Harmony patches: two postfixes that declare the improvement component on the relevant defs,
one at def generation and one after every mod's static constructors have run, and one prefix on
designation removal that returns staged materials and cancels running jobs. None of them skips the
method it attaches to or changes what it returns. Two XML PatchOperations add the work type to
colony mechs.

Modded buildings are picked up at startup once every mod's defs have loaded, which is what the
second of the two injection passes is for.

## Building from source

```sh
export FrameworkPathOverride=/opt/homebrew/opt/mono/lib/mono/4.7.2-api
dotnet build rimworld-simple-improve.sln -c Release
```

`./build.sh` builds Release, stages the mod and replaces the copy in the RimWorld install. It
deletes that folder first, so do not run it to check something.

Tests: `dotnet test Tests/SimpleImprove.Tests.csproj`. `Tests/README.md` says what is and is not
reachable outside a running game, which is most of this mod.

## Licence

GPL-3.0. See `LICENSE`.
