using System;
using System.Collections.Generic;
using System.Text;
using PocketRoles.Core;
using PocketRoles.Net;
using UnityEngine;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;
    using HrChat = PocketRoles.Chat.Chat;

    /// <summary>
    /// 崇拝者 / Worshipper (v0.5.0): Madmate-family crewmate whose own client is an Impostor (ImpostorDesync); its kill button
    /// "worships" a crew target into a literal Madmate through Game.ConvertRole (Game.Worshipped: worshipper → converts).
    /// Worshipping an impostor-team killer kills the Worshipper (SNR MadMaker rule). Hooks are wired in Kills.HandleCheckMurder,
    /// Kills.ResetNotices, NameTags and Chat.RoleInfoText.
    /// SKELETON (Implementer A, design-v0.5.0.md §1.14): stub bodies — the owner (Implementer B, §5) fills them in.
    /// </summary>
    public static class Worshipper
    {
        /// <summary>Name-tag prefix the worshipper sees on the players it converted (literal Madmates).</summary>
        public const string Mark = "<color=" + Roles.ImpostorColor + ">Ⓜ</color>";
        /// <summary>Name-tag prefix impostors see on the Worshipper with [Madmate] KnownToImpostors (distinct from Ⓜ so the Assassin can name the exact role).</summary>
        public const string ImpostorViewMark = "<color=" + Roles.ImpostorColor + ">Ⓦ</color>";

        /// <summary>Successful worships of <paramref name="worshipperId"/> so far.</summary>
        internal static int Used(byte worshipperId)
        {
            return 0;
        }

        /// <summary>Worships <paramref name="worshipperId"/> may still perform ([Worshipper] Uses − Used).</summary>
        internal static int Remaining(byte worshipperId)
        {
            return 0;
        }

        /// <summary>Names of the players <paramref name="worshipperId"/> converted, in conversion order ("" when none).</summary>
        internal static string ConvertedNamesFor(byte worshipperId)
        {
            return "";
        }

        /// <summary>Kill button pressed on <paramref name="target"/> (after Kills.IsValidMurder): convert, self-destruct or fail.</summary>
        internal static void Worship(PlayerControl worshipper, PlayerControl target)
        {
            return;
        }

        /// <summary>Per-game notice throttles (Kills.ResetNotices).</summary>
        internal static void ResetNotices()
        {
            return;
        }
    }
}
