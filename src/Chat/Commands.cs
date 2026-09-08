using System;
using System.Collections.Generic;
using System.Text;
using PocketRoles.Core;
using PocketRoles.Game;
using PocketRoles.Lobby;
using PocketRoles.Net;
using InnerNet;
using UnityEngine;

namespace PocketRoles.Chat
{
    /// <summary>
    /// Chat command grammar: "/cmd &lt;command&gt; …" or "/&lt;command&gt; …".
    /// Everyone: h|help [host], n|now|me, r|role|roles [name], s|settings (the settings summary, ≤ 4 messages), l|last, lang, time.
    /// Host only: set &lt;role&gt; &lt;count&gt; [chance], opt &lt;key&gt; &lt;value&gt;, show, reset, reload, mod on|off,
    /// welcome &lt;text&gt;|show|reset|settings on|off, test on|off, assign &lt;name|id&gt; &lt;role&gt;|clear|show, end,
    /// rehost on|off, public on|off|now, start, cancel, autostart on|off|&lt;N&gt;, haison, endmeeting, results, region,
    /// rules &lt;text&gt;|none|show, cos ids|reload|music &lt;mode&gt;, admin add|remove|list|reload, diag [on|off] (v0.4c / v0.4e).
    /// Guide room (v0.4e, host only): code [on|off] (big room-code overlay), announce|guide (copy "役職部屋 CODE" to the
    /// clipboard + the sub-phone guide-room steps), move|migrate [CODE]|cancel (tell an unregistered 便利ホスト lobby where
    /// the role lobby is; with [Guide] AutoRecreateRegistered re-create this lobby as registered after 30 s).
    /// Permissions (v0.4b, Core.Permissions): mod|vip add|remove|list, kick &lt;name&gt;, ban &lt;name&gt;|list|remove &lt;code&gt;,
    /// vset &lt;key&gt; &lt;value&gt;. Admins (Admin.txt + AdminsCanChangeSettings) may use the settings commands
    /// (/set /opt /show /start /cancel /autostart /welcome /rules /kick /ban /vset /vip /mod list); moderators
    /// (Moderator.txt + ModeratorsCanKick) may /kick and /ban. /h shows the caller's level.
    /// Gating (v0.4): [Chat] PlayerCommands off → non-host commands are swallowed with one notice per player;
    /// [Chat] AllCommands off → only /mod works for the host (everything else is swallowed with a notice).
    /// Replies are private (≤ 3 messages per command; a welcome preview ≤ 4; host-local pages may be longer).
    /// </summary>
    public static class Commands
    {
        /// <summary>Max chat messages for one command reply.</summary>
        private const int MaxReplyMessages = 3;
        /// <summary>Minimum seconds between two commands from the same non-host player (anti-spam).</summary>
        private const float PlayerCooldown = 2f;

        private static readonly Dictionary<byte, float> LastCommandAt = new Dictionary<byte, float>();

        /// <summary>Players (ids, per lobby) that already received the "commands are disabled" notice.</summary>
        private static readonly HashSet<byte> GatedNoticeSent = new HashSet<byte>();

        /// <summary>New lobby (Chat_OnGameJoinedPatch): per-lobby player state starts over (player ids are reused).</summary>
        internal static void OnLobbyJoined()
        {
            LastCommandAt.Clear();
            GatedNoticeSent.Clear();
        }

        /// <summary>Returns true when the text was a command and has been handled (the message must not be shown/sent).</summary>
        public static bool Handle(PlayerControl sender, string text)
        {
            if (sender == null || string.IsNullOrEmpty(text)) return false;
            // Every reply is built in the sender's language (/lang); the host's own commands use the lobby default.
            using (Lang.Scope(Lang.PlayerLang(sender.PlayerId)))
            {
                return HandleInScope(sender, text);
            }
        }

