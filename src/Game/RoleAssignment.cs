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
            TestMode.ApplyForcedRoles(players);
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
                foreach (var pc in players)
                {
                    if (pc.Data == null || pc.Data.Disconnected) continue;
                    if (pc.PlayerId == viewer) { own = pc; continue; }
                    batch.SetRole(pc, View(viewer, pc.PlayerId));
                    sends++;
                }
                if (own != null) { batch.SetRole(own, View(viewer, viewer)); sends++; }
                batch.Send();
                clients++;
            }
            if (!multi.IsEmpty) multi.Send(urgent: true);
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
            // Rebuild the task totals with the fresh role table (a recompute before SelectRoles used the previous game's roles).
            try { GameData.Instance?.RecomputeTaskCounts(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"RoleAssignment: RecomputeTaskCounts failed: {e.Message}"); }
            NameTags.RefreshAll(force: true);
            OptionsDesync.ResyncAll();
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
                if (!Registration.CompatMode || Options.AllowRiskyRoles) return;
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
            PocketRolesPlugin.Logger.LogInfo(sb.ToString());
        }

        // ------------------------------------------------------------------ ghosts

        /// <summary>
        /// Sends the ghost role of a dead player: the common view is broadcast at once (the dead player's own client
        /// needs it to float), viewers whose view differs (desync impostors) get a targeted override, and the host applies
        /// its own view locally (the vanilla local CoSetRole was suppressed together with the broadcast).
        /// </summary>
        public static void SendGhostRole(PlayerControl dead)
        {
            if (!Core.Game.IsHostActive || dead == null || dead.Data == null) return;
            byte deadId = dead.PlayerId;
            RoleTypes common = Core.Game.IsImpostorTeamKiller(deadId) ? RoleTypes.ImpostorGhost : RoleTypes.CrewmateGhost;

            var all = new Rpc.Batch(-1);
            all.SetRole(dead, common);
            all.Send(urgent: true);

            byte hostId = HostPlayerId();
            if (hostId != 255) Rpc.SetRoleTo(dead, View(hostId, deadId), Rpc.HostClientId);

            // Per-viewer overrides (desync viewers) go out 0.2 s later: two CoSetRole coroutines started in the same
            // frame for the same player are not guaranteed to finish in send order (EHR spaces them the same way).
            Scheduler.After(0.2f, () =>
            {
                try
                {
                    if (!Core.Game.IsHostActive || dead == null) return;
                    var players = Core.Game.AllPlayers();
                    foreach (int clientId in Rpc.AllClientIds(false))
                    {
                        byte viewer = ViewerIdOf(clientId, players);
                        if (viewer == 255) continue;
                        RoleTypes view = View(viewer, deadId);
                        if (view != common) Rpc.SetRoleTo(dead, view, clientId);
                    }
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"RoleAssignment.SendGhostRole overrides: {e}");
                }
            }, "assign.ghost." + deadId);
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
                // Unregistered lobby (compat / 便利ホスト, findings #21/#22): the server disconnects the host for any
                // client-addressed message (GameDataTo) — which is exactly what the per-client role views are. Vanilla
                // roles only and nothing dispatched; the game runs like a haison game (the mod stays out of it).
                if (Registration.CompatMode)
                {
                    PocketRolesPlugin.Logger.LogInfo("Assign: unregistered lobby (compat mode) — vanilla roles only, no custom roles and no per-client role views");
                    Chat.Chat.Local(Chat.Chat.Title, Lang.T("compat.roles.off",
                        "登録オフ（便利ホスト）の部屋なので役職なしのバニラで進行します。役職ありは MOD 登録ありの部屋で。",
                        "Unregistered (便利ホスト) lobby: this game runs vanilla without custom roles; roles need a registered lobby (mod-lobby registration on).",
                        "未注册（便利房）的房间：本局按原版进行，没有自定义职业；职业需要已注册（开启 MOD 房间注册）的房间。"));
                    return;
                }
                Core.Game.AssigningRoles = true;
                OptionsDesync.Capture();
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
                RestoreVanillaRoles();
                if (!Core.Game.IsHostActive || Core.Game.HaisonActive || Registration.CompatMode)
                {
                    Core.Game.AssigningRoles = false;
                    return;
                }
                // 2026.8.18 (options V11): SelectRoles hands out only the special roles — the crew pass runs with
                // default=none and a plain Crewmate never receives RpcSetRole, so its roleAssigned stays false and,
                // for the host, the intro (started from its own CoSetRole) never comes (black screen, 2026-09-08).
                // Give every still-unassigned player its plain role here while the passthrough is active: the vanilla
                // RpcSetRole bookkeeping runs exactly as for a special role and the per-client views follow below.
                AssignPlainRoles();
                Core.Game.AssigningRoles = false;
                RoleAssignment.DispatchInitialRoles();
            }
            catch (Exception e)
            {
                Core.Game.AssigningRoles = false;
                PocketRolesPlugin.Logger.LogError($"Assign_SelectRoles postfix: {e}");
            }
        }

        /// <summary>Vanilla special roles whose rates are zeroed while [Roles] VanillaRoles is off (ghost roles excluded).</summary>
        private static readonly RoleTypes[] VanillaSpecialRoles =
        {
            RoleTypes.Scientist, RoleTypes.Engineer, RoleTypes.Shapeshifter, RoleTypes.Noisemaker, RoleTypes.Phantom,
            RoleTypes.Tracker, RoleTypes.Detective, RoleTypes.Viper, RoleTypes.Judge,
        };
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
        private static bool Prefix(PlayerControl __instance, RoleTypes roleType, bool canOverrideRole)
        {
            try
            {
                if (!Core.Game.IsHostActive || __instance == null) return true;
                byte id = __instance.PlayerId;

                if (Core.Game.AssigningRoles)
                {
                    // Record the vanilla role and let the vanilla RpcSetRole run for EVERY player (2026-09-08): the
                    // vanilla start flow only reaches HudManager.CoShowIntro when its own RpcSetRole bookkeeping ran
                    // for all players (swallowing them — even with roleAssigned raised and the host's role re-sent —
                    // left the screen black with 2+ players, while the 廃村 throwaway game, which lets vanilla run,
                    // always showed the intro). The broadcast carries only vanilla roles, exactly like a vanilla game;
                    // DispatchInitialRoles overrides every client's view right after SelectRoles.
                    Core.Game.VanillaRoles[id] = roleType;
                    return true;
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
