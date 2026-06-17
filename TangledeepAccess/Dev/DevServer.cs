using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using TangledeepAccess.Speech;
using TangledeepAccess.Util;

namespace TangledeepAccess.Dev {
    /// <summary>
    /// Dev-only in-process driver, gated behind the TANGLEDEEP_DEV env var (set by
    /// run-game.ps1). Exposes a loopback HTTP server so an external driver can:
    ///   POST /eval         body = C# source, run against the live game (REPL state
    ///                      persists across calls); returns output + result/errors.
    ///   GET  /speech?since=N   lines the mod has spoken since cursor N (we can't hear
    ///                          the TTS, so this is how we observe it).
    ///   GET  /health       liveness.
    ///
    /// Eval runs on the Unity main thread: HTTP requests enqueue a job and block until
    /// <see cref="Pump"/> (called from Plugin.Update) executes it. /speech reads a
    /// thread-safe buffer directly off the HTTP thread. Not shipped to players.
    /// </summary>
    public sealed class DevServer {
        public const string EnableEnv = "TANGLEDEEP_DEV";
        public const string PortEnv = "TANGLEDEEP_DEV_PORT";
        private const int DefaultPort = 8770;

        private sealed class Job {
            public Func<string> Work;
            public string Result = "";
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private readonly SpeechLog _speech = new SpeechLog();
        private readonly CSharpEvaluator _evaluator = new CSharpEvaluator();
        private readonly ConcurrentQueue<Job> _jobs = new ConcurrentQueue<Job>();
        private DevHttpServer _http;
        private bool _enabled;
        private bool _runInBackgroundForced;

        /// <summary>Stand up the server if TANGLEDEEP_DEV=1; otherwise stay inert.</summary>
        public void Start() {
            if (Environment.GetEnvironmentVariable(EnableEnv) != "1") {
                return;
            }

            int port = DefaultPort;
            string p = Environment.GetEnvironmentVariable(PortEnv);
            if (!string.IsNullOrEmpty(p)) {
                int.TryParse(p, out port);
            }

            // Tap every string the mod speaks (single chokepoint) into the ring buffer.
            PrismSpeech.Observer = _speech.Add;

            try {
                _http = new DevHttpServer(port, HandleRequest);
                _http.Start();
                _enabled = true;
                Log.Info("Dev server on http://127.0.0.1:" + port + " (POST /eval, GET /speech)");
            } catch (Exception e) {
                Log.Error("Dev server failed to start: " + e);
            }
        }

        /// <summary>Run queued main-thread jobs. Call once per frame from Update.</summary>
        public void Pump() {
            if (!_enabled) {
                return;
            }
            if (!_runInBackgroundForced) {
                // Insurance: keep the game simulating while unfocused, which is how we drive it.
                // Tangledeep already ships this true; guard against a future patch flipping it.
                UnityEngine.Application.runInBackground = true;
                _runInBackgroundForced = true;
            }
            Job job;
            while (_jobs.TryDequeue(out job)) {
                try {
                    job.Result = job.Work() ?? "";
                } catch (Exception e) {
                    job.Result = "[host error] " + e + "\n";
                }
                job.Done.Set();
            }
        }

        /// <summary>Run <paramref name="work"/> on the main thread (next Pump) and block for its result.</summary>
        private string OnMainThread(Func<string> work, int timeoutSeconds = 30) {
            var job = new Job { Work = work };
            _jobs.Enqueue(job);
            if (!job.Done.Wait(TimeSpan.FromSeconds(timeoutSeconds))) {
                return "[timeout] main thread did not run the job within " + timeoutSeconds + "s (frozen / not pumping?)\n";
            }
            return job.Result;
        }

        // Runs on the HTTP thread.
        private string HandleRequest(string method, string path, string body) {
            string route = path;
            string query = "";
            int q = path.IndexOf('?');
            if (q >= 0) {
                route = path.Substring(0, q);
                query = path.Substring(q + 1);
            }

            if (route == "/eval" && method == "POST") {
                if (string.IsNullOrWhiteSpace(body)) {
                    return "[empty] POST C# source as the request body\n";
                }
                return OnMainThread(() => _evaluator.Eval(body));
            }

            if (route == "/gui/game" && method == "GET") {
                return OnMainThread(() => GuiInspector.DumpGameUi());
            }

            if (route == "/gui/mod" && method == "GET") {
                return OnMainThread(() => GuiInspector.DumpModUi());
            }

            if (route == "/input" && method == "POST") {
                string verb = (body ?? "").Trim();
                return OnMainThread(() => InputInjector.Inject(verb));
            }

            if (route == "/screenshot" && method == "GET") {
                return Screenshot();
            }

            if (route == "/speech" && method == "GET") {
                long since = 0;
                foreach (string kv in query.Split('&')) {
                    if (kv.StartsWith("since=", StringComparison.Ordinal)) {
                        long.TryParse(kv.Substring("since=".Length), out since);
                    }
                }
                long next;
                string lines = _speech.Render(since, out next);
                return "cursor: " + next + "\n" + lines;
            }

            if (route == "/health" || route == "/") {
                return "ok\n";
            }

            return "[404] " + method + " " + route + "\n";
        }

        // Trigger a screenshot on the main thread, then wait (on this HTTP thread) for the PNG,
        // which ScreenCapture writes asynchronously over the next frame(s). Returns the path,
        // which the driver then reads to view the frame.
        private string Screenshot() {
            string path = Path.Combine(Path.GetTempPath(), "td_shot.png");
            OnMainThread(() => {
                try {
                    if (File.Exists(path)) {
                        File.Delete(path);
                    }
                } catch {
                }
                UnityEngine.ScreenCapture.CaptureScreenshot(path);
                return "requested";
            });

            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < 8) {
                try {
                    if (File.Exists(path)) {
                        long size = new FileInfo(path).Length;
                        if (size > 0) {
                            Thread.Sleep(60); // let the write settle, then confirm size is stable
                            if (new FileInfo(path).Length == size) {
                                return path + "\n";
                            }
                        }
                    }
                } catch {
                }
                Thread.Sleep(50);
            }
            return "[timeout] screenshot not written within 8s\n";
        }
    }
}
