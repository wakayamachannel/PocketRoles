using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Net;

namespace PocketRoles.Chat
{
    /// <summary>
    /// v0.4b chat translation (DESIGN-v0.4 §K). Host only. Every chat line that reaches the host (other players'
    /// messages and the host's own) is translated on a background thread by Google (public gtx endpoint, no key) or
    /// DeepL (key in BepInEx/PocketRoles/deepl-key.txt) and the result is shown on the host's screen, broadcast to
    /// everyone and/or sent privately to players who chose a foreign language with /lang, per [Translate] options.
    /// The chat pipeline is never blocked: the AddChat postfix only enqueues, the HTTP work runs in a Task and the
    /// results come back through a thread-safe queue drained from a HudManager.Update postfix (main thread).
    /// Privacy: chat text is sent to the provider; the DeepL key is read from the key file, kept in memory, never
    /// logged and sent to DeepL only.
    /// </summary>
    public static class Translator
    {
        /// <summary>Translation is on when the option is enabled and the host mod is active in this lobby.</summary>
        public static bool Enabled => Options.TranslateEnabled && Core.Game.IsHostActive;

        private const int HttpTimeoutSeconds = 5;
        private const int CacheMinutes = 10;
        private const int CacheMaxEntries = 400;
        private const int MaxPendingJobs = 8;
        private const int MaxResultsPerFrame = 8;
        private const int AutoDetectMinChars = 6;
        private const double AutoDetectMinConfidence = 0.6;
        private const int KeyRefreshSeconds = 60;
        private const string UserAgent = "PocketRoles/" + PocketRolesPlugin.Version + " (Among Us host mod; +https://github.com)";

        // ------------------------------------------------------------------ types

        private sealed class Job
        {
            public byte PlayerId;
            public string Name;
            public string Text;
            public bool FromHost;
            public bool SenderDead;        // written by a ghost during a game: shown to dead players / a dead host only
            public List<string> Targets;   // "ja" | "zh" | "en", host target first
            public string Provider;        // "google" | "deepl" (already resolved)
            public string DeepLKey;        // snapshot, "" when unused
            public int MaxPerMinute;
            public bool AutoDetect;
            public bool AutoChecked;       // main thread: the auto-detect decision was taken for this job
        }

        private sealed class Result
        {
            public Job Job;
            public string Target;
            public string Translation;
            public string Detected;        // normalized "ja"/"zh"/"en" or the raw provider code, null when unknown
            public double Confidence = 1.0;
            public string Provider;
            public string Error;           // non-null → nothing to show, log/notice only
        }

        private sealed class CacheEntry
        {
            public string Translation;
            public string Detected;
            public double Confidence;
            public DateTime At;
        }

        // ------------------------------------------------------------------ state

        private static readonly ConcurrentQueue<Result> Results = new ConcurrentQueue<Result>();
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, CacheEntry> Cache = new Dictionary<string, CacheEntry>();
        private static readonly Queue<DateTime> RequestTimes = new Queue<DateTime>();
        private static int _pending;
        private static HttpClient _http;

        private static string _deepLKey = "";
        private static DateTime _deepLKeyReadAt = DateTime.MinValue;
        private static bool _warnedNoKey;
        private static bool _warnedFallback;

