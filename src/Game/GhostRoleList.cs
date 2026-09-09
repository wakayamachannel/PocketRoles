using System;
using System.Collections.Generic;
using System.Text;
using AmongUs.GameOptions;
using HarmonyLib;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;

    /// <summary>
    /// [Roles] HostGhostRoleList (v0.4.6, default on): once the HOST is dead it reads, on its own screen only, every
    /// player's role — an alive list and a dead list. Shown 1.5 s after the host's death (kill or eject), again at the
    /// start of every meeting while the host is dead, plus one line per later death ("☠ name: role"). Works in
    /// registered role games (PocketRoles roles, coloured) and in unregistered compat lobbies (vanilla roles through
    /// RoleReveal.RoleNameOf). Nothing is ever sent to another client: every line is Chat.Local (host HUD only), so the
    /// feature is invisible to players and costs no chat RPC. /who shows the same list on demand (host, dead or GM).
    /// </summary>
    public static class GhostRoleList
    {
        private const string ShowTag = "ghostlist.show";
        private const float PollInterval = 0.25f;
        private const float ShowDelay = 1.5f;     // after the kill animation / exile screen
        private const float MeetingDelay = 2f;    // after the private role reminders of Meetings_MeetingStartPatch (1 s)

        private static bool _started;
        private static bool _hostDead;
        private static float _nextPoll;
        private static readonly HashSet<byte> _knownDead = new HashSet<byte>();

        /// <summary>Host is currently counted as dead in a running game (drives the meeting re-send and /who).</summary>
        internal static bool HostDead => _started && _hostDead;

        /// <summary>Per-frame poll (Plugin_TickPatch, host only, 4 Hz): host death → list; further deaths while dead → one line each.</summary>
        internal static void Tick()
        {
            try
            {
                if (Time.time < _nextPoll) return;
                _nextPoll = Time.time + PollInterval;
                var client = AmongUsClient.Instance;
                bool started = client != null && client.IsGameStarted && !Game.Ending;
                if (!started)
                {
                    if (_started) Reset();
                    return;
                }
                if (!_started) { _started = true; _hostDead = false; _knownDead.Clear(); }
                var lp = PlayerControl.LocalPlayer;
                if (lp == null || lp.Data == null) return;
                byte hostId = lp.PlayerId;
                bool dead = Game.IsDead(hostId);
                if (!dead) { _hostDead = false; return; }
                if (!_hostDead)
                {
                    _hostDead = true;
                    _knownDead.Clear();
                    foreach (byte id in Game.AllPlayerIds()) if (Game.IsDead(id)) _knownDead.Add(id);
                    PocketRolesPlugin.Logger.LogInfo($"GhostRoleList: host died → list in {ShowDelay:0.#} s (enabled={Options.HostGhostRoleList})");
                    if (Options.HostGhostRoleList) Scheduler.After(ShowDelay, () => Show("death"), ShowTag);
                    return;
                }
                if (!Options.HostGhostRoleList) return;
                foreach (byte id in Game.AllPlayerIds())
                {
                    if (id == hostId || _knownDead.Contains(id) || !Game.IsDead(id)) continue;
                    _knownDead.Add(id);
                    DeathLine(id);
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GhostRoleList.Tick: {e}");
            }
        }

        /// <summary>MeetingHud.Start postfix (own patch below: the Meetings one returns early in compat games).</summary>
        internal static void OnMeetingStart()
        {
            try
            {
                if (!HostDead || !Options.HostGhostRoleList) return;
                Scheduler.After(MeetingDelay, () => Show("meeting"), ShowTag);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GhostRoleList.OnMeetingStart: {e}");
            }
        }

        private static void Reset()
        {
            _started = false;
            _hostDead = false;
            _knownDead.Clear();
            Scheduler.Cancel(ShowTag);
        }

        /// <summary>/who: the list for a dead host (or the Game Master / test mode); refusals otherwise.</summary>
        internal static string OnDemandText()
        {
            var client = AmongUsClient.Instance;
            if (client == null || !client.IsGameStarted || Game.Ending)
                return Lang.T("ghostlist.nogame", "試合中ではありません。", "No game in progress.", "当前不在游戏中。");
            var lp = PlayerControl.LocalPlayer;
            bool dead = lp != null && Game.IsDead(lp.PlayerId);
            if (!dead && !Game.GameMasterActive && !Game.TestMode)
                return Lang.T("ghostlist.alive", "生存中は見られません（死亡すると自動で表示されます）。", "Not while you are alive (the list appears automatically once you die).", "存活时无法查看（死亡后会自动显示）。");
            return BuildText();
        }

        private static void Show(string why)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.IsGameStarted || Game.Ending) return;
                var lp = PlayerControl.LocalPlayer;
                if (lp == null || !Game.IsDead(lp.PlayerId)) return;
                string text = BuildText();
                PocketRolesPlugin.Logger.LogInfo($"GhostRoleList: shown ({why}): " + Lang.StripTags(text).Replace('\n', ' '));
                using (Lang.Scope(Lang.PlayerLang(lp.PlayerId)))
                    Chat.Chat.Local(Chat.Chat.Title, text);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GhostRoleList.Show: {e}");
            }
        }

        private static void DeathLine(byte id)
        {
            try
            {
                int alive = 0;
                foreach (byte p in Game.AllPlayerIds()) if (Game.IsAlive(p)) alive++;
                var lp = PlayerControl.LocalPlayer;
                using (Lang.Scope(Lang.PlayerLang(lp != null ? lp.PlayerId : (byte)0)))
                {
                    string line;
                    // No ☠: the game font has no glyph for it (rendered as ☐ in the 2026-09-10 live test).
                    try { line = string.Format(Lang.T("ghostlist.death", "【死亡】{0}: {1}（生存 {2} 人）", "[Dead] {0}: {1} (alive: {2})", "【死亡】{0}: {1}（存活 {2} 人）"), NameOf(id), RoleText(id), alive); }
                    catch (FormatException) { line = "[Dead] " + NameOf(id) + ": " + RoleText(id); }
                    Chat.Chat.Local(Chat.Chat.Title, line);
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GhostRoleList.DeathLine: {e}");
            }
        }

        /// <summary>Head line + alive block + dead block (one "name: role" line per player, host marked).</summary>
        internal static string BuildText()
        {
            var alive = new List<byte>();
            var dead = new List<byte>();
            byte hostId = PlayerControl.LocalPlayer != null ? PlayerControl.LocalPlayer.PlayerId : (byte)255;
            foreach (byte id in Game.AllPlayerIds())
            {
                if (Game.IsAlive(id)) alive.Add(id); else dead.Add(id);
            }
            alive.Sort();
            dead.Sort();
            var sb = new StringBuilder();
            string head;
            try { head = string.Format(Lang.T("ghostlist.head", "【役職一覧】ホストだけに表示 — 生存 {0} / 死亡 {1}", "[Role list] host only — alive {0} / dead {1}", "【职业一览】仅房主可见 — 存活 {0} / 死亡 {1}"), alive.Count, dead.Count); }
            catch (FormatException) { head = "[Role list] " + alive.Count + " / " + dead.Count; }
            sb.Append(head);
            sb.Append('\n').Append(Lang.T("ghostlist.alive.head", "生存:", "Alive:", "存活:"));
            foreach (byte id in alive) sb.Append('\n').Append(Line(id, hostId));
            sb.Append('\n').Append(Lang.T("ghostlist.dead.head", "死亡:", "Dead:", "死亡:"));
            foreach (byte id in dead) sb.Append('\n').Append(Line(id, hostId));
            return sb.ToString();
        }

        private static string Line(byte id, byte hostId)
        {
            string s = "・" + NameOf(id) + ": " + RoleText(id);
            if (id == hostId) s += Lang.T("ghostlist.you", "（あなた）", " (you)", "（你）");
            var info = Game.Info(id);
            if (info != null && info.Disconnected) s += Lang.T("ghostlist.left", "（切断）", " (left)", "（已离开）");
            return s;
        }

        private static string NameOf(byte id)
        {
            string name = Lang.StripTags(Game.NameOf(id) ?? "").Trim();
            if (name.Length == 0) name = "#" + id;
            if (name.Length > 12) name = name.Substring(0, 12);
            return name;
        }

        /// <summary>PocketRoles role in its colour, else the vanilla role name (impostor-side roles in red).</summary>
        private static string RoleText(byte id)
        {
            var custom = Game.RoleOf(id);
            if (custom != CustomRole.None) return Roles.Info(custom).ColoredName;
            string name = RoleReveal.RoleNameOf(id);
            if (string.IsNullOrEmpty(name)) name = "?";
            return IsImpostorSide(VanillaTypeOf(id)) ? "<color=#ff1919>" + name + "</color>" : name;
        }

        /// <summary>The vanilla role held while alive: the mod's table, then the SetRole recorder, then the live (ghost) role.</summary>
        private static RoleTypes VanillaTypeOf(byte id)
        {
            if (Game.VanillaRoles.TryGetValue(id, out var r)) return r;
            if (RoleReveal.AliveRoles.TryGetValue(id, out var recorded)) return recorded;
            return Game.VanillaRoleOf(id);
        }

        private static bool IsImpostorSide(RoleTypes t)
        {
            return t == RoleTypes.Impostor || t == RoleTypes.Shapeshifter || t == RoleTypes.Phantom || t == RoleTypes.Viper || t == RoleTypes.ImpostorGhost;
        }
    }

    /// <summary>Meeting start while the host is dead: the list again (also in compat games, where Meetings_MeetingStartPatch returns early).</summary>
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    internal static class GhostRoleList_MeetingStartPatch
    {
        private static void Postfix()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || !Game.IsHostActive) return;
                GhostRoleList.OnMeetingStart();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GhostRoleList_MeetingStart: {e}");
            }
        }
    }
}
