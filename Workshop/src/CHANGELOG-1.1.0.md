# Simple Improve 1.1.0

Not a store page. This is the text to post as a Workshop comment when 1.1.0 goes up,
because the in-game uploader hardcodes its own change note and cannot write one.

---

**Simple Improve 1.1.0**

The first update since August 2025. Every open issue is fixed.

Fixed

- Dining chairs, armchairs and couches could not be improved. The mod was enforcing the
  construction skill prerequisite for building one new, which those three declare and stools,
  beds and dressers do not. Reported twice, a year apart.
- Improvement progress and staged materials did not survive a save. Work done on a building
  was lost on every reload. Existing saves recover their progress on the first load after
  this update rather than only stopping the loss from here on.
- Colonists with no skill tracker, and some modded pawn races, could throw an error every
  tick while improving.
- An error with Humanoid Alien Races and Alpha Genes in the mod list, reported in September
  and October 2025, is fixed.
- The material cost percentage could not be typed. Anything starting 0 to 4, including the
  100% default, was rewritten as you typed it.
- The Legendary skill requirement was never read, so the number you set for it did nothing.
- Cancelling an improvement could leave the staged materials stranded, or drop them nowhere.
- A building marked for improvement lost its mark when an Odyssey gravship carried it away.
- The warning about nobody being skilled enough ignored a colonist who was both inspired and
  a production specialist, which is the best case it exists to find.

New

- Colony mechs can do improvement work. Constructoids are switched on automatically when a
  save loads, because RimWorld's Work tab lists colonists only and never shows a mech.
- A cancel button on a building that is marked but can no longer be improved.
- The work type, and eight strings that were English for everyone, are translated in all
  nine languages.

Performance

- With nothing marked for improvement, pawns no longer search the map for it. Previously
  every pawn checked every artificial building on every job search, and having nothing marked
  was the expensive case rather than the cheap one.

Balance, and worth reading before you update

- The Legendary requirement now works, which raises the Default preset's requirement for an
  ordinary colonist from 18 to 20. A colonist with an inspiration or a production role was
  already getting the correct number.
- Colonists will no longer cross a dangerous area to fetch improvement materials. This is a
  tightening. If improvement seems to stop in a colony with a fire or a raid in the way, this
  is why.
- Materials staged for an improvement now count toward colony wealth. They always existed;
  they were not being counted.
- With Humanoid Alien Races installed, a race that is forbidden to build something can now
  improve it. The check that enforced that is gone, along with the error it caused.

The mod no longer ships its source files or two unrelated libraries to subscribers.