        // per-lobby / per-minute notice throttles (main thread)
        private static bool _errorNoticeShown;
        private static float _lastCapNoticeAt = -999f;
        private static float _lastErrorLogAt = -999f;
        private static byte _lastPlayerId = 255;
        private static string _lastText;
        private static float _lastAt = -999f;

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Translates <paramref name="text"/> written by <paramref name="playerId"/> and delivers the results per the
        /// [Translate] options (host screen / everyone / foreign players privately). Non-blocking: returns at once,
        /// the translation arrives a moment later. Commands, very short texts and texts without letters are ignored.
        /// </summary>
        public static void TranslateAndSend(byte playerId, string text)
        {
            try
            {
                if (!Enabled) return;
                if (string.IsNullOrWhiteSpace(text)) return;
                string t = text.Trim();
                if (t.StartsWith("/")) return;                       // commands are never translated
                if (t.IndexOf('<') >= 0) return;                     // mod messages carry rich-text tags; players cannot
                if (t.Length < Options.TranslateMinChars) return;
                if (!HasLetters(t)) return;                           // emoji / digits / punctuation only
                if (t.Length > 300) t = t.Substring(0, 300);

                // The same line can reach AddChat twice within a frame in exotic cases (local echo + relay): drop it.
                float now = UnityEngine.Time.time;
                if (_lastPlayerId == playerId && _lastText == t && now - _lastAt < 2f) return;
                _lastPlayerId = playerId; _lastText = t; _lastAt = now;

                var pc = Core.Game.Player(playerId);
                bool fromHost = pc != null && pc.AmOwner;
                var targets = BuildTargets(playerId, fromHost);
                // Same-language chat must not burn the per-minute cap: kana means Japanese (no "ja" request needed),
                // pure ASCII letters mean English (no "en" request). Best effort; the provider's detection still
                // decides for every other target.
                if (HasKana(t)) targets.Remove(Lang.Ja);
                else if (IsAsciiOnly(t)) targets.Remove(Lang.En);
                if (targets.Count == 0) return;
                // Ghost chat (vanilla: dead → dead only) must never reach alive players through the translation.
                bool senderDead = Core.Game.InProgress && Core.Game.IsDead(playerId);

                if (Volatile.Read(ref _pending) >= MaxPendingJobs)
                {
                    PocketRolesPlugin.Logger?.LogWarning("Translator: too many pending translations, dropped one");
                    return;
                }

                string provider = ResolveProvider(out string key);
                var job = new Job
                {
                    PlayerId = playerId,
                    Name = SafeName(Core.Game.NameOf(playerId)),
                    Text = t,
                    FromHost = fromHost,
                    SenderDead = senderDead,
                    Targets = targets,
                    Provider = provider,
                    DeepLKey = provider == "deepl" ? key : "",
                    MaxPerMinute = Options.TranslateMaxPerMinute,
                    AutoDetect = Options.TranslateAutoDetectLang && !fromHost && t.Length >= AutoDetectMinChars,
                };
                Interlocked.Increment(ref _pending);
                Task.Run(() => ProcessAsync(job));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogError($"Translator.TranslateAndSend: {e}");
            }
        }

        /// <summary>One short welcome line (≤ 100 chars) telling players translation is on; "" when it is off.</summary>
        public static string WelcomeLine()
        {
            if (!Enabled) return "";
            return Lang.T("translate.welcome", "🌐 翻訳あり: 外国語で書いても自動で翻訳されます", "🌐 Auto-translation is on: chat in your own language", "🌐 自动翻译已开启：可以用母语聊天");
        }

        /// <summary>Help-text line about /lang and the automatic translation; "" when translation is off.</summary>
        public static string HelpLine()
        {
            if (!Enabled) return "";
            return Lang.T("translate.help", "/lang en|zh|ja で言語変更。外国語のチャットは自動翻訳されます。", "/lang en|zh|ja changes your language. Foreign-language chat is translated automatically.", "/lang en|zh|ja 切换语言。外语聊天会自动翻译。");
        }

        /// <summary>Main thread (HudManager.Update): hands finished translations to the chat.</summary>
        public static void Tick()
        {
            int n = 0;
            while (n < MaxResultsPerFrame && Results.TryDequeue(out var r))
            {
                n++;
                try { Deliver(r); }
                catch (Exception e) { PocketRolesPlugin.Logger?.LogError($"Translator.Deliver: {e}"); }
            }
        }

        /// <summary>New lobby: per-lobby notice flags start over (the cache and the key snapshot are kept).</summary>
        internal static void OnLobbyJoined()
        {
            _errorNoticeShown = false;
            _lastPlayerId = 255;
            _lastText = null;
        }

        // ------------------------------------------------------------------ main-thread helpers

