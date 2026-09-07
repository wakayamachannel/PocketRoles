using System;
using System.Collections.Generic;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Net;

namespace PocketRoles.Game
{
    /// <summary>
    /// Per-viewer name tags. Every vanilla client can be shown a different string for every player (SetName is per client),
    /// so the role name, the Madmate marker, the Snitch star and the red killer names are all delivered this way.
    /// The host itself never receives an RPC: its view is applied locally through PlayerControl.cosmetics.SetName
    /// (Data.PlayerName is never touched — it must keep the original name for the network Data sync).
    /// </summary>
    public static class NameTags
    {
        private const string MurderRefreshTag = "names.murder";

        /// <summary>(viewer playerId, target playerId) → last string sent to that viewer for that target.</summary>
        private static readonly Dictionary<(byte, byte), string> LastSent = new Dictionary<(byte, byte), string>();

        private static bool _hooked;

        static NameTags()
        {
            EnsureHooks();
        }

        /// <summary>Installs the Rpc callbacks once (idempotent).</summary>
        public static void EnsureHooks()
        {
            if (_hooked) return;
            _hooked = true;
            Rpc.HostNameProvider = HostNameForClient;
            Rpc.AfterReviveRestore = () => RefreshAll(force: true);
            Rpc.AfterReviveRestoreClients = clients =>
            {
                if (clients == null) return;
                foreach (int clientId in clients) RefreshClient(clientId, force: true);
            };
        }

        // ------------------------------------------------------------------ NameFor

        /// <summary>The string <paramref name="viewerId"/> should see for <paramref name="targetId"/>.</summary>
        public static string NameFor(byte viewerId, byte targetId, bool meeting = false)
        {
            string baseName = Core.Game.NameOf(targetId) ?? "";
            try
            {
                // v0.4b §L: VIP.txt players carry a star in front of their name ([Permissions] VipMarker), every viewer.
                try { baseName = Permissions.VipMark(targetId) + baseName; }
                catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"NameTags.NameFor: VipMark({targetId}): {e.Message}"); }

                CustomRole targetRole = Core.Game.RoleOf(targetId);
                CustomRole viewerRole = Core.Game.RoleOf(viewerId);

                // 1. own role tag
                if (viewerId == targetId)
                {
                    if (targetRole == CustomRole.None) return baseName;
                    RoleInfo info = Roles.Info(targetRole);
                    if (meeting)
                        return baseName + " <size=70%>" + info.ColoredName + "</size>";
                    return info.ColoredName + "\r\n" + baseName;
                }

                bool viewerImpKiller = Core.Game.IsImpostorTeamKiller(viewerId);
                bool viewerJackal = viewerRole == CustomRole.Jackal;
                bool targetImpKiller = Core.Game.IsImpostorTeamKiller(targetId);
                bool targetJackal = targetRole == CustomRole.Jackal;

                // 2. Madmate sees the impostor-team killers in red
                if (viewerRole == CustomRole.Madmate && targetImpKiller)
                    return "<color=" + Roles.ImpostorColor + ">" + baseName + "</color>";

                // 3. impostors see the Madmate marker (option)
                if (viewerImpKiller && targetRole == CustomRole.Madmate && Options.MadmateKnownToImpostors)
                    return "<color=" + Roles.ImpostorColor + ">Ⓜ</color>" + baseName;

                // 4. Snitch nearly done → killers see a star on the Snitch
                if (targetRole == CustomRole.Snitch && (viewerImpKiller || viewerJackal)
                    && Core.Game.IsAlive(targetId) && Core.Game.TasksTotal(targetId) > 0
                    && Core.Game.TasksLeft(targetId) <= Options.SnitchTasksLeftToWarn)
                    return "<color=" + Roles.SnitchColor + ">★</color>" + baseName;

