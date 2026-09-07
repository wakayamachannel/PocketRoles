using System;
using PocketRoles.Core;

namespace PocketRoles.Game
{
    /// <summary>
    /// Host tools for a running meeting (v0.4 §B): force the vote to end now (F8 twice / <c>/endmeeting</c>) and
    /// cut the results screen short (<c>/results</c>). Vanilla clients only see the chat notice and the normal
    /// vanilla RPCs (VotingComplete / CloseMeeting), exactly as when the voting timer runs out.
    /// The hotkey handling (double press) lives in <c>Core.Hotkeys</c>; the chat commands in <c>Chat.Commands</c>.
    /// </summary>
    public static class MeetingTools
    {
        /// <summary>Guards against a second force-end on the same MeetingHud instance (e.g. hotkey and command in one frame).</summary>
        private static MeetingHud _ended;
        /// <summary>The MeetingHud whose results screen we already closed (RpcClose must fire only once).</summary>
        private static MeetingHud _closed;

        /// <summary>The current meeting when the host may act on it, else null (logs nothing).</summary>
        private static MeetingHud Current()
        {
            if (!Core.Game.IsHostActive) return null;
            var mh = MeetingHud.Instance;
            if (mh == null) return null;
            return mh;
        }

        private static bool IsVotingState(MeetingHud.MeetingStates s)
        {
            return s == MeetingHud.MeetingStates.Discussion
                || s == MeetingHud.MeetingStates.NotVoted
                || s == MeetingHud.MeetingStates.Voted;
        }

        private static string NoMeetingText() =>
            Lang.T("meeting.none", "会議中ではありません。", "There is no meeting right now.");

        /// <summary>
        /// Ends the vote of the current meeting immediately (host only, state Discussion / NotVoted / Voted).
        /// Notice to everyone, then vanilla timeout path: players who have not voted are marked as missed
        /// (<c>ForceSkipAll</c>) and the tally runs (<c>CheckForEndVoting</c>; the Mayor prefix in Meetings applies).
        /// Returns true when the meeting was ended. When nothing can be done a host-local notice explains why.
        /// </summary>
        public static bool EndMeetingNow()
        {
            try
            {
                var mh = Current();
                if (mh == null)
                {
                    if (Core.Game.IsHostActive) PocketRoles.Chat.Chat.Local(PocketRoles.Chat.Chat.Title, NoMeetingText());
                    return false;
                }
                var state = mh.state;
                if (!IsVotingState(state))
                {
                    PocketRoles.Chat.Chat.Local(PocketRoles.Chat.Chat.Title, state == MeetingHud.MeetingStates.Results || state == MeetingHud.MeetingStates.Proceeding
                        ? Lang.T("meeting.alreadyover", "投票はすでに終了しています。", "The vote is already over.")
                        : NoMeetingText());
                    return false;
                }
                if (_ended == mh)
                {
                    PocketRolesPlugin.Logger.LogInfo("MeetingTools: force-end already requested for this meeting, ignoring");
                    return false;
                }
                _ended = mh;

                PocketRolesPlugin.Logger.LogInfo($"MeetingTools: host force-ends the meeting (state={state})");
                PocketRoles.Chat.Chat.All(PocketRoles.Chat.Chat.Title, () =>
                    Lang.T("meeting.forceend", "ホストが会議を終了しました", "The host ended the meeting"));

                // Variant A (research v04-4): identical to the vanilla voting timeout on the host. Non-voters become
                // MissedVote (not counted); CheckForEndVoting then sees everybody voted / dead and sends
                // RpcVotingComplete itself (Judge overrule included; our Mayor prefix runs when an alive Mayor voted).
                mh.ForceSkipAll();
                mh.CheckForEndVoting();
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"MeetingTools.EndMeetingNow: {e}");
                return false;
            }
        }

        /// <summary>
        /// Skips the rest of the results screen (host only, state Results): the host does what its own Update does
        /// after <c>MeetingHud.ResultsTime</c> — state = Proceeding, then one <c>RpcClose()</c> (clients run Close and
        /// the exile cutscene). Returns true when the meeting was closed.
        /// </summary>
        public static bool ShortenResults()
        {
            try
            {
                var mh = Current();
                if (mh == null)
                {
                    if (Core.Game.IsHostActive) PocketRoles.Chat.Chat.Local(PocketRoles.Chat.Chat.Title, NoMeetingText());
                    return false;
                }
                var state = mh.state;
                if (state != MeetingHud.MeetingStates.Results)
                {
                    PocketRoles.Chat.Chat.Local(PocketRoles.Chat.Chat.Title, IsVotingState(state)
                        ? Lang.T("meeting.notresults", "まだ投票中です。/endmeeting で投票を終了できます。", "Voting is still running. /endmeeting ends the vote.")
                        : NoMeetingText());
                    return false;
                }
                if (_closed == mh)
                {
                    PocketRolesPlugin.Logger.LogInfo("MeetingTools: results already closed for this meeting, ignoring");
                    return false;
                }
                _closed = mh;

                PocketRolesPlugin.Logger.LogInfo("MeetingTools: host shortens the results screen");
                // Proceeding first so the host's own Update does not send a second CloseMeeting after ResultsTime.
                mh.state = MeetingHud.MeetingStates.Proceeding;
                mh.RpcClose();
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"MeetingTools.ShortenResults: {e}");
                return false;
            }
        }
    }
}
