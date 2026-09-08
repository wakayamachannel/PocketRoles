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
    /// 魔女 / Witch (v0.4.1): vanilla Impostor whose kill button curses (Game.Spelled: target → witch); every cursed
    /// player dies 2 s after the next exile screen (Game.Bites, reason "curse", credited to the witch so a Bait victim
    /// forces the witch to report). The curse fades when the witch dies or is exiled.
    /// Hooks are wired in Kills.HandleCheckMurder / OnMurder, Meetings_VotingCompletePatch, Meetings_ExileWrapUpPatch,
    /// NameTags, OptionsDesync and Chat.RoleInfoText. The target never learns it was cursed (no RPC reaches it);
    /// only the witch sees the † mark and the notice (the target too with [Witch] SpelledSeeMark).
    /// </summary>
    public static class Witch
    {
        /// <summary>Name-tag prefix the witch sees on spelled players (and the target itself with [Witch] SpelledSeeMark). Change here if the client font lacks the glyph: X / ✝.</summary>
        public const string Mark = "<color=" + Roles.ImpostorColor + ">†</color>";

        /// <summary>Delay after ExileController.WrapUp: matches the postponed-bite rule (after AntiBlackout.Restore at 1.5 s).</summary>
        private const float CurseDelay = 2f;

        /// <summary>[Witch] SpellCooldown, or the lobby kill cooldown when 0.</summary>
        internal static float SpellCooldown()
        {
            float cd = Options.WitchSpellCooldown;
            return cd > 0f ? cd : Kills.LobbyKillCooldown();
        }

        // ------------------------------------------------------------------ spell

        /// <summary>
        /// Kill button pressed on <paramref name="target"/> (after Kills.IsValidMurder, so both alive, no meeting, target
        /// not in a vent / GA-protected): curse it (Vampire pattern: cooldown reset + mark + private notice; never a kill).
        /// </summary>
        internal static void Spell(PlayerControl witch, PlayerControl target)
        {
            try
            {
                if (witch == null || target == null) return;
                if (witch.Data == null || target.Data == null) return;
                byte w = witch.PlayerId, t = target.PlayerId;

                if (Game.Spelled.ContainsKey(t))
                {
                    // Already cursed (by any witch): no cooldown reset, like a second Vampire bite on the same target.
                    Rpc.FailKill(witch, target);
                    return;
                }

                Rpc.ResetKillCooldown(witch, SpellCooldown());   // host: SetKillTimer; client: options ×2 + FailedProtected + options back
                Game.Spelled[t] = w;
                PocketRolesPlugin.Logger.LogInfo($"Kills: Witch {Game.NameOf(w)} cursed {Game.NameOf(t)} ({Game.Spelled.Count} pending)");
                Kills.Notice(w, "kill.spell", "{0} に呪いをかけました。次の会議の後に死亡します。", "You cursed {0}. They die after the next meeting.", Game.NameOf(t));
                NameTags.RefreshAll();   // † before the name for the witch (and the target with [Witch] SpelledSeeMark)
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Witch.Spell: {e}");
            }
        }

        // ------------------------------------------------------------------ deaths

        /// <summary>A player died (MurderPlayer succeeded, incl. executed bites and assassinations): a dead witch's spells fade, a dead target is forgotten.</summary>
        internal static void OnPlayerDied(byte id) => Forget(id, "died");

        /// <summary>A player was voted out (MeetingHud.VotingComplete, before OnMeetingEnd): an exiled witch's spells are gone before the curse could strike.</summary>
        internal static void OnPlayerExiled(byte id) => Forget(id, "exiled");

        /// <summary>
        /// Drops the spell on <paramref name="id"/> and, when it is a witch, every spell it cast. The † marks vanish with
        /// the next name refresh (ScheduleMurderRefresh after a kill, the forced RefreshAll 2 s after WrapUp).
        /// </summary>
        private static void Forget(byte id, string cause)
        {
            try
            {
                if (Game.Spelled.Count == 0) return;
                if (Game.Spelled.Remove(id))
                    PocketRolesPlugin.Logger.LogInfo($"Witch: spelled player {Game.NameOf(id)} {cause}, spell forgotten");

                if (Game.RoleOf(id) != CustomRole.Witch) return;
                int cleared = 0;
                foreach (var kv in new List<KeyValuePair<byte, byte>>(Game.Spelled))
                {
                    if (kv.Value != id) continue;
                    Game.Spelled.Remove(kv.Key);
                    cleared++;
                }
                if (cleared > 0)
                    PocketRolesPlugin.Logger.LogInfo($"Witch: {Game.NameOf(id)} {cause}, {cleared} spell(s) cleared");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Witch.Forget({id}, {cause}): {e}");
            }
        }

        // ------------------------------------------------------------------ curse

        /// <summary>
        /// ExileController.WrapUp (after the existing bite postponement): every pending spell whose witch and target are
        /// both still alive becomes a delayed death 2 s after the exile screen (Kills.Tick → Rpc.Kill(victim, victim);
        /// OnMurder credits the witch). Spells are cleared afterwards whether or not they struck.
        /// </summary>
        internal static void OnMeetingEnd(byte exiledId)
        {
            try
            {
                if (Game.Spelled.Count == 0) return;
                if (!Game.InProgress || Game.Ending) { Game.Spelled.Clear(); return; }
                int cursed = 0;
                foreach (var kv in new List<KeyValuePair<byte, byte>>(Game.Spelled))
                {
                    byte t = kv.Key, w = kv.Value;
                    if (t == exiledId || !Game.IsAlive(t)) continue;                 // target already dead / exiled / disconnected
                    if (w == exiledId || !Game.IsAlive(w)) continue;                 // witch gone → curse fades
                    if (Game.Bites.ContainsKey(t)) continue;                         // already dying (vampire / lover); it dies anyway
                    Game.Bites[t] = new Game.VampireBite { Killer = w, DueAt = Time.time + CurseDelay, Reason = "curse" };
                    cursed++;
                }
                Game.Spelled.Clear();
                if (cursed > 0)
                {
                    PocketRolesPlugin.Logger.LogInfo($"Witch: curse strikes {cursed} player(s) {CurseDelay:0.#} s after the exile screen");
                    HrChat.All(HrChat.Title, () => Lang.T("kill.curse.all", "魔女の呪いが発動しました。", "The witch's curse strikes."));
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Witch.OnMeetingEnd({exiledId}): {e}");
            }
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Comma-joined names of the alive players spelled by <paramref name="witchId"/> ("" when none; meeting reminder line).</summary>
        internal static string SpelledNamesFor(byte witchId)
        {
            try
            {
                if (Game.Spelled.Count == 0) return "";
                var sb = new StringBuilder();
                foreach (var kv in Game.Spelled)
                {
                    if (kv.Value != witchId || !Game.IsAlive(kv.Key)) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(Game.NameOf(kv.Key));
                }
                return sb.ToString();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Witch.SpelledNamesFor({witchId}): {e}");
                return "";
            }
        }
    }
}
