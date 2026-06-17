using System;
using System.Collections.Concurrent;
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

        private sealed class EvalJob {
            public string Code;
            public string Result = "";
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private readonly SpeechLog _speech = new SpeechLog();
        private readonly CSharpEvaluator _evaluator = new CSharpEvaluator();
        private readonly ConcurrentQueue<EvalJob> _jobs = new ConcurrentQueue<EvalJob>();
        private DevHttpServer _http;
        private bool _enabled;

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

        /// <summary>Run queued eval jobs on the main thread. Call once per frame from Update.</summary>
        public void Pump() {
            if (!_enabled) {
                return;
            }
            EvalJob job;
            while (_jobs.TryDequeue(out job)) {
                try {
                    job.Result = _evaluator.Eval(job.Code);
                } catch (Exception e) {
                    job.Result = "[host error] " + e + "\n";
                }
                job.Done.Set();
            }
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
                var job = new EvalJob { Code = body };
                _jobs.Enqueue(job);
                if (!job.Done.Wait(TimeSpan.FromSeconds(30))) {
                    return "[timeout] eval did not run within 30s (is the game frozen / not pumping?)\n";
                }
                return job.Result;
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
    }
}
