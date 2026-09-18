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
        /// Whether to show advanced settings section.
        /// </summary>
        private bool showAdvancedSettings = false;
        
        /// <summary>
        /// Whether to show detailed success rate information.
        /// </summary>
        private bool showSuccessRatePreview = true;
        
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

        #region Legacy Support (for migration from version 1)
        
        /// <summary>
        /// Legacy trial cutoff threshold - kept for migration from version 1.
        /// </summary>
        private float trialCutoff = 0.05f;
        
        #endregion

        #region Static Configuration
        
        /// <summary>
        /// List of functions that calculate quality tier bonuses for pawns.
        /// These modifiers account for inspirations, roles, and other factors that affect quality generation.
        /// </summary>
        public static List<Func<Pawn, int>> PawnQualityModifiers { get; } = new List<Func<Pawn, int>>();

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

            // Inspired Creativity bonus
            PawnQualityModifiers.Add(pawn =>
            {
                if (pawn?.InspirationDef == InspirationDefOf.Inspired_Creativity)
                    return 2; // Boosts quality by 2 tiers
                return 0;
            });

            // Production Specialist role bonus (Ideology DLC)
            if (ideologyActive)
            {
                PawnQualityModifiers.Add(pawn =>
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
                });
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
            int baseQuality = (int)quality;
            
            if (pawn != null)
            {
                foreach (var modifier in PawnQualityModifiers)
                {
                    baseQuality -= modifier(pawn);
                }
            }

            // Every QualityCategory has a row, so the only job of this clamp is to absorb a modifier
            // that has taken the index below Awful. ValidateAndFixLoadedData fills any key a save is
            // missing from the Default preset, so the lookup below cannot throw.
            baseQuality = Mathf.Clamp(baseQuality, 0, HighestQualityIndex);
            return skillRequirements[(QualityCategory)baseQuality];
        }
        
        /// <summary>
        /// Gets the minimum skill requirement considering the best possible bonuses available on the map.
        /// This calculates what skill level would be needed if a pawn had inspiration and the best available role bonus.
        /// </summary>
        /// <param name="quality">The target quality level.</param>
        /// <param name="map">The map to search for pawns with bonuses (optional).</param>
        /// <returns>The minimum Construction skill level required with best available bonuses.</returns>
        public int GetBestCaseSkillRequirement(QualityCategory quality, Map map = null)
        {
            // Calculate the best possible quality bonus available
            int bestTotalBonus = 0;
            
            // Inspiration is always potentially available (+2 quality levels)
            int inspirationBonus = 2;
            
            // Find the best role bonus available on the map
            int bestRoleBonus = 0;
            if (map?.mapPawns?.FreeColonistsSpawned != null)
            {
                foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    // Get role bonuses for this pawn (not inspiration, which we count separately)
                    foreach (var modifier in PawnQualityModifiers)
                    {
                        int modifierValue = modifier(pawn);
                        // Check if this is a role bonus (not inspiration)
                        if (pawn?.InspirationDef != InspirationDefOf.Inspired_Creativity && modifierValue > 0)
                        {
                            bestRoleBonus = Mathf.Max(bestRoleBonus, modifierValue);
                        }
                    }
                }
            }
            else if (ModsConfig.IdeologyActive)
            {
                // If no map provided but Ideology is active, assume typical production role bonus
                bestRoleBonus = 1;
            }
            
            // Best case scenario: inspiration + best available role bonus
            bestTotalBonus = inspirationBonus + bestRoleBonus;
            
            // Calculate what quality level they'd need to achieve before bonuses
            // If target is Excellent (3) and they get +3 bonus, they only need to achieve Awful (0)
            // Shares the bound above for consistency rather than as a fix. The inspiration bonus of
            // 2 is added unconditionally, so this expression never exceeds 4 and the upper bound has
            // never been reached from here.
            int baseQualityNeeded = Mathf.Clamp((int)quality - bestTotalBonus, 0, HighestQualityIndex);
            
            // Return the skill requirement for that base quality
            return skillRequirements[(QualityCategory)baseQualityNeeded];
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
            showAdvancedSettings = false;
            showSuccessRatePreview = true;

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
            float currentY = listing.CurHeight + inRect.y;
            const float rowHeight = 24f;
            const float rowGap = 4f;

            // Left column - Quality input fields
            Rect leftColumn = new Rect(inRect.x, currentY, columnWidth, 0f);
            DrawQualityInputs(leftColumn, rowHeight, rowGap);

            // Right column - Preset buttons
            Rect rightColumn = new Rect(inRect.x + columnWidth + 20f, currentY, columnWidth, 0f);
            currentY = DrawPresetButtons(rightColumn, rowHeight, rowGap);
			
			currentY += rowHeight + rowGap + 8f;
			Widgets.CheckboxLabeled(new Rect(inRect.x + columnWidth + 20f, currentY, columnWidth, rowHeight), "SimpleImprove_RequireMaterials".Translate(), ref requireMaterials);

			// Material cost multiplier input (only show if materials are required)
			if (requireMaterials)
			{
				currentY += rowHeight + rowGap;
				
				// Label
				const float labelWidth = 200f;
				const float inputWidth = 80f;
				const float percentWidth = 20f;
				
				Rect labelRect = new Rect(inRect.x + columnWidth + 20f, currentY, labelWidth, rowHeight);
				Widgets.Label(labelRect, "SimpleImprove_MaterialCostMultiplier".Translate() + ":");
				
				// Input field. Naming the control is what lets the two cases below be told apart:
				// while the player is typing the box keeps exactly what they typed, and it is only
				// normalised once they are somewhere else. Clamping and rewriting on the same keystroke
				// is what made every percentage starting 0 to 4 untypable, the default among them.
				Rect inputRect = new Rect(inRect.x + columnWidth + 20f + labelWidth + 5f, currentY, inputWidth, rowHeight);

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
                Widgets.Label(labelRect, quality.GetLabel().CapitalizeFirst() + ":");

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
            Scribe_Values.Look(ref currentPreset, "currentPreset", QualityStandardsPreset.Default);
            Scribe_Values.Look(ref requireMaterials, "requireMaterials", true);
            Scribe_Values.Look(ref materialCostMultiplier, "materialCostMultiplier", MaterialCostField.DefaultMultiplier);
            Scribe_Values.Look(ref showAdvancedSettings, "showAdvancedSettings", false);
            Scribe_Values.Look(ref showSuccessRatePreview, "showSuccessRatePreview", true);
            
            // Legacy settings (for migration from version 1)
            Scribe_Values.Look(ref trialCutoff, "trialCutoff", 0.05f);
            
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
            showAdvancedSettings = false;
            showSuccessRatePreview = true;
            
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