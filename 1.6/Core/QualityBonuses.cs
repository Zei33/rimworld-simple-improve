using System;
using System.Collections.Generic;
using Verse;

namespace SimpleImprove.Core
{
    /// <summary>
    /// Whether the best case may assume a quality bonus for the whole colony, or has to go and find
    /// the pawn who has it.
    /// </summary>
    /// <remarks>
    /// This distinction is the fix for the defect. The warning that quotes a best case used to tell
    /// the two apart by asking whether the pawn in front of it was inspired, which is a question
    /// about the pawn and not about the modifier, and it got the answer backwards: an inspired
    /// production specialist was dropped from the best case entirely, which is exactly the pawn the
    /// best case is about.
    /// </remarks>
    public enum PawnQualityBonusKind
    {
        /// <summary>
        /// A bonus that arrives over time rather than belonging to a pawn, so the best case counts
        /// it once for the colony. Inspired creativity is the only one this mod registers. It is not
        /// quite open to everybody, since <c>Inspired_Creativity</c> sets <c>minAge</c> 13, requires
        /// one of Art, Smithing, Construction or Tailoring to be enabled, and requires 3 in
        /// Construction, Artistic or Crafting. What matters is that no pawn is permanently excluded
        /// by who they are, so the best case assumes it whether or not anyone has it right now.
        /// </summary>
        Attainable,

        /// <summary>
        /// A bonus only the pawns who already carry it have, so the best case is the best of them.
        /// The Ideology production specialist role is the only one this mod registers: a colony
        /// either has that role filled or it does not, and no amount of waiting changes it.
        /// </summary>
        Carried
    }

    /// <summary>
    /// One source of quality bonus a pawn can have, and what the best case is allowed to assume
    /// about it.
    /// </summary>
    /// <remarks>
    /// The list these live in is public, so a third party can add to it. An addition has to say
    /// which kind it is, and that is deliberate: the two questions the mod asks of a modifier are
    /// "what is this pawn getting right now" and "what could the best pawn in the colony get", and
    /// nothing about a bare <c>Func&lt;Pawn, int&gt;</c> answers the second.
    /// </remarks>
    public sealed class PawnQualityModifier
    {
        private readonly Func<Pawn, int> bonusFor;

        private PawnQualityModifier(PawnQualityBonusKind kind, int worth, Func<Pawn, int> bonusFor)
        {
            Kind = kind;
            Worth = worth;
            this.bonusFor = bonusFor ?? throw new ArgumentNullException(nameof(bonusFor));
        }

        /// <summary>
        /// Whether the best case may assume this bonus for the colony or has to find it on a pawn.
        /// </summary>
        public PawnQualityBonusKind Kind { get; }

        /// <summary>
        /// What this bonus is worth to a pawn who has it. Only meaningful for
        /// <see cref="PawnQualityBonusKind.Attainable"/>, where nobody may have it yet and the best
        /// case still counts it.
        /// </summary>
        public int Worth { get; }

        /// <summary>
        /// A bonus any pawn could come to have, counted by the best case whether or not anyone has
        /// it yet.
        /// </summary>
        /// <param name="worth">What it is worth to a pawn who has it.</param>
        /// <param name="bonusFor">What it is worth to one pawn right now.</param>
        /// <returns>The modifier.</returns>
        public static PawnQualityModifier Attainable(int worth, Func<Pawn, int> bonusFor)
        {
            return new PawnQualityModifier(PawnQualityBonusKind.Attainable, worth, bonusFor);
        }

        /// <summary>
        /// A bonus only the pawns who carry it have, which the best case reads off the colony.
        /// </summary>
        /// <param name="bonusFor">What it is worth to one pawn right now.</param>
        /// <returns>The modifier.</returns>
        /// <remarks>
        /// There is no <c>worth</c> here on purpose. What a carried bonus is worth is a property of
        /// the colony rather than of the modifier: the Ideology production offset is def data and a
        /// colony that has nobody in the role gets nothing from it, so the only honest answer comes
        /// from looking at the pawns.
        /// </remarks>
        public static PawnQualityModifier Carried(Func<Pawn, int> bonusFor)
        {
            return new PawnQualityModifier(PawnQualityBonusKind.Carried, 0, bonusFor);
        }

