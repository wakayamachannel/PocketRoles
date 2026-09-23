using System;
using System.Collections.Generic;
using System.Text;
using AmongUs.GameOptions;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Net;

namespace PocketRoles.Game
{
    /// <summary>
    /// Custom role assignment and per-client role views (DESIGN.md §4).
    /// The vanilla RoleManager still chooses the true vanilla roles; we intercept every RpcSetRole broadcast during
    /// SelectRoles, pick custom roles from the plain Crewmate / plain Impostor pools and then send every client its own
    /// desynced view of the role table (TECH-NOTES "Role assignment").
    /// </summary>
    public static class RoleAssignment
    {
        private static readonly Random Rand = new Random(Environment.TickCount);

        /// <summary>Time.time when the current RoleManager.SelectRoles began (-1 = none); the dispatch log shows the delay.</summary>
        internal static float SelectRolesStartedAt = -1f;

        /// <summary>
        /// The vanilla role that <paramref name="viewerId"/>'s client should hold for <paramref name="targetId"/>.
        /// Desync rule: a non-impostor with a kill button (Sheriff, Jackal) sees itself as Impostor and every real impostor
        /// as Crewmate; everyone else sees the desync player as Crewmate; real impostors see each other normally.
        /// Dead targets map to the ghost equivalent.
        /// </summary>
        public static RoleTypes View(byte viewerId, byte targetId)
        {
            bool viewerDesync = viewerId != targetId && Core.Game.IsDesyncImpostor(viewerId);
            RoleTypes vanilla = Core.Game.VanillaRoleOf(targetId);

            if (Core.Game.IsDead(targetId))
            {
                // A desync viewer saw every other player as Crewmate while alive; keep that consistent after death.
                if (viewerDesync) return RoleTypes.CrewmateGhost;
                // A desync player's own client holds Impostor while alive: its ghost must be ImpostorGhost (a CrewmateGhost
                // sent to an Impostor client left the phone on a black screen — 2-player test 2026-09-08 23:45).
                if (viewerId == targetId && Core.Game.IsDesyncImpostor(targetId)) return RoleTypes.ImpostorGhost;
                return Core.Game.IsImpostorTeamKiller(targetId) ? RoleTypes.ImpostorGhost : RoleTypes.CrewmateGhost;
            }

            if (viewerId == targetId)
                return Core.Game.IsDesyncImpostor(targetId) ? RoleTypes.Impostor : LiveRole(vanilla);

            if (Core.Game.IsDesyncImpostor(targetId)) return RoleTypes.Crewmate;

            if (viewerDesync && IsImpostorRole(vanilla)) return RoleTypes.Crewmate;

            return LiveRole(vanilla);
        }

        /// <summary>Ghost roles recorded as "vanilla" (should not happen) are mapped back to a living role.</summary>
        private static RoleTypes LiveRole(RoleTypes r)
        {
            if (r == RoleTypes.ImpostorGhost) return RoleTypes.Impostor;
            if (r == RoleTypes.CrewmateGhost || r == RoleTypes.GuardianAngel) return RoleTypes.Crewmate;
            return r;
        }

        internal static bool IsImpostorRole(RoleTypes r)
        {
            return r == RoleTypes.Impostor || r == RoleTypes.Shapeshifter || r == RoleTypes.Phantom || r == RoleTypes.Viper || r == RoleTypes.ImpostorGhost;
        }

        /// <summary>playerId of the player owned by clientId, or 255 when that client has no player.</summary>
        private static byte ViewerIdOf(int clientId, List<PlayerControl> players)
        {
            foreach (var pc in players)
            {
                if (pc.OwnerId == clientId) return pc.PlayerId;
            }
            return 255;
        }

        private static byte HostPlayerId()
        {
            var lp = PlayerControl.LocalPlayer;
            return lp == null ? (byte)255 : lp.PlayerId;
        }

        // ------------------------------------------------------------------ initial assignment

        // ------------------------------------------------------------------ vanilla SelectRoles watch (v0.5.1)

        /// <summary>True between the RoleManager.SelectRoles prefix and postfix: every RpcSetRole in that window is vanilla's own.</summary>
        internal static bool VanillaSelecting;
        private static int _selectImpostorsSeen, _selectTarget;
        /// <summary>v0.5.2 (Designate): the impostor count this selection should reach, and how many impostor roles vanilla has sent so far.</summary>
        internal static int SelectTarget => _selectTarget;
        internal static int SelectImpostorsSeen => _selectImpostorsSeen;
        private static readonly List<byte> _selectRestored = new List<byte>();

        /// <summary>HostWish moved a converted Impostor from one player to another: the notice names the player who really holds it.</summary>
        internal static void RestoredRedirected(byte from, byte to)
        {
            int i = _selectRestored.IndexOf(from);
            if (i >= 0) _selectRestored[i] = to;
        }

        /// <summary>
        /// SelectRoles prefix (both lobby modes, not for haison games): remember how many impostors this game should have
        /// (GetAdjustedNumImpostors; 0 with fewer than 3 players = never convert) so that <see cref="TakeImpostorDefault"/>
        /// can recognise the impostor pass's Crewmate defaults.
        /// </summary>
        internal static void BeginVanillaSelection()
        {
            try
            {
                VanillaSelecting = true;
                _selectImpostorsSeen = 0;
                _selectRestored.Clear();
                int connected = 0;
                foreach (var pc in Core.Game.AllPlayers())
                    if (pc != null && pc.Data != null && !pc.Data.Disconnected) connected++;
                _selectTarget = ImpostorSlots(connected);
                HostWish.OnBegin();
                Designate.OnBegin();   // v0.5.2: after HostWish (reads the host's wish)
                Kills.ResetHostShield();   // v0.5.2
                GuardianLimit.Reset();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"RoleAssignment.BeginVanillaSelection: {e}");
                _selectTarget = 0;
            }
        }