        /// <summary>Languages to request: the host's language first (unless the host wrote it), then every foreign /lang choice.</summary>
        private static List<string> BuildTargets(byte senderId, bool fromHost)
        {
            var targets = new List<string>(3);
            string hostTarget = Options.TranslateTargetLang;
            if (!fromHost && (Options.TranslateShowOnHost || Options.TranslateBroadcastToAll || Options.TranslateAutoDetectLang))
                targets.Add(hostTarget);
            // Per-player translations need client-addressed chat, which an unregistered (compat) lobby has not
            // (findings #21/#28): no foreign-language requests there (broadcast only), which also spares the cap.
            if (Options.TranslateForPlayers && !Registration.CompatMode)
            {
                string def = Lang.Default;
                foreach (var pc in Core.Game.AllPlayers())
                {
                    if (pc.AmOwner || pc.PlayerId == senderId || pc.Data == null || pc.Data.Disconnected) continue;
                    string l = Lang.PlayerLang(pc.PlayerId);
                    if (l == def) continue;                           // only players who chose a foreign language
                    if (!targets.Contains(l)) targets.Add(l);
                }
            }
            return targets;
        }

        /// <summary>"google" or "deepl" per [Translate] Provider and the key file (re-read at most once a minute).</summary>
        private static string ResolveProvider(out string key)
        {
            key = "";
            string p = Options.TranslateProvider;
            if (p == "google") return "google";
            key = CurrentDeepLKey();
            if (key.Length > 0) return "deepl";
            if (p == "deepl" && !_warnedNoKey)
            {
                _warnedNoKey = true;
                PocketRolesPlugin.Logger?.LogWarning($"Translator: [Translate] Provider=deepl but BepInEx/PocketRoles/{Options.DeepLKeyFileName} holds no key; using Google");
            }
            return "google";
        }

        private static string CurrentDeepLKey()
        {
            lock (Sync)
            {
                var now = DateTime.UtcNow;
                if ((now - _deepLKeyReadAt).TotalSeconds >= KeyRefreshSeconds)
                {
                    _deepLKeyReadAt = now;
                    _deepLKey = Options.ReadDeepLKey() ?? "";
                }
                return _deepLKey;
            }
        }

        private static string Title => Lang.T("translate.title", "訳", "Tr", "译");

        private static void Deliver(Result r)
        {
            var job = r.Job;
            if (job == null) return;
            if (r.Error != null)
            {
                float now = UnityEngine.Time.time;
                if (r.Error == "capped")
                {
                    if (now - _lastCapNoticeAt > 60f)
                    {
                        _lastCapNoticeAt = now;
                        Chat.Local(Chat.Title, Lang.TF("translate.capped", "翻訳の回数制限（１分あたり{0}回）に達したため、しばらく翻訳を省略します。", "Translation cap reached ({0} per minute); skipping for a while.", job.MaxPerMinute));
                    }
                    return;
                }
                if (now - _lastErrorLogAt > 30f)
                {
                    _lastErrorLogAt = now;
                    PocketRolesPlugin.Logger?.LogWarning($"Translator: {r.Provider} failed for {r.Target}: {r.Error}");
                }
                if (!_errorNoticeShown && Enabled)
                {
                    _errorNoticeShown = true;
                    Chat.Local(Chat.Title, Lang.TF("translate.error", "翻訳サービスに接続できません（{0}）。", "Translation service unavailable ({0}).", r.Provider + ": " + r.Error));
                }
                return;
            }
            if (!Enabled) return;

            string detected = r.Detected;
            AutoDetectLanguage(job, detected, r.Confidence);

            if (string.IsNullOrWhiteSpace(r.Translation)) return;
            if (detected != null && detected == r.Target) return;                 // already in that language
            string translation = Clean(r.Translation);
            if (string.Equals(translation, job.Text, StringComparison.OrdinalIgnoreCase)) return;
            string line = job.Name + ": " + translation;

            string hostTarget = Options.TranslateTargetLang;
            // A ghost's line (vanilla: dead → dead only) is never broadcast; alive recipients are skipped below.
            bool ghost = job.SenderDead && Core.Game.InProgress;
            // 併用 (BroadcastToAll + TranslateForPlayers, the default since 2026-09-08): the host-language line goes to
            // everyone, the foreign players get their own language privately — but never both for the same player (a
            // zh player is left out of the ja broadcast when a zh line is on its way to them). Private lines exist only
            // in a registered lobby: unregistered (compat) lobbies allow no client-addressed message (findings #21/#22).
            bool forPlayers = Options.TranslateForPlayers && !Registration.CompatMode;
            bool broadcast = Options.TranslateBroadcastToAll && r.Target == hostTarget && !ghost;
            if (!job.FromHost && r.Target == hostTarget)
            {
                var lp = PlayerControl.LocalPlayer;
                bool hostMayRead = !ghost || (lp != null && Core.Game.IsDead(lp.PlayerId));
                using (Lang.Scope(hostTarget))
                {
                    if (broadcast) BroadcastHostLanguage(job, line, forPlayers, hostTarget);
                    else if (Options.TranslateShowOnHost && hostMayRead) Chat.Local(Title, line);
                }
            }
            if (forPlayers && !broadcast)
            {
                string def = Lang.Default;
                // Every private chat RPC leaves from the host's NetId: stagger the recipients like Chat.All(builder)
                // so several foreign players never get the same line in one frame.
                float start = 0f;
                foreach (var pc in Core.Game.AllPlayers())
                {
                    if (pc.AmOwner || pc.PlayerId == job.PlayerId || pc.Data == null || pc.Data.Disconnected) continue;
                    if (ghost && !Core.Game.IsDead(pc.PlayerId)) continue;
                    string l = Lang.PlayerLang(pc.PlayerId);
                    if (l == def || l != r.Target) continue;
                    int clientId = Rpc.ClientIdOf(pc);
                    if (clientId < 0) continue;
                    List<string> chunks; string title;
                    using (Lang.Scope(l)) { title = Title; chunks = Chat.Split(line); }
                    if (chunks.Count == 0) continue;
                    Chat.SendChunksTo(clientId, title, chunks, start);
                    start += Chat.ChunkSpacing * chunks.Count;
                }
            }
        }