                // 5. Snitch with all tasks done sees killers coloured
                if (viewerRole == CustomRole.Snitch && Core.Game.TasksDone(viewerId))
                {
                    if (targetImpKiller) return "<color=" + Roles.ImpostorColor + ">" + baseName + "</color>";
                    if (targetJackal) return "<color=" + Roles.JackalColor + ">" + baseName + "</color>";
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"NameTags.NameFor({viewerId},{targetId}): {e}");
            }
            return baseName;
        }

        // ------------------------------------------------------------------ RefreshAll / RestoreAll

        /// <summary>
        /// Sends every (viewer, target) name that changed since the last send (or everything when <paramref name="force"/>),
        /// one paced Rpc.Batch per remote client; the host's own view is applied locally.
        /// </summary>
        public static void RefreshAll(bool force = false, bool meeting = false) => RefreshAll(force, meeting, false);

        /// <param name="urgent">send the batches immediately instead of through the paced queue (pre-meeting names only)</param>
        public static void RefreshAll(bool force, bool meeting, bool urgent)
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                EnsureHooks();
                if (MeetingHud.Instance != null) meeting = true;

                var players = Core.Game.AllPlayers();
                if (players.Count == 0) return;

                // Urgent: every client's message goes into one shared packet set (2–3 SendOrDisconnect calls at most)
                // instead of one immediate packet per client in the same frame (official-server burst kick).
                var multi = urgent ? new Rpc.MultiBatch() : null;
                foreach (var viewer in players)
                {
                    var viewerInfo = viewer.Data;
                    if (viewerInfo == null || viewerInfo.Disconnected) continue;
                    RefreshViewer(viewer, players, force, meeting, urgent, multi);
                }
                if (multi != null && !multi.IsEmpty) multi.Send(true);

                if (meeting) ApplyLocalMeetingNames();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"NameTags.RefreshAll(force={force}, meeting={meeting}): {e}");
            }
        }

        /// <summary>Re-sends the names one remote client sees (after a targeted Data sync reset them there).</summary>
        internal static void RefreshClient(int clientId, bool force)
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                if (clientId < 0 || Rpc.IsLocal(clientId)) return;
                if (Registration.CompatMode) return; // no SetName to remote clients in compat mode
                var players = Core.Game.AllPlayers();
                foreach (var viewer in players)
                {
                    if (viewer.OwnerId != clientId) continue;
                    if (viewer.Data == null || viewer.Data.Disconnected) continue;
                    RefreshViewer(viewer, players, force, MeetingHud.Instance != null, false);
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"NameTags.RefreshClient({clientId}): {e}");
            }
        }

        private static void RefreshViewer(PlayerControl viewer, List<PlayerControl> players, bool force, bool meeting, bool urgent, Rpc.MultiBatch multi = null)
        {
            byte viewerId = viewer.PlayerId;
            bool local = viewer.AmOwner;
            int clientId = Rpc.ClientIdOf(viewer);
            if (!local && clientId < 0) return;
            // Compat mode (unregistered lobby): SetName is never sent to anyone; only the host's own screen gets tags.
            if (!local && Registration.CompatMode) return;

            Rpc.Batch batch = local ? null : (multi != null ? multi.For(clientId) : new Rpc.Batch(clientId));
            foreach (var target in players)
            {
                var targetInfo = target.Data;
                if (targetInfo == null || targetInfo.Disconnected) continue;
                byte targetId = target.PlayerId;
                string name = NameFor(viewerId, targetId, meeting);
                var key = (viewerId, targetId);
                if (!force && LastSent.TryGetValue(key, out var prev) && prev == name) continue;
                LastSent[key] = name;
                if (local) ApplyLocal(targetId, name);
                else batch.SetName(target, name);
            }
            if (batch != null && !batch.IsEmpty) batch.Send(urgent);
        }

        /// <summary>Broadcasts the original names to everyone (one paced broadcast packet), applies them locally and clears the cache.</summary>
        public static void RestoreAll()
        {
            try
            {
                ClearCache();
                // Host check only: the originals must go back even if the mod was switched off on the end screen.
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return;
                var players = Core.Game.AllPlayers();
                if (players.Count == 0) return;
                // Compat mode: nothing was ever sent, so nothing to restore on the wire (host screen only).
                bool compat = Registration.CompatMode;
                var batch = compat ? null : new Rpc.Batch(-1);
                foreach (var pc in players)
                {
                    byte id = pc.PlayerId;
                    string original = Core.Game.NameOf(id);
                    if (string.IsNullOrEmpty(original)) continue;
                    if (batch != null) batch.SetName(pc, original);
                    ApplyLocal(id, original);
                }
                if (batch != null && !batch.IsEmpty) batch.Send();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"NameTags.RestoreAll: {e}");
            }
        }

        /// <summary>Host view only: changes the rendered name tag, never Data.PlayerName.</summary>
        public static void ApplyLocal(byte targetId, string name)
        {
            try
            {
                if (name == null) return;
                var pc = Core.Game.Player(targetId);
                if (pc == null || pc.cosmetics == null) return;
                pc.cosmetics.SetName(name);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"NameTags.ApplyLocal({targetId}): {e}");
            }
        }

        public static void ClearCache()
        {
            LastSent.Clear();
        }

        // ------------------------------------------------------------------ private helpers

        /// <summary>Rpc.HostNameProvider: the name the given client currently sees for the host.</summary>
        private static string HostNameForClient(int clientId)
        {
            try
            {
                var host = PlayerControl.LocalPlayer;
                if (host == null) return null;
                byte hostId = host.PlayerId;
                foreach (var pc in Core.Game.AllPlayers())
                {
                    if (pc.OwnerId != clientId) continue;
                    if (!Core.Game.InProgress) return Core.Game.NameOf(hostId);
                    return NameFor(pc.PlayerId, hostId, MeetingHud.Instance != null);
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"NameTags.HostNameForClient({clientId}): {e}");
            }
            return null;
        }

        /// <summary>The host's meeting screen builds its vote areas from Data.PlayerName; overwrite their labels with the host's view.</summary>
        private static void ApplyLocalMeetingNames()
        {
            try
            {
                var hud = MeetingHud.Instance;
                var host = PlayerControl.LocalPlayer;
                if (hud == null || host == null || hud.playerStates == null) return;
                byte hostId = host.PlayerId;
                foreach (var pva in hud.playerStates)
                {
                    if (pva == null || pva.NameText == null) continue;
                    byte targetId = pva.PlayerId;
                    pva.NameText.text = NameFor(hostId, targetId, true);
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"NameTags.ApplyLocalMeetingNames: {e}");
            }
        }

        internal static void ScheduleMurderRefresh()
        {
            Scheduler.Cancel(MurderRefreshTag);
            Scheduler.After(0.5f, () => RefreshAll(force: true), MurderRefreshTag);
        }

        internal static bool AnySnitch()
        {
            foreach (var kv in Core.Game.Roles)
            {
                if (kv.Value == CustomRole.Snitch) return true;
            }
            return false;
        }
    }

    /// <summary>Install the Rpc callbacks as soon as the HUD exists (before any chat can be sent).</summary>
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Start))]
    internal static class NameTags_HudStartPatch
    {
        private static void Postfix()
        {
            try
            {
                NameTags.EnsureHooks();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"NameTags_HudStartPatch: {e}");
            }
        }
    }

    /// <summary>Snitch progress: a completed task can change what killers / the Snitch see.</summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CompleteTask))]
    internal static class NameTags_CompleteTaskPatch
    {
        private static void Postfix(PlayerControl __instance)
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                if (!NameTags.AnySnitch()) return;
                NameTags.RefreshAll();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"NameTags_CompleteTaskPatch: {e}");
            }
        }
    }

    /// <summary>A death triggers a NetworkedPlayerInfo Data sync which resets the private names on every client.</summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
    internal static class NameTags_MurderPlayerPatch
    {
        private static void Postfix(PlayerControl __instance, PlayerControl target, MurderResultFlags resultFlags)
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                NameTags.ScheduleMurderRefresh();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"NameTags_MurderPlayerPatch: {e}");
            }
        }
    }

    /// <summary>A disconnect makes the host re-sync NetworkedPlayerInfo data, which resets private names on every client.</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
    internal static class NameTags_OnPlayerLeftPatch
    {
        private static void Postfix()
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                NameTags.ScheduleMurderRefresh();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"NameTags_OnPlayerLeftPatch: {e}");
            }
        }
    }

    /// <summary>The host's own meeting screen: relabel the vote areas with the host's view once they exist.</summary>
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    internal static class NameTags_MeetingStartPatch
    {
        private static void Postfix()
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                Scheduler.After(0.5f, () => NameTags.RefreshAll(force: false, meeting: true), "names.meeting");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"NameTags_MeetingStartPatch: {e}");
            }
        }
    }
}
