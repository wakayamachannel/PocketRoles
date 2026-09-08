using System;
using System.Collections.Generic;
using HarmonyLib;
using Hazel;
using PocketRoles.Core;

namespace PocketRoles.Net
{
    /// <summary>
    /// Host-side anti-cheat. The host never receives its own RPCs through HandleRpc, so any host-only RPC arriving
    /// from another client is forged: drop it, count a strike per client and optionally kick.
    /// </summary>
    public static class AntiCheat
    {
        /// <summary>PlayerControl RPCs that only the host may send.</summary>
        private static readonly HashSet<byte> HostOnlyPlayerRpcs = new HashSet<byte>
        {
            (byte)RpcCalls.SyncSettings,   // 2
            (byte)RpcCalls.SetInfected,    // 3
            (byte)RpcCalls.Exiled,         // 4
            (byte)RpcCalls.SetName,        // 6
            (byte)RpcCalls.MurderPlayer,   // 12
            (byte)RpcCalls.StartMeeting,   // 14
            (byte)RpcCalls.SetTasks,       // 29
            (byte)RpcCalls.SetRole,        // 44
            (byte)RpcCalls.ProtectPlayer,  // 45
            (byte)RpcCalls.Shapeshift,     // 46
            (byte)RpcCalls.StartVanish,    // 63
            (byte)RpcCalls.StartAppear,    // 65
        };

        /// <summary>MeetingHud RPCs that only the host may send.</summary>
        private static readonly HashSet<byte> HostOnlyMeetingRpcs = new HashSet<byte>
        {
            (byte)RpcCalls.CloseMeeting,   // 22
            (byte)RpcCalls.VotingComplete, // 23
        };

        private const int KickStrikes = 3;

        /// <summary>clientId → forged RPC count</summary>
        public static readonly Dictionary<int, int> Strikes = new Dictionary<int, int>();
        private static readonly HashSet<int> Notified = new HashSet<int>();

        public static bool IsHostOnlyPlayerRpc(byte callId) => HostOnlyPlayerRpcs.Contains(callId);
        public static bool IsHostOnlyMeetingRpc(byte callId) => HostOnlyMeetingRpcs.Contains(callId);

        public static void Reset()
        {
            Strikes.Clear();
            Notified.Clear();
        }

        private static bool Enabled()
        {
            var client = AmongUsClient.Instance;
            return client != null && client.AmHost && Options.ModEnabled;
        }

        private static float _lastUnknownSenderLog = -10f;

        /// <summary>A cheating client can flood host-only RPCs; log at most once per second for unattributable ones.</summary>
        internal static bool ShouldLogUnknownSender()
        {
            float now = UnityEngine.Time.time;
            if (now - _lastUnknownSenderLog < 1f) return false;
            _lastUnknownSenderLog = now;
            return true;
        }

        /// <summary>One-shot host-chat notice per targeted player: a forged RPC addressed to them was dropped.</summary>
        internal static void NotifyTargeted(int ownerClientId, string playerName, byte callId)
        {
            if (!Notified.Add(ownerClientId)) return;
            try
            {
                Chat.Chat.Local(Chat.Chat.Title, Lang.TF("anticheat.forged.target",
                    "{0} 宛ての不正な通信(RPC {1})を検出し、無視しました（送信元は特定できません）。",
                    "Forged RPC {1} addressed to {0} was dropped (sender unknown).", playerName, callId));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AntiCheat notify: {e}");
            }
        }

