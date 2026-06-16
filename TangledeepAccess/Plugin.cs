using System.IO;
using System.Reflection;
using BepInEx;
using TangledeepAccess.Native;
using TangledeepAccess.Speech;
using TangledeepAccess.Util;
using UnityEngine;

namespace TangledeepAccess
{
    /// <summary>
    /// BepInEx entry point. Awake does non-Unity setup only (logging, native
    /// preload, Prism init); the spoken startup line is deferred to Update so a
    /// few frames tick first and the game is live before we speak. Thereafter
    /// Update is where per-frame announcement pumping will live.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "io.ahicks.tangledeepaccess";
        public const string PluginName = "Tangledeep Access";

        // PluginVersion is generated from <Version> in Directory.Build.props (see the
        // GeneratePluginVersion target) so the props file is the single source of truth.

        private const int StartupDelayFrames = 60;
        private int _initCountdown = StartupDelayFrames;
        private bool _spoke;

        private PrismSpeech _speech;

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
        }

        private void Update()
        {
            if (_spoke)
                return;
            if (_initCountdown-- > 0)
                return;
            _spoke = true;

            if (_speech != null && _speech.Available)
            {
                _speech.Speak(PluginName + " " + PluginVersion + " loaded. Hello world.");
                Log.Info("spoke startup line via " + _speech.BackendName);
            }
            else
            {
                Log.Error("speech unavailable; no startup line spoken");
            }
        }
    }
}
