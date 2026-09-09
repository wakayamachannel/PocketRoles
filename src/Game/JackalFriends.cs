using System.Collections.Generic;
using PocketRoles.Core;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare name "Game" resolves to the sibling namespace, so alias the class here.
    using Game = PocketRoles.Core.Game;

    /// <summary>
    /// ジャッカルフレンズ / Jackal Friends (v0.5.0): the Jackal's Madmate. A vanilla Crewmate on its own client (no kill
    /// button, Team.Neutral, fake tasks) that sees every Jackal in blue (NameTags rule 3a), is not counted as crew by
    /// WinConditions.EvaluateBase and wins whenever the Jackal wins (ComputeWinners, dead or alive). Only drawn in games
    /// that have a Jackal (RoleAssignment.AssignCustomRoles gate). No ability state: nothing to reset.
    /// </summary>
    public static class JackalFriends
    {
        /// <summary>Any Jackal in the role table (forced or drawn) — the assignment gate and LogAssignment.</summary>
        internal static bool AnyJackal()
        {
            foreach (var kv in Game.Roles) if (kv.Value == CustomRole.Jackal) return true;
            return false;
        }

        /// <summary>Any Jackal Friends in the role table (LogAssignment).</summary>
        internal static bool Any()
        {
            foreach (var kv in Game.Roles) if (kv.Value == CustomRole.JackalFriends) return true;
            return false;
        }

        /// <summary>Names of the alive Jackals joined by Lang.ListSep ("" when none) — Chat.RoleInfoText.</summary>
        internal static string JackalNames()
        {
            var names = new List<string>();
            foreach (var id in Game.AllPlayerIds())
            {
                if (Game.RoleOf(id) == CustomRole.Jackal && Game.IsAlive(id)) names.Add(Game.NameOf(id));
            }
            return string.Join(Lang.ListSep, names);
        }
    }
}