        private static bool HandleInScope(PlayerControl sender, string text)
        {
            try
            {
                if (sender == null || string.IsNullOrEmpty(text)) return false;
                string t = text.Trim();
                if (!t.StartsWith("/")) return false;
                bool isHost = sender.AmOwner;
                if (!isHost)
                {
                    var client = AmongUsClient.Instance;
                    if (client == null || !client.AmHost) return false;
                }

                // v0.4b permissions: admins may run the settings commands, moderators may kick / ban.
                PermLevel level = Permissions.LevelOf(sender);
                bool hostCmds = isHost || Permissions.CanUseHostCommands(sender);
                bool canKick = hostCmds || Permissions.CanKick(sender);

                bool explicitCmd = IsExplicitCmd(t);
                string body = StripPrefix(t);
                string[] tokens = body.Split(new[] { ' ', '\t', '　' }, StringSplitOptions.RemoveEmptyEntries);
                // Japanese IME / copied examples: "/set sheriff １ ５０", "/kick #３", "/autostart ８人" (the mod's own
                // replies show every digit full-width). Only number-like tokens are mapped, so a player name or role
                // text containing full-width digits still matches as typed. `body` stays as is for the welcome/rules text.
                for (int i = 0; i < tokens.Length; i++) tokens[i] = HalfWidthNumber(tokens[i]);
                string cmd = tokens.Length > 0 ? tokens[0].ToLowerInvariant() : null;

                // ---- command gating (v0.4 §A3). Decided before any reply so a disabled command never answers.
                if (!Options.AllCommands)
                {
                    // Only /mod (on|off|state) stays available to the host, so the mod can still be switched on/off.
                    if (!isHost) return GatedPlayer(sender, explicitCmd, cmd);
                    if (cmd != "mod")
                    {
                        Reply(sender, Lang.T("cmd.gated.all",
                            "コマンドは無効です（設定「全コマンド」がオフ）。/mod on|off だけ使えます。設定タブや歯車メニューで戻せます。",
                            "Commands are disabled ([Chat] AllCommands is off). Only /mod on|off works; re-enable it in the settings tab or the gear menu."));
                        return true;
                    }
                }
                else if (!isHost && !Options.PlayerCommands)
                {
                    return GatedPlayer(sender, explicitCmd, cmd);
                }

                // Anti-spam: only a reply to a non-host player consumes the cooldown (ordinary "/shrug" chat must not).
                if (tokens.Length == 0)
                {
                    if (isHost || explicitCmd) { ReplyThrottled(sender, isHost, HelpText(isHost, level, hostCmds), HelpMessages); return true; }
                    return false; // a lone "/" from a player is ordinary chat
                }
                string arg1 = tokens.Length > 1 ? tokens[1] : null;
                string arg2 = tokens.Length > 2 ? tokens[2] : null;
                string arg3 = tokens.Length > 3 ? tokens[3] : null;

                switch (cmd)
                {
                    case "h": case "help": case "?": case "ヘルプ":
                        // Two pages: "/h" (general) and "/h host" (host commands, host only).
                        if (arg1 != null && IsHostPage(arg1))
                        {
                            if (!hostCmds) { ReplyThrottled(sender, isHost, Lang.T("cmd.hostonly", "ホスト専用です。", "Host only.")); return true; }
                            Reply(sender, HostHelpText(), HostHelpMessages);
                            return true;
                        }
                        ReplyThrottled(sender, isHost, HelpText(isHost, level, hostCmds), HelpMessages);
                        return true;
                    case "n": case "now": case "me": case "役職":
                        ReplyThrottled(sender, isHost, MyRoleText(sender));
                        return true;
                    case "r": case "role": case "roles":
                        ReplyThrottled(sender, isHost, arg1 == null ? RoleListText() : RoleDescText(JoinArgs(tokens, 1)));
                        return true;
                    case "s": case "settings": case "設定": case "设置":
                        // The settings summary left the welcome (v0.4.1): everyone can read it here instead.
                        ReplyThrottled(sender, isHost, ShowText(), SettingsMessages);
                        return true;
                    case "l": case "last":
                        ReplyThrottled(sender, isHost, Chat.SummaryText() ?? Lang.T("cmd.nolast", "まだ試合の記録がありません。", "No game recorded yet."));
                        return true;
                    case "lang": case "language": case "言語":
                        if (!isHost && !PassCooldown(sender.PlayerId)) return true;
                        if (arg1 != null && arg1.ToLowerInvariant() == "default" && isHost) { Reply(sender, SetDefaultLang(arg2)); return true; }
                        if (arg1 != null && arg1.ToLowerInvariant() == "reload" && isHost) { Lang.Load(); Reply(sender, Lang.T("cmd.lang.reloaded", "言語ファイルを再読み込みしました。", "Language files reloaded.")); return true; }
                        // The reply about the NEW language must be built in that language: SetPlayerLang changes what
                        // Lang.PlayerLang returns, but the current scope was opened before the change.
                        string reply = SetPlayerLang(sender, arg1, isHost);
                        using (Lang.Scope(Lang.PlayerLang(sender.PlayerId))) Reply(sender, reply == null ? LangStateText(sender, isHost) : reply);
                        return true;
                    case "time": case "timer": case "時間":
                        ReplyThrottled(sender, isHost, TimeText(isHost));
                        return true;
                    case "guess": case "g": case "推理":
                        // An alive Assassin is never throttled (the guess itself is limited per meeting); everyone else
                        // only gets the "not the assassin" / "dead" refusal, which is throttled like any other reply.
                        bool assassinAlive = Core.Game.RoleOf(sender.PlayerId) == CustomRole.Assassin && Core.Game.IsAlive(sender.PlayerId);
                        string guessReply = Assassin.Guess(sender, JoinArgs(tokens, 1), explicitCmd);
                        if (isHost || assassinAlive) Reply(sender, guessReply);
                        else ReplyThrottled(sender, isHost, guessReply);
                        return true;
                }

                // ---- host-only commands
                bool hostOnly = IsHostCommand(cmd);
                if (!hostOnly)
                {
                    if (!isHost && !explicitCmd) return false; // unknown "/…" from a player stays ordinary chat
                    ReplyThrottled(sender, isHost, Lang.T("cmd.unknown", "不明なコマンドです。/cmd h でヘルプ", "Unknown command. /cmd h for help"));
                    return true;
                }
                if (!isHost)
                {
                    bool allowed = (hostCmds && IsAdminCommand(cmd, arg1)) || (canKick && IsKickCommand(cmd, arg1));
                    if (!allowed)
                    {
                        ReplyThrottled(sender, isHost, HostOnlyText(level));
                        return true;
                    }
                    // A remote admin's / moderator's replies are private too (Reply) and never throttled; log who did it.
                    PocketRolesPlugin.Logger.LogInfo($"Commands: /{cmd} by {Core.Game.NameOf(sender.PlayerId)} ({level})");
                }

                switch (cmd)
                {
                    // ---- v0.4b permissions
                    case "admin": case "admins": HandlePermList(sender, Permissions.ListKind.Admin, arg1, JoinArgs(tokens, 2)); return true;
                    case "moderator": case "moderators": HandlePermList(sender, Permissions.ListKind.Moderator, arg1, JoinArgs(tokens, 2)); return true;
                    case "vip": case "vips": HandlePermList(sender, Permissions.ListKind.Vip, arg1, JoinArgs(tokens, 2)); return true;
                    case "mod":
                        // "/mod add|remove|list …" edits Moderator.txt; "/mod", "/mod on|off" toggles the mod (unchanged).
                        if (IsListVerb(arg1)) { HandlePermList(sender, Permissions.ListKind.Moderator, arg1, JoinArgs(tokens, 2)); return true; }
                        Reply(sender, ToggleMod(arg1));
                        return true;
                    case "kick": Reply(sender, KickCommand(sender, JoinArgs(tokens, 1), false)); return true;
                    case "ban": HandleBan(sender, arg1, JoinArgs(tokens, 1), JoinArgs(tokens, 2)); return true;
                    case "unban": Reply(sender, UnbanText(JoinArgs(tokens, 1))); return true;
                    case "vset": Reply(sender, VanillaSet(arg1, JoinArgs(tokens, 2))); return true;
                    case "set": Reply(sender, SetRole(arg1, arg2, arg3)); return true;
                    case "opt":
                        // The key list needs 8 messages of 100 chars (host screen or a remote admin).
                        if (arg1 == null || arg2 == null) { Reply(sender, OptUsageText(), OptUsageMessages); return true; }
                        // A remote admin may only change the §L settings (roles, lobby timer / auto-start, welcome / rules
                        // text, vanilla ranges). The mod switch, registration, re-host, GM, hotkeys, cosmetics, permissions,
                        // command gating, translation and the lobby language stay with the host.
                        if (!isHost && !IsAdminOptKey(arg1))
                        {
                            PocketRolesPlugin.Logger.LogInfo($"Commands: /opt {arg1} by {Core.Game.NameOf(sender.PlayerId)} refused (host-only key)");
                            Reply(sender, HostOnlyText(level));
                            return true;
                        }
                        Reply(sender, SetOption(arg1, JoinArgs(tokens, 2)));
                        return true;
                    case "show": Reply(sender, ShowText()); return true;
                    case "reset": Reply(sender, ResetRoles()); return true;
                    case "reload":
                        // The file may contain Enabled=false: re-reading it mid-game would switch every host patch off
                        // while the clients still hold desynced roles / names / options.
                        if (InGame()) { Reply(sender, InGameText()); return true; }
                        Options.Reload();
                        Reply(sender, Lang.T("cmd.reload", "設定ファイルを再読み込みしました。", "Config file reloaded.") + "\n" + ShowText());
                        return true;
                    case "welcome": HandleWelcome(sender, arg1, RestOfLine(body, tokens[0])); return true;
                    case "test": Reply(sender, ToggleTest(arg1)); return true;
                    case "assign": Reply(sender, Assign(tokens)); return true;
                    case "end": Reply(sender, EndGame()); return true;
                    case "rehost": Reply(sender, ToggleRehost(arg1)); return true;
                    case "public": Reply(sender, PublicCommand(arg1)); return true;
                    // ---- v0.4 host tools (lobby timer / start / haison / meeting / region / rules / cosmetics)
                    case "start": Reply(sender, StartNow()); return true;
                    case "cancel": Reply(sender, CancelStart()); return true;
                    case "autostart": Reply(sender, AutoStartCommand(arg1)); return true;
                    case "haison": case "廃村": Reply(sender, HaisonCommand()); return true;
                    case "endmeeting": case "em": Reply(sender, EndMeetingCommand()); return true;
                    case "results": Reply(sender, ResultsCommand()); return true;
                    case "region": Reply(sender, RegionText(), RegionMessages); return true;
                    case "diag": case "diagnostics": case "診断":
                        // "/diag on|off" toggles the verbose trace (Game.Diagnostics.Verbose); "/diag" prints the snapshot.
                        // "/diag dump" writes the black-box recorder to the log; "/diag skipmeeting on|off" isolates the report prefix.
                        if (arg1 != null && arg1.Equals("dump", StringComparison.OrdinalIgnoreCase)) { Diagnostics.DumpBuffer("manual /diag dump"); Reply(sender, "diag: recorded trace written to the log"); return true; }
                        if (arg1 != null && arg1.Equals("skipmeeting", StringComparison.OrdinalIgnoreCase))
                        {
                            bool skip = arg2 == null || !TryParseOnOff(arg2, out var s2) || s2;
                            Meetings.SkipPrefixWork = skip;
                            Reply(sender, "diag: meeting prefix work " + (skip ? "SKIPPED" : "normal"));
                            return true;
                        }
                        if (arg1 != null && TryParseOnOff(arg1, out var verbose)) { Reply(sender, DiagVerbose(verbose)); return true; }
                        Reply(sender, DiagText(), DiagMessages);
                        return true;
                    case "rules": HandleRules(sender, arg1, RestOfLine(body, tokens[0])); return true;
                    case "cos": case "cosmetics": HandleCos(sender, arg1, arg2); return true;
                    // ---- v0.4e guide room
                    case "code": case "コード": Reply(sender, CodeCommand(arg1)); return true;
                    case "announce": case "guide": case "案内": Reply(sender, AnnounceText(), AnnounceMessages); return true;
                    case "move": case "migrate": case "移動": Reply(sender, MoveCommand(arg1)); return true;
                }
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Commands.Handle('{text}'): {e}");
                return false;
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Command words reserved for the host (everything else is an everyone command or ordinary chat).</summary>
        private static bool IsHostCommand(string cmd)
        {
            switch (cmd)
            {
                case "set": case "opt": case "show": case "reset": case "reload": case "mod":
                case "welcome": case "test": case "assign": case "end": case "rehost": case "public":
                case "start": case "cancel": case "autostart": case "haison": case "廃村":
                case "endmeeting": case "em": case "results": case "region": case "rules": case "cos": case "cosmetics":
                case "diag": case "diagnostics": case "診断":
                case "admin": case "admins": case "moderator": case "moderators": case "vip": case "vips":
                case "kick": case "ban": case "unban": case "vset":
                case "code": case "コード": case "announce": case "guide": case "案内": case "move": case "migrate": case "移動":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Command words available to everyone.</summary>
        private static bool IsEveryoneCommand(string cmd)
        {
            switch (cmd)
            {
                case "h": case "help": case "?": case "ヘルプ":
                case "n": case "now": case "me": case "役職":
                case "r": case "role": case "roles":
                case "s": case "settings": case "設定": case "设置":
                case "l": case "last":
                case "lang": case "language": case "言語":
                case "time": case "timer": case "時間":
                case "guess": case "g": case "推理":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Host commands an admin (Admin.txt + AdminsCanChangeSettings) may run: the settings commands of §L plus the
        /// list commands for moderators / VIPs / bans and /vset. Everything that toggles the mod itself, test mode,
        /// re-host, cosmetics or ends games stays with the host.
        /// </summary>
        private static bool IsAdminCommand(string cmd, string arg1)
        {
            switch (cmd)
            {
                case "set": case "opt": case "show": case "start": case "cancel": case "autostart":
                case "welcome": case "rules": case "kick": case "ban": case "unban": case "vset":
                case "vip": case "vips": case "moderator": case "moderators":
                    return true;
                case "mod":
                    return IsListVerb(arg1); // "/mod add|remove|list", never "/mod on|off"
                default:
                    return false;
            }
        }

        /// <summary>
        /// /opt keys a remote admin may change: role counts / chances and role tuning, the lobby timer / auto-start
        /// settings, welcome / rules text and the vanilla range limits. Everything else (enabled/mod, register, kick,
        /// perm.*, chat.allcommands/playercommands/welcomeall, general.*, cos.*, hotkeys*, credits.*, translate.*,
        /// lobby.autorehost/autopublic*/dleks/autoregion, gm, lang) is host-only through Options.TrySet.
        /// </summary>
        private static bool IsAdminOptKey(string key)
        {
            string k = (key ?? "").Trim().ToLowerInvariant();
            if (k.Length == 0) return false;
            switch (k)
            {
                case "welcome": case "roleinfo":
                case "chat.welcometext": case "welcometext": case "chat.welcomesettings": case "welcomesettings":
                case "chat.rulesmode": case "rulesmode": case "rules.mode":
                case "chat.rulestext": case "rulestext": case "rules.text": case "rules":
                case "lobby.autostart": case "autostart":
                case "lobby.autostartplayers": case "autostartplayers": case "autostart.players":
                case "lobby.autostartcountdown": case "autostartcountdown": case "autostart.countdown":
                case "lobby.timermode": case "timermode":
                case "lobby.timerwarnat": case "timerwarnat": case "lobby.warnat":
                case "lobby.extenddelay": case "lobby.extendnoticedelay": case "extenddelay":
                case "vanilla.ranges": case "vanilla.extendedranges": case "vanilla.extended": case "ranges":
                    return true;
            }
            if (k.StartsWith("vanilla.") || k.StartsWith("sheriff.") || k.StartsWith("jackal.") || k.StartsWith("vampire.")
                || k.StartsWith("mayor.") || k.StartsWith("snitch.") || k.StartsWith("lighter.") || k.StartsWith("speedbooster.")
                || k.StartsWith("speed.") || k.StartsWith("sb.") || k.StartsWith("madmate.")
                || k.StartsWith("lovers.") || k.StartsWith("arsonist.") || k.StartsWith("witch.") || k.StartsWith("assassin."))
                return true;
            // <role>.count / <role>.chance
            int dot = k.LastIndexOf('.');
            return dot > 0 && Roles.TryParse(k.Substring(0, dot), out _);
        }

        /// <summary>Commands a moderator (Moderator.txt + ModeratorsCanKick) may run.</summary>
        private static bool IsKickCommand(string cmd, string arg1)
        {
            if (cmd == "kick") return true;
            // "/ban <name>" only: listing / removing bans is a settings command (admin / host).
            return cmd == "ban" && !IsListVerb(arg1);
        }

        /// <summary>add / remove / list / reload (and their aliases) as the first argument of a list command.</summary>
        private static bool IsListVerb(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return false;
            switch (arg.ToLowerInvariant())
            {
                case "add": case "remove": case "rm": case "del": case "delete": case "list": case "ls": case "show":
                case "reload": case "unban": case "追加": case "削除": case "一覧":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>"Host only" plus the caller's level, so a listed player understands what they may do.</summary>
        private static string HostOnlyText(PermLevel level)
        {
            string s = Lang.T("cmd.hostonly", "ホスト専用です。", "Host only.");
            if (level == PermLevel.Admin && !Options.AdminsCanChangeSettings)
                s += " " + Lang.T("cmd.perm.adminoff", "（アドミンの設定変更はオフです）", "(admin settings changes are off)");
            else if (level == PermLevel.Moderator)
                s += " " + (Options.ModeratorsCanKick
                    ? Lang.T("cmd.perm.modhint", "（モデレーターは /kick と /ban が使えます）", "(moderators may use /kick and /ban)")
                    : Lang.T("cmd.perm.modoff", "（モデレーターのキックはオフです）", "(moderator kicks are off)"));
            return s;
        }

        /// <summary>
        /// A non-host player typed a command while player commands are disabled: anything that would have been handled
        /// (the private "/cmd …" channel or a known command word) is swallowed with one notice per player and lobby;
        /// an unknown "/…" stays ordinary chat exactly as when commands are enabled.
        /// </summary>
        private static bool GatedPlayer(PlayerControl sender, bool explicitCmd, string cmd)
        {
            if (!explicitCmd && !(cmd != null && (IsEveryoneCommand(cmd) || IsHostCommand(cmd)))) return false;
            byte id = sender.PlayerId;
            if (GatedNoticeSent.Contains(id)) return true;
            GatedNoticeSent.Add(id);
            Reply(sender, Lang.T("cmd.gated.player",
                "この部屋ではチャットコマンドは使えません（ホストが無効にしています）。",
                "Chat commands are disabled in this lobby (turned off by the host)."));
            return true;
        }

        /// <summary>True for "/cmd" or "/cmd …" (the private command channel).</summary>
        private static bool IsExplicitCmd(string t)
        {
            string s = t.Substring(1).TrimStart();
            if (s.Length < 3 || !s.Substring(0, 3).Equals("cmd", StringComparison.OrdinalIgnoreCase)) return false;
            return s.Length == 3 || s[3] == ' ' || s[3] == '\t' || s[3] == '　';
        }

        /// <summary>"/cmd n …" → "n …", "/n …" → "n …".</summary>
        private static string StripPrefix(string t)
        {
            string s = t.Substring(1).TrimStart();
            if (s.Length >= 3 && s.Substring(0, 3).Equals("cmd", StringComparison.OrdinalIgnoreCase))
            {
                if (s.Length == 3) return "";
                char c = s[3];
                if (c == ' ' || c == '\t' || c == '　') return s.Substring(4).TrimStart();
            }
            return s;
        }

        /// <summary>
        /// Maps the full-width digits and the number punctuation of one token (０-９ ＃ ％ ． －) to ASCII, but only when
        /// the whole token is number-like (digits, '#', '%', '.', '-', a trailing '人'); any other token is returned as is.
        /// </summary>
        private static string HalfWidthNumber(string token)
        {
            if (string.IsNullOrEmpty(token)) return token;
            var sb = new StringBuilder(token.Length);
            foreach (char c in token)
            {
                char m;
                if (c >= '０' && c <= '９') m = (char)('0' + (c - '０'));
                else if (c == '＃') m = '#';
                else if (c == '％') m = '%';
                else if (c == '．') m = '.';
                else if (c == '－') m = '-';
                else m = c;
                bool numberLike = (m >= '0' && m <= '9') || m == '#' || m == '%' || m == '.' || m == '-' || m == '人';
                if (!numberLike) return token;
                sb.Append(m);
            }
            return sb.ToString();
        }

        private static string JoinArgs(string[] tokens, int from)
        {
            var sb = new StringBuilder();
            for (int i = from; i < tokens.Length; i++)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(tokens[i]);
            }
            return sb.ToString();
        }

        private static bool PassCooldown(byte playerId)
        {
            float now = Time.time;
            if (LastCommandAt.TryGetValue(playerId, out var last) && now - last < PlayerCooldown) return false;
            LastCommandAt[playerId] = now;
            return true;
        }

        /// <summary>Reply to a player only when its command cooldown has passed (the host is never throttled).</summary>
        private static void ReplyThrottled(PlayerControl sender, bool isHost, string text)
        {
            ReplyThrottled(sender, isHost, text, MaxReplyMessages);
        }

        /// <summary>Throttled reply with its own message cap (the general help page needs 4 with the translation note).</summary>
        private static void ReplyThrottled(PlayerControl sender, bool isHost, string text, int maxMessages)
        {
            if (!isHost && !PassCooldown(sender.PlayerId)) return; // handled silently (spam)
            Reply(sender, text, maxMessages);
        }

        /// <summary>A modded game is running or on its end screen: switching the mod off now would strand every client.</summary>
        private static bool InGame() => Core.Game.InProgress || Core.Game.Ending;

        private static string InGameText() => Lang.T("cmd.mod.ingame", "試合中は切り替えできません。ロビーで実行してください。", "Cannot toggle during a game. Use it in the lobby.");

        /// <summary>Private reply, capped at <see cref="MaxReplyMessages"/> chat messages.</summary>
        private static void Reply(PlayerControl sender, string text)
        {
            Reply(sender, text, MaxReplyMessages);
        }

        /// <summary>
        /// Private reply capped at <paramref name="maxMessages"/> chat messages (host-local usage pages may exceed the
        /// default). Compat mode (unregistered lobby, no client-addressed chat): a public host broadcast addressed "@name".
        /// </summary>
        private static void Reply(PlayerControl sender, string text, int maxMessages)
        {
            if (sender == null || string.IsNullOrEmpty(text)) return;
            bool compatPublic = !sender.AmOwner && Registration.CompatMode;
            if (compatPublic) text = Chat.AtName(sender.PlayerId) + text; // before the split so the prefix counts
            var chunks = Chat.Split(text);
            if (chunks.Count == 0) return;
            if (maxMessages < 1) maxMessages = 1;
            if (chunks.Count > maxMessages)
            {
                chunks.RemoveRange(maxMessages, chunks.Count - maxMessages);
                chunks[maxMessages - 1] = Chat.Truncated(chunks[maxMessages - 1]);
            }
            if (sender.AmOwner)
            {
                foreach (var c in chunks) Chat.Local(Chat.Title, c);
                return;
            }
            if (sender.Data != null && sender.Data.Disconnected) return;
            if (compatPublic) { Chat.SendPublicChunks(Chat.Title, chunks); return; }
            Chat.SendChunksTo(Rpc.ClientIdOf(sender), Chat.Title, chunks, 0f);
        }

        // ------------------------------------------------------------------ texts

        /// <summary>
        /// Messages allowed for the general help page. ja/zh-CN pack into ≤ 4 chunks, but the English page with the
        /// translation note packs into 5 (help.1 and help.2 each need their own message, help.time cannot share with
        /// help.lang+help.translate), so the cap is 5 to keep the level / "/h host" line from being truncated.
        /// </summary>
        private const int HelpMessages = 5;

        /// <summary>
        /// "/h": the general page. Every line is ≤ 100 chars and the page packs into ≤ 5 chat messages (Chat.Split):
        /// the short EN pointer is only added for non-host players (the host chose the lobby language) so the
        /// "/h host" pointer still fits.
        /// </summary>
        private static string HelpText(bool isHost, PermLevel level, bool hostCmds)
        {
            var sb = new StringBuilder();
            sb.Append(Lang.T("help.1",
                "コマンド: /cmd h ヘルプ, /cmd n 自分の役職, /cmd r [役職名] 役職の説明, /cmd l 前回の結果, /cmd lang ja|zh|en 言語",
                "Commands: /cmd h help, /cmd n my role, /cmd r [role] role info, /cmd l last game, /cmd lang ja|zh|en"));
            sb.Append('\n');
            sb.Append(Lang.T("help.2",
                "「/cmd …」は登録済みの部屋ではホストにだけ届きます。「/n」のように書くと全員に見えます。",
                "\"/cmd ...\" reaches only the host in a registered lobby; a plain \"/n\" is visible to everyone."));
            sb.Append('\n');
            sb.Append(Lang.T("help.time", "/cmd time ロビーの残り時間, /cmd s この部屋の設定", "/cmd time = lobby time left, /cmd s = current settings", "/cmd time 房间剩余时间，/cmd s 本房间的设置"));
            sb.Append('\n');
            sb.Append(Lang.TF("help.lang", "言語: {0}（/lang ja|zh|en で変更）", "Language: {0} (/lang ja|zh|en to change)", Lang.DisplayName(Lang.Current)));
            // v0.4b §K: the translation note shares the /lang line (EN needs 5 messages with it, see HelpMessages).
            if (Chat.TranslationActive)
            {
                sb.Append(' ');
                sb.Append(Lang.T("help.translate", "外国語のチャットは自動翻訳されます。", "Foreign-language chat is translated automatically.", "外语聊天会自动翻译。"));
            }
            sb.Append('\n');
            sb.Append(Lang.TF("help.perm", "あなたの権限: {0}", "Your level: {0}", Permissions.LevelName(level)));
            if (hostCmds)
            {
                sb.Append(' ');
                sb.Append(Lang.T("help.hostpage", "ホスト用コマンドは /h host で表示", "Host commands: /h host"));
            }
            else if (level == PermLevel.Moderator && Options.ModeratorsCanKick)
            {
                sb.Append(' ');
                sb.Append(Lang.T("help.modcmds", "/kick <名前>, /ban <名前> が使えます", "/kick <name>, /ban <name> available"));
            }
            else if (!Lang.IsEn)
            {
                sb.Append('\n');
                sb.Append("EN: h=help, n=my role, r [role]=role info, s=settings, l=last game, lang en=English.");
            }
            return sb.ToString();
        }

        /// <summary>Messages allowed for the host help page (8 lines ≤ 100 chars; host screen or a remote admin).</summary>
        private const int HostHelpMessages = 8;

        /// <summary>
        /// Messages allowed for the settings summary a player asks for with /cmd s (one short line per enabled role
        /// plus the general line pack into 2–3 messages; 4 leaves room for the lobby / host lines).
        /// </summary>
        private const int SettingsMessages = 4;

        /// <summary>"/h host": the host-only command page (8 lines ≤ 100 chars → 8 messages).</summary>
        private static string HostHelpText()
        {
            var sb = new StringBuilder();
            sb.Append(Lang.T("help.host.1",
                "ホスト①: /set <役職> <人数> [確率], /opt <キー> <値>, /show, /reset, /reload, /mod on|off",
                "Host 1: /set <role> <n> [chance], /opt <key> <value>, /show, /reset, /reload, /mod on|off"));
            sb.Append('\n');
            sb.Append(Lang.T("help.host.2",
                "ホスト②: /welcome <文章>|show|reset|settings on|off 挨拶文, /rehost on|off 自動再ホスト, /public on|off|now 公開",
                "Host 2: /welcome <text>|show|reset|settings on|off, /rehost on|off, /public on|off|now"));
            sb.Append('\n');
            sb.Append(Lang.T("help.host.3",
                "テスト: /test on|off, /assign <名前|ID> <役職>|none, /assign clear|show, /end 終了。/lang default ja|zh|en",
                "Test: /test on|off, /assign <name|id> <role>|none, /assign clear|show, /end, /lang default ja|zh|en"));
            sb.Append('\n');
            sb.Append(Lang.T("help.host.4",
                "ロビー: /time 残り時間, /start 今すぐ開始, /cancel 開始取消, /autostart on|off|<人数>, /haison 廃村, /region 地域",
                "Lobby: /time, /start (start now), /cancel, /autostart on|off|<n>, /haison (timer reset), /region"));
            sb.Append('\n');
            sb.Append(Lang.T("help.host.5",
                "会議: /endmeeting 投票終了, /results 結果を短縮。/rules <文章>|none 挨拶のルール行。/cos ids|reload|music <mode>",
                "Meeting: /endmeeting, /results. /rules <text>|none (welcome rules). /cos ids|reload|music <mode>"));
            sb.Append('\n');
            sb.Append(Lang.T("help.host.6",
                "権限: /admin|/mod|/vip add|remove|list <名前>, /kick <名前>, /ban <名前>|list|remove <コード>, /vset <キー> <値>",
                "Perms: /admin|/mod|/vip add|remove|list <name>, /kick <name>, /ban <name>|list|remove <code>, /vset"));
            sb.Append('\n');
            sb.Append(Lang.T("help.host.7",
                "診断: /diag 開始処理・画面の状態をチャットとログに出力（画面が真っ暗な時など）。F7 は 2 回押しで廃村",
                "Diag: /diag prints the start / screen state to chat and the log (e.g. on a black screen). F7 twice = haison",
                "诊断: /diag 将开局与画面状态输出到聊天和日志（例如黑屏时）。按两次 F7 = 废村"));
            sb.Append('\n');
            sb.Append(Lang.T("help.host.8",
                "案内: /code コードの大表示, /announce 案内部屋の手順＋コードをコピー, /move [コード]|cancel 役職部屋へ案内, /diag on|off",
                "Guide: /code (big code overlay), /announce (guide-room steps + copy code), /move [code]|cancel, /diag on|off",
                "引导: /code 大字显示代码, /announce 引导房步骤＋复制代码, /move [代码]|cancel 引导到职业房, /diag on|off"));
            return sb.ToString();
        }

        /// <summary>True when the help argument asks for the host page ("host", "h", "ホスト").</summary>
        private static bool IsHostPage(string arg)
        {
            string a = arg.ToLowerInvariant();
            return a == "host" || a == "h" || a == "2" || a == "ホスト";
        }

        /// <summary>The raw text after the command token (keeps inner spacing, unlike JoinArgs).</summary>
        private static string RestOfLine(string body, string firstToken)
        {
            if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(firstToken)) return "";
            int at = body.IndexOf(firstToken, StringComparison.Ordinal);
            if (at < 0) return "";
            return body.Substring(at + firstToken.Length).Trim(' ', '\t', '　');
        }

        // ------------------------------------------------------------------ /lang

        /// <summary>Current language of the sender (and the lobby default for the host).</summary>
        private static string LangStateText(PlayerControl sender, bool isHost)
        {
            string mine = Lang.DisplayName(Lang.PlayerLang(sender.PlayerId));
            string s = Lang.TF("cmd.lang.state", "あなたの言語: {0}  /lang ja|zh|en で変更", "Your language: {0}  /lang ja|zh|en to change", mine);
            if (isHost) s += "\n" + Lang.TF("cmd.lang.default", "部屋の既定言語: {0}  /lang default ja|zh|en で変更", "Lobby default: {0}  /lang default ja|zh|en to change", Lang.DisplayName(Lang.Default));
            return s;
        }

        /// <summary>Sets the sender's language; null = show the state instead (no argument).</summary>
        private static string SetPlayerLang(PlayerControl sender, string arg, bool isHost)
        {
            if (string.IsNullOrEmpty(arg)) return null;
            string a = arg.ToLowerInvariant();
            if (a == "reset" || a == "default")
            {
                Lang.SetPlayerLang(sender.PlayerId, null);
                return Lang.T("cmd.lang.reset", "言語を部屋の既定に戻しました。", "Language reset to the lobby default.");
            }
            if (!Lang.TryNormalize(a, out var code))
                return Lang.T("cmd.lang.usage", "使い方: /lang ja|zh|en", "Usage: /lang ja|zh|en");
            if (isHost)
            {
                // The host's own messages follow the lobby default, so the host's /lang IS the lobby default. No
                // per-player entry for the host: it would shadow a later change through /opt lang, the settings
                // tab or the config file.
                Options.Language = code;
                Lang.SetPlayerLang(sender.PlayerId, null);
            }
            else
            {
                Lang.SetPlayerLang(sender.PlayerId, code);
            }
            PocketRolesPlugin.Logger.LogInfo($"Commands: lang {code} for {Core.Game.NameOf(sender.PlayerId)}{(isHost ? " (host, lobby default)" : "")}");
            // The confirmation is worded in the NEW language.
            using (Lang.Scope(code))
                return Lang.TF("cmd.lang.set", "言語を {0} に設定しました。", "Language set to {0}.", Lang.DisplayName(code));
        }

        /// <summary>Host: /lang default &lt;lang&gt; sets the lobby default (Options.Language).</summary>
        private static string SetDefaultLang(string arg)
        {
            if (string.IsNullOrEmpty(arg) || !Lang.TryNormalize(arg, out var code))
                return Lang.T("cmd.lang.default.usage", "使い方: /lang default ja|zh|en", "Usage: /lang default ja|zh|en");
            Options.Language = code;
            Lang.SetPlayerLang(PlayerControl.LocalPlayer != null ? PlayerControl.LocalPlayer.PlayerId : (byte)255, null);
            PocketRolesPlugin.Logger.LogInfo($"Commands: lobby default lang {code}");
            using (Lang.Scope(code))
                return Lang.TF("cmd.lang.default.set", "部屋の既定言語を {0} に設定しました（/lang で個別に変更可）。", "Lobby default language set to {0} (players can override with /lang).", Lang.DisplayName(code));
        }

        private static string MyRoleText(PlayerControl sender)
        {
            if (!Options.ModEnabled)
                return Lang.T("cmd.modoff", "MODは現在オフです（バニラの試合）。", "The mod is currently off (vanilla game).");
            // Unregistered (便利ホスト) lobby: no custom roles at all (findings #21/#22).
            if (Registration.CompatMode)
                return Lang.T("compat.norole", "この部屋では役職はありません（便利ホスト）", "This lobby has no roles (helper-host lobby).", "本房间没有职业（便利房）。");
            if (!Core.Game.InProgress)
                return Lang.T("cmd.nogame", "試合が始まってから使えます。", "Available once the game has started.");
            string text = Chat.RoleInfoText(sender.PlayerId, false);
            return text ?? Lang.T("cmd.norole", "あなたは通常の役職です。", "You have a regular role.");
        }

        private static string RoleListText()
        {
            var sb = new StringBuilder();
            sb.Append(Lang.T("cmd.rl.crew", "クルー: ", "Crew: ")).Append(TeamRoleNames(Team.Crew)).Append('\n');
            sb.Append(Lang.T("cmd.rl.imp", "インポスター: ", "Impostor: ")).Append(TeamRoleNames(Team.Impostor)).Append('\n');
            sb.Append(Lang.T("cmd.rl.neu", "第三陣営: ", "Neutral: ")).Append(TeamRoleNames(Team.Neutral)).Append('\n');
            sb.Append(Lang.T("cmd.rl.enabled", "この部屋で有効: ", "Enabled here: ")).Append(Chat.EnabledRoleNames()).Append('\n');
            sb.Append(Lang.T("cmd.rl.hint", "/cmd r <役職名> で説明を表示", "/cmd r <role> shows the description"));
            return sb.ToString();
        }

        private static string TeamRoleNames(Team team)
        {
            var names = new List<string>();
            foreach (var r in Roles.All) if (r.Team == team) names.Add(r.Name);
            return string.Join(Lang.ListSep, names);
        }

        private static string RoleDescText(string name)
        {
            if (!Roles.TryParse(name, out var role) || role == CustomRole.None)
                return Lang.TF("cmd.badrole", "役職が見つかりません: {0}（/cmd r で一覧）", "Unknown role: {0} (/cmd r lists them)", name);
            var info = Roles.Info(role);
            string head = info.ColoredName + " [" + Roles.TeamName(info.Team) + "]";
            string line = head + Lang.T("roleinfo.sep", " — ", " - ") + info.Desc;
            if (line.Length > Chat.MaxChars) line = head + "\n" + info.Desc;
            string extra = RoleOptionText(role);
            return string.IsNullOrEmpty(extra) ? line : line + "\n" + extra;
        }

        private static string RoleOptionText(CustomRole role)
        {
            switch (role)
            {
                case CustomRole.Sheriff:
                    return Options.SheriffCanKillMadmate
                        ? Lang.TF("cmd.ro.sheriff.madmate", "キルCD {0:0.#}秒、マッドメイトを撃てる", "Kill cooldown {0:0.#}s, can shoot Madmate", Options.SheriffKillCooldown)
                        : Lang.TF("cmd.ro.sheriff", "キルCD {0:0.#}秒、マッドメイトは撃てない", "Kill cooldown {0:0.#}s, cannot shoot Madmate", Options.SheriffKillCooldown);
                case CustomRole.Jackal:
                    return Options.JackalCanVent
                        ? Lang.TF("cmd.ro.jackal.vent", "キルCD {0:0.#}秒、ベント可", "Kill cooldown {0:0.#}s, vent on", Options.JackalKillCooldown)
                        : Lang.TF("cmd.ro.jackal", "キルCD {0:0.#}秒、ベント不可", "Kill cooldown {0:0.#}s, vent off", Options.JackalKillCooldown);
                case CustomRole.Vampire: return Lang.TF("cmd.ro.vampire", "噛みつきから {0:0.#}秒後に死亡", "Victim dies {0:0.#}s after the bite", Options.VampireKillDelay);
                case CustomRole.Mayor: return Lang.TF("cmd.ro.mayor", "投票は {0}票分", "Vote counts as {0}", Options.MayorVotes);
                case CustomRole.Snitch: return Lang.TF("cmd.ro.snitch", "残りタスク {0} でキラーに位置が知られる", "Killers see you when {0} tasks are left", Options.SnitchTasksLeftToWarn);
                case CustomRole.Lighter: return Lang.TF("cmd.ro.lighter", "視界 x{0:0.#}", "Vision x{0:0.#}", Options.LighterVision);
                case CustomRole.SpeedBooster: return Lang.TF("cmd.ro.speed", "速度 x{0:0.#}", "Speed x{0:0.#}", Options.SpeedBoosterSpeed);
                case CustomRole.Madmate: return Options.MadmateKnownToImpostors ? Lang.T("cmd.ro.madmate", "インポスターはマッドメイトが誰か分かる", "Impostors know who the Madmate is") : null;
                // v0.4.1
                case CustomRole.Lovers:
                    return Lang.TF("cmd.ro.lovers", "インポスターも恋人になる: {0}、残り3人で勝利: {1}", "Impostor may be a lover: {0}, win as last 3: {1}", OnOff(Options.LoversAllowImpostor), OnOff(Options.LoversWinAsLastThree));
                case CustomRole.Arsonist:
                    return Options.ArsonistCanVent
                        ? Lang.TF("cmd.ro.arsonist.vent", "油CD {0:0.#}秒、ベント可", "Douse cooldown {0:0.#}s, vent on", Options.ArsonistDouseCooldown)
                        : Lang.TF("cmd.ro.arsonist", "油CD {0:0.#}秒、ベント不可", "Douse cooldown {0:0.#}s, vent off", Options.ArsonistDouseCooldown);
                case CustomRole.Witch:
                    return Lang.TF("cmd.ro.witch", "呪いCD {0}、呪われた本人に印: {1}", "Spell cooldown {0}, target sees mark: {1}",
                        Options.WitchSpellCooldown > 0f ? Options.WitchSpellCooldown.ToString("0.#") + "s" : Lang.T("cmd.ro.witch.samecd", "キルと同じ", "same as kill"), OnOff(Options.WitchSpelledSeeMark));
                case CustomRole.Assassin:
                    return Lang.TF("cmd.ro.assassin", "会議ごとに {0} 回推理、初回会議: {1}", "{0} guess(es) per meeting, first meeting: {1}", Options.AssassinGuessesPerMeeting, OnOff(Options.AssassinCanGuessFirstMeeting));
                default: return null;
            }
        }

        private static string ShowText()
        {
            var sb = new StringBuilder();
            foreach (var line in Options.DescribeLines())
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Lang.StripTags(line));
            }
            if (!Options.ModEnabled) sb.Insert(0, Lang.T("cmd.show.off", "[MODオフ] ", "[mod off] "));
            return sb.ToString();
        }

        private static string SetRole(string roleText, string countText, string chanceText)
        {
            string usage = Lang.T("cmd.set.usage", "使い方: /set <役職> <人数> [確率%]  例: /set sheriff 1 50", "Usage: /set <role> <count> [chance%]  e.g. /set sheriff 1 50");
            if (string.IsNullOrEmpty(roleText) || string.IsNullOrEmpty(countText)) return usage;
            if (!Roles.TryParse(roleText, out var role) || role == CustomRole.None)
                return Lang.TF("cmd.badrole", "役職が見つかりません: {0}（/cmd r で一覧）", "Unknown role: {0} (/cmd r lists them)", roleText);
            if (!int.TryParse(countText, out var count)) return usage;
            Options.SetCount(role, count);
            if (!string.IsNullOrEmpty(chanceText))
            {
                if (!int.TryParse(chanceText.TrimEnd('%'), out var chance)) return usage;
                Options.SetChance(role, chance);
            }
            var info = Roles.Info(role);
            string line = info.ColoredName + " x" + Options.Count(role) + " (" + Options.Chance(role) + "%)";
            if (Core.Game.InProgress) line += Lang.T("cmd.set.next", " — 次の試合から適用", " - applies from the next game");
            return line;
        }

        /// <summary>Messages allowed for the /opt key list (8 lines ≤ 100 chars, host-local only).</summary>
        private const int OptUsageMessages = 8;

        /// <summary>/opt without arguments: usage plus every accepted key (8 lines of ≤ 100 chars; v0.3 / v0.4 keys included).</summary>
        private static string OptUsageText()
        {
            return Lang.T("cmd.opt.usage2",
                "使い方: /opt <キー> <値>  キー: <役職>.count|chance, sheriff.cooldown|killmadmate, jackal.cooldown|vent\nvampire.delay, mayor.votes, snitch.tasks, lighter.vision, speedbooster.speed, madmate.known, lang\nwelcome, roleinfo, register, kick, lobby.autorehost|autopublic|autopublicdelay|rehostmax|maxping\nchat.welcometext|welcomesettings, general.ignoreversion, credits.author|url|show\nlobby.autostart|autostartplayers|autostartcountdown|timermode|timerwarnat|extenddelay\nlobby.autoregion|dleks, gm, hotkeys, hotkeys.haison|endmeeting|cancelstart\nchat.playercommands|allcommands|rulesmode|rulestext, cos.enabled|music|musicfile|musicvolume\ncos.lobbypaint|dropship|menubg|cursor, guide.overlay|code|autoreg",
                "/opt <key> <value>  Keys: <role>.count|chance, sheriff.cooldown|killmadmate, jackal.cooldown|vent\nvampire.delay, mayor.votes, snitch.tasks, lighter.vision, speedbooster.speed, madmate.known, lang\nwelcome, roleinfo, register, kick, lobby.autorehost|autopublic|autopublicdelay|rehostmax|maxping\nchat.welcometext|welcomesettings, general.ignoreversion, credits.author|url|show\nlobby.autostart|autostartplayers|autostartcountdown|timermode|timerwarnat|extenddelay\nlobby.autoregion|dleks, gm, hotkeys, hotkeys.haison|endmeeting|cancelstart\nchat.playercommands|allcommands|rulesmode|rulestext, cos.enabled|music|musicfile|musicvolume\ncos.lobbypaint|dropship|menubg|cursor, guide.overlay|code|autoreg");
        }

        private static string SetOption(string key, string value)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) return OptUsageText();
            string k = key.Trim().ToLowerInvariant();
            if ((k == "enabled" || k == "mod") && InGame()) return InGameText(); // same guard as /mod on|off
            bool ok = Options.TrySet(key, value, out var message);
            if (ok && (k == "register" || k == "modded" || k == "+25"))
                message += Lang.T("cmd.opt.register", "（次に部屋を作った時から適用。オフはInnersloth のMODポリシー違反です）", " (applies to the next lobby you create; off violates Innersloth's mod policy)");
            return (ok ? "" : Lang.T("cmd.opt.fail", "設定できません: ", "Failed: ")) + message;
        }

        private static string ResetRoles()
        {
            foreach (var r in Roles.All)
            {
                int def = (r.Id == CustomRole.Sheriff || r.Id == CustomRole.Jester || r.Id == CustomRole.Madmate) ? 1 : 0;
                Options.SetCount(r.Id, def);
                Options.SetChance(r.Id, 100);
            }
            return Lang.T("cmd.reset", "役職設定を初期値に戻しました。", "Role settings reset to defaults.") + "\n" + ShowText();
        }

        private static string ToggleMod(string arg)
        {
            if (string.IsNullOrEmpty(arg))
                return Lang.TF("cmd.mod.state", "MOD: {0}  /mod on|off で切替", "Mod: {0}  /mod on|off to toggle",
                    Options.ModEnabled ? Lang.T("cmd.on", "オン", "on") : Lang.T("cmd.off", "オフ", "off"));
            string a = arg.ToLowerInvariant();
            bool on;
            if (a == "on" || a == "1" || a == "true" || a == "オン") on = true;
            else if (a == "off" || a == "0" || a == "false" || a == "オフ") on = false;
            else return Lang.T("cmd.mod.usage", "使い方: /mod on|off", "Usage: /mod on|off");
            if (InGame()) return InGameText(); // includes the end screen (names / options are restored in the lobby)
            Options.ModEnabled = on;
            PocketRolesPlugin.Logger.LogInfo($"Commands: mod {(on ? "on" : "off")}");
            return on
                ? Lang.T("cmd.mod.on", "MODをオンにしました。次の試合から役職が配られます。", "Mod enabled. Roles are assigned from the next game.")
                : Lang.T("cmd.mod.off", "MODをオフにしました。次の試合はバニラです。", "Mod disabled. The next game is vanilla.");
        }

        // ------------------------------------------------------------------ on / off helpers

        private static bool TryParseOnOff(string arg, out bool on)
        {
            on = false;
            if (string.IsNullOrEmpty(arg)) return false;
            string a = arg.ToLowerInvariant();
            if (a == "on" || a == "1" || a == "true" || a == "yes" || a == "オン" || a == "开") { on = true; return true; }
            if (a == "off" || a == "0" || a == "false" || a == "no" || a == "オフ" || a == "关") { on = false; return true; }
            return false;
        }

        private static string OnOff(bool on) => on ? Lang.T("cmd.on", "オン", "on") : Lang.T("cmd.off", "オフ", "off");

        // ------------------------------------------------------------------ /welcome

        /// <summary>Longest custom welcome text accepted (the message is capped at 4 chat messages anyway).</summary>
        private const int MaxWelcomeTextChars = 320;

        /// <summary>/welcome (state), /welcome show, /welcome reset, /welcome settings on|off, /welcome &lt;text…&gt;.</summary>
        private static void HandleWelcome(PlayerControl sender, string arg1, string rest)
        {
            string a = arg1 == null ? "" : arg1.ToLowerInvariant();
            if (a.Length == 0)
            {
                Reply(sender, WelcomeStateText());
                return;
            }
            if (a == "show" || a == "preview")
            {
                // Exactly what a joining player receives (≤ Chat.MaxWelcomeMessages messages), on the caller's screen only.
                LocalLines(sender, Lang.T("cmd.welcome.preview", "【挨拶文プレビュー】", "[Welcome preview]"), Chat.WelcomeChunks());
                return;
            }
            if (a == "reset" || a == "default" || a == "clear")
            {
                Options.TrySet("chat.welcometext", "", out _);
                PocketRolesPlugin.Logger.LogInfo("Commands: welcome text reset");
                Reply(sender, Lang.T("cmd.welcome.reset", "挨拶文を標準に戻しました。", "Welcome text reset to the built-in one."));
                return;
            }
            if (a == "settings" || a == "setting")
            {
                string value = RestOfLine(rest, arg1);
                if (!TryParseOnOff(value, out var on))
                {
                    Reply(sender, Lang.T("cmd.welcome.settings.usage", "使い方: /welcome settings on|off（挨拶に現在の設定を付けるか）", "Usage: /welcome settings on|off (append the current settings to the welcome)"));
                    return;
                }
                Options.TrySet("chat.welcomesettings", on ? "on" : "off", out _);
                Reply(sender, on
                    ? Lang.T("cmd.welcome.settings.on", "挨拶に現在の設定を付けます。", "The welcome now includes the current settings.")
                    : Lang.T("cmd.welcome.settings.off", "挨拶に設定を付けません。", "The welcome no longer includes the settings."));
                return;
            }
            // Everything after "welcome" is the new text ("\n" = line break, placeholders {roles} {settings} {help} {version}).
            string text = rest ?? "";
            if (text.Length > MaxWelcomeTextChars)
            {
                Reply(sender, Lang.TF("cmd.welcome.toolong", "長すぎます（最大 {0} 文字）。", "Too long (max {0} characters).", MaxWelcomeTextChars));
                return;
            }
            if (!Options.TrySet("chat.welcometext", text, out var message))
            {
                Reply(sender, Lang.T("cmd.opt.fail", "設定できません: ", "Failed: ") + message);
                return;
            }
            PocketRolesPlugin.Logger.LogInfo($"Commands: welcome text set ({text.Length} chars)");
            LocalLines(sender, Lang.T("cmd.welcome.set", "挨拶文を設定しました。プレビュー:", "Welcome text set. Preview:"), Chat.WelcomeChunks());
        }

        /// <summary>
        /// Several lines for the caller only: the host gets them as local (unsent) chat lines; a remote admin gets them
        /// as one private reply (paced by Chat.SendChunksTo).
        /// </summary>
        private static void LocalLines(PlayerControl sender, string head, List<string> lines)
        {
            if (sender == null) return;
            if (sender.AmOwner)
            {
                if (!string.IsNullOrEmpty(head)) Chat.Local(Chat.Title, head);
                if (lines != null) foreach (var c in lines) Chat.Local(Chat.Title, c);
                return;
            }
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(head)) sb.Append(head);
            if (lines != null) foreach (var c in lines) { if (sb.Length > 0) sb.Append('\n'); sb.Append(c); }
            Reply(sender, sb.ToString(), Chat.MaxWelcomeMessagesWithSettings + 1);
        }

        private static string WelcomeStateText()
        {
            string custom = Options.WelcomeText;
            string which = string.IsNullOrWhiteSpace(custom)
                ? Lang.T("cmd.welcome.builtin", "標準", "built-in")
                : Lang.TF("cmd.welcome.custom", "カスタム（{0}文字）", "custom ({0} chars)", custom.Length);
            var sb = new StringBuilder();
            sb.Append(Lang.TF("cmd.welcome.state", "挨拶文: {0}  設定の表示: {1}  挨拶自体: {2}", "Welcome text: {0}  settings line: {1}  welcome: {2}",
                which, OnOff(Options.WelcomeIncludeSettings), OnOff(Options.WelcomeMessage)));
            sb.Append('\n');
            // Lang.T (not TF): the braces below are literal placeholders, not format items.
            sb.Append(Lang.T("cmd.welcome.usage",
                "使い方: /welcome <文章>, /welcome show|reset|settings on|off\n文章内: \\n で改行、{rules} {roles} {settings} {help} {version} は置き換えられます",
                "Usage: /welcome <text>, /welcome show|reset|settings on|off\nIn the text: \\n = line break; {rules} {roles} {settings} {help} {version} are replaced"));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ /test, /assign, /end

        private static string ToggleTest(string arg)
        {
            if (string.IsNullOrEmpty(arg))
            {
                string s = Lang.TF("cmd.test.state", "テストモード: {0}  /test on|off で切替", "Test mode: {0}  /test on|off to toggle", OnOff(TestMode.Enabled));
                string assigns = TestMode.DescribeAssignments();
                return string.IsNullOrEmpty(assigns) ? s : s + "\n" + assigns;
            }
            if (!TryParseOnOff(arg, out var on)) return Lang.T("cmd.test.usage", "使い方: /test on|off", "Usage: /test on|off");
            if (InGame()) return InGameText(); // win checks / MinPlayers must not flip while a modded game runs
            if (on == TestMode.Enabled)
                return Lang.TF("cmd.test.state", "テストモード: {0}  /test on|off で切替", "Test mode: {0}  /test on|off to toggle", OnOff(on));
            TestMode.Set(on); // announces the change to everyone (Chat.All) and adjusts MinPlayers
            PocketRolesPlugin.Logger.LogInfo($"Commands: test mode {(on ? "on" : "off")}");
            return on
                ? Lang.T("cmd.test.hint", "1人でも開始できます。勝敗判定は止まります: /end で終了、/assign <名前|ID> <役職> で役職指定。", "You can start alone; win checks are off: /end ends the game, /assign <name|id> <role> forces a role.")
                : Lang.T("cmd.test.offhint", "通常モードに戻りました。役職指定もすべて解除しました。", "Back to normal mode; all forced roles were cleared.");
        }

        private static string Assign(string[] tokens)
        {
            string usage = Lang.T("cmd.assign.usage",
                "使い方: /assign <名前|ID> <役職>  例: /assign Taro sheriff  /assign <名前> none 個別解除  /assign clear 全解除  /assign show 一覧",
                "Usage: /assign <name|id> <role>  e.g. /assign Taro sheriff  /assign <name> none clears one  /assign clear  /assign show");
            if (tokens.Length < 2) return usage + "\n" + AssignmentsText();
            string a = tokens[1].ToLowerInvariant();
            if (tokens.Length == 2)
            {
                if (a == "clear" || a == "reset" || a == "none")
                {
                    TestMode.ClearAssignments();
                    PocketRolesPlugin.Logger.LogInfo("Commands: assignments cleared");
                    return Lang.T("cmd.assign.cleared", "役職指定をすべて解除しました。", "All forced roles cleared.");
                }
                if (a == "show" || a == "list") return AssignmentsText();
                return usage;
            }
            // Last token = role, everything in between = player name (names may contain spaces) or player id.
            string roleText = tokens[tokens.Length - 1];
            var who = new StringBuilder();
            for (int i = 1; i < tokens.Length - 1; i++)
            {
                if (who.Length > 0) who.Append(' ');
                who.Append(tokens[i]);
            }
            // "/assign <name> none|clear" removes that player's forced role (TestMode.TryAssign with CustomRole.None).
            bool clear = roleText.Equals("none", StringComparison.OrdinalIgnoreCase) || roleText.Equals("clear", StringComparison.OrdinalIgnoreCase)
                || roleText == "なし" || roleText == "解除" || roleText == "无";
            CustomRole role = CustomRole.None;
            if (!clear && (!Roles.TryParse(roleText, out role) || role == CustomRole.None))
                return Lang.TF("cmd.badrole", "役職が見つかりません: {0}（/cmd r で一覧）", "Unknown role: {0} (/cmd r lists them)", roleText);
            bool ok = TestMode.TryAssign(who.ToString(), clear ? CustomRole.None : role, out var message);
            string s = message ?? "";
            if (ok && !clear)
            {
                PocketRolesPlugin.Logger.LogInfo($"Commands: assign '{who}' -> {role}");
                if (Core.Game.InProgress) s += Lang.T("cmd.set.next", " — 次の試合から適用", " - applies from the next game");
                if (!TestMode.Enabled) s += "\n" + Lang.T("cmd.assign.testoff", "テストモードはオフです（1人で試すには /test on）。", "Test mode is off (/test on to start alone).");
            }
            else if (ok) PocketRolesPlugin.Logger.LogInfo($"Commands: assign '{who}' cleared");
            return s;
        }

        private static string AssignmentsText()
        {
            string s = TestMode.DescribeAssignments();
            return string.IsNullOrEmpty(s) ? Lang.T("cmd.assign.none", "役職指定はありません。", "No forced roles.") : s;
        }

        private static string EndGame()
        {
            if (Core.Game.Ending) return Lang.T("cmd.end.ending", "すでに終了処理中です。", "The game is already ending.");
            if (!Core.Game.InProgress) return Lang.T("cmd.end.nogame", "試合中ではありません。", "No game in progress.");
            // EndGameManually is a no-op without an active modded game: do not claim the game is ending.
            if (!Core.Game.IsHostActive) return Lang.T("cmd.modoff", "MODは現在オフです（バニラの試合）。", "The mod is currently off (vanilla game).");
            PocketRolesPlugin.Logger.LogInfo("Commands: /end");
            TestMode.EndGameManually();
            return Lang.T("cmd.end.done", "試合を終了します（クルー勝利扱い）。", "Ending the game (counted as a crew win).");
        }

        // ------------------------------------------------------------------ /rehost, /public

        private static string ToggleRehost(string arg)
        {
            // While the high-ping confirmation is on screen, "/rehost yes|no" (はい/いいえ) answers it instead of
            // toggling auto re-host (the dialog is host-only, so the command is a keyboard alternative to the buttons).
            if (!string.IsNullOrEmpty(arg) && Lobby.RehostPrompt.IsOpen)
            {
                string a = arg.ToLowerInvariant();
                if (a == "yes" || a == "y" || a == "はい" || a == "是")
                {
                    Lobby.RehostPrompt.Answer(true);
                    return Lang.T("cmd.rehost.prompt.yes", "確認に「はい」と答えました。", "Answered Yes to the re-create question.", "已回答“是”。");
                }
                if (a == "no" || a == "n" || a == "いいえ" || a == "否")
                {
                    Lobby.RehostPrompt.Answer(false);
                    return Lang.T("cmd.rehost.prompt.no", "確認に「いいえ」と答えました。", "Answered No to the re-create question.", "已回答“否”。");
                }
            }
            if (string.IsNullOrEmpty(arg))
                return Lang.TF("cmd.rehost.state", "自動再ホスト: {0}  /rehost on|off で切替（切断されたら部屋を作り直します）", "Auto re-host: {0}  /rehost on|off to toggle (recreates the lobby after a disconnect)", OnOff(Options.AutoRehost));
            if (!TryParseOnOff(arg, out var on)) return Lang.T("cmd.rehost.usage", "使い方: /rehost on|off", "Usage: /rehost on|off");
            if (!Options.TrySet("lobby.autorehost", on ? "on" : "off", out var message))
                return Lang.T("cmd.opt.fail", "設定できません: ", "Failed: ") + message;
            PocketRolesPlugin.Logger.LogInfo($"Commands: auto re-host {(on ? "on" : "off")}");
            return on
                ? Lang.T("cmd.rehost.on", "自動再ホストをオンにしました。サーバーから切断されたら自動で部屋を作り直します。", "Auto re-host on: the lobby is recreated automatically after a server disconnect.")
                : Lang.T("cmd.rehost.off", "自動再ホストをオフにしました。", "Auto re-host off.");
        }

        private static string PublicCommand(string arg)
        {
            if (string.IsNullOrEmpty(arg))
                return Lang.TF("cmd.public.state", "自動公開: {0}（部屋作成の{1}秒後）  /public on|off で切替、/public now で今すぐ公開", "Auto public: {0} ({1}s after the lobby is created)  /public on|off to toggle, /public now to open the lobby now",
                    OnOff(Options.AutoPublic), Options.AutoPublicDelay);
            string a = arg.ToLowerInvariant();
            if (a == "now" || a == "今")
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || client.GameState != InnerNetClient.GameStates.Joined)
                    return Lang.T("cmd.public.nolobby", "ロビーでのみ使えます。", "Only available in the lobby.");
                if (client.NetworkMode != NetworkModes.OnlineGame)
                    return Lang.T("cmd.public.offline", "オンラインの部屋でのみ使えます。", "Only available in an online lobby.");
                if (client.IsGamePublic) return Lang.T("cmd.public.already", "すでに公開中です。", "The lobby is already public.");
                PocketRolesPlugin.Logger.LogInfo("Commands: /public now");
                Rehost.MakePublicNow();
                return Lang.T("cmd.public.now", "部屋を公開にしました。", "The lobby is now public.");
            }
            if (!TryParseOnOff(a, out var on)) return Lang.T("cmd.public.usage", "使い方: /public on|off|now", "Usage: /public on|off|now");
            if (!Options.TrySet("lobby.autopublic", on ? "on" : "off", out var message))
                return Lang.T("cmd.opt.fail", "設定できません: ", "Failed: ") + message;
            PocketRolesPlugin.Logger.LogInfo($"Commands: auto public {(on ? "on" : "off")}");
            return on
                ? Lang.TF("cmd.public.on", "自動公開をオンにしました。部屋を作ると{0}秒後に公開されます（/opt lobby.autopublicdelay <秒> で変更）。", "Auto public on: the lobby goes public {0}s after it is created (/opt lobby.autopublicdelay <s> to change).", Options.AutoPublicDelay)
                : Lang.T("cmd.public.off", "自動公開をオフにしました。", "Auto public off.");
        }

