using NUnit.Framework;
using SimpleImprove.Core;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers the arithmetic behind the best case quoted in the skill warning.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The readings this decides over are unreachable here and that is why it exists as a separate
    /// function. A bare <c>Pawn</c> throws on <c>InspirationDef</c>, because <c>mindState</c> is
    /// null, and naming <c>InspirationDefOf.Inspired_Creativity</c> throws too, because a
    /// <c>DefOf</c> is populated only by a running game. Neither of the two modifiers the mod ships
    /// can be evaluated in this project at all. What can be is everything downstream of them.
    /// </para>
    /// <para>
    /// The distinction being tested is the fix. A bonus any pawn could come to have is counted once
    /// for the colony; a bonus only some pawns carry is looked for among them. The code this
    /// replaces had no way to tell them apart and guessed from the pawn's inspiration state.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class QualityBonusesTests
    {
        [Test]
        public void AColonyWithNobodyInItStillGetsTheAttainableBonuses()
        {
            // Inspiration needs no pawn to already have it, which is the whole meaning of
            // attainable: the warning says "or 14 with inspiration", not "or 14, if Dave stays
            // inspired".
            Assert.That(QualityBonuses.BestCase(2, new int[0]), Is.EqualTo(2));
        }

        [Test]
        public void NoPawnListAtAllIsTheSameAsAnEmptyOne()
        {
            // GetBestCaseSkillRequirement passes map?.mapPawns?.FreeColonistsSpawned straight
            // through, so a null map arrives here as a null sequence.
            Assert.That(QualityBonuses.BestCase(2, null), Is.EqualTo(2));
        }

        [Test]
        public void TheAttainableAndCarriedBonusesAddUp()
        {
            // The correction, at its smallest. One pawn can be both inspired and a production
            // specialist, and that pawn is the best case. The old code treated the two as
            // alternatives and took neither.
            Assert.That(QualityBonuses.BestCase(2, new[] { 1 }), Is.EqualTo(3));
        }

        [Test]
        public void TheBestCarriedPawnWins()
        {
            Assert.That(QualityBonuses.BestCase(2, new[] { 0, 1, 3, 2 }), Is.EqualTo(5));
        }

        [Test]
        public void TwoPawnsCarryingOneBonusEachAreNotOnePawnCarryingTwo()
        {
            // Why the totals come in per pawn rather than as one flat list of bonuses. The best case
            // is the best single pawn, because one pawn does the work.
            Assert.That(QualityBonuses.BestCase(0, new[] { 1, 1 }), Is.EqualTo(1));
        }

        [Test]
        public void ANegativeCarriedBonusIsNeverTheBestCase()
        {
            // RoleEffect_ProductionQualityOffset.offset is def data, so a modded ideoligion can ship
            // a negative one. Having that role is not better than having no role.
            Assert.That(QualityBonuses.BestCase(2, new[] { -3 }), Is.EqualTo(2));
        }

        [Test]
        public void EveryPawnCarryingNothingIsTheSameAsNoPawns()
        {
            Assert.That(
                QualityBonuses.BestCase(2, new[] { 0, 0, 0 }),
                Is.EqualTo(QualityBonuses.BestCase(2, new int[0])));
        }

        [TestCase(0, new int[0], 0)]
        [TestCase(0, new[] { 2 }, 2)]
        [TestCase(2, new int[0], 2)]
        [TestCase(2, new[] { 0 }, 2)]
        [TestCase(2, new[] { 1 }, 3)]
        [TestCase(2, new[] { 1, 0, 2 }, 4)]
        [TestCase(2, new[] { -1 }, 2)]
        [TestCase(2, new[] { -1, 1 }, 3)]
        public void TheBestCaseInFull(int attainableTotal, int[] carriedPerPawn, int expected)
        {
            Assert.That(QualityBonuses.BestCase(attainableTotal, carriedPerPawn), Is.EqualTo(expected));
        }

        [Test]
        public void AnAttainableModifierReportsWhatItIsWorthWithoutAPawn()
        {
            // The property that lets the best case count inspiration when nobody is inspired. It
            // used to be a bare 2 written out in the best case method, a second copy of a number
            // that lives in the modifier.
            var modifier = PawnQualityModifier.Attainable(2, pawn => 0);

            Assert.That(modifier.Kind, Is.EqualTo(PawnQualityBonusKind.Attainable));
            Assert.That(modifier.Worth, Is.EqualTo(2));
        }

        [Test]
        public void ACarriedModifierHasToBeMeasuredOnAPawn()
        {
            // No worth, on purpose: what a carried bonus is worth is a fact about the colony, not
            // about the modifier.
            var modifier = PawnQualityModifier.Carried(pawn => 3);

            Assert.That(modifier.Kind, Is.EqualTo(PawnQualityBonusKind.Carried));
            Assert.That(modifier.Worth, Is.EqualTo(0));
            Assert.That(modifier.BonusFor(null), Is.EqualTo(3));
        }

        [Test]
        public void AModifierWithoutAFunctionIsRejectedWhereItIsRegistered()
        {
            // Rather than at the point of use, which is a scan running once per marked building per
            // warning and which would blame the wrong code.
            Assert.That(() => PawnQualityModifier.Carried(null), Throws.ArgumentNullException);
            Assert.That(() => PawnQualityModifier.Attainable(2, null), Throws.ArgumentNullException);
        }
    }
}
