using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using Hazel;
using PocketRoles.Core;
using InnerNet;
using UnityEngine;

namespace PocketRoles.Net
{
    /// <summary>
    /// Low-level host → client messaging. Everything here writes raw Hazel GameData(5) / GameDataTo(6) messages so that
    /// each vanilla client can receive a different role / name / chat than the others (see TECH-NOTES.md).
    /// </summary>
    public static class Rpc
    {
        /// <summary>
        /// Unregistered lobby (RegisterAsModdedLobby=false while hosting): the full server anti-cheat applies, so
        /// sends are paced slower (Queue 0.3 s), packets are smaller (400 bytes), the silent Exiled RPC is never sent
        /// and the dead-host revive trick is skipped. Set by Registration on lobby join.
        /// </summary>
        public static bool SafeMode;

        /// <summary>
        /// Compat mode (= <see cref="SafeMode"/>, see <see cref="Registration.CompatMode"/>, findings #21/#28): the
        /// official server disconnects the host ("DC because Hacking") for ANY message addressed to one client
        /// (GameDataTo, per-client RPC — a SendChat inside a GameDataTo was kicked too, 2026-09-08 live test). So
        /// nothing per-client ever leaves the host here: every mod chat line is ONE public SendChat from the host's
        /// own player (GameData 5, same bytes as the vanilla RpcSendChat, the text carries the title as a prefix), and
        /// every other per-client send (<see cref="SetRoleTo"/>, <see cref="SetNameTo"/>, targeted
        /// <see cref="Batch"/> / <see cref="MultiBatch"/>, targeted <see cref="SendPlayerInfo"/>, OptionsDesync,
        /// AntiBlackout) is skipped through <see cref="CompatBlocked"/>.
        /// </summary>
        public static bool CompatMode => SafeMode;

        /// <summary>Text prefix of every mod chat message in compat mode ("[PocketRoles] …"); Chat shortens its messages by this length.</summary>
        public const string CompatChatPrefix = "[PocketRoles] ";

        private static int _compatNameDrops;

        /// <summary>Send sites skipped in this lobby / game because of the compat mode (one warning per site).</summary>
        private static readonly HashSet<string> CompatSkipped = new HashSet<string>();