        // ------------------------------------------------------------------ v0.4: /time, /start, /cancel, /autostart, /haison

        /// <summary>We are the host of a lobby (not in a game, not on the end screen).</summary>
        private static bool InLobby()
        {
            var client = AmongUsClient.Instance;
            return client != null && client.AmHost && client.GameState == InnerNetClient.GameStates.Joined;
        }

        private static bool IsOnline()
        {
            var client = AmongUsClient.Instance;
            return client != null && client.NetworkMode == NetworkModes.OnlineGame;
        }

        private static string LobbyOnlyText() => Lang.T("cmd.lobbyonly", "ロビーでのみ使えます。", "Only available in the lobby.");

        private static string MmSs(int seconds)
        {
            if (seconds < 0) seconds = 0;
            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }

        /// <summary>/time: the remaining lobby time (everyone). The host also sees the auto-start / timer settings.</summary>
        private static string TimeText(bool isHost)
        {
            if (!InLobby()) return LobbyOnlyText();
            if (!IsOnline()) return Lang.T("cmd.time.offline", "ローカルの部屋には時間制限がありません。", "A local lobby has no time limit.");
            int rem = LobbyTimer.Remaining;
            string s = rem < 0
                ? Lang.T("cmd.time.unknown", "ロビーの残り時間はまだ分かりません（サーバーからの通知待ち）。", "The lobby time left is not known yet (waiting for the server).")
                : Lang.TF("cmd.time.left", "ロビーの残り時間: {0}（時間切れになると部屋が閉じます）", "Lobby time left: {0} (the lobby closes when it runs out)", MmSs(rem));
            if (isHost)
            {
                s += "\n" + Lang.TF("cmd.time.host", "時間切れ時の動作: {0}  自動開始: {1}  /start 今すぐ開始, /haison 廃村で時間をリセット",
                    "On expiry: {0}  auto start: {1}  /start now, /haison resets the timer", Options.TimerMode, AutoStartState());
            }
            return s;
        }

