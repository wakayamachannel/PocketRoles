using System;
using AmongUs.GameOptions;
using PocketRoles.Core;

namespace PocketRoles.Game
{
    /// <summary>
    /// [Roles] RevealRoleOnDeath (v0.4.4, off by default): when a player is killed or ejected, everyone reads
    /// "X was ROLE" — the PocketRoles role in a role game, otherwise the vanilla role's own localized name. Works in
    /// unregistered (compat) lobbies too, where it is one public host message per death (vanilla roles only there).
    /// Hooks: Kills.OnMurder (any successful MurderPlayer) and Meetings_ExileWrapUpPatch (after the eject screen).
    /// </summary>
    public static class RoleReveal
    {
        /// <summary>
        /// Role each player held while alive, recorded from RoleManager.SetRole on the host (vanilla specials and the
        /// mod's plain roles alike; ghost roles are skipped). The live NetworkedPlayerInfo already shows the ghost role
        /// at the exile screen, and RoleWhenAlive is not reliable for the host's own player (2026-09-09 live test).
        /// </summary>
        internal static readonly System.Collections.Generic.Dictionary<byte, RoleTypes> AliveRoles = new System.Collections.Generic.Dictionary<byte, RoleTypes>();

        internal static bool IsGhostRole(RoleTypes t) => t == RoleTypes.CrewmateGhost || t == RoleTypes.ImpostorGhost || t == RoleTypes.GuardianAngel;

        internal static void Record(PlayerControl player, RoleTypes roleType)
        {
            try
            {
                if (player == null || IsGhostRole(roleType)) return;
                AliveRoles[player.PlayerId] = roleType;
            }
            catch (Exception) { }
        }

        private static bool Active()
        {
            if (!Options.RevealRoleOnDeath) return false;
            var client = AmongUsClient.Instance;
            return client != null && client.AmHost && client.IsGameStarted;
        }

        /// <summary>Deaths inside a meeting (Assassin guess): announced 2 s after the exile screen, like the ejected player (a dead host must not open a revive window in the meeting).</summary>
        private static readonly System.Collections.Generic.List<byte> Deferred = new System.Collections.Generic.List<byte>();

        /// <summary>New game (RoleManager.SelectRoles): a line deferred in a game that ended inside its meeting must not leak.</summary>
        internal static void ClearDeferred() { Deferred.Clear(); DeferredLeft.Clear(); LeftAnnounced.Clear(); _exileEndAt = -100f; }

        internal static void DeferUntilExileEnd(byte id)
        {
            try { if (Active() && !Deferred.Contains(id)) Deferred.Add(id); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"RoleReveal.DeferUntilExileEnd: {e}"); }
        }