        /// <summary>
        /// What this bonus is worth to one pawn right now.
        /// </summary>
        /// <param name="pawn">The pawn to measure.</param>
        /// <returns>The number of quality tiers, which is zero when the pawn does not have it.</returns>
        public int BonusFor(Pawn pawn)
        {
            return bonusFor(pawn);
        }
    }

    /// <summary>
    /// Works out the largest total quality bonus any one pawn in a colony could have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the number behind the skill warning, which tells a player that nothing can reach a
    /// target but that Construction <em>n</em> would, with an inspiration or a production role. It
    /// is a claim about the colony, so getting it wrong tells the player something false about their
    /// own pawns rather than merely showing an odd number.
    /// </para>
    /// <para>
    /// The readings it decides over cannot be taken in the test harness, both measured rather than
    /// assumed. A bare <c>Pawn</c> throws a <c>NullReferenceException</c> on <c>InspirationDef</c>,
    /// and the null is <c>health</c> rather than <c>mindState</c>: the property tests <c>Dead</c>
    /// first and <c>Pawn.Dead</c> is <c>health.Dead</c>, so it never reaches the mind state at all.
    /// Naming <c>InspirationDefOf.Inspired_Creativity</c> throws a
    /// <c>TypeInitializationException</c>, because a <c>DefOf</c> class's static constructor cannot
    /// run outside a game. So the mod's own modifiers cannot be evaluated here at all. What can be
    /// is everything after them: a bonus per pawn is an <c>int</c>, and this decides over ints.
    /// </para>
    /// </remarks>
    public static class QualityBonuses
    {
        /// <summary>
        /// The largest total bonus any one pawn could have.
        /// </summary>
        /// <param name="attainableTotal">
        /// The bonuses any pawn could come to have, added up. Counted once for the colony rather
        /// than per pawn, because every pawn could get them.
        /// </param>
        /// <param name="carriedPerPawn">
        /// What each pawn's carried bonuses add up to, one entry per pawn. May be empty or null,
        /// which is a colony nobody is in or a caller with no map to read.
        /// </param>
        /// <returns>The number of quality tiers the best case may assume.</returns>
        /// <remarks>
        /// <para>
        /// The two are added rather than compared, which is the whole correction. One pawn can be
        /// both inspired and a production specialist, and that pawn is the best case. The code this
        /// replaces asked whether the pawn was inspired and, if so, skipped every one of their
        /// modifiers, so the one pawn who had both counted for neither and the colony's best case
        /// was reported as inspiration alone. On the Default preset that is a warning quoting a
        /// number up to six Construction levels above the truth, telling a player nothing can reach
        /// a target that somebody in the room can reach right now.
        /// </para>
        /// <para>
        /// Carried bonuses are summed per pawn and then the best pawn wins, rather than the best
        /// single modifier winning across the colony. Two pawns each carrying one bonus are not one
        /// pawn carrying two.
        /// </para>
        /// <para>
        /// The floor of zero is what stops a negative bonus making the best case worse than having
        /// no role at all. <c>RoleEffect_ProductionQualityOffset.offset</c> is def data, so a modded
        /// ideoligion can ship one.
        /// </para>
        /// </remarks>
        public static int BestCase(int attainableTotal, IEnumerable<int> carriedPerPawn)
        {
            var bestCarried = 0;

            if (carriedPerPawn != null)
            {
                foreach (var carried in carriedPerPawn)
                {
                    if (carried > bestCarried)
                    {
                        bestCarried = carried;
                    }
                }
            }

            return attainableTotal + bestCarried;
        }
    }
}
