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
        /// Material cost multiplier (0.1 to 2.0, default 1.0).
        /// </summary>
        private float materialCostMultiplier = 1.0f;
        
        #endregion

        #region UI State
        
        /// <summary>
        /// UI string buffers for the skill requirement input fields.
        /// </summary>
        private Dictionary<QualityCategory, string> skillEntryBuffers = new Dictionary<QualityCategory, string>();
        
        /// <summary>
        /// Buffer for material cost multiplier input.
        /// </summary>
        private string materialCostBuffer = "1.0";
        

        
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
        /// Static constructor that initializes pawn quality modifiers.
        /// </summary>
        static SimpleImproveSettings()
        {
            InitializePawnModifiers();
        }

        /// <summary>
        /// Initializes the pawn quality modifiers that affect improvement outcomes.
        /// Sets up bonuses for inspired creativity and production specialist roles.
        /// </summary>
        private static void InitializePawnModifiers()
        {
            // Inspired Creativity bonus
            PawnQualityModifiers.Add(pawn =>
            {
                if (pawn?.InspirationDef == InspirationDefOf.Inspired_Creativity)
                    return 2; // Boosts quality by 2 tiers
                return 0;
            });

            // Production Specialist role bonus (Ideology DLC)
            if (ModsConfig.IdeologyActive)
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

            baseQuality = Mathf.Clamp(baseQuality, 0, 5);
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
            int baseQualityNeeded = Mathf.Clamp((int)quality - bestTotalBonus, 0, 5);
            
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
            materialCostMultiplier = 1.0f;
            materialCostBuffer = "1.0";
            showAdvancedSettings = false;
            showSuccessRatePreview = true;
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

            // Calculate how much vertical space was used by the columns
            var qualities = GetQualityCategoriesInOrder();
            float columnsHeight = qualities.Length * (rowHeight + rowGap) + 80f; // Extra space for headers

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
            Scribe_Values.Look(ref materialCostMultiplier, "materialCostMultiplier", 1.0f);
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
            materialCostMultiplier = 1.0f;
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
            
            // Clamp material cost multiplier
            materialCostMultiplier = Mathf.Clamp(materialCostMultiplier, 0.1f, 2.0f);
            
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
            
            materialCostBuffer = materialCostMultiplier.ToString("F1");
        }
        
        #endregion
    }
}