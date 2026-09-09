using System;
using System.Collections.Generic;
using PocketRoles.Core;
using PocketRoles.Net;
using UnityEngine;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;
    using HrChat = PocketRoles.Chat.Chat;

    /// <summary>
    /// シリアルキラー / Serial Killer (v0.5.0): vanilla Impostor with a short kill cooldown that dies by itself when it has not killed
    /// for [SerialKiller] SuicideTime (Game.SerialKillerTimers / SerialKillerPaused; the time-out is a Game.Bites entry, reason
    /// "serialkiller"). Hooks are wired in Plugin_TickPatch (Tick), Kills.OnMurder (OnKill), Kills.HandleCheckMurder (host kill),
    /// RoleAssignment (OnIntroEnd), Meetings_ExileWrapUpPatch (OnExileWrapUp / OnMeetingEnd), NameTags (CountdownTag),
    /// OptionsDesync and Chat.RoleInfoText (Limit).
    /// SKELETON (Implementer A, design-v0.5.0.md §1.14): stub bodies — the owner (Implementer C, §8) fills them in.
    /// </summary>
    public static class SerialKiller
    {
        /// <summary>Scheduler tag of the intro arm (cancelled at lobby start through Game.GameScopedTags).</summary>
        internal const string IntroArmTag = "serialkiller.arm";
        /// <summary>Game.Bites reason of the own time-out death.</summary>
        internal const string BiteReason = "serialkiller";

        /// <summary>Per-game state (Game.ResetRoleState): one "limit raised" warning per game.</summary>
        internal static void ResetState()
        {
            return;
        }

        /// <summary>Effective suicide limit: [SerialKiller] SuicideTime, never below KillCooldown + 5.</summary>
        internal static float Limit()
        {
            return 0f;
        }

        /// <summary>Countdown text for the own name tag (5 s steps), null when nothing to show.</summary>
        internal static string CountdownTag(byte id)
        {
            return null;
        }

        /// <summary>Per-frame (Plugin_TickPatch, before Kills.Tick): counts down, warns, queues the time-out death as a bite.</summary>
        public static void Tick()
        {
            return;
        }

        /// <summary>Host intro ended (+1 s): schedules the countdown start after the last client's intro.</summary>
        internal static void OnIntroEnd()
        {
            return;
        }

        /// <summary>The Serial Killer <paramref name="killerId"/> killed someone else (or a guarded press): restart its countdown.</summary>
        internal static void OnKill(byte killerId)
        {
            return;
        }

        /// <summary>ExileController.WrapUp: hold the countdowns until OnMeetingEnd.</summary>
        internal static void OnExileWrapUp()
        {
            return;
        }

        /// <summary>WrapUp + 2 s: reset / carry over the countdown, reset the kill timer, notify.</summary>
        internal static void OnMeetingEnd()
        {
            return;
        }
    }
}