        /// <summary>
        /// The host-language translation for everyone (BroadcastToAll): the host locally, then one paced private copy
        /// per client — except the players who will get the same line in their own language (TranslateForPlayers:
        /// a foreign /lang choice that is one of the job's targets), so nobody receives two translations of one
        /// message. The sender stays in (they see that their line was translated); a player whose foreign request
        /// later fails gets nothing but the original, which is what they would have read anyway.
        /// </summary>
        private static void BroadcastHostLanguage(Job job, string line, bool forPlayers, string hostTarget)
        {
            var chunks = Chat.Split(line);
            if (chunks.Count == 0) return;
            string title = Title;
            if (Registration.CompatMode)
            {
                // Unregistered lobby: ONE public host broadcast (it shows on the host's screen too); no private copies.
                Chat.SendPublicChunks(title, chunks);
                return;
            }
            foreach (var chunk in chunks) Chat.Local(title, chunk);
            string def = Lang.Default;
            float start = 0f;
            int sent = 0, deferred = 0;
            foreach (var pc in Core.Game.AllPlayers())
            {
                if (pc.AmOwner || pc.Data == null || pc.Data.Disconnected) continue;
                if (forPlayers && pc.PlayerId != job.PlayerId)
                {
                    string l = Lang.PlayerLang(pc.PlayerId);
                    if (l != def && l != hostTarget && job.Targets != null && job.Targets.Contains(l)) { deferred++; continue; }
                }
                int clientId = Rpc.ClientIdOf(pc);
                if (clientId < 0) continue;
                Chat.SendChunksTo(clientId, title, chunks, start);
                start += Chat.ChunkSpacing * chunks.Count;
                sent++;
            }
            if (deferred > 0)
                PocketRolesPlugin.Logger?.LogDebug($"Translator: {hostTarget} line broadcast to {sent} client(s); {deferred} get their own language instead");
        }