        /// <summary>
        /// Compat mode guard for client-addressed sends: true = the caller must not send (unregistered lobby, the server
        /// would disconnect the host). Logged once per site per lobby / game (<see cref="ResetCompatWarnings"/>).
        /// </summary>
        public static bool CompatBlocked(string where)
        {
            if (!CompatMode) return false;
            try
            {
                if (CompatSkipped.Add(where ?? ""))
                    PocketRolesPlugin.Logger.LogWarning($"Rpc.{where}: per-client send skipped (compat mode: unregistered lobby, no GameDataTo / per-client RPC)");
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Rpc.CompatBlocked({where}): {e}"); }
            return true;
        }

        /// <summary>New lobby / new game: the compat-mode warnings may be logged again.</summary>
        internal static void ResetCompatWarnings()
        {
            CompatSkipped.Clear();
            _compatNameDrops = 0;
        }

        /// <summary>Compat mode: a remote SetName is never sent (logged a few times, then silently).</summary>
        private static bool DropSetNameInCompat(string where)
        {
            if (!CompatMode) return false;
            if (_compatNameDrops < 5)
                PocketRolesPlugin.Logger.LogWarning($"Rpc.{where}: SetName not sent (compat mode)");
            _compatNameDrops++;
            return true;
        }

        /// <summary>Packets larger than this are split into a new writer (official server limit is 1200 incl. header).</summary>
        private static int ChunkBytes => SafeMode ? 400 : 500;

        public static int HostClientId => AmongUsClient.Instance.ClientId;

        public static bool IsLocal(int clientId)
        {
            var client = AmongUsClient.Instance;
            return client != null && clientId == client.ClientId;
        }

        public static int ClientIdOf(PlayerControl pc) => pc == null ? -1 : pc.OwnerId;

        public static IEnumerable<int> AllClientIds(bool includeHost)
        {
            var result = new List<int>();
            var client = AmongUsClient.Instance;
            if (client == null) return result;
            var all = client.allClients;
            if (all == null) return result;
            int host = client.ClientId;
            foreach (var cd in all)
            {
                if (cd == null) continue;
                int id = cd.Id;
                if (!includeHost && id == host) continue;
                result.Add(id);
            }
            return result;
        }

        /// <summary>viewer clientId → name that viewer should see for the host (set by NameTags; default = host's real name)</summary>
        public static Func<int, string> HostNameProvider;

        /// <summary>invoked after a dead-host chat revive/restore cycle that was broadcast (NameTags hooks RefreshAll(force) here)</summary>
        public static Action AfterReviveRestore;

        /// <summary>invoked after a dead-host chat revive/restore cycle that only touched the given client ids (NameTags refreshes those viewers)</summary>
        public static Action<List<int>> AfterReviveRestoreClients;

        // ------------------------------------------------------------------ roles

        public static void SetRoleTo(PlayerControl target, RoleTypes role, int clientId)
        {
            if (target == null) return;
            if (IsLocal(clientId))
            {
                ApplyRoleLocal(target, role);
                return;
            }
            if (CompatBlocked("SetRoleTo")) return; // unregistered lobby: no per-client RPC (findings #21/#28)
            var client = AmongUsClient.Instance;
            if (client == null) return;
            var w = client.StartRpcImmediately(target.NetId, (byte)RpcCalls.SetRole, SendOption.Reliable, clientId);
            w.Write((ushort)role);
            w.Write(true);
            client.FinishRpcImmediately(w);
        }

        public static void SetRoleAll(PlayerControl target, RoleTypes role)
        {
            if (target == null) return;
            var client = AmongUsClient.Instance;
            if (client == null) return;
            var w = client.StartRpcImmediately(target.NetId, (byte)RpcCalls.SetRole, SendOption.Reliable, -1);
            w.Write((ushort)role);
            w.Write(true);
            client.FinishRpcImmediately(w);
            ApplyRoleLocal(target, role);
        }

        /// <summary>
        /// Writes the host's own role table entry for <paramref name="target"/> (the local view). 2026.8.18: a
        /// PlayerControl.CoSetRole(role, canOverride: true) started after the vanilla assignment never reaches
        /// RoleManager.SetRole on the host (solo test 2026-09-08 22:45: a host Arsonist and a forced Impostor basis kept
        /// the Crewmate HUD — no kill button, real tasks), so the table is written directly through RoleManager.SetRole.
        /// Before the intro the HUD is rebuilt from Data.Role at the intro end anyway; while a game already runs
        /// (ghost roles, late views) SetHudActive(true) refreshes the buttons of the local player.
        /// </summary>
        internal static void ApplyRoleLocal(PlayerControl target, RoleTypes role)
        {
            if (target == null) return;
            try
            {
                var rm = RoleManager.Instance;
                if (rm == null)
                {
                    target.StartCoroutine(target.CoSetRole(role, true));
                    return;
                }
                rm.SetRole(target, role);
                RoleTypes now = target.Data != null && target.Data.Role != null ? target.Data.Role.Role : RoleTypes.Crewmate;
                PocketRolesPlugin.Logger.LogInfo($"Rpc.ApplyRoleLocal: #{target.PlayerId} -> {role} (local table now {now})");
                if (target.AmOwner && GameManager.Instance != null && GameManager.Instance.GameHasStarted && HudManager.Instance != null)
                {
                    try { HudManager.Instance.SetHudActive(true); }
                    catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"Rpc.ApplyRoleLocal: SetHudActive: {e.Message}"); }
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Rpc.ApplyRoleLocal({role}): {e}");
            }
        }

        // ------------------------------------------------------------------ names

        public static void SetNameTo(PlayerControl target, string name, int clientId)
        {
            if (target == null || name == null) return;
            if (IsLocal(clientId))
            {
                ApplyNameLocal(target, name);
                return;
            }
            if (DropSetNameInCompat("SetNameTo")) return;
            var client = AmongUsClient.Instance;
            if (client == null || target.Data == null) return;
            var w = client.StartRpcImmediately(target.NetId, (byte)RpcCalls.SetName, SendOption.Reliable, clientId);
            w.Write(target.Data.NetId);
            w.Write(name);
            w.Write(false);
            client.FinishRpcImmediately(w);
        }

