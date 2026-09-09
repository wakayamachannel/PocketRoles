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
        /// <summary>Display group: role name for role rows, or General / Lobby / Chat (localized; table key "opt.section.&lt;en&gt;").</summary>
        public string Section => Lang.T("opt.section." + Slug(SectionEn), SectionJa, SectionEn);
        public string NameJa, NameEn;
        /// <summary>Localized row name (table key "opt.name.&lt;key&gt;").</summary>
        public string Name => Lang.T("opt.name." + Key, NameJa, NameEn);

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
        private static ConfigEntry<bool> _antiCheatKick;
        private static ConfigEntry<bool> _wireLog;

        private static ConfigEntry<bool> _autoRehost;
        private static ConfigEntry<bool> _autoPublic;
        private static ConfigEntry<int> _autoPublicDelay;
        private static ConfigEntry<int> _rehostMaxAttempts;
        private static ConfigEntry<int> _maxHostPing;
        // [Compat] unregistered-compatible mode (RegisterAsModdedLobby=false)
        private static ConfigEntry<bool> _compatAllowRisky;

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

        // v0.4b [Translate] (the DeepL key lives in BepInEx/PocketRoles/deepl-key.txt, never in the cfg)
        private static ConfigEntry<bool> _trEnabled;
        private static ConfigEntry<string> _trProvider;
        private static ConfigEntry<string> _trTargetLang;
        private static ConfigEntry<bool> _trShowOnHost;
        private static ConfigEntry<bool> _trBroadcastToAll;
        private static ConfigEntry<bool> _trForPlayers;
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

        private static readonly List<OptionDescriptor> _descriptors = new List<OptionDescriptor>();

        /// <summary>Every editable option in display order (role rows first, then General / Lobby / Chat). Built by Init().</summary>
        public static IReadOnlyList<OptionDescriptor> Descriptors => _descriptors;

        public static bool Initialized => _cfg != null;

        /// <summary>
        /// One-time rename migration (HostRoles → PocketRoles): when BepInEx/config/jp.hostroles.mod.cfg exists and
        /// jp.pocketroles.mod.cfg does not, the old file is copied to the new name and re-read so every value carries over.
        /// Runs before any Bind(); BepInEx creates the new file on the first save otherwise.
        /// </summary>
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

        /// <summary>The config was copied from jp.hostroles.mod.cfg in this run (rename migration).</summary>
        private static bool _migrated;

        public static void Init(ConfigFile cfg)
        {
            _cfg = cfg;
            _migrated = MigrateLegacyConfig(cfg);
            cfg.SaveOnConfigSet = true;

            _enabled = cfg.Bind("General", "Enabled", true, "Enable PocketRoles (host only). Can be toggled in the lobby with /mod on|off");
            _language = cfg.Bind("General", "Language", "ja", new ConfigDescription("Default language for player-facing text: ja (Japanese), zh (Simplified Chinese) or en (English). Players can pick their own with /lang; texts are editable in BepInEx/PocketRoles/lang/*.json", new AcceptableValueList<string>("ja", "zh", "en")));
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
            _revealOnDeath = cfg.Bind("Roles", "RevealRoleOnDeath", false, "Announce a player's role to everyone when they are killed or ejected ('X was Sheriff'; the vanilla role's name when there is no PocketRoles role, e.g. in an unregistered lobby)");
            _compatWelcomeText = cfg.Bind("Chat", "CompatWelcomeText", "", "Unregistered (compat) lobby only: your own one-line public welcome for every joiner (empty = built-in line 'ようこそ! この部屋は普通のAmong Us(役職なし)です…'). One chat message, at most 86 characters; characters a vanilla player cannot type ([ ] < > full-width ！（） etc.) are converted or dropped automatically");
            _wireLog = cfg.Bind("Diagnostics", "WireLog", false, "Investigation aid: log every packet this client sends (InnerNetClient.SendOrDisconnect) and receives (HandleMessage), decoded one level (GameData / GameDataTo -> Data / RPC / Spawn ...), plus every disconnect, to LogOutput.log. Off (default) = no effect");
            _antiCheatKick = cfg.Bind("AntiCheat", "KickOnForgedRpc", false, "Reserved, currently no effect: forged host-only RPCs (SetRole/SetName/MurderPlayer/...) are always dropped and logged, but the sender of a relayed RPC cannot be identified, so nobody is kicked");

            _autoRehost = cfg.Bind("Lobby", "AutoRehost", false, "Automatically create a new lobby after an unexpected disconnect (server error, timeout) while hosting");
            _autoPublic = cfg.Bind("Lobby", "AutoPublic", false, "Automatically make the lobby public a few seconds after it is created / re-hosted");
            _autoPublicDelay = cfg.Bind("Lobby", "AutoPublicDelay", 3, new ConfigDescription("Seconds to wait before making the lobby public", new AcceptableValueRange<int>(0, 60)));
            _rehostMaxAttempts = cfg.Bind("Lobby", "RehostMaxAttempts", 3, new ConfigDescription("Give up auto re-hosting after this many consecutive attempts", new AcceptableValueRange<int>(1, 10)));
            _maxHostPing = cfg.Bind("Lobby", "MaxHostPing", 0, new ConfigDescription("Offer to re-create the lobby (same settings) while it is still empty when the host's ping to the game server stays above this many ms for 5 s right after the lobby is created (official regions mix near and far servers). The host is ASKED on screen first (Yes/No, once per lobby, or /rehost yes|no) because short-lived lobbies count as deliberate disconnects (ban points). 0 = off; at most 3 re-creations in a row, then the lobby is kept (/opt maxping <ms>)", new AcceptableValueRange<int>(0, 300)));
            _compatAllowRisky = cfg.Bind("Compat", "AllowRiskyRoles", false,
                "Unregistered-compatible mode (RegisterAsModdedLobby=false, the lobby shows in the vanilla public list): also assign roles whose kills come from a non-Impostor (Sheriff, Jackal). " +
                "Without mod-lobby registration (host authority) the official server may reject those kills. Off = Sheriff and Jackal are skipped in compat mode (/opt compat.risky on|off)");

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
            _autoRegion = cfg.Bind("Lobby", "AutoRegion", false, "Before hosting, ping the official regions and select the one with the lowest latency (/region shows the table)");
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

            // ---- v0.4b chat translation (chat text is sent to Google / DeepL; the DeepL key is read from BepInEx/PocketRoles/deepl-key.txt and never written here)
            _trEnabled = cfg.Bind("Translate", "Enabled", true, "Translate foreign-language chat (combined mode: broadcast in the host's language + private translation per player). ON by default: chat text of every player is sent to the translation provider (Google, or DeepL when BepInEx/PocketRoles/deepl-key.txt holds a key). Turn off with /opt translate off");
            _trProvider = cfg.Bind("Translate", "Provider", "auto", new ConfigDescription("Translation provider: auto (DeepL when BepInEx/PocketRoles/" + DeepLKeyFileName + " contains an API key, else Google), google (public endpoint, no key), deepl (needs the key file). The key itself is never stored in this file", new AcceptableValueList<string>(TranslateProviderChoices)));
            _trTargetLang = cfg.Bind("Translate", "TargetLang", "", "Language the host reads translations in: ja, zh or en. Empty = same as [General] Language");
            _trShowOnHost = cfg.Bind("Translate", "ShowOnHost", true, "Show translations of foreign-language chat on the host's screen (local, nothing is sent)");
            _trBroadcastToAll = cfg.Bind("Translate", "BroadcastToAll", true, "Send the translation (into the host's language) to every player as a chat message (paced); players who chose another language still get their private translation when TranslateForPlayers is on (併用, the user's chosen default 2026-09-08)");
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
            _sheriffCanKillMadmate = cfg.Bind("Sheriff", "CanKillMadmate", true, "Sheriff can shoot Madmates without dying");
            _jackalKillCooldown = cfg.Bind("Jackal", "KillCooldown", 30f, new ConfigDescription("Jackal kill cooldown (seconds)", new AcceptableValueRange<float>(2.5f, 180f)));
            _jackalCanVent = cfg.Bind("Jackal", "CanVent", true, "Jackal can use vents");
            _vampireKillDelay = cfg.Bind("Vampire", "KillDelay", 10f, new ConfigDescription("Seconds between a bite and the victim's death", new AcceptableValueRange<float>(1f, 60f)));
            _mayorVotes = cfg.Bind("Mayor", "Votes", 2, new ConfigDescription("How many votes the Mayor's vote counts as", new AcceptableValueRange<int>(1, 5)));
            _snitchTasksLeftToWarn = cfg.Bind("Snitch", "TasksLeftToWarn", 1, new ConfigDescription("Killers see the Snitch marked when this many tasks (or fewer) are left", new AcceptableValueRange<int>(0, 10)));
            _lighterVision = cfg.Bind("Lighter", "VisionMultiplier", 2f, new ConfigDescription("Lighter vision multiplier", new AcceptableValueRange<float>(1f, 5f)));
            _speedBoosterSpeed = cfg.Bind("SpeedBooster", "SpeedMultiplier", 1.5f, new ConfigDescription("Speed Booster speed multiplier", new AcceptableValueRange<float>(1f, 3f)));
            _madmateKnownToImpostors = cfg.Bind("Madmate", "KnownToImpostors", false, "Impostors see who the Madmate is");

            // ---- v0.4.1 roles
            _loversAllowImpostor = cfg.Bind("Lovers", "AllowImpostor", true, "The second lover may be a vanilla Impostor (keeps its kill button and counts as an Impostor for the win rules; wins only as a lover)");
            _loversLastThree = cfg.Bind("Lovers", "WinAsLastThree", true, "The Lovers win as soon as both are alive and at most 3 players are alive");
            _arsonistDouseCooldown = cfg.Bind("Arsonist", "DouseCooldown", 10f, new ConfigDescription("Seconds between two douses (the Arsonist's kill button)", new AcceptableValueRange<float>(2.5f, 180f)));
            _arsonistCanVent = cfg.Bind("Arsonist", "CanVent", false, "Arsonist can use vents");
            _witchSpellCooldown = cfg.Bind("Witch", "SpellCooldown", 0f, new ConfigDescription("Seconds between two spells (0 = the lobby's kill cooldown)", new AcceptableValueRange<float>(0f, 180f)));
            _witchSpelledSeeMark = cfg.Bind("Witch", "SpelledSeeMark", false, "Spelled players see a mark on their own name (the Witch always sees it)");
            _assassinGuessesPerMeeting = cfg.Bind("Assassin", "GuessesPerMeeting", 1, new ConfigDescription("Guesses (/cmd guess) per meeting", new AcceptableValueRange<int>(1, 5)));
            _assassinFirstMeeting = cfg.Bind("Assassin", "CanGuessFirstMeeting", true, "The Assassin may guess in the first meeting of the game");

            BuildDescriptors();
        }

        public static bool ModEnabled { get => _enabled == null || _enabled.Value; set { if (_enabled != null) _enabled.Value = value; } }
        /// <summary>Lobby default language: "ja" | "zh" | "en" (see Lang.Current for the language in effect).</summary>
        public static string Language { get => _language == null ? "ja" : Lang.Normalize(_language.Value); set { if (_language != null) _language.Value = Lang.Normalize(value); } }
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
        public static bool AntiCheatKick { get => _antiCheatKick != null && _antiCheatKick.Value; set { if (_antiCheatKick != null) _antiCheatKick.Value = value; } }
        /// <summary>[Diagnostics] WireLog: packet-level send/receive trace (Net.WireLog), off by default.</summary>
        public static bool WireLog { get => _wireLog != null && _wireLog.Value; set { if (_wireLog != null) _wireLog.Value = value; } }

        public static bool AutoRehost { get => _autoRehost != null && _autoRehost.Value; set { if (_autoRehost != null) _autoRehost.Value = value; } }
        public static bool AutoPublic { get => _autoPublic != null && _autoPublic.Value; set { if (_autoPublic != null) _autoPublic.Value = value; } }
        /// <summary>Seconds (0..60).</summary>
        public static int AutoPublicDelay { get => _autoPublicDelay?.Value ?? 3; set { if (_autoPublicDelay != null) _autoPublicDelay.Value = Math.Max(0, Math.Min(60, value)); } }
        public static int RehostMaxAttempts { get => _rehostMaxAttempts?.Value ?? 3; set { if (_rehostMaxAttempts != null) _rehostMaxAttempts.Value = Math.Max(1, Math.Min(10, value)); } }
        /// <summary>Ping (ms, 0..300) above which a freshly created, still empty lobby is re-created automatically; 0 = off.</summary>
        public static int MaxHostPing { get => _maxHostPing?.Value ?? 0; set { if (_maxHostPing != null) _maxHostPing.Value = Math.Max(0, Math.Min(300, value)); } }
        /// <summary>[Compat] AllowRiskyRoles: assign Sheriff / Jackal even in the unregistered compat mode (default off).</summary>
        public static bool AllowRiskyRoles { get => _compatAllowRisky != null && _compatAllowRisky.Value; set { if (_compatAllowRisky != null) _compatAllowRisky.Value = value; } }

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

        public static bool TranslateEnabled { get => _trEnabled != null && _trEnabled.Value; set { if (_trEnabled != null) _trEnabled.Value = value; } }
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

        private static void BuildDescriptors()
        {
            _descriptors.Clear();

            _descriptors.Add(Bool("roles.vanilla", "全般", "General", "本体の特殊役職も配る", "Also assign vanilla special roles", _vanillaRoles)
                .Tip("オン = サイエンティスト・エンジニア・ジャッジなど本体の役職も本体の設定どおりに出ます。オフ（既定）= クルーとインポスターだけにして、そこから PocketRoles の役職を配ります。",
                    "On = vanilla roles (Scientist, Engineer, Judge, ...) are assigned as set in the vanilla role settings. Off (default) = only Crewmates and Impostors, from which the PocketRoles roles are drawn.",
                    "开 = 科学家、工程师、法官等原版职业按原版设置出现。关（默认）= 只有船员和内鬼，PocketRoles 的职业从中分配。"));

            foreach (var r in Roles.All)
            {
                string sJa = r.NameJa, sEn = r.NameEn, color = r.Color;
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
                        _descriptors.Add(Bool("sheriff.killmadmate", sJa, sEn, "マッドメイトを撃てる", "Can kill Madmate", _sheriffCanKillMadmate, color)
                            .Tip("オンならマッドメイトを撃っても自分は死にません。", "On: shooting a Madmate does not kill the Sheriff.", "开启后射杀疯子船员不会让警长死亡。"));
                        break;
                    case CustomRole.Jackal:
                        _descriptors.Add(Float("jackal.cooldown", sJa, sEn, "キルクールダウン", "Kill cooldown", _jackalKillCooldown, 2.5f, 180f, 2.5f, color)
                            .Tip("ジャッカルのキルクールダウン（秒）。", "Jackal kill cooldown in seconds.", "豺狼的击杀冷却秒数。"));
                        _descriptors.Add(Bool("jackal.vent", sJa, sEn, "ベント使用", "Can vent", _jackalCanVent, color)
                            .Tip("ジャッカルがベントに入れるかどうか。", "Whether the Jackal can use vents.", "豺狼是否可以跳管。"));
                        break;
                    case CustomRole.Vampire:
                        _descriptors.Add(Float("vampire.delay", sJa, sEn, "噛みつき遅延", "Kill delay", _vampireKillDelay, 1f, 60f, 1f, color)
                            .Tip("噛みついてから相手が死ぬまでの秒数。", "Seconds between the bite and the victim's death.", "咬人后到受害者死亡的秒数。"));
                        break;
                    case CustomRole.Mayor:
                        _descriptors.Add(Int("mayor.votes", sJa, sEn, "票数", "Votes", _mayorVotes, 1, 5, 1, color)
                            .Tip("メイヤーの1票を何票として数えるか。", "How many votes the Mayor's single vote counts as.", "市长的一票算作几票。"));
                        break;
                    case CustomRole.Snitch:
                        _descriptors.Add(Int("snitch.tasks", sJa, sEn, "残りタスクで警告", "Tasks left to warn", _snitchTasksLeftToWarn, 0, 10, 1, color)
                            .Tip("残りタスクがこの数以下になるとキラーにスニッチが表示されます。", "Killers see the Snitch once this many tasks (or fewer) remain.", "剩余任务数不超过此值时，杀手会看到告密者。"));
                        break;
                    case CustomRole.Lighter:
                        _descriptors.Add(Float("lighter.vision", sJa, sEn, "視界倍率", "Vision multiplier", _lighterVision, 1f, 5f, 0.25f, color)
                            .Tip("ライターの視界の倍率。", "Vision multiplier of the Lighter.", "点灯者的视野倍率。"));
                        break;
                    case CustomRole.SpeedBooster:
                        _descriptors.Add(Float("speedbooster.speed", sJa, sEn, "速度倍率", "Speed multiplier", _speedBoosterSpeed, 1f, 3f, 0.25f, color)
                            .Tip("スピードブースターの移動速度の倍率。", "Movement speed multiplier of the Speed Booster.", "加速者的移动速度倍率。"));
                        break;
                    case CustomRole.Madmate:
                        _descriptors.Add(Bool("madmate.known", sJa, sEn, "インポスターに公開", "Known to impostors", _madmateKnownToImpostors, color)
                            .Tip("オンならインポスターに誰がマッドメイトか表示されます。", "On: Impostors see who the Madmate is.", "开启后内鬼可以看到谁是疯子船员。"));
                        break;
                    // v0.4.1
                    case CustomRole.Lovers:
                        _descriptors.Add(Bool("lovers.impostor", sJa, sEn, "インポスターも恋人になる", "Impostor may be a lover", _loversAllowImpostor, color)
                            .Tip("オンなら2人目の恋人がインポスターから選ばれることがあります（キルはできたままです）。", "On: the second lover may be a vanilla Impostor (it keeps its kill button).", "开启后第二位恋人可能从内鬼中选出（仍可击杀）。"));
                        _descriptors.Add(Bool("lovers.lastthree", sJa, sEn, "残り3人で勝利", "Win as last 3", _loversLastThree, color)
                            .Tip("オンなら2人とも生きていて生存者が3人以下になった時点でラバーズの勝利です。", "On: the Lovers win as soon as both are alive and at most 3 players remain.", "开启后两人存活且存活者不超过3人时恋人立即获胜。"));
                        break;
                    case CustomRole.Arsonist:
                        _descriptors.Add(Float("arsonist.cooldown", sJa, sEn, "油のクールダウン", "Douse cooldown", _arsonistDouseCooldown, 2.5f, 180f, 2.5f, color)
                            .Tip("油をかけてから次にかけられるまでの秒数。", "Seconds between two douses.", "两次浇油之间的冷却秒数。"));
                        _descriptors.Add(Bool("arsonist.vent", sJa, sEn, "ベント使用", "Can vent", _arsonistCanVent, color)
                            .Tip("放火魔がベントに入れるかどうか。", "Whether the Arsonist can use vents.", "纵火犯是否可以跳管。"));
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
                }
            }

            const string gJa = "全般", gEn = "General";
            _descriptors.Add(Bool("register", gJa, gEn, "MOD部屋登録（公式ルール・役職に必須）", "Mod-lobby registration (official rule, required for roles)", _register)
                .Tip("2026年7月からの公式ルールで、MODを使う部屋はサーバーに登録する必要があります。登録した部屋は公開一覧に出ないので、部屋コードか案内部屋から入ってもらいます。", "Since July 2026 the official servers require lobbies that use mods to register. Registered lobbies do not appear in the public list, so players join by room code or through the guide room.", "根据 2026 年 7 月起的官方规则，使用 MOD 的房间必须向服务器注册。已注册的房间不会出现在公开列表里，请用房间代码或引导房加入。"));
            _descriptors.Add(new OptionDescriptor
            {
                Key = "lang", SectionJa = gJa, SectionEn = gEn, NameJa = "言語", NameEn = "Language", Kind = OptionKind.Choice,
                Min = 0, Max = 2, Step = 1, Choices = new[] { "ja", "zh", "en" },
                GetNumber = () => Language == "en" ? 2f : (Language == "zh" ? 1f : 0f),
                SetNumber = v => Language = v >= 1.5f ? "en" : (v >= 0.5f ? "zh" : "ja"),
            }.Tip("チャットや説明の既定の言語。各プレイヤーは /lang で変更できます。", "Default language for chat texts; each player can change theirs with /lang.", "聊天文本的默认语言；每位玩家可用 /lang 更改。"));
            _descriptors.Add(Bool("welcome", gJa, gEn, "参加時の挨拶", "Welcome message", _welcome)
                .Tip("参加した人にこの部屋がMOD部屋であることを個別に知らせます。", "Privately tells every joining player that this lobby uses a host-side mod.", "私聊告知每位加入的玩家本房间使用房主模组。"));
            _descriptors.Add(Bool("roleinfo", gJa, gEn, "会議で役職説明", "Role info at meetings", _roleInfoAtMeeting)
                .Tip("会議開始時に各自の役職説明を個別に送り直します。", "Re-sends each player's role description privately when a meeting starts.", "会议开始时再次私聊发送各自的职业说明。"));
            _descriptors.Add(Bool("kick", gJa, gEn, "不正RPCでキック", "Kick on forged RPC", _antiCheatKick)
                .Tip("不正なホスト専用RPCを繰り返した人をキックします（オフは記録のみ）。", "Kicks a player who keeps sending forged host-only RPCs (off = log only).", "踢出反复发送伪造房主专用 RPC 的玩家（关闭则仅记录）。"));
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
            _descriptors.Add(Bool("compat.risky", lJa, lEn, "互換モード: シェリフ/ジャッカル許可", "Compat: allow Sheriff/Jackal", _compatAllowRisky)
                .Tip("登録オフ（互換モード）の部屋でもシェリフとジャッカルを配役します。ホスト権限がないためサーバーにキルを拒否されることがあります。", "Also assigns Sheriff and Jackal in an unregistered (compat mode) lobby. Without host authority the server may reject their kills.", "在未注册（兼容模式）房间中也分配警长和豺狼。没有房主权限时服务器可能拒绝其击杀。"));
            _descriptors.Add(Int("lobby.maxping", lJa, lEn, "高PINGなら部屋を作り直す(ms)", "Re-host when ping above (ms)", _maxHostPing, 0, 300, 10)
                .Tip("部屋を作った直後5秒間PINGがこの値(ms)を超え、まだ自分しかいなければ自動で部屋を作り直します（最大3回、0 = しない）。", "Right after creating the lobby, if the ping stays above this (ms) for 5 s while you are alone, the lobby is re-created automatically (up to 3 times; 0 = off).", "创建房间后 5 秒内延迟一直高于此值(ms)且房间里只有自己时，自动重新创建房间（最多 3 次，0 = 关闭）。"));
            _descriptors.Add(Bool("lobby.autostart", lJa, lEn, "自動開始", "Auto start", _autoStart)
                .Tip("設定した人数が揃ったら自動でゲームを開始します。", "Starts the game automatically once enough players are in.", "凑齐设定人数后自动开始游戏。"));
            _descriptors.Add(Int("lobby.autostartplayers", lJa, lEn, "自動開始の人数", "Auto start players", _autoStartPlayers, 4, 15, 1)
                .Tip("自動開始が始まる人数。", "Player count that triggers the automatic start.", "触发自动开始的人数。"));
            _descriptors.Add(Int("lobby.autostartcountdown", lJa, lEn, "開始カウントダウン(秒)", "Start countdown (s)", _autoStartCountdown, 1, 30, 1)
                .Tip("自動開始・強制開始前のカウントダウン秒数。", "Countdown seconds before an automatic or forced start.", "自动或强制开始前的倒计时秒数。"));
            _descriptors.Add(Choice("lobby.timermode", lJa, lEn, "ロビー残り時間の動作", "Lobby timer action", _timerMode, TimerModeChoices)
                .Tip("ロビーの制限時間が切れそうなときの動作（延長 / 廃村 / 通知のみ）。", "What to do when the lobby timer is about to expire (extend / haison / notify only).", "房间倒计时快结束时的处理（延长 / 废村 / 仅通知）。"));
            _descriptors.Add(Int("lobby.timerwarnat", lJa, lEn, "残り時間の警告(秒)", "Timer warning at (s)", _timerWarnAt, 30, 300, 10)
                .Tip("残り時間がこの秒数になったら警告して動作します。", "Lobby seconds left at which the warning and the action happen.", "剩余秒数达到此值时发出警告并执行动作。"));
            _descriptors.Add(Int("lobby.extenddelay", lJa, lEn, "警告から延長までの秒数", "Extend notice delay (s)", _extendNoticeDelay, 0, 60, 1)
                .Tip("警告から延長・廃村までの待ち秒数。", "Seconds between the warning and the extension / haison.", "从警告到延长或废村之间的等待秒数。"));
            _descriptors.Add(Bool("lobby.autoregion", lJa, lEn, "自動で最速の地域を選ぶ", "Auto lowest-ping region", _autoRegion)
                .Tip("部屋を作る前に各地域のpingを測り、最も速い地域を選びます。", "Pings the regions before hosting and picks the fastest one.", "创建房间前测试各区域延迟并选择最快的区域。"));
            _descriptors.Add(Bool("lobby.dleks", lJa, lEn, "逆スケルド(Dleks)を出す", "Offer Dleks map", _enableDleks)
                .Tip("マップ選択に逆スケルド（Dleks）を出します。バニラの人も遊べます。", "Offers the mirrored Skeld (Dleks) in the map picker; vanilla players can play it.", "在地图选择中提供镜像 Skeld（Dleks）；原版玩家也可以游玩。"));

            const string hJa = "ホスト支援", hEn = "Host tools";
            _descriptors.Add(Bool("gm", hJa, hEn, "ゲームマスター", "Game Master", _gameMaster)
                .Tip("ホストは役職を持たず、開始時に死亡して観戦・進行役になります。", "The host gets no role, dies at the start and only watches / moderates.", "房主不持有职业，开局即死亡，只观战和主持。"));
            _descriptors.Add(Bool("hotkeys", hJa, hEn, "ホットキー有効", "Hotkeys enabled", _hotkeysEnabled)
                .Tip("廃村・会議終了・開始キャンセルのホットキーを有効にします。", "Enables the host hotkeys for haison, end meeting and cancel start.", "启用废村、结束会议、取消开始的快捷键。"));
            _descriptors.Add(Hotkey("hotkeys.haison", hJa, hEn, "廃村キー(2回押し)", "Haison key (press twice)", _hotkeyHaison, "F7")
                .Tip("このキーを3秒以内に2回押すとゲームを廃村で終了します。", "Press this key twice within 3 seconds to end the game as haison.", "3 秒内按两次此键以废村结束游戏。"));
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
                .Tip("/move の 30 秒後に、この便利ホスト部屋を MOD 登録ありの役職部屋として作り直します（全員がコードで入り直し）。", "30 s after /move, re-creates this unregistered lobby as a registered role lobby (everyone rejoins with the new code).", "/move 30 秒后，把这个未注册房间重建为已注册的职业房（所有人用新代码重新加入）。"));
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
            // v0.4b chat translation rows (the DeepL key is a file, never a row)
            _descriptors.Add(Bool("translate.enabled", cJa, cEn, "チャット翻訳", "Chat translation", _trEnabled)
                .Tip("外国語のチャットを自動で翻訳します（文章は Google / DeepL に送られます）。", "Auto-translates foreign-language chat (text is sent to Google / DeepL).", "自动翻译外语聊天（文本会发送到 Google / DeepL）。"));
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
        /// "assassin.guesses", "assassin.firstmeeting" (v0.4.1), "lang", "enabled", "welcome", "roleinfo", "register", "kick", "general.ignoreversion",
        /// "lobby.autorehost", "lobby.autopublic", "lobby.autopublicdelay", "lobby.rehostmax", "lobby.maxping" (alias "maxping"), "compat.risky",
        /// "lobby.autostart", "lobby.autostartplayers", "lobby.autostartcountdown", "lobby.timermode", "lobby.timerwarnat",
        /// "lobby.extenddelay", "lobby.autoregion", "lobby.dleks", "gm", "hotkeys", "hotkeys.haison", "hotkeys.endmeeting",
        /// "hotkeys.cancelstart", "chat.welcometext", "chat.welcomesettings", "chat.playercommands", "chat.allcommands",
        /// "chat.rulesmode", "chat.rulestext", "cos.enabled", "cos.music", "cos.musicfile", "cos.musicvolume", "cos.lobbypaint",
        /// "cos.dropship", "cos.menubg", "cos.cursor", "credits.author", "credits.url", "credits.show",
        /// "chat.welcomeall", "translate.enabled", "translate.provider", "translate.target", "translate.showhost", "translate.broadcast",
        /// "translate.players", "translate.autodetect", "translate.minchars", "translate.maxperminute", "perm.adminsettings",
        /// "perm.modkick", "perm.vipmarker", "vanilla.ranges", "vanilla.killmin", "vanilla.killmax", "vanilla.killstep",
        /// "vanilla.votemin", "vanilla.votemax", "vanilla.discussmax", "vanilla.emergencymax", "vanilla.taskmax",
        /// "guide.overlay", "guide.code", "guide.autoreg" (v0.4e guide room).
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
                    if (!Lang.TryNormalize(value, out var lang)) { message = "lang: ja | zh | en"; return false; }
                    Language = lang; message = "lang = " + lang; return true;
                case "enabled": case "mod": return SetBool(_enabled, value, "enabled", out message);
                case "welcome": return SetBool(_welcome, value, "welcome", out message);
                case "roleinfo": return SetBool(_roleInfoAtMeeting, value, "roleinfo", out message);
                case "register": case "modded": case "+25": return SetBool(_register, value, "register", out message);
                case "kick": case "anticheatkick": return SetBool(_antiCheatKick, value, "kick", out message);
                case "general.ignoreversion": case "ignoreversion": return SetBool(_ignoreVersion, value, "general.ignoreversion", out message);
                case "lobby.autorehost": case "autorehost": case "rehost": return SetBool(_autoRehost, value, "lobby.autorehost", out message);
                case "lobby.autopublic": case "autopublic": return SetBool(_autoPublic, value, "lobby.autopublic", out message);
                case "lobby.autopublicdelay": case "autopublicdelay": return SetInt(_autoPublicDelay, value, 0, 60, "lobby.autopublicdelay", out message);
                case "lobby.rehostmax": case "lobby.rehostmaxattempts": case "rehostmax": return SetInt(_rehostMaxAttempts, value, 1, 10, "lobby.rehostmax", out message);
                case "lobby.maxping": case "lobby.maxhostping": case "maxping": case "maxhostping": return SetInt(_maxHostPing, value, 0, 300, "lobby.maxping", out message);
                case "compat.risky": case "compat.allowrisky": case "compat.allowriskyroles": case "risky": return SetBool(_compatAllowRisky, value, "compat.risky", out message);
                case "chat.welcometext": case "welcometext": return SetString(_welcomeText, value, "chat.welcometext", out message);
                case "chat.compatwelcome": case "compatwelcome": case "chat.compatwelcometext": return SetString(_compatWelcomeText, value, "chat.compatwelcome", out message);
                case "chat.welcomesettings": case "welcomesettings": return SetBool(_welcomeIncludeSettings, value, "chat.welcomesettings", out message);
                case "credits.author": return SetString(_creditAuthor, value, "credits.author", out message);
                case "credits.url": case "credits.repourl": return SetString(_creditRepoUrl, value, "credits.url", out message);
                case "credits.show": return SetBool(_showCredits, value, "credits.show", out message);
                case "roles.vanilla": case "vanillaroles": case "vanilla.roles": return SetBool(_vanillaRoles, value, "roles.vanilla", out message);
                case "roles.reveal": case "reveal": case "revealdeath": case "roles.revealroleondeath": return SetBool(_revealOnDeath, value, "roles.reveal", out message);
                case "chat.compatwelcomeinterval": case "compatwelcomeinterval": case "chat.welcomeinterval":
                {
                    if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sec) || sec < 0f || sec > 600f)
                    { message = "chat.compatwelcomeinterval: 0-600"; return false; }
                    CompatWelcomeInterval = sec; message = $"chat.compatwelcomeinterval = {CompatWelcomeInterval:0.#}"; return true;
                }
                // v0.4 lobby
                case "lobby.autostart": case "autostart": return SetBool(_autoStart, value, "lobby.autostart", out message);
                case "lobby.autostartplayers": case "autostartplayers": case "autostart.players": return SetInt(_autoStartPlayers, value, 4, 15, "lobby.autostartplayers", out message);
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
                // v0.4b translation (the DeepL key is never settable here: it lives in BepInEx/PocketRoles/deepl-key.txt)
                case "translate.enabled": case "translate": case "tr": return SetBool(_trEnabled, value, "translate.enabled", out message);
                case "translate.provider": case "tr.provider": return SetChoiceValue(_trProvider, TranslateProviderChoices, value, "translate.provider", out message);
                case "translate.target": case "translate.targetlang": case "translate.lang": case "tr.target":
                    if (!Lang.TryNormalize(value, out var trLang)) { message = "translate.target: ja | zh | en"; return false; }
                    TranslateTargetLang = trLang; message = "translate.target = " + trLang; return true;
                case "translate.showhost": case "translate.showonhost": case "tr.showhost": return SetBool(_trShowOnHost, value, "translate.showhost", out message);
                case "translate.broadcast": case "translate.broadcasttoall": case "translate.all": case "tr.broadcast": return SetBool(_trBroadcastToAll, value, "translate.broadcast", out message);
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
                case "vanilla.killmin": case "vanilla.killcooldownmin": return SetFloat(_vanKillMin, value, 0f, 60f, "vanilla.killmin", out message);
                case "vanilla.killmax": case "vanilla.killcooldownmax": return SetFloat(_vanKillMax, value, 10f, 600f, "vanilla.killmax", out message);
                case "vanilla.killstep": case "vanilla.killcooldownstep": return SetFloat(_vanKillStep, value, 0.5f, 10f, "vanilla.killstep", out message);
                case "vanilla.votemin": case "vanilla.votingtimemin": return SetInt(_vanVoteMin, value, 0, 300, "vanilla.votemin", out message);
                case "vanilla.votemax": case "vanilla.votingtimemax": return SetInt(_vanVoteMax, value, 15, 3600, "vanilla.votemax", out message);
                case "vanilla.discussmax": case "vanilla.discussionmax": case "vanilla.discussiontimemax": return SetInt(_vanDiscussMax, value, 0, 3600, "vanilla.discussmax", out message);
                case "vanilla.emergencymax": case "vanilla.emergencycooldownmax": return SetInt(_vanEmergencyMax, value, 0, 600, "vanilla.emergencymax", out message);
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
                }
                lines.Add(line);
            }
            if (!any) lines.Add(Lang.T("opt.none", "有効な役職はありません（/set <役職> <人数> で設定）", "No custom roles enabled (/set <role> <count>)"));
            lines.Add(Lang.TF("opt.general", "言語={0} MOD登録={1} 挨拶={2}", "lang={0} registration={1} welcome={2}",
                Language, HostAuthorityMode ? "on" : "off", WelcomeMessage ? "on" : "off"));
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
                lines.Add(Lang.TF("opt.compat", "互換モード（登録オフ・便利ホスト）: 役職={0}（バニラ進行）", "Compat mode (unregistered, 便利ホスト): roles={0} (vanilla game)", "off"));
            return lines;
        }

        public static void Reload()
        {
            _cfg?.Reload();
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
