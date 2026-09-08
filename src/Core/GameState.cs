using System.Collections.Generic;
using AmongUs.GameOptions;
using PocketRoles.Game;
using PocketRoles.Net;
using UnityEngine;

namespace PocketRoles.Core
{
    /// <summary>Per-game host-side state. The TRUE roles live here; vanilla Data.Role is desynced and must not be trusted.</summary>
    public static class Game
    {
        /// <summary>We are the host, the mod is enabled and the lobby is a classic (non Hide and Seek) game.</summary>
        public static bool IsHostActive
        {
            get
            {
                if (PocketRolesPlugin.PatchFailed) return false;
                // Wrong game build: stay inert unless the user explicitly opted in (Options.IgnoreVersionMismatch).
                if (VersionMismatch && !Options.IgnoreVersionMismatch) return false;
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || !Options.ModEnabled) return false;
                // Online: only in a lobby WE created (registered on creation per Innersloth's mod policy). After a host
                // migration into somebody else's vanilla lobby the mod must stay inert.
                if (client.NetworkMode == NetworkModes.OnlineGame && !Registration.Hosting) return false;
                var gom = GameOptionsManager.Instance;
                if (gom != null)
                {
                    var mode = gom.currentGameMode;
                    if (mode == GameModes.HideNSeek || mode == GameModes.SeekFools) return false;
                }
                return true;
            }
        }

        public static bool InProgress;
        public static bool Ending;
        public static bool AssigningRoles;

        /// <summary>Application.version differs from PocketRolesPlugin.SupportedGameVersion (set at startup / main menu).</summary>
        public static bool VersionMismatch;

        /// <summary>
        /// Test mode (per session, never persisted): 1-player start allowed, games never end automatically. Not cleared by
        /// <see cref="Reset"/>; TestMode.Set(false) and a new lobby (<see cref="OnLobbyJoined"/>) clear it.
        /// </summary>
        public static bool TestMode;

        /// <summary>
        /// 廃村 (haison) game in progress: the Lobby module starts a game only to end it immediately so everyone stays in
        /// the same lobby with a fresh timer. RoleAssignment dispatches nothing and WinConditions ignores the game while
        /// this is true. Set/cleared by the Lobby module (Haison); not touched by <see cref="Reset"/>.
        /// </summary>
        public static bool HaisonActive;

        /// <summary>
        /// Game Master mode is in effect for the current game (Options.GameMaster was on at start): the host got no
        /// custom role, is exiled after the intro and is ignored by win conditions. Set/cleared by the GameMaster module;
        /// not touched by <see cref="Reset"/>.
        /// </summary>
        public static bool GameMasterActive;

        /// <summary>playerId → role forced by /assign; consumed (and cleared) by RoleAssignment at game start.</summary>
        public static Dictionary<byte, CustomRole> ForcedRoles = new Dictionary<byte, CustomRole>();

        public static Dictionary<byte, CustomRole> Roles = new Dictionary<byte, CustomRole>();
        public static Dictionary<byte, RoleTypes> VanillaRoles = new Dictionary<byte, RoleTypes>();
        public static Dictionary<byte, string> OriginalNames = new Dictionary<byte, string>();

        /// <summary>Delayed, host-executed death (Kills.Tick). Reason: null = vampire bite, "lovers", "curse", "assassin" (log only).</summary>
        public struct VampireBite { public byte Killer; public float DueAt; public string Reason; }
        public static Dictionary<byte, VampireBite> Bites = new Dictionary<byte, VampireBite>();

        // ---- v0.4.1 role state (cleared in Reset / ResetForNewLobby)
        /// <summary>Lovers pair (255 = none). Set by Lovers.Assign during AssignCustomRoles.</summary>
        public static byte LoverA = 255, LoverB = 255;
        /// <summary>arsonist playerId → players it doused.</summary>
        public static Dictionary<byte, HashSet<byte>> Doused = new Dictionary<byte, HashSet<byte>>();
        /// <summary>spelled target playerId → witch playerId (cleared at ExileController.WrapUp).</summary>
        public static Dictionary<byte, byte> Spelled = new Dictionary<byte, byte>();
        /// <summary>assassin playerId → guesses used in the current meeting (cleared at MeetingHud.Start).</summary>
        public static Dictionary<byte, int> GuessesThisMeeting = new Dictionary<byte, int>();
        /// <summary>Meetings started in this game (MeetingHud.Start postfix; the first meeting is 1).</summary>
        public static int MeetingsHeld;