        private static string AutoStartState()
        {
            return Options.AutoStart
                ? Lang.TF("cmd.autostart.on.short", "オン（{0}人で{1}秒後）", "on ({0} players, {1}s)", Options.AutoStartPlayers, Options.AutoStartCountdown)
                : OnOff(false);
        }

        /// <summary>/start: force the start countdown now, whatever the player count (AutoStart.ForceStart).</summary>
        private static string StartNow()
        {
            if (!InLobby()) return LobbyOnlyText();
            int seconds = Options.AutoStartCountdown;
            if (!AutoStart.ForceStart(seconds))
                return Lang.T("cmd.start.fail", "開始できません（すでに開始処理中です）。", "Cannot start now (a start is already in progress).");
            PocketRolesPlugin.Logger.LogInfo($"Commands: /start ({seconds}s)");
            return Lang.TF("cmd.start.ok", "{0}秒後に開始します。/cancel で取り消せます。", "Starting in {0}s. /cancel aborts it.", seconds);
        }

        /// <summary>/cancel: abort a running start countdown (AutoStart.CancelStart → vanilla ResetStartState).</summary>
        private static string CancelStart()
        {
            if (!InLobby()) return LobbyOnlyText();
            if (!AutoStart.CancelStart())
                return Lang.T("cmd.cancel.none", "取り消せる開始処理がありません。", "There is no start countdown to cancel.");
            PocketRolesPlugin.Logger.LogInfo("Commands: /cancel");
            return Lang.T("cmd.cancel.ok", "開始を取り消しました。", "Start cancelled.");
        }