        public static void SetNameAll(PlayerControl target, string name)
        {
            if (target == null || name == null) return;
            if (DropSetNameInCompat("SetNameAll")) { ApplyNameLocal(target, name); return; }
            var client = AmongUsClient.Instance;
            if (client == null || target.Data == null) return;
            var w = client.StartRpcImmediately(target.NetId, (byte)RpcCalls.SetName, SendOption.Reliable, -1);
            w.Write(target.Data.NetId);
            w.Write(name);
            w.Write(false);
            client.FinishRpcImmediately(w);
            ApplyNameLocal(target, name);
        }

        private static void ApplyNameLocal(PlayerControl target, string name)
        {
            try
            {
                if (target.cosmetics != null) target.cosmetics.SetName(name);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Rpc.ApplyNameLocal: {e}");
            }
        }

        // ------------------------------------------------------------------ chat

        public static void SendChatTo(int clientId, string title, string text)
        {
            if (text == null) return;
            if (IsLocal(clientId))
            {
                AddLocalChat(title, text);
                return;
            }
            if (CompatMode)
            {
                // Unregistered lobby: a client-addressed chat does not exist (findings #21/#28). Chat routes its
                // per-player texts through Chat.SendPublicChunks itself (with an "@name" prefix); anything that still
                // lands here becomes the same public broadcast, once (logged once per lobby / game).
                CompatBlocked("SendChatTo->public");
                SendChatAll(title, text);
                return;
            }
            TempReviveHostForChat(() =>
            {
                var w = BuildChatWriter(clientId, title, text);
                if (w == null) return;
                SendNow(w);
            }, 1f, clientId);
        }

        /// <summary>
        /// Compat mode public chat: ONE SendChat RPC (RpcCalls.SendChat, payload = string) on the HOST's own
        /// PlayerControl net id inside a GameData(5) broadcast — byte for byte what the vanilla
        /// PlayerControl.RpcSendChat sends (StartRpcImmediately(netId, SendChat, Reliable, -1)), only queued instead
        /// of sent at once. Every client sees the host as the sender; the title is carried in the text
        /// ("[PocketRoles] …", trimmed to the 100-character chat limit). Never a GameDataTo, never a SetName.
        /// </summary>
        private static MessageWriter BuildPublicChatWriter(string title, string text)
        {
            var client = AmongUsClient.Instance;
            var host = PlayerControl.LocalPlayer;
            if (client == null || host == null || text == null) return null;
            string line = string.IsNullOrEmpty(title) ? text : "[" + title + "] " + text;
            int max = PocketRoles.Chat.Chat.MaxChars;
            if (line.Length > max)
            {
                PocketRolesPlugin.Logger.LogWarning($"Rpc.BuildPublicChatWriter: line of {line.Length} chars trimmed to {max}");
                line = line.Substring(0, max);
            }
            MessageWriter w = null;
            try
            {
                w = client.StartRpcImmediately(host.NetId, (byte)RpcCalls.SendChat, SendOption.Reliable, -1);
                w.Write(line);
                w.EndMessage(); // RPC(2)
                w.EndMessage(); // GameData(5)
                return w;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Rpc.BuildPublicChatWriter: {e}");
                if (w != null) { try { w.Recycle(); } catch { } }
                return null;
            }
        }

        public static void SendChatAll(string title, string text)
        {
            if (text == null) return;
            AddLocalChat(title, text);
            var ids = new List<int>(AllClientIds(false));
            if (ids.Count == 0) return;
            if (CompatMode)
            {
                // One public broadcast for everyone (the host's copy was added above), paced through the queue.
                var pw = BuildPublicChatWriter(title, text);
                if (pw != null) Queue.Enqueue(pw);
                return;
            }
            // Chat goes through the paced queue; keep the host "alive" long enough for every packet to leave.
            float hold = 1f + Queue.Interval * (ids.Count + 1);
            TempReviveHostForChat(() =>
            {
                foreach (int id in ids)
                {
                    var w = BuildChatWriter(id, title, text);
                    if (w != null) Queue.Enqueue(w);
                }
            }, hold, -1);
        }

