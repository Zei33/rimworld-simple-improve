using HarmonyLib;
using Verse;
using SimpleImprove.Core;
using UnityEngine;

namespace SimpleImprove
{
    /// <summary>
    /// Main mod entry point for the SimpleImprove mod.
    /// Handles mod initialization, settings management, and Harmony patching.
    /// </summary>
    public class SimpleImproveMod : Mod
    {
        /// <summary>
        /// Gets the mod settings instance for SimpleImprove.
        /// Provides access to skill requirements and other configuration options.
        /// </summary>
        public static SimpleImproveSettings Settings { get; private set; }
        
        /// <summary>
        /// The Harmony instance used for applying patches to the base game.
        /// </summary>
        private readonly Harmony harmony;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleImproveMod"/> class.
        /// Sets up mod settings, applies Harmony patches, and logs successful initialization.
        /// </summary>
        /// <param name="pack">The mod content pack containing mod information and assets.</param>
        public SimpleImproveMod(ModContentPack pack) : base(pack)
        {
            Settings = GetSettings<SimpleImproveSettings>();
            
            harmony = new Harmony("com.zei33.simpleimprove");
            harmony.PatchAll();

            Log.Message("[SimpleImprove] Loaded version 1.0 successfully.");
        }

        /// <summary>
        /// Gets the category name for this mod in the settings menu.
        /// </summary>
        /// <returns>The display name for the mod's settings category.</returns>
        public override string SettingsCategory() => "Simple Improve";

        /// <summary>
        /// Renders the mod settings window content.
        /// Delegates to the settings instance to draw the configuration interface.
        /// </summary>
        /// <param name="inRect">The rectangle area available for drawing the settings interface.</param>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.DoWindowContents(inRect);
        }
    }
}