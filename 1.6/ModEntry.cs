using HarmonyLib;
using RimWorld;
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

            // Registered here rather than from a static constructor on the settings class, which
            // used to reach ModsConfig and made that whole class unconstructible outside the game.
            SimpleImproveSettings.InitializePawnModifiers(ModsConfig.IdeologyActive);
            
            harmony = new Harmony("com.zei33.simpleimprove");
            harmony.PatchAll();

            Log.Message("[SimpleImprove] Loaded version 1.0.8 successfully.");
        }

        /// <summary>
        /// Gets the category name for this mod in the settings menu.
        /// </summary>
        /// <returns>The display name for the mod's settings category.</returns>
        public override string SettingsCategory() => "SimpleImprove_SettingsCategory".Translate();

        /// <summary>
        /// Renders the mod settings window content.
        /// Delegates to the settings class for proper separation of concerns.
        /// </summary>
        /// <param name="inRect">The rectangle area available for drawing the settings interface.</param>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Settings.DoSettingsWindowContents(inRect);
        }

        /// <summary>
        /// Saves the settings when the settings window closes.
        /// </summary>
        /// <remarks>
        /// <c>Dialog_ModSettings.PreClose</c> calls this, and it is the only notice the mod gets
        /// that the window has gone. The material cost field is settled here because closing is the
        /// one exit that always happens, and because none of the ways of closing move keyboard
        /// focus off a text field: Unity assigns <c>GUIUtility.keyboardControl</c> on a mouse down
        /// inside a field and never clears it on one outside, the close button, the close X and the
        /// click-outside path all go through <c>GUI.Button</c>, which does not touch it, and
        /// Escape reaches <c>WindowStack.Notify_PressedCancel</c>, which does not either. So an out
        /// of range percentage typed and then closed on would otherwise be saved as neither the old
        /// setting nor the clamped new one, and its text would outlive the window.
        /// </remarks>
        public override void WriteSettings()
        {
            Settings.SettleMaterialCostField();
            base.WriteSettings();
        }
    }
}