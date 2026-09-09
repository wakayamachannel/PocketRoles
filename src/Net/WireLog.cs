using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Hazel;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;
using PocketRoles.Core;

namespace PocketRoles.Net
{
    /// <summary>
    /// Investigation aid ([Diagnostics] WireLog): every packet this client hands to the connection
    /// (InnerNetClient.SendOrDisconnect) and every root message it receives (InnerNetClient.HandleMessage), decoded one
    /// level deep (GameData / GameDataTo → Data / RPC / Spawn / SceneChange / Ready …), and every disconnect
    /// (HandleDisconnect) with the last unreliable packets. One "Wire:" line per packet; unreliable Data-only
    /// packets (movement) are only counted. Nothing here changes behaviour; off by default.
    /// </summary>
    public static class WireLog
    {
        private static bool Enabled => Options.WireLog;

        private static readonly Dictionary<byte, string> RootTags = new Dictionary<byte, string>();
        private static bool _tagsLoaded;
        private static readonly string[] InnerTags = { "?", "Data", "RPC", "?", "Spawn", "Despawn", "SceneChange", "Ready", "ChangeSettings", "ConsoleDeclareClientPlatform", "ClientInfo", "ReportPlayer", "SetClientReady", "ClientInfoV2" };

        private static readonly Queue<string> _unreliable = new Queue<string>();
        private static int _unreliableSends, _unreliableRecvs;
        private static float _nextSummary;

        private static void Load()
        {
            if (_tagsLoaded) return;
            _tagsLoaded = true;
            try
            {
                foreach (var p in typeof(Tags).GetProperties(BindingFlags.Public | BindingFlags.Static))
                {
                    if (p.PropertyType != typeof(byte)) continue;
                    try { RootTags[(byte)p.GetValue(null)] = p.Name; } catch (Exception) { }
                }
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"WireLog: Tags: {e.Message}"); }
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("HH:mm:ss.fff") + " " + UnityEngine.Time.realtimeSinceStartup.ToString("0.000") + "s";
        }

        private static void Log(string line)
        {
            try { PocketRolesPlugin.Logger.LogInfo("Wire: " + line); } catch (Exception) { }
        }

        private static void Remember(string line)
        {
            lock (_unreliable)
            {
                _unreliable.Enqueue(line);
                while (_unreliable.Count > 6) _unreliable.Dequeue();
            }
        }

        private static byte[] Copy(Il2CppStructArray<byte> arr, int off, int len)
        {
            if (arr == null || len <= 0) return new byte[0];
            int n = Math.Min(len, arr.Length - off);
            if (n <= 0) return new byte[0];
            var b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = arr[off + i];
            return b;
        }

        // ------------------------------------------------------------------ hooks

        /// <summary>SendOrDisconnect prefix: the writer holds the Hazel header and 1..n root messages.</summary>
        internal static void OnSend(MessageWriter msg)
        {
            if (!Enabled || msg == null) return;
            Load();
            byte[] b = Copy(msg.Buffer, 0, msg.Length);
            if (b.Length == 0) { Log($"{Stamp()} SEND empty"); return; }
            int pos;
            string kind;
            if (b[0] == 1 && b.Length >= 3) { kind = "R"; pos = 3; }
            else if (b[0] == 0) { kind = "U"; pos = 1; }
            else { kind = "H" + b[0]; pos = 1; }
            var sb = new StringBuilder();
            bool dataOnly = true;
            int roots = 0;
            while (pos + 3 <= b.Length)
            {
                int len = b[pos] | (b[pos + 1] << 8);
                byte tag = b[pos + 2];
                int body = pos + 3;
                int end = Math.Min(b.Length, body + len);
                if (roots > 0) sb.Append(" || ");
                sb.Append(DescribeRoot(tag, b, body, end, ref dataOnly));
                roots++;
                pos = end;
            }
            string line = $"{Stamp()} SEND {kind} len={b.Length} {sb}";
            if (kind == "U" && dataOnly)
            {
                _unreliableSends++;
                Remember(line);
                Summary();
                return;
            }
            Log(line + (b.Length <= 200 ? " hex=" + Hex(b, 0, b.Length) : " hex(0..120)=" + Hex(b, 0, 120)));
        }

        /// <summary>HandleMessage prefix: one root message (reader.Tag) whose body is Buffer[Offset..Offset+Length).</summary>
        internal static void OnRecv(MessageReader reader, SendOption option)
        {
            if (!Enabled || reader == null) return;
            Load();
            byte tag = reader.Tag;
            int off = reader.Offset, len = reader.Length;
            byte[] b = Copy(reader.Buffer, off, len);
            bool dataOnly = true;
            string desc = DescribeRoot(tag, b, 0, b.Length, ref dataOnly);
            string line = $"{Stamp()} RECV {(option == SendOption.Reliable ? "R" : "U")} len={b.Length} {desc}";
            if (option != SendOption.Reliable && dataOnly)
            {
                _unreliableRecvs++;
                Remember(line);
                Summary();
                return;
            }
            Log(line + (b.Length <= 160 ? " hex=" + Hex(b, 0, b.Length) : " hex(0..96)=" + Hex(b, 0, 96)));
        }

