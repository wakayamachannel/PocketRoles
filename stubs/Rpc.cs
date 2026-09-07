// STUB (excluded from the real build) — contract only, see DESIGN.md §3
using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using Hazel;
namespace PocketRoles.Net
{
    public static class Rpc
    {
        public static int HostClientId => AmongUsClient.Instance.ClientId;
        public static bool IsLocal(int clientId) => clientId == HostClientId;
        public static int ClientIdOf(PlayerControl pc) => pc.OwnerId;
        public static IEnumerable<int> AllClientIds(bool includeHost) => new int[0];
        public static void SetRoleTo(PlayerControl target, RoleTypes role, int clientId) { }
        public static void SetRoleAll(PlayerControl target, RoleTypes role) { }
        public static void SetNameTo(PlayerControl target, string name, int clientId) { }
        public static void SetNameAll(PlayerControl target, string name) { }
        public static void SendChatTo(int clientId, string title, string text) { }
        public static void SendChatAll(string title, string text) { }
        public static void Kill(PlayerControl killer, PlayerControl target) { }
        public static void FailKill(PlayerControl killer, PlayerControl target) { }
        public static void ResetKillCooldown(PlayerControl killer, float cooldown) { }
        public static void ExileSilently(PlayerControl target) { }
        public static void SendPlayerInfo(NetworkedPlayerInfo info, int clientId = -1) { }
        public static void BootFromVent(PlayerControl pc, int ventId) { }
        public static void TempReviveHostForChat(Action sendChat) { sendChat?.Invoke(); }
        /// <summary>viewer clientId → name that viewer should see for the host (set by NameTags; default = host's real name)</summary>
        public static Func<int, string> HostNameProvider;
        /// <summary>invoked after a dead-host chat revive/restore cycle (NameTags hooks RefreshAll(force) here)</summary>
        public static Action AfterReviveRestore;
        public sealed class Batch
        {
            public Batch(int clientId) { }
            public Batch Rpc(uint netId, RpcCalls call, Action<MessageWriter> payload) => this;
            public Batch SetRole(PlayerControl target, RoleTypes role) => this;
            public Batch SetName(PlayerControl target, string name) => this;
            public void Send(bool urgent = false) { }
            public bool IsEmpty => true;
        }
        public static class Queue
        {
            public static float Interval = 0.1f;
            public static void Enqueue(MessageWriter w) { }
            public static void Tick() { }
            public static void Clear() { }
        }
    }
}
