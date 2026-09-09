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
    /// 侍 / Samurai (v0.5.0): vanilla Impostor whose kill is an area slash — the pressed target dies at once, every bystander in
    /// [Samurai] Range dies as a paced Game.Bites entry (reason "slash", [Samurai] Stagger apart; Kills.MarkSlash / SlashInProgress
    /// bound the window). Hooks are wired in Kills.HandleCheckMurder (Slash), Kills.TryStuntmanGuard (KillCooldown), OptionsDesync,
    /// WinConditions.Check and Kills.ForceReport (wait for the slash) and Chat.RoleInfoText.
    /// SKELETON (Implementer A, design-v0.5.0.md §1.14): stub bodies — the owner (Implementer D, §9) fills them in.
    /// </summary>
    public static class Samurai
    {
        /// <summary>[Samurai] KillCooldown, or the lobby kill cooldown when 0.</summary>
        internal static float KillCooldown()
        {
            return 0f;
        }

        /// <summary>Kill button pressed on <paramref name="target"/> (after Kills.IsValidMurder): slash the target and everyone in range.</summary>
        internal static void Slash(PlayerControl samurai, PlayerControl target)
        {
            return;
        }
    }
}