        /// <summary>
        /// Vanilla's RpcSetRole during SelectRoles (from the RpcSetRole prefix). 2026.8.18 quirk (live 2026-09-14: 14
        /// players, 3 impostors, Shapeshifter + Phantom at 100 % — the third impostor pick was sent Crewmate; the 3-player
        /// tests: the only pick was sent Crewmate): once the impostor pass's special-role list is used up, its remaining
        /// picks get the pass's default, which is Crewmate, while the crew pass never sends a plain Crewmate at all
        /// (finding #39). So a Crewmate handed out while the impostor count is still short IS an impostor pick: that
        /// player gets the plain Impostor instead — vanilla's own choice, and the first (only) SetRole any client sees for
        /// it. Returns true when the role must be replaced by Impostor.
        /// </summary>
        internal static bool TakeImpostorDefault(PlayerControl pc, RoleTypes role)
        {
            try
            {
                if (!VanillaSelecting || pc == null) return false;
                if (IsImpostorRole(role)) { _selectImpostorsSeen++; return false; }
                if (role != RoleTypes.Crewmate || _selectImpostorsSeen >= _selectTarget) return false;
                if (Core.Game.GameMasterActive && Core.Game.IsHost(pc.PlayerId)) return false;   // the Game Master never plays
                _selectImpostorsSeen++;
                _selectRestored.Add(pc.PlayerId);
                PocketRolesPlugin.Logger.LogWarning($"RoleAssignment: vanilla SelectRoles sent Crewmate to #{pc.PlayerId} {Core.Game.NameOf(pc.PlayerId)} while its impostor count was {_selectImpostorsSeen - 1} of {_selectTarget} — the impostor pick keeps Impostor");
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"RoleAssignment.TakeImpostorDefault: {e}");
                return false;
            }
        }

        /// <summary>
        /// v0.5.5 (known bug of v0.5.4: a 3-player forced start in an unregistered lobby had no impostor and the crew won at
        /// once). Vanilla's clamp gives 3 players no impostor role at all, so the impostor pass sends nothing, and with the
        /// crew specials at high rates the crew pass hands every player a special. Then nobody is left without a SetRole and
        /// FillImpostors, which in compat can only promote such players (2026.8.18 clients apply only the first SetRole they
        /// receive), has no candidate. So in compat, while vanilla still owes impostor roles, a crew role is withheld when
        /// fewer OTHER players than it owes are still without a role and may be made impostor: this player stays roleless,
        /// the top-up makes it the impostor and the crew special is dropped (the same move as for a designee in
        /// Designate.Intercept and for the wishing host in HostWish.OnEnd). The crew pass visits players in vanilla's random
        /// order, so the impostor is whoever it reaches last. A designated crewmate or a host who wished to be crew is
        /// withheld only when nobody else without a role is left at all (FillImpostors' own last resort: never a game without
        /// an impostor). No change for registered lobbies (FillImpostors promotes crew specials there), for 4+ players and
        /// for test mode (vanilla's impostor pass already sends the target: nothing owed), or below 3 players (FillImpostors
        /// does nothing there). Called from the RpcSetRole prefix after HostWish and Designate declined the send; true = drop it.
        /// </summary>
        internal static bool WithholdForImpostorFill(PlayerControl pc, RoleTypes role)
        {
            try
            {
                if (!VanillaSelecting || !Registration.CompatMode || HostWish.Redirecting || pc == null || pc.Data == null) return false;
                if (IsImpostorRole(role) || pc.roleAssigned || GameMasterHost(pc.PlayerId)) return false;
                int owed = _selectTarget - _selectImpostorsSeen;
                if (owed <= 0) return false;
                int connected = 0, freeFill = 0, freeAny = 0;
                foreach (var p in Core.Game.AllPlayers())
                {
                    if (p == null || p.Data == null || p.Data.Disconnected) continue;
                    connected++;
                    if (p.PlayerId == pc.PlayerId || p.roleAssigned || GameMasterHost(p.PlayerId) || HostMayTakeHeldRole(p.PlayerId)) continue;
                    freeAny++;
                    if (FillCandidate(p.PlayerId)) freeFill++;
                }
                if (connected < 3) return false;
                bool candidate = FillCandidate(pc.PlayerId);
                if (candidate ? freeFill >= owed : freeAny >= owed) return false;
                PocketRolesPlugin.Logger.LogWarning($"RoleAssignment: {role} for #{pc.PlayerId} {Core.Game.NameOf(pc.PlayerId)} withheld{(candidate ? "" : " (designated crew / crew wish: nobody else is left)")} — vanilla owes {owed} impostor role(s) and only {freeFill} other player(s) the top-up may promote are still without a role (compat: it can promote only those)");
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"RoleAssignment.WithholdForImpostorFill: {e}");
                return false;
            }
        }

        private static bool GameMasterHost(byte id) => Core.Game.GameMasterActive && Core.Game.IsHost(id);

        /// <summary>A host with a crew or vanilla-role wish: HostWish may hand it a held role at the end of the selection.</summary>
        private static bool HostMayTakeHeldRole(byte id) => Core.Game.IsHost(id) && HostWish.IsSet && HostWish.Wish != HostWish.Kind.Impostor;

        /// <summary>A player the compat top-up should make an impostor first: not a designated crewmate (FillImpostors takes those last) and not a host who wished otherwise.</summary>
        private static bool FillCandidate(byte id) => !Designate.FillAvoid.Contains(id) && !HostMayTakeHeldRole(id);

        /// <summary>SelectRoles postfix, first thing: vanilla is done; tell the host what was restored.</summary>
        internal static void EndVanillaSelection()
        {
            if (!VanillaSelecting) return;
            VanillaSelecting = false;
            HostWish.OnEnd();   // releases a held role, tells the host, consumes the wish
            Designate.OnEnd();  // v0.5.2: after HostWish (its crew-wish handoff asks Designate.Taker)
            if (_selectRestored.Count == 0) return;
            try
            {
                Chat.Chat.Local(Chat.Chat.Title, Lang.TF("assign.impostors.filled",
                    "インポスターが {0} 人中 {1} 人しかいなかったので、{2} をインポスターにしました。",
                    "Only {1} of {0} impostors were assigned; promoted {2} to Impostor.",
                    _selectTarget, _selectImpostorsSeen - _selectRestored.Count, string.Join(", ", _selectRestored.ConvertAll(id => Core.Game.NameOf(id)))));
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"RoleAssignment.EndVanillaSelection: {e}"); }
            _selectRestored.Clear();
        }

        /// <summary>
        /// Impostor count guard (v0.5.1, generalising the 2026-09-09 safety net of findings #49/#50): vanilla 2026.8.18
        /// hands out fewer impostors than the lobby setting when its special-role pass eats the plain-Impostor fill —
        /// seen live 2026-09-14 (14 players, 3 impostors set, Shapeshifter 1 / 100 % + Phantom 1 / 100 %: the third
        /// impostor pick was sent Crewmate), and the 3-player tests of 2026-09-09 got no impostor at all. The official
        /// server also disconnects the host ("Hacking", ban points) for a role table without any Impostor-type role.
        /// Fallback: <see cref="TakeImpostorDefault"/> normally keeps vanilla's own impostor picks; this runs afterwards.
        /// Promotes random plain crewmates (vanilla crew specials only when no plain crewmate is left) until the count
        /// matches GetAdjustedNumImpostors(players) — the vanilla clamp
        /// of the lobby setting (test mode: the TestMode patch value); never demotes. With fewer than 3 players nothing
        /// is done (the 2-player tests tolerate an impostor-less game).
        /// Registered lobby (compat = false): the vanilla roles were recorded and their broadcasts dropped, so the
        /// promoted player's first (and only) SetRole is its per-client view from DispatchInitialRoles; the host's own
        /// table is updated here (ApplyRoleLocal). Forced test roles and players already holding a custom role are skipped.
        /// Unregistered lobby (compat = true, called before AssignPlainRoles): the plain Impostor goes out as the normal
        /// vanilla broadcast — only players without any SetRole yet qualify, because 2026.8.18 clients apply just the
        /// first SetRole they receive for a player (2026-09-08 phone test).
        /// </summary>
        internal static void FillImpostors(List<PlayerControl> players, HashSet<byte> forced, bool compat)
        {
            try
            {
                if (players == null || players.Count < 3) return;
                int connected = 0, impostors = 0;
                var candidates = new List<PlayerControl>();
                var specials = new List<PlayerControl>();   // vanilla crew specials ([Roles] VanillaRoles = on): last resort only
                var reluctant = new List<PlayerControl>();  // v0.5.2: players the host designated as crew (Designate): after everybody else
                foreach (var pc in players)
                {
                    if (pc == null || pc.Data == null || pc.Data.Disconnected) continue;
                    connected++;
                    byte id = pc.PlayerId;
                    RoleTypes v = compat
                        ? (pc.Data.Role != null ? pc.Data.Role.Role : RoleTypes.Crewmate)
                        : Core.Game.VanillaRoleOf(id);
                    if (IsImpostorRole(v)) { impostors++; continue; }
                    if (compat)
                    {
                        if (pc.roleAssigned) continue;   // vanilla already sent this player a role: the client ignores a second SetRole
                    }
                    else
                    {
                        if (forced != null && forced.Contains(id)) continue;
                        if (Core.Game.RoleOf(id) != CustomRole.None) continue;
                    }
                    if (Core.Game.GameMasterActive && Core.Game.IsHost(id)) continue;
                    if (Designate.FillAvoid.Contains(id)) { reluctant.Add(pc); continue; }
                    // [Roles] VanillaRoles = on: a vanilla Scientist / Engineer / … keeps its role while a plain crewmate is available
                    if (!compat && v != RoleTypes.Crewmate) { specials.Add(pc); continue; }
                    candidates.Add(pc);
                }
                int target = ExpectedImpostors(connected);
                if (target < 1) target = 1;   // the old safety net: never an impostor-less table
                int missing = target - impostors;
                if (missing <= 0) return;
                if (candidates.Count == 0 && specials.Count == 0 && reluctant.Count == 0)
                {
                    PocketRolesPlugin.Logger.LogWarning($"RoleAssignment: the role table has {impostors} of {target} impostor(s) ({connected} players) and nobody can be promoted");
                    return;
                }
                var rnd = new System.Random();
                int promoted = 0;
                var names = new StringBuilder();
                while (missing > 0 && (candidates.Count > 0 || specials.Count > 0 || reluctant.Count > 0))
                {
                    // v0.5.2: a designated impostor (Designate) vanilla never picked is promoted first
                    List<PlayerControl> pool = null;
                    int i = -1;
                    foreach (byte want in Designate.FillPrefer)
                    {
                        pool = candidates; i = candidates.FindIndex(x => x.PlayerId == want);
                        if (i < 0) { pool = specials; i = specials.FindIndex(x => x.PlayerId == want); }
                        if (i >= 0) break;
                    }
                    bool designated = i >= 0;
                    if (i < 0)
                    {
                        // no designation: the v0.5.1 pick (plain crewmates, then vanilla crew specials), then the designated crew
                        pool = candidates.Count > 0 ? candidates : specials.Count > 0 ? specials : reluctant;
                        i = rnd.Next(pool.Count);
                    }
                    var pick = pool[i];
                    pool.RemoveAt(i);
                    if (compat)
                    {
                        // AssigningRoles is off: the plain vanilla broadcast (+ the host's CoSetRole); roleAssigned becomes true,
                        // so AssignPlainRoles leaves this player alone
                        pick.RpcSetRole(RoleTypes.Impostor, false);
                    }
                    else
                    {
                        Core.Game.VanillaRoles[pick.PlayerId] = RoleTypes.Impostor;
                        // the host's own table too (as ApplyForcedRoles / DowngradeImpostorSpecials do), or vanilla on the host
                        // keeps treating the promoted player as a crewmate (exile text, CanBeKilled, ghost role pick)
                        Rpc.ApplyRoleLocal(pick, RoleTypes.Impostor);
                    }
                    promoted++;
                    missing--;
                    if (names.Length > 0) names.Append(", ");
                    names.Append(Core.Game.NameOf(pick.PlayerId));
                    PocketRolesPlugin.Logger.LogWarning($"RoleAssignment: the role table had {impostors} of {target} impostor(s) ({connected} players) — promoted #{pick.PlayerId} {Core.Game.NameOf(pick.PlayerId)} to Impostor ({(compat ? "vanilla broadcast" : "host table")}{(ReferenceEquals(pool, specials) ? ", was a vanilla crew special" : "")}{(designated ? ", designated" : "")}{(ReferenceEquals(pool, reluctant) ? ", designated crew (last resort)" : "")})");
                }
                if (missing > 0)
                    PocketRolesPlugin.Logger.LogWarning($"RoleAssignment: still {missing} impostor(s) short after promoting {promoted} (no candidates left)");
                Chat.Chat.Local(Chat.Chat.Title, Lang.TF("assign.impostors.filled",
                    "インポスターが {0} 人中 {1} 人しかいなかったので、{2} をインポスターにしました。",
                    "Only {1} of {0} impostors were assigned; promoted {2} to Impostor.",
                    target, impostors, names.ToString()));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"RoleAssignment.FillImpostors: {e}");
            }
        }

        /// <summary>
        /// The impostor count this game should have: the vanilla clamp of the lobby setting (GetAdjustedNumImpostors:
        /// below 7 players 1, below 9 players 2; test mode: the TestMode patch value). -1 when unavailable.
        /// </summary>
        internal static int ExpectedImpostors(int playerCount)
        {
            try
            {
                var gom = GameOptionsManager.Instance;
                if (gom == null || gom.CurrentGameOptions == null) return -1;
                return IGameOptionsExtensions.GetAdjustedNumImpostors(gom.CurrentGameOptions, playerCount);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"RoleAssignment.ExpectedImpostors: {e.Message}");
                return -1;
            }
        }

        /// <summary>The impostor slots a selection with <paramref name="connected"/> players has (0 below 3 players, else at least 1): what BeginVanillaSelection targets and Designate counts against.</summary>
        internal static int ImpostorSlots(int connected) => connected < 3 && !Core.Game.TestMode ? 0 : Math.Max(1, ExpectedImpostors(connected));   // test mode: a forced 2-player start still has its 1 impostor (TestMode patch)

        /// <summary>Called from the RoleManager.SelectRoles postfix once every vanilla role has been recorded.</summary>
        public static void DispatchInitialRoles()
        {
            if (!Core.Game.IsHostActive) return;
            // Unregistered (compat) lobby: the per-client role views are GameDataTo messages, which disconnect the host
            // (findings #21/#22). The SelectRoles prefix already keeps this game vanilla; never dispatch here either.
            if (Registration.CompatMode)
            {
                PocketRolesPlugin.Logger.LogWarning("RoleAssignment.DispatchInitialRoles: skipped (compat mode: unregistered lobby, vanilla roles only)");
                return;
            }

            var players = Core.Game.AllPlayers();
            // Test mode (/assign): forced roles first; they are excluded from the random pools below.
            var forced = TestMode.ApplyForcedRoles(players);
            FillImpostors(players, forced, compat: false);
            AssignCustomRoles(players);
            LogAssignment(players);

            byte hostId = HostPlayerId();
            int hostClient = Rpc.HostClientId;

            // Per-client views (v0.4e review of finding #23): the vanilla SelectRoles broadcast has just left with every
            // player's true vanilla role (one packet per player, sent at once), so every client's view is corrected in
            // the SAME frame: all clients' GameDataTo messages are packed into a few packets (MultiBatch, ~900 bytes
            // each → 2–3 packets for 14 clients) and sent right away instead of one paced packet per client (0.1 s
            // each, behind whatever the queue still holds — lobby chat, name tags). A client reads its role for the
            // intro about 2 s later (HudManager.CoShowIntro → IntroCutscene.CoBegin, ShowRole a few seconds after
            // that), so the override is on the wire long before; the desync viewers (Sheriff / Jackal → Impostor for
            // themselves) get their own role in the same message as the rest of the table, after the others.
            var multi = new Rpc.MultiBatch();
            int clients = 0, sends = 0;
            foreach (int clientId in Rpc.AllClientIds(false))
            {
                byte viewer = ViewerIdOf(clientId, players);
                if (viewer == 255)
                {
                    PocketRolesPlugin.Logger.LogWarning($"RoleAssignment: client {clientId} has no player, skipped");
                    continue;
                }
                var batch = multi.For(clientId);
                PlayerControl own = null;
                int impostorsInView = 0;
                foreach (var pc in players)
                {
                    if (pc.Data == null || pc.Data.Disconnected) continue;
                    if (pc.PlayerId == viewer) { own = pc; continue; }
                    RoleTypes view = View(viewer, pc.PlayerId);
                    if (IsImpostorRole(view)) impostorsInView++;
                    batch.SetRole(pc, view, canOverride: false); // first assignment on the client (vanilla flag)
                    sends++;
                }
                if (own != null)
                {
                    RoleTypes ownView = View(viewer, viewer);
                    if (IsImpostorRole(ownView)) impostorsInView++;
                    batch.SetRole(own, ownView, canOverride: false); sends++; // last: its own CoSetRole starts the intro
                }
                // Official server rule seen 2026-09-09 (findings #49/#50): a client whose role table carries NO Impostor-type
                // role gets the host disconnected ("Hacking") as soon as the table is sent (3-player tests where vanilla
                // assigned no impostor; one-client games with 0 impostors were tolerated). Normal games always have one.
                if (impostorsInView == 0 && players.Count >= 3)
                    PocketRolesPlugin.Logger.LogWarning($"RoleAssignment: client {clientId} (#{viewer}) would see NO impostor — the official server may disconnect the host for this table");
                batch.Send();
                clients++;
            }
            // Paced (0.3 s per client), never all at once: two clients' tables in one frame got the host kicked ("Hacking",
            // 2026-09-09 01:29 / 01:36). Each client starts its intro when its own SetRole arrives, so the pacing only delays
            // the intro of the n-th client by 0.3 s × n.
            if (!multi.IsEmpty) multi.Send(urgent: false);
            float sinceSelect = SelectRolesStartedAt >= 0f ? UnityEngine.Time.time - SelectRolesStartedAt : -1f;
            PocketRolesPlugin.Logger.LogInfo($"RoleAssignment: role views sent at once to {clients} client(s) ({sends} SetRole) "
                + (sinceSelect >= 0f ? $"+{sinceSelect * 1000f:0} ms after SelectRoles began" : "(no SelectRoles timestamp)")
                + $", paced queue backlog {Rpc.Queue.Count}");

            // host-local view: only where it differs from the vanilla role already applied by CoSetRole
            if (hostId != 255)
            {
                foreach (var pc in players)
                {
                    if (pc.Data == null || pc.Data.Disconnected) continue;
                    RoleTypes view = View(hostId, pc.PlayerId);
                    if (view != Core.Game.VanillaRoleOf(pc.PlayerId))
                        Rpc.SetRoleTo(pc, view, hostClient);
                }
            }

            Core.Game.InProgress = true;
            // v0.5.5 (review 2026-09-23): remember every player's role text NOW, 8 s before it is really sent, so that
            // a "/cmd n" of those first seconds repeats the text of the role that was handed out instead of falling
            // back to a description built from the role table at call time (a Worshipper convert of that window).
            Chat.Chat.PrecacheRoleInfoAll();
            // Rebuild the task totals with the fresh role table (a recompute before SelectRoles used the previous game's roles).
            try { GameData.Instance?.RecomputeTaskCounts(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"RoleAssignment: RecomputeTaskCounts failed: {e.Message}"); }
            // Options first (private cooldown / vision / speed must be on the client before its intro ends), then the name tags:
            // both go through the paced queue and a 15-player name burst alone takes ~4.5 s (review 2026-09-10).
            OptionsDesync.ResyncAll();
            NameTags.RefreshAll(force: true);
            Scheduler.After(8f, () =>
            {
                if (!Core.Game.InProgress) return;
                Chat.Chat.SendRoleInfoToAll(false);
                OptionsDesync.ResyncAll();
                NameTags.RefreshAll(force: true);
            }, "assign.roleinfo");
        }

        /// <summary>
        /// Compat mode (unregistered lobby) without [Compat] AllowRiskyRoles: Sheriff / Jackal are never assigned
        /// (their kills come from a non-impostor and the server may reject them). Forced test roles are dropped too.
        /// Logs once per game and tells the host by chat which roles were skipped.
        /// </summary>
        private static void ApplyCompatRoleGate(List<PlayerControl> players)
        {
            try
            {
                if (!Registration.CompatMode) return;
                var skipped = new List<string>();
                foreach (var r in Roles.All)
                {
                    if (Roles.IsCompatBlocked(r.Id) && Options.Count(r.Id) > 0) skipped.Add(r.Name);
                }
                // forced (/assign) roles
                var forced = new List<byte>();
                foreach (var kv in Core.Game.Roles)
                {
                    if (Roles.IsCompatBlocked(kv.Value)) forced.Add(kv.Key);
                }
                foreach (byte id in forced)
                {
                    PocketRolesPlugin.Logger.LogWarning($"RoleAssignment: forced role {Core.Game.Roles[id]} of player {id} dropped (compat mode, risky roles off)");
                    Core.Game.Roles.Remove(id);
                }
                if (skipped.Count == 0 && forced.Count == 0) return;
                PocketRolesPlugin.Logger.LogWarning("RoleAssignment: compat mode, risky roles not assigned: " + string.Join(", ", skipped) + " (/opt compat.risky on to allow)");
                Chat.Chat.Local(Chat.Chat.Title, Lang.T("compat.risky.off",
                    "互換モード: シェリフ/ジャッカルは無効（/opt compat.risky on で有効化）",
                    "Compat mode: Sheriff/Jackal are disabled (/opt compat.risky on enables them)",
                    "兼容模式：警长/豺狼已禁用（/opt compat.risky on 可启用）"));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"RoleAssignment.ApplyCompatRoleGate: {e}");
            }
        }

        private static void AssignCustomRoles(List<PlayerControl> players)
        {
            ApplyCompatRoleGate(players);
            var plainCrew = new List<byte>();
            var plainImp = new List<byte>();
            foreach (var pc in players)
            {
                if (pc.Data == null || pc.Data.Disconnected) continue;
                byte id = pc.PlayerId;
                if (Core.Game.Roles.ContainsKey(id)) continue; // already forced (TestMode.ApplyForcedRoles)
                RoleTypes v = Core.Game.VanillaRoleOf(id);
                if (v == RoleTypes.Crewmate) plainCrew.Add(id);
                else if (v == RoleTypes.Impostor) plainImp.Add(id);
            }

            // killers first (they matter most), the rest in random order for fairness
            var order = new List<RoleInfo>();
            var rest = new List<RoleInfo>();
            foreach (var r in Roles.All)
            {
                if (r.Id == CustomRole.Jackal || r.Id == CustomRole.Sheriff || r.Id == CustomRole.Arsonist) order.Add(r);
                else rest.Add(r);
            }
            Shuffle(order);
            Shuffle(rest);
            order.AddRange(rest);

            // Lovers: the only pair role, drawn from both pools before the single-slot roles.
            Lovers.Assign(plainCrew, plainImp, Rand);

            foreach (var role in order)
            {
                if (role.Id == CustomRole.Lovers) continue; // assigned above
                if (Roles.IsCompatBlocked(role.Id)) continue; // compat mode: Sheriff / Jackal off (ApplyCompatRoleGate told the host)
                if (role.Id == CustomRole.Assassin && !Assassin.Enabled)
                {
                    // /cmd guess is a player command: without player commands the role could never act.
                    if (Options.Count(role.Id) > 0)
                    {
                        PocketRolesPlugin.Logger.LogWarning("RoleAssignment: Assassin skipped (player commands disabled)");
                        Chat.Chat.Local(Chat.Chat.Title, Lang.T("assign.assassin.nocmd",
                            "アサシンはプレイヤーのコマンドが有効な時だけ配られます（/opt chat.playercommands on）。",
                            "The Assassin is only assigned while player commands are enabled (/opt chat.playercommands on).",
                            "只有启用玩家命令时才会分配刺客（/opt chat.playercommands on）。"));
                    }
                    continue;
                }
                // v0.5.0: Jackal Friends only in games that have a Jackal. Killers (Jackal) are drawn first (order = killers + rest), so the
                // Jackal slots are already decided here. Forced Friends (/assign) are already in Game.Roles and unaffected.
                if (role.Id == CustomRole.JackalFriends && !JackalFriends.AnyJackal())
                {
                    if (Options.Count(role.Id) > 0)
                    {
                        PocketRolesPlugin.Logger.LogInfo("RoleAssignment: Jackal Friends skipped (no Jackal this game)");
                        if (Options.Count(CustomRole.Jackal) <= 0)
                            Chat.Chat.Local(Chat.Chat.Title, Lang.T("assign.jackalfriends.nojackal",
                                "ジャッカルフレンズはジャッカルが配られた試合だけ配られます（ジャッカルの人数が 0 です: /set jackal 1）。",
                                "Jackal Friends are only assigned in games that have a Jackal (Jackal count is 0: /set jackal 1).",
                                "只有本局分配了豺狼时才会分配跟班（豺狼人数为 0：/set jackal 1）。"));
                    }
                    continue;
                }
                int count = Options.Count(role.Id);
                int chance = Options.Chance(role.Id);
                var pool = role.FromImpostorPool ? plainImp : plainCrew;
                for (int i = 0; i < count; i++)
                {
                    if (pool.Count == 0) break;
                    if (Rand.Next(100) >= chance) continue;
                    int idx = Rand.Next(pool.Count);
                    byte id = pool[idx];
                    pool.RemoveAt(idx);
                    Core.Game.Roles[id] = role.Id;
                }
            }
        }

        private static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Rand.Next(i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        private static void LogAssignment(List<PlayerControl> players)
        {
            var sb = new StringBuilder();
            sb.Append("Role assignment (").Append(players.Count).Append(" players):");
            foreach (var pc in players)
            {
                byte id = pc.PlayerId;
                string name = pc.Data != null ? pc.Data.PlayerName : "?";
                CustomRole custom = Core.Game.RoleOf(id);
                sb.Append("\n  #").Append(id).Append(' ').Append(name)
                  .Append(" (client ").Append(pc.OwnerId).Append(pc.AmOwner ? ", host" : "").Append("): ")
                  .Append(Core.Game.VanillaRoleOf(id));
                if (custom != CustomRole.None)
                    sb.Append(" -> ").Append(Roles.Info(custom).NameEn).Append(" [").Append(Roles.Info(custom).Team).Append(']');
                if (pc.Data != null && pc.Data.Disconnected) sb.Append(" (disconnected)");
            }
            if (Core.Game.LoverA != 255)
                sb.Append("\n  Lovers: #").Append(Core.Game.LoverA).Append(' ').Append(Core.Game.NameOf(Core.Game.LoverA))
                  .Append(" & #").Append(Core.Game.LoverB).Append(' ').Append(Core.Game.NameOf(Core.Game.LoverB));
            if (JackalFriends.Any() && !JackalFriends.AnyJackal())
                sb.Append("\n  Jackal Friends without a Jackal (forced by /assign)");
            PocketRolesPlugin.Logger.LogInfo(sb.ToString());
        }

        // ------------------------------------------------------------------ ghosts

        /// <summary>
        /// Sends the ghost role of a dead player: every client gets exactly ONE SetRole, the ghost that matches the role
        /// it already holds for that player (View: a desync player's own client → ImpostorGhost, desync viewers →
        /// CrewmateGhost, everyone else by team), packed into one urgent send; the host applies its own view locally
        /// (the vanilla local CoSetRole was suppressed together with the broadcast). No common broadcast first: a
        /// CrewmateGhost reaching an Impostor client (the dead desync player itself) left a black screen, and the
        /// "override 0.2 s later" was ignored by 2026.8.18 clients (2-player test 2026-09-08 23:45).
        /// </summary>
        public static void SendGhostRole(PlayerControl dead)
        {
            if (!Core.Game.IsHostActive || dead == null || dead.Data == null) return;
            byte deadId = dead.PlayerId;
            try
            {
                var players = Core.Game.AllPlayers();
                var multi = new Rpc.MultiBatch();
                int sends = 0;
                foreach (int clientId in Rpc.AllClientIds(false))
                {
                    byte viewer = ViewerIdOf(clientId, players);
                    if (viewer == 255) continue;
                    var batch = multi.For(clientId);
                    batch.SetRole(dead, View(viewer, deadId));
                    batch.Send();
                    sends++;
                }
                if (!multi.IsEmpty) multi.Send(urgent: false); // paced: N ghost messages at once would trip the server rate limit
                byte hostId = HostPlayerId();
                if (hostId != 255) Rpc.SetRoleTo(dead, View(hostId, deadId), Rpc.HostClientId);
                PocketRolesPlugin.Logger.LogInfo($"RoleAssignment: ghost role of #{deadId} {Core.Game.NameOf(deadId)} sent per viewer ({sends} client(s); own view {View(deadId, deadId)})");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"RoleAssignment.SendGhostRole: {e}");
            }
        }

        /// <summary>Shared cleanup for game end / join / disconnect: one reset for every module (Game.ResetForNewLobby).</summary>
        internal static void Cleanup(string reason)
        {
            try
            {
                Core.Game.ResetForNewLobby(reason);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"RoleAssignment.Cleanup({reason}): {e}");
            }
        }
    }

    // ---------------------------------------------------------------------- patches

    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    internal static class Assign_SelectRolesPatch
    {
        private static void Prefix()
        {
            try
            {
                if (!Core.Game.IsHostActive) return;
                RoleAssignment.SelectRolesStartedAt = UnityEngine.Time.time;
                Rpc.ResetCompatWarnings(); // compat-mode "send skipped" warnings: once per game
                Core.Game.Reset();
                // Defensive: the end check flag is cleared by WinConditions.EndGame / Haison.EndNow on the previous
                // GameManager; the new one starts with it set (it is respawned per game, but never rely on it).
                try
                {
                    var gm = GameManager.Instance;
                    if (gm != null && !gm.ShouldCheckForGameEnd)
                    {
                        gm.ShouldCheckForGameEnd = true;
                        PocketRolesPlugin.Logger.LogInfo("Assign: ShouldCheckForGameEnd was false at SelectRoles, re-enabled");
                    }
                }
                catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"Assign: ShouldCheckForGameEnd check failed: {e.Message}"); }
                // 廃村 (Lobby.Haison): a throwaway game that ends right after the intro — vanilla roles only, no dispatch.
                if (Core.Game.HaisonActive)
                {
                    PocketRolesPlugin.Logger.LogInfo("Assign: haison game, custom roles skipped");
                    return;
                }
                if (!Registration.CompatMode) OptionsDesync.Capture();   // before HostWish may bump a wished role's rate: the per-client option copies must not carry it
                RoleAssignment.BeginVanillaSelection();   // v0.5.1: watch vanilla's own RpcSetRole calls (impostor-pass Crewmate defaults)
                // Unregistered lobby (compat / 便利ホスト, findings #21/#22): the server disconnects the host for any
                // client-addressed message (GameDataTo) — which is exactly what the per-client role views are. Vanilla
                // roles only and nothing dispatched; the game runs like a haison game (the mod stays out of it).
                if (Registration.CompatMode)
                {
                    PocketRolesPlugin.Logger.LogInfo("Assign: unregistered lobby (compat mode) — vanilla roles only, no custom roles and no per-client role views");
                    Chat.Chat.Local(Chat.Chat.Title, Lang.T("compat.roles.off",
                        "登録オフ（便利ホスト）の部屋なので MODの追加役職なしで進行します（本来の役職は部屋の設定どおり）。追加役職は MOD 登録ありの部屋で。",
                        "Unregistered (便利ホスト) lobby: this game runs without mod roles (usual roles as set); mod roles need a registered lobby (mod-lobby registration on).",
                        "未注册（简易房）的房间：本局没有模组追加职业（原版职业按房间设置）；模组职业需要已注册（开启 MOD 房间注册）的房间。"));
                    return;
                }
                Core.Game.AssigningRoles = true;
                foreach (var pc in Core.Game.AllPlayers())
                {
                    if (pc.Data == null) continue;
                    Core.Game.OriginalNames[pc.PlayerId] = pc.Data.PlayerName ?? string.Empty;
                }
                // [Roles] VanillaRoles = false (default): the vanilla special roles (Scientist, Engineer, Judge, ...) are
                // not handed out in a role game — only Crewmates and Impostors, from which the custom roles are drawn.
                // Done by zeroing the host's role rates for this SelectRoles call only (restored in the postfix; the
                // change is local to the host and never synced, the clients only ever see the resulting RpcSetRole).
                if (!Options.VanillaRolesEnabled) SuppressVanillaRoles();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Assign_SelectRoles prefix: {e}");
            }
        }

        private static void Postfix()
        {
            try
            {
                RestoreVanillaRoles();                  // inner save/restore pair first (SuppressVanillaRoles saved the rates AFTER HostWish's bump)
                RoleAssignment.EndVanillaSelection();   // then HostWish.OnEnd restores the rate the host really had
                if (!Core.Game.IsHostActive)
                {
                    Core.Game.AssigningRoles = false;
                    return;
                }
                // v0.5.0: everyone who starts this game is in the post-game summary, even after leaving mid-game.
                Core.Game.SnapshotRoster();
                if (Core.Game.HaisonActive || Registration.CompatMode)
                {
                    // v0.4.4: the plain-role gap described below hits vanilla-only games as well — an unregistered
                    // (compat) lobby with a plain-Crewmate host froze before the intro (2026-09-09, 15 players, the
                    // host never got a role → no intro → "black screen", then a Hacking disconnect after the forced
                    // end). AssigningRoles is false here, so every RpcSetRole is the plain vanilla broadcast
                    // (same shape as the special roles the vanilla SelectRoles just sent); no custom roles, no views.
                    Core.Game.AssigningRoles = false;
                    if (!Core.Game.HaisonActive) RoleAssignment.FillImpostors(Core.Game.AllPlayers(), null, compat: true);   // v0.5.1: top the impostor team up before the plain roles go out
                    AssignPlainRoles();
                    return;
                }
                // 2026.8.18 (options V11): SelectRoles hands out only the special roles — the crew pass runs with
                // default=none and a plain Crewmate never receives RpcSetRole, so its roleAssigned stays false and,
                // for the host, the intro (started from its own CoSetRole) never comes (black screen, 2026-09-08).
                // Give every still-unassigned player its plain role here while the passthrough is active: the vanilla
                // RpcSetRole bookkeeping runs exactly as for a special role and the per-client views follow below.
                AssignPlainRoles();
                DowngradeImpostorSpecials();
                Core.Game.AssigningRoles = false;
                RoleAssignment.DispatchInitialRoles();
            }
            catch (Exception e)
            {
                Core.Game.AssigningRoles = false;
                PocketRolesPlugin.Logger.LogError($"Assign_SelectRoles postfix: {e}");
            }
        }

        /// <summary>
        /// Vanilla CREW special roles whose rates are zeroed while [Roles] VanillaRoles is off (ghost roles excluded).
        /// The impostor specials are NOT zeroed any more (2026-09-09, finding #51): V11 computes the plain-Impostor fill
        /// from the saved special counts, so zeroing Shapeshifter / Phantom / Viper at SelectRoles time left the impostor
        /// pass with an empty list and its Crewmate default — a game without any impostor (and the official server then
        /// disconnected the host for the impostor-less role tables). They are assigned by vanilla and downgraded to plain
        /// Impostor in <see cref="DowngradeImpostorSpecials"/> instead.
        /// </summary>
        private static readonly RoleTypes[] VanillaSpecialRoles =
        {
            RoleTypes.Scientist, RoleTypes.Engineer, RoleTypes.Noisemaker, RoleTypes.Tracker, RoleTypes.Detective, RoleTypes.Judge,
        };
        private static readonly RoleTypes[] VanillaImpostorSpecials = { RoleTypes.Shapeshifter, RoleTypes.Phantom, RoleTypes.Viper };

        /// <summary>
        /// [Roles] VanillaRoles off: a vanilla Shapeshifter / Phantom / Viper becomes a plain Impostor — in the host's
        /// role table (so every view and the ghost role say Impostor) and on the host's own client. The clients receive
        /// Impostor as their first assignment from DispatchInitialRoles, so the special abilities never exist.
        /// </summary>
        private static void DowngradeImpostorSpecials()
        {
            try
            {
                if (Options.VanillaRolesEnabled) return;
                int n = 0;
                foreach (var pc in Core.Game.AllPlayers())
                {
                    if (pc == null || pc.Data == null || pc.Data.Disconnected) continue;
                    byte id = pc.PlayerId;
                    RoleTypes v = Core.Game.VanillaRoleOf(id);
                    if (Array.IndexOf(VanillaImpostorSpecials, v) < 0) continue;
                    Core.Game.VanillaRoles[id] = RoleTypes.Impostor;
                    Rpc.ApplyRoleLocal(pc, RoleTypes.Impostor);
                    n++;
                    PocketRolesPlugin.Logger.LogInfo($"Assign: vanilla {v} on #{id} {Core.Game.NameOf(id)} downgraded to plain Impostor ([Roles] VanillaRoles = off)");
                }
                if (n > 0) PocketRolesPlugin.Logger.LogInfo($"Assign: {n} vanilla impostor special(s) downgraded to plain Impostor");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Assign.DowngradeImpostorSpecials: {e}");
            }
        }
        private static readonly Dictionary<RoleTypes, (int count, int chance)> _savedRates = new Dictionary<RoleTypes, (int, int)>();

        /// <summary>
        /// Sends the plain role (Crewmate, or Impostor when the local table already says so) to every player the vanilla
        /// selection left without RpcSetRole (roleAssigned == false). Called before AssigningRoles is cleared so the
        /// RpcSetRole prefix records the vanilla role and lets the vanilla code run.
        /// </summary>
        private static void AssignPlainRoles()
        {
            try
            {
                int sent = 0;
                var missing = new StringBuilder();
                foreach (var pc in Core.Game.AllPlayers())
                {
                    if (pc == null || pc.Data == null || pc.Data.Disconnected) continue;
                    if (pc.roleAssigned) continue;
                    RoleTypes current = pc.Data.Role != null ? pc.Data.Role.Role : RoleTypes.Crewmate;
                    RoleTypes plain = RoleAssignment.IsImpostorRole(current) ? RoleTypes.Impostor : RoleTypes.Crewmate;
                    pc.RpcSetRole(plain, false);
                    sent++;
                    missing.Append(" #").Append(pc.PlayerId).Append('=').Append(plain);
                }
                if (sent > 0)
                    PocketRolesPlugin.Logger.LogInfo($"Assign: plain role sent to {sent} player(s) the vanilla selection left unassigned:{missing}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Assign.AssignPlainRoles: {e}");
            }
        }

        private static void SuppressVanillaRoles()
        {
            _savedRates.Clear();
            try
            {
                var ro = GameOptionsManager.Instance?.CurrentGameOptions?.RoleOptions;
                if (ro == null) return;
                int zeroed = 0;
                foreach (var r in VanillaSpecialRoles)
                {
                    try
                    {
                        int n = ro.GetNumPerGame(r), c = ro.GetChancePerGame(r);
                        if (n <= 0 && c <= 0) continue;
                        _savedRates[r] = (n, c);
                        ro.SetRoleRate(r, 0, 0);
                        zeroed++;
                    }
                    catch (Exception) { }
                }
                if (zeroed > 0) PocketRolesPlugin.Logger.LogInfo($"Assign: vanilla special roles suppressed for this game ({zeroed} role rate(s) zeroed; [Roles] VanillaRoles = off)");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Assign: vanilla role suppression failed: {e.Message}");
            }
        }

        private static void RestoreVanillaRoles()
        {
            if (_savedRates.Count == 0) return;
            try
            {
                var ro = GameOptionsManager.Instance?.CurrentGameOptions?.RoleOptions;
                if (ro != null)
                    foreach (var kv in _savedRates)
                    {
                        try { ro.SetRoleRate(kv.Key, kv.Value.count, kv.Value.chance); } catch (Exception) { }
                    }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Assign: vanilla role restore failed: {e.Message}");
            }
            _savedRates.Clear();
        }
    }

    /// <summary>
    /// Swallows the vanilla role broadcasts: during SelectRoles we record the true role and apply it locally; in game we
    /// replace ghost / GuardianAngel assignments with our own per-viewer sends.
    /// </summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSetRole))]
    [HarmonyPriority(Priority.High)]
    internal static class Assign_RpcSetRolePatch
    {
        private static bool Prefix(PlayerControl __instance, ref RoleTypes roleType, bool canOverrideRole)
        {
            try
            {
                if (!Core.Game.IsHostActive || __instance == null) return true;
                byte id = __instance.PlayerId;
                // v0.5.1: vanilla's impostor pass hands its leftover picks a Crewmate (2026.8.18) — they stay impostors
                if (!HostWish.Redirecting && RoleAssignment.TakeImpostorDefault(__instance, roleType)) roleType = RoleTypes.Impostor;
                // v0.5.1: "next game I am …" — swap recipients inside vanilla's own SelectRoles (HostWish)
                if (RoleAssignment.VanillaSelecting && HostWish.Intercept(__instance, roleType, canOverrideRole)) return false;
                // v0.5.2: "next game, <player> is …" — the designated players (Designate), after the host's own wish
                if (RoleAssignment.VanillaSelecting && Designate.Intercept(__instance, roleType, canOverrideRole)) return false;
                // v0.5.5: unregistered lobby, 3 players — keep a player without a role for the impostor top-up
                if (RoleAssignment.WithholdForImpostorFill(__instance, roleType)) return false;

                if (Core.Game.AssigningRoles)
                {
                    // Record the vanilla role, run the host-local part of RpcSetRole (CoSetRole: roleAssigned, the host's
                    // own intro trigger) and drop the broadcast. 2026.8.18 clients apply only the FIRST SetRole they
                    // receive for a player — a later one, canOverride or not, is ignored (phone test 2026-09-08 23:30:
                    // the Arsonist view sent right after SelectRoles never applied; the host showed the same with a
                    // local CoSetRole). So the vanilla roles must never reach the clients: DispatchInitialRoles sends
                    // each client its own per-viewer table as the first (and only) assignment. The host-side bookkeeping
                    // stays complete because every RpcSetRole of the start (specials, plain roles) still runs CoSetRole
                    // here — the black screens of the earlier hijack came from plain players without any RpcSetRole
                    // (finding #39), which AssignPlainRoles now covers.
                    Core.Game.VanillaRoles[id] = roleType;
                    try { __instance.StartCoroutine(__instance.CoSetRole(roleType, canOverrideRole)); }
                    catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Assign_RpcSetRole: local CoSetRole #{id} {roleType}: {e}"); }
                    return false;
                }

                // EndGame already broadcast the Victory/Defeat ghost roles; a vanilla death in the 0.4 s window before
                // RpcEndGame must not overwrite them.
                if (Core.Game.Ending) return false;

                if (Core.Game.InProgress)
                {
                    if (roleType == RoleTypes.CrewmateGhost || roleType == RoleTypes.ImpostorGhost)
                    {
                        RoleAssignment.SendGhostRole(__instance);
                        return false;
                    }
                    if (roleType == RoleTypes.GuardianAngel
                        && (Core.Game.RoleOf(id) != CustomRole.None || Core.Game.IsImpostorTeamKiller(id)))
                    {
                        // Custom-role players never become Guardian Angels, and vanilla decides GA from the host's LOCAL
                        // role table, which shows real impostors as Crewmate when the host is a desync viewer: redirect
                        // both to our own ghost role (View() yields ImpostorGhost / CrewmateGhost per viewer).
                        RoleAssignment.SendGhostRole(__instance);
                        return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Assign_RpcSetRole prefix: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
    internal static class Assign_IntroCutsceneOnDestroyPatch
    {
        private static void Postfix()
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                Scheduler.After(1f, () =>
                {
                    if (!Core.Game.InProgress) return;
                    NameTags.RefreshAll(force: true);
                    OptionsDesync.ResyncAll();
                    SerialKiller.OnIntroEnd();   // v0.5.0: schedules the countdown start after the LAST client's intro (paced dispatch)
                    Scheduler.After(0.5f, () => Kills.ApplyHostCustomCooldown("intro end"), "hostcd.intro");   // after vanilla's own intro-end timer
                }, "assign.introend");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Assign_IntroCutsceneOnDestroy: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    internal static class Assign_OnGameEndPatch
    {
        private static void Postfix()
        {
            RoleAssignment.Cleanup("OnGameEnd");
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class Assign_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            RoleAssignment.Cleanup("OnGameJoined");
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    internal static class Assign_OnDisconnectedPatch
    {
        private static void Postfix()
        {
            RoleAssignment.Cleanup("OnDisconnected");
        }
    }
}