        private static void AddLocalChat(string title, string text)
        {
            try
            {
                var hud = HudManager.Instance;
                if (hud == null || hud.Chat == null || PlayerControl.LocalPlayer == null) return;
                string line = string.IsNullOrEmpty(title) ? text : $"<color=#a0a0a0>[{title}]</color> {text}";
                hud.Chat.AddChat(PlayerControl.LocalPlayer, line, false);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Rpc.AddLocalChat: {e}");
            }
        }

        /// <summary>SetName(host → title), SendChat(text), SetName(host → restore) in ONE GameDataTo message.</summary>
        private static MessageWriter BuildChatWriter(int clientId, string title, string text)
        {
            var client = AmongUsClient.Instance;
            var host = PlayerControl.LocalPlayer;
            if (client == null || host == null || host.Data == null) return null;
            string restore = null;
            try { restore = HostNameProvider?.Invoke(clientId); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Rpc.HostNameProvider: {e}"); }
            if (string.IsNullOrEmpty(restore)) restore = host.Data.PlayerName ?? "";
            if (string.IsNullOrEmpty(title)) title = restore;

            var w = MessageWriter.Get(SendOption.Reliable);
            w.StartMessage(6);
            w.Write(client.GameId);
            w.WritePacked(clientId);

            w.StartMessage(2);
            w.WritePacked(host.NetId);
            w.Write((byte)RpcCalls.SetName);
            w.Write(host.Data.NetId);
            w.Write(title);
            w.Write(false);
            w.EndMessage();

            w.StartMessage(2);
            w.WritePacked(host.NetId);
            w.Write((byte)RpcCalls.SendChat);
            w.Write(text);
            w.EndMessage();

            w.StartMessage(2);
            w.WritePacked(host.NetId);
            w.Write((byte)RpcCalls.SetName);
            w.Write(host.Data.NetId);
            w.Write(restore);
            w.Write(false);
            w.EndMessage();

            w.EndMessage();
            return w;
        }

        private const string ReviveTag = "rpc.tempRevive";
        private static float _reviveUntil = -1f;
        private static bool _hostRevived;
        /// <summary>The host really was dead when the current revive window opened (guards the IsDead restore).</summary>
        private static bool _wasDead;
        /// <summary>Clients that received the "host alive" Data message (-1 = everyone).</summary>
        private static readonly HashSet<int> ReviveTargets = new HashSet<int>();

        /// <summary>
        /// True while a dead host is temporarily marked alive (Data.IsDead == false) so its chat is shown on vanilla
        /// clients. Host-side logic (Game.IsDead / win checks) must keep treating the host as dead during this window.
        /// </summary>
        public static bool HostTempRevived => _hostRevived && Scheduler.HasTag(ReviveTag);

        public static void TempReviveHostForChat(Action sendChat) => TempReviveHostForChat(sendChat, 1f, -1);

        /// <param name="clientId">the only client that will receive the chat (-1 = everyone); the revive Data message goes to that client only</param>
        private static void TempReviveHostForChat(Action sendChat, float holdSeconds, int clientId)
        {
            var host = PlayerControl.LocalPlayer;
            var info = host != null ? host.Data : null;
            bool dead = info != null && info.IsDead;
            if (!dead && Time.time >= _reviveUntil)
            {
                sendChat?.Invoke();
                return;
            }
            if (SafeMode)
            {
                // Unregistered lobby: no "alive" Data trick (vanilla clients then show the text to dead players only).
                try { sendChat?.Invoke(); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Rpc.TempRevive send (safe mode): {e}"); }
                return;
            }
            // Host is dead (or a revive window is already open): keep IsDead=false while the chat goes out.
            if (info != null)
            {
                bool broadcastDone = ReviveTargets.Contains(-1);
                if (info.IsDead)
                {
                    _hostRevived = true;
                    _wasDead = true;
                    info.IsDead = false;
                }
                // Send the "alive" Data only to clients that have not seen it yet in this window.
                if (_hostRevived && !broadcastDone && ReviveTargets.Add(clientId))
                {
                    if (clientId == -1) SendPlayerInfo(info);
                    else SendPlayerInfo(info, clientId);
                }
            }
            try { sendChat?.Invoke(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Rpc.TempRevive send: {e}"); }

            float until = Time.time + holdSeconds;
            if (until > _reviveUntil) _reviveUntil = until;
            Scheduler.Cancel(ReviveTag);
            Scheduler.After(Mathf.Max(0.05f, _reviveUntil - Time.time), () => EndTempRevive(true), ReviveTag);
        }

        /// <summary>
        /// Restores IsDead = true on the wire for every client that saw the revive. Targeted restores are paced through
        /// the queue (several clients may have accumulated); a broadcast restore goes out immediately.
        /// </summary>
        private static void EndTempRevive(bool notify)
        {
            _reviveUntil = -1f;
            var targets = new List<int>(ReviveTargets);
            ReviveTargets.Clear();
            var h = PlayerControl.LocalPlayer;
            var i = h != null ? h.Data : null;
            bool broadcast = targets.Contains(-1);
            if (i != null && _hostRevived && _wasDead)
            {
                i.IsDead = true;
                if (broadcast) SendPlayerInfo(i);
                else foreach (int id in targets) SendPlayerInfo(i, id, false);
            }
            bool wasRevived = _hostRevived;
            _hostRevived = false;
            _wasDead = false;
            if (!notify || !wasRevived) return;
            Scheduler.After(0.3f + (broadcast ? 0f : Queue.Interval * targets.Count), () =>
            {
                try
                {
                    if (!broadcast && AfterReviveRestoreClients != null) AfterReviveRestoreClients(targets);
                    else AfterReviveRestore?.Invoke();
                }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Rpc.AfterReviveRestore: {e}"); }
            });
        }

        /// <summary>
        /// Ends an open revive window right now (before a meeting is built, so a dead host is never listed as a living
        /// voter). The caller is expected to re-send names afterwards (the restore Data resets the host's name).
        /// </summary>
        public static void CancelTempRevive()
        {
            try
            {
                if (!_hostRevived && ReviveTargets.Count == 0) { _reviveUntil = -1f; _wasDead = false; return; }
                // Only a window whose restore is still scheduled is live; anything else is stale state.
                bool live = Scheduler.HasTag(ReviveTag);
                Scheduler.Cancel(ReviveTag);
                // urgent restore: the meeting starts in this very frame
                var h = PlayerControl.LocalPlayer;
                var i = h != null ? h.Data : null;
                var targets = new List<int>(ReviveTargets);
                ReviveTargets.Clear();
                _reviveUntil = -1f;
                if (i != null && _hostRevived && _wasDead && live)
                {
                    i.IsDead = true;
                    if (targets.Contains(-1) || targets.Count > 2) SendPlayerInfo(i);
                    else foreach (int id in targets) SendPlayerInfo(i, id);
                }
                else if (_hostRevived || targets.Count > 0)
                {
                    PocketRolesPlugin.Logger.LogWarning($"Rpc.CancelTempRevive: dropped stale revive state (live={live}, wasDead={_wasDead})");
                }
                _hostRevived = false;
                _wasDead = false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Rpc.CancelTempRevive: {e}");
                _hostRevived = false;
                _wasDead = false;
                ReviveTargets.Clear();
            }
        }

        /// <summary>
        /// Drops every temp-revive bookkeeping without touching IsDead (game end / join / disconnect, before
        /// Scheduler.Clear() would silently discard the scheduled restore and leave the flags set for the next game).
        /// </summary>
        internal static void ResetReviveState()
        {
            try
            {
                Scheduler.Cancel(ReviveTag);
                ReviveTargets.Clear();
                _hostRevived = false;
                _wasDead = false;
                _reviveUntil = -1f;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Rpc.ResetReviveState: {e}");
            }
        }

        // ------------------------------------------------------------------ kills

        public static void Kill(PlayerControl killer, PlayerControl target)
        {
            if (killer == null || target == null) return;
            killer.RpcMurderPlayer(target, true);
        }

        public static void FailKill(PlayerControl killer, PlayerControl target)
        {
            if (killer == null || target == null) return;
            killer.RpcMurderPlayer(target, false);
        }

        /// <summary>
        /// Vanilla client: options with KillCooldown×2 → MurderPlayer(FailedProtected, target = killer) only to that client
        /// (the client resets its timer to half of its KillCooldown) → normal options 0.5 s later. Host: SetKillTimer.
        /// </summary>
        public static void ResetKillCooldown(PlayerControl killer, float cooldown)
        {
            if (killer == null) return;
            if (killer.AmOwner)
            {
                killer.SetKillTimer(cooldown);
                return;
            }
            if (CompatBlocked("ResetKillCooldown")) return; // unregistered lobby: no per-client options / RPC
            var client = AmongUsClient.Instance;
            if (client == null) return;
            byte id = killer.PlayerId;
            int clientId = killer.OwnerId;
            OptionsDesync.SendTo(killer, OptionsDesync.BuildFor(id, cooldown * 2f), true);
            var w = client.StartRpcImmediately(killer.NetId, (byte)RpcCalls.MurderPlayer, SendOption.Reliable, clientId);
            w.WriteNetObject(killer);
            w.Write((int)MurderResultFlags.FailedProtected);
            client.FinishRpcImmediately(w);
            Scheduler.After(0.5f, () =>
            {
                var pc = Core.Game.Player(id);
                if (pc == null) return;
                OptionsDesync.SendTo(pc, OptionsDesync.BuildFor(id, cooldown), true);
            });
        }

        public static void ExileSilently(PlayerControl target)
        {
            if (target == null) return;
            if (SafeMode)
            {
                // Unregistered lobby: an Exiled RPC outside a meeting is exactly what the server anti-cheat looks for.
                PocketRolesPlugin.Logger.LogWarning($"Rpc.ExileSilently skipped for player {target.PlayerId} (safe mode)");
                return;
            }
            var client = AmongUsClient.Instance;
            if (client != null)
            {
                var w = client.StartRpcImmediately(target.NetId, (byte)RpcCalls.Exiled, SendOption.Reliable, -1);
                client.FinishRpcImmediately(w);
            }
            try { target.Exiled(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Rpc.ExileSilently local: {e}"); }
        }

        /// <summary>Data(1) message of one NetworkedPlayerInfo to one client (or everyone). Immediate by default, else paced.</summary>
        public static void SendPlayerInfo(NetworkedPlayerInfo info, int clientId = -1, bool urgent = true)
        {
            if (info == null) return;
            var client = AmongUsClient.Instance;
            if (client == null) return;
            if (clientId != -1 && IsLocal(clientId)) return; // the host already holds the data
            if (clientId != -1 && CompatBlocked("SendPlayerInfo")) return; // unregistered lobby: no GameDataTo
            MessageWriter w = null;
            try
            {
                w = MessageWriter.Get(SendOption.Reliable);
                if (clientId == -1)
                {
                    w.StartMessage(5);
                    w.Write(client.GameId);
                }
                else
                {
                    w.StartMessage(6);
                    w.Write(client.GameId);
                    w.WritePacked(clientId);
                }
                WritePlayerInfo(w, info);
                w.EndMessage();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Rpc.SendPlayerInfo: {e}");
                if (w != null) { try { w.Recycle(); } catch { } }
                return;
            }
            if (urgent) SendNow(w);
            else Queue.Enqueue(w);
        }

        /// <summary>Data(1) sub-message of a NetworkedPlayerInfo (full serialization) into an open 5/6 message.</summary>
        private static void WritePlayerInfo(MessageWriter w, NetworkedPlayerInfo info)
        {
            info.SetDirtyBit(uint.MaxValue);
            w.StartMessage(1);
            w.WritePacked(info.NetId);
            info.Serialize(w, false);
            w.EndMessage();
        }

        public static void BootFromVent(PlayerControl pc, int ventId)
        {
            if (pc == null || pc.MyPhysics == null) return;
            pc.MyPhysics.RpcBootFromVent(ventId);
        }

        internal static void SendNow(MessageWriter w)
        {
            if (w == null) return;
            var client = AmongUsClient.Instance;
            try
            {
                if (client != null) client.SendOrDisconnect(w);
            }
            finally
            {
                w.Recycle();
            }
        }

        // ------------------------------------------------------------------ batch

        /// <summary>
        /// Several GameDataTo(6) messages for different clients packed into as few Hazel packets as possible (a
        /// MessageWriter may hold several root messages; a new writer starts once the current one exceeds
        /// <see cref="PacketBytes"/>). Use <see cref="For"/> to get a per-client <see cref="Batch"/>, finish it with
        /// Batch.Send() before starting the next client, then <see cref="Send"/> here: 14 clients → 2–3 packets
        /// instead of 14 SendOrDisconnect calls in one frame.
        /// </summary>
        public sealed class MultiBatch
        {
            /// <summary>Rotation threshold; one more sub-message (≤ ~100 bytes) may follow, staying below the 1200-byte limit.</summary>
            internal static int PacketBytes => SafeMode ? 400 : 900;
            private readonly List<MessageWriter> _done = new List<MessageWriter>();
            private MessageWriter _cur;

            public bool IsEmpty => _cur == null && _done.Count == 0;

            /// <summary>
            /// Batch for one client. ONE PACKET PER CLIENT (2026-09-09, finding #52): a reliable packet carrying GameDataTo
            /// messages for two different clients got the host disconnected ("Hacking") six times out of six with two
            /// clients — paced or not, with or without impostors — and never with a single client. Vanilla never packs
            /// messages for different recipients into one packet and TOHE sends one packet per client, so every client's
            /// root message now starts a fresh writer; the writers still leave through the paced queue.
            /// </summary>
            public Batch For(int clientId)
            {
                if (_cur != null) { _done.Add(_cur); _cur = null; }
                return new Batch(clientId, this);
            }

            internal MessageWriter Writer()
            {
                if (_cur != null && _cur.Length >= PacketBytes)
                {
                    _done.Add(_cur);
                    _cur = null;
                }
                if (_cur == null) _cur = MessageWriter.Get(SendOption.Reliable);
                return _cur;
            }

            public void Send(bool urgent = false)
            {
                if (_cur != null)
                {
                    _done.Add(_cur);
                    _cur = null;
                }
                if (CompatBlocked("MultiBatch.Send"))
                {
                    // Every root message here is a GameDataTo (per-client batches): dropped whole in compat mode.
                    foreach (var w in _done) { try { w.Recycle(); } catch { } }
                    _done.Clear();
                    return;
                }
                foreach (var w in _done)
                {
                    if (urgent) SendNow(w);
                    else Queue.Enqueue(w);
                }
                _done.Clear();
            }
        }

        /// <summary>One GameData(5, clientId = -1) / GameDataTo(6) packet of RPC sub-messages, auto-split at ~500 bytes.</summary>
        public sealed class Batch
        {
            private readonly int _clientId;
            private readonly bool _local;
            private readonly MultiBatch _parent;
            private readonly List<MessageWriter> _done = new List<MessageWriter>();
            private MessageWriter _cur;
            private int _count;

            public Batch(int clientId)
            {
                _clientId = clientId;
                _local = clientId != -1 && IsLocal(clientId);
            }

            internal Batch(int clientId, MultiBatch parent) : this(clientId)
            {
                _parent = parent;
            }

            public bool IsEmpty => _count == 0;

            private void StartRoot(MessageWriter w)
            {
                var client = AmongUsClient.Instance;
                if (_clientId == -1)
                {
                    w.StartMessage(5);
                    w.Write(client.GameId);
                }
                else
                {
                    w.StartMessage(6);
                    w.Write(client.GameId);
                    w.WritePacked(_clientId);
                }
            }

            private MessageWriter Writer()
            {
                // With a parent the writer is shared, so its Length is the whole packet so far.
                int limit = _parent != null ? MultiBatch.PacketBytes : ChunkBytes;
                if (_cur != null && _cur.Length >= limit)
                {
                    _cur.EndMessage();
                    if (_parent == null) _done.Add(_cur);
                    _cur = null;
                }
                if (_cur == null)
                {
                    _cur = _parent != null ? _parent.Writer() : MessageWriter.Get(SendOption.Reliable);
                    StartRoot(_cur);
                }
                return _cur;
            }

            public Batch Rpc(uint netId, RpcCalls call, Action<MessageWriter> payload)
            {
                if (_local) return this; // callers apply host-local effects themselves
                try
                {
                    var w = Writer();
                    w.StartMessage(2);
                    w.WritePacked(netId);
                    w.Write((byte)call);
                    payload?.Invoke(w);
                    w.EndMessage();
                    _count++;
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"Rpc.Batch.Rpc({call}): {e}");
                }
                return this;
            }

            /// <param name="canOverride">The SetRole flag on the wire. false for a client's first assignment (the vanilla
            /// value; 2026.8.18 clients ignore any SetRole after their first one), true for later ghost / GA sends.</param>
            public Batch SetRole(PlayerControl target, RoleTypes role, bool canOverride = true)
            {
                if (target == null) return this;
                if (_local)
                {
                    ApplyRoleLocal(target, role);
                    _count++;
                    return this;
                }
                return Rpc(target.NetId, RpcCalls.SetRole, w =>
                {
                    w.Write((ushort)role);
                    w.Write(canOverride);
                });
            }

            /// <summary>Data(1) sub-message carrying the current state of a NetworkedPlayerInfo (e.g. a temporary IsDead change).</summary>
            public Batch PlayerInfo(NetworkedPlayerInfo info)
            {
                if (info == null || _local) return this;
                try
                {
                    var w = Writer();
                    WritePlayerInfo(w, info);
                    _count++;
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"Rpc.Batch.PlayerInfo: {e}");
                }
                return this;
            }

            public Batch SetName(PlayerControl target, string name)
            {
                if (target == null || name == null || target.Data == null) return this;
                if (_local)
                {
                    ApplyNameLocal(target, name);
                    _count++;
                    return this;
                }
                if (DropSetNameInCompat("Batch.SetName")) return this;
                uint dataNetId = target.Data.NetId;
                return Rpc(target.NetId, RpcCalls.SetName, w =>
                {
                    w.Write(dataNetId);
                    w.Write(name);
                    w.Write(false);
                });
            }

            /// <summary>Sends the packets (or, inside a <see cref="MultiBatch"/>, only closes this client's root message).</summary>
            public void Send(bool urgent = false)
            {
                if (_cur != null)
                {
                    _cur.EndMessage();
                    if (_parent == null) _done.Add(_cur);
                    _cur = null;
                }
                // Compat mode: a GameDataTo(6) batch never leaves (a broadcast batch, clientId -1, is vanilla-shaped and
                // still allowed; a batch inside a MultiBatch is dropped by MultiBatch.Send).
                if (_clientId != -1 && !_local && _parent == null && CompatBlocked("Batch.Send"))
                {
                    foreach (var w in _done) { try { w.Recycle(); } catch { } }
                    _done.Clear();
                    _count = 0;
                    return;
                }
                foreach (var w in _done)
                {
                    if (urgent) SendNow(w);
                    else Queue.Enqueue(w);
                }
                _done.Clear();
                _count = 0;
            }
        }

        // ------------------------------------------------------------------ paced queue

        /// <summary>Sends at most one writer every <see cref="Interval"/> seconds (official-server rate limit).</summary>
        public static class Queue
        {
            /// <summary>
            /// 0.3 s always (2026-09-09): the official server disconnected the host with "Hacking" twice when the
            /// per-client role tables of TWO clients went out in the same frame (one client was fine), and TOHE
            /// spaces its per-client SetRole messages by exactly 0.3 s on official servers ("BypassRateLimitAC").
            /// </summary>
            public static float Interval => 0.3f;
            private static readonly List<MessageWriter> Pending = new List<MessageWriter>();
            private static float _lastSend = -1f;

            public static int Count => Pending.Count;

            public static void Enqueue(MessageWriter w)
            {
                if (w == null) return;
                Pending.Add(w);
            }

            public static void Tick()
            {
                if (Pending.Count == 0) return;
                float now = Time.time;
                if (_lastSend >= 0f && now - _lastSend < Interval) return;
                var w = Pending[0];
                Pending.RemoveAt(0);
                _lastSend = now;
                try
                {
                    SendNow(w);
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"Rpc.Queue.Tick: {e}");
                }
            }

            public static void Clear()
            {
                foreach (var w in Pending)
                {
                    try { w.Recycle(); } catch { }
                }
                Pending.Clear();
                _lastSend = -1f;
            }
        }
    }
}