        /// <summary>
        /// A player who never used /lang and writes in a supported foreign language gets that language (once) plus a
        /// one-line private notice in it, so the mod's messages become readable for them right away.
        /// </summary>
        private static void AutoDetectLanguage(Job job, string detected, double confidence)
        {
            if (!job.AutoDetect || job.AutoChecked) return;
            if (detected == null) return;                              // unknown → wait for a result that knows
            job.AutoChecked = true;
            if (!Options.TranslateAutoDetectLang) return;
            if (Array.IndexOf(Lang.Supported, detected) < 0) return;
            if (confidence < AutoDetectMinConfidence) return;
            string def = Lang.Default;
            if (detected == def) return;
            if (Lang.HasPlayerLang(job.PlayerId)) return;
            var pc = Core.Game.Player(job.PlayerId);
            if (pc == null || pc.AmOwner || pc.Data == null || pc.Data.Disconnected) return;
            Lang.SetPlayerLang(job.PlayerId, detected);
            PocketRolesPlugin.Logger?.LogInfo($"Translator: player {job.PlayerId} language auto-set to {detected}");
            using (Lang.Scope(detected))
            {
                // The way back follows the lobby like Chat.LangSwitchLine: private "/cmd lang" in a registered lobby,
                // "/lang" otherwise, and no command hint at all when players cannot use commands.
                string notice;
                if (Chat.PlayerCommandsAvailable)
                {
                    string back = (Registration.ShouldRegister ? "/cmd lang " : "/lang ") + def;
                    notice = Lang.TF("translate.autolang",
                        "表示言語を{0}にしました。{1} で戻せます。",
                        "Display language switched to {0}. Type {1} to switch back.",
                        Lang.DisplayName(detected), back);
                }
                else
                {
                    notice = Lang.TF("translate.autolang.nocmd",
                        "表示言語を{0}にしました。",
                        "Display language switched to {0}.",
                        Lang.DisplayName(detected));
                }
                Chat.To(job.PlayerId, Chat.Title, notice);
            }
        }

        /// <summary>Hiragana / katakana present → the text is Japanese.</summary>
        private static bool HasKana(string s)
        {
            foreach (char c in s) if ((c >= '぀' && c <= 'ヿ') || (c >= 'ｦ' && c <= 'ﾟ')) return true;
            return false;
        }

        /// <summary>Only ASCII characters (letters, digits, punctuation) → treated as English.</summary>
        private static bool IsAsciiOnly(string s)
        {
            foreach (char c in s) if (c > (char)0x7f) return false;
            return true;
        }

        private static bool HasLetters(string s)
        {
            int letters = 0;
            foreach (char c in s) if (char.IsLetter(c)) letters++;
            return letters >= 2;
        }

        private static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            return Lang.StripTags(name).Replace('<', '＜').Replace('>', '＞');
        }

        private static string Clean(string s)
        {
            return (s ?? "").Replace('<', '＜').Replace('>', '＞').Replace("\r", " ").Replace("\n", " ").Trim();
        }

        // ------------------------------------------------------------------ background work

