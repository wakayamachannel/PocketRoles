using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using PocketRoles.Core;
using PocketRoles.Net;
using UnityEngine;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;
    using HrChat = PocketRoles.Chat.Chat;

    /// <summary>
    /// アサシン / Assassin (v0.4.1): vanilla Impostor that may guess a player's role during the voting phase of a
    /// meeting with "/cmd guess &lt;name&gt; &lt;role&gt;" (Commands: everyone command "guess"/"g"/"推理"); a correct
    /// guess kills the target, a wrong one the assassin. Guess bookkeeping: Game.GuessesThisMeeting / Game.MeetingsHeld
    /// (both maintained by Meetings_MeetingStartPatch). Normal kills go through the vanilla CheckMurder path.
    /// Command privacy: "/cmd guess" from a client reaches only the host in a registered lobby; a plain "/guess" was
    /// already delivered to every client (Chat_AddChatPatch hides it on the host's screen only), so the reply then
    /// carries the guess.public warning. The role is only assigned while player commands are enabled
    /// (<see cref="Enabled"/>; RoleAssignment skips it otherwise, because Commands.GatedPlayer would swallow the command).
    /// </summary>
    public static class Assassin
    {
        /// <summary>
        /// true = kill inside the meeting (Rpc.ExileSilently + Data sync, vote area ✕); false = the death is applied as a
        /// Game.Bites entry 2 s after the exile screen (fallback, see design §6.1). Rpc.SafeMode always uses the fallback.
        /// </summary>
        internal const bool KillDuringMeeting = true;

        /// <summary>The role can act only while player commands are enabled ([Chat] PlayerCommands + AllCommands); RoleAssignment skips it otherwise.</summary>
        internal static bool Enabled => Options.PlayerCommands && Options.AllCommands;

        /// <summary>Vanilla roles a guess may name besides plain Crewmate / Impostor (enum name or the vrole.* text in any language).</summary>
        private static readonly (RoleTypes Role, string Key, string Ja, string En)[] VanillaSpecials =
        {
            (RoleTypes.Scientist, "vrole.sci", "サイエンティスト", "Scientist"),
            (RoleTypes.Engineer, "vrole.eng", "エンジニア", "Engineer"),
            (RoleTypes.Noisemaker, "vrole.nm", "ノイズメーカー", "Noisemaker"),
            (RoleTypes.Tracker, "vrole.tr", "トラッカー", "Tracker"),
            (RoleTypes.Detective, "vrole.det", "探偵", "Detective"),
            (RoleTypes.Judge, "vrole.judge", "ジャッジ", "Judge"),
            (RoleTypes.Shapeshifter, "vrole.ss", "シェイプシフター", "Shapeshifter"),
            (RoleTypes.Phantom, "vrole.ph", "ファントム", "Phantom"),
            (RoleTypes.Viper, "vrole.vp", "ヴァイパー", "Viper"),
        };

        // ------------------------------------------------------------------ command

        /// <summary>
        /// Handles "/cmd guess &lt;name&gt; &lt;role&gt;" from <paramref name="sender"/>; returns the private reply text ("" = none).
        /// Runs inside the sender's Lang.Scope (Commands.Handle), so every Lang.T here is in the sender's language.
        /// </summary>
        internal static string Guess(PlayerControl sender, string args, bool explicitCmd)
        {
            try
            {
                if (sender == null || sender.Data == null) return "";
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending)
                    return Lang.T("cmd.nogame", "試合が始まってから使えます。", "Available once the game has started.");

                byte s = sender.PlayerId;
                if (Game.RoleOf(s) != CustomRole.Assassin)
                    return Lang.T("guess.notassassin", "アサシンだけが使えます。", "Only the Assassin can guess.");
                if (!Game.IsAlive(s))
                    return Lang.T("guess.dead", "死亡しているため推理できません。", "You are dead and cannot guess.");

                var hud = MeetingHud.Instance;
                if (hud == null || !IsVotingState(hud.state))
                    return Lang.T("guess.nomeeting", "会議の投票中だけ使えます。", "Only during the voting phase of a meeting.");
                if (!Options.AssassinCanGuessFirstMeeting && Game.MeetingsHeld <= 1)
                    return Lang.T("guess.firstmeeting", "最初の会議では推理できません。", "No guessing in the first meeting.");

                int limit = Options.AssassinGuessesPerMeeting;
                Game.GuessesThisMeeting.TryGetValue(s, out int used);
                if (used >= limit)
                    return Lang.TF("guess.limit", "この会議の推理回数（{0}回）を使い切りました。", "You used all {0} guess(es) of this meeting.", limit);

                string[] tokens = SplitArgs(args);
                if (tokens.Length < 2)
                    return Lang.T("guess.usage",
                        "使い方: /cmd guess <名前|番号> <役職>  例: /cmd guess Taro sheriff（crew / impostor も可）",
                        "Usage: /cmd guess <name|id> <role>  e.g. /cmd guess Taro sheriff (crew / impostor allowed)");

                // Last token = role, everything before it = the player (names may contain spaces).
                string roleText = tokens[tokens.Length - 1];
                string name = string.Join(" ", tokens, 0, tokens.Length - 1);
                var target = TestMode.FindPlayer(name);
                if (target == null || target.Data == null)
                    return Lang.TF("guess.noplayer", "プレイヤー「{0}」が見つかりません。", "Player \"{0}\" not found.", name);

                byte t = target.PlayerId;
                if (t == s)
                    return Lang.T("guess.self", "自分は推理できません。", "You cannot guess yourself.");
                if (!Game.IsAlive(t) || (Game.GameMasterActive && Game.IsHost(t)))
                    return Lang.TF("guess.targetdead", "{0} はすでに死亡しています。", "{0} is already dead.", Game.NameOf(t));
                if (!TryParseGuess(roleText, out CustomRole custom, out RoleTypes vanilla, out string label))
                    return Lang.TF("guess.badrole", "役職が見つかりません: {0}（/cmd r で一覧。crew / impostor も可）", "Unknown role: {0} (/cmd r lists them; crew / impostor allowed)", roleText);

                // The guess counts from here on, whatever the outcome.
                Game.GuessesThisMeeting[s] = used + 1;
                bool correct = Matches(t, custom, vanilla);
                byte victim = correct ? t : s;
                string targetName = Game.NameOf(t);
                Kill(s, t, victim, label, correct);

                string reply = correct
                    ? Lang.TF("guess.correct", "正解！{0} は {1} でした。", "Correct! {0} was {1}.", targetName, label)
                    : Lang.TF("guess.wrong", "外れ！{0} は {1} ではありません。あなたが死亡しました。", "Wrong! {0} is not {1}. You die.", targetName, label);
                if (!explicitCmd && !sender.AmOwner)
                    reply += "\n" + Lang.T("guess.public",
                        "注意: /cmd を付けずに打ったため全員に見えています。次からは /cmd guess を使ってください。",
                        "Note: without /cmd everyone saw your guess; use /cmd guess next time.");
                return reply;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Assassin.Guess: {e}");
                return "";
            }
        }

        // ------------------------------------------------------------------ kill

        /// <summary>
        /// Applies the death of <paramref name="victim"/> (the target of a correct guess, else the assassin). In the
        /// meeting: Exiled RPC + local Exiled() (no body, GameMaster precedent), the player info synced as dead so every
        /// client's vote area shows ✕ (vanilla MeetingHud.Update), the ghost role, its vote dropped and the vote ended
        /// when everyone else already voted. Fallback (KillDuringMeeting = false / Rpc.SafeMode / no PlayerControl):
        /// a Game.Bites entry that Meetings_ExileWrapUpPatch pushes to 2 s after the exile screen.
        /// </summary>
        private static void Kill(byte assassinId, byte targetId, byte victim, string label, bool correct)
        {
            var pc = Game.Player(victim);
            bool now = KillDuringMeeting && !Rpc.SafeMode && pc != null && pc.Data != null;
            if (now)
            {
                // A dead host cannot send chat (Rpc.TempReviveHostForChat aside): announce before it is marked dead.
                bool hostVictim = pc.AmOwner;
                if (hostVictim)
                    HrChat.All(HrChat.Title, () => Lang.TF("guess.killed.all", "{0} は暗殺されました。", "{0} was assassinated.", Game.NameOf(victim)));

                Rpc.ExileSilently(pc);   // Exiled RPC to all + local pc.Exiled(): dies without a body
                Game.CountKill(assassinId);          // post-game kill count (review 2026-09-10)
                RoleReveal.OnKilled(victim);         // [Roles] RevealRoleOnDeath, like any MurderPlayer
                try { pc.Data.IsDead = true; pc.Data.MarkDirty(); Rpc.SendPlayerInfo(pc.Data); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"Assassin: data sync: {e.Message}"); }
                RoleAssignment.SendGhostRole(pc);   // per-viewer ghost roles (ImpostorGhost for an impostor victim)

                var hud = MeetingHud.Instance;
                if (hud != null)
                {
                    try
                    {
                        hud.RpcClearVote(victim);          // drop the vote it may have cast (the Mayor tally also skips AmDead)
                        UnsetVoteLocally(hud, victim);     // also marks the area dead, so the end check below sees it
                        hud.CheckForEndVoting();           // end the vote now if everyone else already voted
                    }
                    catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"Assassin: vote clear: {e.Message}"); }
                    // Second chance after vanilla MeetingHud.Update has processed the Data sync (SetDead on every client).
                    Scheduler.After(0.2f, () =>
                    {
                        try { if (MeetingHud.Instance == hud && Game.InProgress && hud.state < MeetingHud.MeetingStates.Results) hud.CheckForEndVoting(); }
                        catch (Exception) { }
                    }, "assassin.endvote");
                }

                // Exiled() never reaches Kills.OnMurder, so the death hooks run here.
                Witch.OnPlayerDied(victim);
                Arsonist.OnPlayerDied(victim);
                Lovers.OnPlayerDied(victim);
                // A Terrorist with all tasks done wins even when assassinated (Kills.OnMurder equivalent); the game
                // ends in Meetings_ExileWrapUpPatch like a pending Jester win.
                if (Game.RoleOf(victim) == CustomRole.Terrorist && Game.TasksDone(victim) && Game.SoloWinner == CustomRole.None)
                {
                    Game.SoloWinner = CustomRole.Terrorist;
                    Game.SoloWinnerId = victim;
                    PocketRolesPlugin.Logger.LogInfo($"Assassin: Terrorist {Game.NameOf(victim)} was assassinated with all tasks done → solo win pending");
                }
                NameTags.ScheduleMurderRefresh();   // the Data sync resets private names
                if (!hostVictim)
                    HrChat.All(HrChat.Title, () => Lang.TF("guess.killed.all", "{0} は暗殺されました。", "{0} was assassinated.", Game.NameOf(victim)));
            }
            else
            {
                if (Rpc.SafeMode) PocketRolesPlugin.Logger.LogInfo("Assassin: safe mode, the death is applied after the meeting");
                else if (pc == null) PocketRolesPlugin.Logger.LogWarning($"Assassin: no PlayerControl for #{victim}, the death is applied after the meeting");
                if (!Game.Bites.ContainsKey(victim))   // already dying (vampire / lover): it dies anyway
                    Game.Bites[victim] = new Game.VampireBite { Killer = assassinId, DueAt = Time.time, Reason = "assassin" };   // executes 2 s after the exile screen
                HrChat.All(HrChat.Title, () => Lang.TF("guess.killed.later", "{0} は暗殺されました。会議の後に死亡します。", "{0} was assassinated and dies after the meeting.", Game.NameOf(victim)));
            }
            PocketRolesPlugin.Logger.LogInfo($"Assassin: {Game.NameOf(assassinId)} guessed {Game.NameOf(targetId)} = {label} → {(correct ? "correct" : "wrong")}; {Game.NameOf(victim)} dies ({(now ? "now" : "after the meeting")})");
        }

        /// <summary>
        /// Host-side copy of the victim's vote area: a vote already cast must not reach the tally, and the area is marked
        /// dead right away (vanilla only calls SetDead from MeetingHud.Update next frame, so an immediate
        /// CheckForEndVoting would still count the victim as a living non-voter).
        /// </summary>
        private static void UnsetVoteLocally(MeetingHud hud, byte victim)
        {
            var areas = hud.playerStates;
            if (areas == null) return;
            foreach (var pva in areas)
            {
                if (pva == null || (byte)pva.PlayerId != victim) continue;
                if (pva.DidVote) pva.UnsetVote();
                pva.SetDead(true);
                return;
            }
        }

        // ------------------------------------------------------------------ parsing / matching

        private static bool IsVotingState(MeetingHud.MeetingStates s)
        {
            return s == MeetingHud.MeetingStates.Discussion
                || s == MeetingHud.MeetingStates.NotVoted
                || s == MeetingHud.MeetingStates.Voted;
        }

        private static readonly char[] Separators = { ' ', '　', '\t' };

        private static string[] SplitArgs(string args)
        {
            if (string.IsNullOrWhiteSpace(args)) return Array.Empty<string>();
            return args.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Role text of a guess → a custom role (label = its localized name) or a vanilla RoleTypes (label =
        /// Chat.VanillaRoleName). Keywords first, then the vanilla specials, then Roles.TryParse: the zh prefix match
        /// of Roles.TryParse would otherwise turn "内鬼" (Impostor) into Madmate.
        /// </summary>
        internal static bool TryParseGuess(string text, out CustomRole custom, out RoleTypes vanilla, out string label)
        {
            custom = CustomRole.None;
            vanilla = RoleTypes.Crewmate;
            label = "";
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            string lower = t.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");

            switch (lower)
            {
                case "crew": case "crewmate": case "c": case "クルー": case "クルーメイト": case "船员":
                    vanilla = RoleTypes.Crewmate;
                    label = HrChat.VanillaRoleName(vanilla);
                    return true;
                case "imp": case "impostor": case "i": case "インポスター": case "内鬼":
                    vanilla = RoleTypes.Impostor;
                    label = HrChat.VanillaRoleName(vanilla);
                    return true;
            }
            if (MatchesVanillaText(t, lower, "vrole.crew", "クルー", "Crewmate")) { vanilla = RoleTypes.Crewmate; label = HrChat.VanillaRoleName(vanilla); return true; }
            if (MatchesVanillaText(t, lower, "vrole.imp", "インポスター", "Impostor")) { vanilla = RoleTypes.Impostor; label = HrChat.VanillaRoleName(vanilla); return true; }

            foreach (var v in VanillaSpecials)
            {
                if (lower == v.Role.ToString().ToLowerInvariant() || MatchesVanillaText(t, lower, v.Key, v.Ja, v.En))
                {
                    vanilla = v.Role;
                    label = HrChat.VanillaRoleName(v.Role);
                    return true;
                }
            }

            if (Roles.TryParse(t, out custom) && custom != CustomRole.None)
            {
                label = Roles.Info(custom).Name;
                return true;
            }
            custom = CustomRole.None;
            return false;
        }

        /// <summary>True when <paramref name="t"/> equals the vrole text of <paramref name="key"/> in any supported language (or the inline ja/en default).</summary>
        private static bool MatchesVanillaText(string t, string lower, string key, string ja, string en)
        {
            if (t == ja || lower == en.ToLowerInvariant().Replace(" ", "")) return true;
            foreach (var lang in Lang.Supported)
            {
                string v = Lang.Lookup(lang, key);
                if (string.IsNullOrEmpty(v)) continue;
                if (string.Equals(v.Trim(), t, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Does the guess fit player <paramref name="t"/>? A custom guess must equal its custom role; a vanilla guess
        /// needs no custom role (an impostor lover or a Witch is not "impostor"), "impostor" covers every vanilla
        /// impostor kind, any other vanilla guess must match exactly.
        /// </summary>
        private static bool Matches(byte t, CustomRole custom, RoleTypes vanilla)
        {
            CustomRole tr = Game.RoleOf(t);
            if (custom != CustomRole.None) return tr == custom;
            if (tr != CustomRole.None) return false;
            RoleTypes tv = Game.VanillaRoleOf(t);
            if (vanilla == RoleTypes.Impostor) return RoleAssignment.IsImpostorRole(tv);
            return tv == vanilla;
        }
    }
}