        /// <summary>/autostart (state), /autostart on|off, /autostart &lt;N&gt; (sets the player count and turns it on).</summary>
        private static string AutoStartCommand(string arg)
        {
            if (string.IsNullOrEmpty(arg))
            {
                return Lang.TF("cmd.autostart.state", "自動開始: {0}  /autostart on|off|<人数> で変更（{1}人が揃うと{2}秒後に開始）",
                    "Auto start: {0}  /autostart on|off|<n> to change (starts {2}s after {1} players are in)",
                    AutoStartState(), Options.AutoStartPlayers, Options.AutoStartCountdown);
            }
            string a = arg.ToLowerInvariant();
            if (TryParseOnOff(a, out var on))
            {
                // AutoStart.SetEnabled (not the raw option): off cancels a running auto countdown, on re-arms rule 1.
                AutoStart.SetEnabled(on);
                PocketRolesPlugin.Logger.LogInfo($"Commands: autostart {(on ? "on" : "off")}");
                return on
                    ? Lang.TF("cmd.autostart.on", "自動開始をオンにしました。{0}人が揃うと{1}秒後に開始します。", "Auto start on: the game starts {1}s after {0} players are in.", Options.AutoStartPlayers, Options.AutoStartCountdown)
                    : Lang.T("cmd.autostart.off", "自動開始をオフにしました。", "Auto start off.");
            }
            if (!int.TryParse(a.TrimEnd('人'), out var n) || n < 4 || n > 15)
                return Lang.T("cmd.autostart.usage", "使い方: /autostart on|off|<人数>（4〜15）", "Usage: /autostart on|off|<players> (4-15)");
            AutoStart.SetPlayers(n);
            PocketRolesPlugin.Logger.LogInfo($"Commands: autostart on, {Options.AutoStartPlayers} players");
            return Lang.TF("cmd.autostart.on", "自動開始をオンにしました。{0}人が揃うと{1}秒後に開始します。", "Auto start on: the game starts {1}s after {0} players are in.", Options.AutoStartPlayers, Options.AutoStartCountdown);
        }

