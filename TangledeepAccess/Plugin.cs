using System;
using System.Collections.Generic;
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

        // One-time keymap fix: strip the game's Ctrl binding off "Cycle Hotbars" once Rewired is
        // ready (Ctrl is the screen reader's stop-speech key). Stops polling once applied; the
        // SwitchControlMode postfix re-asserts it if the game ever rebuilds the keyboard map.
        private bool _keymapApplied;

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
            _dispatcher.Register(new InventoryOverlay().Handler);       // I tab, consumables
            _dispatcher.Register(new SkillSheetOverlay().Handler);      // J tab, skills (learn mode)
            _dispatcher.Register(new EquipmentOverlay().Handler);       // E tab, gear
            _dispatcher.Register(new ShopOverlay().Handler);            // merchant / banker shop
            _dispatcher.Register(new CharacterSheetOverlay().Handler); // C tab, character stats
            _dispatcher.Register(new OptionsOverlay().Handler);        // options menu (Esc)
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

            // Force the Default layout and evacuate mod-claimed keys once Rewired's maps load
            // (poll until ready). See KeymapPatch.
            if (!_keymapApplied) {
                _keymapApplied = KeymapPatch.TryApplyWhenReady();
            }

            // Speak a one-time "ready" line once the backend has had a few frames to come up,
            // so the player knows the mod is active.
            if (_startupFrames >= 0 && --_startupFrames < 0) {
                _speech?.Speak(new MessageBuilder()
                    .Fragment(PluginName)
                    .Fragment(PluginVersion)
                    .Fragment("ready."));
            }

            // A dialog's typewriter is a purely visual reveal: we read the full text immediately, and
            // the game's confirm path otherwise eats the first press just to finish the typing. Force
            // every open dialog's text to completion each frame so our reads and confirms act at once.
            // Outside the overlay system on purpose — Build is re-entrant, so a side effect there
            // would fire many times per tick.
            if (UIManagerScript.dialogBoxOpen) {
                UIManagerScript.FinishTypewriterTextImmediately();
            }

            // Poll the non-keyboard input sources before draining, so any event they emit this
            // frame is realized below in the same pump. The focus watcher edge-detects the game's
            // validated UI focus (stale focus included) and enqueues a FocusChanged event.
            FocusWatcher.Poll();

            // Single per-frame pump. Each producer enqueued this frame's events tagged with itself
            // (a key drainer claiming in the game's input hook, or a source like the focus watcher
            // polled just above); we drain and hand each straight back to its producer to realize.
            // Speaking and game calls happen in the realizers (the Unity thread), never in a hook.
            IReadOnlyList<PendingInput> input = InputQueue.Drain();
            bool menuRealized = false;
            foreach (PendingInput ev in input) {
                ev.Source.Realize(ev.Action, _speech);
                if (ev.Source == MenuInputDrainer.Instance) {
                    menuRealized = true;
                }
            }

            // The overlay dispatcher must tick every frame even with no input — that is how it
            // follows the game's own menu focus — so tick it idle on any frame the menu drainer
            // did not already drive.
            if (!menuRealized) {
                MenuInputDrainer.Instance.IdleTick(_speech);
            }

            // Ranged-targeting cursor (aiming a ranged weapon / point ability), captured from the
            // targeting hook. Interrupts, since it tracks active cursor movement. Speak no-ops on a
            // null/empty builder, so these pump producers need no guards.
            _speech?.Speak(TargetingReader.Consume());

            // Spontaneous game-log events (combat, status, NPC barks) the Harmony hook buffered
            // this frame. Spoken without interrupting so a multi-event turn is not chopped, and
            // after any overlay speech above so menu navigation stays responsive.
            _speech?.Speak(GameEventLog.DrainToMessage(), interrupt: false);

            // Active weapon-hotbar slot changed (player cycled/equipped a weapon). Interrupts —
            // it is direct feedback to a deliberate key press and should land immediately.
            _speech?.Speak(WeaponWatcher.Poll());

            // Notable contents of a tile the hero just stepped onto (items, hazardous terrain).
            // Queued, not interrupting, so it follows the turn's log lines.
            _speech?.Speak(MovementWatcher.PollOnMove(), interrupt: false);

            // Warn (pure audio) at the start of any turn the hero stands on a telegraphed attack
            // square — the audible substitute for the red danger marker a sighted player would see.
            DangerWatcher.PollTurn();

            // Keep the exploration cursor following the hero (when follow mode is on) and centered
            // after a map change. Silent — cursor reads are on demand.
            ExplorationCursor.SyncFollow();

            // Navigation aids. The continuous entity scanner (F2) advances on its own clock via Tick;
            // the F-key Shift/Ctrl toggles/triggers are realized through the input queue. Pure audio
            // through their own AudioSources, independent of speech.
            NavAids.PollOnMove();
            NavAids.Tick(UnityEngine.Time.deltaTime);

            // Combat radar: one shared buffer per turn carrying the wall-echo tones (when auto wall echo
            // is on and the hero moved) plus a ping for each visible monster that moved this turn.
            CombatRadar.PollTurn();

            // Low/critical health warning. Interrupts — survival in a permadeath game trumps
            // whatever else is queued.
            _speech?.Speak(HealthWatcher.Poll());
        }
    }
}
