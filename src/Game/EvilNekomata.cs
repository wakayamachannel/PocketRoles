using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using PocketRoles.Core;
using PocketRoles.Net;
using UnityEngine;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;
    using HrChat = PocketRoles.Chat.Chat;

    /// <summary>
    /// イビル猫又 / Evil Nekomata (v0.5.0): vanilla Impostor that, when voted out, drags one of its voters along (a Game.Bites entry,
    /// reason "nekomata", registered at MeetingHud.VotingComplete and re-armed / announced at ExileController.WrapUp; Game.NekomataDragged).
    /// Hooks are wired in Meetings_VotingCompletePatch (Meetings.VotersFor) and Meetings_ExileWrapUpPatch.
    /// SKELETON (Implementer A, design-v0.5.0.md §1.14): stub bodies — the owner (Implementer C, §7) fills them in.
    /// </summary>
    public static class EvilNekomata
    {
        /// <summary><paramref name="exiledId"/> was voted out; <paramref name="states"/> is the tallied vote array, <paramref name="hud"/> the fallback reader.</summary>
        internal static void OnPlayerExiled(byte exiledId, Il2CppStructArray<MeetingHud.VoterState> states, MeetingHud hud)
        {
            return;
        }

        /// <summary>ExileController.WrapUp (after the end checks): re-arm the drag bite to +2.5 s and schedule the announcement.</summary>
        internal static void OnMeetingEnd(byte exiledId)
        {
            return;
        }
    }
}