        /// <summary>
        /// /haison: in the lobby, start and immediately end a game so everyone stays in the same lobby with a fresh
        /// timer; during a game, end it right away (same as the F7 hotkey). Haison.Run notifies the players itself.
        /// </summary>
        private static string HaisonCommand()
        {
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost) return LobbyOnlyText();
            if (!IsOnline()) return Lang.T("cmd.haison.offline", "オンラインの部屋でのみ使えます。", "Only available in an online lobby.");
            bool inLobby = client.GameState == InnerNetClient.GameStates.Joined;
            bool inGame = client.GameState == InnerNetClient.GameStates.Started;
            if (!inLobby && !inGame) return Lang.T("cmd.haison.nostate", "今は実行できません（終了画面の後にどうぞ）。", "Not possible right now (wait for the end screen to finish).");
            if (Core.Game.HaisonActive) return Lang.T("cmd.haison.already", "すでに廃村処理中です。", "A haison is already in progress.");
            if (!Core.Game.IsHostActive) return Lang.T("cmd.modoff", "MODは現在オフです（バニラの試合）。", "The mod is currently off (vanilla game).");
            PocketRolesPlugin.Logger.LogInfo($"Commands: /haison ({(inLobby ? "lobby" : "game")})");
            if (!Haison.Run())
                return Lang.T("cmd.haison.busy", "今は実行できません（開始処理中か、すでに廃村中です）。", "Not possible now (a start is in progress or a haison is already running).");
            return inLobby
                ? Lang.T("cmd.haison.lobby", "廃村を実行します。一度開始してすぐ終了し、同じ部屋（同じコード）に戻ります。", "Running haison: a game starts and ends at once; everyone returns to this lobby (same code).")
                : Lang.T("cmd.haison.game", "試合を終了します（廃村）。同じ部屋に戻ります。", "Ending the game (haison). Everyone returns to this lobby.");
        }

        // ------------------------------------------------------------------ v0.4: /endmeeting, /results, /region

        /// <summary>/endmeeting: end the vote now (MeetingTools explains on the host screen when it cannot).</summary>
        private static string EndMeetingCommand()
        {
            if (!Core.Game.IsHostActive) return Lang.T("cmd.modoff", "MODは現在オフです（バニラの試合）。", "The mod is currently off (vanilla game).");
            if (MeetingHud.Instance == null) return Lang.T("meeting.none", "会議中ではありません。", "There is no meeting right now.");
            PocketRolesPlugin.Logger.LogInfo("Commands: /endmeeting");
            if (!MeetingTools.EndMeetingNow()) return null; // the reason was already shown on the host screen
            return Lang.T("cmd.endmeeting.ok", "投票を終了しました。", "The vote has been ended.");
        }

        /// <summary>/results: skip the rest of the results screen (state Results only).</summary>
        private static string ResultsCommand()
        {
            if (!Core.Game.IsHostActive) return Lang.T("cmd.modoff", "MODは現在オフです（バニラの試合）。", "The mod is currently off (vanilla game).");
            if (MeetingHud.Instance == null) return Lang.T("meeting.none", "会議中ではありません。", "There is no meeting right now.");
            PocketRolesPlugin.Logger.LogInfo("Commands: /results");
            if (!MeetingTools.ShortenResults()) return null; // the reason was already shown on the host screen
            return Lang.T("cmd.results.ok", "結果画面を短縮しました。", "Results screen shortened.");
        }

        /// <summary>Messages allowed for the /region table (current region + one line per official region + hint).</summary>
        private const int RegionMessages = 4;

        /// <summary>/region: the current region and the last latency table measured by AutoRegion (host-local).</summary>
        private static string RegionText()
        {
            var sb = new StringBuilder();
            string current = null;
            try
            {
                if (ServerManager.InstanceExists)
                {
                    var region = ServerManager.Instance.CurrentRegion;
                    if (region != null) current = region.Name;
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Commands.RegionText: {e.Message}");
            }
            sb.Append(Lang.TF("cmd.region.current", "現在の地域: {0}", "Current region: {0}", string.IsNullOrEmpty(current) ? "?" : current));
            string table = AutoRegion.TableText();
            sb.Append('\n');
            if (string.IsNullOrWhiteSpace(table))
            {
                sb.Append(Lang.T("cmd.region.notable", "応答時間はまだ計測していません（オンラインメニューを開いた時に計測します）。", "No latency measured yet (it is measured when the online menu opens)."));
            }
            else
            {
                sb.Append(Lang.T("cmd.region.table", "応答時間（中央値）:", "Latency (median):")).Append('\n').Append(table.Trim());
            }
            sb.Append('\n');
            sb.Append(Lang.TF("cmd.region.auto", "自動で最速の地域を選ぶ: {0}（/opt lobby.autoregion on|off。部屋を作る前にだけ切り替わります）",
                "Auto lowest-ping region: {0} (/opt lobby.autoregion on|off; only applied before a lobby is created)", OnOff(Options.AutoRegion)));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ v0.4c: /diag

        /// <summary>Messages allowed for the diagnostics page (host screen).</summary>
        private const int DiagMessages = 10;

        /// <summary>
        /// /diag: the start-trace module's snapshot of the game / screen state (<see cref="Game.Diagnostics.Describe"/>:
        /// client state, ship, intro, HUD overlay, GameManager flags, mod flags), shown on the host screen and written
        /// to the log so a black screen (verify-findings #8) can be pinned down without a debugger.
        /// </summary>
        private static string DiagText()
        {
            string text;
            try
            {
                text = Game.Diagnostics.Describe();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Commands.DiagText: {e}");
                return Lang.TF("cmd.diag.error", "診断の取得に失敗しました: {0}", "Diagnostics failed: {0}", e.Message);
            }
            if (string.IsNullOrWhiteSpace(text))
                text = Lang.T("cmd.diag.empty", "診断情報はありません。", "No diagnostics available.", "没有诊断信息。");
            PocketRolesPlugin.Logger.LogInfo("Diagnostics (/diag):\n" + text);
            return Lang.T("cmd.diag.title", "診断（ログにも出力しました）:", "Diagnostics (also written to the log):", "诊断（同时已写入日志）:") + "\n" + text;
        }

        /// <summary>/diag on|off: the verbose start-flow trace (Game.Diagnostics.Verbose, defined by the diagnostics module).</summary>
        private static string DiagVerbose(bool on)
        {
            Game.Diagnostics.Verbose = on;
            PocketRolesPlugin.Logger.LogInfo($"Commands: diag verbose {(on ? "on" : "off")}");
            return on
                ? Lang.T("cmd.diag.on", "詳細診断ログをオンにしました（開始処理の各段階をログに出力）。/diag で状態表示、/diag off で停止。", "Verbose diagnostics on (every start-flow step is logged). /diag shows the state, /diag off stops it.", "已开启详细诊断日志（记录开局流程的每一步）。/diag 显示状态，/diag off 关闭。")
                : Lang.T("cmd.diag.off", "詳細診断ログをオフにしました。", "Verbose diagnostics off.", "已关闭详细诊断日志。");
        }

        // ------------------------------------------------------------------ v0.4e: guide room (/code, /announce, /move)

        /// <summary>Text with an inline zh fallback plus string.Format (the JSON tables win when they carry the key).</summary>
        private static string TF3(string key, string ja, string en, string zh, params object[] args)
        {
            string text = Lang.T(key, ja, en, zh);
            try { return string.Format(text, args ?? Array.Empty<object>()); }
            catch (FormatException) { try { return string.Format(ja, args ?? Array.Empty<object>()); } catch (FormatException) { return text; } }
        }

        /// <summary>We host the current lobby as a registered (+25) one.</summary>
        private static bool LobbyRegistered()
        {
            try { return Registration.Hosting && Registration.Registered; } catch (Exception) { return false; }
        }

        /// <summary>
        /// Windows clipboard: the game's own ClipboardHelper (Assembly-CSharp, used by the vanilla "copy code" button)
        /// first, UnityEngine.GUIUtility.systemCopyBuffer as the fallback. False when neither worked.
        /// </summary>
        private static bool CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                ClipboardHelper.PutClipboardString(text);
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Commands: ClipboardHelper.PutClipboardString failed ({e.Message}); trying GUIUtility");
            }
            try
            {
                GUIUtility.systemCopyBuffer = text;
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Commands: GUIUtility.systemCopyBuffer failed ({e.Message})");
                return false;
            }
        }

        /// <summary>/code (toggle), /code on|off: the big room-code overlay on the host's lobby screen ([Guide] ShowCodeOverlay).</summary>
        private static string CodeCommand(string arg)
        {
            bool on;
            if (string.IsNullOrEmpty(arg)) on = !Options.ShowCodeOverlay;
            else if (!TryParseOnOff(arg, out on)) return Lang.T("cmd.code.usage", "使い方: /code（切替）, /code on|off", "Usage: /code (toggle), /code on|off", "用法: /code（切换）, /code on|off");
            Options.ShowCodeOverlay = on;
            UI.CodeOverlay.Refresh();
            PocketRolesPlugin.Logger.LogInfo($"Commands: code overlay {(on ? "on" : "off")}");
            string code = Rehost.CurrentRoomCode();
            string s = TF3("cmd.code.state", "部屋コードの大表示: {0}", "Big room-code overlay: {0}", "大字显示房间代码: {0}", OnOff(on));
            if (code.Length > 0) s += "  " + TF3("cmd.code.current", "現在のコード: {0}", "Current code: {0}", "当前代码: {0}", code);
            if (on && !InLobby()) s += "\n" + Lang.T("cmd.code.lobbyonly", "（ロビーにいる間だけ表示されます）", "(shown only while in the lobby)", "（仅在大厅中显示）");
            return s;
        }

