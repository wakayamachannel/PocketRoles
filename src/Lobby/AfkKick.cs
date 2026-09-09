using System;
using System.Collections.Generic;
using HarmonyLib;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Lobby
{
    using Game = PocketRoles.Core.Game;

    /// <summary>
    /// [Lobby] AfkKickMinutes (v0.4.6, 0 = off): a player who has neither moved nor chatted in the LOBBY for that many
    /// minutes gets one warning 30 s before the limit and is then kicked (never banned). Host side only, so it works
    /// in unregistered (compat) lobbies too — a kick is a host prerogative and the warning is ordinary chat (a public
    /// "name: …" line in compat mode). Never touches the host, VIPs / moderators / admins (Permissions), anyone while the
    /// start countdown runs, or anything once the game has started. A player id reused by a newcomer starts fresh.
    /// </summary>
    public static class AfkKick
    {
        private const float PollInterval = 2f;
        private const float WarnBefore = 30f;
        private const float MoveEpsilon = 0.05f;

        private sealed class Track
        {
            public int ClientId;
            public Vector2 Pos;
            public float LastActiveAt;
            public bool Warned;
        }

        private static readonly Dictionary<byte, Track> _tracks = new Dictionary<byte, Track>();
        private static float _nextPoll;

        /// <summary>Chat counts as activity (Chat_AddChatPatch).</summary>
        internal static void Touch(byte playerId)
        {
            try
            {
                if (_tracks.TryGetValue(playerId, out var t)) { t.LastActiveAt = Time.time; t.Warned = false; }
            }
            catch (Exception) { }
        }

        /// <summary>Host tick (Plugin_TickPatch, 0.5 Hz): tracks positions in the online lobby, warns, kicks.</summary>
        internal static void Tick()
        {
            try
            {
                if (Time.time < _nextPoll) return;
                _nextPoll = Time.time + PollInterval;
                int minutes = Options.AfkKickMinutes;
                if (minutes <= 0 || !Game.IsHostActive || !LobbyTimer.InOnlineLobby())
                {
                    if (_tracks.Count > 0) _tracks.Clear();
                    return;
                }
                var client = AmongUsClient.Instance;
                if (client == null || client.IsGameStarted) { _tracks.Clear(); return; }
                bool countdown = false;
                try
                {
                    var gsm = GameStartManager.Instance;
                    countdown = gsm != null && gsm.startState != GameStartManager.StartingStates.NotStarting;
                }
                catch (Exception) { }
                float now = Time.time;
                float limit = minutes * 60f;
                var seen = new HashSet<byte>();
                foreach (var pc in Game.AllPlayers())
                {
                    if (pc.AmOwner || pc.Data == null || pc.Data.Disconnected) continue;
                    byte id = pc.PlayerId;
                    seen.Add(id);
                    Vector2 pos;
                    try { pos = pc.GetTruePosition(); }
                    catch (Exception) { continue; }
                    if (!_tracks.TryGetValue(id, out var t) || t.ClientId != pc.OwnerId)
                    {
                        _tracks[id] = new Track { ClientId = pc.OwnerId, Pos = pos, LastActiveAt = now };
                        continue;
                    }
                    if ((pos - t.Pos).sqrMagnitude > MoveEpsilon * MoveEpsilon)
                    {
                        t.Pos = pos;
                        t.LastActiveAt = now;
                        t.Warned = false;
                        continue;
                    }
                    if (countdown) continue; // waiting for the start is not being away
                    float idle = now - t.LastActiveAt;
                    if (idle < limit - WarnBefore) continue;
                    if (Permissions.LevelOf(id) != PermLevel.Player) continue; // VIP / moderator / admin are exempt
                    if (!t.Warned)
                    {
                        t.Warned = true;
                        PocketRolesPlugin.Logger.LogInfo($"AfkKick: {Game.NameOf(id)} idle {idle:0}s → warning");
                        Kills_NoticeShim(id, "afk.warn",
                            "{0}秒以内に動くか発言しないと退出になります（AFK）。",
                            "Move or chat within {0} s or you will be removed (AFK).",
                            "{0} 秒内不移动或发言将被移出（挂机）。", (int)WarnBefore);
                        continue;
                    }
                    if (idle < limit) continue;
                    string name = Lang.StripTags(Game.NameOf(id) ?? "").Trim();
                    PocketRolesPlugin.Logger.LogInfo($"AfkKick: {name} idle {idle:0}s ≥ {limit:0}s → kick (client {pc.OwnerId})");
                    _tracks.Remove(id);
                    try { client.KickPlayer(pc.OwnerId, false); }
                    catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AfkKick: KickPlayer failed: {e.Message}"); continue; }
                    Chat.Chat.All(Chat.Chat.Title, () => string.Format(Lang.T("afk.kicked",
                        "{0} は {1} 分間動かなかったため退出になりました（AFK）。",
                        "{0} was removed after {1} min without moving (AFK).",
                        "{0} {1} 分钟未移动，已被移出（挂机）。"), name, minutes));
                }
                if (_tracks.Count > seen.Count)
                {
                    var gone = new List<byte>();
                    foreach (var k in _tracks.Keys) if (!seen.Contains(k)) gone.Add(k);
                    foreach (var k in gone) _tracks.Remove(k);
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AfkKick.Tick: {e}");
            }
        }

        /// <summary>Private notice in the player's language (public "name: …" line in compat mode, like every private text there).</summary>
        private static void Kills_NoticeShim(byte id, string key, string ja, string en, string zh, params object[] args)
        {
            string text;
            using (Lang.Scope(Lang.PlayerLang(id)))
            {
                try { text = string.Format(Lang.T(key, ja, en, zh), args); }
                catch (FormatException) { text = Lang.T(key, ja, en, zh); }
            }
            Chat.Chat.To(id, Chat.Chat.Title, text);
        }

        internal static void Clear()
        {
            _tracks.Clear();
        }
    }

    /// <summary>New lobby / disconnect: forget the tracks (ids are reused by the next lobby's players).</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class AfkKick_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            try { AfkKick.Clear(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"AfkKick_OnGameJoined: {e}"); }
        }
    }
}
