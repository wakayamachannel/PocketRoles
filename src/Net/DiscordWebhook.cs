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
                if (client == null || !client.AmHost) return;
                _inGame = false;
                if (client.GameId != _lobbyGameId)
                {
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

        internal static void OnPlayersChanged() { try { Touch(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook.OnPlayersChanged: {e}"); } }

        internal static void OnGameStarted() { try { _inGame = true; Touch(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook.OnGameStarted: {e}"); } }

        internal static void OnGameEnded() { try { _inGame = false; Touch(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook.OnGameEnded: {e}"); } }

        /// <summary>The host leaves the lobby (disconnect / quit): the message is closed out (best effort, fire and forget).</summary>
        internal static void OnLobbyClosed()
        {
            try
            {
                if (!Enabled || _messageId == null || _lobbyGameId == 0) return;
                string code = GameCode.IntToGameName(_lobbyGameId);
                string text = Lang.T("discord.closed", "部屋 {0} は閉じました。", "Lobby {0} has closed.", "房间 {0} 已关闭。").Replace("{0}", "`" + code + "`");
                Scheduler.Cancel(Tag);
                _lobbyGameId = 0;
                Send(text, edit: true, closing: true);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook.OnLobbyClosed: {e}"); }
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

        /// <summary>Something changed: rebuild the line and send it now, or after the throttle window.</summary>
        private static void Touch()
        {
            if (!Enabled) return;
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost || client.GameId == 0) return;
            if (_lobbyGameId != client.GameId) { _lobbyGameId = client.GameId; _messageId = null; _posting = false; _lastContent = null; }
            string content = Build();
            if (content == null || content == _lastContent) return;
            _pendingContent = content;
            float wait = _lastSentAt + MinInterval - UnityEngine.Time.realtimeSinceStartup;
            Scheduler.Cancel(Tag);
            if (_posting) return;                    // the POST reply (message id) triggers the flush
            if (wait > 0f) { Scheduler.After(wait, Flush, Tag); return; }
            Flush();
        }

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
                string newId = null; string error = null;
                try
                {
                    HttpResponseMessage resp;
                    using (var payload = new StringContent(body, Encoding.UTF8, "application/json"))
                    {
                        if (edit && id != null)
                        {
                            var req = new HttpRequestMessage(new HttpMethod("PATCH"), url.TrimEnd('/') + "/messages/" + id) { Content = payload };
                            resp = await Http.SendAsync(req).ConfigureAwait(false);
                        }
                        else resp = await Http.PostAsync(url + (url.Contains("?") ? "&" : "?") + "wait=true", payload).ConfigureAwait(false);
                    }
                    string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) error = (int)resp.StatusCode + " " + Trim(text);
                    else if (!edit)
                    {
                        var m = MessageId.Match(text); if (!m.Success) m = AnyId.Match(text);
                        if (m.Success) newId = m.Groups[1].Value; else error = "no message id in reply: " + Trim(text);
                    }
                }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
                // back to the main thread through the scheduler (Unity objects are not touched here anyway)
                Scheduler.After(0f, () =>
                {
                    try
                    {
                        if (!edit) _posting = false;
                        if (closing) return;
                        if (error != null)
                        {
                            PocketRolesPlugin.Logger.LogWarning($"Discord: {(edit ? "edit" : "post")} failed: {error}");
                            if (!edit) _lastContent = null; // retry with the next change
                            return;
                        }
                        if (newId != null && gameId == _lobbyGameId) { _messageId = newId; PocketRolesPlugin.Logger.LogInfo($"Discord: lobby message posted (id {newId})"); }
                        if (_pendingContent != null) Touch();
                    }
                    catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook reply: {e}"); }
                }, Tag + ".reply");
            });
        }

        private static string Trim(string s) { s = (s ?? "").Replace("\n", " "); return s.Length > 160 ? s.Substring(0, 160) + "…" : s; }

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
        private static void Postfix() { try { Scheduler.After(1.5f, DiscordWebhook.OnLobby, "discord.lobby"); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook_OnGameJoinedPatch: {e}"); } }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined))]
    internal static class DiscordWebhook_OnPlayerJoinedPatch
    {
        private static void Postfix(AmongUsClient __instance) { try { if (__instance != null && __instance.AmHost) Scheduler.After(1f, DiscordWebhook.OnPlayersChanged, "discord.players"); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook_OnPlayerJoinedPatch: {e}"); } }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
    internal static class DiscordWebhook_OnPlayerLeftPatch
    {
        private static void Postfix(AmongUsClient __instance) { try { if (__instance != null && __instance.AmHost) Scheduler.After(1f, DiscordWebhook.OnPlayersChanged, "discord.players"); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook_OnPlayerLeftPatch: {e}"); } }
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

    /// <summary>The host leaves (ExitGame / disconnect): close the Discord line out.</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.ExitGame))]
    internal static class DiscordWebhook_ExitGamePatch
    {
        private static void Prefix() { try { var c = AmongUsClient.Instance; if (c != null && c.AmHost) DiscordWebhook.OnLobbyClosed(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"DiscordWebhook_ExitGamePatch: {e}"); } }
    }
}
