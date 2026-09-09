using System;
using System.Collections.Generic;
using System.Text;
using AmongUs.GameOptions;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Net;
using InnerNet;

namespace PocketRoles.Chat
{
    /// <summary>
    /// Player-facing chat output. Every text is split at newlines into messages of at most <see cref="MaxChars"/>
    /// characters, digits are converted to full-width and at most one colour tag survives per message
    /// (official-server chat validation, see TECH-NOTES). Private delivery goes through <see cref="Rpc"/>.
    /// </summary>
    public static class Chat
    {
        public const string Title = "PocketRoles";
        /// <summary>Max characters per chat message (the vanilla free-chat limit, which Innersloth enforces on modded lobbies too).</summary>
        public const int MaxChars = 100;
        /// <summary>
        /// Delay between two consecutive messages to the same client (≤ 2 chat RPCs / s / client; ≤ 1 / s in an
        /// unregistered "safe mode" lobby, see <see cref="Rpc.SafeMode"/>).
        /// </summary>
        public static float ChunkSpacing => Registration.CompatMode ? 1.0f : 0.55f;
        /// <summary>
        /// Characters available for the text of one message: <see cref="MaxChars"/>, minus the "[PocketRoles] " prefix
        /// that compat-mode private chat carries inside the text (<see cref="Rpc.CompatChatPrefix"/>).
        /// </summary>
        public static int MessageChars => Registration.CompatMode ? MaxChars - Rpc.CompatChatPrefix.Length : MaxChars;
        /// <summary>Max chat messages for one welcome (the mandatory notice always fits in the first one).</summary>
        public const int MaxWelcomeMessages = 4;
        /// <summary>
        /// Welcome cap per join in the unregistered compat mode: ONE public line (<see cref="CompatWelcomeLine"/>) — the
        /// welcome is a host broadcast there (no client-addressed chat, findings #21/#28), so every join costs everyone one message.
        /// </summary>
        public const int CompatMaxWelcomeMessages = 1;
        /// <summary>Pacing key of the compat-mode public channel in <see cref="NextSendAt"/> (one host broadcast per <see cref="ChunkSpacing"/>).</summary>
        private const int PublicKey = -1;
        /// <summary>Welcome cap when the current settings are appended (one line per enabled role needs the extra message).</summary>
        public const int MaxWelcomeMessagesWithSettings = 5;
        /// <summary>
        /// Delay between two different clients when the same text goes to everyone privately. Every private chat RPC
        /// leaves from the host's NetId, and vanilla chat rate validation counts per sender: ≤ 2 chat RPCs / s overall.
        /// </summary>
        public const float PlayerSpacing = 0.5f;

        // ------------------------------------------------------------------ public API

