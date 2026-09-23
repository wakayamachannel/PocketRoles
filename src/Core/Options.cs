using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;

namespace PocketRoles.Core
{
    public enum OptionKind { Bool, Int, Float, Choice }

    /// <summary>
    /// One editable setting, for the in-game settings tab and /opt. Numbers flow through <see cref="GetNumber"/> /
    /// <see cref="SetNumber"/> (Bool = 0/1, Choice = index) and writing persists exactly like <see cref="Options.TrySet"/>.
    /// </summary>
    public sealed class OptionDescriptor
    {
        /// <summary>Same key accepted by Options.TrySet (e.g. "sheriff.count", "lang", "lobby.autopublic").</summary>
        public string Key;
        public string SectionJa, SectionEn;
        /// <summary>v0.5.5: inline Simplified Chinese of the section (role rows: the role's Chinese name); null = the table only.</summary>
        public string SectionZh;
        /// <summary>Display group: role name for role rows, or General / Lobby / Chat (localized; table key "opt.section.&lt;en&gt;").</summary>
        public string Section => Lang.T("opt.section." + Slug(SectionEn), SectionJa, SectionEn, SectionZh);
        public string NameJa, NameEn;
        /// <summary>v0.5.5: inline Simplified Chinese of the row name (<c>.Zh("…")</c>) for rows the zh table lacks; null = the table only.</summary>
        public string NameZh;
        /// <summary>Localized row name (table key "opt.name.&lt;key&gt;"; a Chinese reader gets English, never Japanese, when both are missing).</summary>
        public string Name => Lang.T("opt.name." + Key, NameJa, NameEn, NameZh);

        private static string Slug(string s) => string.IsNullOrEmpty(s) ? "" : s.ToLowerInvariant().Replace(" ", "");
        /// <summary>Tooltip / help text (one sentence) shown by the settings tab "?" button; null = no help.</summary>
        public string DescJa, DescEn, DescZh;
        /// <summary>Localized tooltip (table key "opt.tip.&lt;key&gt;"), "" when the row has none.</summary>
        public string Desc => string.IsNullOrEmpty(DescJa) ? "" : Lang.T("opt.tip." + Key, DescJa, DescEn, DescZh);
        public bool HasDesc => !string.IsNullOrEmpty(DescJa);
        public OptionKind Kind;
        public float Min, Max, Step;
        /// <summary>Choice: display strings (e.g. "ja","en").</summary>
        public string[] Choices;
        /// <summary>Role colour for role rows, null otherwise.</summary>
        public string ColorHex;
        public Func<float> GetNumber;
        public Action<float> SetNumber;
    }

    /// <summary>All settings, persisted by BepInEx in BepInEx/config/jp.pocketroles.mod.cfg and editable in-game with /set and /opt.</summary>
    public static class Options
    {
        private static ConfigFile _cfg;

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<string> _language;
        private static ConfigEntry<bool> _register;
        private static ConfigEntry<bool> _ignoreVersion;
        private static ConfigEntry<bool> _welcome;
        private static ConfigEntry<bool> _roleInfoAtMeeting;
        private static ConfigEntry<string> _welcomeText;
        private static ConfigEntry<string> _compatWelcomeText;
        private static ConfigEntry<bool> _welcomeIncludeSettings;
        private static ConfigEntry<string> _discordWebhookUrl;
        private static ConfigEntry<bool> _discordAnnounce;
        private static ConfigEntry<string> _discordText;
        private static ConfigEntry<string> _discordAvatarUrl;   // v0.5.5
        private static ConfigEntry<bool> _antiCheatKick;
        private static ConfigEntry<bool> _cheatDetect, _cheatAutoKick, _cheatAnnounceKick;   // v0.5.3 CheatDetector
        private static ConfigEntry<bool> _cheatEndGame;   // v0.5.5 AegisMatchStop
        private static ConfigEntry<bool> _cheatCallout;   // v0.5.3 CalloutWatch
        private static ConfigEntry<bool> _cheatRemoteRules;   // v0.5.5 AegisRules
        private static ConfigEntry<int> _cheatJitter;         // v0.5.5 AegisRules per-lobby variation (percent)
        private static ConfigEntry<bool> _cheatSharedBans, _cheatAutoReport, _cheatBanLadder;   // v0.5.5 AegisBans
        private static ConfigEntry<bool> _wireLog;

        private static ConfigEntry<bool> _autoRehost;
        private static ConfigEntry<bool> _autoPublic;
        private static ConfigEntry<int> _autoPublicDelay;
        private static ConfigEntry<int> _rehostMaxAttempts;
        private static ConfigEntry<int> _maxHostPing;
        private static ConfigEntry<int> _afkKickMinutes;
        // [Compat] unregistered-compatible mode (RegisterAsModdedLobby=false)

        private static ConfigEntry<string> _creditAuthor;
        private static ConfigEntry<string> _creditRepoUrl;
        private static ConfigEntry<bool> _showCredits;
        private static ConfigEntry<bool> _vanillaRoles;

        // v0.3 [Cosmetics] (host screen only)
        private static ConfigEntry<bool> _cosEnabled;
        private static ConfigEntry<string> _cosMusic;
        private static ConfigEntry<string> _cosMusicFile;
        private static ConfigEntry<float> _cosMusicVolume;
        private static ConfigEntry<bool> _cosLobbyPaint;
        private static ConfigEntry<bool> _cosDropship;
        private static ConfigEntry<bool> _cosMenuBackground;
        private static ConfigEntry<bool> _cosCursor;

        // v0.4 [Lobby] timer / auto-start / region / Dleks
        private static ConfigEntry<bool> _autoStart;
        private static ConfigEntry<int> _autoStartPlayers;
        private static ConfigEntry<int> _autoStartCountdown;
        private static ConfigEntry<int> _timerWarnAt;
        private static ConfigEntry<int> _extendNoticeDelay;
        private static ConfigEntry<string> _timerMode;
        private static ConfigEntry<bool> _autoRegion;
        private static ConfigEntry<bool> _enableDleks;

        // v0.4 [General] Game Master, [Hotkeys]
        private static ConfigEntry<bool> _gameMaster;
        private static ConfigEntry<bool> _hotkeysEnabled;
        private static ConfigEntry<string> _hotkeyHaison;
        private static ConfigEntry<string> _hotkeyEndMeeting;
        private static ConfigEntry<string> _hotkeyCancelStart;

        // v0.4 [Chat] command gating, rules line
        private static ConfigEntry<bool> _playerCommands;
        private static ConfigEntry<bool> _allCommands;
        private static ConfigEntry<string> _rulesMode;
        private static ConfigEntry<string> _rulesText;
        private static ConfigEntry<bool> _welcomeAllLanguages;
        // v0.5.5 [Chat] NG words (Chat.NgWords)
        private static ConfigEntry<bool> _ngFilter, _ngBan, _ngAnnounce;
        private static ConfigEntry<int> _ngKickAt;

        // v0.4b [Translate] (the DeepL key lives in BepInEx/PocketRoles/deepl-key.txt, never in the cfg)
        private static ConfigEntry<bool> _trEnabled;
        /// <summary>v0.5.5: show the host-screen "chat translation is off" line (once per game session). Off = never.</summary>
        private static ConfigEntry<bool> _trOffNotice;
        private static ConfigEntry<string> _trProvider;
        private static ConfigEntry<string> _trTargetLang;
        private static ConfigEntry<bool> _trShowOnHost;
        private static ConfigEntry<bool> _trBroadcastToAll;
        private static ConfigEntry<bool> _trForPlayers;
        private static ConfigEntry<bool> _trForeignInCompat;   // v0.5.3
        private static ConfigEntry<bool> _trAutoDetect;
        private static ConfigEntry<int> _trMinChars;
        private static ConfigEntry<int> _trMaxPerMinute;

        // v0.4b [Permissions]
        private static ConfigEntry<bool> _permAdminSettings;
        private static ConfigEntry<bool> _permModKick;
        private static ConfigEntry<bool> _permVipMarker;

        // v0.4b [Vanilla] extended ranges for the vanilla numeric settings
        private static ConfigEntry<bool> _vanExtendedRanges;
        private static ConfigEntry<float> _vanKillMin;
        private static ConfigEntry<float> _vanKillMax;
        private static ConfigEntry<float> _vanKillStep;
        private static ConfigEntry<int> _vanVoteMin;
        private static ConfigEntry<int> _vanVoteMax;
        private static ConfigEntry<int> _vanDiscussMax;
        private static ConfigEntry<int> _vanEmergencyMax;
        private static ConfigEntry<int> _vanTaskMax;
        private static ConfigEntry<bool> _vanClampUnreg;
        private static ConfigEntry<bool> _permAdminLobby;
        private static ConfigEntry<bool> _revealOnDeath;
        private static ConfigEntry<bool> _revealOnLeave, _revealLeaveToAll, _revealToAll;
        private static ConfigEntry<int> _hostShieldKills, _vanGaUses;
        private static ConfigEntry<string> _hostShieldKey;
        /// <summary>SHA-256 (hex) of the phrase that enables [Host] ShieldKills (v0.5.2). The phrase itself is not in the mod.</summary>
        private const string HostShieldKeyHash = "768ee31f58561903359dec96d44227272258e5231864d9476f0949e5eccdb2d3";
        private static bool? _hostShieldUnlocked;
        private static ConfigEntry<bool> _hostGhostRoleList;
        private static ConfigEntry<float> _compatWelcomeInterval;

        // v0.4e [Guide] guide-room support (room-code overlay, /announce, /move)
        private static ConfigEntry<bool> _guideShowCodeOverlay;
        private static ConfigEntry<string> _guideRoleRoomCode;
        private static ConfigEntry<bool> _guideAutoRecreateRegistered;

        public static readonly string[] LobbyMusicChoices = { "custom", "vanilla", "mute" };
        /// <summary>"auto" = DeepL when deepl-key.txt holds a key, else Google.</summary>
        public static readonly string[] TranslateProviderChoices = { "auto", "google", "deepl" };
        public static readonly string[] TranslateLangChoices = { "ja", "zh", "en" };
        /// <summary>File name (inside BepInEx/PocketRoles) that holds the DeepL API key.</summary>
        public const string DeepLKeyFileName = "deepl-key.txt";
        public static readonly string[] TimerModeChoices = { "extend", "haison", "notify" };
        public static readonly string[] RulesModeChoices = { "none", "custom" };
        /// <summary>Keys offered by the settings-tab hotkey rows (the config accepts any UnityEngine.KeyCode name).</summary>
        public static readonly string[] HotkeyChoices = { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" };

        private static readonly Dictionary<CustomRole, ConfigEntry<int>> _count = new Dictionary<CustomRole, ConfigEntry<int>>();
        private static readonly Dictionary<CustomRole, ConfigEntry<int>> _chance = new Dictionary<CustomRole, ConfigEntry<int>>();

        private static ConfigEntry<float> _sheriffKillCooldown;
        private static ConfigEntry<bool> _sheriffCanKillMadmate;
        private static ConfigEntry<float> _jackalKillCooldown;
        private static ConfigEntry<bool> _jackalCanVent;
        private static ConfigEntry<float> _vampireKillDelay;
        private static ConfigEntry<int> _mayorVotes;
        private static ConfigEntry<int> _snitchTasksLeftToWarn;
        private static ConfigEntry<float> _lighterVision;
        private static ConfigEntry<float> _speedBoosterSpeed;
        private static ConfigEntry<bool> _madmateKnownToImpostors;

        // v0.4.1 [Lovers] / [Arsonist] / [Witch] / [Assassin]
        private static ConfigEntry<bool> _loversAllowImpostor, _loversLastThree, _arsonistCanVent, _witchSpelledSeeMark, _assassinFirstMeeting;
        private static ConfigEntry<float> _arsonistDouseCooldown, _witchSpellCooldown;
        private static ConfigEntry<int> _assassinGuessesPerMeeting;

        // v0.5.0 [MadMayor] [MadStuntman] [MadHawk] [Worshipper] [JackalFriends] [EvilNekomata] [SerialKiller] [Samurai] [EvilHawk]
        private static ConfigEntry<int> _madMayorVotes, _madStuntmanLives, _worshipperUses;
        private static ConfigEntry<bool> _madMayorKnownToImpostors, _madStuntmanNotify, _jackalFriendsKnownToJackal, _jackalFriendsSheriffCanKill,
            _nekomataVotersOnly, _nekomataExcludeImpostors, _nekomataAnnounce, _serialKillerResetAtMeeting, _samuraiHitTeammates;
        private static ConfigEntry<float> _madHawkVision, _madHawkSpeed, _worshipperCooldown, _serialKillerKillCooldown, _serialKillerSuicideTime,
            _samuraiKillCooldown, _samuraiRange, _samuraiStagger, _evilHawkVision;

        private static readonly List<OptionDescriptor> _descriptors = new List<OptionDescriptor>();

        /// <summary>Every editable option in display order (role rows first, then General / Lobby / Chat). Built by Init().</summary>
        public static IReadOnlyList<OptionDescriptor> Descriptors => _descriptors;

        public static bool Initialized => _cfg != null;

        /// <summary>
        /// One-time rename migration (HostRoles → PocketRoles): when BepInEx/config/jp.hostroles.mod.cfg exists and
        /// jp.pocketroles.mod.cfg does not, the old file is copied to the new name and re-read so every value carries over.
        /// Runs before any Bind(); BepInEx creates the new file on the first save otherwise.
        /// </summary>
        /// <summary>
        /// The high-ping re-creation limit moved twice: v0.5.4 renamed [Lobby] MaxHostPing (default 0 = off) to
        /// [Lobby] HostPingLimit (default 80 ms = on, request 9/21 "pingの改善も頼んだ"); v0.5.5 turns it off by default
        /// again under [Lobby] PingRecreateLimit (default 0, request 9/21 "pingのことは既定でオフして"). A limit the host
        /// chose carries over: HostPingLimit when above 0 and not 80 (the untouched v0.5.4 default), else MaxHostPing when
        /// above 0. Both old keys leave the file. Never throws.
        /// </summary>
        private static void MigratePingLimit(ConfigFile cfg)
        {
            try
            {
                int v054 = TakeLegacyInt(cfg, "Lobby", "HostPingLimit", out bool has054);
                int v053 = TakeLegacyInt(cfg, "Lobby", "MaxHostPing", out bool has053);
                if (has054 && v054 > 0 && v054 != 80)
                {
                    _maxHostPing.Value = Math.Min(300, v054);
                    PocketRolesPlugin.Logger?.LogInfo($"Config migration: [Lobby] HostPingLimit {v054} → PingRecreateLimit");
                }
                else if (has053 && v053 > 0)
                {
                    _maxHostPing.Value = Math.Min(300, v053);
                    PocketRolesPlugin.Logger?.LogInfo($"Config migration: [Lobby] MaxHostPing {v053} → PingRecreateLimit");
                }
                else if (has054 || has053) PocketRolesPlugin.Logger?.LogInfo("Config migration: [Lobby] HostPingLimit / MaxHostPing removed (the v0.5.4 default or off); PingRecreateLimit stays as it is (default 0 = off)");
                cfg.Save();   // the old keys (bound above only to be read) leave the file
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"Config migration: [Lobby] ping limit not carried over: {e.Message}");
            }
        }

        /// <summary>
        /// The value an older version left in the file under [<paramref name="section"/>] <paramref name="key"/>, then the
        /// key is removed. <paramref name="present"/> is false when the file had no such key (or no whole number there).
        /// </summary>
        private static int TakeLegacyInt(ConfigFile cfg, string section, string key, out bool present)
        {
            present = false;
            var def = new ConfigDefinition(section, key);
            try
            {
                int v = cfg.Bind(def, int.MinValue).Value;   // the sentinel tells a missing key apart from any real value
                present = v != int.MinValue;
                return present ? v : 0;
            }
            catch (Exception) { return 0; }
            finally
            {
                try { cfg.Remove(def); } catch (Exception) { }
            }
        }

        private static bool MigrateLegacyConfig(ConfigFile cfg)
        {
            try
            {
                string newPath = cfg.ConfigFilePath;
                if (string.IsNullOrEmpty(newPath)) return false;
                string dir = Path.GetDirectoryName(newPath);
                if (string.IsNullOrEmpty(dir)) return false;
                string oldPath = Path.Combine(dir, "jp.hostroles.mod.cfg");
                if (File.Exists(newPath) || !File.Exists(oldPath)) return false;
                File.Copy(oldPath, newPath, false);
                cfg.Reload(); // the ConfigFile was constructed before the copy existed: pick the values up now
                PocketRolesPlugin.Logger?.LogInfo("Config migrated from jp.hostroles.mod.cfg to " + Path.GetFileName(newPath));
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogError($"Options.MigrateLegacyConfig: {e}");
                return false;
            }
        }