        public static bool IsLover(byte id) => id != 255 && (id == LoverA || id == LoverB);
        public static byte PartnerOf(byte id) => id == 255 ? (byte)255 : (id == LoverA ? LoverB : (id == LoverB ? LoverA : (byte)255));

        private static void ResetRoleState()
        {
            LoverA = LoverB = 255;
            Doused.Clear();
            Spelled.Clear();
            GuessesThisMeeting.Clear();
            MeetingsHeld = 0;
        }

        public static HashSet<byte> ExtraWinners = new HashSet<byte>();
        public static CustomRole SoloWinner = CustomRole.None;
        public static byte SoloWinnerId = 255;
        public static byte LastExiled = 255;

        public static byte[] BaseOptionBytes;

        public sealed class SummaryEntry
        {
            public byte Id;
            public string Name;
            public CustomRole Role;
            public RoleTypes Vanilla;
            public bool Dead;
            public bool Winner;
        }

        /// <summary>Filled by WinConditions.EndGame; shown by /last and the lobby summary. Survives Reset().</summary>
        public static List<SummaryEntry> LastSummary;
        public static string LastWinnerText;

        public static void Reset()
        {
            InProgress = false;
            Ending = false;
            AssigningRoles = false;
            Roles.Clear();
            VanillaRoles.Clear();
            OriginalNames.Clear();
            Bites.Clear();
            ResetRoleState();
            ExtraWinners.Clear();
            SoloWinner = CustomRole.None;
            SoloWinnerId = 255;
            LastExiled = 255;
            BaseOptionBytes = null;
            // TestMode / ForcedRoles deliberately survive: they are per-session lobby state (see OnLobbyJoined).
        }

        // ------------------------------------------------------------------ lobby identity (play-again keeps the GameId)

        private static int _lastGameId = int.MinValue;
        private static int _joinFrame = -1;
        private static bool _joinSame;

        /// <summary>
        /// Called from the OnGameJoined postfixes: true when the lobby just joined is the SAME lobby as before (vanilla
        /// "play again" fires OnGameJoined for the same GameId). Every caller in the same frame gets the same answer.
        /// </summary>
        public static bool JoinedSameLobby()
        {
            int frame = Time.frameCount;
            if (_joinFrame == frame) return _joinSame;
            int id = int.MinValue;
            try
            {
                var client = AmongUsClient.Instance;
                if (client != null) id = client.GameId;
            }
            catch (System.Exception) { id = int.MinValue; }
            _joinSame = id != int.MinValue && id == _lastGameId;
            _lastGameId = id;
            _joinFrame = frame;
            return _joinSame;
        }

        /// <summary>
        /// A lobby was joined or created (AmongUsClient.OnGameJoined). Session-scoped test state does not carry over
        /// into a DIFFERENT lobby; the play-again rejoin of the same lobby keeps TestMode (verify finding #8 b1).
        /// </summary>
        public static void OnLobbyJoined()
        {
            if (JoinedSameLobby())
            {
                PocketRolesPlugin.Logger.LogInfo($"Game: rejoined the same lobby (TestMode={(TestMode ? "on" : "off")} kept)");
                return;
            }
            TestMode = false;
            ForcedRoles.Clear();
        }