        /// <summary>
        /// Registers a forged RPC from the given client; returns true if the client was kicked. Currently unused: no
        /// inbound RPC identifies its sender, so nothing can attribute a strike to a client (see the HandleRpc patches).
        /// </summary>
        internal static bool Strike(int clientId, string playerName, byte callId, string where)
        {
            Strikes.TryGetValue(clientId, out int n);
            n++;
            Strikes[clientId] = n;
            // first 3 strikes, then every 100th: a flood of forged RPCs must not become a per-frame log write
            if (n <= 3 || n % 100 == 0)
                PocketRolesPlugin.Logger.LogWarning($"AntiCheat: dropped forged {where} RPC {callId} from '{playerName}' (client {clientId}, strike {n})");

            if (Notified.Add(clientId))
            {
                try
                {
                    Chat.Chat.Local(Chat.Chat.Title, Lang.TF("anticheat.forged",
                        "{0} から不正な通信(RPC {1})を検出し、無視しました。",
                        "Forged RPC {1} from {0} was dropped.", playerName, callId));
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"AntiCheat notify: {e}");
                }
            }

            if (Options.AntiCheatKick && n >= KickStrikes)
            {
                var client = AmongUsClient.Instance;
                if (client != null && clientId != client.ClientId)
                {
                    PocketRolesPlugin.Logger.LogWarning($"AntiCheat: kicking client {clientId} ('{playerName}') after {n} forged RPCs");
                    try
                    {
                        client.KickPlayer(clientId, false);
                        Chat.Chat.Local(Chat.Chat.Title, Lang.TF("anticheat.kick",
                            "{0} を不正行為でキックしました。",
                            "Kicked {0} for forged RPCs.", playerName));
                    }
                    catch (Exception e)
                    {
                        PocketRolesPlugin.Logger.LogError($"AntiCheat kick: {e}");
                    }
                    return true;
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
    internal static class AntiCheat_PlayerControlHandleRpcPatch
    {
        private static bool Prefix(PlayerControl __instance, byte callId, MessageReader reader)
        {
            try
            {
                if (__instance == null) return true;
                if (!AntiCheat.IsHostOnlyPlayerRpc(callId)) return true;
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || !Options.ModEnabled) return true;
                // The host never receives its own RPCs through HandleRpc, so a host-only RPC arriving on the host's
                // own PlayerControl was forged by some client on our net id: drop it (sender unknown, no strike).
                int owner = __instance.OwnerId;
                if (__instance.AmOwner || owner == client.ClientId)
                {
                    if (AntiCheat.ShouldLogUnknownSender())
                        PocketRolesPlugin.Logger.LogWarning($"AntiCheat: dropped forged RPC {callId} addressed to the host's own player (sender unknown)");
                    return false;
                }
                // A GameData RPC carries no sender id and a cheater can address one to ANY player's net object, so the
                // owner of the targeted PlayerControl is the victim, not the sender: drop it, never strike / kick the owner.
                string name = __instance.Data != null ? __instance.Data.PlayerName : ("#" + __instance.PlayerId);
                if (AntiCheat.ShouldLogUnknownSender())
                    PocketRolesPlugin.Logger.LogWarning($"AntiCheat: dropped forged PlayerControl RPC {callId} addressed to '{name}' (client {owner}, sender unknown)");
                AntiCheat.NotifyTargeted(owner, name, callId);
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AntiCheat_PlayerControlHandleRpcPatch: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.HandleRpc))]
    internal static class AntiCheat_MeetingHudHandleRpcPatch
    {
        private static bool Prefix(MeetingHud __instance, byte callId, MessageReader reader)
        {
            try
            {
                if (!AntiCheat.IsHostOnlyMeetingRpc(callId)) return true;
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || !Options.ModEnabled) return true;
                // The sender is not identifiable here (MeetingHud is host-owned); just drop and log (rate-limited).
                if (AntiCheat.ShouldLogUnknownSender())
                    PocketRolesPlugin.Logger.LogWarning($"AntiCheat: dropped forged MeetingHud RPC {callId} (sender unknown)");
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AntiCheat_MeetingHudHandleRpcPatch: {e}");
                return true;
            }
        }
    }

    /// <summary>Strike counters are per lobby.</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class AntiCheat_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            try { AntiCheat.Reset(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"AntiCheat_OnGameJoinedPatch: {e}"); }
        }
    }
}
