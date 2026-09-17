using NUnit.Framework;
using RimWorld;
using SimpleImprove.Core;

namespace SimpleImprove.Tests
{
    /// <summary>
    /// Covers the skill arithmetic in <see cref="SimpleImproveSettings"/>.
    /// </summary>
    /// <remarks>
    /// Reachable only because the class no longer has a static constructor. It used to call
    /// InitializePawnModifiers, which read ModsConfig.IdeologyActive, which initialised
    /// Verse.UnityData and made a native call, so merely naming the type threw out here.
    /// </remarks>
    [TestFixture]
    public class SimpleImproveSettingsTests
    {
        [SetUp]
        public void ClearModifiers()
        {
            // PawnQualityModifiers is static and shared, and the game populates it from the mod's
            // constructor. Start every test from a known empty state rather than from whatever a
            // previous test left, which also keeps these independent of test ordering.
            SimpleImproveSettings.PawnQualityModifiers.Clear();
        }

        [Test]
        public void ConstructingSettingsOutsideTheGameNoLongerThrows()
        {
            Assert.That(new SimpleImproveSettings(), Is.Not.Null);
        }

        [Test]
        public void DefaultPresetIsDefault()
        {
            Assert.That(new SimpleImproveSettings().CurrentPreset, Is.EqualTo(QualityStandardsPreset.Default));
        }

        [Test]
        public void MaterialsAreRequiredByDefault()
        {
            // This is what puts every player on the path of the save/load defect: materials are
            // hauled into the building before any work starts.
            Assert.That(new SimpleImproveSettings().RequireMaterials, Is.True);
        }

        [Test]
        public void SkillRequirementRisesWithQuality()
        {
            var settings = new SimpleImproveSettings();

            var awful = settings.GetSkillRequirement(QualityCategory.Awful);
            var normal = settings.GetSkillRequirement(QualityCategory.Normal);
            var excellent = settings.GetSkillRequirement(QualityCategory.Excellent);
            var masterwork = settings.GetSkillRequirement(QualityCategory.Masterwork);

            Assert.That(awful, Is.LessThanOrEqualTo(normal));
            Assert.That(normal, Is.LessThan(excellent));
            Assert.That(excellent, Is.LessThan(masterwork));
        }

        [Test]
        public void EverySkillRequirementIsAValidConstructionLevel()
        {
            var settings = new SimpleImproveSettings();

            foreach (var quality in SimpleImproveSettings.GetQualityCategoriesInOrder())
            {
                var requirement = settings.GetSkillRequirement(quality);
                Assert.That(requirement, Is.InRange(0, 20), quality + " is outside the skill range.");
            }
        }

        [Test]
        public void LegendaryIsClampedOntoMasterworkAndIsThereforeUnreachable()
        {
            // Pins defect S-13, filed as simple-improve#14. GetSkillRequirement does
            // Mathf.Clamp(baseQuality, 0, 5) before indexing the table, but QualityCategory
            // .Legendary is 6, so the configured Legendary requirement can never be read and the
            // Masterwork row is used instead.
            //
            // This is a balance change to fix, not a quiet bug fix: on the Default preset it moves
            // the requirement from 18 to 20 for every existing colony. When #14 lands this test
            // must fail, which is the point of it.
            var settings = new SimpleImproveSettings();

            Assert.That(
                settings.GetSkillRequirement(QualityCategory.Legendary),
                Is.EqualTo(settings.GetSkillRequirement(QualityCategory.Masterwork)));
        }

        [Test]
        public void ApplyingAPresetChangesTheRequirements()
        {
            var settings = new SimpleImproveSettings();
            settings.ApplyPreset(QualityStandardsPreset.Apprentice);
            var apprentice = settings.GetSkillRequirement(QualityCategory.Masterwork);

            settings.ApplyPreset(QualityStandardsPreset.Artisan);
            var artisan = settings.GetSkillRequirement(QualityCategory.Masterwork);

            Assert.That(apprentice, Is.LessThan(artisan));
            Assert.That(settings.CurrentPreset, Is.EqualTo(QualityStandardsPreset.Artisan));
        }

        [Test]
        public void EveryPresetProducesAMonotonicSkillTable()
        {
            var settings = new SimpleImproveSettings();
            var ordered = SimpleImproveSettings.GetQualityCategoriesInOrder();

            foreach (QualityStandardsPreset preset in System.Enum.GetValues(typeof(QualityStandardsPreset)))
            {
                if (preset == QualityStandardsPreset.Custom)
                {
                    continue;
                }

                settings.ApplyPreset(preset);

                var previous = -1;
                foreach (var quality in ordered)
                {
                    var requirement = settings.GetSkillRequirement(quality);
                    Assert.That(requirement, Is.GreaterThanOrEqualTo(previous),
                        preset + " goes backwards at " + quality);
                    previous = requirement;
                }
            }
        }

        [Test]
        public void GetQualityCategoriesInOrderCoversEveryQualityExactlyOnce()
        {
            var ordered = SimpleImproveSettings.GetQualityCategoriesInOrder();

            Assert.That(ordered.Length, Is.EqualTo(System.Enum.GetValues(typeof(QualityCategory)).Length));
            Assert.That(ordered, Is.Unique);
            Assert.That(ordered[0], Is.EqualTo(QualityCategory.Awful));
        }

        [Test]
        public void InitializePawnModifiersRegistersTheInspirationBonusWithoutIdeology()
        {
            SimpleImproveSettings.InitializePawnModifiers(ideologyActive: false);

            Assert.That(SimpleImproveSettings.PawnQualityModifiers.Count, Is.EqualTo(1));
        }

        [Test]
        public void InitializePawnModifiersAddsTheRoleBonusOnlyWithIdeology()
        {
            SimpleImproveSettings.InitializePawnModifiers(ideologyActive: true);

            Assert.That(SimpleImproveSettings.PawnQualityModifiers.Count, Is.EqualTo(2));
        }

        [Test]
        public void InitializePawnModifiersDoesNotAccumulateAcrossCalls()
        {
            // The mod constructor runs once per process, but nothing guarantees that forever, and a
            // doubled modifier list would quietly halve every skill requirement.
            SimpleImproveSettings.InitializePawnModifiers(ideologyActive: true);
            SimpleImproveSettings.InitializePawnModifiers(ideologyActive: true);

            Assert.That(SimpleImproveSettings.PawnQualityModifiers.Count, Is.EqualTo(2));
        }

        [Test]
        public void SkillRequirementIgnoresModifiersWhenNoPawnIsGiven()
        {
            var settings = new SimpleImproveSettings();
            var without = settings.GetSkillRequirement(QualityCategory.Masterwork);

            SimpleImproveSettings.InitializePawnModifiers(ideologyActive: true);

            Assert.That(settings.GetSkillRequirement(QualityCategory.Masterwork, null), Is.EqualTo(without));
        }
    }
}