        /// <summary>Messages allowed for the /announce page (clipboard line, note, four steps; host screen only).</summary>
        private const int AnnounceMessages = 8;

        /// <summary>
        /// /announce (alias /guide): copies "役職部屋 CODE" to the clipboard and prints the sub-phone guide-room steps.
        /// The code is this lobby's when it is registered, else [Guide] RoleRoomCode (the role lobby a 便利ホスト room
        /// points to), else this lobby's anyway.
        /// </summary>
        private static string AnnounceText()
        {
            if (!InLobby()) return LobbyOnlyText();
            if (!IsOnline()) return Lang.T("cmd.public.offline", "オンラインの部屋でのみ使えます。", "Only available in an online lobby.");
            bool registered = LobbyRegistered();
            string here = Rehost.CurrentRoomCode();
            string code = registered ? here : (Options.RoleRoomCode.Length > 0 ? Options.RoleRoomCode : here);
            if (string.IsNullOrEmpty(code))
                return Lang.T("cmd.announce.nocode", "部屋コードがまだ分かりません。", "The room code is not known yet.", "还不知道房间代码。");
            string clip = TF3("guide.clip", "役職部屋 {0}", "Role room {0}", "职业房 {0}", code);
            bool copied = CopyToClipboard(clip);
            PocketRolesPlugin.Logger.LogInfo($"Commands: /announce code {code} (registered={registered}, copied={copied})");
            var sb = new StringBuilder();
            sb.Append(copied
                ? TF3("cmd.announce.copied", "クリップボードにコピーしました: 「{0}」", "Copied to the clipboard: \"{0}\"", "已复制到剪贴板：“{0}”", clip)
                : TF3("cmd.announce.notcopied", "コピーできませんでした。手で書き写してください: 「{0}」", "Could not copy; write it down by hand: \"{0}\"", "无法复制，请手动抄写：“{0}”", clip));
            if (!registered)
            {
                sb.Append('\n');
                sb.Append(Options.RoleRoomCode.Length > 0
                    ? TF3("cmd.announce.compat", "この部屋は登録オフ（便利ホスト）です。案内するのは役職部屋 {0} のコードです（/move <コード> で変更）。", "This lobby is unregistered (便利ホスト); the announced code is the role lobby {0} (/move <code> changes it).", "本房间未注册（便利房）；引导的是职业房 {0} 的代码（/move <代码> 可更改）。", code)
                    : Lang.T("cmd.announce.compat.self", "この部屋は登録オフ（便利ホスト）です。役職部屋のコードは /move <コード> で設定できます。", "This lobby is unregistered (便利ホスト); /move <code> sets the role lobby's code.", "本房间未注册（便利房）；用 /move <代码> 设置职业房代码。"));
            }
            sb.Append('\n');
            sb.Append(Lang.T("cmd.announce.head", "【案内部屋の作り方（サブスマホ）】", "[Guide room on the sub-phone]", "【引导房的做法（副手机）】"));
            sb.Append('\n');
            sb.Append(TF3("cmd.announce.step1", "①サブスマホで名前を『役職→{0}』にする", "1) On the sub-phone set your name to \"Roles→{0}\"", "①在副手机上把名字改为“职业→{0}”", code));
            sb.Append('\n');
            sb.Append(Lang.T("cmd.announce.step2", "②バニラのまま公開部屋を作る（MODなし）", "2) Create a PUBLIC vanilla lobby there (no mod)", "②用原版创建一个公开房间（无模组）"));
            sb.Append('\n');
            sb.Append(TF3("cmd.announce.step3", "③チャットに『役職ありは {0}』と書く", "3) Write \"Roles: {0}\" in that lobby's chat", "③在聊天里写“有职业的房间是 {0}”", code));
            sb.Append('\n');
            sb.Append(Lang.T("cmd.announce.step4", "④コードが変わったら（再ホスト・廃村後）名前を直す。/announce でまたコピーできます", "4) When the code changes (re-host / haison) fix the name; /announce copies it again", "④代码变了（重开房间、废村后）就改名字；/announce 可再次复制"));
            return sb.ToString();
        }

        private const string MoveTag = "guide.move";
        private const float MoveDelay = 30f;

        /// <summary>
        /// /move (alias /migrate) [CODE]|cancel: from an unregistered 便利ホスト lobby, tell everyone (ja / zh / en) where
        /// the role lobby is ([Guide] RoleRoomCode, or "the code is in the guide room host's name") and, with
        /// [Guide] AutoRecreateRegistered, re-create this lobby as a registered (+25) one 30 s later (Rehost.RecreateNow
        /// with the in-use guard off; Options.HostAuthorityMode is switched on first so the new lobby registers).
        /// </summary>
        private static string MoveCommand(string arg)
        {
            string a = arg == null ? "" : arg.ToLowerInvariant();
            if (a == "cancel" || a == "stop" || a == "off" || a == "中止" || a == "取消")
            {
                if (!Scheduler.HasTag(MoveTag)) return Lang.T("cmd.move.nopending", "予定している作り直しはありません。", "No re-creation is pending.", "没有待执行的重建。");
                Scheduler.Cancel(MoveTag);
                PocketRolesPlugin.Logger.LogInfo("Commands: /move cancelled");
                Chat.All(Chat.Title, Lang.T("guide.move.cancelled.ja", "部屋の作り直しを中止しました。このままこの部屋で続けます。", "部屋の作り直しを中止しました。このままこの部屋で続けます。", "部屋の作り直しを中止しました。このままこの部屋で続けます。")
                    + "\n" + Lang.T("guide.move.cancelled.zh", "已取消重建房间，继续留在本房间。", "已取消重建房间，继续留在本房间。", "已取消重建房间，继续留在本房间。")
                    + "\n" + Lang.T("guide.move.cancelled.en", "The lobby re-creation was cancelled; we stay in this lobby.", "The lobby re-creation was cancelled; we stay in this lobby.", "The lobby re-creation was cancelled; we stay in this lobby."));
                return Lang.T("cmd.move.cancelled", "作り直しを中止しました（全員に通知）。", "Re-creation cancelled (everyone was told).", "已取消重建（已通知所有人）。");
            }
            if (!InLobby()) return LobbyOnlyText();
            if (!IsOnline()) return Lang.T("cmd.public.offline", "オンラインの部屋でのみ使えます。", "Only available in an online lobby.");
            if (a.Length > 0)
            {
                string set = Options.NormalizeRoomCode(a);
                if (set.Length == 0)
                    return Lang.T("cmd.move.usage", "使い方: /move（案内を流す）, /move <役職部屋のコード>, /move cancel", "Usage: /move (announce), /move <role lobby code>, /move cancel", "用法: /move（发送引导）, /move <职业房代码>, /move cancel");
                Options.RoleRoomCode = set;
                PocketRolesPlugin.Logger.LogInfo($"Commands: role room code set to {set}");
            }
            string here = Rehost.CurrentRoomCode();
            if (LobbyRegistered())
                return TF3("cmd.move.registered", "この部屋はすでに MOD 登録ありの役職部屋です（コード {0}）。案内部屋の手順は /announce で表示します。", "This lobby already is the registered role lobby (code {0}); /announce shows the guide-room steps.", "本房间已经是已注册的职业房（代码 {0}）；/announce 显示引导房步骤。", here);
            string role = Options.RoleRoomCode;
            bool auto = Options.AutoRecreateRegistered;

            // Everyone, all three languages (one message per line; Chat.All paces the chunks per client).
            var msg = new StringBuilder();
            if (role.Length > 0)
            {
                msg.Append(TF3("guide.move.code.ja", "役職ありの部屋は {0} です。コードを入力して入ってください。", "役職ありの部屋は {0} です。コードを入力して入ってください。", "役職ありの部屋は {0} です。コードを入力して入ってください。", role)).Append('\n');
                msg.Append(TF3("guide.move.code.zh", "有职业的房间是 {0}，请输入代码加入。", "有职业的房间是 {0}，请输入代码加入。", "有职业的房间是 {0}，请输入代码加入。", role)).Append('\n');
                msg.Append(TF3("guide.move.code.en", "The lobby with roles is {0}: enter the code to join it.", "The lobby with roles is {0}: enter the code to join it.", "The lobby with roles is {0}: enter the code to join it.", role));
            }
            else
            {
                msg.Append(Lang.T("guide.move.name.ja", "役職ありの部屋のコードは案内部屋のホストの名前に表示しています。", "役職ありの部屋のコードは案内部屋のホストの名前に表示しています。", "役職ありの部屋のコードは案内部屋のホストの名前に表示しています。")).Append('\n');
                msg.Append(Lang.T("guide.move.name.zh", "有职业的房间代码显示在引导房房主的名字上。", "有职业的房间代码显示在引导房房主的名字上。", "有职业的房间代码显示在引导房房主的名字上。")).Append('\n');
                msg.Append(Lang.T("guide.move.name.en", "The code of the lobby with roles is shown in the guide room host's name.", "The code of the lobby with roles is shown in the guide room host's name.", "The code of the lobby with roles is shown in the guide room host's name."));
            }
            if (auto)
            {
                msg.Append('\n');
                msg.Append(TF3("guide.move.auto.ja", "{0}秒後にこの部屋を役職ありの部屋として作り直します。新しいコードで入り直してください。", "{0}秒後にこの部屋を役職ありの部屋として作り直します。新しいコードで入り直してください。", "{0}秒後にこの部屋を役職ありの部屋として作り直します。新しいコードで入り直してください。", (int)MoveDelay)).Append('\n');
                msg.Append(TF3("guide.move.auto.zh", "{0} 秒后本房间将重建为有职业的房间，请用新代码重新加入。", "{0} 秒后本房间将重建为有职业的房间，请用新代码重新加入。", "{0} 秒后本房间将重建为有职业的房间，请用新代码重新加入。", (int)MoveDelay)).Append('\n');
                msg.Append(TF3("guide.move.auto.en", "In {0} s this lobby is re-created as the lobby with roles; rejoin with the new code.", "In {0} s this lobby is re-created as the lobby with roles; rejoin with the new code.", "In {0} s this lobby is re-created as the lobby with roles; rejoin with the new code.", (int)MoveDelay));
            }
            Chat.All(Chat.Title, msg.ToString());
            PocketRolesPlugin.Logger.LogInfo($"Commands: /move announced (role code {(role.Length > 0 ? role : "none")}, auto re-create={auto}, here {here})");

            var reply = new StringBuilder();
            reply.Append(role.Length > 0
                ? TF3("cmd.move.sent", "全員に案内を送りました（役職部屋 {0}、3 言語）。", "Announcement sent to everyone (role lobby {0}, 3 languages).", "已向所有人发送引导（职业房 {0}，3 种语言）。", role)
                : Lang.T("cmd.move.sent.name", "全員に案内を送りました（コードは案内部屋のホスト名を見てもらう）。/move <コード> でコードも流せます。", "Announcement sent (players look at the guide room host's name for the code). /move <code> announces a code too.", "已向所有人发送引导（让玩家看引导房房主的名字）。/move <代码> 也可直接发送代码。"));
            if (auto)
            {
                Scheduler.Cancel(MoveTag);
                Scheduler.After(MoveDelay - 5f, () =>
                {
                    Chat.All(Chat.Title, Lang.T("guide.move.soon.ja", "5秒後に部屋を作り直します。新しいコードで入り直してください。", "5秒後に部屋を作り直します。新しいコードで入り直してください。", "5秒後に部屋を作り直します。新しいコードで入り直してください。")
                        + "\n" + Lang.T("guide.move.soon.zh", "5 秒后重建房间，请用新代码重新加入。", "5 秒后重建房间，请用新代码重新加入。", "5 秒后重建房间，请用新代码重新加入。")
                        + "\n" + Lang.T("guide.move.soon.en", "Re-creating the lobby in 5 s; rejoin with the new code.", "Re-creating the lobby in 5 s; rejoin with the new code.", "Re-creating the lobby in 5 s; rejoin with the new code."));
                }, MoveTag);
                Scheduler.After(MoveDelay, () =>
                {
                    try
                    {
                        Options.HostAuthorityMode = true; // Registration_CoCreateOnlineGamePatch reads it when the new lobby is created
                        if (Rehost.RecreateNow("/move: re-create as a registered role lobby", true))
                        {
                            PocketRolesPlugin.Logger.LogInfo("Commands: /move re-creating the lobby as registered (+25)");
                            return;
                        }
                        Chat.Local(Chat.Title, Lang.T("cmd.move.failed", "部屋を作り直せませんでした（開始処理中か、すでに再ホスト中）。/move でもう一度どうぞ。", "Could not re-create the lobby (a start is under way or a re-host is already running). Try /move again.", "无法重建房间（正在开始或已在重建中）。请再次 /move。"));
                    }
                    catch (Exception e)
                    {
                        PocketRolesPlugin.Logger.LogError($"Commands: /move re-create failed: {e}");
                    }
                }, MoveTag);
                reply.Append('\n');
                reply.Append(TF3("cmd.move.auto", "{0} 秒後に MOD 登録ありの役職部屋として作り直します（全員がコードで入り直し）。/move cancel で中止。", "In {0} s the lobby is re-created as a registered role lobby; everyone rejoins with the new code. /move cancel aborts.", "{0} 秒后将重建为已注册的职业房（所有人用新代码重新加入）。/move cancel 可取消。", (int)MoveDelay));
            }
            else
            {
                reply.Append('\n');
                reply.Append(Lang.T("cmd.move.manual", "この部屋はそのままです（自動作り直しはオフ: /opt guide.autoreg on で 30 秒後に登録部屋として作り直し）。", "This lobby stays as it is (auto re-create is off: /opt guide.autoreg on re-creates it as registered 30 s after /move).", "本房间保持不变（自动重建已关闭：/opt guide.autoreg on 可在 /move 30 秒后重建为注册房间）。"));
            }
            return reply.ToString();
        }

