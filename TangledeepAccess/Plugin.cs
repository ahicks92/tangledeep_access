using System;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using TangledeepAccess.Dev;
using TangledeepAccess.Native;
using TangledeepAccess.Overlays;
using TangledeepAccess.Speech;
using TangledeepAccess.Ui;
using TangledeepAccess.Util;

namespace TangledeepAccess {
    /// <summary>
    /// BepInEx entry point. Awake does non-Unity setup only (logging, native
    /// preload, Prism init, Harmony patching). Update pumps focus announcements
    /// once per frame, so the focused menu element is spoken as focus moves.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public partial class Plugin : BaseUnityPlugin {
        public const string PluginGuid = "io.ahicks.tangledeepaccess";
        public const string PluginName = "Tangledeep Access";

        // PluginVersion is generated from <Version> in Directory.Build.props (see the
        // GeneratePluginVersion target) so the props file is the single source of truth.

        private PrismSpeech _speech;
        private Harmony _harmony;
        private OverlayDispatcher _dispatcher;
        private readonly DevServer _devServer = new DevServer();

        private void Awake() {
            LogBepInExBackend.Install(Logger);
            Log.Info(PluginName + " " + PluginVersion + " loading");

            // Native preload + Prism init are pure native work (no Unity state), safe here.
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (NativeLoader.LoadPrism(pluginDir)) {
                _speech = new PrismSpeech();
                _speech.Initialize();
            }

            // Build the overlay system. Handlers are registered bottom-up: the generic
            // game-focus fallback first (lowest priority), richer menu overlays later.
            _dispatcher = new OverlayDispatcher();
            _dispatcher.Register(new GenericGameFocusOverlay().Handler);
            _dispatcher.Register(new SaveSlotOverlay().Handler); // higher priority than generic
            UiRuntime.Dispatcher = _dispatcher;

            try {
                _harmony = new Harmony(PluginGuid);
                // Patch every annotated type in this assembly (focus chokepoint + in-game input).
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Info("Harmony patches applied");
            } catch (Exception e) {
                Log.Error("Harmony patch failed: " + e);
            }

            // Dev driver (eval + speech tap). No-op unless TANGLEDEEP_DEV=1.
            _devServer.Start();
        }

        private void Update() {
            // Run any queued dev eval jobs on the main thread (no-op when disabled).
            _devServer.Pump();

            // Single per-frame pump. The input hook (TDInputHandler prefix) stashes a nav
            // command; we apply it through the dispatcher, then carry out the game-side
            // effects it asks for. Speaking and game calls stay here, never in a Harmony hook.
            NavCommand? command = UiRuntime.ConsumePendingNav();
            TickResult result = _dispatcher.Tick(command);

            // We moved under our own navigation: follow the game's focus to match and play
            // its move sound (the game didn't move it — we suppressed its input).
            if (result.Moved && result.FocusReference is UIManagerScript.UIObject moveTarget) {
                UIManagerScript.ChangeUIFocusAndAlignCursor(moveTarget);
                UIManagerScript.PlayCursorSound("Move");
            }

            // Player activated a game-backed control: confirm it through the game.
            if (result.Activated) {
                if (result.FocusReference is UIManagerScript.UIObject confirmTarget) {
                    UIManagerScript.ChangeUIFocusAndAlignCursor(confirmTarget);
                }

                UIManagerScript.singletonUIMS?.CursorConfirm();
            }

            if (!string.IsNullOrEmpty(result.Speak)) {
                _speech?.Speak(result.Speak);
            }
        }
    }
}
