using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using HarmonyLib;
using Hazel;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using InnerNet;
using PocketRoles.Core;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.5 server / lag log (host log analysis 2026-09-21: the laggy 18-minute game ran on a far game server, the
    /// host's client.Ping read 73 ms on an 11 ms server and 85 ms on a 73 ms one, per-player lag could not be told from
    /// the log and the game server's address was logged nowhere). Always on and independent of [Diagnostics] WireLog; fed
    /// by the same two wire hooks (WireLog_SendOrDisconnectPatch / WireLog_HandleMessagePatch):
    /// <list type="bullet">
    /// <item>Wire RTT: the host's SEND of HostGame / HostModdedGame / JoinGame / StartGame to the server's reply
    /// (HostGame, JoinedGame, StartGame), Stopwatch time. The lobby value (<see cref="LobbyRttMs"/>) is the lower of the
    /// HostGame and the first JoinGame sample; the play-again rejoin and each StartGame add their own samples.</item>
    /// <item>Server line, once per lobby (not again on the play-again rejoin): address:port, region, lobby code, wire RTT,
    /// near / far (<see cref="FarRttMs"/>) and client.Ping for comparison; plus a host-only chat line for a lobby the host
    /// created ("サーバー: 近い（11 ms）").</item>
    /// <item>In a running game (host): every 30 s one line with the host's client.Ping and, per remote player, the
    /// unreliable movement packets received (attributed through the CustomNetworkTransform net id) and the one-frame
    /// jumps the Aegis speed check skipped; at the game start (first SetRole SEND) and at each meeting start
    /// (StartMeeting SEND) the delay from that SEND to each client's SnapTo reply, with the median and the slow ones.</item>
    /// </list>
    /// Threads: SEND runs on the main thread, RECV on Hazel's receive thread (the RECV stamps in the wire log are a few
    /// ms apart inside one frame), so the network side only stores timestamps, counters and net ids under
    /// <see cref="Gate"/> in preallocated arrays / dictionaries; players, names and log text are resolved on the main
    /// thread (<see cref="Tick"/>, throttled to 4 per second).
    /// </summary>
    public static class LagLog
    {
        /// <summary>Wire RTT above this (ms) is a far game server (2026-09-21: 11 ms near; 73 / 144 / 146 ms far).</summary>
        public const int FarRttMs = 40;

        private const double SummaryInterval = 30.0;   // s, in-game summary line
        private const double MinFinalWindow = 5.0;     // s, a shorter last window at the game end is not logged
        private const double ProbeWindow = 8.0;        // s, SnapTo replies collected after a game / meeting start
        private const double PendingTimeout = 10.0;    // s, an unanswered HostGame / JoinGame / StartGame is dropped
        private const double ServerPingWait = 6.0;     // s, the server line waits this long for a first client.Ping
        private const double LobbyLineTimeout = 20.0;  // s, the host chat line gives up when the chat never comes up
        private const double FarAdviceInterval = 600.0; // s, the "leave and create again" advice at most this often (ban points)
        private const double TickInterval = 0.25;      // s
        private const int SlowMinMs = 300;             // a slow reply is above 2x the median and above this
        private const int MaxSends = 64, MaxReplies = 48, MaxNets = 64, MaxPlayers = 32;

        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;
        private static readonly object Gate = new object();

        // ---- root tags (InnerNet.Tags, resolved once on the main thread; the long-standing numbers until then)
        private static byte _tHostGame = 0, _tJoinGame = 1, _tStartGame = 2, _tGameData = 5, _tGameDataTo = 6, _tJoinedGame = 7, _tHostModded = 255;
        private static bool _tagsResolved;
        private const byte CallSnapTo = (byte)RpcCalls.SnapTo, CallStartMeeting = (byte)RpcCalls.StartMeeting, CallSetRole = (byte)RpcCalls.SetRole;

        // ---- wire RTT (under Gate)
        private static long _hostSentAt, _joinSentAt, _startSentAt;   // Stopwatch ticks, 0 = nothing pending
        private static int _rttGameId = int.MinValue;                 // lobby the samples below belong to
        private static int _hostRtt = -1, _joinRtt = -1, _rejoinRtt = -1, _startRtt = -1;
        private static volatile bool _rttPending;

        // ---- server line / host chat line (main thread)
        private static int _serverGameId = int.MinValue;
        private static bool _serverPending;
        private static double _serverJoinedAt;
        private static string _srvAddress = "", _srvEndpoint = "", _srvRegion = "", _srvCode = "";
        private static int _srvPort, _srvHostRtt = -1, _srvJoinRtt = -1;
        private static bool _srvHost;
        private static bool _lobbyLinePending;
        private static int _lobbyLineGameId;
        private static double _lobbyLineSince;
        private static double _farAdviceAt = -1.0;   // NowSec() of the last far line with the re-creation advice; < 0 = never

        // ---- in-game counters (under Gate: the receive thread counts movement, AegisMore counts jumps)
        private static volatile bool _counting;
        private static readonly Dictionary<uint, int> MovesByNet = new Dictionary<uint, int>(MaxNets);
        private static readonly Dictionary<int, int> JumpsByClient = new Dictionary<int, int>(MaxPlayers);
        private static int _movesOverflow;
        /// <summary>Set by AegisMore.Tick on every frame the speed check runs (unregistered games): the jump counts mean something.</summary>
        internal static bool SpeedCheckSeen;
        private static bool _running;
        private static double _gameStartedAt, _windowStartedAt, _nextSummaryAt, _nextTickAt;

        // ---- SnapTo response probe (under Gate)
        private enum Probe { None, GameStart, Meeting }
        private static Probe _probe = Probe.None;
        private static volatile bool _probeArmed;
        private static bool _startProbePending;                        // StartGame sent: the first SetRole SEND arms the game-start probe
        private static long _probeT0;
        private static readonly int[] SendTarget = new int[MaxSends];  // -1 = broadcast (GameData), else the GameDataTo client
        private static readonly long[] SendAt = new long[MaxSends];
        private static int _sendN;
        private static readonly uint[] ReplyNet = new uint[MaxReplies];
        private static readonly long[] ReplyAt = new long[MaxReplies];
        private static int _replyN;

        // ---- main-thread scratch (summary / probe close; reused)
        private static readonly int[] CSendTarget = new int[MaxSends];
        private static readonly long[] CSendAt = new long[MaxSends];
        private static readonly uint[] CReplyNet = new uint[MaxReplies];
        private static readonly long[] CReplyAt = new long[MaxReplies];
        private static readonly uint[] SnapNet = new uint[MaxNets];
        private static readonly int[] SnapMoves = new int[MaxNets];
        private static readonly bool[] SnapUsed = new bool[MaxNets];
        private static readonly int[] SnapJumpClient = new int[MaxPlayers];
        private static readonly int[] SnapJumps = new int[MaxPlayers];
        private static readonly string[] PName = new string[MaxPlayers];
        private static readonly int[] PClient = new int[MaxPlayers];
        private static readonly uint[] PNet = new uint[MaxPlayers];
        private static readonly bool[] PExpected = new bool[MaxPlayers];
        private static readonly bool[] PDead = new bool[MaxPlayers];
        private static readonly bool[] PGone = new bool[MaxPlayers];
        private static readonly int[] PDelay = new int[MaxPlayers];
        private static readonly int[] Sorted = new int[MaxPlayers];

        private static int _errors;

        // ------------------------------------------------------------------ public API

        /// <summary>Wire RTT of the current lobby in ms (the lower of its HostGame and first JoinGame samples); -1 when unknown.</summary>
        public static int LobbyRttMs
        {
            get { lock (Gate) return MinKnown(_hostRtt, _joinRtt); }
        }

        /// <summary>A wire RTT of <paramref name="rttMs"/> means a far game server (above <see cref="FarRttMs"/>, the same "above" as [Lobby] PingRecreateLimit).</summary>
        public static bool IsFar(int rttMs) => rttMs > FarRttMs;

        /// <summary>
        /// Log / diag verdict with the limit on the side that applies ("FAR (over 40 ms)" / "near (40 ms or less)"); ""
        /// without a sample. The old "near (…; far above 40 ms)" read as if the near server were far.
        /// </summary>
        private static string Verdict(int rttMs)
        {
            if (rttMs <= 0) return "";
            return IsFar(rttMs) ? "FAR (over " + FarRttMs + " ms)" : "near (" + FarRttMs + " ms or less)";
        }

        /// <summary>/diag: the game server of the current (or last) online lobby, its wire RTT and near / far.</summary>
        public static string DiagLine()
        {
            var sb = new StringBuilder(256);
            try
            {
                if (_serverGameId == int.MinValue) return "no online lobby joined yet";
                var client = AmongUsClient.Instance;
                bool inLobby = client != null && client.NetworkMode == NetworkModes.OnlineGame
                    && client.GameState != InnerNetClient.GameStates.NotJoined && client.GameId == _serverGameId;
                int host, join, rejoin, start;
                lock (Gate) { host = _hostRtt; join = _joinRtt; rejoin = _rejoinRtt; start = _startRtt; }
                if (!inLobby) { host = _srvHostRtt; join = _srvJoinRtt; rejoin = -1; start = -1; }
                int rtt = MinKnown(host, join);
                sb.Append(_srvAddress).Append(':').Append(_srvPort);
                if (!string.IsNullOrEmpty(_srvEndpoint) && !_srvEndpoint.StartsWith(_srvAddress, StringComparison.Ordinal))
                    sb.Append(" (endpoint ").Append(_srvEndpoint).Append(')');
                sb.Append(" region=").Append(_srvRegion).Append(" lobby=").Append(_srvCode).Append(_srvHost ? " (host)" : "");
                if (!inLobby) sb.Append(" [left]");
                sb.Append("\nwireRTT=").Append(Fmt(rtt));
                if (rtt > 0) sb.Append(' ').Append(Verdict(rtt));
                sb.Append(" (HostGame ").Append(Fmt(host)).Append(", JoinGame ").Append(Fmt(join))
                  .Append(", rejoin ").Append(Fmt(rejoin)).Append(", StartGame ").Append(Fmt(start)).Append(')');
                if (inLobby) sb.Append(" client.Ping=").Append(client.Ping).Append(" ms");
                if (_running) sb.Append(" lagLog=game +").Append(Clock(NowSec() - _gameStartedAt));
            }
            catch (Exception e)
            {
                sb.Append(" [error: ").Append(e.Message).Append(']');
            }
            return sb.ToString();
        }

        /// <summary>AegisMore.Tick: a one-frame step above the snap limit (snap / teleport / lag catch-up) for this client.</summary>
        internal static void OnJump(int clientId)
        {
            if (!_counting) return;
            lock (Gate)
            {
                if (JumpsByClient.TryGetValue(clientId, out int c)) JumpsByClient[clientId] = c + 1;
                else if (JumpsByClient.Count < MaxPlayers) JumpsByClient[clientId] = 1;
            }
        }

        // ------------------------------------------------------------------ wire hooks

        /// <summary>SendOrDisconnect prefix (main thread): reliable packets only, root tags and the RPC call ids in GameData.</summary>
        internal static void OnSend(MessageWriter msg)
        {
            try
            {
                if (msg == null) return;
                if (!_tagsResolved) ResolveTags();
                int len = msg.Length;
                if (len < 6) return;
                var b = msg.Buffer;
                if (b == null || b[0] != 1) return;   // 1 = reliable (option + 2-byte id); unreliable packets are movement
                int n = Math.Min(len, b.Length);
                long now = Stopwatch.GetTimestamp();
                int pos = 3;
                for (int roots = 0; pos + 3 <= n && roots < 16; roots++)
                {
                    int body = pos + 3;
                    int end = Math.Min(n, body + (b[pos] | (b[pos + 1] << 8)));
                    byte tag = b[pos + 2];
                    if (tag == _tGameData || tag == _tGameDataTo)
                    {
                        if (_running || _startProbePending || _probeArmed) SentGameData(b, body, end, tag == _tGameDataTo, now);
                    }
                    else if (tag == _tHostGame || tag == _tHostModded) HostSent(now);
                    else if (tag == _tJoinGame) JoinSent(ReadInt32(b, body, end), now);
                    else if (tag == _tStartGame) StartSent(now);
                    pos = end;
                }
            }
            catch (Exception e) { Error("send", e); }
        }

        /// <summary>HandleMessage prefix (receive thread): one root message, body = Buffer[Offset..Offset+Length).</summary>
        internal static void OnRecv(MessageReader reader, SendOption option)
        {
            try
            {
                if (reader == null) return;
                bool counting = _counting, probe = _probeArmed, rtt = _rttPending;
                if (!counting && !probe && !rtt) return;
                byte tag = reader.Tag;
                if (option != SendOption.Reliable)
                {
                    if (counting && tag == _tGameData) CountMoves(reader);
                    return;
                }
                if (tag == _tGameData || tag == _tGameDataTo)
                {
                    if (probe) ScanReplies(reader, tag == _tGameDataTo);
                    return;
                }
                if (rtt) Reply(tag, reader);
            }
            catch (Exception e) { Error("recv", e); }
        }

        // ------------------------------------------------------------------ send side

        private static void SentGameData(Il2CppStructArray<byte> b, int pos, int end, bool to, long now)
        {
            pos += 4;   // game id
            int target = to ? ReadPacked(b, ref pos, end) : -1;
            for (int guard = 0; pos + 3 <= end && guard < 64; guard++)
            {
                int body = pos + 3;
                int iend = Math.Min(end, body + (b[pos] | (b[pos + 1] << 8)));
                if (b[pos + 2] == 2 && body < iend)   // RPC: packed net id, call id
                {
                    int p = body;
                    ReadPacked(b, ref p, iend);
                    if (p < iend)
                    {
                        byte call = b[p];
                        if (call == CallSetRole) RoleSent(target, now);
                        else if (call == CallStartMeeting) MeetingSent(now);
                    }
                }
                pos = iend;
            }
        }

        private static void HostSent(long now)
        {
            lock (Gate)
            {
                _hostSentAt = now;
                _hostRtt = _joinRtt = _rejoinRtt = _startRtt = -1;
                _rttGameId = int.MinValue;
                _rttPending = true;
            }
        }

        private static void JoinSent(int gameId, long now)
        {
            lock (Gate)
            {
                // A different lobby starts a new sample set; the same id is the join right after HostGame or the play-again rejoin.
                if (gameId != _rttGameId) { _hostRtt = _joinRtt = _rejoinRtt = _startRtt = -1; _rttGameId = gameId; }
                _joinSentAt = now;
                _rttPending = true;
            }
        }

        private static void StartSent(long now)
        {
            lock (Gate)
            {
                _startSentAt = now;
                _rttPending = true;
                _startProbePending = true;
            }
        }

        private static void RoleSent(int target, long now)
        {
            lock (Gate)
            {
                if (_startProbePending)
                {
                    _startProbePending = false;
                    if (_probe == Probe.None) Arm(Probe.GameStart, now);
                }
                if (_probe != Probe.GameStart || _sendN >= MaxSends) return;
                if (_sendN > 0 && SendTarget[_sendN - 1] == target && SendAt[_sendN - 1] == now) return;   // same packet
                SendTarget[_sendN] = target;
                SendAt[_sendN] = now;
                _sendN++;
            }
        }

        private static void MeetingSent(long now)
        {
            lock (Gate)
            {
                if (_probe != Probe.None) return;   // still collecting the previous event
                Arm(Probe.Meeting, now);
                SendTarget[0] = -1;
                SendAt[0] = now;
                _sendN = 1;
            }
        }

        private static void Arm(Probe kind, long now)
        {
            _probe = kind;
            _probeT0 = now;
            _sendN = 0;
            _replyN = 0;
            _probeArmed = true;
        }

        // ------------------------------------------------------------------ receive side

        private static void CountMoves(MessageReader reader)
        {
            var b = reader.Buffer;
            if (b == null) return;
            int pos = reader.Offset;
            int end = Math.Min(b.Length, pos + reader.Length);
            pos += 4;   // game id
            lock (Gate)
            {
                if (!_counting) return;
                for (int guard = 0; pos + 3 <= end && guard < 64; guard++)
                {
                    int body = pos + 3;
                    int iend = Math.Min(end, body + (b[pos] | (b[pos + 1] << 8)));
                    if (b[pos + 2] == 1 && body < iend)   // Data: packed net id, then the object's payload
                    {
                        int p = body;
                        uint net = (uint)ReadPacked(b, ref p, iend);
                        if (MovesByNet.TryGetValue(net, out int c)) MovesByNet[net] = c + 1;
                        else if (MovesByNet.Count < MaxNets) MovesByNet[net] = 1;
                        else _movesOverflow++;
                    }
                    pos = iend;
                }
            }
        }

        private static void ScanReplies(MessageReader reader, bool to)
        {
            var b = reader.Buffer;
            if (b == null) return;
            int pos = reader.Offset;
            int end = Math.Min(b.Length, pos + reader.Length);
            pos += 4;   // game id
            if (to) ReadPacked(b, ref pos, end);
            long now = Stopwatch.GetTimestamp();
            for (int guard = 0; pos + 3 <= end && guard < 64; guard++)
            {
                int body = pos + 3;
                int iend = Math.Min(end, body + (b[pos] | (b[pos + 1] << 8)));
                if (b[pos + 2] == 2 && body < iend)
                {
                    int p = body;
                    uint net = (uint)ReadPacked(b, ref p, iend);
                    if (p < iend && b[p] == CallSnapTo) AddReply(net, now);
                }
                pos = iend;
            }
        }

        private static void AddReply(uint net, long now)
        {
            lock (Gate)
            {
                if (_probe == Probe.None || _replyN >= MaxReplies) return;
                for (int i = 0; i < _replyN; i++) if (ReplyNet[i] == net) return;   // the first SnapTo per player only
                ReplyNet[_replyN] = net;
                ReplyAt[_replyN] = now;
                _replyN++;
            }
        }

        private static void Reply(byte tag, MessageReader reader)
        {
            long now = Stopwatch.GetTimestamp();
            long stale = (long)(PendingTimeout * Stopwatch.Frequency);
            lock (Gate)
            {
                if (_hostSentAt != 0 && now - _hostSentAt > stale) _hostSentAt = 0;
                if (_joinSentAt != 0 && now - _joinSentAt > stale) _joinSentAt = 0;
                if (_startSentAt != 0 && now - _startSentAt > stale) _startSentAt = 0;
                if ((tag == _tHostGame || tag == _tHostModded) && _hostSentAt != 0)
                {
                    _hostRtt = Ms(now - _hostSentAt);
                    _hostSentAt = 0;
                    var b = reader.Buffer;   // body = the new lobby's game id
                    if (b != null) _rttGameId = ReadInt32(b, reader.Offset, Math.Min(b.Length, reader.Offset + reader.Length));
                }
                else if (tag == _tJoinedGame && _joinSentAt != 0)
                {
                    int rtt = Ms(now - _joinSentAt);
                    if (_joinRtt < 0) _joinRtt = rtt; else _rejoinRtt = rtt;
                    _joinSentAt = 0;
                }
                else if (tag == _tStartGame && _startSentAt != 0)
                {
                    _startRtt = Ms(now - _startSentAt);
                    _startSentAt = 0;
                }
                _rttPending = _hostSentAt != 0 || _joinSentAt != 0 || _startSentAt != 0;
            }
        }

        // ------------------------------------------------------------------ lobby joined (main thread)

        /// <summary>AmongUsClient.OnGameJoined postfix: remembers the server of a new lobby; the lines follow from <see cref="Tick"/>.</summary>
        internal static void OnGameJoined()
        {
            var client = AmongUsClient.Instance;
            if (client == null) return;
            bool same = Core.Game.JoinedSameLobby();   // frame-cached: the same answer as every other OnGameJoined postfix
            lock (Gate) _startProbePending = false;    // a StartGame whose game never sent roles (haison) arms nothing later
            if (client.NetworkMode != NetworkModes.OnlineGame) return;
            int gameId = client.GameId;
            if (same || gameId == _serverGameId)
            {
                int rejoin;
                lock (Gate) rejoin = _rejoinRtt;
                PocketRolesPlugin.Logger.LogInfo($"LagLog: {WireLog.Stamp()} rejoined lobby {Lobby.Rehost.CurrentRoomCode()} after the game (JoinGame RTT {Fmt(rejoin)})");
                return;
            }
            if (_serverPending) ServerLine(null, NowSec());   // the previous lobby's line was still waiting for a ping
            _serverGameId = gameId;
            _srvAddress = "?";
            _srvPort = 0;
            _srvEndpoint = "";
            try { _srvAddress = client.networkAddress ?? "?"; _srvPort = client.networkPort; } catch (Exception) { }
            try
            {
                var conn = client.connection;
                var ep = conn != null ? conn.EndPoint : null;
                if (ep != null) _srvEndpoint = ep.ToString() ?? "";
            }
            catch (Exception) { }
            _srvRegion = Lobby.AutoRegion.CurrentRegionName();
            _srvCode = Lobby.Rehost.CurrentRoomCode();
            _srvHost = client.AmHost;
            lock (Gate) { _srvHostRtt = _hostRtt; _srvJoinRtt = _joinRtt; }
            _serverJoinedAt = NowSec();
            _serverPending = true;
            _lobbyLinePending = _srvHost;   // a lobby the host created (or was given as host): the chat line once
            _lobbyLineGameId = gameId;
            _lobbyLineSince = _serverJoinedAt;
        }

        /// <summary>The per-lobby server line, once client.Ping has a first value (or after <see cref="ServerPingWait"/> s / the lobby is gone).</summary>
        private static void ServerLine(AmongUsClient client, double now)
        {
            int ping = 0;
            bool inLobby = false;
            try
            {
                inLobby = client != null && client.GameId == _serverGameId && client.GameState != InnerNetClient.GameStates.NotJoined;
                if (inLobby) ping = client.Ping;
            }
            catch (Exception) { }
            double waited = now - _serverJoinedAt;
            if (inLobby && ping <= 0 && waited < ServerPingWait) return;
            _serverPending = false;
            int rtt = MinKnown(_srvHostRtt, _srvJoinRtt);
            var sb = new StringBuilder(320);
            sb.Append("LagLog: ").Append(WireLog.Stamp()).Append(" server: lobby ").Append(_srvCode).Append(_srvHost ? " (host)" : " (joined)")
              .Append(" on ").Append(_srvAddress).Append(':').Append(_srvPort);
            if (!string.IsNullOrEmpty(_srvEndpoint) && !_srvEndpoint.StartsWith(_srvAddress, StringComparison.Ordinal))
                sb.Append(" (endpoint ").Append(_srvEndpoint).Append(')');
            sb.Append(" region '").Append(_srvRegion).Append("' | wire RTT ").Append(Fmt(rtt));
            if (rtt > 0) sb.Append(" = ").Append(Verdict(rtt));
            sb.Append(" (HostGame ").Append(Fmt(_srvHostRtt)).Append(", JoinGame ").Append(Fmt(_srvJoinRtt)).Append(')')
              .Append(" | client.Ping ").Append(ping > 0 ? ping + " ms" : "not measured").Append(" after ").Append(waited.ToString("0.0")).Append(" s");
            if (!inLobby) sb.Append(" (lobby already left)");
            PocketRolesPlugin.Logger.LogInfo(sb.ToString());
        }

        /// <summary>Host-only chat line for a lobby the host created: near / far with the wire RTT (local, never broadcast).</summary>
        private static void LobbyLine(AmongUsClient client, double now)
        {
            try
            {
                if (client == null || !client.AmHost || client.GameId != _lobbyLineGameId || client.GameState != InnerNetClient.GameStates.Joined)
                {
                    _lobbyLinePending = false;
                    PocketRolesPlugin.Logger.LogInfo("LagLog: server chat line skipped (lobby left, game started or not the host)");
                    return;
                }
                if (now - _lobbyLineSince > LobbyLineTimeout)
                {
                    _lobbyLinePending = false;
                    PocketRolesPlugin.Logger.LogInfo("LagLog: server chat line skipped (the chat never came up)");
                    return;
                }
                if (PlayerControl.LocalPlayer == null || !HudManager.InstanceExists) return;
                var hud = HudManager.Instance;
                if (hud == null || hud.Chat == null) return;
                _lobbyLinePending = false;
                int rtt = MinKnown(_srvHostRtt, _srvJoinRtt);
                if (rtt <= 0)
                {
                    PocketRolesPlugin.Logger.LogInfo("LagLog: no wire RTT for this lobby, no server chat line");
                    return;
                }
                bool far = IsFar(rtt);
                // v0.5.5 review: the re-creation advice carries the re-host prompt's caution (short-lived lobbies count as
                // deliberate disconnects: ban points, then a temporary create-game restriction) and comes at most once per
                // FarAdviceInterval; another far lobby within that time gets the plain near / far line
                bool advise = far && (_farAdviceAt < 0 || now - _farAdviceAt >= FarAdviceInterval);
                string text;
                if (advise)
                {
                    _farAdviceAt = now;
                    text = TF3("lag.server.far",
                        "サーバー: 遠い（{0} ms）。一度退出して部屋を作り直すと、近いサーバーになることがあります。",
                        "Server: far ({0} ms). Leaving and creating the lobby again may give a nearer server.",
                        "服务器：远（{0} ms）。退出后重新创建房间，可能会分到更近的服务器。", rtt)
                        + "\n" + Lang.T("rehost.prompt.note",
                        "※ 短時間に何度も部屋を作り直すと「意図的な切断」と見なされ、部屋作成が一時制限されます。",
                        "Note: re-creating lobbies repeatedly in a short time counts as deliberate disconnects and temporarily blocks lobby creation.",
                        "注意：短时间内反复重建房间会被视为故意断线，并暂时限制创建房间。");
                }
                else if (far)
                    text = TF3("lag.server.far.plain",
                        "サーバー: 遠い（{0} ms）",
                        "Server: far ({0} ms)",
                        "服务器：远（{0} ms）", rtt);
                else
                    text = TF3("lag.server.near",
                        "サーバー: 近い（{0} ms）",
                        "Server: near ({0} ms)",
                        "服务器：近（{0} ms）", rtt);
                Chat.Chat.Local(Chat.Chat.Title, text);
                PocketRolesPlugin.Logger.LogInfo($"LagLog: server chat line shown to the host ({(advise ? "far, with the re-creation advice" : far ? "far, advice already shown within 10 min" : "near")}, {rtt} ms)");
            }
            catch (Exception e)
            {
                _lobbyLinePending = false;
                PocketRolesPlugin.Logger.LogWarning($"LagLog: server chat line failed: {e.Message}");
            }
        }

        // ------------------------------------------------------------------ tick (AmongUsClient.Update, main thread)

        internal static void Tick()
        {
            double now = NowSec();
            if (now < _nextTickAt) return;
            _nextTickAt = now + TickInterval;
            try
            {
                if (!_tagsResolved) ResolveTags();
                var client = AmongUsClient.Instance;
                if (_serverPending) ServerLine(client, now);
                if (_lobbyLinePending) LobbyLine(client, now);
                GameTick(client, now);
            }
            catch (Exception e) { Error("tick", e); }
        }

        private static void GameTick(AmongUsClient client, double now)
        {
            bool running = false;
            try { running = client != null && client.AmHost && client.NetworkMode == NetworkModes.OnlineGame && client.IsGameStarted; }
            catch (Exception) { }

            if (running && !_running)
            {
                _running = true;
                _gameStartedAt = now;
                _windowStartedAt = now;
                _nextSummaryAt = now + SummaryInterval;
                SpeedCheckSeen = false;
                lock (Gate) { MovesByNet.Clear(); JumpsByClient.Clear(); _movesOverflow = 0; }
                _counting = true;
                return;
            }
            if (!running)
            {
                if (_running)
                {
                    // game over (or left): the last partial window and a probe that was still collecting
                    if (ProbeOpen()) CloseProbe(client);
                    if (now - _windowStartedAt >= MinFinalWindow) Summary(client, now, true);
                    _running = false;
                    _counting = false;
                    lock (Gate) { MovesByNet.Clear(); JumpsByClient.Clear(); _movesOverflow = 0; _startProbePending = false; }
                }
                return;
            }
            if (ProbeOpen() && ProbeAge() >= ProbeWindow) CloseProbe(client);
            if (now >= _nextSummaryAt)
            {
                Summary(client, now, false);
                _nextSummaryAt = now + SummaryInterval;
            }
        }

        private static bool ProbeOpen()
        {
            lock (Gate) return _probe != Probe.None;
        }

        private static double ProbeAge()
        {
            long t0;
            lock (Gate) t0 = _probeT0;
            return (Stopwatch.GetTimestamp() - t0) * TicksToMs / 1000.0;
        }

        /// <summary>One line per 30 s: host ping, then per remote player "name(c&lt;clientId&gt;) movementPackets[/jumps]".</summary>
        private static void Summary(AmongUsClient client, double now, bool final)
        {
            int n = 0, m = 0, overflow;
            lock (Gate)
            {
                foreach (var kv in MovesByNet)
                {
                    if (n >= MaxNets) break;
                    SnapNet[n] = kv.Key; SnapMoves[n] = kv.Value; SnapUsed[n] = false; n++;
                }
                foreach (var kv in JumpsByClient)
                {
                    if (m >= MaxPlayers) break;
                    SnapJumpClient[m] = kv.Key; SnapJumps[m] = kv.Value; m++;
                }
                overflow = _movesOverflow;
                MovesByNet.Clear();
                JumpsByClient.Clear();
                _movesOverflow = 0;
            }
            bool jumps = SpeedCheckSeen;
            SpeedCheckSeen = false;
            double window = now - _windowStartedAt;
            _windowStartedAt = now;

            int ping = -1;
            try { if (client != null) ping = client.Ping; } catch (Exception) { }
            int total = overflow;
            for (int i = 0; i < n; i++) total += SnapMoves[i];

            var sb = new StringBuilder(768);
            sb.Append("LagLog: ").Append(WireLog.Stamp()).Append(final ? " game end" : " game").Append(" +").Append(Clock(now - _gameStartedAt))
              .Append(" (last ").Append(window.ToString("0")).Append(" s): host ping ").Append(ping).Append(" ms");
            try { if (MeetingHud.Instance != null) sb.Append(" [meeting]"); } catch (Exception) { }
            sb.Append(" | movement packets ").Append(total).Append(jumps ? ", per player packets/jumps:" : " (jumps not measured: speed check off), per player:");
            try
            {
                var all = PlayerControl.AllPlayerControls;
                int count = all != null ? all.Count : 0;
                for (int i = 0; i < count; i++)
                {
                    var pc = all[i];
                    if (pc == null || pc.AmOwner) continue;
                    var d = pc.Data;
                    uint net = 0;
                    try { var nt = pc.NetTransform; if (nt != null) net = nt.NetId; } catch (Exception) { }
                    int cid = pc.OwnerId;
                    int mv = 0;
                    for (int k = 0; k < n; k++) if (SnapNet[k] == net && !SnapUsed[k]) { mv = SnapMoves[k]; SnapUsed[k] = true; break; }
                    int jp = 0;
                    for (int k = 0; k < m; k++) if (SnapJumpClient[k] == cid) { jp = SnapJumps[k]; break; }
                    sb.Append(' ').Append(Core.Game.NameOf(pc.PlayerId)).Append("(c").Append(cid).Append(") ").Append(mv);
                    if (jumps) sb.Append('/').Append(jp);
                    if (d == null || d.Disconnected) sb.Append(" left");
                    else if (d.IsDead) sb.Append(" dead");
                    sb.Append(',');
                }
            }
            catch (Exception e) { sb.Append(" [players: ").Append(e.Message).Append(']'); }
            int other = overflow;
            for (int k = 0; k < n; k++) if (!SnapUsed[k]) other += SnapMoves[k];
            if (other > 0) sb.Append(" other ").Append(other);
            if (sb[sb.Length - 1] == ',') sb.Length--;
            PocketRolesPlugin.Logger.LogInfo(sb.ToString());
        }

        /// <summary>
        /// Game start / meeting start: per player the delay from the host's SetRole / StartMeeting SEND (for a per-client
        /// role message: the last one addressed to that client, or broadcast, before its reply) to its first SnapTo RECV.
        /// Two lines: median, slow players (above 2x the median and above 300 ms), no reply; then every delay.
        /// </summary>
        private static void CloseProbe(AmongUsClient client)
        {
            Probe kind;
            long t0;
            int sn, rn;
            lock (Gate)
            {
                kind = _probe;
                t0 = _probeT0;
                sn = _sendN;
                rn = _replyN;
                Array.Copy(SendTarget, CSendTarget, sn);
                Array.Copy(SendAt, CSendAt, sn);
                Array.Copy(ReplyNet, CReplyNet, rn);
                Array.Copy(ReplyAt, CReplyAt, rn);
                _probe = Probe.None;
                _probeArmed = false;
                _sendN = 0;
                _replyN = 0;
            }
            if (kind == Probe.None) return;
            try
            {
                int pn = 0, expected = 0;
                var all = PlayerControl.AllPlayerControls;
                int count = all != null ? all.Count : 0;
                for (int i = 0; i < count && pn < MaxPlayers; i++)
                {
                    var pc = all[i];
                    if (pc == null || pc.AmOwner) continue;
                    var d = pc.Data;
                    uint net = 0;
                    try { var nt = pc.NetTransform; if (nt != null) net = nt.NetId; } catch (Exception) { }
                    PName[pn] = Core.Game.NameOf(pc.PlayerId);
                    PClient[pn] = pc.OwnerId;
                    PNet[pn] = net;
                    PGone[pn] = d == null || d.Disconnected;
                    PDead[pn] = d != null && d.IsDead;
                    PExpected[pn] = !PGone[pn] && (kind == Probe.GameStart || !PDead[pn]);
                    PDelay[pn] = -1;
                    if (PExpected[pn]) expected++;
                    pn++;
                }
                int other = 0;
                for (int r = 0; r < rn; r++)
                {
                    int idx = -1;
                    for (int p = 0; p < pn; p++) if (PNet[p] == CReplyNet[r]) { idx = p; break; }
                    if (idx < 0) { other++; continue; }
                    long start = t0;
                    for (int s = 0; s < sn; s++)
                        if ((CSendTarget[s] == -1 || CSendTarget[s] == PClient[idx]) && CSendAt[s] <= CReplyAt[r] && CSendAt[s] > start) start = CSendAt[s];
                    PDelay[idx] = Ms(CReplyAt[r] - start);
                }
                int dn = 0;
                for (int p = 0; p < pn; p++) if (PDelay[p] >= 0) Sorted[dn++] = PDelay[p];
                if (dn == 0 && expected == 0) return;   // nobody else in the game (solo test)
                Array.Sort(Sorted, 0, dn);
                int median = dn == 0 ? -1 : (dn % 2 == 1 ? Sorted[dn / 2] : (Sorted[dn / 2 - 1] + Sorted[dn / 2]) / 2);
                int replied = 0;
                for (int p = 0; p < pn; p++) if (PExpected[p] && PDelay[p] >= 0) replied++;

                int ping = -1, startRtt;
                try { if (client != null) ping = client.Ping; } catch (Exception) { }
                lock (Gate) startRtt = _startRtt;
                string what = kind == Probe.GameStart ? "game start (SetRole -> SnapTo)" : "meeting start (StartMeeting -> SnapTo)";
                var sb = new StringBuilder(512);
                sb.Append("LagLog: ").Append(WireLog.Stamp()).Append(' ').Append(what).Append(": ").Append(replied).Append('/').Append(expected)
                  .Append(" replied within ").Append(ProbeWindow.ToString("0")).Append(" s, median ").Append(Fmt(median)).Append("; slow:");
                int slow = 0;
                for (int p = 0; p < pn; p++)
                {
                    if (PDelay[p] < 0 || median <= 0 || PDelay[p] <= 2 * median || PDelay[p] <= SlowMinMs) continue;
                    sb.Append(slow++ > 0 ? ", " : " ").Append(PName[p]).Append("(c").Append(PClient[p]).Append(") ").Append(PDelay[p]).Append(" ms");
                }
                if (slow == 0) sb.Append(" none");
                sb.Append("; no reply:");
                int missing = 0;
                for (int p = 0; p < pn; p++)
                {
                    if (!PExpected[p] || PDelay[p] >= 0) continue;
                    sb.Append(missing++ > 0 ? ", " : " ").Append(PName[p]).Append("(c").Append(PClient[p]).Append(')');
                }
                if (missing == 0) sb.Append(" none");
                if (other > 0) sb.Append("; ").Append(other).Append(" reply(s) from unknown objects");
                sb.Append(" | host ping ").Append(ping).Append(" ms");
                if (kind == Probe.GameStart) sb.Append(", StartGame RTT ").Append(Fmt(startRtt));
                PocketRolesPlugin.Logger.LogInfo(sb.ToString());

                if (dn == 0) return;
                // every delay, slowest first (n <= 32: a plain selection is enough)
                sb.Clear();
                sb.Append("LagLog:   delays (ms):");
                for (int k = dn - 1; k >= 0; k--)
                {
                    for (int p = 0; p < pn; p++)
                    {
                        if (PDelay[p] != Sorted[k]) continue;
                        sb.Append(' ').Append(PName[p]).Append(PDead[p] ? "(dead)" : "").Append(' ').Append(PDelay[p]).Append(',');
                        PDelay[p] = -2;   // printed
                        break;
                    }
                }
                if (sb[sb.Length - 1] == ',') sb.Length--;
                PocketRolesPlugin.Logger.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"LagLog: {kind} probe summary failed: {e.Message}");
            }
        }

        // ------------------------------------------------------------------ helpers

        private static void ResolveTags()
        {
            _tagsResolved = true;
            try
            {
                _tHostGame = Tags.HostGame;
                _tJoinGame = Tags.JoinGame;
                _tStartGame = Tags.StartGame;
                _tGameData = Tags.GameData;
                _tGameDataTo = Tags.GameDataTo;
                _tJoinedGame = Tags.JoinedGame;
                _tHostModded = Tags.HostModdedGame;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"LagLog: Tags unavailable ({e.Message}), using the built-in tag numbers");
            }
        }

        private static double NowSec() => Stopwatch.GetTimestamp() * TicksToMs / 1000.0;

        private static int Ms(long ticks) => Math.Max(1, (int)Math.Round(ticks * TicksToMs));

        private static int MinKnown(int a, int b) => a < 0 ? b : (b < 0 ? a : Math.Min(a, b));

        private static string Fmt(int ms) => ms > 0 ? ms + " ms" : "-";

        private static string Clock(double seconds)
        {
            int s = Math.Max(0, (int)seconds);
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        private static int ReadPacked(Il2CppStructArray<byte> b, ref int pos, int end)
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

        private static int ReadInt32(Il2CppStructArray<byte> b, int pos, int end)
        {
            if (pos < 0 || pos + 4 > end) return int.MinValue;
            return b[pos] | (b[pos + 1] << 8) | (b[pos + 2] << 16) | (b[pos + 3] << 24);
        }

        /// <summary>Text with an inline zh fallback (the JSON tables win when they carry the key).</summary>
        private static string TF3(string key, string ja, string en, string zh, params object[] args)
        {
            string text = Lang.T(key, ja, en, zh);
            try { return string.Format(text, args ?? Array.Empty<object>()); }
            catch (FormatException) { try { return string.Format(ja, args ?? Array.Empty<object>()); } catch (FormatException) { return text; } }
        }

        /// <summary>The wire hooks run for every packet: only the first few errors are logged.</summary>
        private static void Error(string where, Exception e)
        {
            if (Interlocked.Increment(ref _errors) > 5) return;
            try { PocketRolesPlugin.Logger.LogWarning($"LagLog {where}: {e.Message}"); } catch (Exception) { }
        }
    }

    // ---------------------------------------------------------------------- patches

    /// <summary>Server line / host chat line / in-game summaries and probe close (the net client ticks in every scene).</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.Update))]
    internal static class LagLog_ClientUpdatePatch
    {
        private static void Postfix()
        {
            try { LagLog.Tick(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"LagLog_ClientUpdatePatch: {e.Message}"); }
        }
    }

    /// <summary>New lobby: capture the game server (address, region, code, wire RTT); not again on the play-again rejoin.</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    [HarmonyPriority(Priority.Last)]
    internal static class LagLog_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            try { LagLog.OnGameJoined(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"LagLog_OnGameJoinedPatch: {e}"); }
        }
    }
}