        /// <summary>
        /// Single reset for "the next game must load normally" (verify finding #8): called from AmongUsClient.OnGameEnd,
        /// AmongUsClient.OnGameJoined, AmongUsClient.OnDisconnected and GameStartManager.Start (reason "LobbyStart").
        /// Clears the per-game flags of every module (Haison live state, AutoStart pending / forced flags, the OptionsDesync
        /// capture, AntiBlackout, the WinConditions guards, forced test roles, game-scoped scheduler entries).
        /// Keeps: TestMode on/off, OriginalNames (restored at LobbyBehaviour.Start), LastSummary / SummaryShown,
        /// WinConditions.EndedByMod (end screen text) and Haison's "last game was a haison" latch (consumed at LobbyBehaviour.Start).
        /// </summary>
        public static void ResetForNewLobby(string reason)
        {
            try
            {
                bool gameEnd = reason == "OnGameEnd";
                bool lobbyStart = reason == "LobbyStart";
                bool disconnected = reason == "OnDisconnected";
                bool sameLobby = reason == "OnGameJoined" && JoinedSameLobby();

                InProgress = false;
                Ending = false;
                AssigningRoles = false;
                Bites.Clear();
                ResetRoleState();
                SoloWinner = CustomRole.None;
                SoloWinnerId = 255;
                LastExiled = 255;
                BaseOptionBytes = null;   // OptionsDesync captures the base again at the next SelectRoles
                // /assign list: consumed (and cleared) by ApplyForcedRoles at a real game start. A haison game never
                // consumes it (SelectRoles returns early), so game end / play-again rejoin / lobby start keep it for
                // the "next game" the host asked for; leaving the lobby (disconnect, a different lobby) drops it.
                if (!gameEnd && !lobbyStart && !sameLobby) ForcedRoles.Clear();
                if (disconnected)
                {
                    _lastGameId = int.MinValue;
                    _joinFrame = -1;
                }

                // Scheduler: on game end / join / disconnect everything goes (the old behaviour of RoleAssignment.Cleanup);
                // at lobby start only the game-scoped tags (LobbyBehaviour.Start may have queued lobby work already).
                if (lobbyStart)
                {
                    foreach (var tag in GameScopedTags) Scheduler.Cancel(tag);
                }
                else
                {
                    Rpc.ResetReviveState(); // Scheduler.Clear() would drop a pending temp-revive restore
                    Scheduler.Clear();
                    Rpc.Queue.Clear();
                }

                WinConditions.ResetGuards();
                AntiBlackout.Clear();
                // OptionsDesync: the "who got private options" set must survive until LobbyBehaviour.Start → RestoreAll()
                // (game end, play-again rejoin and the lobby scene load all happen before that); a different lobby or a
                // disconnect drops it.
                if (!gameEnd && !lobbyStart && !sameLobby) OptionsDesync.Reset();
                Lobby.Haison.ResetLive(reason);
                Lobby.AutoStart.ResetAll();
                PocketRolesPlugin.Logger.LogInfo($"Game: reset for a new lobby ({reason}{(sameLobby ? ", same lobby" : "")})");
            }
            catch (System.Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Game.ResetForNewLobby({reason}): {e}");
            }
        }

        /// <summary>Scheduler tags that belong to a running game (cancelled at lobby start).</summary>
        private static readonly string[] GameScopedTags =
        {
            "win.end", "assign.roleinfo", "assign.introend", "haison.end", "antiblackout.restore", "names.meeting", "gm.apply"
        };

        public static CustomRole RoleOf(byte id)
        {
            return Roles.TryGetValue(id, out var r) ? r : CustomRole.None;
        }

        public static RoleInfo InfoOf(byte id) => Core.Roles.Info(RoleOf(id));

        public static bool HasCustomRole(byte id) => RoleOf(id) != CustomRole.None;

        /// <summary>True vanilla role as chosen by RoleManager (falls back to the current Data.RoleType).</summary>
        public static RoleTypes VanillaRoleOf(byte id)
        {
            if (VanillaRoles.TryGetValue(id, out var r)) return r;
            var info = Info(id);
            return info != null ? info.RoleType : RoleTypes.Crewmate;
        }

        private static bool IsVanillaImpostorRole(RoleTypes r)
        {
            return r == RoleTypes.Impostor || r == RoleTypes.Shapeshifter || r == RoleTypes.Phantom || r == RoleTypes.Viper || r == RoleTypes.ImpostorGhost;
        }

        public static Team TeamOf(byte id)
        {
            var role = RoleOf(id);
            if (role != CustomRole.None) return Core.Roles.Info(role).Team;
            return IsVanillaImpostorRole(VanillaRoleOf(id)) ? Team.Impostor : Team.Crew;
        }