        // ------------------------------------------------------------------ v0.4: /rules

        /// <summary>Longest custom rules text accepted (two chat messages at most).</summary>
        private const int MaxRulesTextChars = 200;

        /// <summary>/rules (state), /rules show, /rules none|reset, /rules &lt;text…&gt; (sets the text and RulesMode = custom).</summary>
        private static void HandleRules(PlayerControl sender, string arg1, string rest)
        {
            string a = arg1 == null ? "" : arg1.ToLowerInvariant();
            if (a.Length == 0 || a == "show")
            {
                Reply(sender, RulesStateText());
                return;
            }
            if (a == "none" || a == "reset" || a == "default" || a == "clear" || a == "off" || a == "なし")
            {
                Options.TrySet("chat.rulesmode", "none", out _);
                PocketRolesPlugin.Logger.LogInfo("Commands: rules mode none");
                Reply(sender, Lang.T("cmd.rules.none", "ルール行を標準（ルールなし）に戻しました。", "Rules line reset to the built-in one (no rules).") + "\n" + Chat.RulesLine());
                return;
            }
            string text = rest ?? "";
            if (text.Length > MaxRulesTextChars)
            {
                Reply(sender, Lang.TF("cmd.rules.toolong", "長すぎます（最大 {0} 文字）。", "Too long (max {0} characters).", MaxRulesTextChars));
                return;
            }
            if (!Options.TrySet("chat.rulestext", text, out var message))
            {
                Reply(sender, Lang.T("cmd.opt.fail", "設定できません: ", "Failed: ") + message);
                return;
            }
            Options.TrySet("chat.rulesmode", "custom", out _);
            PocketRolesPlugin.Logger.LogInfo($"Commands: rules text set ({text.Length} chars)");
            Reply(sender, Lang.T("cmd.rules.set", "ルールを設定しました（挨拶に表示されます。/welcome show でプレビュー）:", "Rules set (shown in the welcome; /welcome show previews it):") + "\n" + Chat.RulesLine());
        }

        private static string RulesStateText()
        {
            string mode = Chat.HasCustomRules
                ? Lang.T("cmd.rules.custom", "カスタム", "custom")
                : Lang.T("cmd.rules.builtin", "標準（ルールなし）", "built-in (no rules)");
            var sb = new StringBuilder();
            sb.Append(Lang.TF("cmd.rules.state", "挨拶のルール行: {0}", "Welcome rules line: {0}", mode));
            sb.Append('\n').Append(Chat.RulesLine());
            sb.Append('\n');
            // Lang.T (not TF): {rules} below is a literal placeholder.
            sb.Append(Lang.T("cmd.rules.usage",
                "使い方: /rules <文章>（\\n で改行）, /rules none で標準に戻す。挨拶文の {rules} の位置に入ります",
                "Usage: /rules <text> (\\n = line break), /rules none = built-in. It replaces {rules} in the welcome text"));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ v0.4: /cos

        /// <summary>/cos ids (every player's cosmetic ids, host screen), /cos reload, /cos music custom|vanilla|mute.</summary>
        private static void HandleCos(PlayerControl sender, string arg1, string arg2)
        {
            string a = arg1 == null ? "" : arg1.ToLowerInvariant();
            if (a == "ids" || a == "id" || a == "list")
            {
                // Host screen only (no network): one local line per player, so nothing is capped or paced.
                var lines = PocketRoles.Cosmetics.Cosmetics.ListIds();
                if (lines == null || lines.Count == 0)
                {
                    Reply(sender, Lang.T("cmd.cos.noids", "プレイヤーがいません。", "No players."));
                    return;
                }
                Chat.Local(Chat.Title, Lang.T("cmd.cos.ids.head", "【見た目のID】（ログにも出力）", "[Cosmetic ids] (also written to the log)"));
                foreach (var line in lines) Chat.Local(Chat.Title, line);
                return;
            }
            if (a == "reload")
            {
                PocketRoles.Cosmetics.Cosmetics.Reload();
                PocketRolesPlugin.Logger.LogInfo("Commands: /cos reload");
                Reply(sender, Lang.T("cmd.cos.reloaded", "見た目のファイルを読み直しました。", "Cosmetic files reloaded."));
                return;
            }
            if (a == "music" || a == "bgm")
            {
                if (string.IsNullOrEmpty(arg2))
                {
                    Reply(sender, Lang.TF("cmd.cos.music.state", "ロビー音楽: {0}  /cos music custom|vanilla|mute で変更", "Lobby music: {0}  /cos music custom|vanilla|mute to change", Options.LobbyMusic));
                    return;
                }
                if (!PocketRoles.Cosmetics.Cosmetics.SetMusicMode(arg2))
                {
                    Reply(sender, Lang.T("cmd.cos.music.usage", "使い方: /cos music custom|vanilla|mute", "Usage: /cos music custom|vanilla|mute"));
                    return;
                }
                PocketRolesPlugin.Logger.LogInfo($"Commands: /cos music {Options.LobbyMusic}");
                Reply(sender, Lang.TF("cmd.cos.music.set", "ロビー音楽を {0} にしました。", "Lobby music set to {0}.", Options.LobbyMusic));
                return;
            }
            Reply(sender, Lang.TF("cmd.cos.usage",
                "使い方: /cos ids（全員の帽子等のID）, /cos reload（画像・音楽を読み直す）, /cos music custom|vanilla|mute。見た目: {0}",
                "Usage: /cos ids (everyone's cosmetic ids), /cos reload (re-read images/music), /cos music custom|vanilla|mute. Cosmetics: {0}",
                OnOff(Options.CosmeticsEnabled)));
        }

        // ------------------------------------------------------------------ v0.4b: /admin, /mod, /vip, /kick, /ban, /vset

        /// <summary>/admin|/mod|/vip (state), add &lt;name&gt;, remove &lt;name|code&gt;, list, reload.</summary>
        private static void HandlePermList(PlayerControl sender, Permissions.ListKind kind, string verb, string rest)
        {
            string a = verb == null ? "" : verb.ToLowerInvariant();
            string word = kind == Permissions.ListKind.Admin ? "admin" : kind == Permissions.ListKind.Moderator ? "mod" : "vip";
            if (a.Length == 0 || a == "list" || a == "ls" || a == "show" || a == "一覧")
            {
                string s = Permissions.ListText(kind);
                if (a.Length == 0)
                {
                    s += "\n" + Lang.TF("cmd.perm.usage", "使い方: /{0} add <名前|番号|フレンドコード>, /{0} remove <名前|コード>, /{0} list（{1}）",
                        "Usage: /{0} add <name|id|friend code>, /{0} remove <name|code>, /{0} list ({1})", word, Permissions.FileNameOf(kind));
                    if (kind == Permissions.ListKind.Admin)
                        s += "\n" + Lang.TF("cmd.perm.admin.state", "アドミンの設定変更: {0}（/opt perm.adminsettings on|off）", "Admins can change settings: {0} (/opt perm.adminsettings on|off)", OnOff(Options.AdminsCanChangeSettings));
                    else if (kind == Permissions.ListKind.Moderator)
                        s += "\n" + Lang.TF("cmd.perm.mod.state", "モデレーターのキック/BAN: {0}（/opt perm.modkick on|off）", "Moderators can kick / ban: {0} (/opt perm.modkick on|off)", OnOff(Options.ModeratorsCanKick));
                    else
                        s += "\n" + Lang.TF("cmd.perm.vip.state", "VIP の名前に★: {0}（/opt perm.vipmarker on|off）", "VIP star marker: {0} (/opt perm.vipmarker on|off)", OnOff(Options.VipMarker));
                }
                Reply(sender, s);
                return;
            }
            if (a == "reload")
            {
                Permissions.Reload();
                Reply(sender, Lang.T("cmd.perm.reloaded", "権限ファイル（Admin/Moderator/VIP/Banlist.txt）を読み直します。", "Permission files (Admin/Moderator/VIP/Banlist.txt) will be re-read.") + "\n" + Permissions.ListText(kind));
                return;
            }
            if (a == "add" || a == "追加")
            {
                if (string.IsNullOrWhiteSpace(rest)) { Reply(sender, Lang.TF("cmd.perm.add.usage", "使い方: /{0} add <名前|番号|フレンドコード>", "Usage: /{0} add <name|id|friend code>", word)); return; }
                Permissions.Add(kind, rest, out var msg);
                Reply(sender, msg);
                return;
            }
            if (a == "remove" || a == "rm" || a == "del" || a == "delete" || a == "削除")
            {
                if (string.IsNullOrWhiteSpace(rest)) { Reply(sender, Lang.TF("cmd.perm.remove.usage", "使い方: /{0} remove <名前|コード>", "Usage: /{0} remove <name|code>", word)); return; }
                Permissions.Remove(kind, rest, out var msg);
                Reply(sender, msg);
                return;
            }
            // "/vip Taro" = add Taro (shortcut).
            Permissions.Add(kind, (verb + " " + (rest ?? "")).Trim(), out var m);
            Reply(sender, m);
        }

        /// <summary>/kick &lt;name&gt; and /ban &lt;name&gt; (Permissions.Kick checks the ranks and does the kick).</summary>
        private static string KickCommand(PlayerControl sender, string who, bool ban)
        {
            if (string.IsNullOrWhiteSpace(who))
                return ban
                    ? Lang.T("cmd.ban.usage", "使い方: /ban <名前|番号>（キック＋Banlist.txt に登録）, /ban list, /ban remove <名前|コード>", "Usage: /ban <name|id> (kick + Banlist.txt), /ban list, /ban remove <name|code>")
                    : Lang.T("cmd.kick.usage", "使い方: /kick <名前|番号>", "Usage: /kick <name|id>");
            if (!InLobbyOrGame()) return Lang.T("cmd.kick.nolobby", "部屋にいる時だけ使えます。", "Only available while in a lobby or game.");
            Permissions.Kick(sender, who, ban, out var msg);
            return msg;
        }

        private static bool InLobbyOrGame()
        {
            var client = AmongUsClient.Instance;
            return client != null && client.AmHost && (client.GameState == InnerNetClient.GameStates.Joined || client.GameState == InnerNetClient.GameStates.Started);
        }

        /// <summary>/ban &lt;name&gt;, /ban list, /ban remove &lt;name|code&gt;, /ban reload.</summary>
        private static void HandleBan(PlayerControl sender, string arg1, string all, string rest)
        {
            string a = arg1 == null ? "" : arg1.ToLowerInvariant();
            if (a == "list" || a == "ls" || a == "show" || a == "一覧") { Reply(sender, Permissions.ListText(Permissions.ListKind.Ban)); return; }
            if (a == "remove" || a == "rm" || a == "del" || a == "delete" || a == "削除" || a == "unban") { Reply(sender, UnbanText(rest)); return; }
            if (a == "reload") { Permissions.Reload(); Reply(sender, Permissions.ListText(Permissions.ListKind.Ban)); return; }
            if (a == "add" || a == "追加") { Reply(sender, KickCommand(sender, rest, true)); return; }
            Reply(sender, KickCommand(sender, all, true));
        }

        private static string UnbanText(string who)
        {
            if (string.IsNullOrWhiteSpace(who)) return Lang.T("cmd.unban.usage", "使い方: /ban remove <名前|コード>（/ban list で一覧）", "Usage: /ban remove <name|code> (/ban list shows them)");
            Permissions.Remove(Permissions.ListKind.Ban, who, out var msg);
            return msg;
        }

        /// <summary>/vset &lt;key&gt; &lt;value&gt;: a vanilla option beyond the menu's range (Game.VanillaRanges).</summary>
        private static string VanillaSet(string key, string value)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                return Lang.T("cmd.vset.usage", "使い方: /vset <キー> <値>  例: /vset killcooldown 2.5（バニラ設定をメニューの範囲外に設定）", "Usage: /vset <key> <value>  e.g. /vset killcooldown 2.5 (sets a vanilla option beyond the menu range)");
            if (!InLobby()) return LobbyOnlyText();
            bool ok = VanillaRanges.TrySet(key, value, out var msg);
            if (ok) PocketRolesPlugin.Logger.LogInfo($"Commands: /vset {key} {value}");
            return (ok ? "" : Lang.T("cmd.opt.fail", "設定できません: ", "Failed: ")) + (msg ?? "");
        }
    }
}