        private static HttpClient Http
        {
            get
            {
                lock (Sync)
                {
                    if (_http == null)
                    {
                        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds) };
                        try { h.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent); }
                        catch (Exception) { h.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PocketRoles/" + PocketRolesPlugin.Version); }
                        _http = h;
                    }
                    return _http;
                }
            }
        }

        private static async Task ProcessAsync(Job job)
        {
            try
            {
                string detected = null;
                double confidence = 1.0;
                foreach (string target in job.Targets)
                {
                    if (detected != null && detected == target)
                    {
                        // Known to be in that language already; tell the main thread so auto-detect still runs.
                        Results.Enqueue(new Result { Job = job, Target = target, Translation = "", Detected = detected, Confidence = confidence, Provider = job.Provider });
                        continue;
                    }
                    if (TryCache(target, job.Text, out var cached))
                    {
                        if (detected == null) { detected = cached.Detected; confidence = cached.Confidence; }
                        Results.Enqueue(new Result { Job = job, Target = target, Translation = cached.Translation, Detected = cached.Detected, Confidence = cached.Confidence, Provider = "cache" });
                        continue;
                    }
                    if (!TryReserveRequest(job.MaxPerMinute))
                    {
                        Results.Enqueue(new Result { Job = job, Target = target, Error = "capped", Provider = job.Provider });
                        break;
                    }
                    Result r = null;
                    string provider = job.Provider;
                    try
                    {
                        r = provider == "deepl"
                            ? await DeepLAsync(job.Text, target, job.DeepLKey).ConfigureAwait(false)
                            : await GoogleAsync(job.Text, target).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        r = new Result { Error = ShortError(e) };
                    }
                    if (r.Error != null && provider == "deepl")
                    {
                        // DeepL down / quota / bad key: Google is the best-effort fallback (logged once).
                        if (!_warnedFallback)
                        {
                            _warnedFallback = true;
                            PocketRolesPlugin.Logger?.LogWarning($"Translator: DeepL failed ({r.Error}); falling back to Google for this session's failures");
                        }
                        if (TryReserveRequest(job.MaxPerMinute))
                        {
                            try { r = await GoogleAsync(job.Text, target).ConfigureAwait(false); }
                            catch (Exception e) { r = new Result { Error = ShortError(e) }; }
                        }
                        provider = "google";
                    }
                    r.Job = job;
                    r.Target = target;
                    r.Provider = provider;
                    if (r.Error == null)
                    {
                        if (detected == null) { detected = r.Detected; confidence = r.Confidence; }
                        Store(target, job.Text, r);
                    }
                    Results.Enqueue(r);
                }
            }
            catch (Exception e)
            {
                Results.Enqueue(new Result { Job = job, Target = job.Targets.Count > 0 ? job.Targets[0] : "", Error = ShortError(e), Provider = job.Provider });
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
            }
        }

        private static string ShortError(Exception e)
        {
            if (e is TaskCanceledException) return "timeout";
            if (e is HttpRequestException) return "network: " + e.GetType().Name;
            return e.GetType().Name;
        }

        // ---- rate cap (any thread)

        private static bool TryReserveRequest(int maxPerMinute)
        {
            lock (Sync)
            {
                var now = DateTime.UtcNow;
                while (RequestTimes.Count > 0 && (now - RequestTimes.Peek()).TotalSeconds > 60) RequestTimes.Dequeue();
                if (RequestTimes.Count >= Math.Max(1, maxPerMinute)) return false;
                RequestTimes.Enqueue(now);
                return true;
            }
        }

        // ---- cache (any thread)

        private static bool TryCache(string target, string text, out CacheEntry entry)
        {
            lock (Sync)
            {
                if (Cache.TryGetValue(target + "\n" + text, out entry))
                {
                    if ((DateTime.UtcNow - entry.At).TotalMinutes < CacheMinutes) return true;
                    Cache.Remove(target + "\n" + text);
                }
                entry = null;
                return false;
            }
        }

        private static void Store(string target, string text, Result r)
        {
            lock (Sync)
            {
                if (Cache.Count >= CacheMaxEntries)
                {
                    var now = DateTime.UtcNow;
                    var drop = new List<string>();
                    foreach (var kv in Cache) if ((now - kv.Value.At).TotalMinutes >= CacheMinutes) drop.Add(kv.Key);
                    if (drop.Count == 0) Cache.Clear();
                    else foreach (var k in drop) Cache.Remove(k);
                }
                Cache[target + "\n" + text] = new CacheEntry { Translation = r.Translation, Detected = r.Detected, Confidence = r.Confidence, At = DateTime.UtcNow };
            }
        }

        // ---- providers (background thread)

        /// <summary>Google's public "gtx" endpoint: no key, best effort, returns the detected source language.</summary>
        private static async Task<Result> GoogleAsync(string text, string target)
        {
            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=" + GoogleCode(target) + "&dt=t&q=" + Uri.EscapeDataString(text);
            using (var resp = await Http.GetAsync(url).ConfigureAwait(false))
            {
                if (!resp.IsSuccessStatusCode) return new Result { Error = "HTTP " + (int)resp.StatusCode };
                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using (var doc = JsonDocument.Parse(body))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 1) return new Result { Error = "bad response" };
                    var sb = new StringBuilder();
                    var segs = root[0];
                    if (segs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var seg in segs.EnumerateArray())
                        {
                            if (seg.ValueKind != JsonValueKind.Array || seg.GetArrayLength() < 1) continue;
                            var first = seg[0];
                            if (first.ValueKind == JsonValueKind.String) sb.Append(first.GetString());
                        }
                    }
                    string detected = null;
                    if (root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String) detected = NormalizeDetected(root[2].GetString());
                    double confidence = 1.0;
                    if (root.GetArrayLength() > 6 && root[6].ValueKind == JsonValueKind.Number && root[6].TryGetDouble(out var c)) confidence = c;
                    return new Result { Translation = sb.ToString(), Detected = detected, Confidence = confidence };
                }
            }
        }

        /// <summary>DeepL v2: free keys end with ":fx" and go to api-free.deepl.com, others to api.deepl.com.</summary>
        private static async Task<Result> DeepLAsync(string text, string target, string key)
        {
            if (string.IsNullOrEmpty(key)) return new Result { Error = "no key" };
            string host = key.EndsWith(":fx", StringComparison.OrdinalIgnoreCase) ? "api-free.deepl.com" : "api.deepl.com";
            using (var req = new HttpRequestMessage(HttpMethod.Post, "https://" + host + "/v2/translate"))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", key);
                req.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("text", text),
                    new KeyValuePair<string, string>("target_lang", DeepLCode(target)),
                });
                using (var resp = await Http.SendAsync(req).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        int code = (int)resp.StatusCode;
                        string why = code == 403 ? "invalid key" : code == 456 ? "quota exceeded" : code == 429 ? "too many requests" : "HTTP " + code;
                        return new Result { Error = why };
                    }
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using (var doc = JsonDocument.Parse(body))
                    {
                        if (!doc.RootElement.TryGetProperty("translations", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 1)
                            return new Result { Error = "bad response" };
                        var first = arr[0];
                        string tr = first.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : "";
                        string det = first.TryGetProperty("detected_source_language", out var d) && d.ValueKind == JsonValueKind.String ? NormalizeDetected(d.GetString()) : null;
                        return new Result { Translation = tr, Detected = det, Confidence = 1.0 };
                    }
                }
            }
        }

        private static string GoogleCode(string lang)
        {
            switch (lang)
            {
                case Lang.Zh: return "zh-CN";
                case Lang.En: return "en";
                default: return "ja";
            }
        }

        private static string DeepLCode(string lang)
        {
            switch (lang)
            {
                case Lang.Zh: return "ZH";
                case Lang.En: return "EN-US";
                default: return "JA";
            }
        }

        /// <summary>Provider language code → "ja" / "zh" / "en", or the lower-cased raw code for other languages.</summary>
        private static string NormalizeDetected(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            string c = code.Trim().ToLowerInvariant();
            if (c == "ja" || c == "jp") return Lang.Ja;
            if (c == "en" || c.StartsWith("en-")) return Lang.En;
            if (c == "zh" || c.StartsWith("zh-") || c == "zh_cn" || c == "zh_tw") return Lang.Zh;
            return c;
        }
    }

    // ====================================================================== patches

    /// <summary>Every chat line shown on the host (others' messages and the host's own) is offered to the translator.</summary>
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat))]
    internal static class Translator_AddChatPatch
    {
        private static void Postfix(PlayerControl sourcePlayer, string chatText)
        {
            try
            {
                if (sourcePlayer == null || string.IsNullOrEmpty(chatText)) return;
                if (!Translator.Enabled) return;
                Translator.TranslateAndSend(sourcePlayer.PlayerId, chatText);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Translator_AddChatPatch: {e}");
            }
        }
    }

    /// <summary>Main-thread drain of the finished translations.</summary>
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    internal static class Translator_TickPatch
    {
        private static void Postfix()
        {
            try
            {
                Translator.Tick();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Translator_TickPatch: {e}");
            }
        }
    }

    /// <summary>New lobby: per-lobby notice flags start over.</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class Translator_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            try
            {
                Translator.OnLobbyJoined();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Translator_OnGameJoinedPatch: {e}");
            }
        }
    }
}
