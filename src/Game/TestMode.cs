using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AmongUs.GameOptions;
using HarmonyLib;
using PocketRoles.Core;

namespace PocketRoles.Game
{
    /// <summary>
    /// Host-only test mode (DESIGN-v0.2 §E): a lobby can be started alone, the game never ends by itself (only /end and
    /// a sabotage timer), and /assign forces a custom role on a player for the next game. Per session, never persisted:
    /// <see cref="Core.Game.TestMode"/> / <see cref="Core.Game.ForcedRoles"/> survive Game.Reset() and are cleared
    /// when a new lobby is joined (Game.OnLobbyJoined) or by <see cref="Set"/>(false).
    /// </summary>
    public static class TestMode
    {
        public static bool Enabled => Core.Game.TestMode;

        // ------------------------------------------------------------------ public API

        /// <summary>Turns test mode on/off, announces it to everybody and applies the 1-player start (MinPlayers).</summary>
        public static void Set(bool on)
        {
            try
            {
                bool was = Core.Game.TestMode;
                Core.Game.TestMode = on;
                if (!on) Core.Game.ForcedRoles.Clear();
                PocketRolesPlugin.Logger.LogInfo($"TestMode: {(on ? "ON" : "OFF")} (was {(was ? "on" : "off")})");
                // The Update prefixes apply MinPlayers on the next frame; vanilla only redraws the start button when the
                // player count changes, so make it re-evaluate now (1 player: "プレーヤーを待つ" → "開始").
                Lobby.AutoStart.RefreshStartButton();
                Chat.Chat.All(Chat.Chat.Title, () => on
                    ? Lang.T("test.on", "テストモード ON：１人でも開始でき、試合は自動終了しません（/end で終了）。",
                        "Test mode ON: the game can start with 1 player and never ends by itself (use /end).")
                    : Lang.T("test.off", "テストモード OFF：通常の試合に戻ります。役職指定も解除しました。",
                        "Test mode OFF: back to normal games. Forced roles were cleared."));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"TestMode.Set({on}): {e}");
            }
        }

