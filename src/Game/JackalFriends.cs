using System;
using System.Collections.Generic;
using System.Text;
using PocketRoles.Core;
using PocketRoles.Net;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;
    using HrChat = PocketRoles.Chat.Chat;

    /// <summary>
    /// ジャッカルフレンズ / Jackal Friends (v0.5.0): the Jackal's Madmate — vanilla Crewmate on its client, Team.Neutral, wins with the
    /// Jackal (WinConditions.ComputeWinners), sees the Jackal in blue (NameTags rule 3a). Only assigned in games with a Jackal
    /// (RoleAssignment.AssignCustomRoles gate). Hooks are wired in RoleAssignment, NameTags, WinConditions, Kills.CanSheriffKill and Chat.RoleInfoText.
    /// SKELETON (Implementer A, design-v0.5.0.md §1.14): stub bodies — the owner (Implementer D, §6) fills them in.
    /// </summary>
    public static class JackalFriends
    {
        /// <summary>A Jackal is in the role table (alive or dead) — the assignment gate for the Friends.</summary>
        internal static bool AnyJackal()
        {
            return false;
        }

        /// <summary>Any Jackal Friends in the role table.</summary>
        internal static bool Any()
        {
            return false;
        }

        /// <summary>Names of the living Jackals ("" when none) for the Friends' role info.</summary>
        internal static string JackalNames()
        {
            return "";
        }
    }
}
