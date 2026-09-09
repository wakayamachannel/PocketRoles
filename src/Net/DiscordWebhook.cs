using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HarmonyLib;
using InnerNet;
using PocketRoles.Core;

namespace PocketRoles.Net
{
    /// <summary>
    /// [Discord] WebhookUrl (v0.4.5): the host posts its lobby to a Discord channel through a webhook — no bot, no token,
    /// just the webhook URL from "チャンネルの編集 → 連携サービス → ウェブフック". One message per lobby
    /// ("部屋コード ABCDEF — 3/15人 募集中"), edited in place when players join / leave, the game starts or ends, and
    /// the lobby closes. Every HTTP call runs on a thread-pool thread with data gathered on the main thread first;
    /// changes are coalesced (one edit per <see cref="MinInterval"/> seconds) so a full lobby never trips Discord's rate limit.
    /// </summary>
    public static class DiscordWebhook
    {
        private const float MinInterval = 5f;
        private const string Tag = "discord.update";
        private static readonly Regex MessageId = new Regex("\"id\":\"(\\d+)\",\"channel_id\"", RegexOptions.Compiled);
        private static readonly Regex AnyId = new Regex("\"id\":\"(\\d+)\"", RegexOptions.Compiled);

        private static HttpClient _http;
        private static readonly object Sync = new object();

        private static int _lobbyGameId;          // GameId the current message belongs to
        private static string _messageId;         // Discord message id (null until the first POST answered)
        private static bool _posting;             // a POST is in flight (edits wait for the id)
        private static string _pendingContent;    // latest content while a request is in flight / throttled
        private static float _lastSentAt = -100f;
        private static string _lastContent;
        private static bool _inGame;
        private static bool _warnedUrl;

        private static bool Enabled
        {
            get
            {
                string url = Options.DiscordWebhookUrl;
                if (string.IsNullOrEmpty(url)) return false;
                if (!url.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase)
                    && !url.StartsWith("https://discordapp.com/api/webhooks/", StringComparison.OrdinalIgnoreCase)
                    && !url.StartsWith("https://ptb.discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase)
                    && !url.StartsWith("https://canary.discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_warnedUrl) { _warnedUrl = true; PocketRolesPlugin.Logger.LogWarning("Discord: [Discord] WebhookUrl is not a Discord webhook URL (https://discord.com/api/webhooks/...) — ignored"); }
                    return false;
                }
                return Options.DiscordAnnounce;
            }
        }

        private static HttpClient Http
        {
            get
            {
                lock (Sync)
                {
                    if (_http == null)
                    {
                        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                        try { h.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PocketRoles/" + PocketRolesPlugin.Version + " (Discord webhook)"); } catch (Exception) { }
                        _http = h;
                    }
                    return _http;
                }
            }
        }

        // ------------------------------------------------------------------ events (main thread)

        /// <summary>Host joined / created a lobby: a new message for a new GameId (same lobby after a game: keep editing).</summary>
        internal static void OnLobby()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || client.NetworkMode != NetworkModes.OnlineGame) return;
                _inGame = false;
                if (client.GameId != _lobbyGameId)
                {
                    if (_messageId != null) CloseCurrent("new lobby"); // a lobby lost without ExitGame (disconnect, re-host)
                    CloseStale(client.GameId);
                    _lobbyGameId = client.GameId;
                    _messageId = null;
                    _posting = false;
                    _lastContent = null;
                    _pendingContent = null;
                }
                Touch();
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook.OnLobby: {e}"); }
        }

        // ------------------------------------------------------------------ stale message from a crashed / killed game

        /// <summary>
        /// The id of the live lobby message is kept in BepInEx\PocketRoles\discord-last.txt. When the game was closed
        /// without ExitGame (crash, task manager, power loss) that message would say "募集中" forever; the next lobby
        /// (or the next start of the game) edits it to "閉じました" first.
        /// </summary>
        private static string StatePath
        {
            get
            {
                try { return System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "PocketRoles", "discord-last.txt"); }
                catch (Exception) { return null; }
            }
        }