        internal static void OnDisconnect(DisconnectReasons reason, string stringReason)
        {
            if (!Enabled) return;
            Log($"{Stamp()} DISCONNECT reason={reason}({(int)reason}) text='{stringReason ?? "null"}' (unreliable so far: sent {_unreliableSends}, received {_unreliableRecvs})");
            string[] last;
            lock (_unreliable) last = _unreliable.ToArray();
            foreach (var l in last) Log("  last unreliable | " + l);
        }

        internal static void OnPlayerJoined(AmongUsClient client, ClientData data)
        {
            if (!Enabled || client == null || data == null) return;
            string name = "?";
            try { name = data.PlayerName; } catch (Exception) { }
            Log($"{Stamp()} OnPlayerJoined client={data.Id} name='{name}' inScene={data.InScene} state={client.GameState} amHost={client.AmHost} players={client.allClients?.Count}");
        }

        private static void Summary()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < _nextSummary) return;
            _nextSummary = now + 5f;
            Log($"{Stamp()} unreliable Data-only packets so far: sent {_unreliableSends}, received {_unreliableRecvs}");
        }

        // ------------------------------------------------------------------ decoding

        private static string RootName(byte tag)
        {
            return RootTags.TryGetValue(tag, out var n) ? n : ("Tag" + tag);
        }

        private static string DescribeRoot(byte tag, byte[] b, int pos, int end, ref bool dataOnly)
        {
            var sb = new StringBuilder();
            sb.Append(RootName(tag)).Append('(').Append(tag).Append(')');
            try
            {
                string name = RootName(tag);
                if (name == "GameData" || name == "GameDataTo" || name == "PackedGameDataTo")
                {
                    if (pos + 4 <= end)
                    {
                        int gameId = b[pos] | (b[pos + 1] << 8) | (b[pos + 2] << 16) | (b[pos + 3] << 24);
                        pos += 4;
                        sb.Append(" game=").Append(gameId);
                    }
                    if (name != "GameData")
                    {
                        int target = ReadPacked(b, ref pos, end);
                        sb.Append(" to=").Append(target);
                    }
                    sb.Append(" [");
                    int n = 0;
                    while (pos + 3 <= end)
                    {
                        int len = b[pos] | (b[pos + 1] << 8);
                        byte itag = b[pos + 2];
                        int body = pos + 3;
                        int iend = Math.Min(end, body + len);
                        if (n++ > 0) sb.Append("; ");
                        sb.Append(DescribeInner(itag, b, body, iend));
                        if (itag != 1) dataOnly = false;
                        pos = iend;
                    }
                    sb.Append(']');
                }
                else
                {
                    dataOnly = false;
                    sb.Append(" body=").Append(end - pos).Append('B');
                    if (name == "JoinGame" || name == "JoinedGame" || name == "RemovePlayer" || name == "KickPlayer" || name == "AlterGame" || name == "StartGame" || name == "EndGame")
                        sb.Append(' ').Append(Hex(b, pos, Math.Min(end - pos, 48)));
                }
            }
            catch (Exception e)
            {
                sb.Append(" decode-error ").Append(e.Message);
            }
            return sb.ToString();
        }

        private static string DescribeInner(byte tag, byte[] b, int pos, int end)
        {
            string name = tag < InnerTags.Length ? InnerTags[tag] : "?";
            var sb = new StringBuilder();
            sb.Append(name).Append('(').Append(tag).Append(')');
            int start = pos;
            switch (tag)
            {
                case 1: // Data
                {
                    int net = ReadPacked(b, ref pos, end);
                    sb.Append(" net=").Append(net).Append(Owner(net)).Append(' ').Append(end - pos).Append('B');
                    break;
                }
                case 2: // RPC
                {
                    int net = ReadPacked(b, ref pos, end);
                    int call = pos < end ? b[pos++] : -1;
                    string rpcName;
                    try { rpcName = ((RpcCalls)call).ToString(); } catch (Exception) { rpcName = "?"; }
                    sb.Append(" net=").Append(net).Append(Owner(net)).Append(" call=").Append(call).Append('/').Append(rpcName).Append(' ').Append(end - pos).Append('B');
                    if (call == (int)RpcCalls.SendChat || call == (int)RpcCalls.SetName || call == (int)RpcCalls.CheckName)
                        sb.Append(" '").Append(ReadString(b, ref pos, end)).Append('\'');
                    else if (call == (int)RpcCalls.SetRole || call == (int)RpcCalls.SetColor || call == (int)RpcCalls.CheckColor || call == (int)RpcCalls.SetLevel)
                        sb.Append(' ').Append(Hex(b, pos, Math.Min(end - pos, 8)));
                    break;
                }
                case 4: // Spawn
                {
                    int spawnId = ReadPacked(b, ref pos, end);
                    int owner = ReadPacked(b, ref pos, end);
                    int flags = pos < end ? b[pos++] : -1;
                    int count = ReadPacked(b, ref pos, end);
                    sb.Append(" prefab=").Append(spawnId).Append('/').Append(PrefabName(spawnId)).Append(" owner=").Append(owner).Append(" flags=").Append(flags).Append(" comps=").Append(count).Append('{');
                    for (int i = 0; i < count && pos < end; i++)
                    {
                        int net = ReadPacked(b, ref pos, end);
                        int len = pos + 2 <= end ? (b[pos] | (b[pos + 1] << 8)) : 0;
                        int ctag = pos + 2 < end ? b[pos + 2] : -1;
                        int body = pos + 3;
                        if (i > 0) sb.Append(", ");
                        sb.Append("net ").Append(net).Append(':').Append(len).Append('B');
                        if (ctag != 1) sb.Append("(tag ").Append(ctag).Append(')');
                        pos = Math.Min(end, body + len);
                    }
                    sb.Append('}');
                    break;
                }
                case 5: // Despawn
                {
                    int net = ReadPacked(b, ref pos, end);
                    sb.Append(" net=").Append(net);
                    break;
                }
                case 6: // SceneChange
                {
                    int cid = ReadPacked(b, ref pos, end);
                    sb.Append(" client=").Append(cid).Append(" scene='").Append(ReadString(b, ref pos, end)).Append('\'');
                    break;
                }
                case 7: // Ready
                {
                    int cid = ReadPacked(b, ref pos, end);
                    sb.Append(" client=").Append(cid);
                    break;
                }
                default:
                    sb.Append(' ').Append(end - pos).Append("B ").Append(Hex(b, pos, Math.Min(end - pos, 24)));
                    break;
            }
            return sb.ToString();
        }

        private static string Owner(int netId)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null) return "";
                var obj = client.FindObjectByNetId<InnerNetObject>((uint)netId);
                if (obj == null) return "";
                var pc = obj.TryCast<PlayerControl>();
                if (pc != null) return $"(PC#{pc.PlayerId}/c{pc.OwnerId})";
                return "(" + obj.GetIl2CppType().Name + "/c" + obj.OwnerId + ")";
            }
            catch (Exception) { return ""; }
        }

        private static readonly string[] Prefabs = { "ShipStatus", "MeetingHud", "LobbyBehaviour", "GameData", "PlayerControl", "MiraShip", "PolusShip", "DleksShip", "Airship", "Fungle" };

        private static string PrefabName(int spawnId)
        {
            return spawnId >= 0 && spawnId < Prefabs.Length ? Prefabs[spawnId] : "?";
        }

        private static int ReadPacked(byte[] b, ref int pos, int end)
        {
            int result = 0, shift = 0;
            while (pos < end && shift < 35)
            {
                byte v = b[pos++];
                result |= (v & 0x7F) << shift;
                shift += 7;
                if ((v & 0x80) == 0) break;
            }
            return result;
        }

        private static string ReadString(byte[] b, ref int pos, int end)
        {
            int len = ReadPacked(b, ref pos, end);
            int n = Math.Max(0, Math.Min(len, end - pos));
            string s;
            try { s = Encoding.UTF8.GetString(b, pos, n); } catch (Exception) { s = "?"; }
            pos += n;
            if (s.Length > 60) s = s.Substring(0, 60) + "…";
            return s.Replace('\n', '⏎');
        }

        private static string Hex(byte[] b, int pos, int n)
        {
            var sb = new StringBuilder(n * 2);
            for (int i = 0; i < n && pos + i < b.Length; i++) sb.Append(b[pos + i].ToString("x2"));
            return sb.ToString();
        }
    }

    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.SendOrDisconnect))]
    internal static class WireLog_SendOrDisconnectPatch
    {
        private static void Prefix(MessageWriter msg)
        {
            try { WireLog.OnSend(msg); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"WireLog send: {e.Message}"); }
        }
    }

    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.HandleMessage))]
    internal static class WireLog_HandleMessagePatch
    {
        private static void Prefix(MessageReader reader, SendOption sendOption)
        {
            try { WireLog.OnRecv(reader, sendOption); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"WireLog recv: {e.Message}"); }
        }
    }

    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.HandleDisconnect))]
    internal static class WireLog_HandleDisconnectPatch
    {
        private static void Prefix(DisconnectReasons reason, string stringReason)
        {
            try { WireLog.OnDisconnect(reason, stringReason); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"WireLog disconnect: {e.Message}"); }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined))]
    internal static class WireLog_OnPlayerJoinedPatch
    {
        private static void Prefix(AmongUsClient __instance, ClientData data)
        {
            try { WireLog.OnPlayerJoined(__instance, data); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"WireLog join: {e.Message}"); }
        }
    }
}
