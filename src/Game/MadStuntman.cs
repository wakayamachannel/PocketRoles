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
    /// マッドスタントマン / Mad Stuntman (v0.5.0): Madmate-family crewmate that survives the first [MadStuntman] Lives kill attempts
    /// (Game.StuntGuards: playerId → attempts survived). The guard itself lives in Kills.TryStuntmanGuard; this class keeps the
    /// counter and the notices. Hooks are wired in Kills (guard, IsShielded) and Chat.RoleInfoText.
    /// SKELETON (Implementer A, design-v0.5.0.md §1.14): stub bodies — the owner (Implementer B, §3) fills them in.
    /// </summary>
    public static class MadStuntman
    {
        /// <summary>Kill attempts <paramref name="id"/> can still survive (0 for anyone who is not a Mad Stuntman or has spent its lives).</summary>
        internal static int Remaining(byte id)
        {
            return 0;
        }

        /// <summary>A kill of <paramref name="killerId"/> (custom role <paramref name="killerRole"/>) on the Stuntman <paramref name="targetId"/> was absorbed: count it, notify both.</summary>
        internal static void OnGuarded(byte killerId, byte targetId, CustomRole killerRole)
        {
            return;
        }
    }
}