        private static void Remember(string messageId, int gameId)
        {
            try
            {
                string p = StatePath; if (p == null) return;
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p));
                System.IO.File.WriteAllText(p, messageId + " " + gameId + " " + GameCode.IntToGameName(gameId));
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"Discord: could not remember the message id: {e.Message}"); }
        }

        private static void Forget()
        {
            try { string p = StatePath; if (p != null && System.IO.File.Exists(p)) System.IO.File.Delete(p); } catch (Exception) { }
        }

        /// <summary>A remembered message that is not the current lobby's: close it out (best effort) and forget it.</summary>
        internal static void CloseStale(int currentGameId)
        {
            try
            {
                if (!Enabled) return;
                string p = StatePath; if (p == null || !System.IO.File.Exists(p)) return;
                string[] parts = System.IO.File.ReadAllText(p).Trim().Split(' ');
                Forget();
                if (parts.Length < 3) return;
                if (int.TryParse(parts[1], out int gid) && gid == currentGameId) return; // same lobby (play again): still live
                string text = Lang.T("discord.closed", "部屋 {0} は閉じました。", "Lobby {0} has closed.", "房间 {0} 已关闭。").Replace("{0}", "`" + parts[2] + "`");
                PocketRolesPlugin.Logger.LogInfo($"Discord: closing the stale lobby message of {parts[2]} (id {parts[0]})");
                SendRaw(text, parts[0]);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"Discord: stale message: {e.Message}"); }
        }

        /// <summary>Fire-and-forget PATCH of an arbitrary message id (stale message clean-up).</summary>
        private static void SendRaw(string content, string id)
        {
            string url = Options.DiscordWebhookUrl;
            string body = "{\"content\":\"" + Escape(content) + "\",\"allowed_mentions\":{\"parse\":[]}}";
            Task.Run(async () =>
            {
                try
                {
                    using (var payload = new StringContent(body, Encoding.UTF8, "application/json"))
                    {
                        var req = new HttpRequestMessage(new HttpMethod("PATCH"), MessageUrl(url, id)) { Content = payload };
                        var resp = await Http.SendAsync(req).ConfigureAwait(false);
                        string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode) Replies.Enqueue(() => PocketRolesPlugin.Logger.LogWarning($"Discord: closing edit failed: {(int)resp.StatusCode} {Trim(text)}"));
                    }
                }
                catch (Exception e) { Replies.Enqueue(() => PocketRolesPlugin.Logger.LogWarning($"Discord: stale message edit failed: {e.Message}")); }
            });
        }

        internal static void OnPlayersChanged() { try { Touch(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook.OnPlayersChanged: {e}"); } }

        internal static void OnGameStarted() { try { _inGame = true; PocketRolesPlugin.Logger.LogInfo("Discord: game started → update"); Touch(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook.OnGameStarted: {e}"); } }

        internal static void OnGameEnded() { try { _inGame = false; PocketRolesPlugin.Logger.LogInfo("Discord: game ended → update"); Touch(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook.OnGameEnded: {e}"); } }

        /// <summary>The host leaves the lobby (ExitGame or a disconnect): the message is closed out (best effort, fire and forget).</summary>
        internal static void OnLobbyClosed() { CloseCurrent("closed"); }

        private static void CloseCurrent(string why)
        {
            try
            {
                if (!Enabled || _lobbyGameId == 0) return;
                string code = GameCode.IntToGameName(_lobbyGameId);
                string text = Lang.T("discord.closed", "部屋 {0} は閉じました。", "Lobby {0} has closed.", "房间 {0} 已关闭。").Replace("{0}", "`" + code + "`");
                _pendingContent = null; _flushAt = -1f; _lobbyAt = -1f; _playersAt = -1f;
                if (_messageId == null)
                {
                    // the first POST is still in flight: its reply (Remember) is for a lobby that no longer exists —
                    // the remembered id is closed by CloseStale at the next lobby / start of the game
                    if (_posting) PocketRolesPlugin.Logger.LogInfo($"Discord: lobby {code} {why} before its message was posted; the message is closed later");
                    _lobbyGameId = 0;
                    return;
                }
                PocketRolesPlugin.Logger.LogInfo($"Discord: lobby {code} {why} → message closed");
                string id = _messageId;
                _messageId = null;
                _lobbyGameId = 0;
                Forget();
                SendRaw(text, id);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook.CloseCurrent: {e}"); }
        }

        // ------------------------------------------------------------------ content

        private static string Build()
        {
            var client = AmongUsClient.Instance;
            if (client == null) return null;
            string code = GameCode.IntToGameName(client.GameId);
            int count = Lobby.AutoStart.PlayerCount();
            int max = 15;
            try { var gom = GameOptionsManager.Instance; if (gom != null && gom.CurrentGameOptions != null) max = gom.CurrentGameOptions.MaxPlayers; } catch (Exception) { }
            string kind = Registration.CompatMode
                ? Lang.T("discord.kind.compat", "役職なし・登録オフ", "no roles (unregistered)", "无职业·未注册")
                : Lang.T("discord.kind.roles", "役職あり", "with roles", "有职业");
            string state = _inGame
                ? Lang.T("discord.state.ingame", "ゲーム中（終わったら入れます）", "in game (join after this round)", "游戏中（本局结束后可加入）")
                : (count >= max ? Lang.T("discord.state.full", "満員", "full", "已满") : Lang.T("discord.state.open", "募集中", "open", "招人中"));
            string line = Lang.T("discord.line", "🔑 部屋コード **{0}** — {1}/{2}人 {3}（{4}）", "🔑 Lobby code **{0}** — {1}/{2} players, {3} ({4})", "🔑 房间码 **{0}** — {1}/{2}人 {3}（{4}）");
            string custom = Options.DiscordText;
            if (!string.IsNullOrEmpty(custom)) line = custom.Replace("\\n", "\n");
            return line.Replace("{0}", code).Replace("{1}", count.ToString()).Replace("{2}", max.ToString()).Replace("{3}", state).Replace("{4}", kind)
                       .Replace("{code}", code).Replace("{count}", count.ToString()).Replace("{max}", max.ToString()).Replace("{state}", state).Replace("{kind}", kind);
        }

        /// <summary>
        /// Something changed: rebuild the line and send it now, or when the throttle window ends (from Tick). All timing
        /// lives in this class: the shared Scheduler is cleared on lobby join / game end, which silently dropped the
        /// first post and pending edits (2026-09-09 live test).
        /// </summary>
        private static void Touch()
        {
            if (!Enabled) return;
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost || client.GameId == 0 || client.NetworkMode != NetworkModes.OnlineGame) return;
            if (_lobbyGameId != client.GameId)
            {
                if (_messageId != null) CloseCurrent("new lobby");
                _lobbyGameId = client.GameId; _messageId = null; _posting = false; _lastContent = null; _retries = 0;
            }
            string content = Build();
            if (content == null || content == _lastContent) return;
            PocketRolesPlugin.Logger.LogInfo($"Discord: change queued (posting={_posting}, id={(_messageId ?? "none")})");
            _pendingContent = content;
            _flushAt = Math.Max(UnityEngine.Time.realtimeSinceStartup, _lastSentAt + MinInterval);
            if (_posting) return;                    // the POST reply (message id) triggers the flush
            if (_flushAt <= UnityEngine.Time.realtimeSinceStartup) Flush();
        }

        private static float _flushAt = -1f;     // when the pending content may leave (throttle)
        private static float _lobbyAt = -1f;     // OnLobby scheduled from the OnGameJoined patch
        private static float _playersAt = -1f;   // OnPlayersChanged scheduled from the join / leave patches

        internal static void ScheduleLobby(float delay) { _lobbyAt = UnityEngine.Time.realtimeSinceStartup + delay; }
        internal static void SchedulePlayers(float delay) { _playersAt = UnityEngine.Time.realtimeSinceStartup + delay; }

        private static void Flush()
        {
            try
            {
                if (!Enabled || _pendingContent == null || _posting) return;
                string content = _pendingContent;
                _pendingContent = null;
                if (content == _lastContent) return;
                _lastSentAt = UnityEngine.Time.realtimeSinceStartup;
                _lastContent = content;
                PocketRolesPlugin.Logger.LogInfo($"Discord: {(_messageId != null ? "edit" : "post")} → {content.Replace('\n', ' ')}");
                Send(content, edit: _messageId != null, closing: false);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook.Flush: {e}"); }
        }

        // ------------------------------------------------------------------ HTTP (thread pool)

        private static void Send(string content, bool edit, bool closing)
        {
            string url = Options.DiscordWebhookUrl;
            string id = _messageId;
            int gameId = _lobbyGameId;
            if (!edit) _posting = true;
            string body = "{\"content\":\"" + Escape(content) + "\",\"allowed_mentions\":{\"parse\":[\"everyone\"]}}";
            Task.Run(async () =>
            {
                string newId = null; string error = null; float retryAfter = -1f;
                try
                {
                    HttpResponseMessage resp;
                    using (var payload = new StringContent(body, Encoding.UTF8, "application/json"))
                    {
                        if (edit && id != null)
                        {
                            var req = new HttpRequestMessage(new HttpMethod("PATCH"), MessageUrl(url, id)) { Content = payload };
                            resp = await Http.SendAsync(req).ConfigureAwait(false);
                        }
                        else resp = await Http.PostAsync(url + (url.Contains("?") ? "&" : "?") + "wait=true", payload).ConfigureAwait(false);
                    }
                    string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) error = (int)resp.StatusCode + " " + Trim(text);
                    if ((int)resp.StatusCode == 429) { var m = RetryAfter.Match(text); if (m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ra)) retryAfter = (float)ra; }
                    else if (!edit)
                    {
                        var m = MessageId.Match(text); if (!m.Success) m = AnyId.Match(text);
                        if (m.Success) newId = m.Groups[1].Value; else error = "no message id in reply: " + Trim(text);
                    }
                }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
                // back to the main thread: the scheduler (a plain list + Time.time) must not be touched from this thread
                Replies.Enqueue(() =>
                {
                    try
                    {
                        if (closing) return;
                        // a reply for a lobby we already left: the remembered id (if any) is closed by CloseStale later
                        if (gameId != _lobbyGameId) { PocketRolesPlugin.Logger.LogInfo("Discord: reply for a previous lobby ignored"); return; }
                        if (!edit) _posting = false;
                        if (error != null)
                        {
                            PocketRolesPlugin.Logger.LogWarning($"Discord: {(edit ? "edit" : "post")} failed: {error}");
                            // retry the same line (bounded): otherwise the channel keeps a stale count / state for the whole game
                            _lastContent = null;
                            if (_retries < MaxRetries)
                            {
                                _retries++;
                                if (_pendingContent == null) _pendingContent = content;
                                _flushAt = UnityEngine.Time.realtimeSinceStartup + Math.Max(MinInterval, retryAfter > 0f ? retryAfter + 0.5f : MinInterval * _retries);
                            }
                            return;
                        }
                        _retries = 0;
                        if (newId != null) { _messageId = newId; Remember(newId, gameId); PocketRolesPlugin.Logger.LogInfo($"Discord: lobby message posted (id {newId})"); }
                        if (_pendingContent != null) Touch();
                    }
                    catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook reply: {e}"); }
                });
            });
        }

        private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> Replies = new System.Collections.Concurrent.ConcurrentQueue<Action>();

        /// <summary>Main thread (HudManager.Update): runs the HTTP replies queued by the thread pool and the timers.</summary>
        internal static void Tick()
        {
            while (Replies.TryDequeue(out var a))
            {
                try { a(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook.Tick: {e}"); }
            }
            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (_lobbyAt >= 0f && now >= _lobbyAt) { _lobbyAt = -1f; OnLobby(); }
                if (_playersAt >= 0f && now >= _playersAt) { _playersAt = -1f; OnPlayersChanged(); }
                if (_pendingContent != null && !_posting && _flushAt >= 0f && now >= _flushAt) Flush();
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook.Tick timers: {e}"); }
        }

        private static string Trim(string s) { s = (s ?? "").Replace("\n", " "); return s.Length > 160 ? s.Substring(0, 160) + "…" : s; }

        private static int _retries;
        private const int MaxRetries = 5;
        private static readonly Regex RetryAfter = new Regex("\"retry_after\":\\s*([0-9.]+)", RegexOptions.Compiled);

        /// <summary>…/webhooks/id/token[?thread_id=…] → …/webhooks/id/token/messages/{id}[?thread_id=…] (the query must follow the path).</summary>
        private static string MessageUrl(string url, string id)
        {
            string path = url, query = "";
            int q = url.IndexOf('?');
            if (q >= 0) { path = url.Substring(0, q); query = url.Substring(q); }
            return path.TrimEnd('/') + "/messages/" + id + query;
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class DiscordWebhook_OnGameJoinedPatch
    {
        // own timer, not the Scheduler: GameState clears the Scheduler in its OnGameJoined handling (patch order is undefined)
        private static void Postfix() { try { DiscordWebhook.ScheduleLobby(1.5f); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook_OnGameJoinedPatch: {e}"); } }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined))]
    internal static class DiscordWebhook_OnPlayerJoinedPatch
    {
        private static void Postfix(AmongUsClient __instance) { try { if (__instance != null && __instance.AmHost) DiscordWebhook.SchedulePlayers(1f); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook_OnPlayerJoinedPatch: {e}"); } }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
    internal static class DiscordWebhook_OnPlayerLeftPatch
    {
        private static void Postfix(AmongUsClient __instance) { try { if (__instance != null && __instance.AmHost) DiscordWebhook.SchedulePlayers(1f); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook_OnPlayerLeftPatch: {e}"); } }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.StartGame))]
    internal static class DiscordWebhook_StartGamePatch
    {
        private static void Postfix() { try { var c = AmongUsClient.Instance; if (c != null && c.AmHost) DiscordWebhook.OnGameStarted(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook_StartGamePatch: {e}"); } }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    internal static class DiscordWebhook_OnGameEndPatch
    {
        private static void Postfix() { try { var c = AmongUsClient.Instance; if (c != null && c.AmHost) DiscordWebhook.OnGameEnded(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook_OnGameEndPatch: {e}"); } }
    }

    /// <summary>The host leaves (ExitGame): close the Discord line out.</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.ExitGame))]
    internal static class DiscordWebhook_ExitGamePatch
    {
        private static void Prefix() { try { var c = AmongUsClient.Instance; if (c != null && c.AmHost) DiscordWebhook.OnLobbyClosed(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook_ExitGamePatch: {e}"); } }
    }

    /// <summary>The server dropped the host (Hacking / LobbyInactivity / network): the lobby is gone, close the line out.</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    internal static class DiscordWebhook_OnDisconnectedPatch
    {
        private static void Prefix() { try { var c = AmongUsClient.Instance; if (c != null && c.AmHost) DiscordWebhook.OnLobbyClosed(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook_OnDisconnectedPatch: {e}"); } }
    }
}