        /// <summary>Vanilla impostor-type role incl. Vampire/Mafia/Witch/Assassin (and an impostor lover) — NOT Madmate.</summary>
        public static bool IsImpostorTeamKiller(byte id)
        {
            var role = RoleOf(id);
            if (role == CustomRole.Vampire || role == CustomRole.Mafia || role == CustomRole.Witch || role == CustomRole.Assassin) return true;
            if (role == CustomRole.Lovers) return IsVanillaImpostorRole(VanillaRoleOf(id)); // an impostor lover keeps its kill button and counts as an impostor
            if (role != CustomRole.None) return false;
            return IsVanillaImpostorRole(VanillaRoleOf(id));
        }

        public static bool IsDesyncImpostor(byte id) => InfoOf(id).ImpostorDesync;

        public static bool IsJackal(byte id) => RoleOf(id) == CustomRole.Jackal;

        /// <summary>Any player with a kill button that is not on the crew side (real impostors, Vampire, Mafia, Jackal, but not Sheriff).</summary>
        public static bool IsNonCrewKiller(byte id) => IsImpostorTeamKiller(id) || IsJackal(id);

        public static NetworkedPlayerInfo Info(byte id)
        {
            var gd = GameData.Instance;
            if (gd == null) return null;
            return gd.GetPlayerById(id);
        }

        public static PlayerControl Player(byte id)
        {
            var info = Info(id);
            if (info == null) return null;
            var pc = info.Object;
            return pc == null ? null : pc;
        }

        public static bool IsDead(byte id)
        {
            var info = Info(id);
            if (info == null) return true;
            // While the temporary AntiBlackout views are in effect a "revived" player's wire state may say alive; the
            // snapshot keeps it dead. A player that was alive in the snapshot but died since (bite, quick kill) falls
            // through to the real info.IsDead so nothing is counted alive twice.
            if (AntiBlackout.Active && AntiBlackout.RealIsDead != null && AntiBlackout.RealIsDead.TryGetValue(id, out var real) && real) return true;
            // A dead host is briefly marked alive on the wire while it sends chat (Rpc.TempReviveHostForChat);
            // host logic must keep counting it as dead or a win check inside that window uses wrong numbers.
            if (Rpc.HostTempRevived)
            {
                var lp = PlayerControl.LocalPlayer;
                if (lp != null && lp.PlayerId == id) return true;
            }
            return info.IsDead;
        }

        public static bool IsAlive(byte id)
        {
            var info = Info(id);
            if (info == null || info.Disconnected) return false;
            return !IsDead(id);
        }

        public static bool IsHost(byte id)
        {
            var pc = Player(id);
            return pc != null && pc.AmOwner;
        }

        /// <summary>Snapshot of all PlayerControls that have data (managed list).</summary>
        public static List<PlayerControl> AllPlayers()
        {
            var list = new List<PlayerControl>();
            var all = PlayerControl.AllPlayerControls;
            if (all == null) return list;
            foreach (var pc in all)
            {
                if (pc == null || pc.Data == null) continue;
                list.Add(pc);
            }
            return list;
        }

        public static List<byte> AllPlayerIds()
        {
            var ids = new List<byte>();
            foreach (var pc in AllPlayers()) ids.Add(pc.PlayerId);
            return ids;
        }

        public static string NameOf(byte id)
        {
            if (OriginalNames.TryGetValue(id, out var n) && !string.IsNullOrEmpty(n)) return n;
            var info = Info(id);
            return info != null ? info.PlayerName : ("#" + id);
        }

        public static int TasksLeft(byte id)
        {
            var info = Info(id);
            if (info == null || info.Tasks == null) return 0;
            int left = 0;
            foreach (var t in info.Tasks)
            {
                if (t != null && !t.Complete) left++;
            }
            return left;
        }

        public static int TasksTotal(byte id)
        {
            var info = Info(id);
            if (info == null || info.Tasks == null) return 0;
            return info.Tasks.Count;
        }

        /// <summary>All tasks complete (an empty task list counts as NOT done).</summary>
        public static bool TasksDone(byte id)
        {
            return TasksTotal(id) > 0 && TasksLeft(id) == 0;
        }
    }
}
