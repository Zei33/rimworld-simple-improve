using RimWorld;

namespace SimpleImprove.Core
{
    /// <summary>
    /// Decides whether an improvement mark still has work ahead of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A mark aims at a quality: the target the player picked, or, for "any improvement", the top of
    /// the scale. The work is outstanding only while the building is below that. Once it is not, no
    /// roll can do what the mark asks. At Legendary <c>CompleteImprovement</c> can only take its
    /// failure branch, because nothing rolls above Legendary; at or above a set target the mod's own
    /// loop would have cleared the mark had it got there itself.
    /// </para>
    /// <para>
    /// Until this existed the work giver and the job driver asked only whether the building was
    /// marked. A building marked for any improvement and then raised to Legendary by something else
    /// was therefore hauled for, worked, rolled and "failed" on every cycle, and with materials
    /// required each failure destroyed the full delivered cost without clearing the mark, so the
    /// colony paid for it indefinitely. A building whose target had been reached or passed by
    /// something else was worked towards a quality it already had, losing the cost on every roll
    /// that did not beat its current quality.
    /// </para>
    /// <para>
    /// It is a function over two values so that every question of this shape is the same question:
    /// the work giver, both job drivers, the marking path and, through <see cref="CanBeOffered"/>,
    /// the gizmo and the selection filter all reach it, and the fixture beside it can run it. Deciding it anywhere else is how the two halves came to
    /// disagree in the first place.
    /// </para>
    /// </remarks>
    public static class ImproveTarget
    {
        /// <summary>
        /// Determines whether a building at one quality still has the improvement a mark asks for
        /// ahead of it.
        /// </summary>
        /// <param name="current">The building's quality now.</param>
        /// <param name="target">
        /// The quality the mark aims at, or <c>null</c> for a mark that accepts any improvement.
        /// </param>
        /// <returns><c>true</c> while the building is strictly below what the mark aims at.</returns>
        /// <remarks>
        /// <para>
        /// Strictly below, not at or below. Reaching the target is the mod's own finishing condition
        /// (<c>ShouldContinueImproving</c> stops there), so a building already at its target has
        /// nothing left to do, and calling it outstanding would send a pawn to roll for a quality
        /// the building already has.
        /// </para>
        /// <para>
        /// A <c>null</c> target aims at Legendary rather than at "the next quality up". That is what
        /// "any improvement" can reach: the mark is cleared after the first roll that beats the
        /// current quality, and until then the only building it cannot help is one with nothing
        /// above it.
        /// </para>
        /// <para>
        /// Whether a particular pawn can roll high enough is a different question and is
        /// deliberately not asked here. This is about the mark, so it has one answer for the whole
        /// colony, which is what lets the work giver and the job driver agree without either of
        /// them knowing who is asking.
        /// </para>
        /// </remarks>
        public static bool IsOutstanding(QualityCategory current, QualityCategory? target)
        {
            return current < (target ?? QualityCategory.Legendary);
        }

        /// <summary>
        /// Determines whether a building at one quality can be offered improvement at all.
        /// </summary>
        /// <param name="current">The building's quality now.</param>
        /// <returns>
        /// <c>true</c> when a mark for any improvement would have work ahead of it, which is every
        /// quality below Legendary.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The gizmo and the selection filter decide whether "Any improvement" is on screen, and
        /// <see cref="IsOutstanding"/> with no target decides whether clicking it marks anything. If
        /// the two drifted, the menu would offer an option that silently does nothing, or hide the
        /// Improve button from a building that could still be improved and show it the stranded
        /// cancel button instead.
        /// </para>
        /// <para>
        /// That is why this takes no target. Both callers used to ask <see cref="IsOutstanding"/>
        /// themselves with a <c>null</c> they each spelled out, and a test reading their compiled
        /// calls could see that the call was there but not what was passed to it, so either could
        /// have asked about Masterwork instead and nothing would have failed. With no argument left
        /// to get wrong at the call site, the one decision lives here, where
        /// <c>ImproveTargetTests</c> holds it equal to <see cref="IsOutstanding"/> for every quality.
        /// </para>
        /// </remarks>
        public static bool CanBeOffered(QualityCategory current)
        {
            return IsOutstanding(current, null);
        }
    }
}