        /// <summary>
        /// Forces <paramref name="role"/> on the player named / numbered <paramref name="playerNameOrId"/> for the next
        /// game (stored in Game.ForcedRoles). <see cref="CustomRole.None"/> removes the entry.
        /// </summary>
        public static bool TryAssign(string playerNameOrId, CustomRole role, out string message)
        {
            message = null;
            try
            {
                var pc = FindPlayer(playerNameOrId);
                if (pc == null)
                {
                    message = Lang.TF("test.assign.noplayer", "プレイヤー「{0}」が見つかりません。/assign <名前|番号> <役職>", "Player \"{0}\" not found. /assign <name|id> <role>", playerNameOrId ?? "");
                    return false;
                }
                byte id = pc.PlayerId;
                string name = Core.Game.NameOf(id);
                if (role == CustomRole.None)
                {
                    bool removed = Core.Game.ForcedRoles.Remove(id);
                    message = removed
                        ? Lang.TF("test.assign.cleared", "{0} の役職指定を解除しました。", "Forced role for {0} cleared.", name)
                        : Lang.TF("test.assign.none", "{0} には役職指定がありません。", "{0} has no forced role.", name);
                    return removed;
                }
                Core.Game.ForcedRoles[id] = role;
                PocketRolesPlugin.Logger.LogInfo($"TestMode: forced role {role} on #{id} {name}");
                message = Lang.TF("test.assign.ok", "次の試合で {0} を {1} にします。", "{0} will be {1} next game.", name, Roles.ColoredName(role));
                if (!Core.Game.TestMode)
                    message += "\n" + Lang.T("test.assign.hint", "（テストモードは OFF です。/test on で有効化できます）", "(Test mode is OFF; /test on enables it.)");
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"TestMode.TryAssign({playerNameOrId}, {role}): {e}");
                message = "error: " + e.Message;
                return false;
            }
        }

        public static void ClearAssignments()
        {
            Core.Game.ForcedRoles.Clear();
            PocketRolesPlugin.Logger.LogInfo("TestMode: forced roles cleared");
        }

        /// <summary>One line per forced role ("name → role"), or a "none" text.</summary>
        public static string DescribeAssignments()
        {
            try
            {
                if (Core.Game.ForcedRoles.Count == 0)
                    return Lang.T("test.assign.empty", "役職指定はありません。", "No forced roles.");
                var sb = new StringBuilder();
                sb.Append(Lang.T("test.assign.list", "次の試合の役職指定:", "Forced roles for the next game:"));
                foreach (var kv in Core.Game.ForcedRoles)
                {
                    sb.Append('\n').Append(Core.Game.NameOf(kv.Key)).Append(" → ").Append(Roles.ColoredName(kv.Value));
                }
                return sb.ToString();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"TestMode.DescribeAssignments: {e}");
                return "error: " + e.Message;
            }
        }

        /// <summary>/end: ends the running game as a crew win even while test mode blocks the automatic end.</summary>
        public static void EndGameManually()
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress || Core.Game.Ending)
                {
                    PocketRolesPlugin.Logger.LogInfo("TestMode.EndGameManually: no modded game running");
                    return;
                }
                PocketRolesPlugin.Logger.LogInfo("TestMode: game ended manually (/end)");
                WinConditions.EndGameOverridingTestMode(WinConditions.WinKind.Crew);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"TestMode.EndGameManually: {e}");
            }
        }

        // ------------------------------------------------------------------ hooks used by RoleAssignment

        /// <summary>
        /// Applies Game.ForcedRoles to the true role table before the random assignment and clears them. A forced role
        /// does not need to come from its usual pool: an impostor-pool role (Vampire / Mafia) on a vanilla crewmate makes
        /// that player a vanilla Impostor for View() / IsImpostorTeamKiller, a crew-pool role on a vanilla impostor makes
        /// it a vanilla Crewmate. The host's local role table is updated the same way (it was set by CoSetRole during
        /// SelectRoles). Returns the ids that received a forced role.
        /// </summary>
        internal static HashSet<byte> ApplyForcedRoles(List<PlayerControl> players)
        {
            var forced = new HashSet<byte>();
            try
            {
                if (Core.Game.ForcedRoles.Count == 0) return forced;
                foreach (var kv in Core.Game.ForcedRoles)
                {
                    byte id = kv.Key;
                    CustomRole role = kv.Value;
                    PlayerControl pc = null;
                    foreach (var p in players)
                    {
                        if (p.PlayerId == id) { pc = p; break; }
                    }
                    if (pc == null || pc.Data == null || pc.Data.Disconnected || role == CustomRole.None)
                    {
                        PocketRolesPlugin.Logger.LogWarning($"TestMode: forced role {role} for #{id} skipped (player missing)");
                        continue;
                    }
                    var info = Roles.Info(role);
                    RoleTypes vanilla = Core.Game.VanillaRoleOf(id);
                    bool vanillaImp = IsImpostorRole(vanilla);
                    RoleTypes basis = vanilla;
                    if (info.FromImpostorPool && !vanillaImp) basis = RoleTypes.Impostor;
                    else if (!info.FromImpostorPool && vanillaImp) basis = RoleTypes.Crewmate;
                    if (basis != vanilla)
                    {
                        Core.Game.VanillaRoles[id] = basis;
                        // Keep the host's local table on the same basis as the desync views computed from it.
                        try { pc.StartCoroutine(pc.CoSetRole(basis, true)); }
                        catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"TestMode: local CoSetRole for #{id} failed: {e.Message}"); }
                    }
                    Core.Game.Roles[id] = role;
                    forced.Add(id);
                    PocketRolesPlugin.Logger.LogInfo($"TestMode: forced #{id} {Core.Game.NameOf(id)} -> {info.NameEn} (vanilla {vanilla} -> {basis})");
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"TestMode.ApplyForcedRoles: {e}");
            }
            finally
            {
                Core.Game.ForcedRoles.Clear();
            }
            return forced;
        }

        // ------------------------------------------------------------------ helpers

        private static bool IsImpostorRole(RoleTypes r)
        {
            return r == RoleTypes.Impostor || r == RoleTypes.Shapeshifter || r == RoleTypes.Phantom || r == RoleTypes.Viper || r == RoleTypes.ImpostorGhost;
        }

        /// <summary>
        /// Full-width spaces (U+3000) become ASCII spaces: the command tokenizer splits on both and rejoins the name
        /// with ASCII spaces, so "山田　太郎" must still match.
        /// </summary>
        private static string NormName(string s)
        {
            return s == null ? "" : s.Replace('　', ' ').Trim();
        }

        /// <summary>"#3" / "3" → player id; otherwise exact name (case-insensitive), then unique prefix / substring.</summary>
        private static PlayerControl FindPlayer(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = NormName(text);
            var players = Core.Game.AllPlayers();

            string num = t.StartsWith("#") ? t.Substring(1) : t;
            if (int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idNum) && idNum >= 0 && idNum < 255)
            {
                foreach (var pc in players)
                {
                    if (pc.PlayerId == idNum && !pc.Data.Disconnected) return pc;
                }
            }

            PlayerControl exact = null, partial = null;
            int partialCount = 0;
            foreach (var pc in players)
            {
                if (pc.Data.Disconnected) continue;
                string name = NormName(Core.Game.NameOf(pc.PlayerId));
                if (string.IsNullOrEmpty(name)) continue;
                if (string.Equals(name, t, StringComparison.OrdinalIgnoreCase))
                {
                    if (exact == null) exact = pc;
                    continue;
                }
                if (name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    partial = pc;
                    partialCount++;
                }
            }
            if (exact != null) return exact;
            return partialCount == 1 ? partial : null;
        }
    }

    // ---------------------------------------------------------------------- patches

    /// <summary>
    /// Lobby: in test mode the start button is enabled from 1 player. Vanilla decides from LastPlayerCount &gt;= MinPlayers
    /// inside Update — but only when the player count CHANGED (verify finding #2), so every MinPlayers change also
    /// resets LastPlayerCount (AutoStart.SetMinPlayers) and the button / counter are redrawn on that Update. The vanilla
    /// value is restored when neither test mode nor auto-start / forced start / haison (AutoStart.WantsMinPlayersOne,
    /// applied by AutoStart_MinPlayersPatch after this prefix) needs 1.
    /// </summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
    internal static class TestMode_GameStartUpdatePatch
    {
        private static int _originalMinPlayers = -1;
        private static IntPtr _capturedFor = IntPtr.Zero;

        /// <summary>The vanilla MinPlayers of the current lobby (-1 when unknown), for Diagnostics.Describe().</summary>
        internal static int OriginalMinPlayers => _originalMinPlayers;

        private static void Prefix(GameStartManager __instance)
        {
            try
            {
                if (__instance == null) return;
                if (_capturedFor != __instance.Pointer)
                {
                    // new GameStartManager (new lobby): the vanilla value is whatever it holds now, before we touch it
                    _capturedFor = __instance.Pointer;
                    _originalMinPlayers = __instance.MinPlayers;
                }
                if (!Core.Game.IsHostActive || !Core.Game.TestMode)
                {
                    // Restore only when nobody else wants 1 (otherwise the two prefixes would flip the value every frame).
                    bool othersWantOne = Core.Game.IsHostActive && Lobby.AutoStart.WantsMinPlayersOne();
                    if (!othersWantOne && _originalMinPlayers > 0)
                        Lobby.AutoStart.SetMinPlayers(__instance, _originalMinPlayers, "test mode off: vanilla value restored");
                    return;
                }
                Lobby.AutoStart.SetMinPlayers(__instance, 1, "test mode");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"TestMode_GameStartUpdatePatch: {e}");
            }
        }
    }

    /// <summary>
    /// Verify finding #14: the start button (BeginGame) shows the vanilla small-lobby popup ("4 人でプレイ可能ですが 5 人以上
    /// がおすすめ", <c>GameStartManager.GameSizePopup</c>, three buttons) below 5 players. In test mode, or while
    /// auto-start / a forced start is driving the lobby, the host does not want to click it: when the popup came up,
    /// press its "OK" for the host — <c>ReallyBegin(false)</c> (false = do not set the vanilla "never show again"
    /// preference). Nothing is touched when BeginGame went straight to ReallyBegin (startState already Countdown) or
    /// when another prefix skipped BeginGame (a player not yet serialized: Registration_BeginGamePatch).
    /// </summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
    internal static class TestMode_GameSizePopupPatch
    {
        /// <summary>GameId of the lobby whose popup skip was already logged (one line per lobby; the skip itself always runs).</summary>
        private static int _loggedGameId = int.MinValue;

        private static void Postfix(GameStartManager __instance, bool __runOriginal)
        {
            try
            {
                if (!__runOriginal || __instance == null) return;
                if (!Core.Game.IsHostActive) return;
                if (!Core.Game.TestMode && !Lobby.AutoStart.Forcing) return;
                var popup = __instance.GameSizePopup;
                if (popup == null || !popup.activeSelf) return;
                if (__instance.startState != GameStartManager.StartingStates.NotStarting) return;
                int gameId = int.MinValue;
                try { if (AmongUsClient.Instance != null) gameId = AmongUsClient.Instance.GameId; } catch (Exception) { }
                if (gameId == int.MinValue || gameId != _loggedGameId)
                {
                    _loggedGameId = gameId;
                    PocketRolesPlugin.Logger.LogInfo($"TestMode: small-lobby popup auto-confirmed ({(Core.Game.TestMode ? "test mode" : "forced start")}, players={Lobby.AutoStart.PlayerCount()}; logged once per lobby)");
                }
                __instance.ReallyBegin(false);
                try { if (popup.activeSelf) popup.SetActive(false); } catch (Exception) { }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"TestMode_GameSizePopupPatch: {e}");
            }
        }
    }
}
