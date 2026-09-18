using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RimWorld;
using SimpleImprove.Core;
using Verse;

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
        public void TheConfiguredLegendaryRequirementIsTheOneThatIsRead()
        {
            // The inversion of the test that used to pin defect S-13, filed as simple-improve#14.
            // GetSkillRequirement clamped the quality index with Mathf.Clamp(baseQuality, 0, 5)
            // before indexing the table, and QualityCategory.Legendary is 6, so the Legendary row
            // was unreachable and the Masterwork number was used in its place. The settings window
            // has always drawn an editable Legendary row, so the player could configure a number
            // the mod then ignored.
            //
            // On the Default preset this moves an ordinary pawn's requirement from 18 to 20, which
            // is a live balance change and needs a changelog note. It only ever affected the pawn
            // carrying no bonus: modifiers are subtracted before the clamp, so an inspired pawn or
            // one with a production role was already below 6 and already getting the right number.
            var settings = new SimpleImproveSettings();

            Assert.That(settings.GetSkillRequirement(QualityCategory.Legendary), Is.EqualTo(20));
            Assert.That(
                settings.GetSkillRequirement(QualityCategory.Legendary),
                Is.GreaterThan(settings.GetSkillRequirement(QualityCategory.Masterwork)));
        }

        [Test]
        public void EveryPresetReadsItsOwnLegendaryRow()
        {
            // The fix depends on every preset carrying a Legendary entry, which all five do. A save
            // written before Legendary existed is repaired by ValidateAndFixLoadedData, which fills
            // any missing key from the Default preset, so there is no KeyNotFoundException path.
            var settings = new SimpleImproveSettings();
            var expected = new Dictionary<QualityStandardsPreset, int>
            {
                { QualityStandardsPreset.Apprentice, 16 },
                { QualityStandardsPreset.Novice, 18 },
                { QualityStandardsPreset.Default, 20 },
                { QualityStandardsPreset.Master, 20 },
                { QualityStandardsPreset.Artisan, 20 }
            };

            foreach (var pair in expected)
            {
                settings.ApplyPreset(pair.Key);

                Assert.That(
                    settings.GetSkillRequirement(QualityCategory.Legendary), Is.EqualTo(pair.Value),
                    pair.Key + " is not reading its own Legendary row.");
            }
        }

        [Test]
        public void ResettingToDefaultsLeavesTheMaterialCostFieldReadingOneHundred()
        {
            // The buffer used to be written by hand here as "1.0", the multiplier, into a field that
            // displays percentages. The box then read 1.0 % while the setting was still 100%, and
            // the first keystroke re-parsed that text as about 1% and dropped the real setting to
            // the minimum. Reset is the control that existed to recover from the untypable field,
            // so it was the one that sprang the trap.
            //
            // The field is dirtied first on purpose. A fresh instance already reads "100", so a test
            // that reset one of those would pass whether or not the reset touched the buffer at all,
            // which is a mutation that got past the first version of this.
            var settings = new SimpleImproveSettings();
            SetMaterialCostState(settings, 3.0f, "1");

            settings.ResetToDefaults();

            Assert.That(settings.MaterialCostMultiplier, Is.EqualTo(MaterialCostField.DefaultMultiplier));
            Assert.That(MaterialCostBufferOf(settings), Is.EqualTo("100"));
        }

        [Test]
        public void ResettingToDefaultsRebuildsTheSkillBuffersToo()
        {
            // ApplyPreset already does this, so the assertion is that routing the reset through
            // UpdateUIBuffers did not lose it.
            var settings = new SimpleImproveSettings();
            settings.ApplyPreset(QualityStandardsPreset.Apprentice);

            settings.ResetToDefaults();

            Assert.That(settings.GetSkillBuffer(QualityCategory.Legendary), Is.EqualTo("20"));
        }

        [Test]
        public void ClosingTheSettingsWindowSettlesAnOutOfRangeField()
        {
            // The commit point, and the one that carries the weight. Unity assigns keyboard focus on
            // a mouse down inside a text field and never clears it on one outside, and none of the
            // ways out of the mod settings window moves it: the close button, the close X and the
            // click-outside path all go through GUI.Button, which does not touch keyboardControl,
            // and Escape reaches WindowStack.Notify_PressedCancel, which does not either. So the
            // field is still focused on every ordinary exit, the unfocused branch in the window has
            // never run, and this is what clamps the last thing typed. SimpleImproveMod.WriteSettings
            // calls it from Dialog_ModSettings.PreClose.
            var settings = new SimpleImproveSettings();
            SetMaterialCostState(settings, 1.0f, "1000");

            settings.SettleMaterialCostField();

            Assert.That(settings.MaterialCostMultiplier, Is.EqualTo(MaterialCostField.MaximumMultiplier));
            Assert.That(MaterialCostBufferOf(settings), Is.EqualTo("500"));
        }

        [Test]
        public void ClosingTheSettingsWindowOnAHalfTypedFieldRestoresTheSetting()
        {
            // The other half of the same exit. An abandoned edit must not outlive the window, and
            // the buffer does outlive it: it is an instance field on the ModSettings object, which
            // the game keeps for the rest of the session.
            var settings = new SimpleImproveSettings();
            SetMaterialCostState(settings, 3.0f, "");

            settings.SettleMaterialCostField();

            Assert.That(settings.MaterialCostMultiplier, Is.EqualTo(3.0f));
            Assert.That(MaterialCostBufferOf(settings), Is.EqualTo("300"));
        }

        [Test]
        public void TheModDeclaresWriteSettings()
        {
            // The declaration, because the body cannot be run: SimpleImproveMod.Settings goes
            // through Mod.GetSettings, which reads a file through Scribe. Deleting this override is
            // invisible to every other test here and would quietly remove the only point at which an
            // abandoned material cost edit is settled, so the override itself is what gets asserted.
            // Same reasoning as WorkGiverSurfaceTests, where the absence of a fix is invisible to
            // behaviour and the declaration is all there is to hold.
            var declared = typeof(SimpleImproveMod).GetMethod("WriteSettings", System.Type.EmptyTypes);

            Assert.That(declared, Is.Not.Null);
            Assert.That(declared.DeclaringType, Is.EqualTo(typeof(SimpleImproveMod)),
                "SimpleImproveMod no longer overrides WriteSettings, so nothing settles the "
                + "material cost field when the settings window closes.");
        }

        [Test]
        public void ANegativeQualityModifierCannotPushTheLookupOffTheEndOfTheTable()
        {
            // What the "- 1" in HighestQualityIndex is actually for, and the only test that
            // exercises the clamp with a pawn at all. The two modifiers the mod registers are both
            // positive, but PawnQualityModifiers is a public static list and the Ideology role
            // offset it reads is def data, so a negative one is a modded save away. Without the
            // clamp that is (QualityCategory)7 and a KeyNotFoundException on every job scan.
            SimpleImproveSettings.PawnQualityModifiers.Add(pawn => -1);
            var settings = new SimpleImproveSettings();

            Assert.That(
                () => settings.GetSkillRequirement(QualityCategory.Legendary, new Pawn()),
                Throws.Nothing);
            Assert.That(
                settings.GetSkillRequirement(QualityCategory.Legendary, new Pawn()), Is.EqualTo(20));
        }

        [Test]
        public void APositiveQualityModifierWalksDownTheTable()
        {
            // The direction that does happen. Inspired creativity is worth two quality levels, so an
            // inspired pawn aiming at Legendary is judged against the Excellent row.
            SimpleImproveSettings.PawnQualityModifiers.Add(pawn => 2);
            var settings = new SimpleImproveSettings();

            Assert.That(
                settings.GetSkillRequirement(QualityCategory.Legendary, new Pawn()),
                Is.EqualTo(settings.GetSkillRequirement(QualityCategory.Excellent)));
        }

        [Test]
        public void LoadingBringsAnOutOfRangeMaterialCostBackIntoRange()
        {
            // The half of the range change that touches existing colonies. Version 1.0.8 allowed
            // anything from 5% to 100000% while promising 10% to 500% in nine languages, so a save
            // can hold a percentage the settings window will no longer accept. This is what brings
            // it back, and it runs from ExposeData on LoadingVars.
            var settings = new SimpleImproveSettings();
            SetMaterialCostState(settings, 20.0f, "2000");

            ValidateLoadedData(settings);

            Assert.That(settings.MaterialCostMultiplier, Is.EqualTo(MaterialCostField.MaximumMultiplier));
        }

        [Test]
        public void LoadingBringsAnUnderRangeMaterialCostBackIntoRange()
        {
            var settings = new SimpleImproveSettings();
            SetMaterialCostState(settings, 0.05f, "5");

            ValidateLoadedData(settings);

            Assert.That(settings.MaterialCostMultiplier, Is.EqualTo(MaterialCostField.MinimumMultiplier));
        }

        [Test]
        public void LoadingLeavesAMaterialCostThatIsAlreadyInRangeAlone()
        {
            var settings = new SimpleImproveSettings();
            SetMaterialCostState(settings, 2.5f, "250");

            ValidateLoadedData(settings);

            Assert.That(settings.MaterialCostMultiplier, Is.EqualTo(2.5f));
        }

        [Test]
        public void LoadingFillsAMissingLegendaryRowFromTheDefaultPreset()
        {
            // What stops the #14 fix throwing. Reading the Legendary row means it has to exist, and
            // a save written before Legendary was configurable will not carry one. All five presets
            // ship the key, so the only way to be without it is an old or hand-edited save.
            var settings = new SimpleImproveSettings();
            SkillRequirementsOf(settings).Remove(QualityCategory.Legendary);

            ValidateLoadedData(settings);

            Assert.That(
                () => settings.GetSkillRequirement(QualityCategory.Legendary), Throws.Nothing);
            Assert.That(settings.GetSkillRequirement(QualityCategory.Legendary), Is.EqualTo(20));
        }

        /// <summary>
        /// Runs the private repair pass that <c>ExposeData</c> calls on <c>LoadingVars</c>.
        /// </summary>
        /// <remarks>
        /// Reached by reflection because the public route is through <c>Scribe</c>, which is static
        /// game state and unreachable here. The method itself touches nothing but the settings
        /// object, the preset table and <see cref="MaterialCostField"/>.
        /// </remarks>
        private static void ValidateLoadedData(SimpleImproveSettings settings)
        {
            typeof(SimpleImproveSettings)
                .GetMethod("ValidateAndFixLoadedData", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(settings, null);
        }

        private static Dictionary<QualityCategory, int> SkillRequirementsOf(SimpleImproveSettings settings)
        {
            return (Dictionary<QualityCategory, int>)Field("skillRequirements").GetValue(settings);
        }

        private static string MaterialCostBufferOf(SimpleImproveSettings settings)
        {
            return (string)Field("materialCostBuffer").GetValue(settings);
        }

        /// <summary>
        /// Puts the material cost setting and its text buffer into a state the settings window could
        /// have left them in. Neither field has a setter, and neither should grow one for a test.
        /// </summary>
        private static void SetMaterialCostState(
            SimpleImproveSettings settings, float multiplier, string buffer)
        {
            Field("materialCostMultiplier").SetValue(settings, multiplier);
            Field("materialCostBuffer").SetValue(settings, buffer);
        }

        private static FieldInfo Field(string name)
        {
            return typeof(SimpleImproveSettings)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
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
