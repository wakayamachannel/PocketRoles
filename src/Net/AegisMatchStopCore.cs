using System;
using System.Collections.Generic;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.5 AegisMatchStop (owner decision 2026-09-22 「いいよ」: "like Vanguard ending a match when a cheater is caught"):
    /// the rules of the automatic match stop, with no Unity, IL2CPP or BepInEx in them (System.* only) so a headless test
    /// project can link this file. <see cref="AegisMatchStop"/> applies them on the host.
    ///
    /// A match is stopped only when Aegis REMOVED a player during a running game of an unregistered (compat) lobby for a
    /// CERTAIN rule of <see cref="StopRules"/> whose action visibly changed the game for everyone (a kill that landed, a
    /// vent entry, a shapeshift / vanish / appear broadcast; a request only the host sees, a failed kill or an impostor's
    /// task changes nothing and is never a stop). At most once per match and <see cref="MaxStopsPerHour"/> real stops an hour
    /// over every lobby of this session (a GameData RPC carries no sender id: a spoofing cheater could frame someone and
    /// stays in the room; the caps bound it, and a rehosted lobby keeps the count so following the host does not reset it).
    /// </summary>
    internal static class AegisMatchStopCore
    {
        /// <summary>
        /// What the detected action did (CheatDetector's KickItem): only <see cref="Effect.Visible"/> may stop a match.
        /// KillPending: a MurderPlayer whose target was alive, until vanilla has applied it (a MurderPlayer postfix turns it
        /// into Visible when the target is dead after it, else KillNotLanded; never confirmed = KillNotLanded).
        /// </summary>
        internal enum Effect { Visible, KillPending, KillNotLanded, ImpostorTask, HostOnlyRequest }

        /// <summary>What the second lobby line (for the players who came back after the first one) does this frame.</summary>
        internal enum Repeat { Send, Wait, Drop }

        /// <summary>
        /// Why a removal does (Stop) or does not stop the match; the first reason that applies wins (order of <see cref="Decide"/>).
        /// Error: never from <see cref="Decide"/> (AegisMatchStop's wrapper when something threw: an error never stops a match).
        /// </summary>
        internal enum Verdict
        {
            Stop, Off, NoAutoKick, NotStopRule, NotCertain, NoEffect, FileOff, Registered, NotInGame, AlreadyEnding,
            OncePerMatch, UnbanGrace, HourlyCap, Error,
        }

        /// <summary>What to do with a decided stop this frame: end now, wait for a safe screen, or give up (the host is told to use the haison).</summary>
        internal enum Phase { Now, Wait, GiveUp }

        /// <summary>The nameless public line in the lobby: send it now, wait, or drop it (a new game started, or nobody came back).</summary>
        internal enum Line { Send, Wait, Drop }

        /// <summary>Real stops (not /ac test) within <see cref="HourSeconds"/>, over every lobby of this session (review: a rehost changes the code).</summary>
        internal const int MaxStopsPerHour = 3;
        internal const float HourSeconds = 3600f;
        /// <summary>Seconds between the removal and the end (the kick and the host's line go out first).</summary>
        internal const float KickGap = 1.0f;
        /// <summary>Seconds after an intro / meeting / exile screen closed before the end is sent (WrapUp has run by then).</summary>
        internal const float AfterPause = 0.5f;
        /// <summary>No safe moment this many seconds after the decision: no automatic end (the host is told to use the haison).</summary>
        internal const float GiveUpAfter = 30f;
        /// <summary>The end was sent but the game still runs after this many seconds: the vanilla game goes on, the haison works again.</summary>
        internal const float EndConfirm = 8f;
        /// <summary>A game that ends later than this after the decision (the stop failed / gave up) gets no "we ended this match" line.</summary>
        internal const float LineArm = 60f;
        /// <summary>The line waits for this share of the players who were in the match (rounded up, at least 1) …</summary>
        internal const float LineShare = 0.6f;
        /// <summary>… and this many seconds after the first of them came back (the others arrive in a burst).</summary>
        internal const float LineSettle = 3f;
        /// <summary>At least one player back (for <see cref="LineSettle"/>) and the host this long in the lobby: send anyway.</summary>
        internal const float LineMaxWait = 12f;
        /// <summary>Nobody came back this long after the host: the line reaches no one, drop it.</summary>
        internal const float LineDropAfter = 45f;
        /// <summary>
        /// Review 2026-09-22: the line goes out once and never reaches a player who comes back later, so it is sent a second
        /// time (at most; a compat lobby can only send public lines) for the players of the match who came back after it:
        /// once all of them are back, or this many seconds after the first line …
        /// </summary>
        internal const float RepeatAfter = 20f;
        /// <summary>… each <see cref="LineSettle"/> after the last arrival; given up this long after the first line (or when the next game starts).</summary>
        internal const float RepeatDropAfter = 60f;

        /// <summary>The CERTAIN rules that may stop a match (an allow list: a new rule never stops a match by accident).</summary>
        internal static readonly string[] StopRules = { "KillRole", "VentRole", "AbilityRole", "TaskImpostor" };

        internal static bool IsStopRule(string rule)
        {
            if (string.IsNullOrEmpty(rule)) return false;
            foreach (var r in StopRules) if (string.Equals(r, rule, StringComparison.Ordinal)) return true;
            return false;
        }

        internal struct StopInput
        {
            /// <summary>[AntiCheat] EndGameOnCheat, [AntiCheat] AutoKick.</summary>
            public bool FeatureOn, AutoKick;
            /// <summary>The detection saw the action change the game for everyone (see the class summary).</summary>
            public bool VisibleEffect;
            /// <summary>The definitions file turned the stop off for this rule ([rules] endgame = off / endgame.&lt;rule&gt; = off).</summary>
            public bool FileOff;
            /// <summary>Unregistered lobby; a game with this match's role snapshot is running; an end is already going out.</summary>
            public bool Compat, InGame, Ending;
            /// <summary>/ac test (no unban grace, no hourly cap, never counted).</summary>
            public bool Simulated;
            public bool StoppedThisMatch;
            /// <summary>The player's ban for this rule was lifted centrally less than 30 days ago (central-unban).</summary>
            public bool UnbanGrace;
            /// <summary>CheatDetector.Rule name; the level when the removal was queued and the level in use now ("Certain" …).</summary>
            public string Rule, QueuedLevel, EffectiveLevel;
            /// <summary>Real stops within the last hour (every lobby of this session).</summary>
            public int StopsLastHour;
        }

        /// <summary>Stop only when every condition holds; otherwise the first one that fails (in this order).</summary>
        internal static Verdict Decide(StopInput i)
        {
            if (!i.FeatureOn) return Verdict.Off;
            if (!i.AutoKick) return Verdict.NoAutoKick;
            if (!IsStopRule(i.Rule)) return Verdict.NotStopRule;
            if (!string.Equals(i.QueuedLevel, "Certain", StringComparison.Ordinal) || !string.Equals(i.EffectiveLevel, "Certain", StringComparison.Ordinal))
                return Verdict.NotCertain;
            if (!i.VisibleEffect) return Verdict.NoEffect;
            if (i.FileOff) return Verdict.FileOff;
            if (!i.Compat) return Verdict.Registered;
            if (!i.InGame) return Verdict.NotInGame;
            if (i.Ending) return Verdict.AlreadyEnding;
            if (i.StoppedThisMatch) return Verdict.OncePerMatch;
            if (!i.Simulated && i.UnbanGrace) return Verdict.UnbanGrace;
            if (!i.Simulated && i.StopsLastHour >= MaxStopsPerHour) return Verdict.HourlyCap;
            return Verdict.Stop;
        }

        /// <summary>The verdicts the host is told about on the removal line (the others go to the log only).</summary>
        internal static bool HostNote(Verdict v) => v == Verdict.UnbanGrace || v == Verdict.HourlyCap || v == Verdict.NoEffect;

        /// <summary>
        /// A screen the end may be sent on: this match's intro has closed (never during the intro or the loading before it:
        /// ending a compat game there got the host disconnected for "Hacking", 2026-09-09), no exile screen, and either a
        /// meeting in discussion / voting (states 1 Discussion, 2 NotVoted, 3 Voted: verified live 2026-09-21) or no meeting
        /// and <see cref="AfterPause"/> since the last intro / meeting / exile closed (vanilla's WrapUp has run: every client
        /// decides the end of an exile there and waits on a black screen until the host ends the game — AntiBlackout).
        /// <paramref name="meetingState"/>: -1 no meeting, else (int)MeetingHud.MeetingStates (0 Animating, 4 Results,
        /// 5 Proceeding and unknown values are not safe).
        /// </summary>
        internal static bool SafeScreen(bool introDone, bool intro, int meetingState, bool exile, float sincePauseEnd)
        {
            if (!introDone || intro || exile) return false;
            if (meetingState >= 0) return meetingState == 1 || meetingState == 2 || meetingState == 3;
            return meetingState == -1 && sincePauseEnd >= AfterPause;
        }

        /// <summary>End now (a safe screen, <see cref="KickGap"/> after the removal), wait, or give up after <see cref="GiveUpAfter"/>.</summary>
        internal static Phase EndPhase(bool introDone, bool intro, int meetingState, bool exile, float sincePauseEnd, float sinceStop)
        {
            if (sinceStop >= KickGap && SafeScreen(introDone, intro, meetingState, exile, sincePauseEnd)) return Phase.Now;
            if (sinceStop >= GiveUpAfter) return Phase.GiveUp;
            return Phase.Wait;
        }

        /// <summary>
        /// The nameless line after the stop: in the lobby once enough of the players are back (they return on their own, 0.4 to
        /// 30 s after the host; a line sent before a player is back never reaches that player). <paramref name="expected"/>:
        /// the players who were in the match (host and removed player not counted); 0 = unknown (CheatDetector's held lines).
        /// </summary>
        internal static Line LobbyLine(bool gameStarted, bool inLobby, bool countdown, float sinceHostInLobby, int clientsBack, int expected, float sinceFirstBack)
        {
            if (gameStarted) return Line.Drop;
            if (!inLobby) return Line.Wait;
            if (countdown) return Line.Send;   // the next game is about to start: now or never
            int need = Math.Max(1, (int)Math.Ceiling(Math.Max(0, expected) * LineShare));
            if (clientsBack >= need && sinceFirstBack >= LineSettle) return Line.Send;
            // review: the settle holds here too (a first player seen after 12 s used to get the line in the same frame)
            if (clientsBack >= 1 && sinceHostInLobby >= LineMaxWait && sinceFirstBack >= LineSettle) return Line.Send;
            if (clientsBack <= 0 && sinceHostInLobby >= LineDropAfter) return Line.Drop;
            return Line.Wait;
        }

        /// <summary>
        /// The second (last) nameless line, for the players who came back after the first one. <paramref name="backAtLine"/>:
        /// clients in the lobby when the first line went out; <paramref name="clientsBack"/> now; <paramref name="expected"/>:
        /// the players who were in the match (0 = unknown: no second line); <paramref name="sinceLastArrival"/>: since the
        /// client count last went up. Players are counted, not identified (a client id after "play again" is not relied on),
        /// so a newcomer can count as one who came back.
        /// </summary>
        internal static Repeat RepeatLine(bool gameStarted, bool inLobby, bool countdown, float sinceLine, int backAtLine, int clientsBack, int expected, float sinceLastArrival)
        {
            if (gameStarted) return Repeat.Drop;
            if (expected <= 0 || backAtLine >= expected) return Repeat.Drop;   // everyone had it (or nobody to wait for)
            bool more = clientsBack > backAtLine;
            if (countdown) return more ? Repeat.Send : Repeat.Drop;         // the next game is about to start: now or never
            if (sinceLine >= RepeatDropAfter) return more ? Repeat.Send : Repeat.Drop;
            if (!inLobby || !more || sinceLastArrival < LineSettle) return Repeat.Wait;
            if (clientsBack >= expected || sinceLine >= RepeatAfter) return Repeat.Send;
            return Repeat.Wait;
        }

        /// <summary>Entries of <paramref name="times"/> within <paramref name="window"/> seconds before <paramref name="now"/> (now - t &lt; window).</summary>
        internal static int CountRecent(IList<float> times, float now, float window)
        {
            if (times == null) return 0;
            int n = 0;
            foreach (float t in times) if (now - t < window) n++;
            return n;
        }

        /// <summary>Drops the entries that <see cref="CountRecent"/> no longer counts (keeps the list short in a long lobby).</summary>
        internal static void Prune(List<float> times, float now, float window)
        {
            if (times == null) return;
            times.RemoveAll(t => now - t >= window);
        }
    }
}
