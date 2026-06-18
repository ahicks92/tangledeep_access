using System;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using TangledeepAccess.Controls;
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

        // One-time spoken "ready" line, deferred a few frames so Prism's backend is up before
        // the first call (speaking from Awake can precede backend init). -1 once spoken.
        private int _startupFrames = 30;

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

            // Build the overlay system. Handlers are registered bottom-up: the unsupported-screen
            // fallback first (lowest priority), richer menu overlays later.
            _dispatcher = new OverlayDispatcher();
            // One overlay per screen. Priority is reverse registration order (last wins). The
            // unsupported fallback is the floor; in-game dialogue sits above it. TitleDialogOverlay is
            // the catch-all for title dialogs, so the screen-specific title overlays (main menu,
            // feat select, save slots) are registered ABOVE it and win on their own screens. The
            // creation screens that are not dialogs (job grid, name entry, begin) claim distinct
            // stages and never collide. SaveSlot stays highest (its screen is a dialog box but
            // wants the bespoke slot reader).
            _dispatcher.Register(new UnsupportedOverlay().Handler);
            _dispatcher.Register(new DialogOverlay().Handler);          // in-game NPC dialogue
            _dispatcher.Register(new TitleDialogOverlay().Handler);     // title narrative dialogs (catch-all)
            _dispatcher.Register(new TitleMenuOverlay().Handler);       // TITLESCREEN menu
            _dispatcher.Register(new FeatSelectOverlay().Handler);      // PERKSELECT dialog
            _dispatcher.Register(new JobGridOverlay().Handler);         // JOBSELECT grid
            _dispatcher.Register(new NameEntryOverlay().Handler);       // NAMEINPUT, deciding
            _dispatcher.Register(new BeginScreenOverlay().Handler);     // NAMEINPUT, ready
            _dispatcher.Register(new SaveSlotOverlay().Handler);        // SELECTSLOT
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

            // Speak a one-time "ready" line once the backend has had a few frames to come up,
            // so the player knows the mod is active and how to discover its commands.
            if (_startupFrames >= 0 && --_startupFrames < 0) {
                _speech?.Speak(PluginName + " " + PluginVersion
                    + " ready. Press slash for a list of commands.");
            }

            // Single per-frame pump. The active input handler (in the game's input pump) stashed the
            // recognized input for this frame, tagged with its context; we realize it here. The
            // overlay dispatcher ticks every frame regardless — even with no input — to follow the
            // game's own menu focus, so we feed it only a Menu-context action. Speaking and game
            // calls stay here, never in a Harmony hook.
            PendingInput? pending = UiRuntime.ConsumePendingInput();
            ModInputAction? menuAction = null;
            if (pending.HasValue && pending.Value.Context == InputContext.Menu) {
                menuAction = pending.Value.Action;
            }

            TickResult result = _dispatcher.Tick(menuAction);

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

            // Free-play input the look/gameplay hook recognized: look-cursor steps and on-demand
            // spatial queries (read-here / scan). Explicit player queries interrupt, so the answer
            // is immediate.
            if (pending.HasValue && pending.Value.Context != InputContext.Menu) {
                ModInputAction action = pending.Value.Action;
                if (action.Kind == ModInputKind.RepeatLast) {
                    // Re-speak the last phrase; handled here since the pump owns the speech instance.
                    _speech?.Speak(_speech.LastSpoken);
                } else {
                    string spoken = GameplayReader.Execute(action);
                    if (!string.IsNullOrEmpty(spoken)) {
                        _speech?.Speak(spoken);
                    }
                }
            }

            // Ranged-targeting cursor (aiming a ranged weapon / point ability), captured from the
            // targeting hook. Interrupts, since it tracks active cursor movement.
            string aim = TargetingReader.Consume();
            if (!string.IsNullOrEmpty(aim)) {
                _speech?.Speak(aim);
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
