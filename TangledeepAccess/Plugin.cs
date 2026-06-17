using System;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using TangledeepAccess.Native;
using TangledeepAccess.Overlays;
using TangledeepAccess.Patches;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui;
using TangledeepAccess.Util;

namespace TangledeepAccess
{
    /// <summary>
    /// BepInEx entry point. Awake does non-Unity setup only (logging, native
    /// preload, Prism init, Harmony patching). Update pumps focus announcements
    /// once per frame, so the focused menu element is spoken as focus moves.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "io.ahicks.tangledeepaccess";
        public const string PluginName = "Tangledeep Access";

        // PluginVersion is generated from <Version> in Directory.Build.props (see the
        // GeneratePluginVersion target) so the props file is the single source of truth.

        private PrismSpeech _speech;
        private Harmony _harmony;
        private OverlayDispatcher _dispatcher;

        private void Awake()
        {
            LogBepInExBackend.Install(Logger);
            Log.Info(PluginName + " " + PluginVersion + " loading");

            // Native preload + Prism init are pure native work (no Unity state), safe here.
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (NativeLoader.LoadPrism(pluginDir))
            {
                _speech = new PrismSpeech();
                _speech.Initialize();
            }

            // Build the overlay system. Handlers are registered bottom-up: the generic
            // game-focus fallback first (lowest priority), richer menu overlays later.
            _dispatcher = new OverlayDispatcher();
            _dispatcher.Register(new GenericGameFocusOverlay().Handler);
            UiRuntime.Dispatcher = _dispatcher;

            try
            {
                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(typeof(UIManagerScript_ChangeUIFocus_Patch));
                Log.Info("Harmony patches applied");
            }
            catch (Exception e)
            {
                Log.Error("Harmony patch failed: " + e);
            }
        }

        private void Update()
        {
            // Single per-frame pump: the dispatcher finds the active overlay, reconciles
            // focus (including any game-driven focus change), and returns what to speak.
            // Speaking stays here, never in a Harmony hook.
            string toSpeak = _dispatcher.Tick();
            if (!string.IsNullOrEmpty(toSpeak))
                _speech?.Speak(toSpeak);
        }
    }
}
