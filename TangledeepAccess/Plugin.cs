using System;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using TangledeepAccess.Dev;
using TangledeepAccess.Gameplay;
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

            // The speech wrapper always exists so the dev /speech tap fires; Prism (and the
            // NVDA controller it statically links) is loaded only when speech is enabled.
            // Disable it for headless/overnight dev runs with no screen reader, where a flaky
            // or absent NVDA must not hang or corrupt anything: TANGLEDEEP_NO_SPEECH=1.
            _speech = new PrismSpeech();
            if (Environment.GetEnvironmentVariable("TANGLEDEEP_NO_SPEECH") == "1") {
                Log.Info("Speech disabled (TANGLEDEEP_NO_SPEECH=1): Prism not loaded; spoken text still captured for /speech");
            } else {
                // Native preload + Prism init are pure native work (no Unity state), safe here.
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (NativeLoader.LoadPrism(pluginDir)) {
                    _speech.Initialize();
                }
            }

            // Build the overlay system. Handlers are registered bottom-up: the generic
            // game-focus fallback first (lowest priority), richer menu overlays later.
            _dispatcher = new OverlayDispatcher();
            // Priority is reverse registration order (last registered wins). Low to high:
            // generic fallback < modal dialog < character creation < save-slot. Dialog is modal
            // so it sits above the generic mirror; CharCreation sits above Dialog because two of
            // its screens (the job grid lives outside dialogs, but feat select is a dialog box)
            // need bespoke reading, yet it claims only its own stages (job grid / feat select /
            // name entry), ceding the narrative-intro dialogs to Dialog; SaveSlot sits highest
            // because the save-slot screen is itself a dialog box wanting the bespoke slot
            // reader, and likewise only claims its exact stage.
            _dispatcher.Register(new GenericGameFocusOverlay().Handler);
            _dispatcher.Register(new DialogOverlay().Handler);
            _dispatcher.Register(new CharCreationOverlay().Handler);
            _dispatcher.Register(new SaveSlotOverlay().Handler);
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

            // On-demand spatial queries (read-here / scan) the gameplay hotkey hook requested.
            // Explicit player queries interrupt, so the answer is immediate.
            GameplayCommand? gameplay = UiRuntime.ConsumePendingGameplay();
            if (gameplay == GameplayCommand.RepeatLast) {
                // Re-speak the last phrase; handled here since the pump owns the speech instance.
                _speech?.Speak(_speech.LastSpoken);
            } else if (gameplay.HasValue) {
                string spoken = GameplayReader.Execute(gameplay.Value);
                if (!string.IsNullOrEmpty(spoken)) {
                    _speech?.Speak(spoken);
                }
            }

            // Ranged-targeting cursor (aiming a ranged weapon / point ability), captured from the
            // targeting hook. Interrupts, since it tracks active cursor movement.
            string aim = TargetingReader.Consume();
            if (!string.IsNullOrEmpty(aim)) {
                _speech?.Speak(aim);
            }

            // Selection in a full-screen panel (inventory/equipment/skills/char sheet), captured
            // from the ImpactUI column hook. Menu navigation, so it interrupts for responsiveness.
            string panel = PanelReader.Consume();
            if (!string.IsNullOrEmpty(panel)) {
                _speech?.Speak(panel);
            }

            // Spontaneous game-log events (combat, status, NPC barks) the Harmony hook buffered
            // this frame. Spoken without interrupting so a multi-event turn is not chopped, and
            // after any overlay speech above so menu navigation stays responsive.
            string log = GameEventLog.DrainToMessage();
            if (!string.IsNullOrEmpty(log)) {
                _speech?.Speak(log, interrupt: false);
            }

            // Notable contents of a tile the hero just stepped onto (items, hazardous terrain).
            // Queued, not interrupting, so it follows the turn's log lines.
            string stepped = MovementWatcher.PollOnMove();
            if (!string.IsNullOrEmpty(stepped)) {
                _speech?.Speak(stepped, interrupt: false);
            }

            // Low/critical health warning. Interrupts — survival in a permadeath game trumps
            // whatever else is queued.
            string health = HealthWatcher.Poll();
            if (!string.IsNullOrEmpty(health)) {
                _speech?.Speak(health);
            }
        }
    }
}