        internal static void OnKilled(byte id)
        {
            try
            {
                if (!Active()) return;
                Announce(id, "reveal.killed", "{0} は {1} でした。", "{0} was {1}.", "{0} 是 {1}。");
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"RoleReveal.OnKilled: {e}"); }
        }

        /// <summary>ExileController.WrapUp postfix: the ejected player (none on a skip / tie).</summary>
        internal static void OnExiled(ExileController exile)
        {
            try
            {
                _exileEndAt = UnityEngine.Time.time;   // v0.5.2: RevealLeaveToAll holds its lines 2.6 s past the exile screen (dead-host chat race)
                if (exile == null) return;
                if (!Active())
                {
                    // [Roles] RevealRoleOnDeath off: only the RevealLeaveToAll lines deferred during the meeting (same 2 s)
                    Deferred.Clear();
                    var c = AmongUsClient.Instance;
                    if (DeferredLeft.Count > 0 && c != null && c.AmHost && c.IsGameStarted) FlushDeferred(2f);
                    return;
                }
                NetworkedPlayerInfo info = null;
                try { info = exile.initData != null ? exile.initData.networkedPlayer : null; } catch (Exception) { }
                if (info == null) { FlushDeferred(2f); return; }   // skip / tie: only the in-meeting deaths to announce
                byte id = info.PlayerId;
                // 2 s after the exile screen, not inside it: chat from a dead host opens Rpc.TempReviveHostForChat (an
                // urgent Data(IsDead=false)) while clients still run their own exile end check → black screen.
                Scheduler.After(2f, () => Announce(id, "reveal.exiled", "追放された {0} は {1} でした。", "Ejected {0} was {1}.", "被驱逐的 {0} 是 {1}。"), "reveal.exiled");
                FlushDeferred(2.6f);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"RoleReveal.OnExiled: {e}"); }
        }

        /// <summary>The in-meeting deaths, one line each, spaced behind the ejected line (Chat.All paces per client anyway).</summary>
        private static void FlushDeferred(float delay)
        {
            int n = 0;
            if (Deferred.Count > 0)
            {
                var list = new System.Collections.Generic.List<byte>(Deferred);
                Deferred.Clear();
                for (int i = 0; i < list.Count; i++)
                {
                    byte id = list[i];
                    Scheduler.After(delay + 0.6f * i, () => Announce(id, "reveal.killed", "{0} は {1} でした。", "{0} was {1}.", "{0} 是 {1}。"), "reveal.exiled");
                }
                n = list.Count;
            }
            // v0.5.2: players who left during the meeting (RevealLeaveToAll), after the deaths
            if (DeferredLeft.Count > 0)
            {
                var left = new System.Collections.Generic.List<byte>(DeferredLeft);
                DeferredLeft.Clear();
                for (int i = 0; i < left.Count; i++)
                {
                    byte id = left[i];
                    Scheduler.After(delay + 0.6f * (n + i), () => { if (LeftText(id, out var t)) Chat.Chat.All(Chat.Chat.Title, t); }, "reveal.exiled");
                }
            }
        }

        // ------------------------------------------------------------------ v0.5.2: a player leaving mid-game

        /// <summary>Roster players already announced as left (cleared at SelectRoles).</summary>
        internal static readonly System.Collections.Generic.HashSet<byte> LeftAnnounced = new System.Collections.Generic.HashSet<byte>();
        private static readonly System.Collections.Generic.List<byte> DeferredLeft = new System.Collections.Generic.List<byte>();
        private static float _exileEndAt = -100f;

        private static bool LeftText(byte id, out string text)
        {
            text = null;
            string name = Core.Game.Roster.TryGetValue(id, out var e) ? e.Name : Core.Game.NameOf(id);
            name = Lang.StripTags(name ?? "").Trim();
            if (name.Length == 0) name = "#" + id;
            string role = RoleNameOf(id);
            if (string.IsNullOrEmpty(role)) return false;
            try { text = string.Format(Lang.T("reveal.left", "抜けた {0} は {1} でした。", "{0} left; they were {1}.", "退出的 {0} 是 {1}。"), name, role); }
            catch (FormatException) { text = name + ": " + role; }
            return true;
        }

        /// <summary>
        /// AmongUsClient.OnPlayerLeft postfix (host, game running): every roster player whose data is gone or flagged
        /// Disconnected gets one line — on the host's own screen ([Roles] RevealRoleOnLeave, default on; Chat.Local
        /// sends nothing, so an unregistered lobby is fine) and, with [Roles] RevealLeaveToAll, to everyone (deferred
        /// past the exile screen when it happens inside a meeting, like the in-meeting deaths). The roles come from
        /// the mod's own tables (RoleNameOf), so they survive the player's data being destroyed.
        /// </summary>
        internal static void OnPlayerLeft()
        {
            try
            {
                if (!Options.RevealRoleOnLeave && !Options.RevealLeaveToAll) return;
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || !client.IsGameStarted) return;
                if (Core.Game.Roster.Count == 0) return;
                foreach (var e in Core.Game.Roster.Values)
                {
                    if (LeftAnnounced.Contains(e.Id)) continue;
                    var info = Core.Game.Info(e.Id);
                    if (info != null && !info.Disconnected) continue;
                    LeftAnnounced.Add(e.Id);
                    if (!LeftText(e.Id, out var text)) continue;
                    PocketRolesPlugin.Logger.LogInfo($"RoleReveal: {Lang.StripTags(e.Name ?? "")} → left (reveal.left)");
                    if (Options.RevealLeaveToAll)
                    {
                        // Chat.All shows the host its own copy. Inside a meeting / exile screen the line waits for FlushDeferred; for
                        // 2.6 s after the exile screen it is scheduled (a dead host's chat reopens the revive race otherwise).
                        bool meeting = false;
                        try { meeting = MeetingHud.Instance != null || ExileController.Instance != null; } catch (Exception) { }
                        byte id = e.Id;
                        float hold = _exileEndAt + 2.6f - UnityEngine.Time.time;
                        if (meeting) DeferredLeft.Add(id);
                        else if (hold > 0f) Scheduler.After(hold, () => { if (LeftText(id, out var t)) Chat.Chat.All(Chat.Chat.Title, t); }, "reveal.exiled");
                        else Chat.Chat.All(Chat.Chat.Title, text);
                    }
                    else if (Options.RevealRoleOnLeave) Chat.Chat.Local(Chat.Chat.Title, text);
                }
            }
            catch (Exception ex) { PocketRolesPlugin.Logger.LogError($"RoleReveal.OnPlayerLeft: {ex}"); }
        }

        private static void Announce(byte id, string key, string ja, string en, string zh)
        {
            string name = Lang.StripTags(Core.Game.NameOf(id) ?? "").Trim();
            if (name.Length == 0) name = "#" + id;
            string role = RoleNameOf(id);
            if (string.IsNullOrEmpty(role)) return;
            PocketRolesPlugin.Logger.LogInfo($"RoleReveal: {name} → {role} ({key})");
            // Lang.T (3 texts) + Format: Lang.TF has no zh overload (its 4th argument is the format args).
            string text;
            try { text = string.Format(Lang.T(key, ja, en, zh), name, role); } catch (FormatException) { text = name + ": " + role; }
            if (Options.RevealRoleToAll) Chat.Chat.All(Chat.Chat.Title, text);
            else Chat.Chat.Local(Chat.Chat.Title, text);   // v0.5.2: [Roles] RevealRoleToAll = false — the host's own screen only (no packet)
        }

        /// <summary>The PocketRoles role name, else the vanilla role's name in the lobby language (Crewmate / Impostor / Judge …).</summary>
        internal static string RoleNameOf(byte id)
        {
            var custom = Core.Game.RoleOf(id);
            if (custom != CustomRole.None) return Roles.Info(custom).Name;
            var type = Core.Game.VanillaRoleOf(id);
            // After the death the live role is already the ghost role (seen at the exile screen): prefer the role
            // recorded at assignment, then the vanilla RoleWhenAlive, then the live role. In a role game the mod's own
            // table is the truth: the recorder also sees the host's per-viewer applications (a Sheriff host holds a
            // Crewmate view of every impostor), which must not become "X was Crewmate".
            if (Core.Game.InProgress && Core.Game.VanillaRoles.ContainsKey(id)) { /* keep VanillaRoleOf */ }
            else if (AliveRoles.TryGetValue(id, out var recorded)) type = recorded;
            else
            {
                try
                {
                    var pc = Core.Game.Player(id);
                    var info = pc != null ? pc.Data : null;
                    var alive = info != null ? info.RoleWhenAlive : null;
                    if (alive != null && alive.HasValue) type = alive.Value;
                }
                catch (Exception) { }
            }
            string own = VanillaRoleName(type);
            if (own != null) return own;
            // Unknown role type: the game's own NiceName, unless it is the "STRMISS" placeholder (seen for Viper on 2026.8.18)
            try
            {
                var rm = RoleManager.Instance;
                var rb = rm != null ? rm.GetRole(type) : null;
                if (rb != null && !string.IsNullOrEmpty(rb.NiceName) && !rb.NiceName.Contains("STRMISS")) return rb.NiceName;
            }
            catch (Exception) { }
            return type.ToString();
        }

        /// <summary>Vanilla 2026.8.18 role names (ja / en / zh-CN); null for a type not in the table.</summary>
        private static string VanillaRoleName(RoleTypes type)
        {
            switch (type)
            {
                // ghost roles (CrewmateGhost / ImpostorGhost / Guardian Angel) are only ever assigned after a death: name the living side
                case RoleTypes.Crewmate: case RoleTypes.CrewmateGhost: case RoleTypes.GuardianAngel: return Lang.T("vanrole.crewmate", "クルー", "Crewmate", "船员");
                case RoleTypes.Impostor: case RoleTypes.ImpostorGhost: return Lang.T("vanrole.impostor", "インポスター", "Impostor", "伪装者");
                case RoleTypes.Scientist: return Lang.T("vanrole.scientist", "科学者", "Scientist", "科学家");
                case RoleTypes.Engineer: return Lang.T("vanrole.engineer", "エンジニア", "Engineer", "工程师");
                case RoleTypes.Shapeshifter: return Lang.T("vanrole.shapeshifter", "シェイプシフター", "Shapeshifter", "变形者");
                case RoleTypes.Noisemaker: return Lang.T("vanrole.noisemaker", "ノイズメーカー", "Noisemaker", "大嗓门");
                case RoleTypes.Phantom: return Lang.T("vanrole.phantom", "ファントム", "Phantom", "幻象师");
                case RoleTypes.Tracker: return Lang.T("vanrole.tracker", "トラッカー", "Tracker", "侦察员");
                case RoleTypes.Detective: return Lang.T("vanrole.detective", "探偵", "Detective", "侦探");
                case RoleTypes.Viper: return Lang.T("vanrole.viper", "バイパー", "Viper", "毒蛇");
                case RoleTypes.Judge: return Lang.T("vanrole.judge", "ジャッジ", "Judge", "法官");
                default: return null;
            }
        }
    }

    /// <summary>Every role the host assigns (vanilla SelectRoles, the mod's plain roles, /assign) is recorded while it is not a ghost role.</summary>
    [HarmonyLib.HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SetRole))]
    internal static class RoleReveal_SetRolePatch
    {
        private static void Prefix(PlayerControl targetPlayer, RoleTypes roleType)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return;
                RoleReveal.Record(targetPlayer, roleType);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"RoleReveal_SetRolePatch: {e}"); }
        }
    }

    /// <summary>A new selection starts: forget the previous game's roles.</summary>
    [HarmonyLib.HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    internal static class RoleReveal_SelectRolesPatch
    {
        private static void Prefix()
        {
            try { RoleReveal.AliveRoles.Clear(); RoleReveal.ClearDeferred(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"RoleReveal_SelectRolesPatch: {e}"); }
        }
    }

    /// <summary>v0.5.2: the role of a player who leaves mid-game (RoleReveal.OnPlayerLeft).</summary>
    [HarmonyLib.HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
    internal static class RoleReveal_OnPlayerLeftPatch
    {
        private static void Postfix()
        {
            try { RoleReveal.OnPlayerLeft(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"RoleReveal_OnPlayerLeftPatch: {e}"); }
        }
    }
}