        /// <summary>Host screen only (no network).</summary>
        public static void Local(string title, string text)
        {
            try
            {
                foreach (var chunk in Split(text))
                {
                    if (AmongUsClient.Instance != null) Rpc.SendChatTo(Rpc.HostClientId, title, chunk);
                    else AddLocalDirect(title, chunk);
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Chat.Local: {e}");
            }
        }

        /// <summary>
        /// Host-screen line as soon as the local player and the HUD exist (retries once per second for up to 10 s).
        /// Right after a scene load (LobbyBehaviour.Start, OnGameJoined) the local PlayerControl may not exist yet and a
        /// plain <see cref="Local"/> would be dropped silently. The text is built when it is actually shown.
        /// </summary>
        public static void LocalWhenReady(Func<string> text, int tries = 0)
        {
            try
            {
                if (text == null) return;
                var hud = HudManager.Instance;
                if (PlayerControl.LocalPlayer == null || hud == null || hud.Chat == null)
                {
                    if (tries < 10) Scheduler.After(1f, () => LocalWhenReady(text, tries + 1));
                    else PocketRolesPlugin.Logger.LogWarning("Chat.LocalWhenReady: no local player after 10 s, dropped: " + Lang.StripTags(text()));
                    return;
                }
                Local(Title, text());
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Chat.LocalWhenReady: {e}");
            }
        }

        /// <summary>Private message to one player (host → Local). Long text is split and paced.</summary>
        public static void To(byte playerId, string title, string text)
        {
            try
            {
                var pc = Core.Game.Player(playerId);
                if (pc == null) return;
                if (pc.AmOwner)
                {
                    Local(title, text);
                    return;
                }
                if (pc.Data != null && pc.Data.Disconnected) return;
                if (Registration.CompatMode)
                {
                    // Unregistered lobby: no client-addressed chat exists → one public message set, addressed "@name".
                    SendPublicChunks(title, Split(AtName(playerId) + text));
                    return;
                }
                SendChunksTo(Rpc.ClientIdOf(pc), title, Split(text), 0f);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Chat.To({playerId}): {e}");
            }
        }

        /// <summary>
        /// "@name " for a public reply addressed to one player (compat mode), "" when the name is unknown. Rich-text
        /// tags are stripped and long names cut so the prefix stays short inside the 100-character message.
        /// </summary>
        internal static string AtName(byte playerId)
        {
            try
            {
                string name = Lang.StripTags(Core.Game.NameOf(playerId) ?? "").Trim();
                if (name.Length == 0) return "";
                if (name.Length > 12) name = name.Substring(0, 12);
                // Compat mode: '@' is not a vanilla chat character (Rpc.SanitizeForVanillaChat would drop it), so "name: ".
                if (Registration.CompatMode) return name + ": ";
                return "@" + name + " ";
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Chat.AtName({playerId}): {e.Message}");
                return "";
            }
        }

        /// <summary>Message to every client including the host.</summary>
        public static void All(string title, string text)
        {
            try
            {
                if (!Core.Game.IsHostActive)
                {
                    Local(title, text);
                    return;
                }
                var chunks = Split(text);
                if (chunks.Count == 0) return;
                if (Registration.CompatMode)
                {
                    // Unregistered lobby: ONE public host broadcast per chunk (Rpc.SendChatAll adds the host's copy).
                    SendPublicChunks(title, chunks);
                    return;
                }
                foreach (var chunk in chunks) Local(title, chunk);
                // One client after the other: Rpc.SendChatAll would emit one SendChat RPC per queue tick (10 / s from
                // the host's NetId); here at most ~2 chat RPCs per second leave the host.
                int j = 0;
                foreach (int clientId in Rpc.AllClientIds(false))
                {
                    float start = ChunkSpacing * chunks.Count * j;
                    SendChunksTo(clientId, title, chunks, start);
                    j++;
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Chat.All: {e}");
            }
        }

        /// <summary>Message to every client including the host, built per recipient in that player's language.</summary>
        public static void All(string title, Func<string> builder)
        {
            try
            {
                if (builder == null) return;
                if (!Core.Game.IsHostActive)
                {
                    Local(title, builder());
                    return;
                }
                if (Registration.CompatMode)
                {
                    // Unregistered lobby: no per-recipient copies — one public set in the lobby's language.
                    List<string> pub;
                    using (Lang.Scope(Lang.Default)) pub = Split(builder());
                    SendPublicChunks(title, pub);
                    return;
                }
                foreach (var chunk in Split(builder())) Local(title, chunk);
                float start = 0f;
                foreach (var pc in Core.Game.AllPlayers())
                {
                    if (pc.AmOwner || pc.Data == null || pc.Data.Disconnected) continue;
                    int clientId = Rpc.ClientIdOf(pc);
                    if (clientId < 0) continue;
                    List<string> chunks;
                    using (Lang.Scope(Lang.PlayerLang(pc.PlayerId))) chunks = Split(builder());
                    if (chunks.Count == 0) continue;
                    SendChunksTo(clientId, title, chunks, start);
                    start += ChunkSpacing * chunks.Count;
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Chat.All(builder): {e}");
            }
        }

        /// <summary>"&lt;role&gt; — description" to that player (in that player's language). Vanilla-role players get a short hint at game start only.</summary>
        public static void SendRoleInfo(byte playerId, bool meeting)
        {
            try
            {
                string text;
                using (Lang.Scope(Lang.PlayerLang(playerId))) text = RoleInfoText(playerId, meeting);
                if (string.IsNullOrEmpty(text)) return;
                To(playerId, Title, text);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Chat.SendRoleInfo({playerId}): {e}");
            }
        }

        public static void SendRoleInfoToAll(bool meeting)
        {
            try
            {
                if (!Core.Game.IsHostActive) return;
                // Unregistered lobby: no custom roles and no per-player chat (findings #21/#28) — nothing to tell.
                if (Registration.CompatMode) return;
                // Every chunk leaves from the host's NetId: accumulate the delay by chunk count (like All) so two
                // consecutive chat RPCs from the host are always ≥ ChunkSpacing apart, whatever the text length.
                float delay = 0f;
                foreach (var pc in Core.Game.AllPlayers())
                {
                    if (pc.Data == null || pc.Data.Disconnected) continue;
                    byte id = pc.PlayerId;
                    if (pc.AmOwner)
                    {
                        SendRoleInfo(id, meeting);
                        continue;
                    }
                    string text;
                    using (Lang.Scope(Lang.PlayerLang(id))) text = RoleInfoText(id, meeting);
                    if (string.IsNullOrEmpty(text)) continue;
                    var chunks = Split(text);
                    if (chunks.Count == 0) continue;
                    float d = delay;
                    Scheduler.After(d, () =>
                    {
                        var p = Core.Game.Player(id);
                        if (p == null || p.Data == null || p.Data.Disconnected) return;
                        SendChunksTo(Rpc.ClientIdOf(p), Title, chunks, 0f);
                    });
                    delay += ChunkSpacing * chunks.Count;
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Chat.SendRoleInfoToAll: {e}");
            }
        }

        /// <summary>Mod notice + enabled roles + help hint, sent privately to a client that joined the lobby.</summary>
        public static void Welcome(int clientId)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !Core.Game.IsHostActive) return;
                if (client.GameState != InnerNetClient.GameStates.Joined) return;
                if (Rpc.IsLocal(clientId)) return;
                bool present = false;
                foreach (int id in Rpc.AllClientIds(false)) if (id == clientId) { present = true; break; }
                if (!present) return;

                var chunks = BuildWelcomeChunks(clientId);
                // Compat mode: the welcome is one public host broadcast (no client-addressed chat, findings #21/#28).
                if (Registration.CompatMode) SendPublicChunks(Title, chunks);
                else SendChunksTo(clientId, Title, chunks, 0f);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Chat.Welcome({clientId}): {e}");
            }
        }

        /// <summary>
        /// The whole welcome of the unregistered (便利ホスト) lobby: one public line in the lobby's language telling the
        /// joiner there are no roles here and where the role lobby is announced (≤ 86 chars + "[PocketRoles] " = 100).
        /// </summary>
        internal static string CompatWelcomeLine()
        {
            return Lang.T("compat.welcome",
                "ようこそ！この部屋は便利ホスト（役職なし）。役職ありの部屋は案内に従ってください",
                "Welcome! This is a helper-host lobby (no roles). For the lobby with roles, follow the guide.",
                "欢迎！本房间是便利房（无职业）。想玩职业请按引导进入职业房。");
        }

        /// <summary>
        /// The messages one join sends (same construction for <see cref="Welcome"/> and <see cref="WelcomeChunkCount"/>):
        /// the welcome in the player's language (or all three with WelcomeAllLanguages) plus the VIP line. Compat mode
        /// (unregistered lobby: public host broadcasts only, 1 message / s): the single <see cref="CompatWelcomeLine"/>
        /// in the lobby's language, capped at <see cref="CompatMaxWelcomeMessages"/> (no roles list, no VIP line).
        /// </summary>
        private static List<string> BuildWelcomeChunks(int clientId)
        {
            byte playerId = PlayerIdOfClient(clientId);
            string playerLang = Lang.PlayerLang(playerId);
            var chunks = new List<string>();
            bool compat = Registration.CompatMode;
            if (compat)
            {
                using (Lang.Scope(Lang.Default)) chunks.AddRange(Split(CompatWelcomeLine()));
                if (chunks.Count > CompatMaxWelcomeMessages)
                    chunks.RemoveRange(CompatMaxWelcomeMessages, chunks.Count - CompatMaxWelcomeMessages);
                return chunks;
            }
            if (Options.WelcomeAllLanguages && !compat)
            {
                // The short welcome in the player's language first, then the two others (one paced stream); the
                // trilingual translation notice follows once instead of once per language copy.
                foreach (string lang in WelcomeLanguageOrder(playerLang))
                {
                    using (Lang.Scope(lang)) chunks.AddRange(WelcomeChunks(true));
                }
                string tr = null;
                using (Lang.Scope(playerLang)) tr = TranslationNotice();
                if (tr != null) chunks.AddRange(Split(tr));
            }
            else
            {
                using (Lang.Scope(playerLang)) chunks.AddRange(WelcomeChunks());
            }
            string vip = null;
            using (Lang.Scope(playerLang)) vip = VipLine(playerId);
            if (vip != null)
            {
                var vipChunks = Split(vip);
                if (!compat || chunks.Count + vipChunks.Count <= CompatMaxWelcomeMessages) chunks.AddRange(vipChunks);
            }
            if (compat && chunks.Count > CompatMaxWelcomeMessages)
                chunks.RemoveRange(CompatMaxWelcomeMessages, chunks.Count - CompatMaxWelcomeMessages);
            return chunks;
        }

        /// <summary>ja, zh, en with the player's language first (WelcomeAllLanguages).</summary>
        private static List<string> WelcomeLanguageOrder(string playerLang)
        {
            var order = new List<string>();
            string first = Lang.Normalize(playerLang);
            order.Add(first);
            foreach (string l in Lang.Supported) if (l != first) order.Add(l);
            return order;
        }

        /// <summary>
        /// Personal greeting for a player listed in VIP.txt (current language), or null. Off when [Permissions] VipMarker
        /// is off, when the player is unknown (255) or when the permissions module says no.
        /// </summary>
        internal static string VipLine(byte playerId)
        {
            try
            {
                if (playerId == 255 || !Options.VipMarker) return null;
                if (!Permissions.IsVip(playerId)) return null;
                string name = Lang.StripTags(Core.Game.NameOf(playerId) ?? "");
                if (name.Length > 20) name = name.Substring(0, 20) + "…";
                return Lang.TF("welcome.vip",
                    "★ VIP {0} さん、ようこそ！いつもありがとうございます。",
                    "★ Welcome back, VIP {0}! Thanks for playing with us.", name);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Chat.VipLine({playerId}): {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Upper bound of chat messages one join may trigger (welcome in one or three languages, the translation
        /// notice, plus the VIP line); fallback for <see cref="WelcomeChunkCount"/> when the welcome text cannot be built.
        /// </summary>
        internal static int MaxWelcomeChunksPerJoin()
        {
            if (Registration.CompatMode) return CompatMaxWelcomeMessages;
            int per = WelcomeCap();
            // WelcomeCap already counts the translation notice once per copy; the one shared notice fits in that.
            return per * (Options.WelcomeAllLanguages ? Lang.Supported.Length : 1) + 1;
        }

        /// <summary>
        /// Chat messages the welcome for <paramref name="clientId"/> will actually take (same construction as
        /// <see cref="Welcome"/>, in the language the player is known to use at this moment), so the join patch spaces
        /// consecutive welcomes by the real count instead of the worst case (up to 22 messages ≈ 12 s per joiner).
        /// </summary>
        internal static int WelcomeChunkCount(int clientId)
        {
            try
            {
                return Math.Max(1, BuildWelcomeChunks(clientId).Count);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Chat.WelcomeChunkCount({clientId}): {e.Message}");
                return MaxWelcomeChunksPerJoin();
            }
        }

        /// <summary>Game.LastSummary → everyone (lobby, after the end screen), each in their own language.</summary>
        public static void SendSummary()
        {
            try
            {
                if (string.IsNullOrEmpty(SummaryText())) return;
                All(Title, () => SummaryText() ?? "");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Chat.SendSummary: {e}");
            }
        }

        // ------------------------------------------------------------------ text builders (also used by Commands)

        /// <summary>Player id that belongs to a client id (255 when unknown, e.g. before its PlayerControl spawned).</summary>
        internal static byte PlayerIdOfClient(int clientId)
        {
            foreach (var pc in Core.Game.AllPlayers())
            {
                if (pc.OwnerId == clientId) return pc.PlayerId;
            }
            return 255;
        }

        /// <summary>
        /// The welcome messages in the current language, capped at <see cref="WelcomeCap"/>. <paramref name="multiLang"/>:
        /// this welcome is one of three language copies (WelcomeAllLanguages) → the trilingual /lang line and the
        /// translation notice are left out (the other languages follow anyway; the notice is added once by the caller).
        /// </summary>
        internal static List<string> WelcomeChunks(bool multiLang = false)
        {
            var chunks = Split(WelcomeText(multiLang));
            int max = WelcomeCap();
            if (chunks.Count > max)
            {
                chunks.RemoveRange(max, chunks.Count - max);
                chunks[max - 1] = Truncated(chunks[max - 1]);
            }
            return chunks;
        }

        /// <summary>
        /// Message cap of one welcome: <see cref="MaxWelcomeMessages"/> (or <see cref="MaxWelcomeMessagesWithSettings"/>)
        /// plus one for the translation notice and one for the trilingual /lang line when they are part of the welcome.
        /// </summary>
        internal static int WelcomeCap()
        {
            if (Registration.CompatMode) return CompatMaxWelcomeMessages;
            int max = Options.WelcomeIncludeSettings ? MaxWelcomeMessagesWithSettings : MaxWelcomeMessages;
            if (TranslationActive) max++;
            if (PlayerCommandsAvailable) max++;
            return max;
        }

        /// <summary>Chat translation is on (Translator module); the welcome then carries a one-line notice.</summary>
        internal static bool TranslationActive
        {
            get
            {
                try { return Translator.Enabled; }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogWarning($"Chat.TranslationActive: {e.Message}");
                    return false;
                }
            }
        }

        /// <summary>One line (≤ 100 chars) telling players that foreign-language chat is translated automatically, or null.</summary>
        internal static string TranslationNotice()
        {
            if (!TranslationActive) return null;
            return Lang.T("welcome.translate",
                "翻訳あり: 外国語で書いても自動で翻訳されます / Auto-translation is on / 自动翻译已开启",
                "Auto-translation is on: write in your own language / 翻訳あり / 自动翻译已开启",
                "自动翻译已开启：用你的语言写即可 / Auto-translation is on / 翻訳あり");
        }

        /// <summary>
        /// The compact trilingual language-switch line ("English: /cmd lang en ｜ 中文: … ｜ 日本語: …"), or null when
        /// players cannot use commands. The command prefix follows the lobby (private /cmd in a registered lobby).
        /// </summary>
        internal static string LangSwitchLine()
        {
            if (!PlayerCommandsAvailable) return null;
            string p = Registration.ShouldRegister ? "/cmd lang " : "/lang ";
            return Lang.TF("welcome.langline",
                "English: {0}en ｜ 中文: {0}zh ｜ 日本語: {0}ja",
                "English: {0}en ｜ 中文: {0}zh ｜ 日本語: {0}ja", p);
        }

        /// <summary>
        /// Translation notice + /lang line joined by newlines ("" when neither applies). Both are left out of one
        /// language copy of the multi-language welcome (the caller adds the notice once after the three copies).
        /// </summary>
        private static string WelcomeExtras(bool multiLang)
        {
            if (multiLang) return "";
            var lines = new List<string>();
            string tr = TranslationNotice();
            if (tr != null) lines.Add(tr);
            string ls = LangSwitchLine();
            if (ls != null) lines.Add(ls);
            return string.Join("\n", lines);
        }

        /// <summary>
        /// The welcome text in the current language: the mandatory mod notice (line 1: role-mod lobby, nothing to
        /// install, the role shows above your own name), then either the built-in body (the role-help line and, only
        /// when the host set one, the custom rules line) or <see cref="Options.WelcomeText"/> with its placeholders
        /// ({rules}, {roles}, {settings}, {help}, {version}) expanded; the current settings are appended when
        /// <see cref="Options.WelcomeIncludeSettings"/> is on (off by default: /cmd s shows them) and the text does not
        /// place them itself, and a custom rules line (/rules) is appended when the custom text does not place {rules} itself.
        /// </summary>
        internal static string WelcomeText(bool multiLang = false)
        {
            var sb = new StringBuilder();
            sb.Append(Lang.T("welcome.1",
                "役職MOD部屋です。何も入れなくてOK。役職は試合が始まると自分の名前の上に出ます。",
                "Role-mod lobby. Nothing to install. Your role appears above your own name when the game starts.",
                "职业MOD房间。什么都不用装。游戏开始后你的职业会显示在自己名字上方。"));
            string custom = Options.WelcomeText;
            bool includeSettings = Options.WelcomeIncludeSettings;
            string extras = WelcomeExtras(multiLang);
            if (string.IsNullOrWhiteSpace(custom))
            {
                sb.Append('\n').Append(BuiltInWelcomeBody());
                if (extras.Length > 0) sb.Append('\n').Append(extras);
                if (includeSettings) sb.Append('\n').Append(SettingsLines());
                return sb.ToString();
            }
            // "\n" typed in chat / the config file (two characters) means a line break.
            string body = custom.Replace("\r\n", "\n").Replace("\\n", "\n");
            bool placedSettings = body.IndexOf("{settings}", StringComparison.OrdinalIgnoreCase) >= 0;
            bool placedRules = body.IndexOf("{rules}", StringComparison.OrdinalIgnoreCase) >= 0;
            bool placedHelp = body.IndexOf("{help}", StringComparison.OrdinalIgnoreCase) >= 0;
            // Compat mode: the "/cmd … is visible to everyone" notice must reach every joiner even with a custom text.
            if (Registration.CompatMode && !placedHelp && PlayerCommandsAvailable) body = CompatCommandHint() + "\n" + body;
            body = ReplacePlaceholder(body, "{rules}", RulesLine());
            body = ReplacePlaceholder(body, "{roles}", EnabledRoleNames());
            body = ReplacePlaceholder(body, "{settings}", includeSettings ? SettingsLines() : "");
            body = ReplacePlaceholder(body, "{help}", HelpHint());
            body = ReplacePlaceholder(body, "{version}", PocketRolesPlugin.Version);
            if (body.Trim().Length > 0) sb.Append('\n').Append(body);
            // A host who typed /rules expects the line to show even in a custom welcome without the placeholder; the
            // built-in "no special rules" line is only added where the host placed {rules}.
            if (!placedRules && HasCustomRules) sb.Append('\n').Append(RulesLine());
            if (extras.Length > 0) sb.Append('\n').Append(extras);
            if (includeSettings && !placedSettings) sb.Append('\n').Append(SettingsLines());
            return sb.ToString();
        }

        /// <summary>True when [Chat] RulesMode = custom and a rules text exists (/rules &lt;text&gt;).</summary>
        internal static bool HasCustomRules => Options.RulesMode == "custom" && !string.IsNullOrWhiteSpace(Options.RulesText);

        /// <summary>
        /// The rules line of the welcome (the {rules} placeholder): the custom text ("\n" = line break) when
        /// <see cref="HasCustomRules"/>, else the built-in "no special rules" text in the current language.
        /// </summary>
        internal static string RulesLine()
        {
            if (HasCustomRules) return Options.RulesText.Replace("\r\n", "\n").Replace("\\n", "\n").Trim();
            return Lang.T("welcome.rules.none",
                "この部屋に特別なルールはありません（ルールなし）。マナーを守って楽しんでください。",
                "No special rules here. Please be respectful and have fun.",
                "本房间没有特别规则（无规则），请遵守礼仪，玩得开心。");
        }

        /// <summary>
        /// Built-in welcome body (short, for first-time players): the role-help line and, only when the host set one
        /// with /rules, the custom rules line. The enabled roles and the settings are not listed here any more
        /// (/cmd r and /cmd s show them; <see cref="Options.WelcomeIncludeSettings"/> appends the settings again).
        /// </summary>
        private static string BuiltInWelcomeBody()
        {
            var sb = new StringBuilder();
            sb.Append(RoleHelpLine());
            if (HasCustomRules)
            {
                sb.Append('\n');
                sb.Append(RulesLine());
            }
            return sb.ToString();
        }

        /// <summary>
        /// Line 2 of the built-in welcome (≤ 100 chars): how to read a role description in meeting chat (/cmd r &lt;role&gt;),
        /// the role list (/cmd r) and help (/cmd h). When players cannot use commands, or in the unregistered compat
        /// lobby, the matching <see cref="HelpHint"/> text is used instead.
        /// </summary>
        internal static string RoleHelpLine()
        {
            if (!PlayerCommandsAvailable || Registration.CompatMode) return HelpHint();
            return Lang.T("welcome.2",
                "役職の説明: 会議のチャットで /cmd r 役職名（例: /cmd r シェリフ）。役職一覧は /cmd r、困ったら /cmd h",
                "Role help: in meeting chat type /cmd r <role> (e.g. /cmd r sheriff). All roles: /cmd r, help: /cmd h",
                "职业说明：会议聊天中输入 /cmd r 职业名（例 /cmd r sheriff）。全部职业 /cmd r，帮助 /cmd h");
        }

        /// <summary>Non-host players may use chat commands ([Chat] PlayerCommands and AllCommands both on).</summary>
        internal static bool PlayerCommandsAvailable => Options.PlayerCommands && Options.AllCommands;

        /// <summary>One-line help hint (the {help} placeholder): how to reach the commands and change the language.</summary>
        internal static string HelpHint()
        {
            if (!PlayerCommandsAvailable)
                return Lang.T("welcome.2.nocmd", "この部屋ではチャットコマンドは使えません（ホストの設定）。", "Chat commands are disabled in this lobby (host setting).", "本房间已禁用聊天命令（房主设置）。");
            if (Registration.CompatMode) return CompatCommandHint();
            return Registration.ShouldRegister
                ? Lang.T("welcome.2.private", "/cmd h でヘルプ（ホストにだけ届きます）。/lang en|zh|ja で言語変更。", "Type /cmd h for help (only the host sees it). /lang en|zh|ja changes your language.")
                : Lang.T("welcome.2.public", "/h でヘルプ（全員に見えます）。/lang en|zh|ja で言語変更。", "Type /h for help (everyone sees it). /lang en|zh|ja changes your language.");
        }

        /// <summary>Compat mode: the server does not route "/cmd …" privately, so every command (including /cmd) is visible to all.</summary>
        internal static string CompatCommandHint()
        {
            return Lang.T("welcome.2.compat",
                "/h でヘルプ。この部屋では「/cmd …」を含むコマンドは全員に見えます。/lang en|zh|ja で言語変更。",
                "Type /h for help. In this lobby every command, \"/cmd ...\" included, is visible to everyone. /lang en|zh|ja changes your language.",
                "/h 查看帮助。本房间中所有命令（包括 /cmd …）对所有人可见。/lang zh|en|ja 更改语言。");
        }

        /// <summary>Current settings (Options.DescribeLines without colour tags) joined by newlines — the {settings} placeholder.</summary>
        internal static string SettingsLines()
        {
            var lines = new List<string>();
            foreach (var line in Options.DescribeLines())
            {
                if (string.IsNullOrEmpty(line)) continue;
                lines.Add(Lang.StripTags(line));
            }
            return string.Join("\n", lines);
        }

        private static string ReplacePlaceholder(string text, string placeholder, string value)
        {
            return text.Replace(placeholder, value ?? "", StringComparison.OrdinalIgnoreCase);
        }

        internal static string EnabledRoleNames()
        {
            var names = new List<string>();
            foreach (var r in Roles.All)
            {
                int n = Options.Count(r.Id);
                if (n <= 0) continue;
                if (Roles.IsCompatBlocked(r.Id)) continue; // compat mode: Sheriff / Jackal are not assigned
                names.Add(n > 1 ? r.Name + "x" + n : r.Name);
            }
            if (names.Count == 0) return Lang.T("welcome.none", "なし", "none");
            return string.Join(Lang.ListSep, names);
        }

        /// <summary>Role description for one player, or null if nothing should be sent.</summary>
        internal static string RoleInfoText(byte playerId, bool meeting)
        {
            // The Game Master host has no role on purpose: never tell it that it is "a regular Crewmate".
            if (Core.Game.GameMasterActive && Core.Game.IsHost(playerId))
                return meeting ? null : Lang.T("gm.roleinfo", "あなたはゲームマスター（観戦・進行役）です。", "You are the Game Master (spectator / moderator).", "你是游戏主持人（观战·主持）。");
            var role = Core.Game.RoleOf(playerId);
            if (role == CustomRole.None)
            {
                if (meeting) return null;
                if (!Core.Game.InProgress) return null;
                bool imp = Core.Game.IsImpostorTeamKiller(playerId);
                return imp
                    ? Lang.T("roleinfo.vanilla.imp", "あなたは通常のインポスターです。この試合には特殊役職がいます。", "You are a regular Impostor. This game has extra roles.")
                    : Lang.T("roleinfo.vanilla.crew", "あなたは通常のクルーです。この試合には特殊役職がいます。", "You are a regular Crewmate. This game has extra roles.");
            }
            var info = Roles.Info(role);
            string head = info.ColoredName + " [" + Roles.TeamName(info.Team) + "]";
            string sep = Lang.T("roleinfo.sep", " — ", " - ");
            string line = head + sep + info.Desc;
            int limit = MessageChars;
            // Sheriff / Jackal / Arsonist hold the vanilla Impostor role on their own client (intro, kill button), so
            // first-time players take themselves for impostors: one line between the name and the description says
            // what the game shows and what the real role is. Left out of the SafeMode meeting reminder (one message).
            string desync = info.ImpostorDesync ? DesyncNotice(info.Name) : null;
            string result;
            if (line.Length <= limit && desync == null) result = line;
            else if (meeting && Rpc.SafeMode)
            {
                // Unregistered lobby: one chat message per player at a meeting (never a split reminder). The colour
                // tag counts toward the 100-character limit, so measure the raw head, not the stripped one.
                if (line.Length <= limit) return line;
                int room = limit - head.Length - sep.Length - 1;
                return room < 8 ? head : head + sep + info.Desc.Substring(0, Math.Min(info.Desc.Length, room)) + "…";
            }
            else result = head + "\n" + (desync != null ? desync + "\n" : "") + info.Desc;

            // v0.4.1 extras (skipped in the SafeMode meeting branch above: one message only there)
            if (role == CustomRole.Lovers)
            {
                byte partner = Core.Game.PartnerOf(playerId);
                if (partner != 255) result += "\n" + Lang.TF("roleinfo.lovers.partner", "あなたの恋人: {0}", "Your lover: {0}", Core.Game.NameOf(partner));
            }
            if (role == CustomRole.Witch && meeting)
            {
                string spelled = Game.Witch.SpelledNamesFor(playerId);
                if (!string.IsNullOrEmpty(spelled)) result += "\n" + Lang.TF("roleinfo.witch.spelled", "呪い中: {0}", "Cursed: {0}", spelled);
            }
            // ---- v0.5.0 extras (skipped automatically in the SafeMode meeting branch above)
            // the Stuntman always knows how many kills it can still survive (start + every meeting reminder + /cmd n); with NotifyStuntman off this is its only feedback
            if (role == CustomRole.MadStuntman)
                result += "\n" + Lang.TF("roleinfo.madstuntman.lives", "耐えられるキル: 残り {0} 回", "Kills you can still survive: {0}", Game.MadStuntman.Remaining(playerId));
            // worships left (start + every meeting) and who was converted (dead converts stay listed: still Madmates)
            if (role == CustomRole.Worshipper)
            {
                result += "\n" + Lang.TF("roleinfo.worshipper.uses", "崇拝の残り回数: {0}", "Worships left: {0}", Game.Worshipper.Remaining(playerId));
                string converted = Game.Worshipper.ConvertedNamesFor(playerId);
                if (!string.IsNullOrEmpty(converted)) result += "\n" + Lang.TF("roleinfo.worshipper.converted", "崇拝した相手: {0}", "Worshipped: {0}", converted);
            }
            // Jackal Friends learns its Jackal(s) by name (game start and every meeting reminder; alive ones only)
            if (role == CustomRole.JackalFriends)
            {
                string jackals = Game.JackalFriends.JackalNames();
                result += "\n" + (jackals.Length > 0
                    ? Lang.TF("roleinfo.jackalfriends.jackal", "ジャッカル: {0}", "Jackal: {0}", jackals)
                    : Lang.T("roleinfo.jackalfriends.nojackal", "生きているジャッカルがいません。", "No Jackal is alive.", "没有存活的豺狼。"));
            }
            // the Nekomata's description assumes the default voter rule; say so when the lobby drags anyone
            if (role == CustomRole.EvilNekomata && !meeting && !Options.EvilNekomataVotersOnly)
                result += "\n" + Lang.T("roleinfo.evilnekomata.anyone", "※この部屋の設定では、道連れは生存者全員からランダムに選ばれます。", "Note: in this lobby the drag picks any living player, not only your voters.");
            // the Serial Killer's concrete limit (start and every meeting reminder)
            if (role == CustomRole.SerialKiller)
                result += "\n" + Lang.TF("roleinfo.serialkiller.limit", "制限時間: {0:0.#}秒（キルするたびにリセット。会議中は停止）", "Time limit: {0:0.#} s (restarts with every kill, paused during meetings)", Game.SerialKiller.Limit());
            // the samurai cannot see its slash radius on screen → tell it the numbers once at game start
            if (role == CustomRole.Samurai && !meeting)
                result += "\n" + Lang.TF("roleinfo.samurai", "斬撃の範囲: 半径 {0:0.#}、味方のインポスターも斬る: {1}", "Slash radius {0:0.#}; hits fellow Impostors: {1}",
                    Options.SamuraiRange, Options.SamuraiHitTeammates ? Lang.T("cmd.on", "オン", "on") : Lang.T("cmd.off", "オフ", "off"));
            return result;
        }

        /// <summary>
        /// One line (≤ 100 chars with any role name) for the roles whose own client holds the Impostor role
        /// (<see cref="RoleInfo.ImpostorDesync"/>): the game says "Impostor", the real role is <paramref name="roleName"/>.
        /// Plain (uncoloured) name: the message may already carry the coloured head and only one colour tag survives.
        /// </summary>
        internal static string DesyncNotice(string roleName)
        {
            return Lang.TF("roleinfo.desync",
                "※本体の表示（イントロ・キルボタン）は「インポスター」ですが、あなたの本当の役職は{0}です。",
                "Note: the game shows you as Impostor (intro, kill button), but your real role is {0}.",
                roleName ?? "");
        }

        /// <summary>Compact result of the last game (packed lines), or null when nothing is recorded.</summary>
        internal static string SummaryText()
        {
            var summary = Core.Game.LastSummary;
            if (summary == null || summary.Count == 0) return null;
            var lines = new List<string>();
            string winner = Lang.StripTags(Core.Game.LastWinnerText);
            lines.Add(Lang.T("summary.head", "前回の結果", "Last game") + (string.IsNullOrEmpty(winner) ? "" : ": " + winner));

            var entries = new List<string>();
            foreach (var e in summary)
            {
                if (e == null) continue;
                string name = e.Name ?? ("#" + e.Id);
                string roleName = e.Role != CustomRole.None ? Roles.Info(e.Role).Name : VanillaRoleName(e.Vanilla);
                string mark = (e.Winner ? "☆" : "") + (e.Dead ? "×" : "");
                entries.Add(mark + name + ":" + roleName);
            }
            string sep = "  ";
            var cur = new StringBuilder();
            foreach (var en in entries)
            {
                if (cur.Length > 0 && cur.Length + sep.Length + en.Length > MaxChars)
                {
                    lines.Add(cur.ToString());
                    cur.Clear();
                }
                if (cur.Length > 0) cur.Append(sep);
                cur.Append(en);
            }
            if (cur.Length > 0) lines.Add(cur.ToString());
            lines.Add(Lang.T("summary.legend", "☆=勝者 ×=死亡", "☆=winner ×=dead"));
            return string.Join("\n", lines);
        }

        internal static string VanillaRoleName(RoleTypes r)
        {
            switch (r)
            {
                case RoleTypes.Crewmate: case RoleTypes.CrewmateGhost: return Lang.T("vrole.crew", "クルー", "Crewmate");
                case RoleTypes.Impostor: case RoleTypes.ImpostorGhost: return Lang.T("vrole.imp", "インポスター", "Impostor");
                case RoleTypes.Scientist: return Lang.T("vrole.sci", "サイエンティスト", "Scientist");
                case RoleTypes.Engineer: return Lang.T("vrole.eng", "エンジニア", "Engineer");
                case RoleTypes.GuardianAngel: return Lang.T("vrole.ga", "守護天使", "Guardian Angel");
                case RoleTypes.Shapeshifter: return Lang.T("vrole.ss", "シェイプシフター", "Shapeshifter");
                case RoleTypes.Noisemaker: return Lang.T("vrole.nm", "ノイズメーカー", "Noisemaker");
                case RoleTypes.Phantom: return Lang.T("vrole.ph", "ファントム", "Phantom");
                case RoleTypes.Tracker: return Lang.T("vrole.tr", "トラッカー", "Tracker");
                case RoleTypes.Detective: return Lang.T("vrole.det", "探偵", "Detective");
                case RoleTypes.Viper: return Lang.T("vrole.vp", "ヴァイパー", "Viper");
                case RoleTypes.Judge: return Lang.T("vrole.judge", "ジャッジ", "Judge");
                default: return r.ToString();
            }
        }

        // ------------------------------------------------------------------ splitting / sanitizing

        /// <summary>
        /// Splits text at newlines into messages of ≤ MaxChars, converts digits to full-width and keeps at most one
        /// colour tag per message. Never returns null; empty input → empty list.
        /// </summary>
        internal static List<string> Split(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            int limit = MessageChars; // compat mode: room for the "[PocketRoles] " prefix inside the 100-char message
            var lines = new List<string>();
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0) continue;
                while (line.Length > limit)
                {
                    int cut = FindCut(line, limit);
                    lines.Add(line.Substring(0, cut).TrimEnd());
                    line = line.Substring(cut).TrimStart();
                }
                if (line.Length > 0) lines.Add(line);
            }
            var cur = new StringBuilder();
            foreach (var line in lines)
            {
                if (cur.Length > 0 && cur.Length + 1 + line.Length > limit)
                {
                    result.Add(Sanitize(cur.ToString()));
                    cur.Clear();
                }
                if (cur.Length > 0) cur.Append('\n');
                cur.Append(line);
            }
            if (cur.Length > 0) result.Add(Sanitize(cur.ToString()));
            return result;
        }

        /// <summary>Cut position ≤ limit that is not inside a rich-text tag, preferring a space.</summary>
        private static int FindCut(string line, int limit)
        {
            int best = -1;
            bool inTag = false;
            int lastSafe = -1;
            for (int i = 0; i < limit && i < line.Length; i++)
            {
                char c = line[i];
                if (c == '<') inTag = true;
                else if (c == '>') { inTag = false; lastSafe = i + 1; continue; }
                if (inTag) continue;
                lastSafe = i + 1;
                if (c == ' ' || c == '、' || c == '。' || c == ',') best = i + 1;
            }
            if (best >= limit / 2) return best;
            if (lastSafe > 0) return lastSafe;
            return limit;
        }

        private static string Sanitize(string chunk)
        {
            chunk = LimitColorTags(chunk);
            return Lang.FullWidthDigits(chunk);
        }

        /// <summary>
        /// The last kept chunk of a capped reply with a "…" marker, still ≤ <see cref="MessageChars"/>: a chunk that
        /// <see cref="Split"/> filled to the limit is cut back (never inside a rich-text tag; the single colour tag
        /// Sanitize left is closed again when its closing tag fell off) before the marker is appended.
        /// </summary>
        internal static string Truncated(string chunk)
        {
            const string marker = "…";
            const string close = "</color>";
            if (chunk == null) chunk = "";
            int limit = MessageChars;
            if (chunk.Length + marker.Length <= limit) return chunk + marker;
            string head = CutOutsideTags(chunk, limit - marker.Length);
            string tail = marker;
            if (head.IndexOf("<color", StringComparison.OrdinalIgnoreCase) >= 0 && head.IndexOf(close, StringComparison.OrdinalIgnoreCase) < 0)
            {
                head = CutOutsideTags(chunk, limit - marker.Length - close.Length);
                if (head.IndexOf("<color", StringComparison.OrdinalIgnoreCase) >= 0) tail = close + marker;
            }
            return head.TrimEnd() + tail;
        }

        /// <summary>The first <paramref name="count"/> characters, moved back to the '&lt;' when the cut lands inside a tag.</summary>
        private static string CutOutsideTags(string s, int count)
        {
            if (count <= 0) return "";
            if (count >= s.Length) return s;
            int open = s.LastIndexOf('<', count - 1);
            if (open >= 0 && s.IndexOf('>', open) >= count) count = open;
            return s.Substring(0, count);
        }

        /// <summary>Leaves the first &lt;color&gt;…&lt;/color&gt; pair; strips every other colour tag.</summary>
        private static string LimitColorTags(string s)
        {
            int first = s.IndexOf("<color", StringComparison.OrdinalIgnoreCase);
            if (first < 0) return s;
            int second = s.IndexOf("<color", first + 6, StringComparison.OrdinalIgnoreCase);
            if (second < 0) return s;
            var sb = new StringBuilder(s.Length);
            int i = 0;
            int colorCount = 0;
            bool keepOpen = false;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '<')
                {
                    int end = s.IndexOf('>', i);
                    if (end < 0) { sb.Append(s, i, s.Length - i); break; }
                    string tag = s.Substring(i, end - i + 1);
                    string lower = tag.ToLowerInvariant();
                    if (lower.StartsWith("<color"))
                    {
                        colorCount++;
                        if (colorCount == 1) { sb.Append(tag); keepOpen = true; }
                    }
                    else if (lower == "</color>")
                    {
                        if (keepOpen) { sb.Append(tag); keepOpen = false; }
                    }
                    else sb.Append(tag);
                    i = end + 1;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ delivery helpers

        /// <summary>Earliest time (Time.time) the next chat message may leave for a client id, across independent calls.</summary>
        private static readonly Dictionary<int, float> NextSendAt = new Dictionary<int, float>();

        /// <summary>A per-client send was redirected to the public channel in this lobby (compat mode; logged once).</summary>
        private static bool _compatRedirectWarned;

        /// <summary>New lobby: client ids start over.</summary>
        internal static void ResetPacing()
        {
            NextSendAt.Clear();
            _compatRedirectWarned = false;
        }

        /// <summary>
        /// Compat mode delivery: every chunk is ONE public host broadcast (<see cref="Rpc.SendChatAll"/>, which also
        /// shows it on the host's screen), spaced by <see cref="ChunkSpacing"/> across independent calls (welcome,
        /// command replies, translations, notices share the single public channel).
        /// </summary>
        internal static void SendPublicChunks(string title, List<string> chunks, float startDelay = 0f)
        {
            try
            {
                if (chunks == null || chunks.Count == 0) return;
                if (AmongUsClient.Instance == null) { foreach (var c in chunks) AddLocalDirect(title, c); return; }
                float now = UnityEngine.Time.time;
                float start = Math.Max(0f, startDelay);
                if (NextSendAt.TryGetValue(PublicKey, out var next) && next - now > start) start = next - now;
                NextSendAt[PublicKey] = now + start + ChunkSpacing * chunks.Count;
                for (int i = 0; i < chunks.Count; i++)
                {
                    string chunk = chunks[i];
                    float delay = start + ChunkSpacing * i;
                    if (delay <= 0f) Rpc.SendChatAll(title, chunk);
                    else Scheduler.After(delay, () =>
                    {
                        if (AmongUsClient.Instance == null) return;
                        Rpc.SendChatAll(title, chunk);
                    });
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Chat.SendPublicChunks: {e}");
            }
        }

        /// <summary>
        /// Chunks with "@name " in front of the first one (public reply to one player). A first chunk that no longer fits
        /// the message limit is re-split (its tail goes out as a second message without the prefix).
        /// </summary>
        private static List<string> WithAtName(byte playerId, List<string> chunks)
        {
            var result = new List<string>();
            if (chunks == null || chunks.Count == 0) return result;
            string first = AtName(playerId) + chunks[0];
            if (first.Length <= MessageChars) result.Add(first);
            else result.AddRange(Split(first));
            for (int i = 1; i < chunks.Count; i++) result.Add(chunks[i]);
            return result;
        }

        /// <summary>
        /// Sends chunks to one client, spaced by <see cref="ChunkSpacing"/>; the first after <paramref name="startDelay"/>.
        /// Independent calls to the same client (a kill notice and a command reply, a welcome and a Chat.To) are spaced
        /// too, so the per-client chat rate never exceeds one message per <see cref="ChunkSpacing"/>.
        /// </summary>
        internal static void SendChunksTo(int clientId, string title, List<string> chunks, float startDelay)
        {
            if (chunks == null || chunks.Count == 0) return;
            if (Registration.CompatMode && !Rpc.IsLocal(clientId))
            {
                // Unregistered lobby: a client-addressed chat would disconnect the host (findings #21/#28). The known
                // callers use the public helpers themselves; anything else becomes one public set addressed "@name".
                if (!_compatRedirectWarned)
                {
                    _compatRedirectWarned = true;
                    PocketRolesPlugin.Logger.LogWarning($"Chat.SendChunksTo: per-client chat for client {clientId} sent as a public broadcast (compat mode)");
                }
                SendPublicChunks(title, WithAtName(PlayerIdOfClient(clientId), chunks), startDelay);
                return;
            }
            float now = UnityEngine.Time.time;
            float start = Math.Max(0f, startDelay);
            if (!Rpc.IsLocal(clientId))
            {
                if (NextSendAt.TryGetValue(clientId, out var next) && next - now > start) start = next - now;
                NextSendAt[clientId] = now + start + ChunkSpacing * chunks.Count;
            }
            for (int i = 0; i < chunks.Count; i++)
            {
                string chunk = chunks[i];
                float delay = start + ChunkSpacing * i;
                if (delay <= 0f) Rpc.SendChatTo(clientId, title, chunk);
                else Scheduler.After(delay, () =>
                {
                    if (AmongUsClient.Instance == null) return;
                    if (!ClientPresent(clientId)) return; // left between two chunks: never address a gone client id
                    Rpc.SendChatTo(clientId, title, chunk);
                });
            }
        }

        private static bool ClientPresent(int clientId)
        {
            if (Rpc.IsLocal(clientId)) return true;
            foreach (int id in Rpc.AllClientIds(false)) if (id == clientId) return true;
            return false;
        }

        private static void AddLocalDirect(string title, string text)
        {
            var hud = HudManager.Instance;
            if (hud == null || hud.Chat == null || PlayerControl.LocalPlayer == null) return;
            string line = string.IsNullOrEmpty(title) ? text : "<color=#a0a0a0>[" + title + "]</color> " + text;
            hud.Chat.AddChat(PlayerControl.LocalPlayer, line, false);
        }
    }

    // ====================================================================== patches

    /// <summary>Host typing: a message that starts with '/' is a command → handled locally, never sent.</summary>
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
    internal static class Chat_SendChatPatch
    {
        private static bool Prefix(ChatController __instance)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return true;
                if (__instance == null || __instance.freeChatField == null || __instance.freeChatField.textArea == null) return true;
                string text = __instance.freeChatField.textArea.text;
                if (string.IsNullOrEmpty(text)) return true;
                string trimmed = text.Trim();
                var me = PlayerControl.LocalPlayer;
                if (!trimmed.StartsWith("/"))
                {
                    // A dead host (Game Master, or simply killed) typing: vanilla clients hide chat from dead senders
                    // for alive viewers, so open the same temporary "alive" window the mod messages use; the vanilla
                    // RpcSendChat that follows this prefix leaves inside it and the restore runs as usual.
                    if (me != null && me.Data != null && me.Data.IsDead && Core.Game.IsHostActive && Core.Game.InProgress && !Rpc.SafeMode)
                        Rpc.TempReviveHostForChat(() => { });
                    return true;
                }
                if (me == null) return true;
                if (!Commands.Handle(me, trimmed)) return true;
                try { __instance.freeChatField.textArea.Clear(); }
                catch (Exception ce)
                {
                    PocketRolesPlugin.Logger.LogWarning($"Chat_SendChatPatch clear: {ce.Message}");
                    try { __instance.freeChatField.textArea.SetText("", ""); } catch { }
                }
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Chat_SendChatPatch: {e}");
                return true;
            }
        }
    }

    /// <summary>Incoming chat from another player: '/…' is a command for the host (private "/cmd …" or public "/…").</summary>
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat))]
    internal static class Chat_AddChatPatch
    {
        private static bool Prefix(PlayerControl sourcePlayer, string chatText)
        {
            try
            {
                if (!Core.Game.IsHostActive) return true;
                if (sourcePlayer == null || sourcePlayer.AmOwner) return true;
                if (string.IsNullOrEmpty(chatText)) return true;
                string trimmed = chatText.Trim();
                if (!trimmed.StartsWith("/")) return true;
                return !Commands.Handle(sourcePlayer, trimmed);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Chat_AddChatPatch: {e}");
                return true;
            }
        }
    }

    /// <summary>New lobby: per-client pacing state starts over (client ids are per lobby).</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class Chat_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            try
            {
                Chat.ResetPacing();
                Chat_OnPlayerJoinedPatch.ResetPacing();
                Commands.OnLobbyJoined();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Chat_OnGameJoinedPatch: {e}");
            }
        }
    }

    /// <summary>Lobby join → private welcome notice a few seconds later.</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined))]
    internal static class Chat_OnPlayerJoinedPatch
    {
        private const float WelcomeDelay = 3f;
        /// <summary>Earliest time the next welcome may start, so welcomes for players joining together never overlap.</summary>
        private static float _nextWelcomeAt;

        internal static void ResetPacing()
        {
            _nextWelcomeAt = 0f;
        }

        private static void Postfix(AmongUsClient __instance, ClientData data)
        {
            try
            {
                if (!Core.Game.IsHostActive || !Options.WelcomeMessage) return;
                if (__instance == null || data == null) return;
                if (__instance.GameState != InnerNetClient.GameStates.Joined) return;
                int clientId = data.Id;
                if (Rpc.IsLocal(clientId)) return;
                // A banned joiner is kicked by Permissions.CheckJoin: no welcome and no welcome slot for it.
                try { if (Permissions.IsBanned(data.ProductUserId, data.FriendCode)) return; }
                catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"Chat_OnPlayerJoinedPatch: ban check: {e.Message}"); }
                float now = UnityEngine.Time.time;
                float startAt = Math.Max(now + WelcomeDelay, _nextWelcomeAt);
                int chunks = Chat.WelcomeChunkCount(clientId); // the real count, not the worst case
                _nextWelcomeAt = startAt + Chat.ChunkSpacing * chunks + 0.1f;
                Scheduler.After(startAt - now, () => Chat.Welcome(clientId));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Chat_OnPlayerJoinedPatch: {e}");
            }
        }
    }
}