        /// <summary>
        /// v0.5.5 review: the v0.5.5 test builds bound [Chat] NgKickAt with default 2 (now 3). A file one of them wrote
        /// still has "# Default value: 2" right above "NgKickAt = 2"; a 2 the host sets under this version sits under
        /// "# Default value: 3" and is kept. Read as text before any Bind() (every save rewrites those comments); the
        /// file is only scanned for that one entry, nothing of it is logged. Never throws.
        /// </summary>
        private static bool NgKickAtFromTestBuild(ConfigFile cfg)
        {
            try
            {
                string path = cfg.ConfigFilePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                bool inChat = false;
                string defaultLine = null;
                foreach (string raw in File.ReadLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line[0] == '[') { inChat = line == "[Chat]"; defaultLine = null; continue; }
                    if (!inChat) continue;
                    if (line[0] == '#')
                    {
                        if (line.StartsWith("# Default value:", StringComparison.Ordinal)) defaultLine = line;
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq > 0 && line.Substring(0, eq).Trim() == "NgKickAt")
                        return line.Substring(eq + 1).Trim() == "2" && defaultLine == "# Default value: 2";
                    defaultLine = null;   // the comment block belonged to this other entry
                }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>The config was copied from jp.hostroles.mod.cfg in this run (rename migration).</summary>
        private static bool _migrated;

        public static void Init(ConfigFile cfg)
        {
            _cfg = cfg;
            _migrated = MigrateLegacyConfig(cfg);
            bool ngKickAtFromTestBuild = NgKickAtFromTestBuild(cfg);   // before the first Bind() saves the file
            OptInUpgrade.Scan(cfg);                                    // same reason: the first save rewrites every "# Default value:" line
            ReadLanguageBeforeBind(cfg);   // v0.5.5: the one-time Language = ja → auto migration needs the file as the old version wrote it
            cfg.SaveOnConfigSet = true;

            _enabled = cfg.Bind("General", "Enabled", true, "Enable PocketRoles (host only). Can be toggled in the lobby with /mod on|off");
            _language = cfg.Bind("General", "Language", LangCore.Auto, new ConfigDescription("Default language for player-facing text: auto (v0.5.5 default: follow the game's own language; Simplified / Traditional Chinese → zh, Japanese → ja, English and the others → en), ja (Japanese), zh (Simplified Chinese) or en (English). Players can pick their own with /lang; texts are editable in BepInEx/PocketRoles/lang/*.json", new AcceptableValueList<string>(LangCore.Auto, "ja", "zh", "en")));
            _register = cfg.Bind("General", "RegisterAsModdedLobby", true,
                "Mod-lobby registration (the so-called +25, the host-authority protocol flag): register the lobby as modded when hosting. REQUIRED by Innersloth's Among Us Mod Policy (2026-07-30) for any mod that changes gameplay / roles on official servers. " +
                "Turning this off is a policy violation, disables the private /cmd command channel and exposes the host to the full server anti-cheat. Registered lobbies do not appear in the vanilla public lobby list (players join by room code or through the guide room).");
            _ignoreVersion = cfg.Bind("General", "IgnoreVersionMismatch", false,
                "Keep the mod active even when the game version differs from the one PocketRoles was built for (" + PocketRolesPlugin.SupportedGameVersion + "). Off = the mod stays inert on a mismatch (safe default)");
            _welcome = cfg.Bind("Chat", "WelcomeMessage", true, "Send a private notice to every player who joins explaining that this lobby uses a host-side role mod");
            _roleInfoAtMeeting = cfg.Bind("Chat", "RoleInfoAtMeeting", true, "Re-send each player's role description privately at the start of every meeting");
            _welcomeText = cfg.Bind("Chat", "WelcomeText", "",
                "Custom welcome text sent to joining players (empty = built-in text). \\n = line break; placeholders: {rules} {roles} {settings} {help} {version}. The mandatory mod notice line is always prepended");
            _welcomeIncludeSettings = cfg.Bind("Chat", "WelcomeIncludeSettings", false, "Append the current role settings to the welcome message (off by default: the welcome stays short, the settings summary is always available with /cmd s)");
            _compatWelcomeInterval = cfg.Bind("Chat", "CompatWelcomeInterval", 60f, new ConfigDescription("Unregistered (compat) lobby: the welcome is ONE public message for everyone, so it is sent at most once per this many seconds no matter how many players join in between (a full public lobby gets a join every few seconds; 12 welcomes a minute drove players out on 2026-09-09). 0 = every join", new AcceptableValueRange<float>(0f, 600f)));
            _revealToAll = cfg.Bind("Roles", "RevealRoleToAll", true, "Where the 'X was ROLE' lines of RevealRoleOnDeath go: true = everyone (public chat), false = the host's own screen only (v0.5.2; the players' suggestion for a vanilla room)");
            _revealOnLeave = cfg.Bind("Roles", "RevealRoleOnLeave", true, "Show the host (own screen only) the role of a player who leaves during a game ('X left; they were Sheriff'). Works in registered and unregistered lobbies");
            _revealLeaveToAll = cfg.Bind("Roles", "RevealLeaveToAll", false, "Also announce a leaving player's role to everyone (like RevealRoleOnDeath; when it happens inside a meeting the line is sent after the exile screen)");
            _revealOnDeath = cfg.Bind("Roles", "RevealRoleOnDeath", false, "Announce a player's role to everyone when they are killed or ejected ('X was Sheriff'; the vanilla role's name when there is no PocketRoles role, e.g. in an unregistered lobby)");
            _hostGhostRoleList = cfg.Bind("Roles", "HostGhostRoleList", true, "Once the host is dead, list every player's role (alive / dead) on the HOST's screen only: at the host's death, again at every meeting, plus one line per later death. Works in unregistered (compat) lobbies too (vanilla roles). Never sent to other players; /who shows it on demand");
            _compatWelcomeText = cfg.Bind("Chat", "CompatWelcomeText", "", "Unregistered (compat) lobby only: your own one-line public welcome for every joiner (empty = built-in line 'ようこそ! 普通のAmong Usです。本来の役職は部屋の設定どおり、MODの追加役職はなし…'). One chat message, at most 86 characters; characters a vanilla player cannot type ([ ] < > full-width ！（） etc.) are converted or dropped automatically");
            _discordWebhookUrl = cfg.Bind("Discord", "WebhookUrl", "", "Discord webhook URL (channel settings → 連携サービス → ウェブフック → URL をコピー). When set, the host posts one message per lobby ('部屋コード ABCDEF — 3/15人 募集中') and edits it as players join / leave and games start / end. No bot needed. Keep this URL private (anyone with it can post to the channel). Not editable from chat");
            _discordAnnounce = cfg.Bind("Discord", "Announce", true, "Post / update the lobby line on Discord when WebhookUrl is set");
            _discordText = cfg.Bind("Discord", "Text", "", "Your own lobby line (empty = built-in). Placeholders: {code} {count} {max} {state} {kind}; \\n = line break; Discord markdown works (**bold**, @here)");
            _discordAvatarUrl = cfg.Bind("Discord", "AvatarUrl", DefaultDiscordAvatarUrl, "v0.5.5: icon shown with the lobby message (sent as the webhook's avatar_url when the message is posted; edits keep it). https:// image URL, at most 512 characters. Empty = the icon set for the webhook in Discord");
            _wireLog = cfg.Bind("Diagnostics", "WireLog", false, "Investigation aid: log every packet this client sends (InnerNetClient.SendOrDisconnect) and receives (HandleMessage), decoded one level (GameData / GameDataTo -> Data / RPC / Spawn ...), plus every disconnect, to LogOutput.log. Off (default) = no effect");
            _antiCheatKick = cfg.Bind("AntiCheat", "KickOnForgedRpc", false, "Reserved, currently no effect: forged host-only RPCs (SetRole/SetName/MurderPlayer/...) are always dropped and logged, but the sender of a relayed RPC cannot be identified, so nobody is kicked");

            _cheatDetect = cfg.Bind("AntiCheat", "Detect", true, "v0.5.3: in unregistered lobbies, detect actions a vanilla client never produces (kill / vent / ability / task by a role that cannot, alive chat outside meetings, crew sabotage, kills faster than the cooldown or from too far, unknown RPC ids) and show them on the host's screen (/ac lists them)");
            _cheatAutoKick = cfg.Bind("AntiCheat", "AutoKick", true, "v0.5.3: remove (with a ban for this room) a player on the first CERTAIN detection (kill / vent / ability / task by a role that cannot) or on the second alive chat outside a meeting. VIP and above are never removed automatically. Note: the sender of a relayed message cannot be proven, so a spoofing cheater could in theory frame someone");
            _cheatCallout = cfg.Bind("AntiCheat", "Callout", true, "v0.5.3: in unregistered lobbies, tell the host (screen only, never a kick) when a living crewmate names in a meeting impostors nobody could know yet (not the host, no kill / vent / shapeshift / vanish yet, not named first by someone else) - a trace of a role-seeing cheat. v0.5.5: also a living crewmate who, over 2+ games, keeps voting for such impostors in meetings before any impostor action. Held until the host is dead or the game is over while the host is a living crewmate (it names impostors)");
            _cheatAnnounceKick = cfg.Bind("AntiCheat", "AnnounceKick", true, "v0.5.3: when the anti-cheat removes a player, tell everyone in one public line (who and why)");
            _cheatEndGame = cfg.Bind("AntiCheat", "EndGameOnCheat", true, "v0.5.5: in unregistered lobbies, when Aegis removes a player during a match for a CERTAIN detection whose action changed the game for everyone (a kill that landed, a vent entry, a shapeshift / vanish / appear by a role that cannot), end that match at once for everyone (like Vanguard): the same end as the F7 haison (vanilla 'Impostor disconnected' screen with a winner that means nothing; the host returns to this lobby by itself, the others when they press Play Again) and, once the players are back in the lobby, tell everyone why in one public line without a name, sent once more for the players who came back after it (instead of the named AnnounceKick line, and even with AnnounceKick = false; the vanilla ban notice still shows the name). Waits for the intro and for the vote result / exile screens to finish; when the match cannot be ended (no safe moment within 30 s, the end not sent or not confirmed) or this is turned off meanwhile, the removal is announced as without this setting. Never for detections that could be lag (Repeat / Notice) or for actions that changed nothing (an impostor completing a task, a request only the host sees, a kill that did not land), at most once per match and 3 times an hour (a rehosted lobby keeps the count), never in registered lobbies (host authority refuses such actions there), never for VIP and above (not removed). Needs AutoKick = true. The definitions file can turn it off per rule ([rules] endgame = off). Note: like AutoKick, a detection trusts the owner of the object an action came through; a spoofing cheater could in theory frame someone, get them removed and end a match, and stays in the room (the caps above bound the stops, not the removals)");
            _cheatRemoteRules = cfg.Bind("AntiCheat", "RemoteRules", true, "v0.5.5: take the numbers the in-game Aegis judges with (chat flood count, speed multipliers, repeat window ...) and the rule levels from the [rules] section of the Aegis definitions file on GitHub (main/aegis/definitions.txt; the last one applied is kept in BepInEx/PocketRoles/aegis-rules-cache.txt), so false positives and loopholes are fixed without a mod update. Rule levels can only be made more lenient (a file never turns a notice-only rule into a removal); the numbers can move either way, stricter too, but only within a fixed range each (e.g. speed.kick 1.6 to 6, chatflood.count 3 to 20). A [rules] line may end with the PocketRoles / Among Us versions it is for (e.g. \"@mod<=0.5.5\", or \"@mod>=0.5.6, game>=2026.9.1\" for both: one @, conditions joined by commas). /ac rules shows the values in use. false = built-in numbers and NG words, no shared bans; but level changes that make a rule stop removing players (notice or off), the required (minimum) PocketRoles version ([update] minmod: below it no room can be created) and the [erase] list (requests to erase a player's records) still apply: the signed file is still downloaded, verified and cached for these");
            _cheatRemoteRules.SettingChanged += (_, __) => Net.AegisRules.OnRemoteRulesChanged();   // /opt, settings tab, /reload, /restore
            _cheatJitter = cfg.Bind("AntiCheat", "Jitter", 10, new ConfigDescription("v0.5.5: move the in-game Aegis limits (speed.kick, chat flood count / seconds, colour changes, vent and kill distance, kill cooldown, task burst, repeat gap / window) up or down at random by at most this many percent, drawn anew for every lobby from the OS cryptographic random source (not from the lobby code), so a cheater cannot ride just under the published values of the definitions file. Applied to the file's values, then clamped to each key's allowed range; speed.notice stays below speed.kick. The limits that can remove a player (speed.kick, chat flood, repeat gap / window) move at most half this percentage toward stricter, and speed.kick only moves its margin above 1x, so no lobby cuts the room a laggy player has by more than half this percentage. Never varied: repeat.count, callout.game / callout.lobby (small whole numbers) and the lag filters speed.snap, speed.window and chatalive.grace. The drawn values go to the host's log (one line per lobby) and /ac rules lobby only. 0 = off (the file's values exactly)", new AcceptableValueRange<int>(0, Net.AegisRules.MaxJitterPercent)));
            _cheatJitter.SettingChanged += (_, __) => Net.AegisRules.OnJitterChanged();   // /opt, settings tab, /reload, /restore
            _cheatSharedBans = cfg.Bind("AntiCheat", "SharedBans", true, "v0.5.5: remove a joining player who is on the shared ban list of the signed Aegis definitions file ([bans] section on GitHub, needs RemoteRules = true): that player is told they cannot join (privately; in an unregistered lobby one public line without a name) and kicked 30 s later, at once on an Aegis detection, an NG word, a chat flood or a game start (one who comes back to the same lobby: removed at once with a ban for this room); the others then read one line without a name. Players are listed only as a salted SHA-256 of their PUID (not guessable back, unlike a friend code), never by name. VIP and above are never removed. Your own bans (BepInEx/PocketRoles/aegis-bans.json) apply whatever this says");
            _cheatAutoReport = cfg.Bind("AntiCheat", "AutoReport", false, "v0.5.5: when Aegis removes a player for a CERTAIN detection (kill / vent / ability / task by a role that cannot), also send Among Us's own player report (Cheating / Hacking) for them, before the removal. OFF by default (v0.5.5: nobody is reported to Among Us unless the host turns this on): turn it on with /opt anticheat.autoreport on, or in the settings tab. When on: at most once per player every 30 days and 5 reports an hour. Single reports are always available with /aegis report <name> [cheat|chat|harass|name], whatever this says. Every report is logged and marked in aegis-bans.json. Note: like AutoKick, a detection trusts the owner of the object an action came through; a spoofing cheater could in theory frame someone, and a report cannot be taken back");
            _cheatBanLadder = cfg.Bind("AntiCheat", "BanLadder", true, "v0.5.5: a removal for a CERTAIN detection also records a ban in BepInEx/PocketRoles/aegis-bans.json (applied in every lobby you host): 30 days for the first offence, 180 days for the second, permanent from the third (unbanning keeps the count; the count is forgotten a year after the last ban ends). An evidence record goes to BepInEx/PocketRoles/evidence/<id>.json. false = a ban for that room only (as before v0.5.5). /aegis bans lists them, /aegis unban lifts one. Note: like AutoKick, a detection trusts the owner of the object an action came through; a spoofing cheater could in theory frame someone");
            _autoRehost = cfg.Bind("Lobby", "AutoRehost", false, "Automatically create a new lobby after an unexpected disconnect (server error, timeout) while hosting");
            _autoPublic = cfg.Bind("Lobby", "AutoPublic", false, "Automatically make the lobby public a few seconds after it is created / re-hosted");
            _autoPublicDelay = cfg.Bind("Lobby", "AutoPublicDelay", 3, new ConfigDescription("Seconds to wait before making the lobby public", new AcceptableValueRange<int>(0, 60)));
            _rehostMaxAttempts = cfg.Bind("Lobby", "RehostMaxAttempts", 3, new ConfigDescription("Give up auto re-hosting after this many consecutive attempts", new AcceptableValueRange<int>(1, 10)));
            _afkKickMinutes = cfg.Bind("Lobby", "AfkKickMinutes", 0, new ConfigDescription("Kick (not ban) a lobby player who neither moves nor chats for this many minutes; one warning 30 s before. Host, VIPs, moderators and admins are exempt; nothing happens during the start countdown or a game. Works in unregistered lobbies too. 0 = off", new AcceptableValueRange<int>(0, 30)));
            _maxHostPing = cfg.Bind("Lobby", "PingRecreateLimit", 0, new ConfigDescription("v0.5.5 (was HostPingLimit, on at 80 in v0.5.4; MaxHostPing before that): re-create the lobby (same settings) while it is still empty when the round trip to the game server measured as the lobby is created (v0.5.5: the wire RTT of HostGame / JoinGame, not client.Ping) is above this many ms and nobody has joined 5 s later (official regions mix near and far servers). The host is asked on screen first (Yes/No, once per lobby, or /rehost yes|no); with no answer for 15 s the lobby is re-created. At most 3 re-creations in a row, then the lobby is kept (short-lived lobbies can count as deliberate disconnects). 0 = off, the default (/opt maxping <ms>)", new AcceptableValueRange<int>(0, 300)));
            MigratePingLimit(cfg);
            _compatCommonTasks = cfg.Bind("Compat", "CommonTasks", 0, new ConfigDescription("Unregistered lobby: common tasks actually handed out per player (0 = the lobby setting; the synced setting stays inside the vanilla range)", new AcceptableValueRange<int>(0, 60)));
            _compatShortTasks = cfg.Bind("Compat", "ShortTasks", 0, new ConfigDescription("Unregistered lobby: short tasks actually handed out per player (0 = the lobby setting)", new AcceptableValueRange<int>(0, 60)));
            _compatLongTasks = cfg.Bind("Compat", "LongTasks", 0, new ConfigDescription("Unregistered lobby: long tasks actually handed out per player (0 = the lobby setting)", new AcceptableValueRange<int>(0, 60)));
            _hostShieldKills = cfg.Bind("Host", "ShieldKills", 0, new ConfigDescription("Registered lobby only: kill attempts on the host that fail before the host dies (0 = off). No effect unless [Host] ShieldKey is the right phrase (/opt host.shieldkey <phrase>)", new AcceptableValueRange<int>(0, 9)));
            _hostShieldKey = cfg.Bind("Host", "ShieldKey", "", "Phrase that enables [Host] ShieldKills (only its hash is in the mod)");
            _hostShieldKey.SettingChanged += (_, __) => _hostShieldUnlocked = null;   // /opt, /reload, /restore, file edits
            _vanGaUses = cfg.Bind("Vanilla", "GuardianAngelUses", 0, new ConfigDescription("Registered lobby only: how many times each Guardian Angel may protect per game (0 = vanilla, unlimited). Unregistered lobby: raise the cooldown instead (/vset gacd 600)", new AcceptableValueRange<int>(0, 9)));

            _creditAuthor = cfg.Bind("Credits", "Author", "もみじちゃ", "Name shown in the lobby credits line (empty = none)");
            _creditRepoUrl = cfg.Bind("Credits", "RepoUrl", "https://github.com/wakayamachannel/PocketRoles", "Repository / homepage URL shown in the credits line (empty = none)");
            _showCredits = cfg.Bind("Credits", "ShowInMenu", true, "Show the PocketRoles credits line in the menu");
            _vanillaRoles = cfg.Bind("Roles", "VanillaRoles", false, "Also hand out the vanilla special roles (Scientist, Engineer, Shapeshifter, Noisemaker, Phantom, Tracker, Detective, Viper, Judge) in role games, as set in the vanilla role settings. Off (default) = only Crewmates and Impostors, from which the PocketRoles roles are drawn (the vanilla role rates are zeroed for the assignment only)");
            if (_migrated)
            {
                // The v0.2 file shipped with empty [Credits] values; the copied file must not hide the new defaults.
                try
                {
                    if (string.IsNullOrWhiteSpace(_creditAuthor.Value)) _creditAuthor.Value = (string)_creditAuthor.DefaultValue;
                    if (string.IsNullOrWhiteSpace(_creditRepoUrl.Value)) _creditRepoUrl.Value = (string)_creditRepoUrl.DefaultValue;
                    PocketRolesPlugin.Logger?.LogInfo("Config migration: applied the new [Credits] defaults");
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger?.LogWarning($"Config migration: [Credits] defaults not applied: {e.Message}");
                }
            }

            // ---- v0.3 cosmetics (only the host's own screen; nothing is transmitted)
            _cosEnabled = cfg.Bind("Cosmetics", "Enabled", true, "Enable the host-only cosmetics (custom hats/visors/nameplates, lobby music, lobby decor, menu background, cursor) loaded from BepInEx/PocketRoles/. Other players never see them");
            // Off (settings tab, /opt cos.enabled off or the gear menu alike): the overrides already written into the
            // loaded hat/visor/nameplate view data must be undone now; nothing re-touches them once Enabled is false.
            _cosEnabled.SettingChanged += (_, __) =>
            {
                if (_cosEnabled.Value) return;
                try { PocketRoles.Cosmetics.CosmeticOverrides.Reset(); }
                catch (Exception e) { PocketRolesPlugin.Logger?.LogError($"Options: cos.enabled off: {e}"); }
            };
            _cosMusic = cfg.Bind("Cosmetics", "LobbyMusic", "custom", new ConfigDescription("Lobby music: custom (play a file from BepInEx/PocketRoles/music when present, else vanilla), vanilla (game theme), mute (no lobby music)", new AcceptableValueList<string>(LobbyMusicChoices)));
            _cosMusicFile = cfg.Bind("Cosmetics", "LobbyMusicFile", "", "File name inside BepInEx/PocketRoles/music to play (WAV or OGG; empty = the first file found)");
            _cosMusicVolume = cfg.Bind("Cosmetics", "LobbyMusicVolume", 0.07f, new ConfigDescription("Gain applied to the custom lobby music (0..1). The vanilla theme plays at about 0.07, so normalised tracks need a low value", new AcceptableValueRange<float>(0f, 1f)));
            _cosLobbyPaint = cfg.Bind("Cosmetics", "LobbyPaint", true, "Show BepInEx/PocketRoles/images/lobbypaint.png on the lobby wall (host screen only)");
            _cosDropship = cfg.Bind("Cosmetics", "Dropship", true, "Show BepInEx/PocketRoles/images/dropship.png as a dropship decoration (host screen only)");
            _cosMenuBackground = cfg.Bind("Cosmetics", "MenuBackground", true, "Replace the main-menu background with BepInEx/PocketRoles/images/menu.png when it exists");
            _cosCursor = cfg.Bind("Cosmetics", "Cursor", true, "Use BepInEx/PocketRoles/images/cursor.png as the mouse cursor when it exists");

            // ---- v0.4 lobby tools
            _autoStart = cfg.Bind("Lobby", "AutoStart", false, "Start the game automatically once AutoStartPlayers players are in the lobby (/autostart on|off|<N>)");
            _autoStartPlayers = cfg.Bind("Lobby", "AutoStartPlayers", 10, new ConfigDescription("Player count that triggers the automatic start", new AcceptableValueRange<int>(4, 15)));
            _autoStartCountdown = cfg.Bind("Lobby", "AutoStartCountdown", 5, new ConfigDescription("Seconds of countdown before an automatic / forced start", new AcceptableValueRange<int>(1, 30)));
            _timerWarnAt = cfg.Bind("Lobby", "TimerWarnAt", 60, new ConfigDescription("Seconds of lobby time left at which the mod acts (auto-start when enough players, otherwise the TimerMode action)", new AcceptableValueRange<int>(30, 300)));
            _extendNoticeDelay = cfg.Bind("Lobby", "ExtendNoticeDelay", 5, new ConfigDescription("Seconds between the 'lobby time is running out' notice and the extension / haison", new AcceptableValueRange<int>(0, 60)));
            _timerMode = cfg.Bind("Lobby", "TimerMode", "extend", new ConfigDescription("What to do when the lobby timer is about to expire: extend (request the server extension, fall back to haison), haison (start and end a game immediately so everyone stays in the same lobby), notify (only tell the players)", new AcceptableValueList<string>(TimerModeChoices)));
            _autoRegion = cfg.Bind("Lobby", "AutoRegion", false, "When the CREATE GAME screen opens, ping the official regions and select the one with the lowest latency (/region shows the table). v0.5.5: only on that screen - opening the online menu, 'find game' or joining a room by its code never changes your region. The ping run takes a few seconds: press Create before it ends and that room keeps the current region, the switch then happens the next time the screen opens");
            _enableDleks = cfg.Bind("Lobby", "EnableDleks", true, "Offer the mirrored Skeld (Dleks) in the lobby map picker. Vanilla clients ship the map and can play it");

            // ---- v0.4 Game Master + hotkeys
            _gameMaster = cfg.Bind("General", "GameMaster", false, "Game Master mode: the host gets no role, dies at the start of every game and only watches / moderates (chat and map stay usable)");
            _hotkeysEnabled = cfg.Bind("Hotkeys", "Enabled", true, "Enable the host hotkeys (Haison / EndMeeting / CancelStart). Ignored while typing in chat");
            _hotkeyHaison = cfg.Bind("Hotkeys", "Haison", "F7", "Key (UnityEngine.KeyCode name) that ends the current game as haison; press twice within 3 seconds");
            _hotkeyEndMeeting = cfg.Bind("Hotkeys", "EndMeeting", "F8", "Key (UnityEngine.KeyCode name) that force-ends the current meeting; press twice within 3 seconds");
            _hotkeyCancelStart = cfg.Bind("Hotkeys", "CancelStart", "F9", "Key (UnityEngine.KeyCode name) that cancels the start countdown in the lobby");

            // ---- v0.4 chat
            _playerCommands = cfg.Bind("Chat", "PlayerCommands", true, "Allow non-host players to use chat commands (/help, /roles, /lang ...). Off = their commands are ignored (one notice)");
            _allCommands = cfg.Bind("Chat", "AllCommands", true, "Allow chat commands at all. Off = only /mod on works for the host");
            _rulesMode = cfg.Bind("Chat", "RulesMode", "none", new ConfigDescription("Rules line in the welcome message: none (built-in 'no special rules' text) or custom (RulesText, /rules <text>)", new AcceptableValueList<string>(RulesModeChoices)));
            _rulesText = cfg.Bind("Chat", "RulesText", "", "Custom rules text used by the {rules} placeholder when RulesMode = custom (\\n = line break)");
            _welcomeAllLanguages = cfg.Bind("Chat", "WelcomeAllLanguages", true, "Send the short welcome (2 lines) to every joining player in the player's language first, then in the two other languages (paced). Off = the player's language only plus one compact trilingual /lang line");
            // ---- v0.5.5 NG words (request 9/21 "言動の対策もある？" → "NGは繰り返したら自動退出でできない？")
            _ngFilter = cfg.Bind("Chat", "NgFilter", true, "v0.5.5: check every typed chat line of the other players (lobby and game, registered and unregistered lobbies, alive or dead; not quick chat, not commands) against the NG word list: the built-in list, or the [ngwords] / [ngallow] sections of the Aegis definitions file on GitHub when it has them, plus your own BepInEx/PocketRoles/NgWords.txt. Every hit before NgKickAt gets a public warning (with the hits left), NgKickAt hits a removal. Host, VIPs, moderators and admins are exempt. /ng shows the state and edits your own words");
            _ngKickAt = cfg.Bind("Chat", "NgKickAt", 3, new ConfigDescription("v0.5.5: remove a player at this many NG-word hits in one lobby (hits less than 5 s after the previous counted one are the same outburst and count once). 0 = never remove (public warnings and host notices only). Removals stop for the rest of a lobby once 3 different players hit within 120 s (a sign of a wrong list)", new AcceptableValueRange<int>(0, 5)));
            if (ngKickAtFromTestBuild && _ngKickAt.Value == 2)
            {
                _ngKickAt.Value = 3;
                PocketRolesPlugin.Logger?.LogInfo("Config migration: [Chat] NgKickAt 2 (the default of the v0.5.5 test builds) → 3 (the new default)");
            }
            _ngBan = cfg.Bind("Chat", "NgBan", true, "v0.5.5: the NG-word removal also bans the player from this lobby (false = a plain kick; they can rejoin)");
            _ngAnnounce = cfg.Bind("Chat", "NgAnnounce", true, "v0.5.5: when a player is removed for NG words, tell everyone in one public line");

            // ---- v0.4b chat translation (chat text is sent to Google / DeepL; the DeepL key is read from BepInEx/PocketRoles/deepl-key.txt and never written here)
            _trEnabled = cfg.Bind("Translate", "Enabled", false, "Translate foreign-language chat (combined mode: broadcast in the host's language + private translation per player). OFF by default (v0.5.5: no chat text leaves this PC unless the host turns this on): turn it on with /opt translate.enabled on, or in the settings tab. When on, the chat text of every player is sent to the translation provider (Google, or DeepL when BepInEx/PocketRoles/deepl-key.txt holds a key); turn it off again with /opt translate off");
            // v0.5.5: the default is OFF, so "turn it on while players are already in the room" is now the normal way
            // in. The "chat is translated" notice only rides along with the welcome, which the players who are
            // already here will never see again, so tell the room at the moment it is switched on (README chapter 13
            // and the FAQ promise the room is told while it runs). Turning it off re-arms the host-screen notice.
            _trEnabled.SettingChanged += (_, __) => PocketRoles.Chat.Chat.OnTranslationEnabledChanged();   // /opt, settings tab, /reload, /restore
            _trOffNotice = cfg.Bind("Translate", "OffNotice", true, "v0.5.5: while chat translation is off, put one line on the HOST's own screen (nobody else sees it) in the first lobby of each game session, saying it is off and which command turns it on. false = never show it (/opt translate.notice off). It is shown again after you turn translation on and off again");
            _trProvider = cfg.Bind("Translate", "Provider", "auto", new ConfigDescription("Translation provider: auto (DeepL when BepInEx/PocketRoles/" + DeepLKeyFileName + " contains an API key, else Google), google (public endpoint, no key), deepl (needs the key file). The key itself is never stored in this file", new AcceptableValueList<string>(TranslateProviderChoices)));
            _trTargetLang = cfg.Bind("Translate", "TargetLang", "", "Language the host reads translations in: ja, zh or en. Empty = same as [General] Language");
            _trShowOnHost = cfg.Bind("Translate", "ShowOnHost", true, "Show translations of foreign-language chat on the host's screen (local, nothing is sent)");
            _trBroadcastToAll = cfg.Bind("Translate", "BroadcastToAll", true, "Send the translation (into the host's language) to every player as a chat message (paced); players who chose another language still get their private translation when TranslateForPlayers is on (併用, the user's chosen default 2026-09-08)");
            _trForeignInCompat = cfg.Bind("Translate", "ForeignInCompat", true, "v0.5.3: in an unregistered lobby (no private messages), translate the chat into the language of each foreign-language player in the room (/lang or auto-detect) and post it as one public line per language - only while such a player is here");
            _trForPlayers = cfg.Bind("Translate", "TranslateForPlayers", true, "Translate chat into each foreign player's /lang language and send it to them privately (only players who chose a non-Japanese language)");
            _trAutoDetect = cfg.Bind("Translate", "AutoDetectLang", true, "When a player who has not used /lang writes in Chinese or English, switch their display language automatically (once) and tell them");
            _trMinChars = cfg.Bind("Translate", "MinChars", 3, new ConfigDescription("Messages shorter than this are not translated", new AcceptableValueRange<int>(1, 50)));
            _trMaxPerMinute = cfg.Bind("Translate", "MaxPerMinute", 20, new ConfigDescription("Global cap of translation requests per minute; messages beyond it are dropped", new AcceptableValueRange<int>(1, 120)));

            // ---- v0.4b permissions (Admin.txt / Moderator.txt / VIP.txt under BepInEx/PocketRoles)
            _permAdminSettings = cfg.Bind("Permissions", "AdminsCanChangeSettings", true, "Players listed in Admin.txt may use the host commands (/set /opt /show /start /cancel /autostart /welcome /rules /kick)");
            _permModKick = cfg.Bind("Permissions", "ModeratorsCanKick", true, "Players listed in Moderator.txt may use /kick and /ban");
            _permAdminLobby = cfg.Bind("Permissions", "AdminLobbyControl", false, "Admins may also run /start, /cancel, /autostart and /vset and change the lobby timer / auto-start / vanilla-range keys with /opt (off = admins only change roles, welcome / rules text and the moderator / VIP / ban lists; the host keeps everything that can break the lobby)");
            _permVipMarker = cfg.Bind("Permissions", "VipMarker", true, "Show a star marker next to the name of players listed in VIP.txt and greet them personally");

            // ---- v0.4b vanilla extended ranges (settings screen + /vset); the values reach vanilla clients through the normal settings sync
            _vanExtendedRanges = cfg.Bind("Vanilla", "ExtendedRanges", true, "Allow the vanilla numeric settings (kill cooldown, voting / discussion time, emergency cooldown, task counts) outside their vanilla limits using the ranges below");
            _vanKillMin = cfg.Bind("Vanilla", "KillCooldownMin", 0f, new ConfigDescription("Lowest kill cooldown (seconds) offered by the settings screen (vanilla: 10)", new AcceptableValueRange<float>(0f, 60f)));
            _vanKillMax = cfg.Bind("Vanilla", "KillCooldownMax", 120f, new ConfigDescription("Highest kill cooldown (seconds) offered by the settings screen (vanilla: 60)", new AcceptableValueRange<float>(10f, 600f)));
            _vanKillStep = cfg.Bind("Vanilla", "KillCooldownStep", 2.5f, new ConfigDescription("Step (seconds) of the kill-cooldown arrows in the settings screen (vanilla: 2.5; use /vset for finer values)", new AcceptableValueRange<float>(0.5f, 10f)));
            _vanVoteMin = cfg.Bind("Vanilla", "VotingTimeMin", 0, new ConfigDescription("Lowest voting time (seconds) offered by the settings screen (vanilla: 15; 0 = no voting phase)", new AcceptableValueRange<int>(0, 300)));
            _vanVoteMax = cfg.Bind("Vanilla", "VotingTimeMax", 600, new ConfigDescription("Highest voting time (seconds) offered by the settings screen (vanilla: 300)", new AcceptableValueRange<int>(15, 3600)));
            _vanDiscussMax = cfg.Bind("Vanilla", "DiscussionTimeMax", 600, new ConfigDescription("Highest discussion time (seconds) offered by the settings screen (vanilla: 120)", new AcceptableValueRange<int>(0, 3600)));
            _vanEmergencyMax = cfg.Bind("Vanilla", "EmergencyCooldownMax", 120, new ConfigDescription("Highest emergency-meeting cooldown (seconds) offered by the settings screen (vanilla: 60)", new AcceptableValueRange<int>(0, 600)));
            _vanTaskMax = cfg.Bind("Vanilla", "TaskCountMax", 30, new ConfigDescription("Highest common / short / long task count offered by the settings screen (vanilla: 2 / 5 / 3)", new AcceptableValueRange<int>(1, 60)));
            _vanClampUnreg = cfg.Bind("Vanilla", "ClampInUnregistered", true, "Unregistered (compat) lobby: pull every vanilla numeric setting back into its vanilla range when the lobby is created and offer only vanilla ranges in the settings screen / /vset (precaution against the official server's option validation). false = keep the extended values in unregistered lobbies too (AUR does this for task counts); the host takes the risk of a server disconnect");

            // ---- v0.4e guide room (a second, vanilla, PUBLIC lobby on a sub-phone whose host name / chat carry this room's code)
            _guideShowCodeOverlay = cfg.Bind("Guide", "ShowCodeOverlay", false, "Show the room code large on the host's screen while hosting a lobby (top-left; off by default because vanilla already shows the code at the bottom; /code on turns it on). Hidden in game");
            _guideRoleRoomCode = cfg.Bind("Guide", "RoleRoomCode", "", "Room code of the registered role lobby (mod-lobby registration on) announced by /move from an unregistered 便利ホスト lobby (empty = 'the code is shown in the guide room host name'). /move <CODE> sets it");
            _guideAutoRecreateRegistered = cfg.Bind("Guide", "AutoRecreateRegistered", false, "/move: 30 s after the announcement, re-create the current unregistered lobby as a registered role lobby (mod-lobby registration on; everyone has to rejoin with the new code). Off = announce only");

            foreach (var r in Roles.All)
            {
                int defCount = (r.Id == CustomRole.Sheriff || r.Id == CustomRole.Jester || r.Id == CustomRole.Madmate) ? 1 : 0;
                // Lovers is the only pair role: at most one pair per game.
                int maxCount = r.Id == CustomRole.Lovers ? 1 : 15;
                string countDesc = r.Id == CustomRole.Lovers ? "Lovers pairs per game (0 or 1)" : $"Maximum number of {r.NameEn} ({r.NameJa}) per game";
                _count[r.Id] = cfg.Bind("Roles", r.NameEn.Replace(" ", "") + ".Count", defCount,
                    new ConfigDescription(countDesc, new AcceptableValueRange<int>(0, maxCount)));
                _chance[r.Id] = cfg.Bind("Roles", r.NameEn.Replace(" ", "") + ".Chance", 100,
                    new ConfigDescription($"Chance (%) for each {r.NameEn} slot to be assigned", new AcceptableValueRange<int>(0, 100)));
            }

            _sheriffKillCooldown = cfg.Bind("Sheriff", "KillCooldown", 30f, new ConfigDescription("Sheriff kill cooldown (seconds)", new AcceptableValueRange<float>(2.5f, 180f)));
            _sheriffCanKillMadmate = cfg.Bind("Sheriff", "CanKillMadmate", true, "Sheriff can shoot Mad-type roles (Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper) without dying");
            _jackalKillCooldown = cfg.Bind("Jackal", "KillCooldown", 30f, new ConfigDescription("Jackal kill cooldown (seconds)", new AcceptableValueRange<float>(2.5f, 180f)));
            _jackalCanVent = cfg.Bind("Jackal", "CanVent", true, "Jackal can use vents");
            _vampireKillDelay = cfg.Bind("Vampire", "KillDelay", 10f, new ConfigDescription("Seconds between a bite and the victim's death", new AcceptableValueRange<float>(1f, 60f)));
            _mayorVotes = cfg.Bind("Mayor", "Votes", 2, new ConfigDescription("How many votes the Mayor's vote counts as", new AcceptableValueRange<int>(1, 5)));
            _snitchTasksLeftToWarn = cfg.Bind("Snitch", "TasksLeftToWarn", 1, new ConfigDescription("Killers see the Snitch marked when this many tasks (or fewer) are left", new AcceptableValueRange<int>(0, 10)));
            _lighterVision = cfg.Bind("Lighter", "VisionMultiplier", 2f, new ConfigDescription("Lighter vision multiplier", new AcceptableValueRange<float>(1f, 5f)));
            _speedBoosterSpeed = cfg.Bind("SpeedBooster", "SpeedMultiplier", 1.5f, new ConfigDescription("Speed Booster speed multiplier", new AcceptableValueRange<float>(1f, 3f)));
            _madmateKnownToImpostors = cfg.Bind("Madmate", "KnownToImpostors", false, "Impostors see who the Mad-type players are (red Ⓜ; Ⓦ for the Worshipper; the Mad Mayor has its own switch)");

            // ---- v0.4.1 roles
            _loversAllowImpostor = cfg.Bind("Lovers", "AllowImpostor", true, "The second lover may be a vanilla Impostor (keeps its kill button and counts as an Impostor for the win rules; wins only as a lover)");
            _loversLastThree = cfg.Bind("Lovers", "WinAsLastThree", true, "The Lovers win as soon as both are alive and at most 3 players are alive");
            _arsonistDouseCooldown = cfg.Bind("Arsonist", "DouseCooldown", 10f, new ConfigDescription("Seconds between two douses (the Arsonist's kill button)", new AcceptableValueRange<float>(2.5f, 180f)));
            _arsonistCanVent = cfg.Bind("Arsonist", "CanVent", false, "Arsonist can use vents");
            _witchSpellCooldown = cfg.Bind("Witch", "SpellCooldown", 0f, new ConfigDescription("Seconds between two spells (0 = the lobby's kill cooldown)", new AcceptableValueRange<float>(0f, 180f)));
            _witchSpelledSeeMark = cfg.Bind("Witch", "SpelledSeeMark", false, "Spelled players see a mark on their own name (the Witch always sees it)");
            _assassinGuessesPerMeeting = cfg.Bind("Assassin", "GuessesPerMeeting", 1, new ConfigDescription("Guesses (/cmd guess) per meeting", new AcceptableValueRange<int>(1, 5)));
            _assassinFirstMeeting = cfg.Bind("Assassin", "CanGuessFirstMeeting", true, "The Assassin may guess in the first meeting of the game");

            // ---- v0.5.0 roles
            _madMayorVotes = cfg.Bind("MadMayor", "Votes", 2, new ConfigDescription("How many votes the Mad Mayor's vote counts as", new AcceptableValueRange<int>(1, 5)));
            _madMayorKnownToImpostors = cfg.Bind("MadMayor", "KnownToImpostors", false, "Impostors see who the Mad Mayor is (red Ⓜ before the name)");
            _madStuntmanLives = cfg.Bind("MadStuntman", "Lives", 1, new ConfigDescription("Kill attempts the Mad Stuntman survives before a kill goes through (votes are never blocked)", new AcceptableValueRange<int>(1, 10)));
            _madStuntmanNotify = cfg.Bind("MadStuntman", "NotifyStuntman", false, "Tell the Mad Stuntman in private chat when it survived a kill attempt and how many are left (off = SNR behaviour: the role chat still shows the count at start and every meeting; also reveals absorbed Vampire bites / Witch spells; the killer is always told)");
            _madHawkVision = cfg.Bind("MadHawk", "VisionMultiplier", 3f, new ConfigDescription("Mad Hawk vision multiplier (crew vision x N; a lights sabotage still shrinks it)", new AcceptableValueRange<float>(1f, 5f)));
            _madHawkSpeed = cfg.Bind("MadHawk", "SpeedMultiplier", 1f, new ConfigDescription("Mad Hawk movement speed multiplier (1 = normal; below 1 pays for the wide vision; the result is kept inside the vanilla speed range 0.5-3)", new AcceptableValueRange<float>(0.5f, 1.5f)));
            _worshipperUses = cfg.Bind("Worshipper", "Uses", 1, new ConfigDescription("How many players the Worshipper may turn into Madmates per game (successful worships only)", new AcceptableValueRange<int>(1, 5)));
            _worshipperCooldown = cfg.Bind("Worshipper", "Cooldown", 30f, new ConfigDescription("Seconds between two worships (the Worshipper's kill button)", new AcceptableValueRange<float>(2.5f, 180f)));
            _jackalFriendsKnownToJackal = cfg.Bind("JackalFriends", "KnownToJackal", false, "The Jackal sees who the Jackal Friends are (blue names)");
            _jackalFriendsSheriffCanKill = cfg.Bind("JackalFriends", "SheriffCanKill", true, "The Sheriff can shoot Jackal Friends without dying");
            _nekomataVotersOnly = cfg.Bind("EvilNekomata", "VotersOnly", true, "The dragged player is picked among the players who voted for the Evil Nekomata (false = among every living player)");
            _nekomataExcludeImpostors = cfg.Bind("EvilNekomata", "ExcludeImpostors", true, "Impostor-team players (Impostors, Madmate family, an impostor lover) are never dragged");
            _nekomataAnnounce = cfg.Bind("EvilNekomata", "Announce", true, "Everyone reads who was dragged along after the ejection screen (false = only the victim is told)");
            _serialKillerKillCooldown = cfg.Bind("SerialKiller", "KillCooldown", 10f, new ConfigDescription("Serial Killer kill cooldown (seconds)", new AcceptableValueRange<float>(1f, 60f)));
            _serialKillerSuicideTime = cfg.Bind("SerialKiller", "SuicideTime", 30f, new ConfigDescription("Seconds without a kill before the Serial Killer dies by itself (paused during meetings; never below KillCooldown + 5)", new AcceptableValueRange<float>(10f, 300f)));
            _serialKillerResetAtMeeting = cfg.Bind("SerialKiller", "ResetAtMeeting", true, "The suicide timer restarts after every meeting (off: the remaining time carries over)");
            _samuraiKillCooldown = cfg.Bind("Samurai", "KillCooldown", 45f, new ConfigDescription("Seconds between two slashes (0 = the lobby's kill cooldown; 2.5 or more recommended)", new AcceptableValueRange<float>(0f, 180f)));
            _samuraiRange = cfg.Bind("Samurai", "Range", 2f, new ConfigDescription("Slash radius around the Samurai in map units (vanilla kill distances are roughly short 1 / medium 1.8 / long 2.5)", new AcceptableValueRange<float>(0.5f, 5f)));
            _samuraiStagger = cfg.Bind("Samurai", "Stagger", 0.3f, new ConfigDescription("Seconds between two bystander deaths of one slash (0.3 = the official server's packet spacing)", new AcceptableValueRange<float>(0.1f, 1f)));
            _samuraiHitTeammates = cfg.Bind("Samurai", "HitTeammates", false, "The slash also kills Impostor-team players in range (Impostors, Madmate family, an Impostor lover)");
            _evilHawkVision = cfg.Bind("EvilHawk", "VisionMultiplier", 2f, new ConfigDescription("Evil Hawk vision multiplier, applied to the impostor vision (always on)", new AcceptableValueRange<float>(1f, 5f)));

            BuildDescriptors();

            // v0.5.5: the one-time upgrade check of the two opt-in defaults. It changes no value; see OptInUpgrade.
            // The two handlers must be added AFTER the record is written, so the record keeps the value the file held.
            OptInUpgrade.Apply();
            if (_trEnabled != null) _trEnabled.SettingChanged += (_, __) => OptInUpgrade.OnChanged(true);
            if (_cheatAutoReport != null) _cheatAutoReport.SettingChanged += (_, __) => OptInUpgrade.OnChanged(false);
        }

        public static bool ModEnabled { get => _enabled == null || _enabled.Value; set { if (_enabled != null) _enabled.Value = value; } }
        /// <summary>
        /// Lobby default language in effect: "ja" | "zh" | "en" (see Lang.Current). v0.5.5: [General] Language = auto (the default)
        /// follows the game's own language (<see cref="GameLanguage"/>). Setting a code stores it; setting "auto" goes back to following the game.
        /// </summary>
        public static string Language
        {
            get => LangCore.Resolve(_language == null ? LangCore.Auto : _language.Value, GameLanguage.Code);
            set { if (_language != null) _language.Value = LangCore.IsAuto(value) ? LangCore.Auto : Lang.Normalize(value); }
        }

        /// <summary>Choices of the settings-tab row and the gear menu, in cycle order.</summary>
        internal static readonly string[] LanguageChoices = { LangCore.Auto, "ja", "zh", "en" };

        /// <summary>[General] Language as stored: "auto" | "ja" | "zh" | "en".</summary>
        public static string LanguageSetting => _language == null || LangCore.IsAuto(_language.Value) ? LangCore.Auto : Lang.Normalize(_language.Value);

        /// <summary>"auto(zh)" while following the game, else the code (for /opt and /cmd s lines).</summary>
        public static string LanguageLabel => LanguageSetting == LangCore.Auto ? LangCore.Auto + "(" + Language + ")" : Language;

        // v0.5.5 one-time language migration (see ReadLanguageBeforeBind / ApplyLanguageMigration)
        private static bool _langMigrationOwed, _langMigrationChecked;
        /// <summary>Set when the migration switched Language to auto: the host's next lobby shows one line (GameLanguage_LobbyNoticePatch).</summary>
        internal static bool LanguageMigrationNoticePending;

        /// <summary>
        /// BepInEx/PocketRoles/language-migration.pending: the one-time migration is owed but not decided yet. The first
        /// Bind() rewrites the cfg with "# Default value: auto", so without this marker a first v0.5.5 start that ends
        /// before the main menu (a crash, closing the game, the game's language not known yet) would lose it for good.
        /// </summary>
        private static string LangMigrationMarker
        {
            get
            {
                try
                {
                    string root = BepInEx.Paths.BepInExRootPath;
                    return string.IsNullOrEmpty(root) ? null : Path.Combine(root, "PocketRoles", "language-migration.pending");
                }
                catch (Exception) { return null; }
            }
        }

        /// <summary>
        /// Reads [General] Language and its "# Default value:" line as the previous version left them, before any Bind()
        /// rewrites the file (a pre-v0.5.5 file says "ja" under "# Default value: ja", chosen or not), and keeps the
        /// marker file in step: written while the migration is owed, removed when it is not. Never throws.
        /// </summary>
        private static void ReadLanguageBeforeBind(ConfigFile cfg)
        {
            try
            {
                string path = cfg.ConfigFilePath;
                string marker = LangMigrationMarker;
                bool markerPresent = marker != null && File.Exists(marker);
                string value = null, defaultValue = null;
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) LangCore.ReadCfgLanguage(File.ReadLines(path), out value, out defaultValue);
                _langMigrationOwed = LangCore.LanguageMigrationOwed(value, defaultValue, markerPresent);
                if (marker == null) return;
                if (_langMigrationOwed && !markerPresent)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(marker));
                    File.WriteAllText(marker, "Language = ja (the pre-v0.5.5 default): PocketRoles decides auto / ja once the game's language is known\r\n");
                }
                else if (!_langMigrationOwed && markerPresent) File.Delete(marker);
            }
            catch (Exception) { }
        }

        private static void ClearLangMigrationMarker()
        {
            try
            {
                string marker = LangMigrationMarker;
                if (marker != null && File.Exists(marker)) File.Delete(marker);
            }
            catch (Exception e) { PocketRolesPlugin.Logger?.LogWarning($"Config migration: cannot remove the marker ({e.Message})"); }
        }

        /// <summary>
        /// v0.5.5, once: every older config holds Language = ja (the old default, whatever the host's language), so a
        /// Chinese host got Japanese menus (Discord 2026-09-22). When the file still says ja as the old default and the
        /// game runs in another language, switch to auto and tell the host (their own screen only) at the next lobby.
        /// Called from the main menu, when the game's language is known; a Japanese game keeps ja. Until it has decided,
        /// the marker file keeps the migration owed across starts; after that a ja the host sets is never touched again.
        /// </summary>
        internal static void ApplyLanguageMigration()
        {
            if (_langMigrationChecked || _language == null) return;
            if (!_langMigrationOwed) { _langMigrationChecked = true; return; }
            string game = GameLanguage.Code;
            if (game == null) return;   // not known yet: the next main menu (or the next start: the marker stays) decides
            _langMigrationChecked = true;
            ClearLangMigrationMarker();
            // still ja now (not changed since the start) and the game runs in another language
            if (!LangCore.ShouldMigrateToAuto(_language.Value, LangCore.Ja, game)) return;
            _language.Value = LangCore.Auto;
            LanguageMigrationNoticePending = true;
            PocketRolesPlugin.Logger?.LogInfo($"Config migration: [General] Language ja (the pre-v0.5.5 default) → auto (the game runs in {game}); the host is told at the next lobby");
        }
        public static bool HostAuthorityMode { get => _register == null || _register.Value; set { if (_register != null) _register.Value = value; } }
        public static bool IgnoreVersionMismatch { get => _ignoreVersion != null && _ignoreVersion.Value; set { if (_ignoreVersion != null) _ignoreVersion.Value = value; } }
        public static bool WelcomeMessage { get => _welcome == null || _welcome.Value; set { if (_welcome != null) _welcome.Value = value; } }
        public static bool RoleInfoAtMeeting { get => _roleInfoAtMeeting == null || _roleInfoAtMeeting.Value; set { if (_roleInfoAtMeeting != null) _roleInfoAtMeeting.Value = value; } }
        /// <summary>Custom welcome text ("" = built-in). Raw value: "\n" two-character sequences and {placeholders} are expanded by Chat.</summary>
        public static string WelcomeText { get => _welcomeText == null ? "" : (_welcomeText.Value ?? ""); set { if (_welcomeText != null) _welcomeText.Value = value ?? ""; } }
        /// <summary>[Chat] CompatWelcomeText: custom one-line public welcome of an unregistered lobby ("" = built-in).</summary>
        public static string CompatWelcomeText { get => _compatWelcomeText == null ? "" : (_compatWelcomeText.Value ?? ""); set { if (_compatWelcomeText != null) _compatWelcomeText.Value = value ?? ""; } }
        /// <summary>Append the settings summary to the welcome (off by default; /cmd s shows it on demand).</summary>
        public static bool WelcomeIncludeSettings { get => _welcomeIncludeSettings != null && _welcomeIncludeSettings.Value; set { if (_welcomeIncludeSettings != null) _welcomeIncludeSettings.Value = value; } }
        public static string DiscordWebhookUrl => _discordWebhookUrl == null ? "" : (_discordWebhookUrl.Value ?? "").Trim();
        public static bool DiscordAnnounce { get => _discordAnnounce == null || _discordAnnounce.Value; set { if (_discordAnnounce != null) _discordAnnounce.Value = value; } }
        public static string DiscordText => _discordText == null ? "" : (_discordText.Value ?? "");
        /// <summary>Default of [Discord] AvatarUrl (v0.5.5): the PocketRoles icon in the GitHub repository.</summary>
        public const string DefaultDiscordAvatarUrl = "https://raw.githubusercontent.com/wakayamachannel/PocketRoles/main/assets/PocketRoles-256.png";
        /// <summary>[Discord] AvatarUrl (v0.5.5): icon of a newly posted lobby message ("" = the webhook's own icon). Validated by DiscordWebhook.</summary>
        public static string DiscordAvatarUrl => _discordAvatarUrl == null ? DefaultDiscordAvatarUrl : (_discordAvatarUrl.Value ?? "").Trim();
        /// <summary>[Chat] NgFilter (v0.5.5): Chat.NgWords checks the other players' chat (default true).</summary>
        public static bool NgFilter { get => _ngFilter == null || _ngFilter.Value; set { if (_ngFilter != null) _ngFilter.Value = value; } }
        /// <summary>[Chat] NgKickAt (v0.5.5): NG-word hits in one lobby that remove a player, 1..5; 0 = never (default 3).</summary>
        public static int NgKickAt { get => Math.Max(0, Math.Min(5, _ngKickAt?.Value ?? 3)); set { if (_ngKickAt != null) _ngKickAt.Value = Math.Max(0, Math.Min(5, value)); } }
        /// <summary>[Chat] NgBan (v0.5.5): the NG-word removal bans from this lobby (default true).</summary>
        public static bool NgBan { get => _ngBan == null || _ngBan.Value; set { if (_ngBan != null) _ngBan.Value = value; } }
        /// <summary>[Chat] NgAnnounce (v0.5.5): one public line for an NG-word removal (default true).</summary>
        public static bool NgAnnounce { get => _ngAnnounce == null || _ngAnnounce.Value; set { if (_ngAnnounce != null) _ngAnnounce.Value = value; } }
        public static bool AntiCheatKick { get => _antiCheatKick != null && _antiCheatKick.Value; set { if (_antiCheatKick != null) _antiCheatKick.Value = value; } }
        /// <summary>[AntiCheat] Detect (v0.5.3): CheatDetector on (default true).</summary>
        public static bool CheatDetect { get => _cheatDetect == null || _cheatDetect.Value; set { if (_cheatDetect != null) _cheatDetect.Value = value; } }
        /// <summary>[AntiCheat] AutoKick (v0.5.3): remove on the first certain detection (default true).</summary>
        public static bool CheatAutoKick { get => _cheatAutoKick == null || _cheatAutoKick.Value; set { if (_cheatAutoKick != null) _cheatAutoKick.Value = value; } }
        /// <summary>[AntiCheat] AnnounceKick (v0.5.3): one public line when the anti-cheat removes a player (default true).</summary>
        /// <summary>[AntiCheat] Callout (v0.5.3): CalloutWatch host notice (default true).</summary>
        public static bool CheatCallout { get => _cheatCallout == null || _cheatCallout.Value; set { if (_cheatCallout != null) _cheatCallout.Value = value; } }
        public static bool CheatAnnounceKick { get => _cheatAnnounceKick == null || _cheatAnnounceKick.Value; set { if (_cheatAnnounceKick != null) _cheatAnnounceKick.Value = value; } }
        /// <summary>[AntiCheat] EndGameOnCheat (v0.5.5 AegisMatchStop): end the match of a compat game after a CERTAIN removal whose action changed the game (default true).</summary>
        public static bool CheatEndGame { get => _cheatEndGame == null || _cheatEndGame.Value; set { if (_cheatEndGame != null) _cheatEndGame.Value = value; } }
        /// <summary>[AntiCheat] RemoteRules (v0.5.5): Aegis thresholds / levels from the GitHub definitions file (default true; false = built-in values).</summary>
        public static bool CheatRemoteRules { get => _cheatRemoteRules == null || _cheatRemoteRules.Value; set { if (_cheatRemoteRules != null) _cheatRemoteRules.Value = value; } }
        /// <summary>[AntiCheat] Jitter (v0.5.5): per-lobby variation of the Aegis limits in percent (default 10, 0..20; 0 = off).</summary>
        public static int CheatJitter
        {
            get => _cheatJitter == null ? 10 : Math.Max(0, Math.Min(Net.AegisRules.MaxJitterPercent, _cheatJitter.Value));
            set { if (_cheatJitter != null) _cheatJitter.Value = Math.Max(0, Math.Min(Net.AegisRules.MaxJitterPercent, value)); }
        }
        /// <summary>[AntiCheat] SharedBans (v0.5.5): apply the [bans] list of the signed definitions file at join time (default true).</summary>
        public static bool CheatSharedBans { get => _cheatSharedBans == null || _cheatSharedBans.Value; set { if (_cheatSharedBans != null) _cheatSharedBans.Value = value; } }
        /// <summary>
        /// [AntiCheat] AutoReport (v0.5.5): Among Us's own report (Cheating / Hacking) for a CERTAIN removal.
        /// OFF by default, and OFF whenever the value cannot be read (entry not bound yet / config unreadable):
        /// nobody is ever reported automatically unless a host's own config file says true.
        /// </summary>
        public static bool CheatAutoReport { get => _cheatAutoReport != null && _cheatAutoReport.Value; set { if (_cheatAutoReport != null) _cheatAutoReport.Value = value; } }
        /// <summary>[AntiCheat] BanLadder (v0.5.5): a CERTAIN removal records a local ban, 30 d / 180 d / permanent (default true).</summary>
        public static bool CheatBanLadder { get => _cheatBanLadder == null || _cheatBanLadder.Value; set { if (_cheatBanLadder != null) _cheatBanLadder.Value = value; } }
        /// <summary>[Diagnostics] WireLog: packet-level send/receive trace (Net.WireLog), off by default.</summary>
        public static bool WireLog { get => _wireLog != null && _wireLog.Value; set { if (_wireLog != null) _wireLog.Value = value; } }

        public static bool AutoRehost { get => _autoRehost != null && _autoRehost.Value; set { if (_autoRehost != null) _autoRehost.Value = value; } }
        public static bool AutoPublic { get => _autoPublic != null && _autoPublic.Value; set { if (_autoPublic != null) _autoPublic.Value = value; } }
        /// <summary>Seconds (0..60).</summary>
        public static int AutoPublicDelay { get => _autoPublicDelay?.Value ?? 3; set { if (_autoPublicDelay != null) _autoPublicDelay.Value = Math.Max(0, Math.Min(60, value)); } }
        public static int RehostMaxAttempts { get => _rehostMaxAttempts?.Value ?? 3; set { if (_rehostMaxAttempts != null) _rehostMaxAttempts.Value = Math.Max(1, Math.Min(10, value)); } }
        /// <summary>[Lobby] PingRecreateLimit (v0.5.5; HostPingLimit / MaxHostPing before): ping (ms, 0..300) above which a freshly created, still empty lobby is re-created; 0 = off (default).</summary>
        public static int MaxHostPing { get => _maxHostPing?.Value ?? 0; set { if (_maxHostPing != null) _maxHostPing.Value = Math.Max(0, Math.Min(300, value)); } }
        /// <summary>[Lobby] AfkKickMinutes: kick a lobby player idle (no movement / chat) for this many minutes; 0 = off (default).</summary>
        public static int AfkKickMinutes { get => _afkKickMinutes?.Value ?? 0; set { if (_afkKickMinutes != null) _afkKickMinutes.Value = Math.Max(0, Math.Min(30, value)); } }
        private static ConfigEntry<int> _compatCommonTasks, _compatShortTasks, _compatLongTasks;
        /// <summary>[Compat] CommonTasks / ShortTasks / LongTasks (v0.5.1): tasks handed out in an unregistered lobby beyond the vanilla range (0 = the lobby setting).</summary>
        public static int CompatCommonTasks { get => _compatCommonTasks?.Value ?? 0; set { if (_compatCommonTasks != null) _compatCommonTasks.Value = Math.Max(0, Math.Min(60, value)); } }
        public static int CompatShortTasks { get => _compatShortTasks?.Value ?? 0; set { if (_compatShortTasks != null) _compatShortTasks.Value = Math.Max(0, Math.Min(60, value)); } }
        public static int CompatLongTasks { get => _compatLongTasks?.Value ?? 0; set { if (_compatLongTasks != null) _compatLongTasks.Value = Math.Max(0, Math.Min(60, value)); } }
        /// <summary>v0.5.2: true while [Host] ShieldKey hashes to <see cref="HostShieldKeyHash"/> (cached; /opt host.shieldkey clears the cache).</summary>
        public static bool HostShieldUnlocked
        {
            get
            {
                if (_hostShieldUnlocked == null)
                {
                    try { _hostShieldUnlocked = Sha256Hex((_hostShieldKey?.Value ?? "").Trim()) == HostShieldKeyHash; }
                    catch (Exception) { _hostShieldUnlocked = false; }
                }
                return _hostShieldUnlocked.Value;
            }
        }
        /// <summary>[Host] ShieldKills (v0.5.2): kill attempts on the host absorbed per game; 0 unless unlocked.</summary>
        public static int HostShieldKills => HostShieldUnlocked ? Math.Max(0, Math.Min(9, _hostShieldKills?.Value ?? 0)) : 0;
        /// <summary>[Vanilla] GuardianAngelUses (v0.5.2): protects per Guardian Angel per game in a registered lobby (0 = unlimited).</summary>
        public static int GuardianAngelUses { get => _vanGaUses?.Value ?? 0; set { if (_vanGaUses != null) _vanGaUses.Value = Math.Max(0, Math.Min(9, value)); } }
        private static string Sha256Hex(string s)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s ?? ""));
                var sb = new System.Text.StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static string CreditAuthor { get => _creditAuthor == null ? "" : (_creditAuthor.Value ?? ""); set { if (_creditAuthor != null) _creditAuthor.Value = value ?? ""; } }
        public static string CreditRepoUrl { get => _creditRepoUrl == null ? "" : (_creditRepoUrl.Value ?? ""); set { if (_creditRepoUrl != null) _creditRepoUrl.Value = value ?? ""; } }
        public static bool ShowCredits { get => _showCredits == null || _showCredits.Value; set { if (_showCredits != null) _showCredits.Value = value; } }
        /// <summary>[Roles] VanillaRoles: hand out the vanilla special roles alongside the custom ones (default off = crew/impostor only).</summary>
        public static bool VanillaRolesEnabled { get => _vanillaRoles != null && _vanillaRoles.Value; set { if (_vanillaRoles != null) _vanillaRoles.Value = value; } }

        // ------------------------------------------------------------------ v0.3 [Cosmetics]

        /// <summary>Normalizes a choice string against <paramref name="choices"/> (case-insensitive); unknown → choices[0].</summary>
        private static string NormalizeChoice(string v, string[] choices)
        {
            if (!string.IsNullOrEmpty(v))
            {
                string t = v.Trim().ToLowerInvariant();
                foreach (var c in choices) if (c == t) return c;
            }
            return choices[0];
        }

        private static string GetChoice(ConfigEntry<string> e, string[] choices) => e == null ? choices[0] : NormalizeChoice(e.Value, choices);
        private static void SetChoice(ConfigEntry<string> e, string[] choices, string v) { if (e != null) e.Value = NormalizeChoice(v, choices); }

        public static bool CosmeticsEnabled { get => _cosEnabled == null || _cosEnabled.Value; set { if (_cosEnabled != null) _cosEnabled.Value = value; } }
        /// <summary>"custom" | "vanilla" | "mute" (see <see cref="LobbyMusicChoices"/>).</summary>
        public static string LobbyMusic { get => GetChoice(_cosMusic, LobbyMusicChoices); set => SetChoice(_cosMusic, LobbyMusicChoices, value); }
        /// <summary>File name inside the music folder ("" = first file found).</summary>
        public static string LobbyMusicFile { get => _cosMusicFile == null ? "" : (_cosMusicFile.Value ?? ""); set { if (_cosMusicFile != null) _cosMusicFile.Value = value ?? ""; } }
        /// <summary>0..1 gain for the custom lobby track.</summary>
        public static float LobbyMusicVolume { get => _cosMusicVolume?.Value ?? 0.07f; set { if (_cosMusicVolume != null) _cosMusicVolume.Value = Clamp(value, 0f, 1f); } }
        public static bool LobbyPaint { get => _cosLobbyPaint == null || _cosLobbyPaint.Value; set { if (_cosLobbyPaint != null) _cosLobbyPaint.Value = value; } }
        public static bool Dropship { get => _cosDropship == null || _cosDropship.Value; set { if (_cosDropship != null) _cosDropship.Value = value; } }
        public static bool MenuBackground { get => _cosMenuBackground == null || _cosMenuBackground.Value; set { if (_cosMenuBackground != null) _cosMenuBackground.Value = value; } }
        public static bool CustomCursor { get => _cosCursor == null || _cosCursor.Value; set { if (_cosCursor != null) _cosCursor.Value = value; } }

        // ------------------------------------------------------------------ v0.4 [Lobby]

        public static bool AutoStart { get => _autoStart != null && _autoStart.Value; set { if (_autoStart != null) _autoStart.Value = value; } }
        /// <summary>Players needed for the automatic start (4..15).</summary>
        public static int AutoStartPlayers { get => _autoStartPlayers?.Value ?? 10; set { if (_autoStartPlayers != null) _autoStartPlayers.Value = Math.Max(4, Math.Min(15, value)); } }
        /// <summary>Countdown seconds before an automatic / forced start (1..30).</summary>
        public static int AutoStartCountdown { get => _autoStartCountdown?.Value ?? 5; set { if (_autoStartCountdown != null) _autoStartCountdown.Value = Math.Max(1, Math.Min(30, value)); } }
        /// <summary>Lobby seconds left at which the timer logic acts (30..300).</summary>
        public static int TimerWarnAt { get => _timerWarnAt?.Value ?? 60; set { if (_timerWarnAt != null) _timerWarnAt.Value = Math.Max(30, Math.Min(300, value)); } }
        /// <summary>Seconds between the warning notice and the extension / haison (0..60).</summary>
        public static int ExtendNoticeDelay { get => _extendNoticeDelay?.Value ?? 5; set { if (_extendNoticeDelay != null) _extendNoticeDelay.Value = Math.Max(0, Math.Min(60, value)); } }
        /// <summary>"extend" | "haison" | "notify" (see <see cref="TimerModeChoices"/>).</summary>
        public static string TimerMode { get => GetChoice(_timerMode, TimerModeChoices); set => SetChoice(_timerMode, TimerModeChoices, value); }
        public static bool AutoRegion { get => _autoRegion != null && _autoRegion.Value; set { if (_autoRegion != null) _autoRegion.Value = value; } }
        public static bool EnableDleks { get => _enableDleks == null || _enableDleks.Value; set { if (_enableDleks != null) _enableDleks.Value = value; } }

        // ------------------------------------------------------------------ v0.4 [General] Game Master, [Hotkeys]

        public static bool GameMaster { get => _gameMaster != null && _gameMaster.Value; set { if (_gameMaster != null) _gameMaster.Value = value; } }
        public static bool HotkeysEnabled { get => _hotkeysEnabled == null || _hotkeysEnabled.Value; set { if (_hotkeysEnabled != null) _hotkeysEnabled.Value = value; } }
        /// <summary>UnityEngine.KeyCode name (default "F7"). Parse with <see cref="TryParseKey"/>.</summary>
        public static string HotkeyHaison { get => GetKeyName(_hotkeyHaison, "F7"); set => SetKeyName(_hotkeyHaison, value, "F7"); }
        /// <summary>UnityEngine.KeyCode name (default "F8").</summary>
        public static string HotkeyEndMeeting { get => GetKeyName(_hotkeyEndMeeting, "F8"); set => SetKeyName(_hotkeyEndMeeting, value, "F8"); }
        /// <summary>UnityEngine.KeyCode name (default "F9").</summary>
        public static string HotkeyCancelStart { get => GetKeyName(_hotkeyCancelStart, "F9"); set => SetKeyName(_hotkeyCancelStart, value, "F9"); }

        private static string GetKeyName(ConfigEntry<string> e, string def)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.Value)) return def;
            return TryParseKey(e.Value, out var k) ? k.ToString() : def;
        }

        private static void SetKeyName(ConfigEntry<string> e, string value, string def)
        {
            if (e == null) return;
            e.Value = TryParseKey(value, out var k) ? k.ToString() : def;
        }

        /// <summary>Parses a UnityEngine.KeyCode name (case-insensitive, e.g. "F7", "f7", "Escape", "Space", "LeftShift"). "None"/empty → false.</summary>
        public static bool TryParseKey(string name, out UnityEngine.KeyCode key)
        {
            key = UnityEngine.KeyCode.None;
            if (string.IsNullOrWhiteSpace(name)) return false;
            string t = name.Trim();
            if (!Enum.TryParse(t, true, out key)) return false;
            return key != UnityEngine.KeyCode.None;
        }

        /// <summary>Configured key or the default when the name does not parse.</summary>
        public static UnityEngine.KeyCode HaisonKey => TryParseKey(HotkeyHaison, out var k) ? k : UnityEngine.KeyCode.F7;
        public static UnityEngine.KeyCode EndMeetingKey => TryParseKey(HotkeyEndMeeting, out var k) ? k : UnityEngine.KeyCode.F8;
        public static UnityEngine.KeyCode CancelStartKey => TryParseKey(HotkeyCancelStart, out var k) ? k : UnityEngine.KeyCode.F9;

        // ------------------------------------------------------------------ v0.4 [Chat]

        public static bool PlayerCommands { get => _playerCommands == null || _playerCommands.Value; set { if (_playerCommands != null) _playerCommands.Value = value; } }
        public static bool AllCommands { get => _allCommands == null || _allCommands.Value; set { if (_allCommands != null) _allCommands.Value = value; } }
        /// <summary>"none" | "custom" (see <see cref="RulesModeChoices"/>).</summary>
        public static string RulesMode { get => GetChoice(_rulesMode, RulesModeChoices); set => SetChoice(_rulesMode, RulesModeChoices, value); }
        /// <summary>Raw custom rules text ("\n" two-character sequences are expanded by Chat).</summary>
        public static string RulesText { get => _rulesText == null ? "" : (_rulesText.Value ?? ""); set { if (_rulesText != null) _rulesText.Value = value ?? ""; } }
        /// <summary>Send the short welcome in the player's language, then the two others (paced; on by default) instead of one language only.</summary>
        public static bool WelcomeAllLanguages { get => _welcomeAllLanguages == null || _welcomeAllLanguages.Value; set { if (_welcomeAllLanguages != null) _welcomeAllLanguages.Value = value; } }

        // ------------------------------------------------------------------ v0.4b [Translate]

        /// <summary>
        /// [Translate] Enabled: chat translation. OFF by default, and OFF whenever the value cannot be read (entry not
        /// bound yet / config unreadable): no chat text is ever sent to Google / DeepL unless a host's own config says true.
        /// </summary>
        public static bool TranslateEnabled { get => _trEnabled != null && _trEnabled.Value; set { if (_trEnabled != null) _trEnabled.Value = value; } }
        /// <summary>
        /// [Translate] OffNotice: v0.5.5, show the host-screen "chat translation is off" line once per game session.
        /// True when the entry cannot be read, so a host who never opens the config still learns the feature exists;
        /// /opt translate.notice off silences it for a host who will never use translation.
        /// </summary>
        public static bool TranslateOffNotice { get => _trOffNotice == null || _trOffNotice.Value; set { if (_trOffNotice != null) _trOffNotice.Value = value; } }
        /// <summary>Configured provider: "auto" | "google" | "deepl" (see <see cref="TranslateProviderChoices"/>). Use <see cref="TranslateEffectiveProvider"/> to know which one to call.</summary>
        public static string TranslateProvider { get => GetChoice(_trProvider, TranslateProviderChoices); set => SetChoice(_trProvider, TranslateProviderChoices, value); }
        /// <summary>
        /// Provider actually to use: "deepl" when the configured provider is deepl/auto AND deepl-key.txt holds a key, else "google".
        /// Reads the key file on every call (cheap; callers that poll should cache for a minute).
        /// </summary>
        public static string TranslateEffectiveProvider
        {
            get
            {
                string p = TranslateProvider;
                if (p == "google") return "google";
                return HasDeepLKey ? "deepl" : "google";
            }
        }
        /// <summary>Language the host reads translations in: "ja" | "zh" | "en" (empty config value = <see cref="Language"/>).</summary>
        public static string TranslateTargetLang
        {
            get
            {
                string v = _trTargetLang?.Value;
                if (string.IsNullOrWhiteSpace(v)) return Language;
                return Lang.TryNormalize(v, out var code) ? code : Language;
            }
            set { if (_trTargetLang != null) _trTargetLang.Value = Lang.TryNormalize(value, out var code) ? code : ""; }
        }
        public static bool TranslateShowOnHost { get => _trShowOnHost == null || _trShowOnHost.Value; set { if (_trShowOnHost != null) _trShowOnHost.Value = value; } }
        public static bool TranslateBroadcastToAll { get => _trBroadcastToAll != null && _trBroadcastToAll.Value; set { if (_trBroadcastToAll != null) _trBroadcastToAll.Value = value; } }
        /// <summary>[Translate] ForeignInCompat (v0.5.3): unregistered lobbies translate the room's chat into the foreign players' languages, publicly (default true).</summary>
        public static bool TranslateForeignInCompat { get => _trForeignInCompat == null || _trForeignInCompat.Value; set { if (_trForeignInCompat != null) _trForeignInCompat.Value = value; } }
        public static bool TranslateForPlayers { get => _trForPlayers == null || _trForPlayers.Value; set { if (_trForPlayers != null) _trForPlayers.Value = value; } }
        public static bool TranslateAutoDetectLang { get => _trAutoDetect == null || _trAutoDetect.Value; set { if (_trAutoDetect != null) _trAutoDetect.Value = value; } }
        /// <summary>Messages shorter than this are not translated (1..50).</summary>
        public static int TranslateMinChars { get => _trMinChars?.Value ?? 3; set { if (_trMinChars != null) _trMinChars.Value = Math.Max(1, Math.Min(50, value)); } }
        /// <summary>Global cap of translation requests per minute (1..120).</summary>
        public static int TranslateMaxPerMinute { get => _trMaxPerMinute?.Value ?? 20; set { if (_trMaxPerMinute != null) _trMaxPerMinute.Value = Math.Max(1, Math.Min(120, value)); } }

        /// <summary>Full path of BepInEx/PocketRoles/deepl-key.txt (null when BepInEx paths are unavailable).</summary>
        public static string DeepLKeyPath
        {
            get
            {
                try
                {
                    string root = BepInEx.Paths.BepInExRootPath;
                    if (string.IsNullOrEmpty(root)) return null;
                    return Path.Combine(root, "PocketRoles", DeepLKeyFileName);
                }
                catch (Exception) { return null; }
            }
        }

        /// <summary>True when <see cref="ReadDeepLKey"/> returns a non-empty key.</summary>
        public static bool HasDeepLKey => ReadDeepLKey().Length > 0;

        /// <summary>
        /// The DeepL API key from BepInEx/PocketRoles/deepl-key.txt: UTF-8 (BOM ignored), blank lines and lines starting
        /// with '#' skipped, the first remaining line trimmed. "" when the file is missing or holds no key.
        /// The key is never written to the config, never logged and never included in report zips.
        /// </summary>
        public static string ReadDeepLKey()
        {
            try
            {
                string path = DeepLKeyPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
                foreach (var raw in File.ReadAllLines(path, System.Text.Encoding.UTF8))
                {
                    if (raw == null) continue;
                    string line = raw.Trim().TrimStart('\uFEFF').Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    return line;
                }
                return "";
            }
            catch (Exception e)
            {
                // Log the failure type only: the message could echo file content in exotic cases and the key must stay private.
                PocketRolesPlugin.Logger?.LogWarning($"Options.ReadDeepLKey: {e.GetType().Name} while reading {DeepLKeyFileName}");
                return "";
            }
        }

        // ------------------------------------------------------------------ v0.4b [Permissions]

        public static bool AdminsCanChangeSettings { get => _permAdminSettings == null || _permAdminSettings.Value; set { if (_permAdminSettings != null) _permAdminSettings.Value = value; } }
        public static bool ModeratorsCanKick { get => _permModKick == null || _permModKick.Value; set { if (_permModKick != null) _permModKick.Value = value; } }
        /// <summary>[Permissions] AdminLobbyControl: admins may /start /cancel /autostart /vset and change lobby.* / vanilla.* keys (default false).</summary>
        public static bool AdminLobbyControl { get => _permAdminLobby != null && _permAdminLobby.Value; set { if (_permAdminLobby != null) _permAdminLobby.Value = value; } }
        /// <summary>[Roles] RevealRoleOnDeath: "X was ROLE" to everyone on every kill / eject (default false).</summary>
        public static bool RevealRoleOnDeath { get => _revealOnDeath != null && _revealOnDeath.Value; set { if (_revealOnDeath != null) _revealOnDeath.Value = value; } }
        /// <summary>[Roles] RevealRoleToAll (v0.5.2): the death / ejection reveal goes to everyone (true) or to the host's screen only (false).</summary>
        public static bool RevealRoleToAll { get => _revealToAll == null || _revealToAll.Value; set { if (_revealToAll != null) _revealToAll.Value = value; } }
        /// <summary>[Roles] RevealRoleOnLeave (v0.5.2): the host's own screen shows "X left; they were ROLE" for a player leaving mid-game (default true).</summary>
        public static bool RevealRoleOnLeave { get => _revealOnLeave == null || _revealOnLeave.Value; set { if (_revealOnLeave != null) _revealOnLeave.Value = value; } }
        /// <summary>[Roles] RevealLeaveToAll (v0.5.2): that line goes to everyone as well (default false).</summary>
        public static bool RevealLeaveToAll { get => _revealLeaveToAll != null && _revealLeaveToAll.Value; set { if (_revealLeaveToAll != null) _revealLeaveToAll.Value = value; } }
        /// <summary>[Roles] HostGhostRoleList: every player's role on the dead host's own screen (default true; host-local, also in compat lobbies).</summary>
        public static bool HostGhostRoleList { get => _hostGhostRoleList == null || _hostGhostRoleList.Value; set { if (_hostGhostRoleList != null) _hostGhostRoleList.Value = value; } }
        /// <summary>[Chat] CompatWelcomeInterval: seconds between two public welcomes in an unregistered lobby (default 60, 0 = every join).</summary>
        public static float CompatWelcomeInterval { get => _compatWelcomeInterval?.Value ?? 60f; set { if (_compatWelcomeInterval != null) _compatWelcomeInterval.Value = Math.Max(0f, Math.Min(600f, value)); } }
        public static bool VipMarker { get => _permVipMarker == null || _permVipMarker.Value; set { if (_permVipMarker != null) _permVipMarker.Value = value; } }

        // ------------------------------------------------------------------ v0.4b [Vanilla] extended ranges

        public static bool ExtendedRanges { get => _vanExtendedRanges == null || _vanExtendedRanges.Value; set { if (_vanExtendedRanges != null) _vanExtendedRanges.Value = value; } }
        /// <summary>Seconds (0..60), vanilla 10.</summary>
        public static float KillCooldownMin { get => _vanKillMin?.Value ?? 0f; set { if (_vanKillMin != null) _vanKillMin.Value = Clamp(value, 0f, 60f); } }
        /// <summary>Seconds (10..600), vanilla 60.</summary>
        public static float KillCooldownMax { get => _vanKillMax?.Value ?? 120f; set { if (_vanKillMax != null) _vanKillMax.Value = Clamp(value, 10f, 600f); } }
        /// <summary>Seconds (0.5..10), vanilla 2.5.</summary>
        public static float KillCooldownStep { get => _vanKillStep?.Value ?? 2.5f; set { if (_vanKillStep != null) _vanKillStep.Value = Clamp(value, 0.5f, 10f); } }
        /// <summary>Seconds (0..300), vanilla 15.</summary>
        public static int VotingTimeMin { get => _vanVoteMin?.Value ?? 0; set { if (_vanVoteMin != null) _vanVoteMin.Value = Math.Max(0, Math.Min(300, value)); } }
        /// <summary>Seconds (15..3600), vanilla 300.</summary>
        public static int VotingTimeMax { get => _vanVoteMax?.Value ?? 600; set { if (_vanVoteMax != null) _vanVoteMax.Value = Math.Max(15, Math.Min(3600, value)); } }
        /// <summary>Seconds (0..3600), vanilla 120.</summary>
        public static int DiscussionTimeMax { get => _vanDiscussMax?.Value ?? 600; set { if (_vanDiscussMax != null) _vanDiscussMax.Value = Math.Max(0, Math.Min(3600, value)); } }
        /// <summary>Seconds (0..600), vanilla 60.</summary>
        public static int EmergencyCooldownMax { get => _vanEmergencyMax?.Value ?? 120; set { if (_vanEmergencyMax != null) _vanEmergencyMax.Value = Math.Max(0, Math.Min(600, value)); } }
        /// <summary>Per-category task count (1..60), vanilla 2 / 5 / 3.</summary>
        public static int TaskCountMax { get => _vanTaskMax?.Value ?? 30; set { if (_vanTaskMax != null) _vanTaskMax.Value = Math.Max(1, Math.Min(60, value)); } }
        /// <summary>[Vanilla] ClampInUnregistered: clamp the vanilla numeric settings to their vanilla ranges in an unregistered lobby (default true).</summary>
        public static bool ClampInUnregistered { get => _vanClampUnreg == null || _vanClampUnreg.Value; set { if (_vanClampUnreg != null) _vanClampUnreg.Value = value; } }

        // ------------------------------------------------------------------ v0.4e [Guide]

        /// <summary>Show the big room-code overlay on the host's lobby screen (default on; /code toggles it).</summary>
        public static bool ShowCodeOverlay { get => _guideShowCodeOverlay == null || _guideShowCodeOverlay.Value; set { if (_guideShowCodeOverlay != null) _guideShowCodeOverlay.Value = value; } }
        /// <summary>Room code of the registered role lobby announced by /move ("" = none; normalized to upper case, letters only).</summary>
        public static string RoleRoomCode
        {
            get => _guideRoleRoomCode == null ? "" : NormalizeRoomCode(_guideRoleRoomCode.Value);
            set { if (_guideRoleRoomCode != null) _guideRoleRoomCode.Value = NormalizeRoomCode(value); }
        }
        /// <summary>/move re-creates the current unregistered lobby as a registered one after 30 s (default off).</summary>
        public static bool AutoRecreateRegistered { get => _guideAutoRecreateRegistered != null && _guideAutoRecreateRegistered.Value; set { if (_guideAutoRecreateRegistered != null) _guideAutoRecreateRegistered.Value = value; } }

        /// <summary>Upper-case A-Z only (a room code is 4 or 6 letters); anything else → "".</summary>
        public static string NormalizeRoomCode(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "";
            string t = v.Trim().ToUpperInvariant();
            if (t.Length != 4 && t.Length != 6) return "";
            foreach (char c in t) if (c < 'A' || c > 'Z') return "";
            return t;
        }

        public static int Count(CustomRole r) => _count.TryGetValue(r, out var e) ? e.Value : 0;
        public static int Chance(CustomRole r) => _chance.TryGetValue(r, out var e) ? e.Value : 0;
        public static void SetCount(CustomRole r, int n) { if (_count.TryGetValue(r, out var e)) e.Value = Math.Max(0, Math.Min(r == CustomRole.Lovers ? 1 : 15, n)); }
        public static void SetChance(CustomRole r, int c) { if (_chance.TryGetValue(r, out var e)) e.Value = Math.Max(0, Math.Min(100, c)); }

        public static float SheriffKillCooldown => _sheriffKillCooldown?.Value ?? 30f;
        public static bool SheriffCanKillMadmate => _sheriffCanKillMadmate == null || _sheriffCanKillMadmate.Value;
        public static float JackalKillCooldown => _jackalKillCooldown?.Value ?? 30f;
        public static bool JackalCanVent => _jackalCanVent == null || _jackalCanVent.Value;
        public static float VampireKillDelay => _vampireKillDelay?.Value ?? 10f;
        public static int MayorVotes => _mayorVotes?.Value ?? 2;
        public static int SnitchTasksLeftToWarn => _snitchTasksLeftToWarn?.Value ?? 1;
        public static float LighterVision => _lighterVision?.Value ?? 2f;
        public static float SpeedBoosterSpeed => _speedBoosterSpeed?.Value ?? 1.5f;
        public static bool MadmateKnownToImpostors => _madmateKnownToImpostors != null && _madmateKnownToImpostors.Value;

        // v0.4.1
        public static bool LoversAllowImpostor => _loversAllowImpostor == null || _loversAllowImpostor.Value;
        public static bool LoversWinAsLastThree => _loversLastThree == null || _loversLastThree.Value;
        public static float ArsonistDouseCooldown => _arsonistDouseCooldown?.Value ?? 10f;
        public static bool ArsonistCanVent => _arsonistCanVent != null && _arsonistCanVent.Value;
        /// <summary>0 = the lobby kill cooldown.</summary>
        public static float WitchSpellCooldown => _witchSpellCooldown?.Value ?? 0f;
        public static bool WitchSpelledSeeMark => _witchSpelledSeeMark != null && _witchSpelledSeeMark.Value;
        public static int AssassinGuessesPerMeeting => _assassinGuessesPerMeeting?.Value ?? 1;
        public static bool AssassinCanGuessFirstMeeting => _assassinFirstMeeting == null || _assassinFirstMeeting.Value;

        // v0.5.0
        public static int MadMayorVotes => _madMayorVotes?.Value ?? 2;
        public static bool MadMayorKnownToImpostors => _madMayorKnownToImpostors != null && _madMayorKnownToImpostors.Value;
        public static int MadStuntmanLives => _madStuntmanLives?.Value ?? 1;
        public static bool MadStuntmanNotify => _madStuntmanNotify != null && _madStuntmanNotify.Value;   // default false
        public static float MadHawkVision => _madHawkVision?.Value ?? 3f;
        public static float MadHawkSpeed => _madHawkSpeed?.Value ?? 1f;
        public static int WorshipperUses => _worshipperUses?.Value ?? 1;
        public static float WorshipperCooldown => _worshipperCooldown?.Value ?? 30f;
        public static bool JackalFriendsKnownToJackal => _jackalFriendsKnownToJackal != null && _jackalFriendsKnownToJackal.Value;
        public static bool JackalFriendsSheriffCanKill => _jackalFriendsSheriffCanKill == null || _jackalFriendsSheriffCanKill.Value;
        public static bool EvilNekomataVotersOnly => _nekomataVotersOnly == null || _nekomataVotersOnly.Value;
        public static bool EvilNekomataExcludeImpostors => _nekomataExcludeImpostors == null || _nekomataExcludeImpostors.Value;
        public static bool EvilNekomataAnnounce => _nekomataAnnounce == null || _nekomataAnnounce.Value;
        public static float SerialKillerKillCooldown => _serialKillerKillCooldown?.Value ?? 10f;
        /// <summary>Raw option; SerialKiller.Limit() raises it to KillCooldown + 5 when set lower.</summary>
        public static float SerialKillerSuicideTime => _serialKillerSuicideTime?.Value ?? 30f;
        public static bool SerialKillerResetAtMeeting => _serialKillerResetAtMeeting == null || _serialKillerResetAtMeeting.Value;
        /// <summary>0 = the lobby kill cooldown (Samurai.KillCooldown()).</summary>
        public static float SamuraiKillCooldown => _samuraiKillCooldown?.Value ?? 45f;
        /// <summary>Slash radius in map units.</summary>
        public static float SamuraiRange => _samuraiRange?.Value ?? 2f;
        /// <summary>Seconds between two bystander deaths (never 0: one MurderPlayer RPC per HudManager tick). Options.cs has no UnityEngine import: System.Math.</summary>
        public static float SamuraiStagger => Math.Max(0.1f, Math.Min(1f, _samuraiStagger?.Value ?? 0.3f));
        public static bool SamuraiHitTeammates => _samuraiHitTeammates != null && _samuraiHitTeammates.Value;
        public static float EvilHawkVision => _evilHawkVision?.Value ?? 2f;

        /// <summary>Total number of custom-role slots that are enabled (for quick sanity messages).</summary>
        public static int EnabledSlots()
        {
            int n = 0;
            foreach (var r in Roles.All) n += Count(r.Id);
            return n;
        }

        // ------------------------------------------------------------------ descriptors

        private static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        private static OptionDescriptor Bool(string key, string sJa, string sEn, string nJa, string nEn, ConfigEntry<bool> e, string color = null)
        {
            return new OptionDescriptor
            {
                Key = key, SectionJa = sJa, SectionEn = sEn, NameJa = nJa, NameEn = nEn, Kind = OptionKind.Bool,
                Min = 0, Max = 1, Step = 1, ColorHex = color,
                GetNumber = () => e != null && e.Value ? 1f : 0f,
                SetNumber = v => { if (e != null) e.Value = v >= 0.5f; },
            };
        }

        private static OptionDescriptor Int(string key, string sJa, string sEn, string nJa, string nEn, ConfigEntry<int> e, int min, int max, int step, string color = null)
        {
            return new OptionDescriptor
            {
                Key = key, SectionJa = sJa, SectionEn = sEn, NameJa = nJa, NameEn = nEn, Kind = OptionKind.Int,
                Min = min, Max = max, Step = step, ColorHex = color,
                GetNumber = () => e != null ? e.Value : 0f,
                SetNumber = v => { if (e != null) e.Value = (int)Math.Round(Clamp(v, min, max)); },
            };
        }

        private static OptionDescriptor Float(string key, string sJa, string sEn, string nJa, string nEn, ConfigEntry<float> e, float min, float max, float step, string color = null)
        {
            return new OptionDescriptor
            {
                Key = key, SectionJa = sJa, SectionEn = sEn, NameJa = nJa, NameEn = nEn, Kind = OptionKind.Float,
                Min = min, Max = max, Step = step, ColorHex = color,
                GetNumber = () => e != null ? e.Value : 0f,
                SetNumber = v => { if (e != null) e.Value = (float)Math.Round(Clamp(v, min, max), 2); },
            };
        }

        /// <summary>Choice row over a string ConfigEntry: index ↔ choices[i] (unknown value → index 0).</summary>
        private static OptionDescriptor Choice(string key, string sJa, string sEn, string nJa, string nEn, ConfigEntry<string> e, string[] choices, string color = null)
        {
            return new OptionDescriptor
            {
                Key = key, SectionJa = sJa, SectionEn = sEn, NameJa = nJa, NameEn = nEn, Kind = OptionKind.Choice,
                Min = 0, Max = choices.Length - 1, Step = 1, Choices = choices, ColorHex = color,
                GetNumber = () => Array.IndexOf(choices, GetChoice(e, choices)),
                SetNumber = v => { int i = (int)Math.Round(Clamp(v, 0, choices.Length - 1)); SetChoice(e, choices, choices[i]); },
            };
        }

        /// <summary>Hotkey row: cycles through <see cref="HotkeyChoices"/>; a key outside that list shows as the default.</summary>
        private static OptionDescriptor Hotkey(string key, string sJa, string sEn, string nJa, string nEn, ConfigEntry<string> e, string def)
        {
            var choices = HotkeyChoices;
            return new OptionDescriptor
            {
                Key = key, SectionJa = sJa, SectionEn = sEn, NameJa = nJa, NameEn = nEn, Kind = OptionKind.Choice,
                Min = 0, Max = choices.Length - 1, Step = 1, Choices = choices,
                GetNumber = () =>
                {
                    string cur = GetKeyName(e, def);
                    int i = Array.IndexOf(choices, cur);
                    if (i < 0) i = Array.IndexOf(choices, def);
                    return i < 0 ? 0 : i;
                },
                SetNumber = v => { int i = (int)Math.Round(Clamp(v, 0, choices.Length - 1)); SetKeyName(e, choices[i], def); },
            };
        }

        /// <summary>Attaches the tooltip (ja / en / zh) shown by the settings-tab "?" button; returns the same descriptor.</summary>
        private static OptionDescriptor Tip(this OptionDescriptor d, string ja, string en, string zh)
        {
            d.DescJa = ja; d.DescEn = en; d.DescZh = zh;
            return d;
        }

        /// <summary>v0.5.5: inline Simplified Chinese row name for a row the zh table lacks (official terms); returns the same descriptor.</summary>
        private static OptionDescriptor Zh(this OptionDescriptor d, string name)
        {
            d.NameZh = name;
            return d;
        }

        private static void BuildDescriptors()
        {
            _descriptors.Clear();

            _descriptors.Add(Bool("roles.ghostlist", "全般", "General", "死亡後に役職一覧（ホストのみ）", "Role list after death (host only)", _hostGhostRoleList)
                .Tip("ホストが死んだあと、全員の役職（生存・死亡）をホストの画面だけに表示します。会議のたびに再表示、以後の死亡も1行ずつ。未登録の部屋でも動きます（本体の役職名）。他の人には送られません。/who でも表示。",
                    "Once you (the host) are dead, every player's role (alive / dead) is shown on your screen only: again at each meeting, plus one line per later death. Works in unregistered lobbies too (vanilla roles). Never sent to others; /who shows it on demand.",
                    "房主死亡后，所有玩家的职业（存活/死亡）只显示在房主的屏幕上：每次会议再次显示，之后每有人死亡显示一行。未注册房间也可用（原版职业名）。不会发给其他人；/who 可随时查看。"));
            _descriptors.Add(Bool("roles.vanilla", "全般", "General", "本体の特殊役職も配る", "Also assign vanilla special roles", _vanillaRoles)
                .Tip("オン = 科学者・エンジニア・ジャッジなど本体の役職も本体の設定どおりに出ます。オフ（既定）= クルーとインポスターだけにして、そこから PocketRoles の役職を配ります。",
                    "On = vanilla roles (Scientist, Engineer, Judge, ...) are assigned as set in the vanilla role settings. Off (default) = only Crewmates and Impostors, from which the PocketRoles roles are drawn.",
                    "开 = 科学家、工程师、法官等原版职业按原版设置出现。关（默认）= 只有船员和伪装者，PocketRoles 的职业从中分配。"));

            foreach (var r in Roles.All)
            {
                string sJa = r.NameJa, sEn = r.NameEn, color = r.Color;
                int firstRow = _descriptors.Count;
                var role = r.Id;
                if (role == CustomRole.Lovers)
                    _descriptors.Add(Int(r.Key + ".count", sJa, sEn, "組数（0/1）", "Pairs (0/1)", _count[role], 0, 1, 1, color)
                        .Tip("ラバーズを出すか（1 = 1 組 2 人、0 = 出さない）。", "1 = one pair (two players), 0 = never.", "1 = 一对（两人），0 = 不出现。"));
                else
                    _descriptors.Add(Int(r.Key + ".count", sJa, sEn, "人数", "Count", _count[role], 0, 15, 1, color)
                        .Tip("この役職を最大何人まで出すか（0 = 出さない）。", "Maximum number of this role per game (0 = never).", "此职业每局最多出现的人数（0 = 不出现）。"));
                _descriptors.Add(Int(r.Key + ".chance", sJa, sEn, "確率", "Chance", _chance[role], 0, 100, 5, color)
                    .Tip("各枠にこの役職が実際に割り当てられる確率（%）。", "Chance (%) that each slot of this role is actually assigned.", "每个名额实际分配此职业的概率（%）。"));
                switch (role)
                {
                    case CustomRole.Sheriff:
                        _descriptors.Add(Float("sheriff.cooldown", sJa, sEn, "キルクールダウン", "Kill cooldown", _sheriffKillCooldown, 2.5f, 180f, 2.5f, color)
                            .Tip("シェリフがキルボタンを再び使えるまでの秒数。", "Seconds before the Sheriff can shoot again.", "警长再次开枪所需的冷却秒数。"));
                        _descriptors.Add(Bool("sheriff.killmadmate", sJa, sEn, "マッド系を撃てる", "Can kill Mad roles", _sheriffCanKillMadmate, color)
                            .Tip("オンならマッド系役職（マッドメイト・マッドメイヤー・マッドスタントマン・マッドホーク・崇拝者）を撃っても自分は死にません。", "On: shooting a Mad-type role (Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper) does not kill the Sheriff.", "开启后射杀狂信徒系职业（狂信徒、狂信徒市长、狂信徒特技演员、狂信徒鹰眼、传教士）不会让警长死亡。"));
                        break;
                    case CustomRole.Jackal:
                        _descriptors.Add(Float("jackal.cooldown", sJa, sEn, "キルクールダウン", "Kill cooldown", _jackalKillCooldown, 2.5f, 180f, 2.5f, color)
                            .Tip("ジャッカルのキルクールダウン（秒）。", "Jackal kill cooldown in seconds.", "豺狼的击杀冷却秒数。"));
                        _descriptors.Add(Bool("jackal.vent", sJa, sEn, "ベント使用", "Can vent", _jackalCanVent, color)
                            .Tip("ジャッカルがベントに入れるかどうか。", "Whether the Jackal can use vents.", "豺狼是否可以钻通风口。"));
                        break;
                    case CustomRole.Vampire:
                        _descriptors.Add(Float("vampire.delay", sJa, sEn, "噛みつき遅延", "Kill delay", _vampireKillDelay, 1f, 60f, 1f, color)
                            .Tip("噛みついてから相手が死ぬまでの秒数。", "Seconds between the bite and the victim's death.", "咬人后到受害者死亡的秒数。"));
                        break;
                    case CustomRole.Mayor:
                        _descriptors.Add(Int("mayor.votes", sJa, sEn, "票数", "Votes", _mayorVotes, 1, 5, 1, color)
                            .Tip("メイヤーの1票を何票として数えるか。", "How many votes the Mayor's single vote counts as.", "市长的一票作为多少票。"));
                        break;
                    case CustomRole.Snitch:
                        _descriptors.Add(Int("snitch.tasks", sJa, sEn, "残りタスクで警告", "Tasks left to warn", _snitchTasksLeftToWarn, 0, 10, 1, color)
                            .Tip("残りタスクがこの数以下になるとキラーにスニッチが表示されます。", "Killers see the Snitch once this many tasks (or fewer) remain.", "剩余任务数不超过此值时，杀手会看到告密者。"));
                        break;
                    case CustomRole.Lighter:
                        _descriptors.Add(Float("lighter.vision", sJa, sEn, "視界倍率", "Vision multiplier", _lighterVision, 1f, 5f, 0.25f, color)
                            .Tip("ライターの視界の倍率。", "Vision multiplier of the Lighter.", "执灯人的视野倍率。"));
                        break;
                    case CustomRole.SpeedBooster:
                        _descriptors.Add(Float("speedbooster.speed", sJa, sEn, "速度倍率", "Speed multiplier", _speedBoosterSpeed, 1f, 3f, 0.25f, color)
                            .Tip("スピードブースターの移動速度の倍率。", "Movement speed multiplier of the Speed Booster.", "加速者的移动速度倍率。"));
                        break;
                    case CustomRole.Madmate:
                        _descriptors.Add(Bool("madmate.known", sJa, sEn, "インポスターに公開", "Known to impostors", _madmateKnownToImpostors, color)
                            .Tip("オンならインポスターにマッド系役職（マッドメイト・マッドスタントマン・マッドホーク・崇拝者）が誰か表示されます（マッドメイヤーは別設定）。", "On: Impostors see who the Mad-type players are (Madmate, Mad Stuntman, Mad Hawk; Ⓦ for the Worshipper; the Mad Mayor has its own switch).", "开启后伪装者可以看到谁是狂信徒系职业（狂信徒、狂信徒特技演员、狂信徒鹰眼；传教士为 Ⓦ；狂信徒市长另有设置）。"));
                        break;
                    // v0.4.1
                    case CustomRole.Lovers:
                        _descriptors.Add(Bool("lovers.impostor", sJa, sEn, "インポスターも恋人になる", "Impostor may be a lover", _loversAllowImpostor, color)
                            .Tip("オンなら2人目の恋人がインポスターから選ばれることがあります（キルはできたままです）。", "On: the second lover may be a vanilla Impostor (it keeps its kill button).", "开启后第二位恋人可能从伪装者中选出（仍可击杀）。"));
                        _descriptors.Add(Bool("lovers.lastthree", sJa, sEn, "残り3人で勝利", "Win as last 3", _loversLastThree, color)
                            .Tip("オンなら2人とも生きていて生存者が3人以下になった時点でラバーズの勝利です。", "On: the Lovers win as soon as both are alive and at most 3 players remain.", "开启后两人存活且存活者不超过3人时恋人立即获胜。"));
                        break;
                    case CustomRole.Arsonist:
                        _descriptors.Add(Float("arsonist.cooldown", sJa, sEn, "油のクールダウン", "Douse cooldown", _arsonistDouseCooldown, 2.5f, 180f, 2.5f, color)
                            .Tip("油をかけてから次にかけられるまでの秒数。", "Seconds between two douses.", "两次浇油之间的冷却秒数。"));
                        _descriptors.Add(Bool("arsonist.vent", sJa, sEn, "ベント使用", "Can vent", _arsonistCanVent, color)
                            .Tip("放火魔がベントに入れるかどうか。", "Whether the Arsonist can use vents.", "纵火犯是否可以钻通风口。"));
                        break;
                    case CustomRole.Witch:
                        _descriptors.Add(Float("witch.cooldown", sJa, sEn, "呪いのクールダウン", "Spell cooldown", _witchSpellCooldown, 0f, 180f, 2.5f, color)
                            .Tip("呪いをかけてから次にかけられるまでの秒数（0 = キルクールダウンと同じ）。", "Seconds between two spells (0 = same as the kill cooldown).", "两次诅咒之间的秒数（0 = 与击杀冷却相同）。"));
                        _descriptors.Add(Bool("witch.mark", sJa, sEn, "呪われた本人に印", "Target sees mark", _witchSpelledSeeMark, color)
                            .Tip("オンなら呪われた人の自分の名前に印が付きます（魔女にはいつも見えます）。", "On: a spelled player sees a mark on their own name (the Witch always sees it).", "开启后被诅咒者能在自己名字上看到标记（女巫始终可见）。"));
                        break;
                    case CustomRole.Assassin:
                        _descriptors.Add(Int("assassin.guesses", sJa, sEn, "会議ごとの推理回数", "Guesses per meeting", _assassinGuessesPerMeeting, 1, 5, 1, color)
                            .Tip("1回の会議で /cmd guess を使える回数。", "How many /cmd guess an Assassin may use per meeting.", "每次会议可使用 /cmd guess 的次数。"));
                        _descriptors.Add(Bool("assassin.firstmeeting", sJa, sEn, "初回会議でも推理可", "Can guess in 1st meeting", _assassinFirstMeeting, color)
                            .Tip("オフなら試合の最初の会議では推理できません。", "Off: no guessing in the first meeting of the game.", "关闭后本局第一次会议不能猜测。"));
                        break;
                    // ---- v0.5.0
                    case CustomRole.MadMayor:
                        _descriptors.Add(Int("madmayor.votes", sJa, sEn, "票数", "Votes", _madMayorVotes, 1, 5, 1, color)
                            .Tip("マッドメイヤーの1票を何票として数えるか。", "How many votes the Mad Mayor's single vote counts as.", "狂信徒市长的一票作为多少票。"));
                        _descriptors.Add(Bool("madmayor.known", sJa, sEn, "インポスターに公開", "Known to impostors", _madMayorKnownToImpostors, color)
                            .Tip("オンならインポスターに誰がマッドメイヤーか表示されます。", "On: Impostors see who the Mad Mayor is.", "开启后伪装者可以看到谁是狂信徒市长。"));
                        break;
                    case CustomRole.MadStuntman:
                        _descriptors.Add(Int("madstuntman.lives", sJa, sEn, "耐えられるキル回数", "Kills survived", _madStuntmanLives, 1, 10, 1, color)
                            .Tip("この回数まではキルされても死にません（投票による追放は防げません）。", "Kill attempts the Mad Stuntman survives before one goes through (votes are never blocked).", "在此次数内被击杀也不会死（无法阻止投票驱逐）。"));
                        _descriptors.Add(Bool("madstuntman.notify", sJa, sEn, "本人に通知", "Notify stuntman", _madStuntmanNotify, color)
                            .Tip("オンならキルを耐えたことと残り回数を本人にチャットで知らせます（ヴァンパイアの噛みつきや魔女の呪いを耐えた時も知らせます。キルした側にはいつも知らせます）。", "On: the stuntman is told in chat that it survived and how many attempts are left (also for an absorbed Vampire bite or Witch spell; the killer is always told).", "开启后会用聊天告诉本人挡下了击杀以及剩余次数（挡下吸血鬼的咬或女巫的诅咒时也会告知；击杀者始终会被告知）。"));
                        break;
                    case CustomRole.MadHawk:
                        _descriptors.Add(Float("madhawk.vision", sJa, sEn, "視界倍率", "Vision multiplier", _madHawkVision, 1f, 5f, 0.25f, color)
                            .Tip("マッドホークの視界の倍率（停電中は、狭くなった視界にこの倍率がかかります）。", "Vision multiplier of the Mad Hawk (during a blackout the shrunken vision is multiplied).", "狂信徒鹰眼的视野倍率（停电时是缩小后视野的倍数）。"));
                        _descriptors.Add(Float("madhawk.speed", sJa, sEn, "速度倍率", "Speed multiplier", _madHawkSpeed, 0.5f, 1.5f, 0.25f, color)
                            .Tip("マッドホークの移動速度の倍率（1 = 通常。広い視界の代償に遅くするなら 1 未満）。", "Movement speed multiplier of the Mad Hawk (1 = normal; below 1 to pay for the wide vision).", "狂信徒鹰眼的移动速度倍率（1 = 普通；小于 1 可作为大视野的代价）。"));
                        break;
                    case CustomRole.Worshipper:
                        _descriptors.Add(Int("worshipper.uses", sJa, sEn, "崇拝回数", "Worships", _worshipperUses, 1, 5, 1, color)
                            .Tip("1 試合に崇拝できる回数（成功した分だけ数えます）。", "How many players the Worshipper may convert per game (only successes count).", "每局可以传教的次数（只计成功的次数）。"));
                        _descriptors.Add(Float("worshipper.cooldown", sJa, sEn, "崇拝のクールダウン", "Worship cooldown", _worshipperCooldown, 2.5f, 180f, 2.5f, color)
                            .Tip("崇拝してから次に崇拝できるまでの秒数（キルボタンのクールダウン）。", "Seconds between two worships (the kill button's cooldown).", "两次传教之间的秒数（击杀键冷却）。"));
                        break;
                    case CustomRole.JackalFriends:
                        _descriptors.Add(Bool("jackalfriends.known", sJa, sEn, "ジャッカルに公開", "Known to Jackal", _jackalFriendsKnownToJackal, color)
                            .Tip("オンならジャッカルにジャッカルフレンズの名前が青く見えます。", "On: the Jackal sees the Jackal Friends' names in blue.", "开启后豺狼能看到跟班的名字（蓝色）。"));
                        _descriptors.Add(Bool("jackalfriends.sheriff", sJa, sEn, "シェリフに撃たれる", "Sheriff can shoot", _jackalFriendsSheriffCanKill, color)
                            .Tip("オンならシェリフはジャッカルフレンズを撃っても死にません。オフなら誤射扱いでシェリフが死にます。", "On: the Sheriff may shoot Jackal Friends without dying. Off: shooting one is a misfire (the Sheriff dies).", "开启后警长射杀跟班不会死亡；关闭则视为误杀，警长死亡。"));
                        break;
                    case CustomRole.EvilHawk:
                        _descriptors.Add(Float("evilhawk.vision", sJa, sEn, "視界倍率", "Vision multiplier", _evilHawkVision, 1f, 5f, 0.25f, color)
                            .Tip("イビルホークの視界の倍率（インポスターの視界に掛けます。常時有効）。", "Vision multiplier of the Evil Hawk (applied to the impostor vision, always on).", "邪恶鹰眼的视野倍率（乘以伪装者视野，始终有效）。"));
                        break;
                    case CustomRole.EvilNekomata:
                        _descriptors.Add(Bool("evilnekomata.voters", sJa, sEn, "道連れは投票者から", "Drag a voter only", _nekomataVotersOnly, color)
                            .Tip("オンなら自分に投票した人の中から、オフなら生存者全員の中から道連れを選びます。", "On: the victim is one of the players who voted for you. Off: any living player.", "开启：从投票给你的人中选择；关闭：从所有存活玩家中选择。"));
                        _descriptors.Add(Bool("evilnekomata.excludeimp", sJa, sEn, "インポスター陣営を除外", "Exclude impostor team", _nekomataExcludeImpostors, color)
                            .Tip("オンならインポスター陣営（マッド系役職含む）は道連れになりません。", "On: Impostor-team players (Madmate family included) are never dragged.", "开启后伪装者阵营（含狂信徒系职业）不会被拖走。"));
                        _descriptors.Add(Bool("evilnekomata.announce", sJa, sEn, "道連れを全員に通知", "Announce the drag", _nekomataAnnounce, color)
                            .Tip("オンなら追放画面の後に「○○ は △△ の道連れになりました」と全員に届きます。オフなら本人にだけ届きます。", "On: after the ejection screen everyone reads who was dragged along. Off: only the victim is told.", "开启后驱逐画面结束时所有人都会看到谁被拖走；关闭则只通知本人。"));
                        break;
                    case CustomRole.SerialKiller:
                        _descriptors.Add(Float("serialkiller.cooldown", sJa, sEn, "キルクールダウン", "Kill cooldown", _serialKillerKillCooldown, 1f, 60f, 1f, color)
                            .Tip("シリアルキラーがキルボタンを再び使えるまでの秒数。", "Seconds before the Serial Killer can kill again.", "连环杀手再次击杀所需的秒数。"));
                        _descriptors.Add(Float("serialkiller.time", sJa, sEn, "自殺までの時間", "Time until suicide", _serialKillerSuicideTime, 10f, 300f, 5f, color)
                            .Tip("前のキルからこの秒数キルしないと自滅します（会議中は止まります。キルCD+5 秒未満には下がりません）。", "Seconds without a kill before the Serial Killer dies by itself (paused during meetings; never below kill cooldown + 5).", "距上次击杀超过此秒数未击杀则自灭（会议中暂停；不会低于击杀冷却+5 秒）。"));
                        _descriptors.Add(Bool("serialkiller.meetingreset", sJa, sEn, "会議でタイマーをリセット", "Timer resets at meetings", _serialKillerResetAtMeeting, color)
                            .Tip("オンなら会議が終わるたびにタイマーが最初から始まります。オフなら残り時間を引き継ぎます。", "On: the timer restarts after every meeting. Off: the remaining time carries over.", "开启后每次会议结束计时重新开始；关闭则沿用剩余时间。"));
                        break;
                    case CustomRole.Samurai:
                        _descriptors.Add(Float("samurai.cooldown", sJa, sEn, "斬撃のクールダウン", "Slash cooldown", _samuraiKillCooldown, 0f, 180f, 2.5f, color)
                            .Tip("斬撃から次の斬撃までの秒数（0 = キルクールダウンと同じ）。", "Seconds between two slashes (0 = same as the kill cooldown).", "两次斩击之间的秒数（0 = 与击杀冷却相同）。"));
                        _descriptors.Add(Float("samurai.range", sJa, sEn, "斬撃の範囲", "Slash range", _samuraiRange, 0.5f, 5f, 0.25f, color)
                            .Tip("侍を中心にした半径。バニラのキル距離はおよそ 短1 / 中1.8 / 長2.5。", "Radius around the Samurai. Vanilla kill distances are roughly short 1 / medium 1.8 / long 2.5.", "以武士为中心的半径。原版击杀范围约为 短1 / 中1.8 / 长2.5。"));
                        _descriptors.Add(Float("samurai.stagger", sJa, sEn, "倒れる間隔", "Death interval", _samuraiStagger, 0.1f, 1f, 0.1f, color)
                            .Tip("巻き込まれた人が順に倒れる間隔の秒数（0.3 = 公式サーバーの送信間隔）。", "Seconds between two bystander deaths (0.3 = the official server's packet spacing).", "被波及者依次倒下的间隔秒数（0.3 = 官方服务器的发送间隔）。"));
                        _descriptors.Add(Bool("samurai.teammates", sJa, sEn, "味方も斬る", "Hits allies", _samuraiHitTeammates, color)
                            .Tip("オンなら範囲内のインポスター陣営（インポスター・マッド系役職など）も死にます。", "On: Impostor-team players in range (Impostors, Madmate family …) die too.", "开启后范围内的伪装者阵营（伪装者、狂信徒系职业等）也会死亡。"));
                        break;
                }
                // v0.5.5: the role section heading in Chinese even without a zh table entry
                for (int i = firstRow; i < _descriptors.Count; i++) _descriptors[i].SectionZh = r.NameZh;
            }

            const string gJa = "全般", gEn = "General";
            _descriptors.Add(Bool("register", gJa, gEn, "MOD部屋登録（公式ルール・追加役職に必須）", "Mod-lobby registration (official rule, needed for mod roles)", _register)
                .Tip("2026年7月からの公式ルールで、MODを使う部屋はサーバーに登録する必要があります。登録した部屋は公開一覧に出ないので、部屋コードか案内部屋から入ってもらいます。", "Since July 2026 the official servers require lobbies that use mods to register. Registered lobbies do not appear in the public list, so players join by room code or through the guide room.", "根据 2026 年 7 月起的官方规则，使用 MOD 的房间必须向服务器注册。已注册的房间不会出现在公开列表里，请用房间代码或引导房加入。"));
            _descriptors.Add(new OptionDescriptor
            {
                Key = "lang", SectionJa = gJa, SectionEn = gEn, NameJa = "言語", NameEn = "Language", Kind = OptionKind.Choice,
                Min = 0, Max = 3, Step = 1, Choices = LanguageChoices,
                GetNumber = () => Math.Max(0, Array.IndexOf(LanguageChoices, LanguageSetting)),
                SetNumber = v => Language = LanguageChoices[(int)Math.Round(Clamp(v, 0, LanguageChoices.Length - 1))],
            }.Tip("チャットや説明の既定の言語。auto = ゲームの言語に合わせる（中国語 → 中文、日本語 → 日本語、それ以外 → English）。各プレイヤーは /lang で変更できます。", "auto = follow the game's language (Chinese → 中文, Japanese → 日本語, else English). Players: /lang", "聊天文本的默认语言。auto = 跟随游戏语言（中文 → 中文，日语 → 日本語，其他 → English）。每位玩家可用 /lang 更改。"));
            _descriptors.Add(Bool("welcome", gJa, gEn, "参加時の挨拶", "Welcome message", _welcome)
                .Tip("参加した人にこの部屋がMOD部屋であることを個別に知らせます。", "Privately tells every joining player that this lobby uses a host-side mod.", "私聊告知每位加入的玩家本房间使用房主模组。"));
            _descriptors.Add(Bool("roles.reveal", gJa, gEn, "死亡・追放時に役職を表示", "Reveal role on death / ejection", _revealOnDeath).Zh("死亡或被驱逐时显示职业"));
            _descriptors.Add(Bool("roles.revealall", gJa, gEn, "役職表示を全員に(オフ=ホストのみ)", "Reveal to everyone (off = host only)", _revealToAll).Zh("职业显示给所有人(关=仅房主)"));
            _descriptors.Add(Bool("roleinfo", gJa, gEn, "会議で役職説明", "Role info at meetings", _roleInfoAtMeeting)
                .Tip("会議開始時に各自の役職説明を個別に送り直します。", "Re-sends each player's role description privately when a meeting starts.", "会议开始时再次私聊发送各自的职业说明。"));
            _descriptors.Add(Bool("anticheat", gJa, gEn, "Aegisアンチチート(登録オフ)", "Aegis anti-cheat (unregistered)", _cheatDetect)
                .Tip("登録オフの部屋で、普通のAmong Usではありえない操作(キルできない役のキル、ベント、能力、タスク、生存中の会議外チャットなど)を見つけてホストの画面に出します。/ac で一覧。", "In unregistered rooms, spots actions vanilla Among Us never produces (kills, vents, abilities, tasks by roles that cannot, alive chat outside meetings...) and shows them on the host's screen. /ac lists them.", "在未注册房间中，发现原版Among Us不可能出现的操作(不能击杀的职业击杀、不能钻通风口的职业钻通风口、使用没有的能力、伪装者完成任务、存活时会议外聊天等)并显示在房主的画面上。/ac 查看列表。"));
            _descriptors.Add(Bool("anticheat.kick", gJa, gEn, "チートの人を自動で退出", "Remove cheaters automatically", _cheatAutoKick)
                .Tip("確実な検知(キル・ベント・能力・タスク)は1回、会議外チャットは2回で、この部屋へのバン付きで退出させます。VIP以上は対象外。", "Removes (with a ban for this room) on the first certain detection (kill / vent / ability / task) or the second alive chat outside a meeting. VIP and above are exempt.", "确定的检测(击杀/通风口/能力/任务)1次、会议外聊天2次即移出并限制其再次进入本房间。VIP以上除外。"));
            _descriptors.Add(Bool("anticheat.announce", gJa, gEn, "退出させたことを全員に知らせる", "Announce removals to everyone", _cheatAnnounceKick)
                .Tip("チート検知で退出させた時、誰をなぜ退出させたかを全員のチャットに1行出します。", "When the anti-cheat removes someone, one public chat line says who and why.", "因作弊检测移出玩家时，在所有人的聊天中显示一行：谁以及原因。"));
            _descriptors.Add(Bool("anticheat.endgame", gJa, gEn, "確実なチートで試合を止める", "Stop the match on a sure cheat", _cheatEndGame)
                .Tip("登録オフの部屋の試合中に、ありえない操作(キルできない役職のキルなど)で人を退出させた時、その試合をすぐに終わりにします(廃村と同じ終わり方)。ホストは自動で、ほかの人は「もう一度プレイ」でロビーに戻り、名前なしで理由を知らせます。インポスターのタスクのように試合が変わらない操作や、ラグかもしれない検知では止めません。1試合1回・1時間3回まで。「チートの人を自動で退出」がオンの時だけ。",
                    "Unregistered rooms: when a player is removed mid-match for an impossible action (e.g. a kill by a role that cannot kill), the match ends at once, like the haison. The host returns by itself, the others with Play Again; then all are told why, with no name. Not for actions that change nothing (an impostor's task) or possible lag. Once per match, 3 times an hour. Needs 'Remove cheaters automatically'.",
                    "在未注册房间的对局中，因不可能的操作(如不能击杀的职业击杀了人)移出玩家时，立即结束本局(与废局相同的结束方式)。房主自动回到大厅，其他人按“再玩一次”回来，然后不点名地告诉大家原因。伪装者做任务这类不改变对局的操作、可能是延迟的检测不会结束对局。每局1次、每小时3次为限。仅在“自动移出作弊者”开启时有效。"));
            _descriptors.Add(Bool("anticheat.callout", gJa, gEn, "インポを言い当てた人を知らせる", "Tell me about impostor callouts", _cheatCallout)
                .Tip("登録オフの部屋で、生きているクルーがまだ何もしていないインポスターを会議で言い当てたら、ホストの画面にだけ出します(インポスターが見えるチートの目印。退出はさせません)。手がかりのない会議で、そういうインポスターに何試合も投票する人も知らせます。ホストが生きたクルーの間はネタバレになるので、死亡後か試合後に出します。", "In unregistered rooms: when a living crewmate names, in a meeting, impostors that have done nothing yet, the host alone is told (a sign of a role-seeing cheat; nobody is removed). Also someone who, game after game, votes for such impostors in meetings with no clue yet. While the host is a living crewmate it waits until the host dies or the game ends.", "在未注册房间中，存活的船员在会议中点中尚未行动的伪装者时，只在房主的画面上提示(能看到伪装者的作弊的迹象，不会移出)。在没有线索的会议中多局投票给这类伪装者的人也会提示。房主作为存活船员时为避免剧透，会在死亡后或赛后显示。"));
            _descriptors.Add(Bool("anticheat.remoterules", gJa, gEn, "Aegisの判定値をGitHubから更新", "Update Aegis rules from GitHub", _cheatRemoteRules)
                .Tip("Aegisが判定に使う数値(チャット連投の回数、スピードの倍率など)とルールの強さを、GitHubの定義ファイルから取って更新します。誤検知や抜け穴をMODの更新なしで直せます(ルールの強さはゆるくする方向だけ。数値は項目ごとに決めた範囲の中で上下します)。オフなら数値とNGワードは組み込みのものを使い、共有BANは使いません。ただし、退出させないようにゆるめる変更(知らせるだけ・オフ)、必要な版、記録を消す依頼の一覧は、オフでも受け取ります。/ac rules で確認。", "Takes the numbers Aegis judges with (chat flood count, speed multiplier...) and the rule levels from the definitions file on GitHub, so false positives and loopholes get fixed without a mod update (rule levels only get more lenient; the numbers move either way within fixed ranges). Off: built-in numbers and NG words, no shared bans; but changes that make a rule stop removing players (notice only / off), the required version and the list of erase requests are still received. /ac rules shows them.", "从GitHub的定义文件获取Aegis判定用的数值(刷屏次数、速度倍率等)和规则强度并更新，无需更新模组即可修正误检和漏洞(规则强度只会放宽；数值在每项固定的范围内上下调整)。关闭则使用内置数值和违禁词，不使用共享限制进入名单，但让规则不再移出玩家的放宽更改(仅提示・关闭)、所需版本和删除记录请求的名单仍会接收。/ac rules 查看。"));
            _descriptors.Add(Int("anticheat.jitter", gJa, gEn, "Aegisの判定値を部屋ごとにずらす(%)", "Vary Aegis limits per lobby (%)", _cheatJitter, 0, Net.AegisRules.MaxJitterPercent, 1)
                .Tip("Aegisの判定値(スピードの倍率、チャット連投の回数と秒数など)を、部屋ごとにこの%の範囲でランダムに上下させます。公開されている定義ファイルの値ぎりぎりを狙うチートを防ぎます(退出につながる値は、厳しくする側へはこの%の半分まで)。ずらした値はホストだけが見られます(/ac rules lobby とログ)。0 = ずらさない。既定 10。",
                    "Moves the Aegis limits (speed multiplier, chat flood count and seconds...) up or down at random within this percentage for every lobby, so a cheater cannot sit just under the published values (limits that can remove a player get at most half of it toward stricter). Only the host sees the drawn values (/ac rules lobby and the log). 0 = off. Default 10.",
                    "按房间在此百分比范围内随机上下调整Aegis的判定值(速度倍率、刷屏次数和秒数等)，防止作弊者卡在公开数值的边缘(会导致移出的判定值，往更严的方向最多只调整此百分比的一半)。调整后的值只有房主能看到(/ac rules lobby 和日志)。0 = 不调整。默认 10。"));
            _descriptors.Add(Bool("anticheat.banladder", gJa, gEn, "確実なチートは長期BAN(30日→180日→無期限)", "Long bans for certain cheats (30 d, 180 d, permanent)", _cheatBanLadder)
                .Tip("確実な検知(キルできない役のキルなど)で退出させた人を、自分が立てるすべての部屋でBANします。1回目30日、2回目180日、3回目から無期限。解除は /aegis unban(回数は、BANが終わってから1年残ります)。証拠の記録も残します。オフなら、その部屋だけのBAN。なりすましで他人をはめることも理論上はできるので、誤りの申し立ては証拠の記録で確かめてください。",
                    "Players removed for a certain detection (a kill by a role that cannot...) are banned from every lobby you host: 30 days the first time, 180 days the second, permanent from the third. /aegis unban lifts one (the count is kept for a year after a ban ends). An evidence record is kept. Off: a ban for that room only. A spoofing cheater could in theory frame someone: check appeals against the evidence record.",
                    "因确定的检测(不能击杀的职业击杀等)被移出的玩家，将被限制进入你创建的所有房间：第1次30天，第2次180天，第3次起永久。/aegis unban 解除(次数在限制进入结束后保留1年)。同时保存证据记录。关闭则只限制进入该房间。理论上作弊者可以冒充他人陷害别人，申诉请对照证据记录确认。"));
            _descriptors.Add(Bool("anticheat.autoreport", gJa, gEn, "確実なチートを公式に自動通報", "Auto-report certain cheats to Among Us", _cheatAutoReport)
                .Tip("既定はオフ（自動では誰も通報しません）。オンにすると、確実な検知で退出させる前に Among Us の公式の通報(チート)を送ります。同じ人へは30日に1回、1時間に5件まで。/opt anticheat.autoreport on でもオンにできます。オフのままでも /aegis report <名前> で1人ずつ通報できます。通報は取り消せません(なりすましで他人をはめることも理論上はできます)。",
                    "Off by default (nobody is reported automatically). When on, Among Us's own player report (cheating) is sent before a removal for a certain detection: at most once per player every 30 days and 5 an hour. /opt anticheat.autoreport on also turns it on. Even while off, /aegis report <name> reports one player. A report cannot be taken back (a spoofing cheater could in theory frame someone).",
                    "默认关闭（不会自动举报任何人）。开启后，在因确定的检测移出玩家前向 Among Us 官方发送举报(作弊)：同一玩家30天1次，每小时最多5次。也可用 /opt anticheat.autoreport on 开启。即使关闭，也可用 /aegis report <名字> 单独举报。举报无法撤回(理论上作弊者可以冒充他人陷害别人)。"));
            _descriptors.Add(Bool("anticheat.sharedbans", gJa, gEn, "共有BANリストを使う", "Use the shared ban list", _cheatSharedBans)
                .Tip("署名されたAegisの定義ファイルにある共有BANリストの人が入ってきたら、その人に「この部屋には入れません」と知らせて(登録オフの部屋では名前を出さずに全員へ)、30秒後に退出させます。待つ間にチートの検知・NGワード・連投があった時と、試合を始める時はすぐに退出(同じ部屋にまた来たら、すぐにその部屋へのBAN付きで退出)。リストには名前もフレンドコードも載らず、推測できないPUIDのハッシュだけが載ります。VIP以上は対象外。",
                    "When someone on the shared ban list of the signed Aegis definitions file joins, they are told they cannot join (in unregistered rooms: one public line without a name) and removed 30 s later, at once on a cheat detection, an NG word, a chat flood or a game start (back in the same lobby: removed at once with a ban for that room). The list holds no names and no friend codes, only a hash of the PUID, which cannot be guessed back. VIP and above are exempt.",
                    "签名的Aegis定义文件中的共享限制进入名单上的玩家加入时，告知其“无法进入本房间”(未注册房间中不显示名字，向所有人发一条)，30秒后移出；等待期间出现作弊检测、违禁词、刷屏或开始游戏时立即移出(再次进入同一房间时立即移出并限制其进入该房间)。名单上没有名字也没有好友编号，只有无法推测的PUID哈希。VIP以上除外。"));
            _descriptors.Add(Bool("general.ignoreversion", gJa, gEn, "バージョン不一致を無視", "Ignore version mismatch", _ignoreVersion)
                .Tip("ゲームのバージョンが対応版と違ってもMODを動かします（自己責任）。", "Keeps the mod active on an unsupported game version (at your own risk).", "游戏版本不匹配时仍启用模组（风险自负）。"));
            _descriptors.Add(Bool("credits.show", gJa, gEn, "クレジット表示", "Show credits", _showCredits)
                .Tip("メニューにPocketRolesのクレジット行を表示します。", "Shows the PocketRoles credit line in the menu.", "在菜单中显示 PocketRoles 的制作信息。"));

            const string lJa = "ロビー", lEn = "Lobby";
            _descriptors.Add(Bool("lobby.autorehost", lJa, lEn, "自動再ホスト", "Auto re-host", _autoRehost)
                .Tip("切断されたら自動で新しい部屋を立て直します。", "Creates a new lobby automatically after an unexpected disconnect.", "意外断线后自动重新创建房间。"));
            _descriptors.Add(Bool("lobby.autopublic", lJa, lEn, "自動公開", "Auto public", _autoPublic)
                .Tip("部屋を作った数秒後に自動で公開にします。", "Makes the lobby public a few seconds after it is created.", "创建房间几秒后自动设为公开。"));
            _descriptors.Add(Int("lobby.autopublicdelay", lJa, lEn, "自動公開までの秒数", "Auto public delay (s)", _autoPublicDelay, 0, 60, 5)
                .Tip("自動公開までに待つ秒数。", "Seconds to wait before making the lobby public.", "自动公开前等待的秒数。"));
            _descriptors.Add(Int("lobby.rehostmax", lJa, lEn, "再ホスト最大回数", "Re-host max attempts", _rehostMaxAttempts, 1, 10, 1)
                .Tip("自動再ホストを連続で試す最大回数。", "Maximum consecutive automatic re-host attempts.", "自动重建房间的最大连续尝试次数。"));
            _descriptors.Add(Int("lobby.maxping", lJa, lEn, "高PINGなら部屋を作り直す(ms)", "Re-host when ping above (ms)", _maxHostPing, 0, 300, 10)
                .Tip("部屋を作った時に測ったサーバーまでの往復時間（PING）がこの値(ms)を超え、5秒たってもまだ自分しかいなければ「作り直しますか？」と聞きます。15秒答えがなければ作り直します（続けて最大3回）。既定 0 = しない。", "If the round trip to the game server measured when the lobby is created is above this (ms) and you are still alone 5 s later, you are asked whether to re-create it; with no answer for 15 s it is re-created (up to 3 times in a row). Default 0 = off.", "创建房间时测得的到服务器的往返时间高于此值(ms)，且 5 秒后房间里仍只有自己时，会询问是否重建；15 秒内未回答则自动重建（连续最多 3 次）。默认 0 = 关闭。"));
            _descriptors.Add(Int("lobby.afkkick", lJa, lEn, "AFKキック(分, 0=なし)", "AFK kick (min, 0 = off)", _afkKickMinutes, 0, 30, 1)
                .Tip("ロビーでこの分数だけ動きも発言もない人に30秒前に警告し、退出させます（BANではありません）。ホスト・VIP・モデレーター・管理者は対象外。未登録の部屋でも動きます。0 = しない。", "A lobby player who neither moves nor chats for this many minutes is warned 30 s ahead and then kicked (not banned). Host, VIPs, moderators and admins are exempt. Works in unregistered lobbies too. 0 = off.", "在大厅中这段分钟数内既不移动也不发言的玩家会在 30 秒前收到警告，然后被移出（不是限制进入）。房主、VIP、管理员除外。未注册房间也可用。0 = 关闭。"));
            _descriptors.Add(Bool("lobby.autostart", lJa, lEn, "自動開始", "Auto start", _autoStart)
                .Tip("設定した人数が揃ったら自動でゲームを開始します。", "Starts the game automatically once enough players are in.", "凑齐设定人数后自动开始游戏。"));
            _descriptors.Add(Int("lobby.autostartplayers", lJa, lEn, "自動開始の人数", "Auto start players", _autoStartPlayers, 4, 15, 1)
                .Tip("自動開始が始まる人数。", "Player count that triggers the automatic start.", "触发自动开始的人数。"));
            _descriptors.Add(Int("lobby.autostartcountdown", lJa, lEn, "開始カウントダウン(秒)", "Start countdown (s)", _autoStartCountdown, 1, 30, 1)
                .Tip("自動開始・強制開始前のカウントダウン秒数。", "Countdown seconds before an automatic or forced start.", "自动或强制开始前的倒计时秒数。"));
            _descriptors.Add(Choice("lobby.timermode", lJa, lEn, "ロビー残り時間の動作", "Lobby timer action", _timerMode, TimerModeChoices)
                .Tip("ロビーの制限時間が切れそうなときの動作（延長 / 廃村 / 通知のみ）。", "What to do when the lobby timer is about to expire (extend / haison / notify only).", "房间倒计时快结束时的处理（延长 / 废局 / 仅通知）。"));
            _descriptors.Add(Int("lobby.timerwarnat", lJa, lEn, "残り時間の警告(秒)", "Timer warning at (s)", _timerWarnAt, 30, 300, 10)
                .Tip("残り時間がこの秒数になったら警告して動作します。", "Lobby seconds left at which the warning and the action happen.", "剩余秒数达到此值时发出警告并执行动作。"));
            _descriptors.Add(Int("lobby.extenddelay", lJa, lEn, "警告から延長までの秒数", "Extend notice delay (s)", _extendNoticeDelay, 0, 60, 1)
                .Tip("警告から延長・廃村までの待ち秒数。", "Seconds between the warning and the extension / haison.", "从警告到延长或废局之间的等待秒数。"));
            _descriptors.Add(Bool("lobby.autoregion", lJa, lEn, "自動で最速の地域を選ぶ", "Auto lowest-ping region", _autoRegion)
                .Tip("「部屋を作る」の画面を開いた時だけ各地域のpingを測り、最も速い地域を選びます。部屋コードで参加する時は変わりません。計測に数秒かかるので、すぐ「作成」を押した部屋は元の地域のままで、次に画面を開いた時に切り替わります。", "Pings the regions when the CREATE GAME screen opens and picks the fastest one. Joining a room by its code never changes your region. The run takes a few seconds: a room created before it ends keeps the current region and the switch happens the next time the screen opens.", "只在打开“创建房间”界面时测试各区域延迟并选择最快的区域。用房间代码加入时不会更改区域。测量需要几秒，在此之前就点“创建”的房间仍使用原区域，会在下次打开该界面时切换。"));
            _descriptors.Add(Bool("lobby.dleks", lJa, lEn, "逆スケルド(Dleks)を出す", "Offer Dleks map", _enableDleks)
                .Tip("マップ選択に逆スケルド（Dleks）を出します。バニラの人も遊べます。", "Offers the mirrored Skeld (Dleks) in the map picker; vanilla players can play it.", "在地图选择中提供镜像 Skeld（Dleks）；原版玩家也可以游玩。"));

            const string hJa = "ホスト支援", hEn = "Host tools";
            _descriptors.Add(Bool("gm", hJa, hEn, "ゲームマスター", "Game Master", _gameMaster)
                .Tip("ホストは役職を持たず、開始時に死亡して観戦・進行役になります。", "The host gets no role, dies at the start and only watches / moderates.", "房主不持有职业，开局即死亡，只观战（不参与游戏）。"));
            _descriptors.Add(Bool("hotkeys", hJa, hEn, "ホットキー有効", "Hotkeys enabled", _hotkeysEnabled)
                .Tip("廃村・会議終了・開始キャンセルのホットキーを有効にします。", "Enables the host hotkeys for haison, end meeting and cancel start.", "启用废局、结束会议、取消开始的快捷键。"));
            _descriptors.Add(Hotkey("hotkeys.haison", hJa, hEn, "廃村キー(2回押し)", "Haison key (press twice)", _hotkeyHaison, "F7")
                .Tip("このキーを3秒以内に2回押すとゲームを廃村で終了します。", "Press this key twice within 3 seconds to end the game as haison.", "3 秒内按两次此键以废局结束游戏。"));
            _descriptors.Add(Hotkey("hotkeys.endmeeting", hJa, hEn, "会議終了キー(2回押し)", "End meeting key (press twice)", _hotkeyEndMeeting, "F8")
                .Tip("このキーを3秒以内に2回押すと会議を強制終了します。", "Press this key twice within 3 seconds to force-end the meeting.", "3 秒内按两次此键强制结束会议。"));
            _descriptors.Add(Hotkey("hotkeys.cancelstart", hJa, hEn, "開始キャンセルキー", "Cancel start key", _hotkeyCancelStart, "F9")
                .Tip("このキーで開始カウントダウンを止めます。", "This key cancels the start countdown.", "此键取消开始倒计时。"));
            // v0.4b permissions (Admin.txt / Moderator.txt / VIP.txt) live on the host-tools page
            _descriptors.Add(Bool("perm.adminsettings", hJa, hEn, "アドミンにも設定変更を許可", "Admins can change settings", _permAdminSettings)
                .Tip("Admin.txt に載せた人もホスト用コマンドで設定を変えられます。", "Players in Admin.txt may change settings with the host commands.", "Admin.txt 中的玩家也可用房主命令更改设置。"));
            _descriptors.Add(Bool("perm.modkick", hJa, hEn, "モデレーターのキック/BAN", "Moderators can kick", _permModKick)
                .Tip("Moderator.txt に載せた人が /kick と /ban を使えます。", "Players in Moderator.txt may use /kick and /ban.", "Moderator.txt 中的玩家可使用 /kick 和 /ban。"));
            _descriptors.Add(Bool("perm.vipmarker", hJa, hEn, "VIP の名前に★", "VIP star marker", _permVipMarker)
                .Tip("VIP.txt に載せた人の名前に★を付け、個別に挨拶します。", "Marks players in VIP.txt with a star and greets them personally.", "给 VIP.txt 中的玩家名字加★并单独问候。"));
            // v0.4e guide room (the role-room code itself is text: /move <CODE> or the config file)
            _descriptors.Add(Bool("guide.overlay", hJa, hEn, "部屋コードを大きく表示", "Big room-code overlay", _guideShowCodeOverlay)
                .Tip("ロビー中、ホストの画面左上に部屋コードを大きく表示します（/code で切替。案内部屋の名前に書き写す用）。", "Shows the room code large at the top-left of the host's lobby screen (/code toggles it; copy it into the guide room's name).", "在大厅中于房主屏幕左上角大字显示房间代码（/code 切换；用于抄写到引导房的名字）。"));
            _descriptors.Add(Bool("guide.autoreg", hJa, hEn, "/move 後に登録部屋へ作り直す", "/move: re-create as registered", _guideAutoRecreateRegistered)
                .Tip("/move の 30 秒後に、この便利ホスト部屋を MOD 登録ありの追加役職部屋として作り直します（全員がコードで入り直し）。", "30 s after /move, re-creates this unregistered lobby as a registered mod-roles lobby (everyone rejoins with the new code).", "/move 30 秒后，把这个未注册房间重建为已注册的模组职业房（所有人用新代码重新加入）。"));
            // v0.4b vanilla extended ranges (also host-tools page)
            _descriptors.Add(Bool("vanilla.ranges", hJa, hEn, "バニラ設定の範囲拡張", "Vanilla extended ranges", _vanExtendedRanges)
                .Tip("バニラの数値設定をバニラの上限・下限を超えて設定できるようにします。", "Lets the vanilla numeric settings go beyond their vanilla limits.", "允许原版数值设置超出原版上下限。"));
            _descriptors.Add(Float("vanilla.killmin", hJa, hEn, "キルCD最小(秒)", "Kill cooldown min (s)", _vanKillMin, 0f, 60f, 0.5f)
                .Tip("設定画面で選べるキルクールダウンの最小値（秒）。", "Lowest kill cooldown (s) selectable in the settings screen.", "设置界面可选的最小击杀冷却（秒）。"));
            _descriptors.Add(Float("vanilla.killmax", hJa, hEn, "キルCD最大(秒)", "Kill cooldown max (s)", _vanKillMax, 10f, 600f, 5f)
                .Tip("設定画面で選べるキルクールダウンの最大値（秒）。", "Highest kill cooldown (s) selectable in the settings screen.", "设置界面可选的最大击杀冷却（秒）。"));
            _descriptors.Add(Float("vanilla.killstep", hJa, hEn, "キルCDの刻み(秒)", "Kill cooldown step (s)", _vanKillStep, 0.5f, 10f, 0.5f)
                .Tip("キルクールダウンの矢印1回あたりの増減（秒）。", "Change per arrow click of the kill cooldown (s).", "击杀冷却每次点击箭头的增减（秒）。"));
            _descriptors.Add(Int("vanilla.votemin", hJa, hEn, "投票時間最小(秒)", "Voting time min (s)", _vanVoteMin, 0, 300, 5)
                .Tip("投票時間の最小値（秒、0 = 投票なし）。", "Lowest voting time (s; 0 = no voting phase).", "投票时间的最小值（秒，0 = 无投票阶段）。"));
            _descriptors.Add(Int("vanilla.votemax", hJa, hEn, "投票時間最大(秒)", "Voting time max (s)", _vanVoteMax, 15, 3600, 30)
                .Tip("投票時間の最大値（秒）。", "Highest voting time (s).", "投票时间的最大值（秒）。"));
            _descriptors.Add(Int("vanilla.discussmax", hJa, hEn, "議論時間最大(秒)", "Discussion time max (s)", _vanDiscussMax, 0, 3600, 30)
                .Tip("議論時間の最大値（秒）。", "Highest discussion time (s).", "讨论时间的最大值（秒）。"));
            _descriptors.Add(Int("vanilla.emergencymax", hJa, hEn, "緊急会議CD最大(秒)", "Emergency cooldown max (s)", _vanEmergencyMax, 0, 600, 10)
                .Tip("緊急会議クールダウンの最大値（秒）。", "Highest emergency-meeting cooldown (s).", "紧急会议冷却的最大值（秒）。"));
            _descriptors.Add(Int("vanilla.gauses", hJa, hEn, "守護天使の護衛回数(登録あり,0=無制限)", "Guardian Angel uses (reg., 0=unlimited)", _vanGaUses, 0, 9, 1).Zh("守护天使保护次数(已注册,0=无限)"));
            if (HostShieldUnlocked) _descriptors.Add(Int("host.shield", hJa, hEn, "ホストのシールド回数(登録あり)", "Host shield kills (reg.)", _hostShieldKills, 0, 9, 1).Zh("房主护盾次数(已注册)"));
            _descriptors.Add(Int("compat.tasks.common", hJa, hEn, "配るコモン数(登録オフ,0=設定)", "Common tasks dealt (unreg., 0=setting)", _compatCommonTasks, 0, 60, 1).Zh("实际分发普通任务数(未注册,0=按设置)"));
            _descriptors.Add(Int("compat.tasks.short", hJa, hEn, "配るショート数(登録オフ,0=設定)", "Short tasks dealt (unreg., 0=setting)", _compatShortTasks, 0, 60, 1).Zh("实际分发短任务数(未注册,0=按设置)"));
            _descriptors.Add(Int("compat.tasks.long", hJa, hEn, "配るロング数(登録オフ,0=設定)", "Long tasks dealt (unreg., 0=setting)", _compatLongTasks, 0, 60, 1).Zh("实际分发长任务数(未注册,0=按设置)"));
            _descriptors.Add(Int("vanilla.taskmax", hJa, hEn, "タスク数最大", "Task count max", _vanTaskMax, 1, 60, 1)
                .Tip("共通・短い・長いタスク数の最大値。", "Highest common / short / long task count.", "普通、短、长任务数量的最大值。"));

            const string cJa = "チャット", cEn = "Chat";
            _descriptors.Add(Bool("chat.welcomesettings", cJa, cEn, "挨拶に設定を含める", "Welcome includes settings", _welcomeIncludeSettings)
                .Tip("挨拶に現在の役職設定を付けます（既定オフ。設定は /cmd s でいつでも見られます）。", "Appends the current role settings to the welcome (off by default; /cmd s shows them any time).", "在欢迎语中附上当前职业设置（默认关闭。随时可用 /cmd s 查看）。"));
            _descriptors.Add(Choice("chat.rulesmode", cJa, cEn, "挨拶のルール行", "Rules line", _rulesMode, RulesModeChoices)
                .Tip("挨拶に載せるルール行（なし / /rules で設定した独自ルール）。", "Rules line in the welcome (none / the custom text set with /rules).", "欢迎语中的规则行（无 / 用 /rules 设置的自定义规则）。"));
            _descriptors.Add(Bool("chat.welcomeall", cJa, cEn, "挨拶を3言語で送る", "Welcome in all languages", _welcomeAllLanguages)
                .Tip("短い挨拶（2行）をその人の言語→残り2言語の順に送ります（既定オン）。", "Sends the short welcome (2 lines) in the player's language, then the other two (on by default).", "把简短欢迎语（2行）按该玩家的语言→其余2种语言的顺序发送（默认开启）。"));
            _descriptors.Add(Bool("chat.playercommands", cJa, cEn, "プレイヤーのコマンド", "Player commands", _playerCommands)
                .Tip("ホスト以外のプレイヤーも /help などのコマンドを使えます。", "Lets non-host players use chat commands such as /help.", "允许非房主玩家使用 /help 等聊天命令。"));
            _descriptors.Add(Bool("chat.allcommands", cJa, cEn, "全コマンド", "All commands", _allCommands)
                .Tip("オフにするとチャットコマンドを全て無効にします（/mod on のみ可）。", "Off disables every chat command (only /mod on still works).", "关闭后禁用所有聊天命令（仅 /mod on 可用）。"));
            // v0.5.5 NG words (Chat.NgWords)
            _descriptors.Add(Bool("ng", cJa, cEn, "NGワード（暴言に注意・退出）", "NG words (warn / remove)", _ngFilter)
                .Tip("ほかの人のチャット（ロビーでも試合中でも、登録オン・オフどちらの部屋でも）をNGワードの一覧と照らします。決めた回数（既定 3）の前までは毎回全員に注意、その回数で退出です。ホスト・VIP・モデレーター・アドミンは対象外。一覧は組み込み（GitHubの定義ファイルで更新）と BepInEx\\PocketRoles\\NgWords.txt。/ng で状態の確認と編集。",
                    "Checks the other players' chat (lobby and game, registered or not) against the NG word list: a public warning on each hit before the set count (default 3), removal at it. Host, VIPs, moderators and admins are exempt. List: built-in (updated from the GitHub definitions file) plus BepInEx\\PocketRoles\\NgWords.txt. /ng shows and edits it.",
                    "用违禁词表检查其他玩家的聊天（大厅和对局中，注册与否都适用）：达到设定次数（默认 3）前每次公开警告，达到则移出。房主、VIP、版主、管理员除外。词表为内置（随 GitHub 定义文件更新）加上 BepInEx\\PocketRoles\\NgWords.txt。/ng 查看和编辑。"));
            _descriptors.Add(Int("ng.kickat", cJa, cEn, "NGワード何回目で退出(0=しない)", "Remove at NG hit # (0 = never)", _ngKickAt, 0, 5, 1)
                .Tip("同じ部屋でNGワードにこの回数当たったら退出させます（それまでは毎回、全員に見える注意。5秒以内の続けての発言は1回と数えます）。0 = 注意だけで退出させません。既定 3。",
                    "Removes a player at this many NG-word hits in one room (each hit before that gets a public warning; lines within 5 s count once). 0 = warnings only, no removal. Default 3.",
                    "同一房间内命中违禁词达到此次数即移出（在此之前每次都公开警告；5 秒内的连续发言算 1 次）。0 = 只警告不移出。默认 3。"));
            _descriptors.Add(Bool("ng.ban", cJa, cEn, "NGワードの退出に部屋バン", "Room ban on NG removal", _ngBan)
                .Tip("NGワードで退出させる時、この部屋に戻れないようにします（オフなら普通のキックで、入り直せます）。",
                    "The NG-word removal also bans the player from this room (off = a plain kick; they can rejoin).",
                    "因违禁词移出时同时禁止其回到本房间（关闭则为普通踢出，可以重新加入）。"));
            _descriptors.Add(Bool("ng.announce", cJa, cEn, "NGワードの退出を全員に知らせる", "Announce NG removals", _ngAnnounce)
                .Tip("NGワードで退出させた時、全員のチャットに1行出します。",
                    "One public chat line when a player is removed for NG words.",
                    "因违禁词移出玩家时，在所有人的聊天中显示一行。"));
            // v0.4b chat translation rows (the DeepL key is a file, never a row)
            _descriptors.Add(Bool("translate.enabled", cJa, cEn, "チャット翻訳", "Chat translation", _trEnabled)
                .Tip("既定はオフ（チャットはどこにも送られません）。オンにすると外国語のチャットを自動で翻訳します（文章が Google / DeepL に送られます）。/opt translate.enabled on でもオンにできます。",
                    "Off by default (no chat text leaves this PC). When on, foreign-language chat is auto-translated (the text is sent to Google / DeepL). /opt translate.enabled on also turns it on.",
                    "默认关闭（聊天内容不会发送到任何地方）。开启后自动翻译外语聊天（文本会发送到 Google / DeepL）。也可用 /opt translate.enabled on 开启。"));
            _descriptors.Add(Bool("translate.notice", cJa, cEn, "オフの時のお知らせ", "\"Translation is off\" notice", _trOffNotice)
                .Tip("翻訳がオフの間、ゲームを起動してから最初の部屋で 1 回だけ、自分の画面に「翻訳はオフです」の 1 行を出します（ほかの人には見えません）。翻訳を使わないならオフにしてください。/opt translate.notice off でも消せます。",
                    "While translation is off, put one \"translation is off\" line on your own screen in the first lobby after the game starts (nobody else sees it). Turn it off if you never use translation. /opt translate.notice off also silences it.",
                    "翻译关闭期间，在启动游戏后的第一个房间里，只在你自己的画面上显示一行“翻译已关闭”（其他人看不到）。不使用翻译的话请关闭。也可用 /opt translate.notice off 关闭。"));
            _descriptors.Add(Choice("translate.provider", cJa, cEn, "翻訳サービス", "Translation provider", _trProvider, TranslateProviderChoices)
                .Tip("翻訳サービス。auto は deepl-key.txt にキーがあれば DeepL、なければ Google。", "Translation service; auto = DeepL when deepl-key.txt has a key, else Google.", "翻译服务；auto 表示 deepl-key.txt 有密钥时用 DeepL，否则用 Google。"));
            _descriptors.Add(new OptionDescriptor
            {
                Key = "translate.target", SectionJa = cJa, SectionEn = cEn, NameJa = "翻訳先の言語", NameEn = "Translation language", Kind = OptionKind.Choice,
                Min = 0, Max = 2, Step = 1, Choices = TranslateLangChoices,
                GetNumber = () => { int i = Array.IndexOf(TranslateLangChoices, TranslateTargetLang); return i < 0 ? 0 : i; },
                SetNumber = v => { int i = (int)Math.Round(Clamp(v, 0, 2)); TranslateTargetLang = TranslateLangChoices[i]; },
            }.Tip("ホストが読む翻訳の言語。", "Language the host reads translations in.", "房主阅读翻译的语言。"));
            _descriptors.Add(Bool("translate.showhost", cJa, cEn, "翻訳をホスト画面に表示", "Show translation on host", _trShowOnHost)
                .Tip("翻訳をホストの画面に表示します（送信はしません）。", "Shows translations on the host's screen only.", "仅在房主屏幕上显示翻译。"));
            _descriptors.Add(Bool("translate.broadcast", cJa, cEn, "翻訳を全員に送る", "Broadcast translation", _trBroadcastToAll)
                .Tip("翻訳をチャットで全員に送ります。", "Sends the translation to everyone as a chat message.", "把翻译作为聊天消息发送给所有人。"));
            _descriptors.Add(Bool("translate.compat", cJa, cEn, "登録オフでも外国語へ翻訳", "Translate into foreign languages (unregistered)", _trForeignInCompat)
                .Tip("登録オフの部屋で、外国語の人がいる時だけ、チャットをその人の言葉に訳して全員に流します(個別に送れないため)。", "In unregistered rooms, while a foreign-language player is here, chat is translated into their language and posted for everyone (no private messages there).", "在未注册房间中，只在有外语玩家时，把聊天翻译成其语言并发给所有人(无法私聊)。"));
            _descriptors.Add(Bool("translate.players", cJa, cEn, "外国語の人へ翻訳", "Translate for players", _trForPlayers)
                .Tip("外国語を選んだプレイヤーに、チャットをその言語に訳して個別に送ります。", "Privately sends chat translated into each foreign player's /lang language.", "把聊天翻译成外语玩家所选语言并私聊发送。"));
            _descriptors.Add(Bool("translate.autodetect", cJa, cEn, "言語の自動判定", "Auto-detect language", _trAutoDetect)
                .Tip("/lang 未設定の人が中国語・英語で書いたら表示言語を自動で切り替えます。", "Switches a player's language automatically when they write in Chinese or English.", "未设置 /lang 的玩家用中文或英文发言时自动切换其显示语言。"));
            _descriptors.Add(Int("translate.minchars", cJa, cEn, "翻訳の最小文字数", "Translate min characters", _trMinChars, 1, 50, 1)
                .Tip("この文字数未満のメッセージは翻訳しません。", "Messages shorter than this are not translated.", "少于此字数的消息不翻译。"));
            _descriptors.Add(Int("translate.maxperminute", cJa, cEn, "翻訳の1分あたり上限", "Translations per minute", _trMaxPerMinute, 1, 120, 5)
                .Tip("1分あたりの翻訳回数の上限（超えた分は翻訳しません）。", "Maximum translations per minute (extra messages are skipped).", "每分钟翻译次数上限（超出的消息不翻译）。"));

            const string vJa = "見た目（ホストのみ）", vEn = "Cosmetics (host only)";
            _descriptors.Add(Bool("cos.enabled", vJa, vEn, "見た目のカスタマイズ", "Custom cosmetics", _cosEnabled)
                .Tip("ホスト画面だけの見た目カスタマイズ（帽子・音楽・背景など）を有効にします。", "Enables the host-only cosmetics (hats, music, backgrounds...).", "启用仅房主可见的外观自定义（帽子、音乐、背景等）。"));
            _descriptors.Add(Choice("cos.music", vJa, vEn, "ロビー音楽", "Lobby music", _cosMusic, LobbyMusicChoices)
                .Tip("ロビーで流す音楽（自作ファイル / バニラ / 無音）。", "Lobby music: custom file, vanilla theme or mute.", "大厅音乐（自定义文件 / 原版 / 静音）。"));
            _descriptors.Add(Float("cos.musicvolume", vJa, vEn, "ロビー音楽の音量", "Lobby music volume", _cosMusicVolume, 0f, 1f, 0.01f)
                .Tip("自作ロビー音楽の音量（0〜1）。", "Volume of the custom lobby music (0..1).", "自定义大厅音乐的音量（0～1）。"));
            _descriptors.Add(Bool("cos.lobbypaint", vJa, vEn, "ロビーの壁絵", "Lobby paint", _cosLobbyPaint)
                .Tip("ロビーの壁に images/lobbypaint.png を表示します。", "Shows images/lobbypaint.png on the lobby wall.", "在大厅墙上显示 images/lobbypaint.png。"));
            _descriptors.Add(Bool("cos.dropship", vJa, vEn, "ドロップシップ装飾", "Dropship decoration", _cosDropship)
                .Tip("ドロップシップに images/dropship.png を飾ります。", "Decorates the dropship with images/dropship.png.", "用 images/dropship.png 装饰飞船。"));
            _descriptors.Add(Bool("cos.menubg", vJa, vEn, "メニュー背景", "Menu background", _cosMenuBackground)
                .Tip("メインメニューの背景を images/menu.png に置き換えます。", "Replaces the main-menu background with images/menu.png.", "将主菜单背景替换为 images/menu.png。"));
            _descriptors.Add(Bool("cos.cursor", vJa, vEn, "マウスカーソル", "Mouse cursor", _cosCursor)
                .Tip("マウスカーソルを images/cursor.png にします。", "Uses images/cursor.png as the mouse cursor.", "将鼠标光标改为 images/cursor.png。"));
        }

        /// <summary>Descriptor by TrySet key (null when unknown).</summary>
        public static OptionDescriptor Descriptor(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string k = key.Trim().ToLowerInvariant();
            foreach (var d in _descriptors) if (d.Key == k) return d;
            return null;
        }

        // ------------------------------------------------------------------ parsing / TrySet

        private static bool ParseBool(string v, out bool b)
        {
            b = false;
            if (string.IsNullOrEmpty(v)) return false;
            switch (v.Trim().ToLowerInvariant())
            {
                case "1": case "on": case "true": case "yes": case "y": case "はい": case "オン": b = true; return true;
                case "0": case "off": case "false": case "no": case "n": case "いいえ": case "オフ": b = false; return true;
            }
            return false;
        }

        private static bool ParseFloat(string v, out float f) => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f);

        private static bool SetFloat(ConfigEntry<float> e, string value, float min, float max, string label, out string message)
        {
            if (!ParseFloat(value, out var f)) { message = Lang.TF("opt.badnumber", "{0}: 数値を指定してください", "{0}: expected a number", label); return false; }
            f = Math.Max(min, Math.Min(max, f));
            e.Value = f;
            message = $"{label} = {f.ToString(CultureInfo.InvariantCulture)}";
            return true;
        }

        private static bool SetInt(ConfigEntry<int> e, string value, int min, int max, string label, out string message)
        {
            if (!int.TryParse(value, out var i)) { message = Lang.TF("opt.badnumber", "{0}: 数値を指定してください", "{0}: expected a number", label); return false; }
            i = Math.Max(min, Math.Min(max, i));
            e.Value = i;
            message = $"{label} = {i}";
            return true;
        }

        private static bool SetBool(ConfigEntry<bool> e, string value, string label, out string message)
        {
            if (!ParseBool(value, out var b)) { message = Lang.TF("opt.badbool", "{0}: on / off を指定してください", "{0}: expected on / off", label); return false; }
            e.Value = b;
            message = $"{label} = {(b ? "on" : "off")}";
            return true;
        }

        private static bool SetString(ConfigEntry<string> e, string value, string label, out string message)
        {
            e.Value = value ?? "";
            message = string.IsNullOrEmpty(e.Value) ? $"{label} = ({Lang.T("opt.empty", "空", "empty")})" : $"{label} = {e.Value}";
            return true;
        }

        private static bool SetChoiceValue(ConfigEntry<string> e, string[] choices, string value, string label, out string message)
        {
            string t = (value ?? "").Trim().ToLowerInvariant();
            if (Array.IndexOf(choices, t) < 0) { message = $"{label}: {string.Join(" | ", choices)}"; return false; }
            SetChoice(e, choices, t);
            message = $"{label} = {t}";
            return true;
        }

        private static bool SetKey(ConfigEntry<string> e, string value, string def, string label, out string message)
        {
            if (!TryParseKey(value, out var k)) { message = Lang.TF("opt.badkey", "{0}: キー名を指定してください（例: F7, F8, Escape）", "{0}: expected a key name (e.g. F7, F8, Escape)", label); return false; }
            SetKeyName(e, k.ToString(), def);
            message = $"{label} = {k}";
            return true;
        }

        /// <summary>
        /// Sets one option from a chat command. Keys: "&lt;role&gt;.count", "&lt;role&gt;.chance", "sheriff.cooldown", "sheriff.killmadmate",
        /// "jackal.cooldown", "jackal.vent", "vampire.delay", "mayor.votes", "snitch.tasks", "lighter.vision", "speedbooster.speed",
        /// "madmate.known", "lovers.impostor", "lovers.lastthree", "arsonist.cooldown", "arsonist.vent", "witch.cooldown", "witch.mark",
        /// "assassin.guesses", "assassin.firstmeeting" (v0.4.1), "madmayor.votes", "madmayor.known", "madstuntman.lives", "madstuntman.notify",
        /// "madhawk.vision", "madhawk.speed", "worshipper.uses", "worshipper.cooldown", "jackalfriends.known", "jackalfriends.sheriff", "evilhawk.vision",
        /// "evilnekomata.voters", "evilnekomata.excludeimp", "evilnekomata.announce", "serialkiller.cooldown", "serialkiller.time", "serialkiller.meetingreset",
        /// "samurai.cooldown", "samurai.range", "samurai.stagger", "samurai.teammates" (v0.5.0), "lang", "enabled", "welcome", "roleinfo", "register", "kick", "general.ignoreversion",
        /// "lobby.autorehost", "lobby.autopublic", "lobby.autopublicdelay", "lobby.rehostmax", "lobby.maxping" (alias "maxping"), "compat.risky",
        /// "lobby.autostart", "lobby.autostartplayers", "lobby.autostartcountdown", "lobby.timermode", "lobby.timerwarnat",
        /// "lobby.extenddelay", "lobby.autoregion", "lobby.dleks", "gm", "hotkeys", "hotkeys.haison", "hotkeys.endmeeting",
        /// "hotkeys.cancelstart", "chat.welcometext", "chat.welcomesettings", "chat.playercommands", "chat.allcommands",
        /// "chat.rulesmode", "chat.rulestext", "ng", "ng.kickat", "ng.ban", "ng.announce" (v0.5.5), "cos.enabled", "cos.music", "cos.musicfile", "cos.musicvolume", "cos.lobbypaint",
        /// "cos.dropship", "cos.menubg", "cos.cursor", "credits.author", "credits.url", "credits.show",
        /// "chat.welcomeall", "translate.enabled", "translate.notice" (v0.5.5), "translate.provider", "translate.target", "translate.showhost", "translate.broadcast",
        /// "translate.players", "translate.autodetect", "translate.minchars", "translate.maxperminute", "perm.adminsettings",
        /// "perm.modkick", "perm.vipmarker", "vanilla.ranges", "vanilla.killmin", "vanilla.killmax", "vanilla.killstep",
        /// "vanilla.votemin", "vanilla.votemax", "vanilla.discussmax", "vanilla.emergencymax", "vanilla.taskmax",
        /// "guide.overlay", "guide.code", "guide.autoreg" (v0.4e guide room), "upgrade" (v0.5.5 one-time opt-in check).
        /// The DeepL key has no key here on purpose (file BepInEx/PocketRoles/deepl-key.txt).
        /// </summary>
        public static bool TrySet(string key, string value, out string message)
        {
            message = "";
            if (!Initialized) { message = "config not initialized"; return false; }
            if (string.IsNullOrWhiteSpace(key)) { message = Lang.T("opt.nokey", "キーを指定してください", "Missing key"); return false; }
            string k = key.Trim().ToLowerInvariant();
            value = (value ?? "").Trim();

            switch (k)
            {
                case "lang": case "language":
                    if (LangCore.IsAuto(value)) { Language = LangCore.Auto; message = "lang = " + LanguageLabel; return true; }
                    if (!Lang.TryNormalize(value, out var lang)) { message = "lang: auto | ja | zh | en"; return false; }
                    Language = lang; message = "lang = " + lang; return true;
                case "enabled": case "mod": return SetBool(_enabled, value, "enabled", out message);
                case "welcome": return SetBool(_welcome, value, "welcome", out message);
                case "roleinfo": return SetBool(_roleInfoAtMeeting, value, "roleinfo", out message);
                case "register": case "modded": case "+25": return SetBool(_register, value, "register", out message);
                case "anticheat": case "cheat": return SetBool(_cheatDetect, value, "anticheat", out message);
                case "anticheat.kick": case "kick": case "anticheatkick": return SetBool(_cheatAutoKick, value, "anticheat.kick", out message);   // v0.5.3: the old reserved toggle now means the real auto-kick
                case "anticheat.callout": case "callout": return SetBool(_cheatCallout, value, "anticheat.callout", out message);
                case "anticheat.announce": case "anticheatannounce": return SetBool(_cheatAnnounceKick, value, "anticheat.announce", out message);
                case "anticheat.endgame": case "endgame": case "endgameoncheat": return SetBool(_cheatEndGame, value, "anticheat.endgame", out message);   // v0.5.5 AegisMatchStop (host only: not in Commands.IsAdminOptKey)
                case "anticheat.remoterules": case "remoterules": return SetBool(_cheatRemoteRules, value, "anticheat.remoterules", out message);   // v0.5.5 AegisRules
                case "anticheat.jitter": case "jitter": return SetInt(_cheatJitter, value, 0, Net.AegisRules.MaxJitterPercent, "anticheat.jitter", out message);   // v0.5.5 per-lobby variation (host only: not in Commands.IsAdminOptKey)
                // v0.5.5 AegisBans (host only: not in Commands.IsAdminOptKey)
                case "anticheat.sharedbans": case "sharedbans": return SetBool(_cheatSharedBans, value, "anticheat.sharedbans", out message);
                // v0.5.5: turning this on from chat starts reporting other players to Innersloth, and a report cannot be
                // taken back. The settings tab shows that in its tooltip; the chat reply has to say it too (README
                // sends hosts to the chat command), so the "= on" line carries one warning line after it.
                case "anticheat.autoreport": case "autoreport":
                {
                    bool set = SetBool(_cheatAutoReport, value, "anticheat.autoreport", out message);
                    if (set && _cheatAutoReport != null && _cheatAutoReport.Value)
                        message += "\n" + Lang.T("opt.warn.autoreport",
                            "確実なチートの人を Among Us 公式に自動で通報します。通報は取り消せません。なりすましで無実の人を通報してしまうこともありえます。",
                            "Players caught by a certain detection are reported to Among Us automatically. A report cannot be taken back, and a spoofing cheater could in theory get an innocent player reported.",
                            "被“确定”等级检测到的玩家会自动举报给 Among Us 官方。举报无法撤回，理论上也可能因伪装而误报无辜的玩家。");
                    return set;
                }
                case "anticheat.banladder": case "banladder": return SetBool(_cheatBanLadder, value, "anticheat.banladder", out message);
                // v0.5.5: the one-time upgrade check of the two opt-in defaults (host only: not in Commands.IsAdminOptKey).
                // off = turn off exactly what an older version's default left on, undo = put it back, keep = leave it.
                case "upgrade": case "upgrade.defaults": return OptInUpgrade.TryCommand(value, out message);
                case "general.ignoreversion": case "ignoreversion": return SetBool(_ignoreVersion, value, "general.ignoreversion", out message);
                case "lobby.autorehost": case "autorehost": case "rehost": return SetBool(_autoRehost, value, "lobby.autorehost", out message);
                case "lobby.autopublic": case "autopublic": return SetBool(_autoPublic, value, "lobby.autopublic", out message);
                case "lobby.autopublicdelay": case "autopublicdelay": return SetInt(_autoPublicDelay, value, 0, 60, "lobby.autopublicdelay", out message);
                case "lobby.rehostmax": case "lobby.rehostmaxattempts": case "rehostmax": return SetInt(_rehostMaxAttempts, value, 1, 10, "lobby.rehostmax", out message);
                case "lobby.maxping": case "lobby.maxhostping": case "maxping": case "maxhostping": return SetInt(_maxHostPing, value, 0, 300, "lobby.maxping", out message);
                case "chat.welcometext": case "welcometext": return SetString(_welcomeText, value, "chat.welcometext", out message);
                case "chat.compatwelcome": case "compatwelcome": case "chat.compatwelcometext": return SetString(_compatWelcomeText, value, "chat.compatwelcome", out message);
                case "chat.welcomesettings": case "welcomesettings": return SetBool(_welcomeIncludeSettings, value, "chat.welcomesettings", out message);
                case "credits.author": return SetString(_creditAuthor, value, "credits.author", out message);
                case "credits.url": case "credits.repourl": return SetString(_creditRepoUrl, value, "credits.url", out message);
                case "credits.show": return SetBool(_showCredits, value, "credits.show", out message);
                case "roles.vanilla": case "vanillaroles": case "vanilla.roles": return SetBool(_vanillaRoles, value, "roles.vanilla", out message);
                case "roles.revealall": case "revealall": case "revealtoall": return SetBool(_revealToAll, value, "roles.revealall", out message);
                case "roles.reveal": case "reveal": case "revealdeath": case "roles.revealroleondeath": return SetBool(_revealOnDeath, value, "roles.reveal", out message);
                case "roles.revealleave": case "revealleave": case "roles.revealroleonleave": return SetBool(_revealOnLeave, value, "roles.revealleave", out message);
                case "roles.revealleaveall": case "revealleaveall": case "roles.revealleavetoall": return SetBool(_revealLeaveToAll, value, "roles.revealleaveall", out message);
                case "roles.ghostlist": case "ghostlist": case "roles.hostghostrolelist": case "who": return SetBool(_hostGhostRoleList, value, "roles.ghostlist", out message);
                case "chat.compatwelcomeinterval": case "compatwelcomeinterval": case "chat.welcomeinterval":
                {
                    if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sec) || sec < 0f || sec > 600f)
                    { message = "chat.compatwelcomeinterval: 0-600"; return false; }
                    CompatWelcomeInterval = sec; message = $"chat.compatwelcomeinterval = {CompatWelcomeInterval:0.#}"; return true;
                }
                // v0.4 lobby
                case "lobby.autostart": case "autostart": return SetBool(_autoStart, value, "lobby.autostart", out message);
                case "lobby.autostartplayers": case "autostartplayers": case "autostart.players": return SetInt(_autoStartPlayers, value, 4, 15, "lobby.autostartplayers", out message);
                case "lobby.afkkick": case "afkkick": case "afk": case "lobby.afkkickminutes": return SetInt(_afkKickMinutes, value, 0, 30, "lobby.afkkick", out message);
                case "lobby.autostartcountdown": case "autostartcountdown": case "autostart.countdown": return SetInt(_autoStartCountdown, value, 1, 30, "lobby.autostartcountdown", out message);
                case "lobby.timermode": case "timermode": return SetChoiceValue(_timerMode, TimerModeChoices, value, "lobby.timermode", out message);
                case "lobby.timerwarnat": case "timerwarnat": case "lobby.warnat": return SetInt(_timerWarnAt, value, 30, 300, "lobby.timerwarnat", out message);
                case "lobby.extenddelay": case "lobby.extendnoticedelay": case "extenddelay": return SetInt(_extendNoticeDelay, value, 0, 60, "lobby.extenddelay", out message);
                case "lobby.autoregion": case "autoregion": return SetBool(_autoRegion, value, "lobby.autoregion", out message);
                case "lobby.dleks": case "lobby.enabledleks": case "dleks": return SetBool(_enableDleks, value, "lobby.dleks", out message);
                // v0.4 host tools
                case "gm": case "gamemaster": case "general.gamemaster": return SetBool(_gameMaster, value, "gm", out message);
                case "hotkeys": case "hotkeys.enabled": return SetBool(_hotkeysEnabled, value, "hotkeys", out message);
                case "hotkeys.haison": case "hotkey.haison": return SetKey(_hotkeyHaison, value, "F7", "hotkeys.haison", out message);
                case "hotkeys.endmeeting": case "hotkey.endmeeting": return SetKey(_hotkeyEndMeeting, value, "F8", "hotkeys.endmeeting", out message);
                case "hotkeys.cancelstart": case "hotkey.cancelstart": return SetKey(_hotkeyCancelStart, value, "F9", "hotkeys.cancelstart", out message);
                // v0.4 chat
                case "chat.playercommands": case "playercommands": return SetBool(_playerCommands, value, "chat.playercommands", out message);
                case "chat.allcommands": case "allcommands": return SetBool(_allCommands, value, "chat.allcommands", out message);
                case "chat.rulesmode": case "rulesmode": case "rules.mode": return SetChoiceValue(_rulesMode, RulesModeChoices, value, "chat.rulesmode", out message);
                case "chat.rulestext": case "rulestext": case "rules.text": case "rules":
                {
                    // The text only shows when RulesMode = custom: keep the mode consistent with the text (like /rules).
                    bool ok = SetString(_rulesText, value, "chat.rulestext", out message);
                    if (ok) SetChoice(_rulesMode, RulesModeChoices, string.IsNullOrWhiteSpace(value) ? "none" : "custom");
                    return ok;
                }
                case "chat.welcomeall": case "chat.welcomealllanguages": case "welcomeall": return SetBool(_welcomeAllLanguages, value, "chat.welcomeall", out message);
                // v0.5.5 NG words (/ng on|off does the same as "ng")
                case "ng": case "chat.ngfilter": case "ngfilter": case "chat.ng": return SetBool(_ngFilter, value, "ng", out message);
                case "ng.kickat": case "chat.ngkickat": case "ngkickat": return SetInt(_ngKickAt, value, 0, 5, "ng.kickat", out message);
                case "ng.ban": case "chat.ngban": case "ngban": return SetBool(_ngBan, value, "ng.ban", out message);
                case "ng.announce": case "chat.ngannounce": case "ngannounce": return SetBool(_ngAnnounce, value, "ng.announce", out message);
                // v0.4b translation (the DeepL key is never settable here: it lives in BepInEx/PocketRoles/deepl-key.txt)
                // v0.5.5: turning this on sends every chat line of the room to Google / DeepL. Same reason as
                // anticheat.autoreport: the chat reply must say so, not only the settings-tab tooltip.
                case "translate.enabled": case "translate": case "tr":
                {
                    bool set = SetBool(_trEnabled, value, "translate.enabled", out message);
                    if (set && _trEnabled != null && _trEnabled.Value)
                        message += "\n" + Lang.T("opt.warn.translate",
                            "オンの間、部屋のチャットの文章が Google（または DeepL）に送られます。部屋の全員にもお知らせを出しました。",
                            "While this is on, the chat text of the room is sent to Google (or DeepL). Everyone in the room has been told.",
                            "开启期间，房间的聊天文本会发送到 Google（或 DeepL）。已经通知了房间里的所有人。");
                    return set;
                }
                case "translate.notice": case "translate.offnotice": case "tr.notice": return SetBool(_trOffNotice, value, "translate.notice", out message);
                case "translate.provider": case "tr.provider": return SetChoiceValue(_trProvider, TranslateProviderChoices, value, "translate.provider", out message);
                case "translate.target": case "translate.targetlang": case "translate.lang": case "tr.target":
                    if (!Lang.TryNormalize(value, out var trLang)) { message = "translate.target: ja | zh | en"; return false; }
                    TranslateTargetLang = trLang; message = "translate.target = " + trLang; return true;
                case "translate.showhost": case "translate.showonhost": case "tr.showhost": return SetBool(_trShowOnHost, value, "translate.showhost", out message);
                case "translate.broadcast": case "translate.broadcasttoall": case "translate.all": case "tr.broadcast": return SetBool(_trBroadcastToAll, value, "translate.broadcast", out message);
                case "translate.compat": case "translate.foreignincompat": case "tr.compat": return SetBool(_trForeignInCompat, value, "translate.compat", out message);
                case "translate.players": case "translate.forplayers": case "tr.players": return SetBool(_trForPlayers, value, "translate.players", out message);
                case "translate.autodetect": case "translate.autodetectlang": case "tr.autodetect": return SetBool(_trAutoDetect, value, "translate.autodetect", out message);
                case "translate.minchars": case "tr.minchars": return SetInt(_trMinChars, value, 1, 50, "translate.minchars", out message);
                case "translate.maxperminute": case "translate.max": case "tr.maxperminute": return SetInt(_trMaxPerMinute, value, 1, 120, "translate.maxperminute", out message);
                // v0.4b permissions
                case "perm.adminsettings": case "perm.admin": case "adminsettings": case "permissions.adminscanchangesettings": return SetBool(_permAdminSettings, value, "perm.adminsettings", out message);
                case "perm.modkick": case "perm.mod": case "modkick": case "permissions.moderatorscankick": return SetBool(_permModKick, value, "perm.modkick", out message);
                case "perm.adminlobby": case "perm.lobby": case "adminlobby": case "permissions.adminlobbycontrol": return SetBool(_permAdminLobby, value, "perm.adminlobby", out message);
                case "perm.vipmarker": case "perm.vip": case "vipmarker": case "permissions.vipmarker": return SetBool(_permVipMarker, value, "perm.vipmarker", out message);
                // v0.4b vanilla extended ranges
                case "vanilla.ranges": case "vanilla.extendedranges": case "vanilla.extended": case "ranges": return SetBool(_vanExtendedRanges, value, "vanilla.ranges", out message);
                case "vanilla.clampunreg": case "clampunreg": case "vanilla.clampinunregistered": return SetBool(_vanClampUnreg, value, "vanilla.clampunreg", out message);
                case "vanilla.killmin": case "vanilla.killcooldownmin": return SetFloat(_vanKillMin, value, 0f, 60f, "vanilla.killmin", out message);
                case "vanilla.killmax": case "vanilla.killcooldownmax": return SetFloat(_vanKillMax, value, 10f, 600f, "vanilla.killmax", out message);
                case "vanilla.killstep": case "vanilla.killcooldownstep": return SetFloat(_vanKillStep, value, 0.5f, 10f, "vanilla.killstep", out message);
                case "vanilla.votemin": case "vanilla.votingtimemin": return SetInt(_vanVoteMin, value, 0, 300, "vanilla.votemin", out message);
                case "vanilla.votemax": case "vanilla.votingtimemax": return SetInt(_vanVoteMax, value, 15, 3600, "vanilla.votemax", out message);
                case "vanilla.discussmax": case "vanilla.discussionmax": case "vanilla.discussiontimemax": return SetInt(_vanDiscussMax, value, 0, 3600, "vanilla.discussmax", out message);
                case "vanilla.emergencymax": case "vanilla.emergencycooldownmax": return SetInt(_vanEmergencyMax, value, 0, 600, "vanilla.emergencymax", out message);
                case "compat.tasks.common": case "compat.common": return SetInt(_compatCommonTasks, value, 0, 60, "compat.tasks.common", out message);
                case "compat.tasks.short": case "compat.short": return SetInt(_compatShortTasks, value, 0, 60, "compat.tasks.short", out message);
                case "compat.tasks.long": case "compat.long": return SetInt(_compatLongTasks, value, 0, 60, "compat.tasks.long", out message);
                case "vanilla.gauses": case "gauses": case "guardianuses": case "vanilla.guardianangeluses": return SetInt(_vanGaUses, value, 0, 9, "vanilla.gauses", out message);
                case "host.shieldkey": case "shieldkey":
                {
                    bool ok = SetString(_hostShieldKey, value, "host.shieldkey", out message);
                    _hostShieldUnlocked = null;
                    if (ok) message = HostShieldUnlocked
                        ? Lang.T("opt.shield.unlocked", "ホストのシールドが使えるようになりました（/opt host.shield <回数>）。", "Host shield unlocked (/opt host.shield <n>).")
                        : Lang.T("opt.shield.locked", "合言葉が違います。", "Wrong phrase.");
                    return ok;
                }
                case "host.shield": case "shield": case "hostshield":
                    if (!HostShieldUnlocked) break;
                    return SetInt(_hostShieldKills, value, 0, 9, "host.shield", out message);
                case "vanilla.taskmax": case "vanilla.taskcountmax": case "vanilla.tasks": return SetInt(_vanTaskMax, value, 1, 60, "vanilla.taskmax", out message);
                // v0.4e guide room
                case "guide.overlay": case "guide.showcodeoverlay": case "guide.codeoverlay": case "codeoverlay": return SetBool(_guideShowCodeOverlay, value, "guide.overlay", out message);
                case "guide.code": case "guide.roleroomcode": case "roleroomcode":
                {
                    if (value.Length > 0 && NormalizeRoomCode(value).Length == 0)
                    {
                        message = Lang.T("opt.badcode", "guide.code: 部屋コード（英字 4 または 6 文字）を指定してください", "guide.code: expected a room code (4 or 6 letters)", "guide.code: 请输入房间代码（4 或 6 个字母）");
                        return false;
                    }
                    RoleRoomCode = value;
                    message = "guide.code = " + (RoleRoomCode.Length == 0 ? "(" + Lang.T("opt.empty", "空", "empty") + ")" : RoleRoomCode);
                    return true;
                }
                case "guide.autoreg": case "guide.autorecreate": case "guide.autorecreateregistered": return SetBool(_guideAutoRecreateRegistered, value, "guide.autoreg", out message);
                // v0.3 cosmetics (host screen only)
                case "cos.enabled": case "cosmetics": case "cos": return SetBool(_cosEnabled, value, "cos.enabled", out message);
                case "cos.music": case "cos.lobbymusic": case "lobbymusic": return SetChoiceValue(_cosMusic, LobbyMusicChoices, value, "cos.music", out message);
                case "cos.musicfile": case "cos.lobbymusicfile": case "lobbymusicfile": return SetString(_cosMusicFile, value, "cos.musicfile", out message);
                case "cos.musicvolume": case "cos.lobbymusicvolume": case "lobbymusicvolume": return SetFloat(_cosMusicVolume, value, 0f, 1f, "cos.musicvolume", out message);
                case "cos.lobbypaint": case "lobbypaint": return SetBool(_cosLobbyPaint, value, "cos.lobbypaint", out message);
                case "cos.dropship": case "dropship": return SetBool(_cosDropship, value, "cos.dropship", out message);
                case "cos.menubg": case "cos.menubackground": case "menubackground": return SetBool(_cosMenuBackground, value, "cos.menubg", out message);
                case "cos.cursor": case "cursor": return SetBool(_cosCursor, value, "cos.cursor", out message);
                // v0.4.1 roles
                case "lovers.impostor": case "lovers.allowimpostor": return SetBool(_loversAllowImpostor, value, "lovers.impostor", out message);
                case "lovers.lastthree": case "lovers.last3": case "lovers.winaslastthree": return SetBool(_loversLastThree, value, "lovers.lastthree", out message);
                case "arsonist.cooldown": case "arsonist.cd": case "arsonist.dousecooldown": return SetFloat(_arsonistDouseCooldown, value, 2.5f, 180f, "arsonist.cooldown", out message);
                case "arsonist.vent": case "arsonist.canvent": return SetBool(_arsonistCanVent, value, "arsonist.vent", out message);
                case "witch.cooldown": case "witch.cd": case "witch.spellcooldown": return SetFloat(_witchSpellCooldown, value, 0f, 180f, "witch.cooldown", out message);
                case "witch.mark": case "witch.spelledseemark": return SetBool(_witchSpelledSeeMark, value, "witch.mark", out message);
                case "assassin.guesses": case "assassin.guessespermeeting": return SetInt(_assassinGuessesPerMeeting, value, 1, 5, "assassin.guesses", out message);
                case "assassin.firstmeeting": case "assassin.first": case "assassin.canguessfirstmeeting": return SetBool(_assassinFirstMeeting, value, "assassin.firstmeeting", out message);
                // v0.5.0 roles
                case "madmayor.votes": case "madmayor.vote": return SetInt(_madMayorVotes, value, 1, 5, "madmayor.votes", out message);
                case "madmayor.known": case "madmayor.knowntoimpostors": return SetBool(_madMayorKnownToImpostors, value, "madmayor.known", out message);
                case "madstuntman.lives": case "madstuntman.guard": case "madstuntman.guards": case "stunt.lives": return SetInt(_madStuntmanLives, value, 1, 10, "madstuntman.lives", out message);
                case "madstuntman.notify": case "madstuntman.notifystuntman": case "stunt.notify": return SetBool(_madStuntmanNotify, value, "madstuntman.notify", out message);
                case "madhawk.vision": case "madhawk.visionmultiplier": return SetFloat(_madHawkVision, value, 1f, 5f, "madhawk.vision", out message);
                case "madhawk.speed": case "madhawk.speedmultiplier": return SetFloat(_madHawkSpeed, value, 0.5f, 1.5f, "madhawk.speed", out message);
                case "worshipper.uses": case "worshipper.times": case "worshipper.worships": return SetInt(_worshipperUses, value, 1, 5, "worshipper.uses", out message);
                case "worshipper.cooldown": case "worshipper.cd": return SetFloat(_worshipperCooldown, value, 2.5f, 180f, "worshipper.cooldown", out message);
                case "jackalfriends.known": case "jackalfriends.knowntojackal": case "jf.known": return SetBool(_jackalFriendsKnownToJackal, value, "jackalfriends.known", out message);
                case "jackalfriends.sheriff": case "jackalfriends.sheriffcankill": case "jf.sheriff": return SetBool(_jackalFriendsSheriffCanKill, value, "jackalfriends.sheriff", out message);
                case "evilhawk.vision": case "evilhawk.visionmultiplier": case "eh.vision": return SetFloat(_evilHawkVision, value, 1f, 5f, "evilhawk.vision", out message);
                case "evilnekomata.voters": case "evilnekomata.votersonly": case "nekomata.voters": case "neko.voters": return SetBool(_nekomataVotersOnly, value, "evilnekomata.voters", out message);
                case "evilnekomata.excludeimp": case "evilnekomata.excludeimpostors": case "nekomata.excludeimp": case "neko.excludeimp": return SetBool(_nekomataExcludeImpostors, value, "evilnekomata.excludeimp", out message);
                case "evilnekomata.announce": case "nekomata.announce": case "neko.announce": return SetBool(_nekomataAnnounce, value, "evilnekomata.announce", out message);
                case "serialkiller.cooldown": case "serialkiller.cd": case "serialkiller.killcooldown": case "sk.cooldown": case "sk.cd": return SetFloat(_serialKillerKillCooldown, value, 1f, 60f, "serialkiller.cooldown", out message);
                case "serialkiller.time": case "serialkiller.suicide": case "serialkiller.suicidetime": case "serialkiller.limit": case "sk.time": return SetFloat(_serialKillerSuicideTime, value, 10f, 300f, "serialkiller.time", out message);
                case "serialkiller.meetingreset": case "serialkiller.reset": case "serialkiller.resetatmeeting": case "sk.reset": return SetBool(_serialKillerResetAtMeeting, value, "serialkiller.meetingreset", out message);
                case "samurai.cooldown": case "samurai.cd": case "samurai.killcooldown": return SetFloat(_samuraiKillCooldown, value, 0f, 180f, "samurai.cooldown", out message);
                case "samurai.range": case "samurai.radius": return SetFloat(_samuraiRange, value, 0.5f, 5f, "samurai.range", out message);
                case "samurai.stagger": case "samurai.interval": return SetFloat(_samuraiStagger, value, 0.1f, 1f, "samurai.stagger", out message);
                case "samurai.teammates": case "samurai.allies": case "samurai.hitteammates": return SetBool(_samuraiHitTeammates, value, "samurai.teammates", out message);
                case "sheriff.cooldown": case "sheriff.cd": case "sheriff.killcooldown": return SetFloat(_sheriffKillCooldown, value, 2.5f, 180f, "sheriff.cooldown", out message);
                case "sheriff.killmadmate": case "sheriff.madmate": return SetBool(_sheriffCanKillMadmate, value, "sheriff.killmadmate", out message);
                case "jackal.cooldown": case "jackal.cd": case "jackal.killcooldown": return SetFloat(_jackalKillCooldown, value, 2.5f, 180f, "jackal.cooldown", out message);
                case "jackal.vent": case "jackal.canvent": return SetBool(_jackalCanVent, value, "jackal.vent", out message);
                case "vampire.delay": case "vampire.killdelay": return SetFloat(_vampireKillDelay, value, 1f, 60f, "vampire.delay", out message);
                case "mayor.votes": case "mayor.vote": return SetInt(_mayorVotes, value, 1, 5, "mayor.votes", out message);
                case "snitch.tasks": case "snitch.tasksleft": return SetInt(_snitchTasksLeftToWarn, value, 0, 10, "snitch.tasks", out message);
                case "lighter.vision": return SetFloat(_lighterVision, value, 1f, 5f, "lighter.vision", out message);
                case "speedbooster.speed": case "speed.speed": case "sb.speed": return SetFloat(_speedBoosterSpeed, value, 1f, 3f, "speedbooster.speed", out message);
                case "madmate.known": case "madmate.knowntoimpostors": return SetBool(_madmateKnownToImpostors, value, "madmate.known", out message);
            }

            // <role>.count / <role>.chance
            int dot = k.LastIndexOf('.');
            if (dot > 0)
            {
                string rolePart = k.Substring(0, dot);
                string field = k.Substring(dot + 1);
                if (Roles.TryParse(rolePart, out var role))
                {
                    if (field == "count" || field == "num" || field == "n")
                    {
                        if (!int.TryParse(value, out var n)) { message = Lang.TF("opt.badnumber", "{0}: 数値を指定してください", "{0}: expected a number", "count"); return false; }
                        SetCount(role, n);
                        message = $"{Roles.Info(role).Name}.count = {Count(role)}";
                        return true;
                    }
                    if (field == "chance" || field == "rate" || field == "c")
                    {
                        if (!int.TryParse(value.TrimEnd('%'), out var c)) { message = Lang.TF("opt.badnumber", "{0}: 数値を指定してください", "{0}: expected a number", "chance"); return false; }
                        SetChance(role, c);
                        message = $"{Roles.Info(role).Name}.chance = {Chance(role)}%";
                        return true;
                    }
                }
            }

            message = Lang.TF("opt.unknown", "不明な設定キーです: {0}", "Unknown option key: {0}", key);
            return false;
        }

        /// <summary>Human-readable current settings: one line per enabled role + relevant sub-options.</summary>
        public static IEnumerable<string> DescribeLines()
        {
            var lines = new List<string>();
            bool any = false;
            foreach (var r in Roles.All)
            {
                int n = Count(r.Id);
                if (n <= 0) continue;
                any = true;
                string line = $"{r.ColoredName} x{n}";
                if (Chance(r.Id) < 100) line += $" ({Chance(r.Id)}%)";
                switch (r.Id)
                {
                    case CustomRole.Sheriff:
                        line += Lang.TF("opt.desc.sheriff", " キルCD{0:0.#}秒", " KCD {0:0.#}s", SheriffKillCooldown);
                        if (SheriffCanKillMadmate) line += Lang.T("opt.desc.sheriff.madmate", " マッド可", ", can kill Madmate");
                        break;
                    case CustomRole.Jackal:
                        line += Lang.TF("opt.desc.jackal", " キルCD{0:0.#}秒", " KCD {0:0.#}s", JackalKillCooldown);
                        if (JackalCanVent) line += Lang.T("opt.desc.jackal.vent", " ベント可", ", can vent");
                        break;
                    case CustomRole.Vampire: line += Lang.TF("opt.desc.vampire", " 噛みつき{0:0.#}秒", " bite {0:0.#}s", VampireKillDelay); break;
                    case CustomRole.Mayor: line += Lang.TF("opt.desc.mayor", " {0}票", " {0} votes", MayorVotes); break;
                    case CustomRole.Snitch: line += Lang.TF("opt.desc.snitch", " 残り{0}で警告", " warn at {0} left", SnitchTasksLeftToWarn); break;
                    case CustomRole.Lighter: line += $" x{LighterVision:0.#}"; break;
                    case CustomRole.SpeedBooster: line += $" x{SpeedBoosterSpeed:0.#}"; break;
                    case CustomRole.Madmate: if (MadmateKnownToImpostors) line += Lang.T("opt.desc.madmate", " インポスターに公開", " known to impostors"); break;
                    // v0.4.1
                    case CustomRole.Lovers:
                        if (LoversAllowImpostor) line += Lang.T("opt.desc.lovers.impostor", " インポスター可", ", impostor allowed");
                        if (LoversWinAsLastThree) line += Lang.T("opt.desc.lovers.lastthree", " 残り3人で勝利", ", win as last 3");
                        break;
                    case CustomRole.Arsonist:
                        line += Lang.TF("opt.desc.arsonist", " 油CD{0:0.#}秒", " douse CD {0:0.#}s", ArsonistDouseCooldown);
                        if (ArsonistCanVent) line += Lang.T("opt.desc.arsonist.vent", " ベント可", ", can vent");
                        break;
                    case CustomRole.Witch:
                        if (WitchSpellCooldown > 0f) line += Lang.TF("opt.desc.witch", " 呪いCD{0:0.#}秒", " spell CD {0:0.#}s", WitchSpellCooldown);
                        if (WitchSpelledSeeMark) line += Lang.T("opt.desc.witch.mark", " 本人に印", ", target sees mark");
                        break;
                    case CustomRole.Assassin:
                        line += Lang.TF("opt.desc.assassin", " 推理{0}回/会議", " {0} guess/meeting", AssassinGuessesPerMeeting);
                        if (!AssassinCanGuessFirstMeeting) line += Lang.T("opt.desc.assassin.first", " 初回会議不可", ", not in 1st meeting");
                        break;
                    // ---- v0.5.0
                    case CustomRole.MadMayor:
                        line += Lang.TF("opt.desc.madmayor", " {0}票", " {0} votes", MadMayorVotes);
                        if (MadMayorKnownToImpostors) line += Lang.T("opt.desc.madmayor.known", " インポスターに公開", ", known to impostors");
                        break;
                    case CustomRole.MadStuntman:
                        line += Lang.TF("opt.desc.madstuntman", " 耐久{0}回", " survives {0}", MadStuntmanLives);
                        if (MadStuntmanNotify) line += Lang.T("opt.desc.madstuntman.notify", " 本人に通知", ", notifies");
                        break;
                    case CustomRole.MadHawk:
                        line += $" x{MadHawkVision:0.#}";   // same inline form as Lighter / SpeedBooster
                        if (Math.Abs(MadHawkSpeed - 1f) > 0.001f) line += Lang.TF("opt.desc.madhawk.speed", " 速度x{0:0.##}", ", speed x{0:0.##}", MadHawkSpeed);
                        break;
                    case CustomRole.Worshipper:
                        line += Lang.TF("opt.desc.worshipper", " 崇拝{0}回 CD{1:0.#}秒", " {0} worship(s), CD {1:0.#}s", WorshipperUses, WorshipperCooldown);
                        break;
                    case CustomRole.JackalFriends:
                        if (JackalFriendsKnownToJackal) line += Lang.T("opt.desc.jackalfriends", " ジャッカルに公開", " known to Jackal");
                        if (!JackalFriendsSheriffCanKill) line += Lang.T("opt.desc.jackalfriends.sheriff", " シェリフ不可", ", Sheriff cannot shoot");
                        break;
                    case CustomRole.EvilHawk: line += $" x{EvilHawkVision:0.#}"; break;
                    case CustomRole.EvilNekomata:   // only deviations from the defaults are shown
                        if (!EvilNekomataVotersOnly) line += Lang.T("opt.desc.evilnekomata.anyone", " 生存者全員から", ", any player");
                        if (!EvilNekomataExcludeImpostors) line += Lang.T("opt.desc.evilnekomata.impok", " インポスターも対象", ", impostors too");
                        if (!EvilNekomataAnnounce) line += Lang.T("opt.desc.evilnekomata.silent", " 非公開", ", silent");
                        break;
                    case CustomRole.SerialKiller:
                        line += Lang.TF("opt.desc.serialkiller", " キルCD{0:0.#}秒 制限{1:0.#}秒", " KCD {0:0.#}s, limit {1:0.#}s", SerialKillerKillCooldown, SerialKillerSuicideTime);
                        if (!SerialKillerResetAtMeeting) line += Lang.T("opt.desc.serialkiller.noreset", " 会議で継続", ", no reset at meetings");
                        break;
                    case CustomRole.Samurai:
                        if (SamuraiKillCooldown > 0f) line += Lang.TF("opt.desc.samurai", " 斬撃CD{0:0.#}秒", " slash CD {0:0.#}s", SamuraiKillCooldown);
                        line += Lang.TF("opt.desc.samurai.range", " 範囲{0:0.#}", ", range {0:0.#}", SamuraiRange);
                        if (Math.Abs(SamuraiStagger - 0.3f) > 0.001f) line += Lang.TF("opt.desc.samurai.stagger", " 間隔{0:0.#}秒", ", stagger {0:0.#}s", SamuraiStagger);
                        if (SamuraiHitTeammates) line += Lang.T("opt.desc.samurai.teammates", " 味方も斬る", ", hits allies");
                        break;
                }
                lines.Add(line);
            }
            if (!any) lines.Add(Lang.T("opt.none", "有効な役職はありません（/set <役職> <人数> で設定）", "No custom roles enabled (/set <role> <count>)"));
            lines.Add(Lang.TF("opt.general", "言語={0} MOD登録={1} 挨拶={2}", "lang={0} registration={1} welcome={2}",
                LanguageLabel, HostAuthorityMode ? "on" : "off", WelcomeMessage ? "on" : "off"));
            if (AutoRehost || AutoPublic)
            {
                string pub = AutoPublic ? Lang.TF("opt.lobby.public.on", "on({0}秒)", "on ({0}s)", AutoPublicDelay) : "off";
                lines.Add(Lang.TF("opt.lobby", "自動再ホスト={0} 自動公開={1}", "auto re-host={0} auto public={1}", AutoRehost ? "on" : "off", pub));
            }
            if (AutoStart || GameMaster)
            {
                string auto = AutoStart ? Lang.TF("opt.lobby.autostart.on", "on({0}人)", "on ({0} players)", AutoStartPlayers) : "off";
                lines.Add(Lang.TF("opt.host", "自動開始={0} ゲームマスター={1}", "auto start={0} game master={1}", auto, GameMaster ? "on" : "off"));
            }
            if (IgnoreVersionMismatch && Game.VersionMismatch)
                lines.Add(Lang.T("opt.versionignored", "注意: ゲームのバージョン不一致を無視して動作中", "Note: running despite a game version mismatch"));
            bool compat = false;
            try { compat = Net.Registration.CompatMode; } catch (Exception) { }
            if (compat)
                lines.Add(Lang.TF("opt.compat", "互換モード（登録オフ・便利ホスト）: 追加役職={0}（本来の役職は設定どおり）", "Compat mode (unregistered, 便利ホスト): mod roles={0} (usual roles as set)", "off"));
            return lines;
        }

        public static void Reload()
        {
            _cfg?.Reload();
            _hostShieldUnlocked = null;
        }

        public static void Save()
        {
            _cfg?.Save();
        }

        // ------------------------------------------------------------------ backup / restore (v0.4.4)

        /// <summary>
        /// Copies the config file to "&lt;config&gt;&lt;suffix&gt;" (".backup" = /backup, ".startup" = the state at game launch,
        /// written by PocketRolesPlugin.Load). Returns the copy's path, null on failure.
        /// </summary>
        public static string Backup(string suffix = ".backup")
        {
            try
            {
                string p = _cfg?.ConfigFilePath;
                if (string.IsNullOrEmpty(p)) return null;
                try { _cfg.Save(); } catch (Exception) { }
                if (!File.Exists(p)) return null;
                string b = p + suffix;
                File.Copy(p, b, true);
                return b;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Options.Backup({suffix}): {e}");
                return null;
            }
        }

        /// <summary>Restores the config file from "&lt;config&gt;&lt;suffix&gt;" and re-reads it. False when there is no such copy.</summary>
        public static bool Restore(string suffix = ".backup")
        {
            try
            {
                string p = _cfg?.ConfigFilePath;
                if (string.IsNullOrEmpty(p)) return false;
                string b = p + suffix;
                if (!File.Exists(b)) return false;
                File.Copy(b, p, true);
                _cfg.Reload();
                _hostShieldUnlocked = null;
                PocketRolesPlugin.Logger.LogInfo($"Options: config restored from {Path.GetFileName(b)}");
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Options.Restore({suffix}): {e}");
                return false;
            }
        }
    }
}
