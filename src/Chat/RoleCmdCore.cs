using System.Collections.Generic;
using System.Text;

namespace PocketRoles.Chat
{
    /// <summary>
    /// v0.5.5 (design review §5.5): the decisions behind "/cmd n" for a player of a running game, kept free of game
    /// types so the game-free tests can compile this file on its own.
    /// <para>
    /// A living vanilla player cannot open the chat outside a meeting, so the host must not answer a living player's
    /// "/cmd n" during the round with the CURRENT state ("no Jackal is alive", "2 kills left"): a modded client would
    /// otherwise learn mid-round what everybody else only reads at the next meeting. Such an answer repeats the last
    /// role text that player was really sent (<see cref="Answer.LastSent"/>), or, when nothing was sent yet,
    /// the description without any live line (<see cref="Answer.StaticOnly"/>, built by <see cref="Compose"/>).
    /// </para>
    /// </summary>
    internal static class RoleCmdCore
    {
        /// <summary>What "/cmd n" answers with.</summary>
        internal enum Answer
        {
            /// <summary>Today's behaviour: the text built from the current state (meeting, dead player, host).</summary>
            Live,
            /// <summary>The last role text the host sent that player in this game, unchanged.</summary>
            LastSent,
            /// <summary>Nothing sent yet: the role description without the lines that read the running game.</summary>
            StaticOnly,
        }

        /// <summary>
        /// Which text "/cmd n" answers with, for a caller the command has already established is in a RUNNING game
        /// (Commands.MyRoleText answers "available once the game has started" before this, so the lobby and the end
        /// screen never get here — Game.Ending and Game.InProgress are set in the same breath by WinConditions).
        /// Live for the host's own screen, for a dead player (a ghost may chat, so nothing is hidden from it) and
        /// during a meeting (everybody can chat and read there). During the round — including the intro and the exile
        /// screen, where the chat is closed for the living too — a living player gets the text it was last sent.
        /// </summary>
        internal static Answer Decide(bool isHost, bool alive, bool inMeeting, bool hasLastSent)
        {
            if (isHost) return Answer.Live;              // the host's own screen: it holds every state anyway
            if (!alive) return Answer.Live;              // ghosts chat in vanilla too
            if (inMeeting) return Answer.Live;           // a meeting: vanilla players read and write chat here
            return hasLastSent ? Answer.LastSent : Answer.StaticOnly;
        }

        /// <summary>
        /// The "everyone" commands whose reply is built from the RUNNING game's state, and which therefore take the
        /// guarded path and the extra rate limit (v0.5.5 audit of every subcommand, see Commands.IsEveryoneCommand):
        /// <list type="bullet">
        /// <item>n / now / me / 役職 — the role text: Jackal Friends' living Jackals, the Mad Stuntman's kills left,
        /// the Worshipper's uses and converts, the Witch's cursed players. <b>Guarded here.</b></item>
        /// <item>h / help / ? / ヘルプ — the fixed help page, the caller's own permission level and language.</item>
        /// <item>r / role / roles — the role list and one role's description: lobby settings only.</item>
        /// <item>s / settings / 設定 / 设置 — the settings summary: lobby settings only.</item>
        /// <item>l / last — the summary of the PREVIOUS game (WinConditions fills it when a game ends).</item>
        /// <item>lang / language / 言語 — the caller's own language.</item>
        /// <item>time / timer / 時間 — lobby only ("only available in the lobby" during a game).</item>
        /// <item>about / info / 説明 / 关于 — a fixed text.</item>
        /// <item>guess / g / 推理 — refuses outside the voting phase of a meeting before it looks at any player.</item>
        /// <item>id / myid — the caller's own erase code (its own 60 s limit).</item>
        /// </list>
        /// </summary>
        internal static bool ReadsRunningGameState(string cmd)
        {
            switch (cmd)
            {
                case "n": case "now": case "me": case "役職":
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ the lines under the role description

        /// <summary>
        /// One extra line under "role — description" in a role text, with the one property that decides whether a
        /// <see cref="Answer.StaticOnly"/> answer may carry it (review 2026-09-23: keeping the flag next to the line
        /// and the dropping in one tested place beats an <c>&amp;&amp; live</c> on every append, which is easy to
        /// forget and reopens the leak silently).
        /// </summary>
        internal readonly struct Extra
        {
            /// <summary>The line itself (never contains the leading newline).</summary>
            internal readonly string Text;
            /// <summary>The line is built from the RUNNING game and is left out of a StaticOnly answer.</summary>
            internal readonly bool ReadsRunningGame;

            internal Extra(string text, bool readsRunningGame)
            {
                Text = text;
                ReadsRunningGame = readsRunningGame;
            }
        }

        /// <summary>A line that was settled when the roles were handed out (the lover's name, the Serial Killer's limit …).</summary>
        internal static Extra Settled(string text)
        {
            return new Extra(text, false);
        }

        /// <summary>A line that reads the RUNNING game (living Jackals, kills left, worships left, cursed players …).</summary>
        internal static Extra LiveState(string text)
        {
            return new Extra(text, true);
        }

        /// <summary>
        /// <paramref name="head"/> plus the extras that belong in this answer, one per line. With
        /// <paramref name="live"/> = false (only the StaticOnly fallback of "/cmd n") every
        /// <see cref="Extra.ReadsRunningGame"/> line is dropped. Empty lines are skipped either way.
        /// </summary>
        internal static string Compose(string head, IList<Extra> extras, bool live)
        {
            if (extras == null || extras.Count == 0) return head;
            var sb = new StringBuilder(head ?? string.Empty);
            foreach (var e in extras)
            {
                if (string.IsNullOrEmpty(e.Text)) continue;
                if (!live && e.ReadsRunningGame) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(e.Text);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the rate limit

        /// <summary>
        /// One answer per player per <c>cooldown</c> seconds (the rest silently dropped), like the /id limiter: a
        /// modded client must not be able to make the host send the 5-message role text again and again mid-round.
        /// The slot is spent by <see cref="Allow"/> only where an answer is really about to go out (review
        /// 2026-09-23), never for a constant string like "available once the game has started".
        /// </summary>
        internal sealed class AskLimiter
        {
            private readonly float _cooldown;
            private readonly Dictionary<byte, float> _last = new Dictionary<byte, float>();

            internal AskLimiter(float cooldown)
            {
                _cooldown = cooldown;
            }

            /// <summary>True when the player may be answered now (and the slot is consumed).</summary>
            internal bool Allow(byte playerId, float now)
            {
                if (_last.TryGetValue(playerId, out float last) && now >= last && now - last < _cooldown) return false;
                _last[playerId] = now;
                return true;
            }

            /// <summary>New game / new lobby: player ids are reused, and nobody may start a game already blocked.</summary>
            internal void Clear()
            {
                _last.Clear();
            }
        }
    }
}
