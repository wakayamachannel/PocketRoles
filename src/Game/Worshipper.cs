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
    /// 崇拝者 / Worshipper (v0.5.0): Madmate-type desync impostor (Team.Impostor, crew pool) whose kill button "worships":
    /// a crew target becomes a Madmate on the spot (Game.ConvertRole → Madmate, permanent, limited uses), an impostor-team
    /// killer as target kills the worshipper instead (SNR MadMaker rule), anything else fails without spending a use;
    /// after the last use every press only fails (never lethal). Hooks are wired in Kills.HandleCheckMurder /
    /// CanSheriffKill / ResetNotices, NameTags (Ⓜ on converts; Ⓜ/Ⓦ for impostors through Roles.IsMadType),
    /// WinConditions.EvaluateBase (IsMadType), OptionsDesync (cooldown, Resync) and Chat.RoleInfoText.
    /// The target of a successful worship IS told (private notice + Madmate role text, one ChunkSpacing after the
    /// worshipper's packets: never two clients' chat in one frame — official-server rate limit); a failed target learns nothing.
    /// </summary>
    public static class Worshipper
    {
        /// <summary>Name-tag prefix the worshipper sees on the players it converted (Ⓜ is proven on vanilla clients).</summary>
        public const string Mark = "<color=" + Roles.ImpostorColor + ">Ⓜ</color>";

        /// <summary>
        /// Name-tag prefix impostors see on the Worshipper with [Madmate] KnownToImpostors (NameTags rule 3): distinct from
        /// the Madmate's Ⓜ so the Assassin can guess the exact role. Ⓦ (U+24CC) is in the same Unicode block as the proven Ⓜ
        /// (U+24C2); if an emulator shows □ (§12 W2-1), change this to a colour-distinct Ⓜ, e.g. "<color=#ff8c8c>Ⓜ</color>".
        /// </summary>
        public const string ImpostorViewMark = "<color=" + Roles.ImpostorColor + ">Ⓦ</color>";

        /// <summary>
        /// Blocking acceptance test (§12 W2-4): MurderPlayer with the HOST as victim while the host's own local role
        /// is Impostor (desync dispatch) is unverified — the host-target block in Kills exists because vanilla CheckMurder
        /// refuses such a target. If the host self-kill does not apply, set this to true: the host then dies through the
        /// Assassin / GameMaster pattern (Exiled RPC + Data sync + ghost role, no body).
        /// </summary>
        private const bool HostDiesByExile = false;

        /// <summary>Same throttle as Kills.ShouldNotice: a mashed button on an invalid target / with no uses left sends several CheckMurder per second.</summary>
        private const float NoticeRepeatSeconds = 10f;
        private static readonly Dictionary<byte, float> LastFailNotice = new Dictionary<byte, float>();

        private enum Verdict { Convert, Impostor, Invalid }

        // ------------------------------------------------------------------ queries

        /// <summary>Successful worships so far (dead converts still count).</summary>
        internal static int Used(byte worshipperId) => Game.Worshipped.TryGetValue(worshipperId, out var list) ? list.Count : 0;

        /// <summary>Worships left ([Worshipper] Uses minus successes; never negative when the option was lowered mid-game).</summary>
        internal static int Remaining(byte worshipperId) => Math.Max(0, Options.WorshipperUses - Used(worshipperId));

        /// <summary>Comma-joined names of the players <paramref name="worshipperId"/> converted, in conversion order ("" when none; role-info line). Dead converts stay listed.</summary>
        internal static string ConvertedNamesFor(byte worshipperId)
        {
            try
            {
                if (!Game.Worshipped.TryGetValue(worshipperId, out var list) || list.Count == 0) return "";
                var sb = new StringBuilder();
                foreach (var id in list)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(Game.NameOf(id));
                }
                return sb.ToString();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Worshipper.ConvertedNamesFor({worshipperId}): {e}");
                return "";
            }
        }

        /// <summary>
        /// What a worship on <paramref name="t"/> does. Impostor-team killers (vanilla impostor types, Vampire, Mafia, Witch,
        /// Assassin, the v0.5.0 impostor-pool roles, an IMPOSTOR-SIDE lover) → the worshipper dies. Team.Impostor players (the whole
        /// Madmate family, Worshipper, converts) are already on our side; desync killers (Sheriff / Jackal / Arsonist) keep a kill
        /// button on their client (2026.8.18 first-SetRole rule) and a crew-side lover carries pair state → those fail. Everything else
        /// (vanilla crew incl. vanilla crew roles, Mayor, Snitch, Lighter, SpeedBooster, Bait, Jester, Opportunist, Terrorist, Jackal Friends)
        /// is view-neutral → converted. These four refusals are the Game.ConvertRole contract.
        /// </summary>
        private static Verdict Judge(byte t)
        {
            if (Game.IsImpostorTeamKiller(t)) return Verdict.Impostor;   // incl. an impostor-side lover (IsImpostorTeamKiller → vanilla side)
            if (Game.TeamOf(t) == Team.Impostor) return Verdict.Invalid;  // Roles.IsMadType members and converts
            if (Game.IsDesyncImpostor(t)) return Verdict.Invalid;
            if (Game.RoleOf(t) == CustomRole.Lovers) return Verdict.Invalid;   // crew-side lover
            return Verdict.Convert;
        }

        // ------------------------------------------------------------------ worship

        /// <summary>
        /// Kill button pressed on <paramref name="target"/> (after Kills.IsValidMurder, so both alive, no meeting / exile /
        /// intro, target not in a vent / GA-protected). Never a vanilla kill: convert, self-destruct, or fail.
        /// </summary>
        internal static void Worship(PlayerControl worshipper, PlayerControl target)
        {
            try
            {
                if (worshipper == null || target == null) return;
                if (worshipper.Data == null || target.Data == null) return;
                byte w = worshipper.PlayerId, t = target.PlayerId;

                if (Remaining(w) <= 0)
                {
                    // The vanilla button cannot be removed (rule 2): a press after the last use is a failed kill, notice throttled.
                    // Checked BEFORE Judge on purpose: a spent Worshipper never self-destructs (SNR MadMaker has no button after its use).
                    Rpc.FailKill(worshipper, target);
                    if (ShouldNotice(w))
                        Kills.Notice(w, "kill.worship.none", "崇拝の回数を使い切りました（これ以降はキル失敗の演出だけです）。", "You have no worships left (the button only fails from now on).");
                    return;
                }

                switch (Judge(t))
                {
                    case Verdict.Impostor:
                        SelfDestruct(worshipper, t);
                        return;

                    case Verdict.Invalid:
                        // FailKill does not reset the client's kill timer, so the notice is throttled like the Mafia one.
                        Rpc.FailKill(worshipper, target);
                        if (ShouldNotice(w))
                            Kills.Notice(w, "kill.worship.invalid", "{0} は崇拝できません。", "{0} cannot be worshipped.", Game.NameOf(t));
                        return;
                }

                Rpc.ResetKillCooldown(worshipper, Options.WorshipperCooldown);   // host: SetKillTimer; client: options ×2 + FailedProtected + options back
                Convert(w, t);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Worshipper.Worship: {e}");
            }
        }

        /// <summary>
        /// SNR MadMaker rule: worshipping an impostor-team killer is fatal. Default = Sheriff-misfire visual (self-kill
        /// animation + body; OnMurder runs with killer == target → no Bait report, own desync client gets ImpostorGhost).
        /// One broadcast MurderPlayer + one private chat to the presser's client = the shipped misfire burst.
        /// </summary>
        private static void SelfDestruct(PlayerControl worshipper, byte t)
        {
            byte w = worshipper.PlayerId;
            PocketRolesPlugin.Logger.LogInfo($"Kills: Worshipper {Game.NameOf(w)} tried to worship an impostor ({Game.NameOf(t)}) → self-destruct");
            if (HostDiesByExile && worshipper.AmOwner && !Rpc.SafeMode)
            {
                // Fallback for a host whose local role is Impostor (Assassin.Kill / GameMaster.Apply pattern): Exiled() never
                // reaches Kills.OnMurder, so the death hooks and the win check run here.
                Rpc.ExileSilently(worshipper);
                try { worshipper.Data.IsDead = true; worshipper.Data.MarkDirty(); Rpc.SendPlayerInfo(worshipper.Data); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"Worshipper: data sync: {e.Message}"); }
                RoleAssignment.SendGhostRole(worshipper);
                Witch.OnPlayerDied(w);
                Arsonist.OnPlayerDied(w);
                Lovers.OnPlayerDied(w);
                NameTags.ScheduleMurderRefresh();
                Scheduler.After(0.1f, () => { if (Game.IsHostActive && Game.InProgress && !Game.Ending) WinConditions.Check(); });
            }
            else
            {
                Rpc.Kill(worshipper, worshipper);
            }
            Kills.Notice(w, "kill.worship.impostor", "{0} はインポスターでした。崇拝は失敗し、あなたは自爆しました。", "{0} was an Impostor. The worship failed and you died.", Game.NameOf(t));
        }

        /// <summary>
        /// The target becomes a literal Madmate through Game.ConvertRole (view-neutral: its vanilla Crewmate client is untouched;
        /// the helper logs, recomputes tasks, resyncs the target's options and refreshes the tags). Packets this frame: only the
        /// worshipper's client gets immediate ones (ResetKillCooldown ×2 + one chat = the live-tested Arsonist.Douse burst); options /
        /// name tags go through the paced queue; the target's chat starts one ChunkSpacing later (one client per frame).
        /// </summary>
        private static void Convert(byte w, byte t)
        {
            // The list first: ConvertRole refreshes the name tags, and the Worshipper's own Ⓜ on the convert (NameTags.NameFor) reads it
            // (static review 2026-09-10: added afterwards, the mark only showed up at the next unrelated refresh).
            if (!Game.Worshipped.TryGetValue(w, out var list)) Game.Worshipped[w] = list = new List<byte>();
            if (!list.Contains(t)) list.Add(t);
            CustomRole before = Game.ConvertRole(t, CustomRole.Madmate, "worshipped by " + Game.NameOf(w));
            int left = Remaining(w);
            PocketRolesPlugin.Logger.LogInfo($"Kills: Worshipper {Game.NameOf(w)} worshipped {Game.NameOf(t)} ({before} -> Madmate, {left} left)");

            Kills.Notice(w, "kill.worship", "{0} を崇拝しました。{0} はマッドメイトになりました（残り {1} 回）。", "You worshipped {0}: they are now a Madmate ({1} left).", Game.NameOf(t), left);

            // Target side one ChunkSpacing later; SendChunksTo's per-client NextSendAt then spaces the role text behind the notice.
            // Dropped by Scheduler.Clear() if the Check() below ends the game at once (the summary lists the convert as Madmate).
            Scheduler.After(HrChat.ChunkSpacing + 0.05f, () =>
            {
                if (!Game.IsHostActive || !Game.InProgress) return;
                var p = Game.Player(t);
                if (p == null || p.Data == null || p.Data.Disconnected) return;
                Kills.Notice(t, "kill.worshipped", "崇拝者に崇拝され、あなたはマッドメイトになりました。インポスターの名前が赤く見えます。", "A Worshipper converted you: you are now a Madmate. Impostors appear in red.");
                HrChat.SendRoleInfo(t, false);                                  // "マッドメイト [インポスター陣営] — …" in the convert's language
            });

            WinConditions.Check();                                              // crewForCount shrank → Impostor win, or the convert's tasks vanished → Crew task win (suppressed in test mode)
        }

        private static bool ShouldNotice(byte playerId)
        {
            float now = Time.time;
            if (LastFailNotice.TryGetValue(playerId, out var last) && now - last < NoticeRepeatSeconds) return false;
            LastFailNotice[playerId] = now;
            return true;
        }

        /// <summary>Clears the notice throttle (called from Kills.ResetNotices at lobby start).</summary>
        internal static void ResetNotices() => LastFailNotice.Clear();
    }
}
