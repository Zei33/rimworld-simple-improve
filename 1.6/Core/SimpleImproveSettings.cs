using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SimpleImprove.Core
{
    /// <summary>
    /// Preset quality standards configurations for SimpleImprove settings.
    /// </summary>
    public enum QualityStandardsPreset
    {
        Apprentice,
        Novice,
        Default,
        Master,
        Artisan,
        Custom
    }

    /// <summary>
    /// Advanced mod settings class for SimpleImprove with improved UI, presets, and validation.
    /// Provides comprehensive configuration for skill requirements, quality standards presets, and quality calculations.
    /// </summary>
    public class SimpleImproveSettings : ModSettings
    {
        #region Core Settings
        
        /// <summary>
        /// Settings version for migration and compatibility handling.
        /// Version 1: Original implementation with trial system
        /// Version 2: New implementation with presets and improved UI
        /// </summary>
        private int settingsVersion = 2;
        
        /// <summary>
        /// Dictionary mapping quality categories to minimum skill requirements.
        /// These values represent the base skill needed to attempt improvements.
        /// </summary>
        private Dictionary<QualityCategory, int> skillRequirements = new Dictionary<QualityCategory, int>
        {
            { QualityCategory.Awful, 0 },
            { QualityCategory.Poor, 0 },
            { QualityCategory.Normal, 4 },
            { QualityCategory.Good, 10 },
            { QualityCategory.Excellent, 14 },
            { QualityCategory.Masterwork, 18 },
            { QualityCategory.Legendary, 20 }
        };

        /// <summary>
        /// Current quality standards preset being used.
        /// </summary>
        private QualityStandardsPreset currentPreset = QualityStandardsPreset.Default;
        
        /// <summary>
        /// Whether improvements should require materials (like construction).
        /// </summary>
        public bool requireMaterials = true;
        
        /// <summary>
        /// Material cost multiplier. <see cref="MaterialCostField"/> owns its range and its default.
        /// </summary>
        private float materialCostMultiplier = MaterialCostField.DefaultMultiplier;
        
        #endregion

        #region UI State
        
        /// <summary>
        /// UI string buffers for the skill requirement input fields.
        /// </summary>
        private Dictionary<QualityCategory, string> skillEntryBuffers = new Dictionary<QualityCategory, string>();
        
        /// <summary>
        /// Buffer for material cost multiplier input (as percentage).
        /// </summary>
        private string materialCostBuffer = MaterialCostField.Format(MaterialCostField.DefaultMultiplier);

        /// <summary>
        /// The IMGUI control name of the material cost field, used to tell whether the player is
        /// typing in it. Vanilla's <c>Widgets.TextFieldNumeric</c> names its controls after their
        /// screen position, which two mods drawing at the same coordinates would share, so this one
        /// carries the mod's own prefix instead.
        /// </summary>
        private const string MaterialCostControlName = "SimpleImprove_MaterialCostField";
        

        
        #endregion

        #region Static Configuration
        
        /// <summary>
        /// The sources of quality tier bonus a pawn can have: inspirations, Ideology roles, and
        /// anything a third party adds.
        /// </summary>
        /// <remarks>
        /// Each entry says whether it is <see cref="PawnQualityBonusKind.Attainable"/> or
        /// <see cref="PawnQualityBonusKind.Carried"/>, because the mod asks two different questions
        /// of them. <see cref="GetSkillRequirement"/> wants what one pawn is getting right now and
        /// reads every entry; <see cref="GetBestCaseSkillRequirement"/> wants what the best pawn in
        /// the colony could get and has to treat the two kinds differently.
        /// </remarks>
        public static List<PawnQualityModifier> PawnQualityModifiers { get; } = new List<PawnQualityModifier>();

        /// <summary>
        /// How many quality tiers inspired creativity is worth, which is vanilla's own number.
        /// </summary>
        /// <remarks>
        /// <c>QualityUtility.GenerateQualityCreatedByPawn</c> clamps its base roll to Masterwork and
        /// then adds two tiers for <c>Inspired_Creativity</c>, so a bonus of some kind is what
        /// reaches Legendary. Not only this one: the Ideology role offset is applied by a second,
        /// separate <c>AddLevels</c> in the same method, and the one offset vanilla ships is +1, so
        /// an uninspired production specialist rolling a Masterwork base gets there too.
        /// </remarks>
        private const int InspiredCreativityBonus = 2;

        /// <summary>
        /// The highest <see cref="QualityCategory"/> index, and so the upper bound on any lookup
        /// into the skill requirement table.
        /// </summary>
        /// <remarks>
        /// Derived from <see cref="GetQualityCategoriesInOrder"/> rather than written out, because
        /// that method is already the one list of qualities the rest of the class agrees on. It used
        /// to be a literal 5, which is Masterwork, so the Legendary row of the table was unreachable
        /// and a Legendary target was silently gated on the Masterwork number.
        /// </remarks>
        private static readonly int HighestQualityIndex = GetQualityCategoriesInOrder().Length - 1;

        /// <summary>
        /// Predefined quality standards preset configurations.
        /// </summary>
        private static readonly Dictionary<QualityStandardsPreset, Dictionary<QualityCategory, int>> PresetConfigurations = new Dictionary<QualityStandardsPreset, Dictionary<QualityCategory, int>>
        {
            {
                QualityStandardsPreset.Apprentice, new Dictionary<QualityCategory, int>
                {
                    { QualityCategory.Awful, 0 },
                    { QualityCategory.Poor, 0 },
                    { QualityCategory.Normal, 1 },
                    { QualityCategory.Good, 3 },
                    { QualityCategory.Excellent, 7 },
                    { QualityCategory.Masterwork, 12 },
                    { QualityCategory.Legendary, 16 }
                }
            },
            {
                QualityStandardsPreset.Novice, new Dictionary<QualityCategory, int>
                {
                    { QualityCategory.Awful, 0 },
                    { QualityCategory.Poor, 0 },
                    { QualityCategory.Normal, 2 },
                    { QualityCategory.Good, 6 },
                    { QualityCategory.Excellent, 10 },
                    { QualityCategory.Masterwork, 15 },
                    { QualityCategory.Legendary, 18 }
                }
            },
            {
                QualityStandardsPreset.Default, new Dictionary<QualityCategory, int>
                {
                    { QualityCategory.Awful, 0 },
                    { QualityCategory.Poor, 0 },
                    { QualityCategory.Normal, 4 },
                    { QualityCategory.Good, 10 },
                    { QualityCategory.Excellent, 14 },
                    { QualityCategory.Masterwork, 18 },
                    { QualityCategory.Legendary, 20 }
                }
            },
            {
                QualityStandardsPreset.Master, new Dictionary<QualityCategory, int>
                {
                    { QualityCategory.Awful, 0 },
                    { QualityCategory.Poor, 1 },
                    { QualityCategory.Normal, 6 },
                    { QualityCategory.Good, 12 },
                    { QualityCategory.Excellent, 16 },
                    { QualityCategory.Masterwork, 19 },
                    { QualityCategory.Legendary, 20 }
                }
            },
            {
                QualityStandardsPreset.Artisan, new Dictionary<QualityCategory, int>
                {
                    { QualityCategory.Awful, 0 },
                    { QualityCategory.Poor, 2 },
                    { QualityCategory.Normal, 8 },
                    { QualityCategory.Good, 14 },
                    { QualityCategory.Excellent, 18 },
                    { QualityCategory.Masterwork, 20 },
                    { QualityCategory.Legendary, 20 }
                }
            }
        };

        /// <summary>
        /// Initializes the pawn quality modifiers that affect improvement outcomes.
        /// Sets up bonuses for inspired creativity and production specialist roles.
        /// </summary>
        /// <param name="ideologyActive">Whether the Ideology DLC is active.</param>
        /// <remarks>
        /// Called from the mod's constructor rather than from a static constructor, and it takes the
        /// DLC flag rather than reading it. A static constructor here reached
        /// <c>ModsConfig.IdeologyActive</c>, which initialises <c>Verse.ModsConfig</c>, which
        /// initialises <c>Verse.UnityData</c>, which makes a native call. That chain made the whole
        /// class impossible to construct outside a running game, so none of the skill arithmetic
        /// below could be tested. Passing the flag in moves the only piece of game state to the one
        /// caller that genuinely has a game.
        ///
        /// Clears first, so calling it more than once cannot double up the modifiers.
        /// </remarks>
        public static void InitializePawnModifiers(bool ideologyActive)
        {
            PawnQualityModifiers.Clear();

            // Inspired Creativity. Attainable, because any colonist can be struck by it, so the
            // best case counts it whether or not anybody has it right now. The worth is stated once
            // and used twice, which is what keeps the best case from carrying its own copy of the
            // number: it used to, in another method, as a bare 2.
            PawnQualityModifiers.Add(PawnQualityModifier.Attainable(
                InspiredCreativityBonus,
                pawn => pawn?.InspirationDef == InspirationDefOf.Inspired_Creativity
                    ? InspiredCreativityBonus
                    : 0));

            // Production Specialist role bonus (Ideology DLC). Carried, because a colony either has
            // somebody in the role or it does not, and the offset is def data, so the only honest
            // way to know what it is worth here is to look at the pawns.
            if (ideologyActive)
            {
                PawnQualityModifiers.Add(PawnQualityModifier.Carried(pawn =>
                {
                    if (pawn?.Ideo != null)
                    {
                        var role = pawn.Ideo.GetRole(pawn);
                        if (role?.def.roleEffects != null)
                        {
                            var productionEffect = role.def.roleEffects
                                .FirstOrDefault(e => e is RoleEffect_ProductionQualityOffset) as RoleEffect_ProductionQualityOffset;
                            if (productionEffect != null)
                                return productionEffect.offset; // Use actual offset value
                        }
                    }
                    return 0;
                }));
            }
        }
        
        #endregion

        #region Public Properties and Methods

        /// <summary>
        /// Gets the current quality standards preset being used.
        /// </summary>
        public QualityStandardsPreset CurrentPreset => currentPreset;
        
        /// <summary>
        /// Gets whether materials are required for improvements.
        /// </summary>
        public bool RequireMaterials => requireMaterials;
        
        /// <summary>
        /// Gets the material cost multiplier.
        /// </summary>
        public float MaterialCostMultiplier => materialCostMultiplier;

        /// <summary>
        /// Gets the minimum skill requirement to improve an item to the specified quality level.
        /// Accounts for pawn-specific modifiers like inspirations and roles.
        /// </summary>
        /// <param name="quality">The target quality level.</param>
        /// <param name="pawn">The pawn performing the improvement (optional). Used to calculate bonuses.</param>
        /// <returns>The minimum Construction skill level required.</returns>
        public int GetSkillRequirement(QualityCategory quality, Pawn pawn = null)
        {
            int bonus = 0;

            if (pawn != null)
            {
                foreach (var modifier in PawnQualityModifiers)
                {
                    bonus += modifier.BonusFor(pawn);
                }
            }

            return RequirementForBonus(quality, bonus);
        }

        /// <summary>
        /// Looks up the skill requirement for a target quality once a bonus has been allowed for.
        /// </summary>
        /// <param name="quality">The target quality level.</param>
        /// <param name="bonus">The quality tiers the pawn gets for free.</param>
        /// <returns>The minimum Construction skill level required.</returns>
        /// <remarks>
        /// Shared by the per-pawn requirement and the colony best case, which used to hold two
        /// copies of this arithmetic and two copies of the clamp. Every <c>QualityCategory</c> has a
        /// row, so the clamp's only job is to absorb a bonus that has taken the index past either
        /// end, and <c>ValidateAndFixLoadedData</c> fills any key a save is missing from the Default
        /// preset, so the lookup cannot throw.
        /// </remarks>
        private int RequirementForBonus(QualityCategory quality, int bonus)
        {
            int index = Mathf.Clamp((int)quality - bonus, 0, HighestQualityIndex);
            return skillRequirements[(QualityCategory)index];
        }
        
        /// <summary>
        /// Gets the lowest skill requirement any pawn in the colony could get away with, allowing
        /// for an inspiration and for the best Ideology production role on the map.
        /// </summary>
        /// <param name="quality">The target quality level.</param>
        /// <param name="map">The map whose colonists to read. Optional.</param>
        /// <returns>The minimum Construction skill level the best case would need.</returns>
        /// <remarks>
        /// <para>
        /// This is the second number in the skill warning, the one after "or". It is a claim about
        /// the player's own colony, so it has to be right: quoting it too high tells them nothing
        /// can reach a target that somebody standing there can reach.
        /// </para>
        /// <para>
        /// Colonists rather than <c>ImproveWorkers.PotentialOnMap</c>, which is deliberate and the
        /// opposite of the choice the two call sites make when they decide whether to warn at all.
        /// They include colony mechs, because a mech can do the work. This does not, because a mech
        /// can hold neither bonus. <c>PawnComponentsUtility</c> creates <c>pawn.ideo</c> only inside
        /// <c>if (pawn.RaceProps.Humanlike)</c>, so <c>Pawn.Ideo</c> is null for a mechanoid and it
        /// can hold no role. And an inspiration cannot start on one:
        /// <c>InspirationWorker.InspirationCanOccur</c> rejects <c>!pawn.IsColonist</c> unless the
        /// def sets <c>allowedOnNonColonists</c>, which <c>Inspired_Creativity</c> does not, and
        /// <c>IsColonist</c> requires <c>RaceProps.Humanlike</c>; a mech also has no mood need, so
        /// <c>InspirationHandler.StartInspirationMTBDays</c> returns -1 and the random path never
        /// fires either. Note that the gate is there and not in the quality roll:
        /// <c>GenerateQualityCreatedByPawn</c> reads <c>InspirationDef</c> with no race test at all,
        /// and its mechanoid ternary picks the skill level and nothing else. Adding mechs here would
        /// change nothing and cost a scan.
        /// </para>
        /// <para>
        /// With no map this answers with the attainable bonuses alone. The version before 1.0.9
        /// invented a role bonus of 1 here when Ideology was active, on the grounds that it was
        /// typical, which was a guess about a colony it had not looked at, and reading
        /// <c>ModsConfig</c> to make it was also what kept this whole method out of the test
        /// harness. Neither call site passes a null map.
        /// </para>
        /// </remarks>
        public int GetBestCaseSkillRequirement(QualityCategory quality, Map map = null)
        {
            return BestCaseRequirementFor(quality, map?.mapPawns?.FreeColonistsSpawned);
        }

        /// <summary>
        /// The best case over a given set of pawns, which is everything
        /// <see cref="GetBestCaseSkillRequirement"/> does once the map has been read.
        /// </summary>
        /// <param name="quality">The target quality level.</param>
        /// <param name="colonists">The pawns to consider, which may be empty or null.</param>
        /// <returns>The minimum Construction skill level the best case would need.</returns>
        internal int BestCaseRequirementFor(QualityCategory quality, IEnumerable<Pawn> colonists)
        {
            return RequirementForBonus(
                quality, QualityBonuses.BestCase(AttainableBonusTotal(), CarriedBonusesOf(colonists)));
        }

        /// <summary>
        /// Adds up the bonuses any pawn could come to have.
        /// </summary>
        /// <returns>The quality tiers the best case may assume for the whole colony.</returns>
        internal static int AttainableBonusTotal()
        {
            int total = 0;

            foreach (var modifier in PawnQualityModifiers)
            {
                if (modifier.Kind == PawnQualityBonusKind.Attainable)
                {
                    total += modifier.Worth;
                }
            }

            return total;
        }

        /// <summary>
        /// What each pawn's carried bonuses add up to, one entry per pawn.
        /// </summary>
        /// <param name="pawns">The pawns to measure, which may be null.</param>
        /// <returns>One total per pawn, in the order given.</returns>
        /// <remarks>
        /// A pawn is asked only about the bonuses they carry, and never about whether they happen to
        /// be inspired. That is the defect this replaces: the old scan tested the pawn's inspiration
        /// state as a proxy for which modifier it was looking at, so an inspired production
        /// specialist had every one of their modifiers skipped and contributed nothing.
        /// </remarks>
        internal static IEnumerable<int> CarriedBonusesOf(IEnumerable<Pawn> pawns)
        {
            if (pawns == null)
            {
                yield break;
            }

            foreach (var pawn in pawns)
            {
                int carried = 0;

                foreach (var modifier in PawnQualityModifiers)
                {
                    if (modifier.Kind == PawnQualityBonusKind.Carried)
                    {
                        carried += modifier.BonusFor(pawn);
                    }
                }

                yield return carried;
            }
        }
        
        /// <summary>
        /// Applies a preset configuration to the current settings.
        /// </summary>
        /// <param name="preset">The preset to apply.</param>
        public void ApplyPreset(QualityStandardsPreset preset)
        {
            if (preset == QualityStandardsPreset.Custom)
            {
                currentPreset = preset;
                return;
            }

            if (PresetConfigurations.TryGetValue(preset, out var presetConfig))
            {
                skillRequirements.Clear();
                foreach (var kvp in presetConfig)
                {
                    skillRequirements[kvp.Key] = kvp.Value;
                }
                currentPreset = preset;
                
                // Update UI buffers
                skillEntryBuffers.Clear();
                foreach (var kvp in skillRequirements)
                {
                    skillEntryBuffers[kvp.Key] = kvp.Value.ToString();
                }
            }
        }
        
        /// <summary>
        /// Validates and clamps skill requirements to reasonable ranges.
        /// </summary>
        private void ValidateSkillRequirements()
        {
            var keys = skillRequirements.Keys.ToList();
            foreach (var quality in keys)
            {
                skillRequirements[quality] = Mathf.Clamp(skillRequirements[quality], 0, 20);
            }
        }
        
        #endregion

        #region UI Helper Methods (for Mod class)

        /// <summary>
        /// Gets a simple preview string for the current preset.
        /// </summary>
        public string GetPresetDisplayString()
        {
            return GetPresetDisplayName(currentPreset);
        }

        /// <summary>
        /// Gets the skill requirement buffer for a specific quality category.
        /// </summary>
        public string GetSkillBuffer(QualityCategory quality)
        {
            if (!skillEntryBuffers.ContainsKey(quality))
            {
                skillEntryBuffers[quality] = skillRequirements.TryGetValue(quality, out int value) ? value.ToString() : "0";
            }
            return skillEntryBuffers[quality];
        }

        /// <summary>
        /// Sets the skill requirement buffer for a specific quality category and updates the actual value.
        /// Automatically switches to Custom preset when manually edited.
        /// </summary>
        public void SetSkillBuffer(QualityCategory quality, string buffer)
        {
            skillEntryBuffers[quality] = buffer;
            
            if (int.TryParse(buffer, out int value))
            {
                value = Mathf.Clamp(value, 0, 20);
                skillRequirements[quality] = value;
                skillEntryBuffers[quality] = value.ToString(); // Update buffer with clamped value
                
                // Switch to custom preset since user manually edited values
                currentPreset = QualityStandardsPreset.Custom;
            }
        }

        /// <summary>
        /// Gets all quality categories in display order.
        /// </summary>
        public static QualityCategory[] GetQualityCategoriesInOrder()
        {
            return new[]
            {
                QualityCategory.Awful,
                QualityCategory.Poor,
                QualityCategory.Normal,
                QualityCategory.Good,
                QualityCategory.Excellent,
                QualityCategory.Masterwork,
                QualityCategory.Legendary
            };
        }

        #endregion

        #region UI Helper Methods
        
        /// <summary>
        /// Gets the display name for a quality standards preset.
        /// </summary>
        private string GetPresetDisplayName(QualityStandardsPreset preset)
        {
            switch (preset)
            {
                case QualityStandardsPreset.Apprentice: return "SimpleImprove_PresetVeryEasy".Translate();
                case QualityStandardsPreset.Novice: return "SimpleImprove_PresetEasy".Translate();
                case QualityStandardsPreset.Default: return "SimpleImprove_PresetNormal".Translate();
                case QualityStandardsPreset.Master: return "SimpleImprove_PresetHard".Translate();
                case QualityStandardsPreset.Artisan: return "SimpleImprove_PresetExpert".Translate();
                case QualityStandardsPreset.Custom: return "SimpleImprove_PresetCustom".Translate();
                default: return preset.ToString();
            }
        }
        
        /// <summary>
        /// Resets all settings to their default values.
        /// </summary>
        public void ResetToDefaults()
        {
            ApplyPreset(QualityStandardsPreset.Default);
            requireMaterials = true;
            materialCostMultiplier = MaterialCostField.DefaultMultiplier;

            // The text buffers are never written out by hand here. This line used to put the
            // multiplier "1.0" into a field that displays percentages, so after a reset the box read
            // 1.0 % while the setting was still 100%, and the next keystroke re-parsed that text as
            // about 1% and quietly dropped the real setting to the minimum. The one control that
            // could recover from the untypable field was what sprang the trap.
            UpdateUIBuffers();
        }

        /// <summary>
        /// Settles the material cost field as though the player had just left it: an out of range
        /// percentage is clamped and the box is rewritten to match the stored setting.
        /// </summary>
        /// <remarks>
        /// Called from the settings window on every frame the field does not have focus, and from
        /// <c>SimpleImproveMod.WriteSettings</c> when the window closes. The second caller is the
        /// one that matters. Unity leaves keyboard focus on a text field when the player clicks the
        /// close button, presses Escape or clicks outside the window, so on every ordinary way out
        /// of the settings the field still holds focus and the first caller has never run. Without
        /// this the last thing typed would be saved as neither the old setting nor the clamped new
        /// one, and its text would outlive the window on a settings object the game keeps for the
        /// rest of the session.
        /// </remarks>
        public void SettleMaterialCostField()
        {
            MaterialCostEdit edit = MaterialCostField.Unfocused(materialCostBuffer, materialCostMultiplier);
            materialCostMultiplier = edit.Multiplier;
            materialCostBuffer = edit.Buffer;
        }

        /// <summary>
        /// Renders the mod settings window content.
        /// Provides a two-column interface with quality inputs on the left and preset buttons on the right.
        /// </summary>
        /// <param name="inRect">The rectangle area available for drawing the settings interface.</param>
        public void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            // Main title
            Text.Font = GameFont.Small;
            listing.Gap(8f);

            // Current preset display
            listing.Label("SimpleImprove_CurrentQualityStandards".Translate(GetPresetDisplayString()));

            // Calculate column dimensions
            float columnWidth = (inRect.width - 20f) / 2f; // 20f gap between columns

            // Coordinates below are relative to inRect, not absolute. Listing.Begin calls
            // Widgets.BeginGroup(inRect), so everything drawn until Listing.End is already offset by
            // inRect's origin. Adding inRect.y applied that offset a second time, and
            // Dialog_ModSettings passes a rect at y = 40, so the two columns were drawn 40px below
            // where the code intended. The magic 80f added to columnsHeight further down is what kept
            // the reset button from landing on top of them, which is why nothing looked wrong.
            //
            // ColumnTopGap is 40f so that the rendered layout is byte-identical to what shipped. The
            // change is that the gap is now a number this mod chose, rather than a coordinate
            // belonging to a vanilla window that is free to move. Rationalising it against the 80f
            // needs somebody to open the settings window and look, so it is on the in-game list.
            const float ColumnTopGap = 40f;
            float currentY = listing.CurHeight + ColumnTopGap;
            const float rowHeight = 24f;
            const float rowGap = 4f;

            // Left column - Quality input fields
            Rect leftColumn = new Rect(0f, currentY, columnWidth, 0f);
            DrawQualityInputs(leftColumn, rowHeight, rowGap);

            // Right column - Preset buttons
            Rect rightColumn = new Rect(columnWidth + 20f, currentY, columnWidth, 0f);
            currentY = DrawPresetButtons(rightColumn, rowHeight, rowGap);
			
			currentY += rowHeight + rowGap + 8f;
			Widgets.CheckboxLabeled(new Rect(columnWidth + 20f, currentY, columnWidth, rowHeight), "SimpleImprove_RequireMaterials".Translate(), ref requireMaterials);

			// Material cost multiplier input (only show if materials are required)
			if (requireMaterials)
			{
				currentY += rowHeight + rowGap;
				
				// Label
				const float labelWidth = 200f;
				const float inputWidth = 80f;
				const float percentWidth = 20f;
				
				Rect labelRect = new Rect(columnWidth + 20f, currentY, labelWidth, rowHeight);
				Widgets.Label(labelRect, "SimpleImprove_MaterialCostMultiplier".Translate() + "SimpleImprove_LabelColon".Translate());
				
				// Input field. Naming the control is what lets the two cases below be told apart:
				// while the player is typing the box keeps exactly what they typed, and it is only
				// normalised once they are somewhere else. Clamping and rewriting on the same keystroke
				// is what made every percentage starting 0 to 4 untypable, the default among them.
				Rect inputRect = new Rect(columnWidth + 20f + labelWidth + 5f, currentY, inputWidth, rowHeight);

				// The field has to notice a click landing elsewhere, because nothing else will. Unity
				// assigns GUIUtility.keyboardControl when a mouse down lands inside a text field and
				// never clears it when one lands outside, GUI.DoControl behind every button and checkbox
				// does not touch it, and Dialog_ModSettings makes no focus call of its own. Without this
				// the only way out of this box is to click one of the skill boxes. Vanilla's own
				// delayed-commit field, Widgets.DelayedTextField, does the same test for the same reason,
				// and reads OriginalEventUtility because an earlier widget may already have consumed the
				// event, which is exactly what a click on a preset button does.
				if (OriginalEventUtility.EventType == EventType.MouseDown
					&& !inputRect.Contains(Event.current.mousePosition)
					&& GUI.GetNameOfFocusedControl() == MaterialCostControlName)
				{
					UI.UnfocusCurrentControl();
				}

				GUI.SetNextControlName(MaterialCostControlName);
				materialCostBuffer = Widgets.TextField(inputRect, materialCostBuffer);
				if (GUI.GetNameOfFocusedControl() == MaterialCostControlName)
				{
					MaterialCostEdit edit = MaterialCostField.Typing(materialCostBuffer, materialCostMultiplier);
					materialCostMultiplier = edit.Multiplier;
					materialCostBuffer = edit.Buffer;
				}
				else
				{
					SettleMaterialCostField();
				}
				
				// Percentage symbol
				Rect percentRect = new Rect(inputRect.xMax + 2f, currentY, percentWidth, rowHeight);
				Widgets.Label(percentRect, "%");
				
				// Add tooltip
				if (Mouse.IsOver(labelRect) || Mouse.IsOver(inputRect))
				{
					TooltipHandler.TipRegion(new Rect(labelRect.x, labelRect.y, inputRect.xMax - labelRect.x, rowHeight), "SimpleImprove_MaterialCostMultiplierTooltip".Translate());
				}
			}

            // Calculate how much vertical space was used by the columns
            var qualities = GetQualityCategoriesInOrder();
            float materialCostHeight = requireMaterials ? (rowHeight + rowGap) : 0f; // Extra space for material cost input
            float columnsHeight = qualities.Length * (rowHeight + rowGap) + 80f + materialCostHeight; // Extra space for headers

            // Continue with remaining settings below the columns
            listing.Gap(columnsHeight);

            listing.Gap(16f);

            // Reset button
            if (listing.ButtonText("SimpleImprove_ResetToDefaults".Translate()))
            {
                ResetToDefaults();
            }

            listing.End();
        }

        /// <summary>
        /// Draws the quality input fields on the left column.
        /// </summary>
        private void DrawQualityInputs(Rect columnRect, float rowHeight, float rowGap)
        {
            float currentY = columnRect.y;

            // Column header
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(columnRect.x, currentY, columnRect.width, rowHeight), "SimpleImprove_SkillRequirementsHeader".Translate());
            currentY += rowHeight + rowGap + 8f;

            var qualities = GetQualityCategoriesInOrder();
            const float labelWidth = 100f;
            const float inputWidth = 60f;

            foreach (var quality in qualities)
            {
                // Quality label
                Rect labelRect = new Rect(columnRect.x, currentY, labelWidth, rowHeight);
                Widgets.Label(labelRect, quality.GetLabel().CapitalizeFirst() + "SimpleImprove_LabelColon".Translate());

                // Input field
                Rect inputRect = new Rect(columnRect.x + labelWidth + 5f, currentY, inputWidth, rowHeight);
                string buffer = GetSkillBuffer(quality);
                string newBuffer = Widgets.TextField(inputRect, buffer);
                
                if (newBuffer != buffer)
                {
                    SetSkillBuffer(quality, newBuffer);
                }

                currentY += rowHeight + rowGap;
            }
        }

        /// <summary>
        /// Draws the preset buttons on the right column.
        /// </summary>
        private float DrawPresetButtons(Rect columnRect, float rowHeight, float rowGap)
        {
            float currentY = columnRect.y;

            // Column header
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(columnRect.x, currentY, columnRect.width, rowHeight), "SimpleImprove_PresetsHeader".Translate());
            currentY += rowHeight + rowGap + 8f;

            // Preset buttons
            if (Widgets.ButtonText(new Rect(columnRect.x, currentY, columnRect.width, rowHeight), "SimpleImprove_PresetVeryEasy".Translate()))
            {
                ApplyPreset(QualityStandardsPreset.Apprentice);
            }
            currentY += rowHeight + rowGap;

            if (Widgets.ButtonText(new Rect(columnRect.x, currentY, columnRect.width, rowHeight), "SimpleImprove_PresetEasy".Translate()))
            {
                ApplyPreset(QualityStandardsPreset.Novice);
            }
            currentY += rowHeight + rowGap;

            if (Widgets.ButtonText(new Rect(columnRect.x, currentY, columnRect.width, rowHeight), "SimpleImprove_PresetNormal".Translate()))
            {
                ApplyPreset(QualityStandardsPreset.Default);
            }
            currentY += rowHeight + rowGap;

            if (Widgets.ButtonText(new Rect(columnRect.x, currentY, columnRect.width, rowHeight), "SimpleImprove_PresetHard".Translate()))
            {
                ApplyPreset(QualityStandardsPreset.Master);
            }
            currentY += rowHeight + rowGap;

            if (Widgets.ButtonText(new Rect(columnRect.x, currentY, columnRect.width, rowHeight), "SimpleImprove_PresetExpert".Translate()))
            {
                ApplyPreset(QualityStandardsPreset.Artisan);
            }

			return currentY;
        }
        
        #endregion

        #region Save/Load and Migration
        
        /// <summary>
        /// Saves and loads mod settings data with version migration support.
        /// Handles backward compatibility and graceful migration from version 1 to version 2.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            
            // Save/load version for migration handling
            Scribe_Values.Look(ref settingsVersion, "settingsVersion", 1); // Default to version 1 for old saves
            
            // Core settings
            Scribe_Collections.Look(ref skillRequirements, "skillRequirements", LookMode.Value, LookMode.Value);
            // Scribed as text on purpose, which is what Scribe_Values.Look writes for an enum
            // anyway, so the file is unchanged in both directions. Reading it as the enum was the
            // defect: a value this build cannot parse leaves the field at default(T), and
            // QualityStandardsPreset's zero member is Apprentice, the LOOSEST preset. Enum.IsDefined
            // in ValidateAndFixLoadedData then returns true for it, because Apprentice is perfectly
            // defined, so the guard there could never fire and a player downgrading from a build with
            // an extra preset silently had their skill requirements relaxed.
            string presetName = currentPreset.ToString();
            Scribe_Values.Look(ref presetName, "currentPreset", QualityStandardsPreset.Default.ToString());

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                currentPreset = Enum.TryParse(presetName, out QualityStandardsPreset parsedPreset)
                                && Enum.IsDefined(typeof(QualityStandardsPreset), parsedPreset)
                    ? parsedPreset
                    : QualityStandardsPreset.Default;
            }
            Scribe_Values.Look(ref requireMaterials, "requireMaterials", true);
            Scribe_Values.Look(ref materialCostMultiplier, "materialCostMultiplier", MaterialCostField.DefaultMultiplier);

            // Handle version migration on loading
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                MigrateFromVersion1();
                ValidateAndFixLoadedData();
                UpdateUIBuffers();
            }
        }
        
        /// <summary>
        /// Migrates settings from version 1 (trial-based) to version 2 (preset-based).
        /// Only performs migration if necessary and preserves user customizations when possible.
        /// </summary>
        private void MigrateFromVersion1()
        {
            if (settingsVersion >= 2)
                return; // No migration needed
            
            Log.Message("[SimpleImprove] Migrating settings from version 1 to version 2...");
            
            // If skill requirements are missing or invalid, initialize with defaults
            if (skillRequirements == null || skillRequirements.Count == 0)
            {
                skillRequirements = new Dictionary<QualityCategory, int>
                {
                    { QualityCategory.Awful, 0 },
                    { QualityCategory.Poor, 0 },
                    { QualityCategory.Normal, 4 },
                    { QualityCategory.Good, 10 },
                    { QualityCategory.Excellent, 14 },
                    { QualityCategory.Masterwork, 18 },
                    { QualityCategory.Legendary, 20 }
                };
                currentPreset = QualityStandardsPreset.Default;
            }
            else
            {
                // Try to match existing skill requirements to a preset
                currentPreset = DetermineClosestPreset(skillRequirements);
                
                if (Prefs.DevMode)
                {
                    Log.Message($"[SimpleImprove] Detected closest preset: {currentPreset}");
                }
            }
            
            // Set new version 2 defaults for new settings
            requireMaterials = true;
            materialCostMultiplier = MaterialCostField.DefaultMultiplier;
            
            // Update version
            settingsVersion = 2;
            
            Log.Message("[SimpleImprove] Settings migration completed successfully.");
        }
        
        /// <summary>
        /// Determines the closest preset match for given skill requirements.
        /// Used during migration to preserve user preferences as much as possible.
        /// </summary>
        private QualityStandardsPreset DetermineClosestPreset(Dictionary<QualityCategory, int> requirements)
        {
            var bestMatch = QualityStandardsPreset.Custom;
            var bestScore = float.MaxValue;
            
            foreach (var preset in PresetConfigurations.Keys)
            {
                var presetConfig = PresetConfigurations[preset];
                var score = 0f;
                
                foreach (var quality in requirements.Keys)
                {
                    if (presetConfig.ContainsKey(quality))
                    {
                        var diff = Math.Abs(requirements[quality] - presetConfig[quality]);
                        score += diff * diff; // Squared difference for better matching
                    }
                }
                
                if (score < bestScore)
                {
                    bestScore = score;
                    bestMatch = preset;
                }
            }
            
            // If the match is very close (total difference <= 2), use the preset
            // Otherwise, mark as custom to preserve user's exact values
            return bestScore <= 4f ? bestMatch : QualityStandardsPreset.Custom;
        }
        
        /// <summary>
        /// Validates and fixes any invalid data that may have been loaded.
        /// Ensures all required fields are properly initialized.
        /// </summary>
        private void ValidateAndFixLoadedData()
        {
            // MigrateFromVersion1 builds this when it runs, but it returns immediately when
            // settingsVersion is already 2, so a config carrying version 2 and no skillRequirements
            // node reaches here with a null and throws on the ContainsKey below. That is a hand-edited
            // or truncated config rather than anything the mod writes, which is why it is low, but the
            // failure is a red error on every settings load rather than a degraded default.
            if (skillRequirements == null)
            {
                skillRequirements = new Dictionary<QualityCategory, int>();
            }

            // Ensure all quality categories are present
            var defaultRequirements = PresetConfigurations[QualityStandardsPreset.Default];
            foreach (var quality in Enum.GetValues(typeof(QualityCategory)).Cast<QualityCategory>())
            {
                if (!skillRequirements.ContainsKey(quality))
                {
                    skillRequirements[quality] = defaultRequirements.TryGetValue(quality, out int defaultValue) ? defaultValue : 0;
                }
            }
            
            // Validate and clamp all values
            ValidateSkillRequirements();
            
            // Bring the material cost multiplier inside the range the tooltip promises. Until
            // version 1.0.9 the bounds here said 5% to 100000% while the tooltip said 10% to 500%,
            // so a save holding a value outside the promised band is brought back into it here.
            materialCostMultiplier = MaterialCostField.Clamp(materialCostMultiplier);
            
            // Ensure preset is valid
            if (!Enum.IsDefined(typeof(QualityStandardsPreset), currentPreset))
            {
                currentPreset = QualityStandardsPreset.Default;
            }
        }
        
        /// <summary>
        /// Updates UI buffers after loading to ensure consistency.
        /// </summary>
        private void UpdateUIBuffers()
        {
            skillEntryBuffers.Clear();
            foreach (var kvp in skillRequirements)
            {
                skillEntryBuffers[kvp.Key] = kvp.Value.ToString();
            }
            
            materialCostBuffer = MaterialCostField.Format(materialCostMultiplier);
        }
        
        #endregion
    }
}