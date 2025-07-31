using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SimpleImprove.Core
{
    /// <summary>
    /// Mod settings class for SimpleImprove that handles skill requirements and quality calculations.
    /// Provides configuration for minimum skill levels needed to improve items to different quality tiers.
    /// </summary>
    public class SimpleImproveSettings : ModSettings
    {
        /// <summary>
        /// Dictionary mapping quality categories to minimum skill requirements.
        /// Default values are based on achieving approximately 5% success chance.
        /// </summary>
        private Dictionary<QualityCategory, int> skillRequirements = new Dictionary<QualityCategory, int>
        {
            { QualityCategory.Awful, 0 },
            { QualityCategory.Poor, 0 },
            { QualityCategory.Normal, 4 },
            { QualityCategory.Good, 10 },
            { QualityCategory.Excellent, 14 },
            { QualityCategory.Masterwork, 18 }
        };

        /// <summary>
        /// UI string buffers for the skill requirement input fields.
        /// </summary>
        private Dictionary<QualityCategory, string> skillEntryBuffers = new Dictionary<QualityCategory, string>();
        
        /// <summary>
        /// The cutoff threshold for calculating skill requirements (success chance percentage).
        /// </summary>
        private float trialCutoff = 0.05f;
        
        /// <summary>
        /// String buffer for the trial cutoff input field.
        /// </summary>
        private string trialCutoffBuffer = "0.05";
        
        /// <summary>
        /// 2D array storing trial results for quality distribution calculations.
        /// First dimension is skill level (0-20), second dimension is quality level (0-6).
        /// </summary>
        private int[,] trialResults = new int[21, 7];

        /// <summary>
        /// List of functions that calculate quality tier bonuses for pawns.
        /// These modifiers account for inspirations, roles, and other factors that affect quality generation.
        /// </summary>
        public static List<Func<Pawn, int>> PawnQualityModifiers { get; } = new List<Func<Pawn, int>>();

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
                                .FirstOrDefault(e => e is RoleEffect_ProductionQualityOffset);
                            if (productionEffect != null)
                                return 1; // Boosts quality by 1 tier
                        }
                    }
                    return 0;
                });
            }
        }

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
        /// Renders the mod settings window contents.
        /// Provides UI for configuring skill requirements and calculating optimal values.
        /// </summary>
        /// <param name="inRect">The rectangle area to draw the settings within.</param>
        public void DoWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard { ColumnWidth = 300f };
            listing.Begin(inRect);

            listing.Label("Minimum skill requirements to improve to each quality level:");
            listing.Gap(12);

            foreach (var quality in Enum.GetValues(typeof(QualityCategory)).Cast<QualityCategory>())
            {
                if (quality == QualityCategory.Legendary) continue; // Can't improve to Legendary normally

                if (!skillEntryBuffers.ContainsKey(quality))
                    skillEntryBuffers[quality] = skillRequirements[quality].ToString();

                var value = skillRequirements[quality];
                var buffer = skillEntryBuffers[quality];
                listing.TextFieldNumericLabeled(
                    $"{quality.GetLabel().CapitalizeFirst()}:",
                    ref value,
                    ref buffer,
                    0,
                    20
                );
                skillRequirements[quality] = value;
                skillEntryBuffers[quality] = buffer;
            }

            listing.Gap(20);
            listing.Label("Quality Distribution Calculator:");
            listing.TextFieldNumericLabeled(
                "Success chance threshold:",
                ref trialCutoff,
                ref trialCutoffBuffer,
                0f,
                1f
            );

            if (listing.ButtonText("Calculate Skill Requirements"))
            {
                CalculateSkillRequirements(trialCutoff);
            }

            listing.End();
        }

        /// <summary>
        /// Calculates optimal skill requirements based on quality distribution trials.
        /// Uses the specified success chance threshold to determine minimum skill levels.
        /// </summary>
        /// <param name="cutoff">The minimum success chance threshold (0.0 to 1.0).</param>
        private void CalculateSkillRequirements(float cutoff)
        {
            // Run trials if not already done
            if (trialResults.Cast<int>().Sum() == 0)
            {
                RunQualityTrials();
            }

            // Calculate skill requirements based on cutoff
            for (int skill = 20; skill >= 0; skill--)
            {
                int totalTrials = 0;
                for (int q = 0; q < 7; q++)
                {
                    totalTrials += trialResults[skill, q];
                }

                double cumulativeProbability = 0;
                for (int q = 6; q >= 0; q--)
                {
                    cumulativeProbability += (double)trialResults[skill, q] / totalTrials;
                    
                    if (q < 6) // Skip Legendary
                    {
                        var quality = (QualityCategory)q;
                        if (cumulativeProbability >= cutoff && skill < 20)
                        {
                            skillRequirements[quality] = Math.Max(skillRequirements[quality], skill + 1);
                        }
                    }
                }
            }

            // Update UI buffers
            foreach (var kvp in skillRequirements)
            {
                skillEntryBuffers[kvp.Key] = kvp.Value.ToString();
            }
        }

        /// <summary>
        /// Runs Monte Carlo trials to determine quality distribution probabilities.
        /// Simulates quality generation for each skill level to build statistical data.
        /// </summary>
        private void RunQualityTrials()
        {
            const int trialsPerSkillLevel = 1000000;
            
            for (int trial = 0; trial < trialsPerSkillLevel; trial++)
            {
                for (int skill = 0; skill <= 20; skill++)
                {
                    var quality = QualityUtility.GenerateQualityCreatedByPawn(skill, false);
                    trialResults[skill, (int)quality]++;
                }
            }
        }

        /// <summary>
        /// Saves and loads mod settings data.
        /// Handles serialization of skill requirements and provides fallback defaults.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            
            Scribe_Collections.Look(ref skillRequirements, "skillRequirements", 
                LookMode.Value, LookMode.Value);
            
            if (Scribe.mode == LoadSaveMode.LoadingVars && skillRequirements == null)
            {
                // Initialize with defaults if loading fails
                skillRequirements = new Dictionary<QualityCategory, int>
                {
                    { QualityCategory.Awful, 0 },
                    { QualityCategory.Poor, 0 },
                    { QualityCategory.Normal, 4 },
                    { QualityCategory.Good, 10 },
                    { QualityCategory.Excellent, 14 },
                    { QualityCategory.Masterwork, 18 }
                };
            }

            // Update UI buffers after loading
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                foreach (var kvp in skillRequirements)
                {
                    skillEntryBuffers[kvp.Key] = kvp.Value.ToString();
                }
            }
        }
    }
}