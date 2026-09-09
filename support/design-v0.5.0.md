# PocketRoles v0.5.0 — implementation plan: SNR-inspired roles for the thin factions (マッドメイヤー / マッドスタントマン / マッドホーク / 崇拝者 / ジャッカルフレンズ / イビルホーク / イビル猫又 / シリアルキラー / 侍)

Everything below was verified against the tree at `C:\Users\riotgames\Desktop\HostRoles` (csproj / plugin **v0.4.3**, game 2026.8.18, lang files 784 keys) on 2026-09-09 by the nine role specs and the six subsystem ground-truth passes this document merges. Line numbers are those of the files at design time; **anchor every Edit on the quoted code, never on the numbers** — the nine roles shift them, and Implementer A's shared edits shift them again before B/C/D start. Style and depth follow `support/design-v0.4.1.md`.

Hard constraints (verified live; not re-litigated anywhere below): (1) only the host runs the mod — every ability is expressed through the vanilla kill button (`ImpostorDesync`), per-client option desync, per-viewer name strings, private host chat, host-executed deaths, vote tallies and host-side win checks; (2) a 2026.8.18 client applies only the FIRST `SetRole` it receives for a living player, so a living player's vanilla-visible role is fixed at dispatch (ghost roles still apply); (3) the unregistered compat lobby assigns no roles; (4) every delayed / conditional death is a `Game.Bites` entry; (5) every user-visible string is trilingual via `Lang.T` / `Lang.TF` + the three JSON files; (6) this document is the single implementation plan — the role specs it was merged from are superseded where §0 "Decisions" says so.

---

## 0. Ground truth the implementers rely on (read once)

| Concern | Where / signature | Facts |
|---|---|---|
| Version | `PocketRoles.csproj:10` `<Version>0.4.3</Version>`; `src/PocketRolesPlugin.cs:23` `public const string Version = "0.4.3";`, `:24 SupportedGameVersion = "2026.8.18"`; `CHANGELOG.md:5` `## v0.4.3 — 2026-09-09` (newest); README*.md "current version" strings still say **0.4.1** (0.4.2/0.4.3 never touched them); `PocketRolesLauncher.ps1:30 $script:LauncherVersion = '0.4.0'` (launcher's own, untouched). | Batch target = **v0.5.0** (`support/worklog-2026-09-09.md:40-55`). README role count "17" → **26**. |
| Role table | `src/Core/Roles.cs:8-16` `enum CustomRole { None=0, Sheriff, Mayor, Snitch, Lighter, SpeedBooster, Bait, Madmate, Vampire, Mafia, Jester, Opportunist, Terrorist, Jackal, Lovers, Arsonist, Witch, Assassin }`; `sealed class RoleInfo { Id, Key, NameJa/En/Zh, DescJa/En/Zh, Team, Color, IsKiller, ImpostorDesync, CanVent, CanSabotage, TasksCount, FromImpostorPool, Aliases }`, `CompatRisky => IsKiller && !FromImpostorPool`, `Name => Lang.T("role."+Key+".name", …)`, `Desc`; `Roles.All` (rows grouped crew → impostor-pool → neutral; Madmate row ends `Aliases = new[] { "mad", "mm" } },` line 120, Assassin row ends `Aliases = new[] { "as", "asn" } },` line 144, Jackal row ends `Aliases = new[] { "jk" } },` line 168, Arsonist row last); `Roles.Info(r)` never null; colours `ImpostorColor "#ff1919"`, `CrewColor "#8cffff"`, `JackalColor "#00b4eb"`, `SnitchColor`, `LoversColor`, `ArsonistColor`; `Roles.TeamColor(Neutral) = "#00ff00"`; `Roles.ColoredName(CustomRole)` line 234. | `Roles.All` order = `/cmd r` order inside a team, `/show`, `Chat.EnabledRoleNames`, config bind order, settings-tab row order, README §10 row order, **and the first-match order of `TryParse`'s prefix passes**. |
| `Roles.TryParse` | lines 198-232: `lower = t.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "")`; pass 1 per row: `Key == lower`, `NameEn` lowercased/despaced, `NameJa == t`, `NameZh == t`, enum name, aliases; pass 2: `t.Length >= 2 && NameJa.StartsWith(t)` **first row wins**; pass 3: `t.Length >= 1 && NameZh.StartsWith(t)` first row wins. Existing aliases: `sh my sn lt sb speed booster bt mad mm vamp vp mf wt wi as asn js opp op terror tr jk lover lv love arso ars`. Existing zh first characters: 警 市 告 点 增 诱 内 吸 黑 女 刺 小 投 恐 豺 恋 纵. | Consumed by `/set`, `/opt <role>.count|chance`, `/cmd r`, `/assign` (last token = role), `Commands.IsAdminOptKey` fallback, `Assassin.TryParseGuess` (after its keyword table; `Assassin.Matches` compares `RoleOf(t) == custom` exactly, so a guess must name the exact custom role; a vanilla `impostor`/`crew` guess on any custom role is wrong). `Key` must not be another key + "." prefix (`SettingsTab.RoleOf`: `d.Key.StartsWith(r.Key + ".")`). |
| RoleInfo flags at runtime | `IsKiller`: only `Kills.HandleCheckMurder` `if (!info.IsKiller) return true;` (+ `SettingsTab.RoleHelp`). `ImpostorDesync`: `Game.IsDesyncImpostor` → `RoleAssignment.View` (own view Impostor, others see Crewmate, viewer sees every impostor as Crewmate), host-target block, `Win_RecomputeTaskCountsPatch` skip, `Chat.RoleInfoText` desync line. `FromImpostorPool`: pool choice + `TestMode.ApplyForcedRoles` basis. `CanVent` / `CanSabotage`: `Kills.CanVent/CanSabotage` (None → true, Lovers → true, Jackal/Arsonist → option, else the flag). `TasksCount`: `Win_RecomputeTaskCountsPatch` (`TeamOf != Crew → skip` first). `Team`: `Game.TeamOf`, `ComputeWinners(Crew/Impostor)`, task counting, `Roles.TeamName/TeamColor`, `/cmd r` grouping. | A crew-pool role gets a kill button only with `IsKiller = true, ImpostorDesync = true` (Sheriff/Jackal/Arsonist/Worshipper); an impostor-pool role keeps the vanilla Impostor button. |
| Game state | `src/Core/GameState.cs` `static class Game` (namespace `PocketRoles.Core`; file has `using PocketRoles.Game; using PocketRoles.Net; using UnityEngine;`): `Roles : Dictionary<byte, CustomRole>` (plain, public — no writer after dispatch today), `VanillaRoles`, `OriginalNames`, `struct VampireBite { byte Killer; float DueAt; string Reason; }` + `Bites` (keyed by **victim**), v0.4.1 block `LoverA/LoverB, Doused, Spelled, GuessesThisMeeting, MeetingsHeld` (73-83), `IsLover/PartnerOf` (85-86), `private static void ResetRoleState()` (88-95; called from `Reset()` and `ResetForNewLobby` after `Bites.Clear()`), `ExtraWinners`, `SoloWinner : CustomRole`, `SoloWinnerId`, `LastExiled`, `GameScopedTags = { "win.end", "assign.roleinfo", "assign.introend", "haison.end", "antiblackout.restore", "names.meeting", "gm.apply" }` (244-247, cancelled at LobbyStart), `IsImpostorTeamKiller` (279-286, quoted in §1.3), `IsDesyncImpostor`, `IsJackal(id) => RoleOf(id) == CustomRole.Jackal` (290), `IsNonCrewKiller`, `TeamOf` (custom → `RoleInfo.Team`, else vanilla side), `IsDead` (honours `AntiBlackout.RealIsDead` for snapshot-dead players and `Rpc.HostTempRevived`), `IsAlive` (info exists, `!Disconnected`, `!IsDead`), `IsHost`, `Info(id)`, `Player(id)`, `AllPlayers()`, `AllPlayerIds()`, `NameOf`, `TasksLeft/TasksTotal/TasksDone`, `GameMasterActive`, `TestMode`, `Ending`, `InProgress`, `IsHostActive`. Inside `PocketRoles.Game` files: `using Game = PocketRoles.Core.Game; using HrChat = PocketRoles.Chat.Chat;` (bare `Game`/`Chat` resolve to the sibling namespaces). Inside class `Game` the role table is `Core.Roles.…` (the class has a `Roles` field). | New per-game state goes next to the v0.4.1 block and into `ResetRoleState()`. |
| First-SetRole rule | `RoleAssignment.cs:646-653` comment; `DispatchInitialRoles` sends each client its whole table with `canOverride: false`; `SendGhostRole` / AntiBlackout send later `SetRole`s (dead players; temporary exile-screen views). | A mid-game `Game.Roles[id] = X` is **view-neutral only when old and new role share `ImpostorDesync`** and `VanillaRoles[id]` is untouched (vanilla Crewmate ↔ Crewmate). Consumers read `RoleOf` live; the refresh list is in §1.3 `ConvertRole`. |
| Assignment | `src/Game/RoleAssignment.cs`: `Assign_SelectRolesPatch` (prefix `Game.Reset()`, compat early return, `AssigningRoles`, `OptionsDesync.Capture()`; postfix → `DispatchInitialRoles()`); `DispatchInitialRoles` (128-227): `TestMode.ApplyForcedRoles(players)` (unconditional, test mode or not) → `EnsureImpostorPresent` (promotes an unforced player to vanilla Impostor when a forced role removed the last one; warning log) → `AssignCustomRoles` (pools `plainCrew` / `plainImp` of exactly `Crewmate` / `Impostor`; forced ids excluded; killers-first `if (r.Id == CustomRole.Jackal \|\| r.Id == CustomRole.Sheriff \|\| r.Id == CustomRole.Arsonist) order.Add(r);` line 288, `Shuffle(order); Shuffle(rest);`; `Lovers.Assign(plainCrew, plainImp, Rand);` before the loop; loop `foreach (var role in order) { if (role.Id == CustomRole.Lovers) continue; if (Roles.IsCompatBlocked(role.Id)) continue; if (role.Id == CustomRole.Assassin && !Assassin.Enabled) { … continue; } int count = Options.Count(role.Id); int chance = Options.Chance(role.Id); var pool = role.FromImpostorPool ? plainImp : plainCrew; for (…) { … Core.Game.Roles[id] = role.Id; } }`) → `LogAssignment` (`#id name (client N): <Vanilla> -> <NameEn> [<Team>]`, then the Lovers line ending `.Append(" & #").Append(Core.Game.LoverB).Append(' ').Append(Core.Game.NameOf(Core.Game.LoverB));`) → per-client `Rpc.MultiBatch` (paced, `Rpc.Queue.Interval = 0.3f` per client; own role last) → `Core.Game.InProgress = true;` → `RecomputeTaskCounts()` → `NameTags.RefreshAll(force: true); OptionsDesync.ResyncAll();` → +8 s `SendRoleInfoToAll(false)` + resync + refresh (`"assign.roleinfo"`). `Assign_IntroCutsceneOnDestroyPatch` (+1 s after the host's intro: `NameTags.RefreshAll(force: true); OptionsDesync.ResyncAll();`, tag `"assign.introend"`). `View(viewerId, targetId)` (30-53): dead → desync viewer `CrewmateGhost`, own desync `ImpostorGhost`, else `IsImpostorTeamKiller(target) ? ImpostorGhost : CrewmateGhost`; alive → own desync `Impostor`, desync target `Crewmate`, desync viewer sees vanilla impostors as `Crewmate`, else `LiveRole(vanilla)`. `SendGhostRole(dead)` (373-400). `Assign_RpcSetRolePatch` (664-680) redirects vanilla ghost / GA sends. | One custom role per player. `Core.Game.InProgress` is already true several frames **before** `IntroCutscene` exists. Game Master host: never assigned. |
| Test mode | `src/Game/TestMode.cs`: `TryAssign` writes `Core.Game.ForcedRoles[id]` **unconditionally** (reply adds `test.assign.hint` when test mode is off); `ApplyForcedRoles` (143-191): `info.FromImpostorPool && !vanillaImp → basis Impostor`; `!info.FromImpostorPool && vanillaImp && role != Lovers → basis Crewmate`; local `Rpc.ApplyRoleLocal`; log `TestMode: forced #id name -> <NameEn> (vanilla X -> Y)`; never demotes an unforced vanilla impostor. `Set(false)` **clears `ForcedRoles`** (line 30); `/test` is refused in-game. `TestMode_AdjustedNumImpostorsPatch` keeps ≥ 1 vanilla impostor (≤ (players−1)/2) while on; `MinPlayers = 1`. **`WinConditions.CheckNow` returns right after the sabotage check while `Core.Game.TestMode`** (`WinConditions.cs:72-77`): count-based outcomes are never evaluated, `WouldContinue` returns `true`, `EndFromVanilla` logs `ignored: test mode`; the `EndGame(X) suppressed: test mode (use /end)` line exists only for direct `EndGame` callers (Terrorist / Jester / Arsonist paths). `/start` = `AutoStart.ForceStart` → `MinPlayers = 1` with test mode off (small-lobby popup auto-confirmed while forcing). `FindPlayer` internal. | Every count rule of this batch is observable only with `/test off` (or through the §1.8 test-mode count line). |
| Kill interception | `src/Game/Kills.cs` `[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckMurder))] Kills_CheckMurderPatch.Prefix` → `HandleCheckMurder(killer, target)` for remote and host presses. Order (each line an anchor): `if (killer == null) return true;` → `if (Game.Ending) { FailKill; return false; }` (176) → `var role = Game.RoleOf(killerId);` → host-target block `if (target != null && (role == CustomRole.None \|\| role == CustomRole.Mafia \|\| role == CustomRole.Assassin \|\| role == CustomRole.Lovers) && Game.IsHost(target.PlayerId) && Game.IsDesyncImpostor(target.PlayerId)) { bool allowed = Game.IsImpostorTeamKiller(killerId) && IsValidMurder(killer, target) && (role != CustomRole.Mafia \|\| !AnyOtherImpostorKillerAlive(killerId)); … Rpc.Kill / Rpc.FailKill; return false; }` (186-203) → `if (role == CustomRole.None) return true;` (205) → `var info = Roles.Info(role); if (!info.IsKiller) return true;` (206-207) → `if (!IsValidMurder(killer, target)) { FailKill; return false; }` → `switch (role)` cases Sheriff / Jackal / Vampire / Mafia / Arsonist / Witch (268-270 `case CustomRole.Witch: Witch.Spell(killer, target); return false;`), `default: return true; // Assassin: vanilla kill …` (272-273). Return `true` = vanilla `CheckMurder` continues; `false` = handled (the method must have sent `Kill`/`FailKill`/`ResetKillCooldown`). | `IsValidMurder` (131-142, private): game running, no meeting/exile/intro, both `Data` non-null, not self, both `IsAlive`, `!target.inVent`, `!target.protectedByGuardianThisRound`. Kill distance is never validated by the mod. `PlayerControl` also exposes `inMovingPlat`, `onLadder`, `walkingToVent`, `killTimer`, `SetKillTimer(float)`, `GetTruePosition()` (metadata only). |
| Kill patterns | `src/Net/Rpc.cs`: `Kill(killer, target)` = `killer.RpcMurderPlayer(target, true)` (immediate broadcast `MurderPlayer(Succeeded)`, host-local `MurderPlayer` → `Kills_MurderPlayerPatch`); `FailKill` = `RpcMurderPlayer(target, false)` (no client timer reset → throttle notices 10 s); `ResetKillCooldown(killer, cd)` = host `SetKillTimer(cd)`; client: `OptionsDesync.SendTo(killer, BuildFor(id, cd * 2f), true)` + `MurderPlayer(FailedProtected, target = killer)` to that client + `BuildFor(id, cd)` 0.5 s later (2 urgent GameDataTo + 1 targeted RPC; a `FailedProtected` on the killer halves its timer from its `KillCooldown` option; shows a brief shield flash); self-kill `Rpc.Kill(victim, victim)` (self-kill animation + body; `OnMurder` with `killer == target`; live-verified for phones and for the host); `ExileSilently(pc)` (Exiled RPC + local `Exiled()`, no body, skipped in `SafeMode`; never reaches `OnMurder` — hooks by hand, Assassin/GameMaster template); `SendPlayerInfo(info)`; `BootFromVent`. `MurderResultFlags { Succeeded=1, FailedError=2, FailedProtected=4, DecisionByHost=8 }`. | The 0.5 s re-send of `ResetKillCooldown` is `BuildFor(id, cd)` → any per-role vision/speed line must live inside `OptionsDesync.BuildFor`'s switch or it is lost after every reset. |
| `OnMurder` | `Kills_MurderPlayerPatch` → `OnMurder(killer, target, flags)` (279-317, Succeeded only): bite credit (`reporterId = bite.Killer` for self-kills, entry removed) → `Witch.OnPlayerDied(targetId); Arsonist.OnPlayerDied(targetId); Lovers.OnPlayerDied(targetId);    // partner follows …` (295-297) → Terrorist `EndGame` → Bait `ForceReport` +0.2 s (319-…; `if (MeetingHud.Instance != null \|\| ExileController.Instance != null) return;` line 324) → `Scheduler.After(WinCheckDelay = 0.1f, () => { … WinConditions.Check(); });`. `NameTags_MurderPlayerPatch` → `ScheduleMurderRefresh()` (0.5 s, deduped). Vanilla ghost `RpcSetRole` → `SendGhostRole`. | Every successful kill with `killer != target` is where a killer's timer can be restarted (Serial Killer). |
| Bites | `Kills.Tick()` (36-51): `if (Game.Bites.Count == 0) return;` first, then `!IsHostActive \|\| !InProgress \|\| Ending`, then `MeetingHud/ExileController/IntroCutscene.Instance != null` → executes due entries `for (int i = 0; i < Scratch.Count; i++) ExecuteBite(Scratch[i], true);` (49). `ExecuteBite` (340-360): dead/missing → removed; `if (allowPostpone && victim.inVent)` → +1 s; log `Kills: {(bite.Reason ?? "bite")} on {victim} (by {Killer}) executes`; `Rpc.Kill(victim, victim)`. `FlushBites()` (54-67): every entry at once, `DueAt` ignored, then `Bites.Clear()`; called from `Meetings_ReportDeadBodyPatch` (the reporter's own bite is parked with `DueAt = now`). `Meetings_ExileWrapUpPatch` pushes every bite to `Time.time + 2f`. Driver: `src/PocketRolesPlugin.cs:333` `if (Core.Game.IsHostActive) Kills.Tick();` inside `Plugin_TickPatch` (`HudManager.Update` postfix, after `Scheduler.Tick()` and `Rpc.Queue.Tick()`). `Scheduler.After(sec, action, tag)`, `Cancel(tag)`, `HasTag`, `Clear()`. | A `Bites` entry is "dies at the latest at the next report": a countdown that must not fire at a report (Serial Killer) needs its own tick. Several bites due in one frame = several broadcast `MurderPlayer` in one frame (live-tested with one victim only). |
| Meeting pipeline | `src/Game/Meetings.cs`: `Meetings_ReportDeadBodyPatch.Prefix` (`Rpc.CancelTempRevive(); FlushBites; NameTags.RefreshAll(meeting: true, urgent: false)`); `Meetings_MeetingStartPatch` (`AntiBlackout.Prepared = false; MeetingsHeld++; GuessesThisMeeting.Clear();` reminder +1 s); `Meetings_CheckForEndVotingPatch.Prefix` → `if (!Meetings.AliveMayorVoted(hud)) return true; return !Meetings.TryEndVotingWithMayor(hud);`; `TryEndVotingWithMayor` (41-136, quoted in §1.7) builds `states` (+ duplicate `VoterState` per extra Mayor vote → extra vote icons on every client), `AntiBlackout.Prepare(exiledId); hud.RpcVotingComplete(arr, exiled, tie, wasOverruled, nonce);`; `AliveMayorVoted` (139-156; `if (Options.MayorVotes <= 1) return false;` first); `IsCountedVote` (33-36, skip counts); `Meetings_VotingCompletePatch.Postfix(MeetingHud __instance, NetworkedPlayerInfo exiled)` (253-284: `LastExiled` → `if (exiledId != 255) { Witch.OnPlayerExiled; Arsonist.OnPlayerDied; Lovers.OnPlayerExiled; }` → `if (!AntiBlackout.Prepared) AntiBlackout.Prepare(exiledId);` → Jester pending solo); `Meetings_ExileWrapUpPatch.Postfix` (287-346, quoted in §1.7): `Restore` +1.5 s → bites → +2 s → Jester/Terrorist pending `EndGame` → exiled Terrorist → `WinConditions.CheckNow(); if (!InProgress) return;` → `Witch.OnMeetingEnd(exiledId);` → `OptionsDesync.ResyncAll();` → +2 s `{ ResyncAll(); NameTags.RefreshAll(force: true); WinConditions.Check(); }`. Vanilla `MeetingHud.VotingComplete(Il2CppStructArray<MeetingHud.VoterState> states, NetworkedPlayerInfo exiled, bool tie, bool wasOverruled, ushort overruleNonce)` (interop metadata; Harmony binds `states` by name); `MeetingHud.VoterState { byte VoterId; byte VotedForId; }`; `PlayerVoteArea.PlayerId/VotedForId/DidVote/AmDead/DidReport` (`PlayerId` struct ↔ byte implicit). `MeetingTools.EndMeetingNow()` = `ForceSkipAll(); CheckForEndVoting();`. | The exiled player is still `IsAlive` at `VotingComplete` (vanilla exiles at WrapUp); a bite registered there cannot execute before WrapUp + 2 s. Nothing stores votes outside the meeting's lifetime. |
| AntiBlackout | `src/Game/AntiBlackout.cs` `Prepare(exiledId)` (snapshot `RealIsDead`, `WouldContinue(exiledId)`, temporary per-viewer `SetRole`s for the exile screen, `Active`, `Prepared`), `Restore()` (+1.5 s after WrapUp; `RefreshAll(force: true)` +0.5 s), `PickDummy` prefers `IsImpostorTeamKiller`. | Deaths between `Prepare` and the clients' `WrapUp` are outside its model → every meeting-end death is a bite executed at WrapUp + 2 s (after `Restore`), and chat from a dead host at WrapUp+0 would open `Rpc.TempReviveHostForChat` inside that window. |
| Wins | `src/Game/WinConditions.cs`: `enum WinKind { Crew, Impostor, Jackal, Jester, Terrorist, Lovers, Arsonist }`, `Outcome { Continue, Crew, Impostor, Jackal, Lovers, Arsonist }`, `IsSoloKind`; `Check()` (49-63: guards `IsHostActive/InProgress/Ending`, `HaisonActive`, `MeetingHud/ExileController.Instance`, `SoloWinner != None` → `CheckNow()`); `CheckNow()` (66-84, test mode → sabotage only); `WouldContinue(exiledId)` (87-99, public); `ThrottledCheck()` (0.25 s, from the `LogicGameFlowNormal.CheckEndCriteria` prefix); `Evaluate(exiledId)` (Arsonist / Lovers overrides on `EvaluateBase`); `EvaluateBase` (238-274, quoted in §1.8); `EndGame(kind, soloId)` (sets `Ending`, `InProgress = false`, ghost roles for everyone, `RpcEndGame` after 0.4 s; log `EndGame: {kind} solo={id} winners=[ids] text=…`); `ComputeWinners` (278-306: `Crew → TeamOf == Crew`, `Impostor → TeamOf == Impostor` dead or alive, `Jackal → role == Jackal && (IsAlive \|\| id == soloId)`, solo kinds `id == soloId`, `Lovers → role == Lovers`, alive Opportunist added); `BuildSummary` (`win.*` keys, `☆`/`×` summary via `Chat.SummaryText`); `EndFromOutcome`; `Win_RecomputeTaskCountsPatch` (519-577: counts only alive, `TeamOf == Crew`, `TasksCount`, `!IsDesyncImpostor`; phantom `1/0` when total is 0); `Win_LobbyStartPatch` (`NameTags.RestoreAll(); OptionsDesync.RestoreAll();`). `_evalSoloId` line 23, `_endedByMod`, `OnLobby()`. | `madmate++` is an exact `== CustomRole.Madmate` compare today; `IsJackal` exact; no team abstraction beyond `Team { Crew, Impostor, Neutral }`. |
| `IsImpostorTeamKiller` consumers | `EvaluateBase` (`imp`), `View` (ghost kind), `Assign_RpcSetRolePatch` (GA redirect), `NameTags` rules 2/3/5, `Kills.CanSheriffKill` (110-117: `IsImpostorTeamKiller → true; Jackal \|\| Arsonist → true; Madmate && Options.SheriffCanKillMadmate → true`), `AnyOtherImpostorKillerAlive` (Mafia gate), host-target `allowed`, `AntiBlackout.PickDummy`, `Lovers.Assign` log. | Every impostor-pool killer of this batch must be listed (§1.3) or it counts as crew, gets `CrewmateGhost`, is not red for Madmates and not Sheriff-shootable. |
| Name tags | `src/Game/NameTags.cs` `NameFor(viewerId, targetId, meeting)` (46-108, quoted in §1.11): VIP star → v0.4.1 marks (`Lovers.Heart`, `Witch.Mark`, `Arsonist.Mark`; prefixes on `baseName`) → `// 1. own role tag` → `viewerImpKiller/viewerJackal/targetImpKiller/targetJackal` → `// 2. Madmate sees the impostor-team killers in red` (exact `viewerRole == CustomRole.Madmate`) → `// 3. impostors see the Madmate marker (option)` (inline `"<color=" + Roles.ImpostorColor + ">Ⓜ</color>"`) → `// 4.` Snitch ★ → `// 5.` Snitch colours. `RefreshAll(force, meeting, urgent)` sends only changed strings (`LastSent` cache; one paced `Rpc.Batch` per client; host via `ApplyLocal`); `ScheduleMurderRefresh()` internal; `AnySnitch()`; `RestoreAll()` at lobby. Refresh triggers: dispatch, +8 s, intro end +1 s, every MurderPlayer / OnPlayerLeft (+0.5 s), `CompleteTask` (Snitch only), report, `MeetingHud.Start` +0.5 s, WrapUp +2 s, AntiBlackout restore, dead-host chat restore, and every ability that changes a mark (`Witch.Spell`, `Arsonist.Douse` call `RefreshAll()` themselves). Glyphs proven on clients: `★`, `Ⓜ`; `♥ † ♨` shipped with a font caveat. | A new mark / colour whose state changes outside kill/task/meeting events must call `NameTags.RefreshAll()` itself. Nested `<color>` (a coloured mark inside a coloured name) is the existing structure. |
| Chat | `src/Chat/Chat.cs`: `Title`, `MaxChars = 100`, `MessageChars`, `ChunkSpacing => CompatMode ? 1.0f : 0.55f` (26), `PlayerSpacing`, `Local(title, text)`, `To(playerId, title, text)` (host → Local; disconnected → drop; compat → public @name; else `SendChunksTo(clientId, title, Split(text), 0f)` paced per client by `NextSendAt`), `All(title, text)`, `All(title, Func<string>)` (per-recipient language), `SendRoleInfo(playerId, meeting)` (217), `SendRoleInfoToAll(meeting)`, `Split` (≤ 100 chars per message; cut at the last space ≤ 100; one `<color>` pair per message), `Truncated`, `RoleInfoText(playerId, meeting)` (674-722: head `ColoredName [team]`, `roleinfo.desync` line for `ImpostorDesync` roles, `head + sep + desc` when ≤ 100 chars else `head\n(desync\n)desc`, SafeMode meeting branch one message, then the v0.4.1 extras ending `if (role == CustomRole.Witch && meeting) { … }` then `return result;` line 721). `Rpc.SendChatTo` = one GameDataTo (`SetName(host→title)`, `SendChat`, `SetName(host→restore)`); with a dead host it opens `TempReviveHostForChat(…, 1f, clientId)` per addressed client. Pacing rule (official server "Hacking" kick, findings #49/#50/#52): **never two clients' immediate packets in one frame**; ≤ 2 chat RPCs / s per client. `Kills.Notice(playerId, key, ja, en, args)` internal (recipient language, zh only from JSON), `Kills.ShouldNotice` private (10 s throttle), `LobbyKillCooldown()` internal (0 is a valid lobby value; fallback 30). | The only live-tested burst to one client = `Arsonist.Douse` (options ×2 urgent + `FailedProtected` + one chat). |
| Commands | `src/Chat/Commands.cs`: `Handle` → `HandleInScope` under `Lang.Scope(PlayerLang(sender))`; `Reply(sender, text)` caps a remote reply at `MaxReplyMessages = 3` (34) with `Chat.Truncated` (`…`) on the last kept chunk; `ReplyThrottled` (2 s per player); `/cmd r <role>` → `RoleDescText` = head + description + `RoleOptionText(role)` (744-776, last case `case CustomRole.Assassin: return Lang.TF("cmd.ro.assassin", …);` then `default: return null;` 774); `IsAdminOptKey` prefix chain line 355 `\|\| k.StartsWith("lovers.") \|\| k.StartsWith("arsonist.") \|\| k.StartsWith("witch.") \|\| k.StartsWith("assassin."))` + fallback `Roles.TryParse(k.Substring(0, dot))` (admits `<key\|alias\|name>.<anything>`); `OptUsageText()` static 8 lines (already omits v0.4.1 keys — left alone); `Assign(tokens)` (last token = role, rest = name); `SetRole` positional; `ResetRoles` default-1 list (`Sheriff \|\| Jester \|\| Madmate`); `OnOff(bool)` private; `/cmd guess` = `Assassin.Guess` (last token = role text). | Remote `/cmd r <role>` and `/cmd n` fit 3 messages only when the description is ≤ 100 chars in that language (head + desc + one extra line); every en description > 100 chars already drops the option line (pre-existing for Sheriff/Lovers/Witch). |
| Options | `src/Core/Options.cs` (no `using UnityEngine;` — use `System.Math`): fields 148-165 (last `private static ConfigEntry<int> _assassinGuessesPerMeeting;` 165); `Init` bind loop 327-337 (`[Roles] <NameEnNoSpaces>.Count` default 0 except Sheriff/Jester/Madmate = 1, max 15 (Lovers 1), `.Chance` 100); role binds 339-358 (last `_assassinFirstMeeting = cfg.Bind("Assassin", "CanGuessFirstMeeting", true, …);` 358, then `BuildDescriptors();`); `_sheriffCanKillMadmate = cfg.Bind("Sheriff", "CanKillMadmate", true, "Sheriff can shoot Madmates without dying");` 340; `_madmateKnownToImpostors = cfg.Bind("Madmate", "KnownToImpostors", false, "Impostors see who the Madmate is");` 348; getters 620-645 (last `AssassinCanGuessFirstMeeting` 645; patterns `_x?.Value ?? def`, default-true `_x == null \|\| _x.Value`, default-false `_x != null && _x.Value`); `BuildDescriptors` role loop 739-815 (`Bool/Int/Float(key, sJa, sEn, nJa, nEn, entry, [min,max,step,] color).Tip(ja, en, zh)`; Sheriff row 756-757 `Bool("sheriff.killmadmate", sJa, sEn, "マッドメイトを撃てる", "Can kill Madmate", …).Tip("オンならマッドメイトを撃っても自分は死にません。", …)`; Madmate row 786-787; last case Assassin 808-813, switch closes 814); `TrySet` 1057-1212 (last role case 1172 `case "assassin.firstmeeting": …`, then `case "sheriff.cooldown":`; helpers `SetFloat/SetInt(e, value, min, max, label, out msg)`, `SetBool(e, value, label, out msg)` reply `label = value`; fallback `<role>.count|num|n` / `.chance|rate|c` via `Roles.TryParse`; doc comment 1040-1056 lists keys); `DescribeLines` 1215-1282 (`opt.desc.*` fragments with a leading space / `", "`; Sheriff `opt.desc.sheriff.madmate` " マッド可" 1230; last case Assassin 1255-1258). `OptionDescriptor.Section => Lang.T("opt.section." + slug(SectionEn), …)`, `Name => Lang.T("opt.name." + Key, NameJa, NameEn)` (**3-arg T: zh only from JSON**), `Desc` 4-arg. `SettingsTab.RoleOf(d)` = `d.Key.StartsWith(r.Key + ".")` or `d.SectionEn == r.NameEn` → roles page; rows must stay contiguous per section (the per-role `switch` guarantees it). | Config: `[Roles] X.Count/.Chance` automatic; per-role `[NameEnNoSpaces] Key`; `SaveOnConfigSet`. |
| Per-client options | `src/Net/OptionsDesync.cs` (has `using UnityEngine;`): `Capture()` at SelectRoles; `BuildFor(playerId, killCooldownOverride)` switch 64-91 (Sheriff cooldown + `ImpostorLightMod = CrewLightMod`; Jackal cooldown; Lighter `CrewLightMod *= LighterVision`; SpeedBooster `PlayerSpeedMod *= SpeedBoosterSpeed`; Arsonist cooldown; `case CustomRole.Witch: if (Options.WitchSpellCooldown > 0f) opts.SetFloat(FloatOptionNames.KillCooldown, Mathf.Max(0.02f, Options.WitchSpellCooldown)); break;` 85-87; then the comment `// Vampire / Mafia: vanilla impostor kill cooldown (base value) unless overridden below.`); `NeedsCustomOptions` 100-115 (`case CustomRole.Arsonist: return true; case CustomRole.Witch: return Options.WitchSpellCooldown > 0f; default: return false;`); `SendTo(pc, opts, urgent)` (`Desynced` bookkeeping; paced `Rpc.Queue` unless urgent; `CompatBlocked`); `ResyncAll()` 214-236 (sends `BuildFor` for `NeedsCustomOptions` players, base back for `Desynced` others; skips `AmOwner` / `Disconnected` only — dead players still receive); `RestoreAll()` at lobby; `Reset()` 238; host patches: `OptionsDesync_GetKillCooldownPatch` switch (last `case CustomRole.Witch: … break;` 268), `OptionsDesync_GetPlayerSpeedModPatch` (288-289 `if (Core.Game.RoleOf(pc.PlayerId) == CustomRole.SpeedBooster) __result *= Options.SpeedBoosterSpeed;`), `OptionsDesync_CalculateLightRadiusPatch.ApplyHostLightMod` (307-319 `if (role == CustomRole.Lighter) { result *= Options.LighterVision; } else if (role == CustomRole.Sheriff) { … }`; base `ShipStatus` patch + `AirshipStatus` override patch — the other ship classes inherit the base body; **never add a third `CalculateLightRadius` patch**). Client semantics (TECH-NOTES 58-61): crewmate-basis client reads `CrewLightMod`, Impostor-basis client reads `ImpostorLightMod`, timer from `KillCooldown`, speed from `PlayerSpeedMod`; vanilla re-broadcasts the true options around meetings → the WrapUp `ResyncAll` (paced, behind ghost-role packets) + the +2 s resend. `VanillaRanges`: lobby `KillCooldown` may be 0, speed legal 0.5–3 (`AreInvalid` rejects ≤ 0 / > 3), vision 0.1–10. | **No live record exists of any client applying a received per-client vision or below-lobby speed value** (Sheriff/Lighter/SpeedBooster games are not in the worklogs); only the packet shape is live-proven (Arsonist/Vampire/Witch games). |
| Lang / JSON | `src/Core/Lang.cs`: `T(key, ja, en = null)` (197), `T(key, ja, en, zh)` (207), `TF(key, ja, en, args)` (221, no inline zh), `Scope(lang)`, `PlayerLang(id)`, `ListSep`, `StripTags`, `FullWidthDigits`, `MergeMissingKeys` (adds missing keys to a user file; **never updates changed values**). `lang/ja.json`, `lang/en.json`, `lang/zh-CN.json`: 786 lines = `{` + **784 keys** + `}`, identical key sets, ordinal-sorted (`LC_ALL=C sort -c`), same line numbers, 2-space indent, `"key": "value",`, last entry `"win.terrorist"` without comma, LF, no BOM, embedded (`PocketRoles.csproj:26`). | After this batch: **944 keys** per file (784 + 160) and 7 changed values (§1.15). Edit with the Edit tool only (never PowerShell `Get-Content`/`Set-Content` on BOM-less UTF-8). |
| Docs | `README.md` (1707 lines), `README.en.md` (1704), `README.zh-CN.md` (1708), `CHANGELOG.md` (85), csproj, `src/*.cs`: **CRLF**; `lang/*.json`, `support/*.md`: **LF**. README anchors: §10 role table header ja 547 (`\| 役職 \| 陣営 \| 説明 \| 主な設定（既定値） \|`), Madmate row ja 555 / en 552 / zh 556, Assassin row ja 559 / en 556 / zh 560, Jackal row ja 563 / en 560 / zh 564, alias sentence ja 567 / en 564 / zh 568, win table ja 573-582 / en 570-579 / zh 574-583 (last row Opportunist), §8 settings bullet ja 464 / en 461 / zh 465 (stale since v0.4.1), §11 `/opt` table last role row `assassin.firstmeeting` ja 719 / en 716 / zh 720, §12 config `[Roles]` last pair `Assassin.Chance = 100` ja 914 / en 911 / zh 915 and last section `[Assassin]` ja 954-956 / en 951-953 / zh 955-957 (comment column at character 33), §1 desync sentence ja 146 / en 145 / zh 147, §23 `/assign` note ja 1351 / en 1348 / zh 1352, §26 intro table ja 1467-1472, tasks table ja 1478-1483, kill table last row `\| 試合終了処理中のキル \| 失敗扱い \|` ja 1501 / en 1498 / zh 1502, vent bullet ja 1505, name-tag table last row `\| 通常役職 \| 何も変わりません \|` ja 1521 / en 1518 / zh 1522, chat bullet ja 1529, meeting bullets ja 1539-1541, §27 ja 1588 / 1597 / 1614-1616. Release-notes shape: `support/release-notes-v0.4.1.md` (role batch precedent). | Docs pass owner: the docs agent (§13); implementers own csproj / plugin / CHANGELOG. |
| Explicitly NOT in the tree | Vanilla method bodies (`CheckMurder`, `MurderPlayer`, `Exiled`, `Die`, `VotingComplete`, `ClearForResults`, `CalculateLightRadius`, `GetTruePosition`); the `KillDistances` values; any live test of ≥ 2 `MurderPlayer` broadcasts in one frame; any client killing a host that holds a desync role; any host self-kill while the host's local role is Impostor; any Lighter / SpeedBooster / Sheriff-vision observation on a client; any per-client `PlayerSpeedMod` below the lobby value; `support/design-v0.5.0.md` / `release-notes-v0.5.0.md` (this batch creates them); en/zh names of the nine roles anywhere outside this document. | Every such item is a gated live-test step in §12 with a written fallback. |

### 0.1 Decisions taken for the whole batch (reconciling the nine specs — do not re-derive)

1. **One Madmate-family predicate.** `Roles.IsMadType(CustomRole r)` (single source, `src/Core/Roles.cs`) = `Madmate ‖ MadMayor ‖ MadStuntman ‖ MadHawk ‖ Worshipper`, wrapped by `Core.Game.IsMadType(byte id)`. It replaces the three specs' `IsMadmateType` / `IsMadmate` / `IsMadType` proposals and drives exactly three rules: the crew-count subtraction (`WinConditions.EvaluateBase`), `[Sheriff] CanKillMadmate` (`Kills.CanSheriffKill`), and the impostor-side marker (`NameTags` rule 3). The **red-impostor-names rule (rule 2) applies to the family minus the Worshipper** (SNR MadMaker does not know the impostors; the self-destruct rule needs that) — written as `Roles.IsMadType(viewerRole) && viewerRole != CustomRole.Worshipper`. Every convert of the Worshipper is a literal `CustomRole.Madmate` and inherits everything.
2. **Marker option per role, one glyph rule.** Rule 3 reads `NameTags.MadKnownToImpostors(role)`: `[MadMayor] KnownToImpostors` for the Mad Mayor, `[Madmate] KnownToImpostors` for Madmate / Mad Stuntman / Mad Hawk / Worshipper. Glyph: `Ⓜ` for all, `Worshipper.ImpostorViewMark` (`Ⓦ`) for the Worshipper (the Assassin must be able to tell them apart).
3. **`IsImpostorTeamKiller` becomes one `switch`** listing Vampire / Mafia / Witch / Assassin / **EvilHawk / EvilNekomata / SerialKiller / Samurai**; `Kills` gets a private `VanillaKillsHost(role)` whitelist (`None, Mafia, Assassin, Lovers, EvilHawk, EvilNekomata`) for the host-target block. The Serial Killer and the Samurai have their own switch cases (`Rpc.Kill` themselves) and are deliberately **not** in the whitelist.
4. **Kill-immunity = the Mad Stuntman guard, exposed as `Kills.IsShielded(byte id)`** (`=> MadStuntman.Remaining(id) > 0`). `Kills.TryStuntmanGuard` runs in `HandleCheckMurder` before the vanilla path and covers **every** button press: its switch is extended with `EvilHawk` / `EvilNekomata` (vanilla-path killers), `SerialKiller` and `Samurai` (a pressed shielded target absorbs the **whole** slash — no bystanders — because the guard returns before the Samurai case). The two victim-selection loops that bypass `CheckMurder` consult `IsShielded` and skip the player without spending a life: the Samurai bystander filter and the Evil Nekomata candidate filter. `IsShielded` is **not** consulted in `IsValidMurder` (the guard handles presses and must consume a life + reset the cooldown, which a bare `FailKill` would not) nor in `ExecuteBite` (no bite path can reach a shielded Stuntman: Vampire/Witch presses are absorbed, a Stuntman is never a lover, the Assassin fallback bite exists only in SafeMode where no roles exist). A guarded press restarts a Serial Killer's countdown (`MadStuntman.OnGuarded` → `SerialKiller.OnKill`): the killer did everything right.
5. **Generic vote weight.** `Meetings.VoteWeightOf(voter)` (Mayor → `[Mayor] Votes`, Mad Mayor → `[MadMayor] Votes`, else 1) is used by both `TryEndVotingWithMayor` and `AliveMayorVoted`. **Generic "who voted for whom" reader**: `Meetings.VotersFor(targetId, states, hud)` (one entry per player, Mayor / Mad Mayor duplicates collapsed, `hud.playerStates` fallback), consumed by the Evil Nekomata. `Meetings_VotingCompletePatch` binds `states` (`[HarmonyArgument(0)] Il2CppStructArray<MeetingHud.VoterState> states`).
6. **Generic mid-game conversion.** `Core.Game.ConvertRole(byte id, CustomRole to, string why)` is the only sanctioned writer of `Game.Roles` after dispatch: it writes the table, logs `before -> after`, runs `RecomputeTaskCounts()`, `OptionsDesync.Resync(id)` (new single-player resync, §1.9) and `NameTags.RefreshAll()`, and returns the old role; the caller sends its notices and calls `WinConditions.Check()`. Its callers **must refuse** `IsImpostorTeamKiller` targets (the Evil Hawk / Nekomata / Serial Killer / Samurai clients keep their Impostor button and options), `IsMadType` targets (already on the side; a Mad Hawk would silently lose its vision), `IsDesyncImpostor` targets (Sheriff / Jackal / Arsonist keep a kill button) and Lovers (pair state) — `Worshipper.Judge` does all four. A Jackal Friends **is** convertible (vanilla Crewmate, `Team.Neutral`, no state; SNR "塗り替える" overwrites any role; it loses its Jackal win).
7. **Shared bite pacing.** `Kills.Tick` / `FlushBites` re-check `Game.Ending ‖ !InProgress` before every bite (a Terrorist death inside a batch ends the game synchronously); `ExecuteBite` postpones on `inVent ‖ onLadder ‖ inMovingPlat` (+1 s); `WinConditions.Check()` and the Bait `ForceReport` wait for a running Samurai slash (`Kills.SlashInProgress()`, bounded). The Evil Nekomata drag is re-armed to WrapUp + 2.5 s (0.5 s behind curses / lover follows); the Serial Killer queues at most one time-out bite per frame. Nothing in v0.5.0 sends two broadcast `MurderPlayer` in one frame **by design**; the residual same-frame paths (a player-initiated report inside a slash window; the shipped multi-victim curse) are documented, not changed.
8. **`IsValidMurder` is not widened** to `onLadder` / `inMovingPlat` for every custom killer (batch-level change deferred; the Stuntman guard steps aside for vanilla-path killers on such targets, the Samurai skips such bystanders).
9. **Test-mode count diagnostic adopted for the batch**: the Jackal Friends spec's `WinConditions(test): outcome=… alive=… imp=… jackal=… others=… madmate=… friends=… crewForCount=…` line (deduplicated, `CheckNow` in test mode) — six of the nine roles touch `EvaluateBase` and nothing else makes the counts visible with `/test on`.
10. **Roles.All order and aliases are fixed here** (§1.2): crew pool after Madmate = Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper; impostor pool after Assassin = **Evil Hawk, Evil Nekomata, Serial Killer, Samurai**; neutral after Jackal = Jackal Friends. Enum members append in the same order. Bare alias `hawk` is claimed by nobody. Shortest unique prefixes are listed in §1.2 and quoted verbatim in the README alias sentence (§13).
11. **Sheriff / Madmate wording is changed once** for the family (`[Sheriff] CanKillMadmate` bind/label/tip, `[Madmate] KnownToImpostors` bind/tip, `cmd.ro.sheriff[.madmate]`, `cmd.ro.madmate`, `role.sheriff.desc` incl. the Jackal Friends option) — the seven changed JSON values of §1.15 reach existing installs only by copying `lang/*.json` (release note).
12. **Blocking user decision carried over (Worshipper self-destruct)**: this plan implements the SNR MadMaker rule (pressing on an impostor-team killer, incl. an impostor-side lover, kills the Worshipper). The delta if the user rejects it is in §5 (one `Verdict` branch, one key, one README sentence).
13. **Remote `/cmd r <role>` budget**: every `role.<key>.desc` (ja/en/zh) is kept ≤ 100 chars where the spec managed it (Mad Mayor en 99, Jackal Friends en 99, Worshipper ja 98 / zh 74); en descriptions above 100 chars (Stuntman, Mad Hawk, Worshipper, Nekomata, Serial Killer, Samurai) drop the option line for remote en players exactly like Lovers / Witch today — accepted, listed in §13 risks.

### 0.2 Files this batch touches

New: `src/Game/MadStuntman.cs`, `src/Game/Worshipper.cs`, `src/Game/JackalFriends.cs`, `src/Game/EvilNekomata.cs`, `src/Game/SerialKiller.cs`, `src/Game/Samurai.cs` (no class for Mad Mayor, Mad Hawk, Evil Hawk: rows + switch cases only), `support/design-v0.5.0.md` (this file), `support/release-notes-v0.5.0.md`. Edited (shared, Implementer A only): `PocketRoles.csproj`, `src/PocketRolesPlugin.cs`, `CHANGELOG.md`, `src/Core/Roles.cs`, `src/Core/GameState.cs`, `src/Core/Options.cs`, `src/Chat/Commands.cs`, `src/Chat/Chat.cs`, `src/Game/Kills.cs`, `src/Game/Meetings.cs`, `src/Game/WinConditions.cs`, `src/Game/NameTags.cs`, `src/Game/RoleAssignment.cs`, `src/Net/OptionsDesync.cs`, `lang/ja.json`, `lang/en.json`, `lang/zh-CN.json`. Docs agent: `README.md`, `README.en.md`, `README.zh-CN.md`. Untouched (checked by every spec): `src/Game/TestMode.cs`, `AntiBlackout.cs`, `Assassin.cs`, `Lovers.cs`, `Witch.cs`, `Arsonist.cs`, `SettingsTab.cs`, `VanillaRanges.cs`, `Rpc.cs`, `Scheduler.cs`, `Registration`.

Work split (§11): **A** = §1 (shared skeleton, stubs, lang JSON, version, CHANGELOG) — the tree compiles after A; **B** = §2 Mad Mayor, §3 Mad Stuntman (`MadStuntman.cs`), §4 Mad Hawk, §5 Worshipper (`Worshipper.cs`); **C** = §7 Evil Nekomata (`EvilNekomata.cs`), §8 Serial Killer (`SerialKiller.cs`); **D** = §6 Jackal Friends (`JackalFriends.cs`), §9 Samurai (`Samurai.cs`), §10 Evil Hawk. B/C/D never edit a shared file; every shared line they need is already in place as a stub after A.

---
## 1. Shared changes (Implementer A, first; the tree compiles after every numbered step)

### 1.1 Version bump / CHANGELOG
* `PocketRoles.csproj` line 10 `    <Version>0.4.3</Version>` → `0.5.0`. `src/PocketRolesPlugin.cs` line 23 `        public const string Version = "0.4.3";` → `"0.5.0"` (`SupportedGameVersion` unchanged). `PocketRolesLauncher.ps1` `$script:LauncherVersion` stays.
* `CHANGELOG.md` (CRLF): insert after line 3 (`Among Us **2026.8.18**（Steam / Windows）向けの…`) and the blank line 4, before `## v0.4.3 — 2026-09-09`:
  ```
  ## v0.5.0 — 2026-09-xx

  - 役職を 9 つ追加（SuperNewRoles 準拠の「薄い陣営」向け役職）: マッドメイヤー・マッドスタントマン・マッドホーク・崇拝者（インポスター陣営・クルー枠）、ジャッカルフレンズ（第三陣営・クルー枠）、イビルホーク・イビル猫又・シリアルキラー・侍（インポスター枠）。すべて既定では人数 0
  - マッドメイヤー: 会議の投票が複数票分（投票アイコンも票数分表示され、他の人からはメイヤーに見える）。`[MadMayor] Votes / KnownToImpostors`（`/opt madmayor.votes|known`）、短縮形 `mmy`
  - マッドスタントマン: キルされても設定回数までは死なず、キルした側はクールダウンだけが戻る（追放・アサシンの推理は防げない）。インポスター側のキラーには役職名と残り回数、シェリフ・ジャッカルには「耐えました」とだけ通知。`[MadStuntman] Lives / NotifyStuntman`（本人への通知は既定オフ）、短縮形 `stunt` `madstunt` `mst` `ms`
  - マッドホーク: 視界が広いマッドメイト（停電中も倍率がかかる）。`[MadHawk] VisionMultiplier / SpeedMultiplier`（`/opt madhawk.vision|speed`）、短縮形 `mh`
  - 崇拝者（クルー枠・本人のクライアントはインポスター）: キルボタンで相手を崇拝してマッドメイトにする（`[Worshipper] Uses / Cooldown`）。インポスター側のキラー（インポスター側のラバーズ含む）を崇拝すると自爆、回数を使い切った後はキル失敗の演出のみ。崇拝で人数が減るため、その場でインポスター勝利（相手が最後の残りタスクを持っていた場合はクルー勝利）になることがある。`KnownToImpostors` オンのインポスターには Ⓦ（アサシンは `worshipper` で当てる）。短縮形 `ws`
  - ジャッカルフレンズ（第三陣営・クルー枠）: ジャッカルの名前が青く見え、ジャッカルが勝つと勝つ（キル・ベント・サボ不可、タスクは偽物）。ジャッカルが配られた試合だけ配られる。`[JackalFriends] KnownToJackal / SheriffCanKill`（`/opt jackalfriends.*`）、短縮形 `jf`
  - イビルホーク（インポスター枠）: 視界が通常のインポスターより広い（インポスターの視界 × `[EvilHawk] VisionMultiplier`、既定 2、常時）。`/opt evilhawk.vision`、短縮形 `eh`。アサシンが当てるときは `impostor` ではなく `evilhawk` / `eh`
  - イビル猫又（インポスター枠）: 会議で追放されると自分に投票した人から 1 人を道連れにする（追放画面の約 2.5 秒後に死亡。追放で試合が終わる時は道連れなし）。`[EvilNekomata] VotersOnly / ExcludeImpostors / Announce`（`/opt evilnekomata.*`）、短縮形 `neko` `nekomata`
  - シリアルキラー（インポスター枠）: キルクールダウンが短い代わりに、前のキルから一定時間キルしないとその場で自滅（自分で自分をキルする演出）。タイマーはキルのたびにリセット、会議中は停止、既定では会議後にリセット。残り時間は本人の名前タグ（5 秒刻み）と、残り 10 秒（制限が 20 秒未満なら半分）のチャットで通知。イントロの数秒後と会議の約 2 秒後にキルボタンを短いクールダウンに合わせ直す（シールドの演出が一瞬出る）。`[SerialKiller] KillCooldown / SuicideTime / ResetAtMeeting`（`/opt serialkiller.cooldown|time|meetingreset`）、短縮形 `sk`
  - 侍（インポスター枠）: キルが斬撃になり、キルした相手に続いてその瞬間に周囲にいた人が約 0.3 秒間隔で次々に死ぬ（既定で仲間は巻き込まない。勝敗判定とベイトの自動通報は斬撃が終わるまで待つ）。`[Samurai] KillCooldown / Range / Stagger / HitTeammates`（`/opt samurai.*`）、短縮形 `sam`
  - マッド系役職の共通ルール: `[Sheriff] CanKillMadmate`、`[Madmate] KnownToImpostors`（Ⓜ。マッドメイヤーは `[MadMayor] KnownToImpostors`）、インポスター勝利の人数判定はマッドメイト・マッドメイヤー・マッドスタントマン・マッドホーク・崇拝者（と崇拝された人）に適用。設定の説明文・`/cmd r sheriff` / `madmate` の文言、シェリフの役職説明を更新（`lang/*.json` は上書きコピーが必要）
  - マッドメイト: バニラ役職オンでエンジニアがマッドメイトになった時（`/assign` で付けた場合・崇拝された場合）、ベントから追い出さなくなった
  - テストモード: `/test on` 中は勝敗判定の人数をログに出す（`WinConditions(test): outcome=… crewForCount=…`）。会議の開票時、メイヤー系の票の重みは `Meetings.VoteWeightOf` で共通化
  - 説明書 3 言語に 9 役職の説明・設定・見え方を追記。README の版数表記を 0.5.0 に更新（0.4.2 / 0.4.3 では未更新だった）
  ```

### 1.2 `src/Core/Roles.cs`
1. Enum — anchor `        Lovers, Arsonist, Witch, Assassin` (line 15) →
   ```csharp
           Lovers, Arsonist, Witch, Assassin,
           // v0.5.0 (values are never persisted; order = Roles.All order)
           MadMayor, MadStuntman, MadHawk, Worshipper, JackalFriends, EvilHawk, EvilNekomata, SerialKiller, Samurai
   ```
2. Family helper — after `        public static string ColoredName(CustomRole r) => Info(r).ColoredName;` (line 234):
   ```csharp
           /// <summary>
           /// The Madmate family (v0.5.0): Team.Impostor without a real kill — Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper
           /// (and every player the Worshipper converts, which becomes a literal Madmate). Drives exactly three rules: not crew for the
           /// count thresholds (WinConditions.EvaluateBase), [Sheriff] CanKillMadmate (Kills.CanSheriffKill), the impostor-side Ⓜ/Ⓦ marker
           /// (NameTags rule 3). The red-impostor-names rule (NameTags rule 2) is IsMadType minus the Worshipper. A converter must refuse
           /// a target for which this is true (Game.ConvertRole doc).
           /// </summary>
           public static bool IsMadType(CustomRole r) =>
               r == CustomRole.Madmate || r == CustomRole.MadMayor || r == CustomRole.MadStuntman || r == CustomRole.MadHawk || r == CustomRole.Worshipper;
   ```
3. `Roles.All` — **crew pool**: insert directly after the Madmate row, i.e. after `                Aliases = new[] { "mad", "mm" } },` (line 120) and before `            new RoleInfo { Id = CustomRole.Vampire,`:
   ```csharp
               // ---- v0.5.0 Madmate family (crew pool, vanilla Crewmate on every client, see Roles.IsMadType).
               // Ordering matters: Roles.TryParse resolves a ≥2-char ja prefix / ≥1-char zh prefix to the FIRST row, so `マッド` and
               // `マッドメイ` stay Madmate (row above); the shortest unique prefixes are マッドメイヤ / マッドス / マッドホ / 崇拝 and 狂 / 疯 / 鹰 / 崇
               // (README alias sentence quotes them). DescEn of the Mad Mayor is ≤ 100 chars on purpose: /cmd r <role> is capped at 3 messages.
               new RoleInfo { Id = CustomRole.MadMayor, Key = "madmayor", NameJa = "マッドメイヤー", NameEn = "Mad Mayor", NameZh = "狂粉市长", Team = Team.Impostor, Color = ImpostorColor,
                   TasksCount = false,
                   DescJa = "インポスター陣営のクルーです。会議での投票が複数票として数えられます。インポスターの名前が赤く見えますがキルはできません。インポスターが勝つとあなたも勝ちです。タスクは偽物です。",
                   DescEn = "A Madmate whose vote counts as several votes. You see Impostors in red but cannot kill; fake tasks.",
                   DescZh = "内鬼阵营的船员，会议中的投票按多票计算。内鬼的名字显示为红色，但你不能击杀。内鬼获胜时你也获胜。任务是假的。",
                   Aliases = new[] { "mmy", "madmy", "mmayor" } },
               new RoleInfo { Id = CustomRole.MadStuntman, Key = "madstuntman", NameJa = "マッドスタントマン", NameEn = "Mad Stuntman", NameZh = "疯狂特技演员", Team = Team.Impostor, Color = ImpostorColor,
                   // The first [MadStuntman] Lives kill attempts on it fail (Kills.TryStuntmanGuard) — votes, the Assassin's guess and disconnects still kill it.
                   TasksCount = false,
                   DescJa = "インポスター陣営のクルーです。インポスターの名前が赤く見えます。キルはできませんが、キルされても設定回数までは死にません（追放は防げません）。インポスターが勝つとあなたも勝ちです。",
                   DescEn = "A crewmate on the Impostor team. You see Impostors in red and cannot kill, but you survive the first few kill attempts (a vote still gets you). You win with the Impostors.",
                   DescZh = "内鬼阵营的船员。内鬼的名字显示为红色，你不能击杀，但前几次被击杀不会死（投票放逐无法避免）。内鬼获胜时你也获胜。",
                   Aliases = new[] { "stunt", "madstunt", "mst", "ms" } },
               new RoleInfo { Id = CustomRole.MadHawk, Key = "madhawk", NameJa = "マッドホーク", NameEn = "Mad Hawk", NameZh = "鹰眼狂粉", Team = Team.Impostor, Color = ImpostorColor,
                   // Passive Lighter-style multiplier on a crewmate-basis client (SNR's active "hawk eye" needs a button a vanilla crewmate does not have).
                   TasksCount = false,
                   DescJa = "インポスター陣営のクルーです。視界が通常よりずっと広くなります。インポスターの名前が赤く見えます。キルはできません。インポスターが勝つとあなたも勝ちです。",
                   DescEn = "A crewmate on the Impostor team with much wider vision. You see Impostors in red but cannot kill. You win with the Impostors.",
                   DescZh = "内鬼阵营的船员，视野比普通船员大得多。内鬼的名字显示为红色，但你不能击杀。内鬼获胜时你也获胜。",
                   Aliases = new[] { "mh", "mhawk" } },
               // Madmate with a button: its own client is an Impostor (kill button = worship), everyone else sees a Crewmate.
               new RoleInfo { Id = CustomRole.Worshipper, Key = "worshipper", NameJa = "崇拝者", NameEn = "Worshipper", NameZh = "崇拜者", Team = Team.Impostor, Color = ImpostorColor,
                   IsKiller = true, ImpostorDesync = true, CanVent = false, CanSabotage = false, TasksCount = false,
                   DescJa = "インポスター陣営のクルー。インポスターは分かりません。キルボタンで相手を崇拝しマッドメイトにします（回数制限）。インポスターを崇拝すると自爆、回数切れ後は失敗だけ。ベント・サボ不可、タスクは偽物。",
                   DescEn = "A crewmate on the Impostor team; you do not know who the Impostors are. Your kill button worships: the target becomes a Madmate (limited uses). Worshipping an Impostor kills you; after the last use the button only fails. No venting or sabotage; your tasks are fake.",
                   DescZh = "内鬼阵营的船员（不知道谁是内鬼）。击杀键变为“崇拜”，对方成为内鬼狂粉（次数有限）。崇拜内鬼会自爆，次数用完后只会失败。不能跳管和破坏，任务是假的。",
                   Aliases = new[] { "ws", "worship" } },
   ```
   **Impostor pool**: insert directly after the Assassin row, i.e. after `                Aliases = new[] { "as", "asn" } },` (line 144) and before `            new RoleInfo { Id = CustomRole.Jester,`:
   ```csharp
               // ---- v0.5.0 impostor-pool roles (real vanilla Impostor on the client; listed in Game.IsImpostorTeamKiller).
               // Evil Hawk stays FIRST: TryParse's prefix passes take the first row, so イビ / イビル / 邪 / 邪恶 resolve here; イビルホ / イビル猫 and
               // 邪恶鹰 / 邪恶猫 are the prefixes that are certain whatever the order (README alias sentence).
               new RoleInfo { Id = CustomRole.EvilHawk, Key = "evilhawk", NameJa = "イビルホーク", NameEn = "Evil Hawk", NameZh = "邪恶鹰眼", Team = Team.Impostor, Color = ImpostorColor,
                   IsKiller = true, CanVent = true, CanSabotage = true, FromImpostorPool = true,
                   DescJa = "インポスターです。視界が通常のインポスターよりずっと広くなります。キル・ベント・サボタージュは通常どおりです。",
                   DescEn = "An Impostor who sees much further than a normal Impostor. Kills, vents and sabotages as usual.",
                   DescZh = "内鬼。视野比普通内鬼大得多。击杀、跳管、破坏与普通内鬼相同。",
                   Aliases = new[] { "eh" } },
               new RoleInfo { Id = CustomRole.EvilNekomata, Key = "evilnekomata", NameJa = "イビル猫又", NameEn = "Evil Nekomata", NameZh = "邪恶猫又", Team = Team.Impostor, Color = ImpostorColor,
                   IsKiller = true, CanVent = true, CanSabotage = true, FromImpostorPool = true,
                   DescJa = "インポスターです。通常どおりキルできます。会議で追放されると、あなたに投票した人の中からランダムで 1 人を道連れにします。",
                   DescEn = "An Impostor who kills normally. When you are voted out, one random player who voted for you dies with you.",
                   DescZh = "内鬼，可以正常击杀。当你在会议中被投出时，会从投票给你的人中随机拖一人一起死。",
                   Aliases = new[] { "nekomata", "neko", "eneko" } },
               // After SuperNewRoles: vanilla Impostor with a short kill cooldown that dies by itself when it has not killed for [SerialKiller] SuicideTime.
               new RoleInfo { Id = CustomRole.SerialKiller, Key = "serialkiller", NameJa = "シリアルキラー", NameEn = "Serial Killer", NameZh = "连环杀手", Team = Team.Impostor, Color = ImpostorColor,
                   IsKiller = true, CanVent = true, CanSabotage = true, FromImpostorPool = true,
                   DescJa = "インポスターです。キルクールダウンが短い代わりに、前のキルから一定時間キルしないとその場で自滅します（キルするたびにリセット、会議中は停止）。ベント・サボタージュ可。",
                   DescEn = "An Impostor with a very short kill cooldown. If you do not kill within the time limit after your last kill you die on the spot (the timer restarts with every kill and pauses during meetings). Can vent and sabotage.",
                   DescZh = "内鬼。击杀冷却很短，但距上次击杀超过限定时间仍未击杀就会当场自灭（每次击杀后重置，会议中暂停）。可跳管、可破坏。",
                   Aliases = new[] { "sk", "serial" } },
               new RoleInfo { Id = CustomRole.Samurai, Key = "samurai", NameJa = "侍", NameEn = "Samurai", NameZh = "武士", Team = Team.Impostor, Color = ImpostorColor,
                   IsKiller = true, CanVent = true, CanSabotage = true, FromImpostorPool = true,
                   DescJa = "インポスターです。キルが「斬撃」になり、キルした相手に続いて、その瞬間にあなたの周囲にいた人が次々に死にます。クールダウンは長めです。",
                   DescEn = "An Impostor whose kill is a slash: your target dies, then everyone who was near you at that moment falls one after another. Long cooldown.",
                   DescZh = "内鬼。你的击杀变成“斩击”：击杀目标之后，那一刻你周围的所有人也会接连死亡。冷却较长。",
                   Aliases = new[] { "sam", "sm" } },
   ```
   **Neutral**: insert directly after the Jackal row, i.e. after `                Aliases = new[] { "jk" } },` (line 168) and before `            new RoleInfo { Id = CustomRole.Lovers,`:
   ```csharp
               // v0.5.0: the Jackal's Madmate — vanilla Crewmate on its client (no kill button), Team.Neutral so its tasks never count on the
               // host and it wins only through the Jackal (WinConditions.ComputeWinners). DescEn ≤ 100 chars (3-message /cmd r cap).
               new RoleInfo { Id = CustomRole.JackalFriends, Key = "jackalfriends", NameJa = "ジャッカルフレンズ", NameEn = "Jackal Friends", NameZh = "豺狼之友", Team = Team.Neutral, Color = JackalColor,
                   TasksCount = false,
                   DescJa = "ジャッカル陣営のクルーです。ジャッカルの名前が青く見えます。キルはできません。ジャッカルが勝つとあなたも勝ちです。タスクは偽物です。",
                   DescEn = "Jackal-side crewmate: you see the Jackal in blue, cannot kill, and win with the Jackal. Fake tasks.",
                   DescZh = "豺狼阵营的船员。豺狼的名字显示为蓝色，但你不能击杀。豺狼获胜时你也获胜。任务是假的。",
                   Aliases = new[] { "jf", "friends", "friend" } },
   ```
4. Sheriff row (lines 81-83; the option now covers the family and the Jackal Friends has its own): replace the three literals with
   * `DescJa = "キルボタンでインポスターやジャッカル、放火魔を撃てます（設定でマッド系役職・ジャッカルフレンズも）。クルーを撃つと自分が死にます。ベントとサボタージュは使えず、タスクは偽物です。",` (87 chars)
   * `DescEn = "You have a kill button to shoot Impostors, the Jackal and the Arsonist (Mad-type roles and Jackal Friends by option). Shooting anyone else kills you instead. No venting or sabotage; your tasks are fake.",`
   * `DescZh = "可用击杀键射杀内鬼、豺狼和纵火犯（按设置也可射杀狂粉系职业、豺狼之友）。误杀其他人会让自己死亡。不能跳管和破坏，任务是假的。",` (65 chars)
   The JSON `role.sheriff.desc` values (§1.15) must match (JSON wins).
5. **Alias / prefix table (the batch's single source; nothing else may claim these)**:

   | Role | Key | ja | en | zh | Aliases | `/cmd r` / `/set` / `/assign` / `/cmd guess` shortest certain input |
   |---|---|---|---|---|---|---|
   | Mad Mayor | `madmayor` | マッドメイヤー | Mad Mayor | 狂粉市长 | `mmy` `madmy` `mmayor` | `マッドメイヤ` (`マッド` / `マッドメイ` = Madmate), `狂` |
   | Mad Stuntman | `madstuntman` | マッドスタントマン | Mad Stuntman | 疯狂特技演员 | `stunt` `madstunt` `mst` `ms` | `マッドス`, `疯` |
   | Mad Hawk | `madhawk` | マッドホーク | Mad Hawk | 鹰眼狂粉 | `mh` `mhawk` | `マッドホ`, `鹰` |
   | Worshipper | `worshipper` | 崇拝者 | Worshipper | 崇拜者 | `ws` `worship` | `崇拝`, `崇` |
   | Jackal Friends | `jackalfriends` | ジャッカルフレンズ | Jackal Friends | 豺狼之友 | `jf` `friends` `friend` | `ジャッカルフ` (`ジャ`…`ジャッカル` = Jackal), `豺狼之` (`豺` = Jackal) |
   | Evil Hawk | `evilhawk` | イビルホーク | Evil Hawk | 邪恶鹰眼 | `eh` | `イビ` / `イビル` (first row) — `イビルホ` certain; `邪` / `邪恶` (first row) — `邪恶鹰` certain |
   | Evil Nekomata | `evilnekomata` | イビル猫又 | Evil Nekomata | 邪恶猫又 | `nekomata` `neko` `eneko` | `イビル猫`, `邪恶猫` |
   | Serial Killer | `serialkiller` | シリアルキラー | Serial Killer | 连环杀手 | `sk` `serial` | `シリ`, `连` |
   | Samurai | `samurai` | 侍 | Samurai | 武士 | `sam` `sm` | `侍` exact only (1 char never prefix-matches), `武` |

   Checks done: none of the 25 new aliases collides with `sh my sn lt sb speed booster bt mad mm vamp vp mf wt wi as asn js opp op terror tr jk lover lv love arso ars` or with each other (`ms` ≠ `sm`); no new `Key` is another key + "." prefix; zh first characters 狂 疯 鹰 崇 连 武 are new, 豺 and 邪 are shared as listed. `TasksCount` stays `false` on every new row (non-crew teams never count anyway). `CompatRisky` is true only for the Worshipper (killer from the crew pool) — irrelevant: compat lobbies assign nothing.

### 1.3 `src/Core/GameState.cs`
1. State — insert after `        public static int MeetingsHeld;` (line 83), before `        public static bool IsLover(byte id)`:
   ```csharp

           // ---- v0.5.0 role state (cleared in ResetRoleState)
           /// <summary>Mad Stuntman playerId → kill attempts it has already survived (Kills.TryStuntmanGuard / MadStuntman.Remaining).</summary>
           public static Dictionary<byte, int> StuntGuards = new Dictionary<byte, int>();
           /// <summary>worshipper playerId → players it turned into Madmates, in conversion order (permanent; dead converts stay: Ⓜ mark, uses count, reminder line).</summary>
           public static Dictionary<byte, List<byte>> Worshipped = new Dictionary<byte, List<byte>>();
           /// <summary>Players an exiled Evil Nekomata drags along (bites registered at VotingComplete; re-armed to +2.5 s and announced from ExileController.WrapUp).</summary>
           public static List<byte> NekomataDragged = new List<byte>();
           /// <summary>
           /// Serial Killer countdown: Remaining = seconds left (≤ 0 while the death is pending / retried); Warned = the "N s left" notice went
           /// out in this cycle; TimedOut = the time-out was logged / announced; TagStep = countdown value last put into the own name tag (5 s steps, -1 = never).
           /// </summary>
           public struct SerialKillerTimer { public float Remaining; public bool Warned; public bool TimedOut; public int TagStep; }
           /// <summary>serial killer playerId → countdown (SerialKiller.Tick; entries of dead / disconnected players are dropped there).</summary>
           public static Dictionary<byte, SerialKillerTimer> SerialKillerTimers = new Dictionary<byte, SerialKillerTimer>();
           /// <summary>Set at ExileController.WrapUp, cleared by SerialKiller.OnMeetingEnd (WrapUp + 2 s): the countdowns hold until the clients' kill timers were reset.</summary>
           public static bool SerialKillerPaused;
   ```
2. `ResetRoleState()` — after `            MeetingsHeld = 0;` (line 94) add:
   ```csharp
               // v0.5.0
               StuntGuards.Clear();
               Worshipped.Clear();
               NekomataDragged.Clear();
               SerialKillerTimers.Clear();
               SerialKillerPaused = false;
               SerialKiller.ResetState();   // one "limit raised" warning per game
   ```
   (`using PocketRoles.Game;` is at the top of the file.)
3. `VampireBite` doc comment (line 69): `Reason: null = vampire bite, "lovers", "curse", "assassin"` → append `, "nekomata" (Evil Nekomata drag), "serialkiller" (own time-out), "slash" (Samurai bystander) (log only; Kills.SlashInProgress keys on the batch window, not on the reason)`.
4. `IsImpostorTeamKiller` — replace the whole body (lines 281-285) with one switch and update the summary line to `/// <summary>Vanilla impostor-type role incl. Vampire/Mafia/Witch/Assassin and the v0.5.0 Evil Hawk/Evil Nekomata/Serial Killer/Samurai (and an impostor lover) — NOT the Madmate family (Roles.IsMadType).</summary>`:
   ```csharp
               var role = RoleOf(id);
               switch (role)
               {
                   case CustomRole.Vampire: case CustomRole.Mafia: case CustomRole.Witch: case CustomRole.Assassin:
                   // v0.5.0 impostor-pool killers
                   case CustomRole.EvilHawk: case CustomRole.EvilNekomata: case CustomRole.SerialKiller: case CustomRole.Samurai:
                       return true;
                   case CustomRole.Lovers:   // an impostor lover keeps its kill button and counts as an impostor
                   case CustomRole.None:
                       return IsVanillaImpostorRole(VanillaRoleOf(id));
                   default:
                       return false;         // every other custom role incl. the Madmate family and the Jackal Friends
               }
   ```
5. Family helper — after `        public static bool IsJackal(byte id) => RoleOf(id) == CustomRole.Jackal;` (line 290):
   ```csharp
           /// <summary>Madmate FAMILY (Roles.IsMadType: Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper, converts). Not "is exactly a Madmate".</summary>
           public static bool IsMadType(byte id) => Core.Roles.IsMadType(RoleOf(id));

           /// <summary>
           /// v0.5.0: the only sanctioned mid-game writer of <see cref="Roles"/> (the Worshipper's conversion). Writes the table, logs
           /// before → after, and pushes what every consumer caches per client: task totals (Win_RecomputeTaskCountsPatch), the target's
           /// per-client options (OptionsDesync.Resync: base options back for a Lighter / SpeedBooster convert, one paced packet), and the
           /// name tags (paced, changed pairs only). The caller sends its notices and calls WinConditions.Check() afterwards.
           /// Callers MUST refuse targets for which IsImpostorTeamKiller, IsMadType, IsDesyncImpostor or RoleOf == Lovers is true: the
           /// client keeps its first SetRole (2026.8.18 rule), so only a vanilla-Crewmate → Crewmate-looking change is view-neutral.
           /// </summary>
           public static CustomRole ConvertRole(byte id, CustomRole to, string why)
           {
               CustomRole before = RoleOf(id);
               Roles[id] = to;
               PocketRolesPlugin.Logger.LogInfo($"Game: #{id} {NameOf(id)} {before} -> {to} ({why})");
               try { GameData.Instance?.RecomputeTaskCounts(); }
               catch (System.Exception e) { PocketRolesPlugin.Logger.LogWarning($"Game.ConvertRole: RecomputeTaskCounts: {e.Message}"); }
               OptionsDesync.Resync(id);
               NameTags.RefreshAll();
               return before;
           }
   ```
6. `GameScopedTags` (lines 244-247): append `, "serialkiller.arm"` (the Serial Killer's intro arm is cancelled at lobby start like `"assign.introend"`).
   `IsDesyncImpostor`, `TeamOf`, `IsNonCrewKiller`, `IsDead/IsAlive` unchanged.

### 1.4 `src/Core/Options.cs` (all nine roles in one block each; role order = enum order)
1. Fields — after `        private static ConfigEntry<int> _assassinGuessesPerMeeting;` (line 165):
   ```csharp
           // v0.5.0 [MadMayor] [MadStuntman] [MadHawk] [Worshipper] [JackalFriends] [EvilNekomata] [SerialKiller] [Samurai] [EvilHawk]
           private static ConfigEntry<int> _madMayorVotes, _madStuntmanLives, _worshipperUses;
           private static ConfigEntry<bool> _madMayorKnownToImpostors, _madStuntmanNotify, _jackalFriendsKnownToJackal, _jackalFriendsSheriffCanKill,
               _nekomataVotersOnly, _nekomataExcludeImpostors, _nekomataAnnounce, _serialKillerResetAtMeeting, _samuraiHitTeammates;
           private static ConfigEntry<float> _madHawkVision, _madHawkSpeed, _worshipperCooldown, _serialKillerKillCooldown, _serialKillerSuicideTime,
               _samuraiKillCooldown, _samuraiRange, _samuraiStagger, _evilHawkVision;
   ```
2. Binds — after `            _assassinFirstMeeting = cfg.Bind("Assassin", "CanGuessFirstMeeting", true, "The Assassin may guess in the first meeting of the game");` (line 358), before the blank line and `BuildDescriptors();`:
   ```csharp

               // ---- v0.5.0 roles
               _madMayorVotes = cfg.Bind("MadMayor", "Votes", 2, new ConfigDescription("How many votes the Mad Mayor's vote counts as", new AcceptableValueRange<int>(1, 5)));
               _madMayorKnownToImpostors = cfg.Bind("MadMayor", "KnownToImpostors", false, "Impostors see who the Mad Mayor is (red Ⓜ before the name)");
               _madStuntmanLives = cfg.Bind("MadStuntman", "Lives", 1, new ConfigDescription("Kill attempts the Mad Stuntman survives before a kill goes through (votes are never blocked)", new AcceptableValueRange<int>(1, 10)));
               _madStuntmanNotify = cfg.Bind("MadStuntman", "NotifyStuntman", false, "Tell the Mad Stuntman in private chat when it survived a kill attempt and how many are left (off = SNR behaviour: the role chat still shows the count at start and every meeting; also reveals absorbed Vampire bites / Witch spells; the killer is always told)");
               _madHawkVision = cfg.Bind("MadHawk", "VisionMultiplier", 3f, new ConfigDescription("Mad Hawk vision multiplier (crew vision x N; a lights sabotage still shrinks it)", new AcceptableValueRange<float>(1f, 5f)));
               _madHawkSpeed = cfg.Bind("MadHawk", "SpeedMultiplier", 1f, new ConfigDescription("Mad Hawk movement speed multiplier (1 = normal; below 1 pays for the wide vision; the result is kept inside the vanilla speed range 0.5-3)", new AcceptableValueRange<float>(0.5f, 1.5f)));
               _worshipperUses = cfg.Bind("Worshipper", "Uses", 1, new ConfigDescription("How many players the Worshipper may turn into Madmates per game (successful worships only)", new AcceptableValueRange<int>(1, 5)));
               _worshipperCooldown = cfg.Bind("Worshipper", "Cooldown", 30f, new ConfigDescription("Seconds between two worships (the Worshipper's kill button)", new AcceptableValueRange<float>(2.5f, 180f)));
               _jackalFriendsKnownToJackal = cfg.Bind("JackalFriends", "KnownToJackal", false, "The Jackal sees who the Jackal Friends are (blue names)");
               _jackalFriendsSheriffCanKill = cfg.Bind("JackalFriends", "SheriffCanKill", true, "The Sheriff can shoot Jackal Friends without dying");
               _nekomataVotersOnly = cfg.Bind("EvilNekomata", "VotersOnly", true, "The dragged player is picked among the players who voted for the Evil Nekomata (false = among every living player)");
               _nekomataExcludeImpostors = cfg.Bind("EvilNekomata", "ExcludeImpostors", true, "Impostor-team players (Impostors, Madmate family, an impostor lover) are never dragged");
               _nekomataAnnounce = cfg.Bind("EvilNekomata", "Announce", true, "Everyone reads who was dragged along after the ejection screen (false = only the victim is told)");
               _serialKillerKillCooldown = cfg.Bind("SerialKiller", "KillCooldown", 10f, new ConfigDescription("Serial Killer kill cooldown (seconds)", new AcceptableValueRange<float>(1f, 60f)));
               _serialKillerSuicideTime = cfg.Bind("SerialKiller", "SuicideTime", 30f, new ConfigDescription("Seconds without a kill before the Serial Killer dies by itself (paused during meetings; never below KillCooldown + 5)", new AcceptableValueRange<float>(10f, 300f)));
               _serialKillerResetAtMeeting = cfg.Bind("SerialKiller", "ResetAtMeeting", true, "The suicide timer restarts after every meeting (off: the remaining time carries over)");
               _samuraiKillCooldown = cfg.Bind("Samurai", "KillCooldown", 45f, new ConfigDescription("Seconds between two slashes (0 = the lobby's kill cooldown; 2.5 or more recommended)", new AcceptableValueRange<float>(0f, 180f)));
               _samuraiRange = cfg.Bind("Samurai", "Range", 2f, new ConfigDescription("Slash radius around the Samurai in map units (vanilla kill distances are roughly short 1 / medium 1.8 / long 2.5)", new AcceptableValueRange<float>(0.5f, 5f)));
               _samuraiStagger = cfg.Bind("Samurai", "Stagger", 0.3f, new ConfigDescription("Seconds between two bystander deaths of one slash (0.3 = the official server's packet spacing)", new AcceptableValueRange<float>(0.1f, 1f)));
               _samuraiHitTeammates = cfg.Bind("Samurai", "HitTeammates", false, "The slash also kills Impostor-team players in range (Impostors, Madmate family, an Impostor lover)");
               _evilHawkVision = cfg.Bind("EvilHawk", "VisionMultiplier", 2f, new ConfigDescription("Evil Hawk vision multiplier, applied to the impostor vision (always on)", new AcceptableValueRange<float>(1f, 5f)));
   ```
   Family wording of the two reused options (comment only in the cfg file): line 340 `"Sheriff can shoot Madmates without dying"` → `"Sheriff can shoot Mad-type roles (Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper) without dying"`; line 348 `"Impostors see who the Madmate is"` → `"Impostors see who the Mad-type players are (red Ⓜ; Ⓦ for the Worshipper; the Mad Mayor has its own switch)"`. Count / Chance for all nine come from the `Roles.All` loop (`[Roles] MadMayor.Count = 0`, `MadStuntman.Count`, `MadHawk.Count`, `Worshipper.Count`, `JackalFriends.Count`, `EvilHawk.Count`, `EvilNekomata.Count`, `SerialKiller.Count`, `Samurai.Count`, each `.Chance = 100`); default counts stay 0 → `Commands.ResetRoles` untouched.
3. Getters — after `        public static bool AssassinCanGuessFirstMeeting => _assassinFirstMeeting == null || _assassinFirstMeeting.Value;` (line 645):
   ```csharp

           // v0.5.0
           public static int MadMayorVotes => _madMayorVotes?.Value ?? 2;
           public static bool MadMayorKnownToImpostors => _madMayorKnownToImpostors != null && _madMayorKnownToImpostors.Value;
           public static int MadStuntmanLives => _madStuntmanLives?.Value ?? 1;
           public static bool MadStuntmanNotify => _madStuntmanNotify != null && _madStuntmanNotify.Value;   // default false
           public static float MadHawkVision => _madHawkVision?.Value ?? 3f;
           public static float MadHawkSpeed => _madHawkSpeed?.Value ?? 1f;
           public static int WorshipperUses => _worshipperUses?.Value ?? 1;
           public static float WorshipperCooldown => _worshipperCooldown?.Value ?? 30f;
           public static bool JackalFriendsKnownToJackal => _jackalFriendsKnownToJackal != null && _jackalFriendsKnownToJackal.Value;
           public static bool JackalFriendsSheriffCanKill => _jackalFriendsSheriffCanKill == null || _jackalFriendsSheriffCanKill.Value;
           public static bool EvilNekomataVotersOnly => _nekomataVotersOnly == null || _nekomataVotersOnly.Value;
           public static bool EvilNekomataExcludeImpostors => _nekomataExcludeImpostors == null || _nekomataExcludeImpostors.Value;
           public static bool EvilNekomataAnnounce => _nekomataAnnounce == null || _nekomataAnnounce.Value;
           public static float SerialKillerKillCooldown => _serialKillerKillCooldown?.Value ?? 10f;
           /// <summary>Raw option; SerialKiller.Limit() raises it to KillCooldown + 5 when set lower.</summary>
           public static float SerialKillerSuicideTime => _serialKillerSuicideTime?.Value ?? 30f;
           public static bool SerialKillerResetAtMeeting => _serialKillerResetAtMeeting == null || _serialKillerResetAtMeeting.Value;
           /// <summary>0 = the lobby kill cooldown (Samurai.KillCooldown()).</summary>
           public static float SamuraiKillCooldown => _samuraiKillCooldown?.Value ?? 45f;
           /// <summary>Slash radius in map units.</summary>
           public static float SamuraiRange => _samuraiRange?.Value ?? 2f;
           /// <summary>Seconds between two bystander deaths (never 0: one MurderPlayer RPC per HudManager tick). Options.cs has no UnityEngine import: System.Math.</summary>
           public static float SamuraiStagger => Math.Max(0.1f, Math.Min(1f, _samuraiStagger?.Value ?? 0.3f));
           public static bool SamuraiHitTeammates => _samuraiHitTeammates != null && _samuraiHitTeammates.Value;
           public static float EvilHawkVision => _evilHawkVision?.Value ?? 2f;
   ```
4. `BuildDescriptors()` — insert after the Assassin case's `                        break;` (line 813) and before the switch-closing `                }` (814):
   ```csharp
                       // ---- v0.5.0
                       case CustomRole.MadMayor:
                           _descriptors.Add(Int("madmayor.votes", sJa, sEn, "票数", "Votes", _madMayorVotes, 1, 5, 1, color)
                               .Tip("マッドメイヤーの1票を何票として数えるか。", "How many votes the Mad Mayor's single vote counts as.", "狂粉市长的一票算作几票。"));
                           _descriptors.Add(Bool("madmayor.known", sJa, sEn, "インポスターに公開", "Known to impostors", _madMayorKnownToImpostors, color)
                               .Tip("オンならインポスターに誰がマッドメイヤーか表示されます。", "On: Impostors see who the Mad Mayor is.", "开启后内鬼可以看到谁是狂粉市长。"));
                           break;
                       case CustomRole.MadStuntman:
                           _descriptors.Add(Int("madstuntman.lives", sJa, sEn, "耐えられるキル回数", "Kills survived", _madStuntmanLives, 1, 10, 1, color)
                               .Tip("この回数まではキルされても死にません（投票による追放は防げません）。", "Kill attempts the Mad Stuntman survives before one goes through (votes are never blocked).", "在此次数内被击杀也不会死（无法阻止投票放逐）。"));
                           _descriptors.Add(Bool("madstuntman.notify", sJa, sEn, "本人に通知", "Notify stuntman", _madStuntmanNotify, color)
                               .Tip("オンならキルを耐えたことと残り回数を本人にチャットで知らせます（ヴァンパイアの噛みつきや魔女の呪いを耐えた時も知らせます。キルした側にはいつも知らせます）。", "On: the stuntman is told in chat that it survived and how many attempts are left (also for an absorbed Vampire bite or Witch spell; the killer is always told).", "开启后会用聊天告诉本人挡下了击杀以及剩余次数（挡下吸血鬼的咬或女巫的诅咒时也会告知；击杀者始终会被告知）。"));
                           break;
                       case CustomRole.MadHawk:
                           _descriptors.Add(Float("madhawk.vision", sJa, sEn, "視界倍率", "Vision multiplier", _madHawkVision, 1f, 5f, 0.25f, color)
                               .Tip("マッドホークの視界の倍率（停電中は、狭くなった視界にこの倍率がかかります）。", "Vision multiplier of the Mad Hawk (during a blackout the shrunken vision is multiplied).", "鹰眼狂粉的视野倍率（停电时是缩小后视野的倍数）。"));
                           _descriptors.Add(Float("madhawk.speed", sJa, sEn, "速度倍率", "Speed multiplier", _madHawkSpeed, 0.5f, 1.5f, 0.25f, color)
                               .Tip("マッドホークの移動速度の倍率（1 = 通常。広い視界の代償に遅くするなら 1 未満）。", "Movement speed multiplier of the Mad Hawk (1 = normal; below 1 to pay for the wide vision).", "鹰眼狂粉的移动速度倍率（1 = 普通；小于 1 可作为大视野的代价）。"));
                           break;
                       case CustomRole.Worshipper:
                           _descriptors.Add(Int("worshipper.uses", sJa, sEn, "崇拝回数", "Worships", _worshipperUses, 1, 5, 1, color)
                               .Tip("1 試合に崇拝できる回数（成功した分だけ数えます）。", "How many players the Worshipper may convert per game (only successes count).", "每局可以崇拜的次数（只计成功的次数）。"));
                           _descriptors.Add(Float("worshipper.cooldown", sJa, sEn, "崇拝のクールダウン", "Worship cooldown", _worshipperCooldown, 2.5f, 180f, 2.5f, color)
                               .Tip("崇拝してから次に崇拝できるまでの秒数（キルボタンのクールダウン）。", "Seconds between two worships (the kill button's cooldown).", "两次崇拜之间的秒数（击杀键冷却）。"));
                           break;
                       case CustomRole.JackalFriends:
                           _descriptors.Add(Bool("jackalfriends.known", sJa, sEn, "ジャッカルに公開", "Known to Jackal", _jackalFriendsKnownToJackal, color)
                               .Tip("オンならジャッカルにジャッカルフレンズの名前が青く見えます。", "On: the Jackal sees the Jackal Friends' names in blue.", "开启后豺狼能看到豺狼之友的名字（蓝色）。"));
                           _descriptors.Add(Bool("jackalfriends.sheriff", sJa, sEn, "シェリフに撃たれる", "Sheriff can shoot", _jackalFriendsSheriffCanKill, color)
                               .Tip("オンならシェリフはジャッカルフレンズを撃っても死にません。オフなら誤射扱いでシェリフが死にます。", "On: the Sheriff may shoot Jackal Friends without dying. Off: shooting one is a misfire (the Sheriff dies).", "开启后警长射杀豺狼之友不会死亡；关闭则视为误杀，警长死亡。"));
                           break;
                       case CustomRole.EvilHawk:
                           _descriptors.Add(Float("evilhawk.vision", sJa, sEn, "視界倍率", "Vision multiplier", _evilHawkVision, 1f, 5f, 0.25f, color)
                               .Tip("イビルホークの視界の倍率（インポスターの視界に掛けます。常時有効）。", "Vision multiplier of the Evil Hawk (applied to the impostor vision, always on).", "邪恶鹰眼的视野倍率（乘以内鬼视野，始终有效）。"));
                           break;
                       case CustomRole.EvilNekomata:
                           _descriptors.Add(Bool("evilnekomata.voters", sJa, sEn, "道連れは投票者から", "Drag a voter only", _nekomataVotersOnly, color)
                               .Tip("オンなら自分に投票した人の中から、オフなら生存者全員の中から道連れを選びます。", "On: the victim is one of the players who voted for you. Off: any living player.", "开启：从投票给你的人中选择；关闭：从所有存活玩家中选择。"));
                           _descriptors.Add(Bool("evilnekomata.excludeimp", sJa, sEn, "インポスター陣営を除外", "Exclude impostor team", _nekomataExcludeImpostors, color)
                               .Tip("オンならインポスター陣営（マッド系役職含む）は道連れになりません。", "On: Impostor-team players (Madmate family included) are never dragged.", "开启后内鬼阵营（含狂粉系职业）不会被拖走。"));
                           _descriptors.Add(Bool("evilnekomata.announce", sJa, sEn, "道連れを全員に通知", "Announce the drag", _nekomataAnnounce, color)
                               .Tip("オンなら追放画面の後に「○○ は △△ の道連れになりました」と全員に届きます。オフなら本人にだけ届きます。", "On: after the ejection screen everyone reads who was dragged along. Off: only the victim is told.", "开启后放逐画面结束时所有人都会看到谁被拖走；关闭则只通知本人。"));
                           break;
                       case CustomRole.SerialKiller:
                           _descriptors.Add(Float("serialkiller.cooldown", sJa, sEn, "キルクールダウン", "Kill cooldown", _serialKillerKillCooldown, 1f, 60f, 1f, color)
                               .Tip("シリアルキラーがキルボタンを再び使えるまでの秒数。", "Seconds before the Serial Killer can kill again.", "连环杀手再次击杀所需的秒数。"));
                           _descriptors.Add(Float("serialkiller.time", sJa, sEn, "自殺までの時間", "Time until suicide", _serialKillerSuicideTime, 10f, 300f, 5f, color)
                               .Tip("前のキルからこの秒数キルしないと自滅します（会議中は止まります。キルCD+5 秒未満には下がりません）。", "Seconds without a kill before the Serial Killer dies by itself (paused during meetings; never below kill cooldown + 5).", "距上次击杀超过此秒数未击杀则自灭（会议中暂停；不会低于击杀冷却+5 秒）。"));
                           _descriptors.Add(Bool("serialkiller.meetingreset", sJa, sEn, "会議でタイマーをリセット", "Timer resets at meetings", _serialKillerResetAtMeeting, color)
                               .Tip("オンなら会議が終わるたびにタイマーが最初から始まります。オフなら残り時間を引き継ぎます。", "On: the timer restarts after every meeting. Off: the remaining time carries over.", "开启后每次会议结束计时重新开始；关闭则沿用剩余时间。"));
                           break;
                       case CustomRole.Samurai:
                           _descriptors.Add(Float("samurai.cooldown", sJa, sEn, "斬撃のクールダウン", "Slash cooldown", _samuraiKillCooldown, 0f, 180f, 2.5f, color)
                               .Tip("斬撃から次の斬撃までの秒数（0 = キルクールダウンと同じ）。", "Seconds between two slashes (0 = same as the kill cooldown).", "两次斩击之间的秒数（0 = 与击杀冷却相同）。"));
                           _descriptors.Add(Float("samurai.range", sJa, sEn, "斬撃の範囲", "Slash range", _samuraiRange, 0.5f, 5f, 0.25f, color)
                               .Tip("侍を中心にした半径。バニラのキル距離はおよそ 短1 / 中1.8 / 長2.5。", "Radius around the Samurai. Vanilla kill distances are roughly short 1 / medium 1.8 / long 2.5.", "以武士为中心的半径。原版击杀距离约为 短1 / 中1.8 / 长2.5。"));
                           _descriptors.Add(Float("samurai.stagger", sJa, sEn, "倒れる間隔", "Death interval", _samuraiStagger, 0.1f, 1f, 0.1f, color)
                               .Tip("巻き込まれた人が順に倒れる間隔の秒数（0.3 = 公式サーバーの送信間隔）。", "Seconds between two bystander deaths (0.3 = the official server's packet spacing).", "被波及者依次倒下的间隔秒数（0.3 = 官方服务器的发送间隔）。"));
                           _descriptors.Add(Bool("samurai.teammates", sJa, sEn, "味方も斬る", "Hits allies", _samuraiHitTeammates, color)
                               .Tip("オンなら範囲内のインポスター陣営（インポスター・マッド系役職など）も死にます。", "On: Impostor-team players in range (Impostors, Madmate family …) die too.", "开启后范围内的内鬼阵营（内鬼、狂粉系职业等）也会死亡。"));
                           break;
   ```
   Family wording in the same switch: Sheriff row (756-757) → `_descriptors.Add(Bool("sheriff.killmadmate", sJa, sEn, "マッド系を撃てる", "Can kill Mad roles", _sheriffCanKillMadmate, color)` `.Tip("オンならマッド系役職（マッドメイト・マッドメイヤー・マッドスタントマン・マッドホーク・崇拝者）を撃っても自分は死にません。", "On: shooting a Mad-type role (Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper) does not kill the Sheriff.", "开启后射杀狂粉系职业（内鬼狂粉、狂粉市长、疯狂特技演员、鹰眼狂粉、崇拜者）不会让警长死亡。"));` (the shortened row label is a settings-tab width guess — §12 step A3 checks it; revert to `"マッドメイトを撃てる" / "Can kill Madmate"` if it clips); Madmate row (786-787): label unchanged, `.Tip("オンならインポスターにマッド系役職（マッドメイト・マッドスタントマン・マッドホーク・崇拝者）が誰か表示されます（マッドメイヤーは別設定）。", "On: Impostors see who the Mad-type players are (Madmate, Mad Stuntman, Mad Hawk; Ⓦ for the Worshipper; the Mad Mayor has its own switch).", "开启后内鬼可以看到谁是狂粉系职业（内鬼狂粉、疯狂特技演员、鹰眼狂粉；崇拜者为 Ⓦ；狂粉市长另有设置）。"));`. All descriptor keys start with `<Key>.` → `SettingsTab.RoleOf` puts them on the roles page under their own header; nothing to change in `SettingsTab.cs`.
5. `TrySet` — after `                case "assassin.firstmeeting": case "assassin.first": case "assassin.canguessfirstmeeting": return SetBool(_assassinFirstMeeting, value, "assassin.firstmeeting", out message);` (line 1172):
   ```csharp
                   // v0.5.0 roles
                   case "madmayor.votes": case "madmayor.vote": return SetInt(_madMayorVotes, value, 1, 5, "madmayor.votes", out message);
                   case "madmayor.known": case "madmayor.knowntoimpostors": return SetBool(_madMayorKnownToImpostors, value, "madmayor.known", out message);
                   case "madstuntman.lives": case "madstuntman.guard": case "madstuntman.guards": case "stunt.lives": return SetInt(_madStuntmanLives, value, 1, 10, "madstuntman.lives", out message);
                   case "madstuntman.notify": case "madstuntman.notifystuntman": case "stunt.notify": return SetBool(_madStuntmanNotify, value, "madstuntman.notify", out message);
                   case "madhawk.vision": case "madhawk.visionmultiplier": return SetFloat(_madHawkVision, value, 1f, 5f, "madhawk.vision", out message);
                   case "madhawk.speed": case "madhawk.speedmultiplier": return SetFloat(_madHawkSpeed, value, 0.5f, 1.5f, "madhawk.speed", out message);
                   case "worshipper.uses": case "worshipper.times": case "worshipper.worships": return SetInt(_worshipperUses, value, 1, 5, "worshipper.uses", out message);
                   case "worshipper.cooldown": case "worshipper.cd": return SetFloat(_worshipperCooldown, value, 2.5f, 180f, "worshipper.cooldown", out message);
                   case "jackalfriends.known": case "jackalfriends.knowntojackal": case "jf.known": return SetBool(_jackalFriendsKnownToJackal, value, "jackalfriends.known", out message);
                   case "jackalfriends.sheriff": case "jackalfriends.sheriffcankill": case "jf.sheriff": return SetBool(_jackalFriendsSheriffCanKill, value, "jackalfriends.sheriff", out message);
                   case "evilhawk.vision": case "evilhawk.visionmultiplier": case "eh.vision": return SetFloat(_evilHawkVision, value, 1f, 5f, "evilhawk.vision", out message);
                   case "evilnekomata.voters": case "evilnekomata.votersonly": case "nekomata.voters": case "neko.voters": return SetBool(_nekomataVotersOnly, value, "evilnekomata.voters", out message);
                   case "evilnekomata.excludeimp": case "evilnekomata.excludeimpostors": case "nekomata.excludeimp": case "neko.excludeimp": return SetBool(_nekomataExcludeImpostors, value, "evilnekomata.excludeimp", out message);
                   case "evilnekomata.announce": case "nekomata.announce": case "neko.announce": return SetBool(_nekomataAnnounce, value, "evilnekomata.announce", out message);
                   case "serialkiller.cooldown": case "serialkiller.cd": case "serialkiller.killcooldown": case "sk.cooldown": case "sk.cd": return SetFloat(_serialKillerKillCooldown, value, 1f, 60f, "serialkiller.cooldown", out message);
                   case "serialkiller.time": case "serialkiller.suicide": case "serialkiller.suicidetime": case "serialkiller.limit": case "sk.time": return SetFloat(_serialKillerSuicideTime, value, 10f, 300f, "serialkiller.time", out message);
                   case "serialkiller.meetingreset": case "serialkiller.reset": case "serialkiller.resetatmeeting": case "sk.reset": return SetBool(_serialKillerResetAtMeeting, value, "serialkiller.meetingreset", out message);
                   case "samurai.cooldown": case "samurai.cd": case "samurai.killcooldown": return SetFloat(_samuraiKillCooldown, value, 0f, 180f, "samurai.cooldown", out message);
                   case "samurai.range": case "samurai.radius": return SetFloat(_samuraiRange, value, 0.5f, 5f, "samurai.range", out message);
                   case "samurai.stagger": case "samurai.interval": return SetFloat(_samuraiStagger, value, 0.1f, 1f, "samurai.stagger", out message);
                   case "samurai.teammates": case "samurai.allies": case "samurai.hitteammates": return SetBool(_samuraiHitTeammates, value, "samurai.teammates", out message);
   ```
   Doc comment (1040-1056): after `"assassin.guesses", "assassin.firstmeeting" (v0.4.1),` add `"madmayor.votes", "madmayor.known", "madstuntman.lives", "madstuntman.notify", "madhawk.vision", "madhawk.speed", "worshipper.uses", "worshipper.cooldown", "jackalfriends.known", "jackalfriends.sheriff", "evilhawk.vision", "evilnekomata.voters", "evilnekomata.excludeimp", "evilnekomata.announce", "serialkiller.cooldown", "serialkiller.time", "serialkiller.meetingreset", "samurai.cooldown", "samurai.range", "samurai.stagger", "samurai.teammates" (v0.5.0),`. `<role>.count|num|n` / `.chance|rate|c` (any key / alias / name) already work through the `Roles.TryParse` fallback — do **not** alias `worshipper.uses` to `count`. Mid-game `/opt` changes apply where each role reads the option live (§2–§10 say "next tally / next resync / next refresh").
6. `DescribeLines()` — after the Assassin case's `                        break;` (line 1258), before `                }`:
   ```csharp
                       // ---- v0.5.0
                       case CustomRole.MadMayor:
                           line += Lang.TF("opt.desc.madmayor", " {0}票", " {0} votes", MadMayorVotes);
                           if (MadMayorKnownToImpostors) line += Lang.T("opt.desc.madmayor.known", " インポスターに公開", ", known to impostors");
                           break;
                       case CustomRole.MadStuntman:
                           line += Lang.TF("opt.desc.madstuntman", " 耐久{0}回", " survives {0}", MadStuntmanLives);
                           if (MadStuntmanNotify) line += Lang.T("opt.desc.madstuntman.notify", " 本人に通知", ", notifies");
                           break;
                       case CustomRole.MadHawk:
                           line += $" x{MadHawkVision:0.#}";   // same inline form as Lighter / SpeedBooster
                           if (Math.Abs(MadHawkSpeed - 1f) > 0.001f) line += Lang.TF("opt.desc.madhawk.speed", " 速度x{0:0.##}", ", speed x{0:0.##}", MadHawkSpeed);
                           break;
                       case CustomRole.Worshipper:
                           line += Lang.TF("opt.desc.worshipper", " 崇拝{0}回 CD{1:0.#}秒", " {0} worship(s), CD {1:0.#}s", WorshipperUses, WorshipperCooldown);
                           break;
                       case CustomRole.JackalFriends:
                           if (JackalFriendsKnownToJackal) line += Lang.T("opt.desc.jackalfriends", " ジャッカルに公開", " known to Jackal");
                           if (!JackalFriendsSheriffCanKill) line += Lang.T("opt.desc.jackalfriends.sheriff", " シェリフ不可", ", Sheriff cannot shoot");
                           break;
                       case CustomRole.EvilHawk: line += $" x{EvilHawkVision:0.#}"; break;
                       case CustomRole.EvilNekomata:   // only deviations from the defaults are shown
                           if (!EvilNekomataVotersOnly) line += Lang.T("opt.desc.evilnekomata.anyone", " 生存者全員から", ", any player");
                           if (!EvilNekomataExcludeImpostors) line += Lang.T("opt.desc.evilnekomata.impok", " インポスターも対象", ", impostors too");
                           if (!EvilNekomataAnnounce) line += Lang.T("opt.desc.evilnekomata.silent", " 非公開", ", silent");
                           break;
                       case CustomRole.SerialKiller:
                           line += Lang.TF("opt.desc.serialkiller", " キルCD{0:0.#}秒 制限{1:0.#}秒", " KCD {0:0.#}s, limit {1:0.#}s", SerialKillerKillCooldown, SerialKillerSuicideTime);
                           if (!SerialKillerResetAtMeeting) line += Lang.T("opt.desc.serialkiller.noreset", " 会議で継続", ", no reset at meetings");
                           break;
                       case CustomRole.Samurai:
                           if (SamuraiKillCooldown > 0f) line += Lang.TF("opt.desc.samurai", " 斬撃CD{0:0.#}秒", " slash CD {0:0.#}s", SamuraiKillCooldown);
                           line += Lang.TF("opt.desc.samurai.range", " 範囲{0:0.#}", ", range {0:0.#}", SamuraiRange);
                           if (Math.Abs(SamuraiStagger - 0.3f) > 0.001f) line += Lang.TF("opt.desc.samurai.stagger", " 間隔{0:0.#}秒", ", stagger {0:0.#}s", SamuraiStagger);
                           if (SamuraiHitTeammates) line += Lang.T("opt.desc.samurai.teammates", " 味方も斬る", ", hits allies");
                           break;
   ```
   `opt.desc.sheriff.madmate` (`" マッド可"` / `", can kill Madmate"`, line 1230) is kept as is (a ≤ 100-char `/show` token; ja/zh already read as the family). `SetCount` clamp / count-row max stay 15 for every new role.

### 1.5 `src/Chat/Commands.cs`
* `IsAdminOptKey` — replace line 355 `                || k.StartsWith("lovers.") || k.StartsWith("arsonist.") || k.StartsWith("witch.") || k.StartsWith("assassin."))` with two real source lines:
  ```csharp
                  || k.StartsWith("lovers.") || k.StartsWith("arsonist.") || k.StartsWith("witch.") || k.StartsWith("assassin.")
                  || k.StartsWith("madmayor.") || k.StartsWith("madstuntman.") || k.StartsWith("stunt.") || k.StartsWith("madhawk.") || k.StartsWith("worshipper.")
                  || k.StartsWith("jackalfriends.") || k.StartsWith("jf.") || k.StartsWith("evilhawk.") || k.StartsWith("evilnekomata.") || k.StartsWith("nekomata.")
                  || k.StartsWith("neko.") || k.StartsWith("serialkiller.") || k.StartsWith("sk.") || k.StartsWith("samurai."))
  ```
  (symmetry with the v0.4.1 list; the `Roles.TryParse` fallback below it already admits every `<key|alias|name>.<anything>`).
* `RoleOptionText` — insert before `                default: return null;` (line 774), after the Assassin case:
  ```csharp
                  // ---- v0.5.0
                  case CustomRole.MadMayor:
                      return Lang.TF("cmd.ro.madmayor", "投票は {0}票分、インポスターに公開: {1}", "Vote counts as {0}, known to impostors: {1}", Options.MadMayorVotes, OnOff(Options.MadMayorKnownToImpostors));
                  case CustomRole.MadStuntman:
                      return Lang.TF("cmd.ro.madstuntman", "キルを {0} 回まで耐える、本人に通知: {1}", "Survives {0} kill(s), notify stuntman: {1}", Options.MadStuntmanLives, OnOff(Options.MadStuntmanNotify));
                  case CustomRole.MadHawk:
                      return Options.MadmateKnownToImpostors
                          ? Lang.TF("cmd.ro.madhawk.known", "視界 x{0:0.#}、速度 x{1:0.##}、インポスターに公開", "Vision x{0:0.#}, speed x{1:0.##}, known to impostors", Options.MadHawkVision, Options.MadHawkSpeed)
                          : Lang.TF("cmd.ro.madhawk", "視界 x{0:0.#}、速度 x{1:0.##}", "Vision x{0:0.#}, speed x{1:0.##}", Options.MadHawkVision, Options.MadHawkSpeed);
                  case CustomRole.Worshipper:
                      return Lang.TF("cmd.ro.worshipper", "崇拝 {0} 回、崇拝CD {1:0.#}秒、シェリフが撃てる: {2}", "{0} worship(s), cooldown {1:0.#}s, Sheriff can shoot: {2}", Options.WorshipperUses, Options.WorshipperCooldown, OnOff(Options.SheriffCanKillMadmate));
                  case CustomRole.JackalFriends:
                      return Lang.TF("cmd.ro.jackalfriends", "ジャッカルに公開: {0}、シェリフに撃たれる: {1}", "Known to Jackal: {0}, Sheriff can shoot: {1}", OnOff(Options.JackalFriendsKnownToJackal), OnOff(Options.JackalFriendsSheriffCanKill));
                  case CustomRole.EvilHawk:
                      return Lang.TF("cmd.ro.evilhawk", "視界 x{0:0.#}（インポスターの視界に掛ける、常時）", "Vision x{0:0.#} (times the impostor vision, always on)", Options.EvilHawkVision);
                  case CustomRole.EvilNekomata:
                      return Lang.TF("cmd.ro.evilnekomata", "道連れ: {0}、インポスター陣営を除外: {1}、全員に通知: {2}", "Drag: {0}, impostor team excluded: {1}, announce: {2}",
                          Options.EvilNekomataVotersOnly ? Lang.T("cmd.ro.evilnekomata.voters", "投票者から 1 人", "one of your voters") : Lang.T("cmd.ro.evilnekomata.anyone", "生存者から 1 人", "any living player"),
                          OnOff(Options.EvilNekomataExcludeImpostors), OnOff(Options.EvilNekomataAnnounce));
                  case CustomRole.SerialKiller:
                      return Lang.TF("cmd.ro.serialkiller", "キルCD {0:0.#}秒、自殺まで {1:0.#}秒、会議でリセット: {2}", "Kill cooldown {0:0.#}s, suicide after {1:0.#}s, reset at meetings: {2}",
                          Options.SerialKillerKillCooldown, Options.SerialKillerSuicideTime, OnOff(Options.SerialKillerResetAtMeeting));
                  case CustomRole.Samurai:
                      return Lang.TF("cmd.ro.samurai", "斬撃CD {0}、範囲 {1:0.#}、間隔 {2:0.#}秒、味方も斬る: {3}", "Slash cooldown {0}, range {1:0.#}, stagger {2:0.#}s, hits allies: {3}",
                          Options.SamuraiKillCooldown > 0f ? Options.SamuraiKillCooldown.ToString("0.#") + "s" : Lang.T("cmd.ro.samurai.samecd", "キルと同じ", "same as kill"),
                          Options.SamuraiRange, Options.SamuraiStagger, OnOff(Options.SamuraiHitTeammates));
  ```
* Family wording in the same switch (lines 750-751, 761): `"キルCD {0:0.#}秒、マッドメイトを撃てる" / "Kill cooldown {0:0.#}s, can shoot Madmate"` → `"キルCD {0:0.#}秒、マッド系を撃てる" / "Kill cooldown {0:0.#}s, can shoot Mad roles"`; `"キルCD {0:0.#}秒、マッドメイトは撃てない" / "…cannot shoot Madmate"` → `"キルCD {0:0.#}秒、マッド系は撃てない" / "Kill cooldown {0:0.#}s, cannot shoot Mad roles"`; `cmd.ro.madmate` `"インポスターはマッドメイトが誰か分かる" / "Impostors know who the Madmate is"` → `"インポスターはマッド系役職が誰か分かる" / "Impostors know who the Mad-type players are"`. `/cmd r sheriff` deliberately does **not** show the Jackal Friends flag (read it with `/cmd r jf`). `OptUsageText()` is left stale (8 fixed lines); `IsEveryoneCommand`, `Assign`, `SetRole`, `ResetRoles` unchanged. No new player command in this batch.

### 1.6 `src/Game/Kills.cs` (shared parts — every switch, guard and helper the six role classes call)
1. **Host-target whitelist** — replace line 186 `            if (target != null && (role == CustomRole.None || role == CustomRole.Mafia || role == CustomRole.Assassin || role == CustomRole.Lovers)` with `            if (target != null && VanillaKillsHost(role)` and add next to `CanSheriffKill`:
   ```csharp
           /// <summary>
           /// Killers whose case returns true to vanilla CheckMurder (vanilla impostors, Mafia, Assassin, an impostor lover, v0.5.0 Evil Hawk /
           /// Evil Nekomata): a host that is locally Impostor (desync role) as their target must be killed by Rpc.Kill here. Roles with their own
           /// switch case (Witch, Serial Killer, Samurai …) call Rpc.Kill themselves and are NOT listed.
           /// </summary>
           private static bool VanillaKillsHost(CustomRole role)
           {
               switch (role)
               {
                   case CustomRole.None: case CustomRole.Mafia: case CustomRole.Assassin: case CustomRole.Lovers:
                   case CustomRole.EvilHawk: case CustomRole.EvilNekomata:
                       return true;
                   default:
                       return false;
               }
           }
   ```
   (extend the comment above the block with `, Evil Hawk, Evil Nekomata`; `allowed` already requires `Game.IsImpostorTeamKiller(killerId)`, true for both after §1.3.)
2. **Mad Stuntman guard** — insert before line 205 `            if (role == CustomRole.None) return true;`:
   ```csharp
               // v0.5.0 Mad Stuntman: the first [MadStuntman] Lives kill attempts on it fail. Decided here, before the vanilla path, so a
               // vanilla impostor's kill is caught too; the killer's button restarts as if it had killed (Rpc.ResetKillCooldown, the Vampire
               // pattern) and no MurderPlayer reaches anyone. A pressed shielded target absorbs a Samurai's whole slash (no bystanders).
               if (target != null && TryStuntmanGuard(killer, target, role)) return false;
   ```
   and insert the method (plus one blank line) immediately before the unique line `        /// <summary>Private notice, built in the recipient's language (see Lang.PlayerLang). Texts may use {0}… placeholders.</summary>` (line 144):
   ```csharp
           /// <summary>v0.5.0 shared gate: true while the target's Mad Stuntman shield absorbs kills. Consumed by the Samurai bystander filter and the Evil Nekomata candidate filter (no life spent there); presses go through TryStuntmanGuard.</summary>
           internal static bool IsShielded(byte targetId) => MadStuntman.Remaining(targetId) > 0;

           /// <summary>Killers whose kill goes through vanilla CheckMurder (HandleCheckMurder returns true): vanilla refuses moving-platform / ladder / vent-entering targets itself.</summary>
           private static bool IsVanillaKillPath(CustomRole role) =>
               role == CustomRole.None || role == CustomRole.Lovers || role == CustomRole.Assassin || role == CustomRole.EvilHawk || role == CustomRole.EvilNekomata;

           /// <summary>
           /// Mad Stuntman guard (v0.5.0): true when <paramref name="target"/> is a Mad Stuntman with lives left and the kill of
           /// <paramref name="killer"/> (custom role <paramref name="role"/>) would otherwise have succeeded. Consumes one life, restarts the
           /// killer's cooldown with its own cooldown (≥ 1 s) and notifies both. Anything that is not a plain kill (Mafia gate, Sheriff misfire,
           /// Arsonist douse, Worshipper convert, invalid murder, a target vanilla itself refuses) returns false so the normal path answers it.
           /// </summary>
           private static bool TryStuntmanGuard(PlayerControl killer, PlayerControl target, CustomRole role)
           {
               byte killerId = killer.PlayerId, targetId = target.PlayerId;
               if (Game.RoleOf(targetId) != CustomRole.MadStuntman) return false;
               if (MadStuntman.Remaining(targetId) <= 0) return false;      // lives spent: the normal path decides (vanilla kill, shot, bite, spell, slash)
               if (!IsValidMurder(killer, target)) return false;            // meeting / vent / GA shield / dead: no life spent, the normal path answers
               // Vanilla CheckMurder refuses these targets itself (Airship moving platform / ladders, vent-enter animation) and the mod's
               // custom-killer paths never checked them: let vanilla answer a vanilla-path press (no life, no reset).
               if (IsVanillaKillPath(role) && (target.inMovingPlat || target.onLadder || target.walkingToVent)) return false;
               bool wouldKill;
               float cooldown;
               switch (role)
               {
                   case CustomRole.None:                                    // vanilla impostor
                   case CustomRole.Lovers:                                  // impostor lover (a crew lover has no button)
                   case CustomRole.EvilHawk:                                // v0.5.0 vanilla-path impostor-pool roles
                   case CustomRole.EvilNekomata:
                       wouldKill = Game.IsImpostorTeamKiller(killerId); cooldown = LobbyKillCooldown(); break;
                   case CustomRole.Mafia:                                   // a blocked Mafia stays blocked (its own case: FailKill + notice)
                       wouldKill = !AnyOtherImpostorKillerAlive(killerId); cooldown = LobbyKillCooldown(); break;
                   case CustomRole.Assassin:
                   case CustomRole.Vampire:                                 // the bite is absorbed at the press: no Bites entry
                       wouldKill = true; cooldown = LobbyKillCooldown(); break;
                   case CustomRole.Witch:                                   // the spell is absorbed at the press: no Spelled entry
                       wouldKill = true; cooldown = Witch.SpellCooldown(); break;
                   case CustomRole.Jackal:
                       wouldKill = true; cooldown = Options.JackalKillCooldown; break;
                   case CustomRole.Sheriff:                                 // a misfire stays a misfire (the Sheriff dies, no life spent)
                       wouldKill = CanSheriffKill(targetId); cooldown = Options.SheriffKillCooldown; break;
                   case CustomRole.SerialKiller:                            // v0.5.0: the guarded press restarts its countdown (MadStuntman.OnGuarded)
                       wouldKill = true; cooldown = Options.SerialKillerKillCooldown; break;
                   case CustomRole.Samurai:                                 // v0.5.0: the whole slash is absorbed (guard returns before the Samurai case)
                       wouldKill = true; cooldown = Samurai.KillCooldown(); break;
                   default:                                                 // Arsonist douse, Worshipper convert and anything else: not a kill
                       wouldKill = false; cooldown = 0f; break;
               }
               if (!wouldKill) return false;
               // ≥ 1 s: at a 0 s lobby cooldown (VanillaRanges) every press of a mashed button would otherwise cost a life and send an options
               // pair + a MurderPlayer to that client (GameDataTo bursts kick the host, findings #49/#52).
               Rpc.ResetKillCooldown(killer, Mathf.Max(1f, cooldown));   // host: SetKillTimer; client: options ×2 + FailedProtected + options back
               MadStuntman.OnGuarded(killerId, targetId, role);
               return true;
           }
   ```
   (`Mathf` from the file's `using UnityEngine;`; `inMovingPlat` / `onLadder` / `walkingToVent` exist on `PlayerControl` in the 2026.8.18 interop.)
3. `CanSheriffKill` — replace line 115 `            if (role == CustomRole.Madmate && Options.SheriffCanKillMadmate) return true;` with
   ```csharp
               if (Roles.IsMadType(role) && Options.SheriffCanKillMadmate) return true;         // Madmate family (v0.5.0: Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper, converts)
               if (role == CustomRole.JackalFriends && Options.JackalFriendsSheriffCanKill) return true; // v0.5.0
   ```
4. `CanVent` — after line 78 `            if (role == CustomRole.Lovers) return true; // a lover keeps its vanilla side's abilities (vanilla already rejects a crew lover)` add
   ```csharp
               // v0.5.0: a Madmate is a vanilla crew client (vanilla rejects its vents itself); a vanilla Engineer converted by the Worshipper
               // keeps its vents instead of being booted. Also covers a /assign-forced Engineer Madmate (previously booted). The Worshipper
               // itself (Impostor client) stays on the RoleInfo path (CanVent = false → boot).
               if (role == CustomRole.Madmate) return true;
   ```
5. Role switch — replace lines 268-273
   ```csharp
                   case CustomRole.Witch:
                       Witch.Spell(killer, target);
                       return false;

                   default:
                       return true; // Assassin: vanilla kill (an impostor lover already left at the IsKiller check above)
   ```
   with
   ```csharp
                   case CustomRole.Witch:
                       Witch.Spell(killer, target);
                       return false;

                   // ---- v0.5.0
                   // worship (Vampire pattern on a crew target: it becomes a Madmate; an impostor target kills the worshipper)
                   case CustomRole.Worshipper:
                       Worshipper.Worship(killer, target);
                       return false;
                   // host-executed kill (Jackal pattern): the wire message equals vanilla's success path, but nothing depends on vanilla's
                   // CheckMurder body validating (or not) a killer whose timer is shorter than the lobby cooldown.
                   case CustomRole.SerialKiller:
                       PocketRolesPlugin.Logger.LogInfo($"Kills: Serial Killer {Game.NameOf(killerId)} killed {Game.NameOf(targetId)}");
                       Rpc.Kill(killer, target);
                       return false;
                   // the kill is an area slash (pressed target + everyone in range, host-side positions, paced)
                   case CustomRole.Samurai:
                       Samurai.Slash(killer, target);
                       return false;

                   default:
                       return true; // Assassin / Evil Hawk / Evil Nekomata: vanilla kill (an impostor lover already left at the IsKiller check above)
   ```
6. `OnMurder` — after the bite bookkeeping block (`… Game.Bites.Remove(targetId); }`) and before `            // v0.4.1 role bookkeeping on any death`:
   ```csharp
               // v0.5.0: a Serial Killer that killed someone else restarts its countdown (its own time-out death arrives here with killer == target and is ignored)
               if (killer != null && killer.PlayerId != targetId) SerialKiller.OnKill(killer.PlayerId);
   ```
7. `ResetNotices()` — after `            Arsonist.ResetNotices();` (line 418): `            Worshipper.ResetNotices();` and `            _slashUntil = -1f;`.
8. **Slash window** — next to `        private static readonly List<byte> Scratch = new List<byte>();` (line 31):
   ```csharp
           /// <summary>v0.5.0 (Samurai): a paced multi-victim slash is running until this Time.time; WinConditions.Check and the Bait auto-report wait for it (bounded: stagger window + 1 s, so a victim hiding in a vent cannot block the end).</summary>
           private static float _slashUntil = -1f;
           internal static void MarkSlash(float seconds) => _slashUntil = Time.time + seconds;
           internal static bool SlashInProgress() => Game.Bites.Count > 0 && Time.time < _slashUntil;
   ```
9. **Bite loops re-check the end state** — `Tick()` line 49 `            for (int i = 0; i < Scratch.Count; i++) ExecuteBite(Scratch[i], true);` → `            for (int i = 0; i < Scratch.Count; i++) { if (Game.Ending || !Game.InProgress) break; ExecuteBite(Scratch[i], true); }` and `FlushBites()` line 64 likewise with `false` (the `Bites.Clear()` after that loop stays). Comment: `// v0.5.0: a Terrorist death inside the batch ends the game synchronously — no MurderPlayer after EndGame`.
10. `ExecuteBite` — line 349 `            if (allowPostpone && victim.inVent)` → `            if (allowPostpone && (victim.inVent || victim.onLadder || victim.inMovingPlat))` (same +1 s postponement; vanilla refuses such targets and a self-kill there was never exercised).
11. `ForceReport` — after line 324 `                if (MeetingHud.Instance != null || ExileController.Instance != null) return;` insert
    ```csharp
                    // v0.5.0: a Samurai's slash is still executing (paced bites): reporting now would make FlushBites fire every remaining victim
                    // in one frame. Wait for the batch (bounded by Kills.MarkSlash).
                    if (SlashInProgress()) { Scheduler.After(0.3f, () => ForceReport(reporterId, bodyId)); return; }
    ```
12. Doc comment of `LobbyKillCooldown()` (`used for Vampire / Mafia / Witch, …`): add `/ Samurai (0 = lobby) / the Stuntman guard`. `Notice`, `HandleEnterVent`, `HandleSabotage`, `HandleCheckMurder`'s `Ending` guard, `Kills_LobbyStartPatch`: unchanged.

### 1.7 `src/Game/Meetings.cs` (shared hooks: vote weight, voter reader, exile drag, Serial Killer pause/resume)
1. **Vote weight helper** — inside `public static class Meetings`, after the anchor
   ```csharp
           private static bool IsCountedVote(byte vote, byte hasNotVoted, byte missedVote, byte deadVote)
           {
               return vote != hasNotVoted && vote != missedVote && vote != deadVote;
           }
   ```
   add:
   ```csharp

           /// <summary>
           /// Weight of one counted vote: [Mayor] Votes for a Mayor, [MadMayor] Votes for a Mad Mayor (v0.5.0), else 1.
           /// Callers apply it only to living voters (a dead / disconnected voter counts once, as vanilla would).
           /// </summary>
           internal static int VoteWeightOf(byte voter)
           {
               switch (Core.Game.RoleOf(voter))
               {
                   case CustomRole.Mayor: return Math.Max(1, Options.MayorVotes);
                   case CustomRole.MadMayor: return Math.Max(1, Options.MadMayorVotes);
                   default: return 1;
               }
           }

           /// <summary>
           /// v0.5.0 generic "who voted for whom" reader (Evil Nekomata): players whose counted vote was <paramref name="targetId"/>, one entry
           /// per player — the Mayor / Mad Mayor tally repeats a VoterState per extra vote. <paramref name="states"/> is the tallied array bound
           /// by Meetings_VotingCompletePatch (the guaranteed copy); <paramref name="hud"/>.playerStates is the fallback only (whether
           /// PlayerVoteArea.VotedForId survives vanilla's ClearForResults is not verifiable from the interop).
           /// </summary>
           internal static List<byte> VotersFor(byte targetId, Il2CppStructArray<MeetingHud.VoterState> states, MeetingHud hud)
           {
               var seen = new HashSet<byte>();
               var list = new List<byte>();
               if (states != null && states.Length > 0)
               {
                   for (int i = 0; i < states.Length; i++)
                   {
                       var vs = states[i];
                       byte voter = vs.VoterId;   // byte locals first: no operator ambiguity whichever way the interop typed the fields
                       byte voted = vs.VotedForId;
                       if (voted != targetId) continue;
                       if (seen.Add(voter)) list.Add(voter);
                   }
                   return list;
               }
               var areas = hud != null ? hud.playerStates : null;
               if (areas == null) return list;
               foreach (var pva in areas)
               {
                   if (pva == null || pva.AmDead || !pva.DidVote) continue;
                   byte voter = pva.PlayerId;
                   byte vote = pva.VotedForId;
                   if (vote != targetId) continue;
                   if (seen.Add(voter)) list.Add(voter);
               }
               return list;
           }
   ```
   (`using System;`, `using System.Collections.Generic;`, `using Il2CppInterop.Runtime.InteropTypes.Arrays;` are already at the top — `Math.Max` and `Il2CppStructArray` are used today.)
2. `TryEndVotingWithMayor` — delete line 57 `            int mayorVotes = Math.Max(1, Options.MayorVotes);` and replace lines 70-79
   ```csharp
                   int weight = 1;
                   if (Core.Game.RoleOf(voter) == CustomRole.Mayor && Core.Game.IsAlive(voter) && !ps.AmDead)
                   {
                       weight = mayorVotes;
                       // Duplicate entries make vanilla clients draw one extra vote icon per extra vote.
                       for (int i = 1; i < mayorVotes; i++)
                           states.Add(new MeetingHud.VoterState { VoterId = voter, VotedForId = vote });
                   }
                   tally.TryGetValue(vote, out int cur);
                   tally[vote] = cur + weight;
   ```
   with
   ```csharp
                   // v0.5.0: Mayor and Mad Mayor share one weight helper (ps.AmDead voters left above; a disconnected voter is not IsAlive and counts once).
                   int weight = Core.Game.IsAlive(voter) ? VoteWeightOf(voter) : 1;
                   // Duplicate entries make vanilla clients draw one extra vote icon per extra vote.
                   for (int i = 1; i < weight; i++)
                       states.Add(new MeetingHud.VoterState { VoterId = voter, VotedForId = vote });
                   tally.TryGetValue(vote, out int cur);
                   tally[vote] = cur + weight;
   ```
   Doc comment line 39 → `/// Replacement tally used only when an alive Mayor / Mad Mayor has cast a counted vote. …`. The `Meetings: Mayor tally → exiled=… votes={states.Count}` log stays (`votes` = players + extra icons).
3. `AliveMayorVoted` — line 141 `            if (Options.MayorVotes <= 1) return false;` → `            if (Options.MayorVotes <= 1 && Options.MadMayorVotes <= 1) return false; // nobody can carry extra votes → vanilla tally`; line 151 `                if (Core.Game.RoleOf(voter) != CustomRole.Mayor || !Core.Game.IsAlive(voter)) continue;` → `                if (VoteWeightOf(voter) <= 1 || !Core.Game.IsAlive(voter)) continue; // Mayor or Mad Mayor (v0.5.0)`; doc comment line 138 → `/// An alive Mayor / Mad Mayor has cast a vote that counts (skip included) and extra votes are enabled.` Method names stay (`Meetings_CheckForEndVotingPatch` calls them by name).
4. `Meetings_VotingCompletePatch` — replace line 256 `        private static void Postfix(MeetingHud __instance, NetworkedPlayerInfo exiled)` with
   ```csharp
           private static void Postfix(MeetingHud __instance, [HarmonyArgument(0)] Il2CppStructArray<MeetingHud.VoterState> states, NetworkedPlayerInfo exiled)
   ```
   (vanilla `VotingComplete(Il2CppStructArray<VoterState> states, NetworkedPlayerInfo exiled, bool tie, bool wasOverruled, ushort overruleNonce)`; the interop parameter is literally named `states`, so the attribute is belt-and-braces — §12 gate G0: if the startup log shows `PatchAll failed`, drop the attribute first, then the parameter) and extend the hook block (lines 265-270) to
   ```csharp
                   if (exiledId != 255)
                   {
                       Witch.OnPlayerExiled(exiledId);
                       Arsonist.OnPlayerDied(exiledId);
                       Lovers.OnPlayerExiled(exiledId);
                       EvilNekomata.OnPlayerExiled(exiledId, states, __instance);   // v0.5.0: an exiled Evil Nekomata drags one voter (bite; skipped when the exile ends the game)
                   }
   ```
   This runs before `AntiBlackout.Prepare` in the vanilla path and after it in the Mayor / Mad Mayor path; both are fine (a bite changes neither `IsAlive` nor `WouldContinue(exiledId)`, Lovers precedent; the hook's own `WouldContinue` call is the same evaluation `Prepare` makes).
5. `Meetings_ExileWrapUpPatch.Postfix` — three insertions:
   * after the bite-postponement block (the `}` that closes `if (Core.Game.Bites.Count > 0) { … }`, line 308) and before `                byte exiledId = Core.Game.LastExiled;`:
     ```csharp
                     SerialKiller.OnExileWrapUp();   // v0.5.0: countdowns hold until the resume below (clients' kill timers are reset there)
     ```
   * after line 328 `                Witch.OnMeetingEnd(exiledId);`:
     ```csharp
                     EvilNekomata.OnMeetingEnd(exiledId);   // v0.5.0: drag bite → +2.5 s (behind the +2 s curses), public line scheduled at +2 s
     ```
   * inside the existing `Scheduler.After(2f, () => { … });`, replace lines 336-338
     ```csharp
                         OptionsDesync.ResyncAll();
                         NameTags.RefreshAll(force: true);
                         WinConditions.Check();
     ```
     with
     ```csharp
                         OptionsDesync.ResyncAll();
                         SerialKiller.OnMeetingEnd();   // v0.5.0: reset / carry over, authoritative kill-timer reset, private notice (before the forced tag refresh so the countdown tag rides along)
                         NameTags.RefreshAll(force: true);
                         WinConditions.Check();
     ```
   Resulting WrapUp order: `Restore` +1.5 s → bites → +2 s → `SerialKiller.OnExileWrapUp()` → pending solo / exiled Terrorist / `CheckNow()` → `Witch.OnMeetingEnd` (curse bites +2 s) → `EvilNekomata.OnMeetingEnd` (drag → +2.5 s, announce +2 s) → `ResyncAll()` → +2 s lambda (`ResyncAll`, `SerialKiller.OnMeetingEnd`, `RefreshAll`, `Check`). At +2 s the scheduler runs before `Kills.Tick` in the same frame (an SK whose own bite executes now is seen as "already dying"); the Nekomata announce callback was scheduled earlier than the +2 s lambda and runs first. `Meetings_ReportDeadBodyPatch`, `Meetings_MeetingStartPatch`, `MeetingTools`: unchanged.

### 1.8 `src/Game/WinConditions.cs` (shared)
1. `EvaluateBase` — replace lines 253-267
   ```csharp
               int imp = 0, jackal = 0, others = 0, madmate = 0, alive = 0;
               …
                   else
                   {
                       others++;
                       if (Core.Game.RoleOf(id) == CustomRole.Madmate) madmate++;
                   }
               }
               int crewForCount = others - madmate;
   ```
   with
   ```csharp
               int imp = 0, jackal = 0, others = 0, madmate = 0, friends = 0, alive = 0;
               foreach (var id in Core.Game.AllPlayerIds())
               {
                   if (id == exiledId) continue;
                   if (!Core.Game.IsAlive(id)) continue;
                   alive++;
                   if (Core.Game.IsImpostorTeamKiller(id)) imp++;
                   else if (Core.Game.IsJackal(id)) jackal++;
                   else
                   {
                       others++;
                       var r = Core.Game.RoleOf(id);
                       if (Roles.IsMadType(r)) madmate++;                     // Madmate family (v0.5.0: Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper, converts): not crew for either threshold
                       else if (r == CustomRole.JackalFriends) friends++;    // v0.5.0: the Jackal's Madmate — not crew for either threshold, never a killer
                   }
               }
               int crewForCount = others - madmate - friends;
               if (Core.Game.TestMode)
                   _lastCounts = $"alive={alive} imp={imp} jackal={jackal} others={others} madmate={madmate} friends={friends} crewForCount={crewForCount}";
   ```
   Rules unchanged in shape (`imp == 0 && jackal == 0 → Crew`, `jackal == 0 && imp >= crewForCount → Impostor`, `imp == 0 && jackal >= crewForCount → Jackal`). Comment on `ComputeWinners` line 290 → `// Madmate family (Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper, converts), Vampire, Mafia, Witch, Assassin, Evil Hawk, Evil Nekomata, Serial Killer, Samurai, vanilla impostors`.
2. `ComputeWinners` — replace line 291 `                    case WinKind.Jackal: win = role == CustomRole.Jackal && (Core.Game.IsAlive(id) || id == soloId); break;` with
   ```csharp
                       case WinKind.Jackal:
                           // v0.5.0: Jackal Friends win with the Jackal, dead or alive (Madmate parity); dead Jackals still lose
                           win = (role == CustomRole.Jackal && (Core.Game.IsAlive(id) || id == soloId)) || role == CustomRole.JackalFriends;
                           break;
   ```
3. `Check()` — after line 55 `                if (MeetingHud.Instance != null || ExileController.Instance != null) return;` insert `                if (Kills.SlashInProgress()) return; // v0.5.0: a Samurai's paced slash finishes first (bounded, Kills.MarkSlash)` (before the `SoloWinner` line). `CheckNow()` (WrapUp) is **not** gated; `ThrottledCheck()` and the 0.1 s post-death check both go through `Check()`.
4. **Test-mode count line** (batch decision 9) — fields after line 23 `        private static byte _evalSoloId = 255;`:
   ```csharp

           // v0.5.0 test-mode diagnostic (CheckNow): the last "WinConditions(test)" line, written only when it changes.
           private static string _lastTestLine, _lastCounts = "";
   ```
   `CheckNow()` — inside `if (Core.Game.TestMode) { … }` (lines 72-77) insert `                    LogTestOutcome();   // v0.5.0: the counting rules are otherwise invisible in test mode` before its `return;`. New private method next to `WouldContinue`:
   ```csharp
           /// <summary>Test mode only: evaluates the true rules without ending anything and logs the counts when they change.</summary>
           private static void LogTestOutcome()
           {
               Outcome o = Evaluate(255);   // EndFromOutcome is deliberately not called
               string line = "WinConditions(test): outcome=" + o + " " + _lastCounts;
               if (line == _lastTestLine) return;
               _lastTestLine = line;
               PocketRolesPlugin.Logger.LogInfo(line);
           }
   ```
   `OnLobby()` — after `            _endedByMod = false;` add `            _lastTestLine = null; _lastCounts = "";`. Cost = a normal game's `ThrottledCheck` (`Evaluate(255)` every 0.25 s while `/test on`; side effects `RecomputeTaskCounts` and `_evalSoloId` only). No new `WinKind` / `Outcome` / `win.*` key / `IsSoloKind` change in this batch: the Mad family and the Worshipper's converts win on `WinKind.Impostor` (`TeamOf == Team.Impostor`), the Jackal Friends on `WinKind.Jackal`, the four impostor-pool roles on `WinKind.Impostor`. `BuildSummary` prints the role name via `Roles.Info(role).Name`; a convert is listed as マッドメイト (its original role survives only in the `Game: #id … -> Madmate` log line). `Win_RecomputeTaskCountsPatch` untouched (non-Crew teams never count; a Worshipper is also `IsDesyncImpostor`).

### 1.9 `src/Net/OptionsDesync.cs`
1. `BuildFor` — after lines 85-87 (`case CustomRole.Witch: … break;`) and before `                    // Vampire / Mafia: vanilla impostor kill cooldown (base value) unless overridden below.`:
   ```csharp
                       // ---- v0.5.0
                       // Mad Hawk = Lighter pattern (crewmate-basis client reads CrewLightMod) + optional SpeedBooster-style factor. The speed
                       // product is clamped to the vanilla-legal PlayerSpeedMod range (NormalGameOptions.AreInvalid: 0 < speed <= 3; VanillaRanges
                       // keeps the lobby value in 0.5..3): a client never receives 0.25 (lobby 0.5 x option 0.5). The vision product is not clamped (Lighter precedent).
                       case CustomRole.MadHawk:
                           opts.SetFloat(FloatOptionNames.CrewLightMod, opts.GetFloat(FloatOptionNames.CrewLightMod) * Options.MadHawkVision);
                           if (Options.MadHawkSpeed != 1f)
                               opts.SetFloat(FloatOptionNames.PlayerSpeedMod, Mathf.Clamp(opts.GetFloat(FloatOptionNames.PlayerSpeedMod) * Options.MadHawkSpeed, 0.5f, 3f));
                           break;
                       // the worship cooldown drives the client's kill button timer (impostor vision kept, like the Jackal).
                       case CustomRole.Worshipper:
                           opts.SetFloat(FloatOptionNames.KillCooldown, Mathf.Max(0.02f, Options.WorshipperCooldown));
                           break;
                       // the Serial Killer's short cooldown drives the client's kill button timer (the client resets its own timer from this value after every kill).
                       case CustomRole.SerialKiller:
                           opts.SetFloat(FloatOptionNames.KillCooldown, Mathf.Max(0.02f, Options.SerialKillerKillCooldown));
                           break;
                       // the slash cooldown drives the client's kill button timer (0 = lobby value, nothing to send).
                       case CustomRole.Samurai:
                           if (Options.SamuraiKillCooldown > 0f) opts.SetFloat(FloatOptionNames.KillCooldown, Mathf.Max(0.02f, Options.SamuraiKillCooldown));
                           break;
                       // the Evil Hawk is an Impostor-basis client, so its vision comes from ImpostorLightMod (TECH-NOTES §Per-client game options; the
                       // Sheriff line above writes the same field — neither line has a live test record yet, §12 gate G3). Kill cooldown stays the base value.
                       case CustomRole.EvilHawk:
                           opts.SetFloat(FloatOptionNames.ImpostorLightMod, opts.GetFloat(FloatOptionNames.ImpostorLightMod) * Options.EvilHawkVision);
                           break;
   ```
   Every line sits inside the switch on purpose: `Rpc.ResetKillCooldown`'s 0.5 s re-send is `BuildFor(id, cooldown)` and must keep the role's vision / speed. Optional for the gating tests only: a temporary `LogInfo` of `opts.GetFloat(FloatOptionNames.ImpostorLightMod)` / `CrewLightMod` after the `SetFloat` (`WireLog` never prints option values).
2. `NeedsCustomOptions` — replace lines 108-113 with
   ```csharp
                   case CustomRole.Arsonist:
                   // v0.5.0: always (the multiplier / cooldown is always in range; even ×1 keeps the Desynced bookkeeping honest for the Mad Hawk)
                   case CustomRole.MadHawk:
                   case CustomRole.Worshipper:
                   case CustomRole.SerialKiller:
                       return true;
                   case CustomRole.Witch:
                       return Options.WitchSpellCooldown > 0f;
                   // v0.5.0: Witch pattern — nothing to send at the neutral value
                   case CustomRole.Samurai:
                       return Options.SamuraiKillCooldown > 0f;
                   case CustomRole.EvilHawk:
                       return Options.EvilHawkVision > 1f;
                   default:
                       return false;
   ```
3. `OptionsDesync_GetKillCooldownPatch` — after line 268 (`case CustomRole.Witch: … break;`):
   ```csharp
                       // v0.5.0
                       case CustomRole.Worshipper: __result = Mathf.Max(0.02f, Options.WorshipperCooldown); break;
                       case CustomRole.SerialKiller: __result = Mathf.Max(0.02f, Options.SerialKillerKillCooldown); break;
                       case CustomRole.Samurai: if (Options.SamuraiKillCooldown > 0f) __result = Mathf.Max(0.02f, Options.SamuraiKillCooldown); break;
   ```
   (Evil Hawk / Evil Nekomata: lobby cooldown, no case.)
4. `OptionsDesync_GetPlayerSpeedModPatch` — replace lines 288-289
   ```csharp
                   if (Core.Game.RoleOf(pc.PlayerId) == CustomRole.SpeedBooster)
                       __result *= Options.SpeedBoosterSpeed;
   ```
   with
   ```csharp
                   var role = Core.Game.RoleOf(pc.PlayerId);
                   if (role == CustomRole.SpeedBooster) __result *= Options.SpeedBoosterSpeed;
                   else if (role == CustomRole.MadHawk) __result = Mathf.Clamp(__result * Options.MadHawkSpeed, 0.5f, 3f);   // v0.5.0, same clamp as BuildFor
   ```
5. `OptionsDesync_CalculateLightRadiusPatch.ApplyHostLightMod` — replace lines 307-311
   ```csharp
               if (role == CustomRole.Lighter)
               {
                   result *= Options.LighterVision;
               }
               else if (role == CustomRole.Sheriff)
   ```
   with
   ```csharp
               if (role == CustomRole.Lighter)
               {
                   result *= Options.LighterVision;
               }
               else if (role == CustomRole.MadHawk)
               {
                   result *= Options.MadHawkVision;   // v0.5.0: host is a local Crewmate → result already uses CrewLightMod
               }
               else if (role == CustomRole.EvilHawk)
               {
                   result *= Options.EvilHawkVision;  // v0.5.0: the host's local role is Impostor, so `result` already carries ImpostorLightMod
               }
               else if (role == CustomRole.Sheriff)
   ```
   Class summary → `Host's own vision: Lighter ×, Mad Hawk × (crew basis), Evil Hawk × (impostor basis), Sheriff gets crew-equivalent vision`. **Never add a `[HarmonyPatch]` for `CalculateLightRadius` on `FungleShipStatus`/`MiraShipStatus`/`PolusShipStatus`/`SkeldShipStatus`**: they inherit the base body already patched; a second patch would square the multiplier (interop metadata: only `AirshipStatus` overrides, and that override is patched at lines 335-350).
6. **Single-player resync** — after the closing `        }` of `public static void ResyncAll()` (line 236), before `        public static void Reset()`:
   ```csharp
           /// <summary>
           /// One player's options after a mid-game role change (v0.5.0 Game.ConvertRole): custom options when the new role needs them, else
           /// the base options back if the player was ever desynced (a Lighter / SpeedBooster convert); no-op for everyone else, the host and
           /// disconnected players. One paced packet (Rpc.Queue), never the whole ResyncAll (which would queue one packet per custom-option
           /// player in front of the name-tag batches and re-send the worshipper's own options a third time inside the ResetKillCooldown sequence).
           /// </summary>
           public static void Resync(byte playerId)
           {
               try
               {
                   if (!Core.Game.IsHostActive) return;
                   var pc = Core.Game.Player(playerId);
                   if (pc == null || pc.AmOwner || pc.Data == null || pc.Data.Disconnected) return;
                   bool needs = NeedsCustomOptions(playerId);
                   if (!needs && !Desynced.Contains(playerId)) return;
                   var opts = BuildFor(playerId);
                   if (opts == null) return;
                   SendTo(pc, opts, false);
                   if (!needs) Desynced.Remove(playerId); // base options restored
               }
               catch (Exception e)
               {
                   PocketRolesPlugin.Logger.LogError($"OptionsDesync.Resync({playerId}): {e}");
               }
           }
   ```
   Existing `ResyncAll` call sites (dispatch, +8 s, intro end, WrapUp, WrapUp +2 s) deliver every new role's options; nothing else changes. Timing facts the roles rely on: private options are paced (`Rpc.Queue`, 0.3 s per packet, behind role tables at dispatch and behind ghost-role packets at WrapUp); a dead player still receives them (harmless); `/opt <role>.vision` mid-game applies at the next `ResyncAll`.

### 1.10 `src/Game/RoleAssignment.cs`
1. `AssignCustomRoles` — inside the loop, after the Assassin gate block (anchor: the `                    continue;\n                }` that ends `if (role.Id == CustomRole.Assassin && !Assassin.Enabled) { … }`) and before `                int count = Options.Count(role.Id);`:
   ```csharp
                   // v0.5.0: Jackal Friends only in games that have a Jackal. Killers (Jackal) are drawn first (order = killers + rest), so the
                   // Jackal slots are already decided here. Forced Friends (/assign) are already in Game.Roles and unaffected.
                   if (role.Id == CustomRole.JackalFriends && !JackalFriends.AnyJackal())
                   {
                       if (Options.Count(role.Id) > 0)
                       {
                           PocketRolesPlugin.Logger.LogInfo("RoleAssignment: Jackal Friends skipped (no Jackal this game)");
                           if (Options.Count(CustomRole.Jackal) <= 0)
                               Chat.Chat.Local(Chat.Chat.Title, Lang.T("assign.jackalfriends.nojackal",
                                   "ジャッカルフレンズはジャッカルが配られた試合だけ配られます（ジャッカルの人数が 0 です: /set jackal 1）。",
                                   "Jackal Friends are only assigned in games that have a Jackal (Jackal count is 0: /set jackal 1).",
                                   "只有本局分配了豺狼时才会分配豺狼之友（豺狼人数为 0：/set jackal 1）。"));
                       }
                       continue;
                   }
   ```
   The killers-first line (288) is **not** extended: the impostor pool holds only killer roles and the Worshipper (crew pool, `ImpostorDesync`) is drawn in the shuffled rest like the Arsonist was before v0.4.1 made it a killer — decision: keep it in `rest` (it does not compete with Sheriff/Jackal/Arsonist for the first crew slots; add it to the line only if a live game shows the Worshipper never being drawn in small lobbies).
2. `LogAssignment` — after line `                  .Append(" & #").Append(Core.Game.LoverB).Append(' ').Append(Core.Game.NameOf(Core.Game.LoverB));` add
   ```csharp
               if (JackalFriends.Any() && !JackalFriends.AnyJackal())
                   sb.Append("\n  Jackal Friends without a Jackal (forced by /assign)");
   ```
3. `Assign_IntroCutsceneOnDestroyPatch` — inside the scheduled action, after `                    OptionsDesync.ResyncAll();` (line 703) and before `                }, "assign.introend");`:
   ```csharp
                       SerialKiller.OnIntroEnd();   // v0.5.0: schedules the countdown start after the LAST client's intro (paced dispatch)
   ```
`View`, `DispatchInitialRoles`, `EnsureImpostorPresent`, `SendGhostRole`, `Assign_RpcSetRolePatch` and `src/Game/TestMode.cs` need no change: crew-pool roles on a vanilla impostor get the `Crewmate` basis, impostor-pool roles on a crewmate the `Impostor` basis (a forced Worshipper on an impostor becomes a desync Crewmate-basis player exactly like a forced Sheriff).

### 1.11 `src/Game/NameTags.cs` `NameFor` (the whole rule block after the VIP mark; replaces lines 55-101)
```csharp
                CustomRole targetRole = Core.Game.RoleOf(targetId);
                CustomRole viewerRole = Core.Game.RoleOf(viewerId);

                // v0.4.1 marks (prefixes; the later colour wrapping keeps them): ♥ on the partner for a lover,
                // † on spelled players for the witch (and on the target itself by option), ♨ on doused players for the arsonist.
                string mark = "";
                if (viewerId != targetId && Core.Game.PartnerOf(viewerId) == targetId) mark += Lovers.Heart;
                if (viewerRole == CustomRole.Witch && Core.Game.Spelled.TryGetValue(targetId, out var witchId) && witchId == viewerId) mark += Witch.Mark;
                if (viewerId == targetId && Options.WitchSpelledSeeMark && Core.Game.Spelled.ContainsKey(targetId)) mark += Witch.Mark;
                if (viewerRole == CustomRole.Arsonist && Core.Game.Doused.TryGetValue(viewerId, out var doused) && doused.Contains(targetId)) mark += Arsonist.Mark;
                // v0.5.0: the worshipper sees Ⓜ on the players it converted (they are literal Madmates; impostors get their own Ⓜ by rule 3)
                if (viewerRole == CustomRole.Worshipper && Core.Game.Worshipped.TryGetValue(viewerId, out var worshipped) && worshipped.Contains(targetId)) mark += Worshipper.Mark;
                baseName = mark + baseName;

                // 1. own role tag
                if (viewerId == targetId)
                {
                    if (targetRole == CustomRole.None) return baseName;
                    RoleInfo info = Roles.Info(targetRole);
                    if (meeting)
                        return baseName + " <size=70%>" + info.ColoredName + "</size>";
                    // v0.5.0: the Serial Killer sees its own countdown after the role name (5 s steps; SerialKiller.Tick refreshes when the step changes)
                    string countdown = targetRole == CustomRole.SerialKiller ? SerialKiller.CountdownTag(targetId) : null;
                    return info.ColoredName + (countdown != null ? " " + countdown : "") + "\r\n" + baseName;
                }

                bool viewerImpKiller = Core.Game.IsImpostorTeamKiller(viewerId);
                bool viewerJackal = viewerRole == CustomRole.Jackal;
                bool targetImpKiller = Core.Game.IsImpostorTeamKiller(targetId);
                bool targetJackal = targetRole == CustomRole.Jackal;

                // 2. Madmate-family viewers (Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, converts) see the impostor-team killers in red.
                //    The Worshipper does NOT (SNR MadMaker rule: worshipping an impostor is its self-destruct risk).
                if (Roles.IsMadType(viewerRole) && viewerRole != CustomRole.Worshipper && targetImpKiller)
                    return "<color=" + Roles.ImpostorColor + ">" + baseName + "</color>";

                // 3. impostors see the Ⓜ marker on a Madmate-family player (per-role KnownToImpostors option); Ⓦ for the Worshipper so the
                //    Assassin can name the exact role (Assassin.Matches is exact)
                if (viewerImpKiller && Roles.IsMadType(targetRole) && MadKnownToImpostors(targetRole))
                    return (targetRole == CustomRole.Worshipper ? Worshipper.ImpostorViewMark : "<color=" + Roles.ImpostorColor + ">Ⓜ</color>") + baseName;

                // 3a. v0.5.0: Jackal Friends sees every Jackal in blue (dead ones too, like the Madmate's red impostors)
                if (viewerRole == CustomRole.JackalFriends && targetJackal)
                    return "<color=" + Roles.JackalColor + ">" + baseName + "</color>";

                // 3b. v0.5.0: the Jackal sees its Friends in blue (option)
                if (viewerJackal && targetRole == CustomRole.JackalFriends && Options.JackalFriendsKnownToJackal)
                    return "<color=" + Roles.JackalColor + ">" + baseName + "</color>";

                // 4. Snitch nearly done → killers see a star on the Snitch
                if (targetRole == CustomRole.Snitch && (viewerImpKiller || viewerJackal)
                    && Core.Game.IsAlive(targetId) && Core.Game.TasksTotal(targetId) > 0
                    && Core.Game.TasksLeft(targetId) <= Options.SnitchTasksLeftToWarn)
                    return "<color=" + Roles.SnitchColor + ">★</color>" + baseName;

                // 5. Snitch with all tasks done sees killers coloured
                if (viewerRole == CustomRole.Snitch && Core.Game.TasksDone(viewerId))
                {
                    if (targetImpKiller) return "<color=" + Roles.ImpostorColor + ">" + baseName + "</color>";
                    if (targetJackal) return "<color=" + Roles.JackalColor + ">" + baseName + "</color>";
                }
```
plus the private helper right before `        // ------------------------------------------------------------------ RefreshAll / RestoreAll` (line 110):
```csharp
        /// <summary>Per-role "known to impostors" switch of the Madmate family: [MadMayor] KnownToImpostors for the Mad Mayor, [Madmate] KnownToImpostors for everyone else (Madmate, Mad Stuntman, Mad Hawk, Worshipper, converts).</summary>
        private static bool MadKnownToImpostors(CustomRole role) =>
            role == CustomRole.MadMayor ? Options.MadMayorKnownToImpostors : Options.MadmateKnownToImpostors;
```
Priority facts: rules 2 and 3 stay mutually exclusive (`IsMadType(viewer)` viewers are never `viewerImpKiller`); a Mad-family viewer looking at another Mad-family player gets nothing (rule 3 needs `viewerImpKiller`); the Jackal Friends rules cannot collide with 2/3 (a Friends is neither Mad nor an impostor-team killer) nor with 4/5 (a Friends is never a Snitch; a Snitch viewer is never a Friends). Refresh points that matter for the batch: no new `RefreshAll` call is needed for role-table rules (fixed at dispatch); `Worshipper.Convert` goes through `Game.ConvertRole` → `RefreshAll()`; `SerialKiller.Tick` calls `RefreshAll()` (unforced) when the countdown step changes — one paced `SetName` for the (sk, sk) pair; a mid-game `/opt madmate.known|madmayor.known|jackalfriends.known` change takes effect at the next refresh (murder / meeting / WrapUp) — Madmate parity, no `TrySet` refresh added.

### 1.12 `src/Chat/Chat.cs` `RoleInfoText` extras (after the Witch block, lines 716-720, before `            return result;` line 721)
```csharp
            // ---- v0.5.0 extras (skipped automatically in the SafeMode meeting branch above)
            // the Stuntman always knows how many kills it can still survive (start + every meeting reminder + /cmd n); with NotifyStuntman off this is its only feedback
            if (role == CustomRole.MadStuntman)
                result += "\n" + Lang.TF("roleinfo.madstuntman.lives", "耐えられるキル: 残り {0} 回", "Kills you can still survive: {0}", Game.MadStuntman.Remaining(playerId));
            // worships left (start + every meeting) and who was converted (dead converts stay listed: still Madmates)
            if (role == CustomRole.Worshipper)
            {
                result += "\n" + Lang.TF("roleinfo.worshipper.uses", "崇拝の残り回数: {0}", "Worships left: {0}", Game.Worshipper.Remaining(playerId));
                string converted = Game.Worshipper.ConvertedNamesFor(playerId);
                if (!string.IsNullOrEmpty(converted)) result += "\n" + Lang.TF("roleinfo.worshipper.converted", "崇拝した相手: {0}", "Worshipped: {0}", converted);
            }
            // Jackal Friends learns its Jackal(s) by name (game start and every meeting reminder; alive ones only)
            if (role == CustomRole.JackalFriends)
            {
                string jackals = Game.JackalFriends.JackalNames();
                result += "\n" + (jackals.Length > 0
                    ? Lang.TF("roleinfo.jackalfriends.jackal", "ジャッカル: {0}", "Jackal: {0}", jackals)
                    : Lang.T("roleinfo.jackalfriends.nojackal", "生きているジャッカルがいません。", "No Jackal is alive.", "没有存活的豺狼。"));
            }
            // the Nekomata's description assumes the default voter rule; say so when the lobby drags anyone
            if (role == CustomRole.EvilNekomata && !meeting && !Options.EvilNekomataVotersOnly)
                result += "\n" + Lang.T("roleinfo.evilnekomata.anyone", "※この部屋の設定では、道連れは生存者全員からランダムに選ばれます。", "Note: in this lobby the drag picks any living player, not only your voters.");
            // the Serial Killer's concrete limit (start and every meeting reminder)
            if (role == CustomRole.SerialKiller)
                result += "\n" + Lang.TF("roleinfo.serialkiller.limit", "制限時間: {0:0.#}秒（キルするたびにリセット。会議中は停止）", "Time limit: {0:0.#} s (restarts with every kill, paused during meetings)", Game.SerialKiller.Limit());
            // the samurai cannot see its slash radius on screen → tell it the numbers once at game start
            if (role == CustomRole.Samurai && !meeting)
                result += "\n" + Lang.TF("roleinfo.samurai", "斬撃の範囲: 半径 {0:0.#}、味方のインポスターも斬る: {1}", "Slash radius {0:0.#}; hits fellow Impostors: {1}",
                    Options.SamuraiRange, Options.SamuraiHitTeammates ? Lang.T("cmd.on", "オン", "on") : Lang.T("cmd.off", "オフ", "off"));
```
(`Game.` here is the `PocketRoles.Game` namespace, as in `Game.Witch.SpelledNamesFor`; `Options` resolves through `using PocketRoles.Core;`; `cmd.on` / `cmd.off` exist.) The `roleinfo.desync` line is emitted automatically for the Worshipper (`ImpostorDesync`). Heads: `マッドメイヤー [インポスター陣営]`, …, `ジャッカルフレンズ [第三陣営]`, `侍 [インポスター陣営]`. Remote `/cmd n` / `/cmd r <role>` message budget: see §0.1-13.

### 1.13 `src/PocketRolesPlugin.cs` `Plugin_TickPatch`
Replace line 333 `                if (Core.Game.IsHostActive) Kills.Tick();` with
```csharp
                if (Core.Game.IsHostActive)
                {
                    SerialKiller.Tick(); // v0.5.0: counts down, queues a time-out death as a bite …
                    Kills.Tick();        // … which executes in this same frame
                }
```
(`using PocketRoles.Game;` is already there for `Kills`.)

### 1.14 Class skeletons (Implementer A creates; owners fill in — stub bodies `return` / `return 0` / `return false` / `return ""` / `return null` until implemented)
Each file: `namespace PocketRoles.Game { using Game = PocketRoles.Core.Game; using HrChat = PocketRoles.Chat.Chat; … }`.
```csharp
public static class MadStuntman  { internal static int Remaining(byte id); internal static void OnGuarded(byte killerId, byte targetId, CustomRole killerRole); }
public static class Worshipper   { public const string Mark = "<color=" + Roles.ImpostorColor + ">Ⓜ</color>";
    public const string ImpostorViewMark = "<color=" + Roles.ImpostorColor + ">Ⓦ</color>";
    internal static int Used(byte worshipperId); internal static int Remaining(byte worshipperId); internal static string ConvertedNamesFor(byte worshipperId);
    internal static void Worship(PlayerControl worshipper, PlayerControl target); internal static void ResetNotices(); }
public static class JackalFriends { internal static bool AnyJackal(); internal static bool Any(); internal static string JackalNames(); }
public static class EvilNekomata { internal static void OnPlayerExiled(byte exiledId, Il2CppStructArray<MeetingHud.VoterState> states, MeetingHud hud);
    internal static void OnMeetingEnd(byte exiledId); }
public static class SerialKiller { internal const string IntroArmTag = "serialkiller.arm"; internal const string BiteReason = "serialkiller";
    internal static void ResetState(); internal static float Limit(); internal static string CountdownTag(byte id); public static void Tick();
    internal static void OnIntroEnd(); internal static void OnKill(byte killerId); internal static void OnExileWrapUp(); internal static void OnMeetingEnd(); }
public static class Samurai      { internal static float KillCooldown(); internal static void Slash(PlayerControl samurai, PlayerControl target); }
```
`Kills.Notice` (internal), `Kills.LobbyKillCooldown()` (internal), `Kills.IsShielded` / `MarkSlash` / `SlashInProgress` (internal, §1.6), `Meetings.VotersFor` / `VoteWeightOf` (internal, §1.7), `Game.ConvertRole` (public, §1.3), `OptionsDesync.Resync` (public, §1.9), `RoleAssignment.SendGhostRole` (public), `TestMode.FindPlayer` (internal) are the cross-file surface the six classes use. No Harmony patch lives in any of the six files (every hook is wired in §1.6–§1.13).

### 1.15 Lang keys (add to `lang/ja.json`, `lang/en.json`, `lang/zh-CN.json` in the sorted position given; **160 new keys → 944 per file**; `␠` = leading space, as in every existing `opt.desc.*`; ja / en / zh-CN)

Blocks are listed in ordinal (`LC_ALL=C`) order; "after X / before Y" names the existing neighbours (same line numbers in all three files today). Inside a block the rows are already sorted.

**A. `assign.jackalfriends.nojackal`** — after `assign.assassin.nocmd`, before `autostart.cancelled`

| key | ja | en | zh |
|---|---|---|---|
| assign.jackalfriends.nojackal | ジャッカルフレンズはジャッカルが配られた試合だけ配られます（ジャッカルの人数が 0 です: /set jackal 1）。 | Jackal Friends are only assigned in games that have a Jackal (Jackal count is 0: /set jackal 1). | 只有本局分配了豺狼时才会分配豺狼之友（豺狼人数为 0：/set jackal 1）。 |

**B. `cmd.ro.*`** — `evilhawk` … `evilnekomata.voters` after `cmd.ro.assassin` / before `cmd.ro.jackal`; `jackalfriends` after `cmd.ro.jackal.vent` / before `cmd.ro.lighter`; `madhawk`, `madhawk.known` after `cmd.ro.lovers` / before `cmd.ro.madmate`; `madmayor`, `madstuntman` after `cmd.ro.madmate` / before `cmd.ro.mayor`; `samurai`, `samurai.samecd`, `serialkiller` after `cmd.ro.mayor` / before `cmd.ro.sheriff`; `worshipper` after `cmd.ro.witch.samecd` / before `cmd.rules.builtin`

| key | ja | en | zh |
|---|---|---|---|
| cmd.ro.evilhawk | 視界 x{0:0.#}（インポスターの視界に掛ける、常時） | Vision x{0:0.#} (times the impostor vision, always on) | 视野 x{0:0.#}（乘以内鬼视野，始终有效） |
| cmd.ro.evilnekomata | 道連れ: {0}、インポスター陣営を除外: {1}、全員に通知: {2} | Drag: {0}, impostor team excluded: {1}, announce: {2} | 道连：{0}，排除内鬼阵营：{1}，通知所有人：{2} |
| cmd.ro.evilnekomata.anyone | 生存者から 1 人 | any living player | 从存活者中选 1 人 |
| cmd.ro.evilnekomata.voters | 投票者から 1 人 | one of your voters | 从投票者中选 1 人 |
| cmd.ro.jackalfriends | ジャッカルに公開: {0}、シェリフに撃たれる: {1} | Known to Jackal: {0}, Sheriff can shoot: {1} | 对豺狼公开：{0}，可被警长击杀：{1} |
| cmd.ro.madhawk | 視界 x{0:0.#}、速度 x{1:0.##} | Vision x{0:0.#}, speed x{1:0.##} | 视野 x{0:0.#}，速度 x{1:0.##} |
| cmd.ro.madhawk.known | 視界 x{0:0.#}、速度 x{1:0.##}、インポスターに公開 | Vision x{0:0.#}, speed x{1:0.##}, known to impostors | 视野 x{0:0.#}，速度 x{1:0.##}，对内鬼公开 |
| cmd.ro.madmayor | 投票は {0}票分、インポスターに公開: {1} | Vote counts as {0}, known to impostors: {1} | 投票计为 {0} 票，对内鬼公开：{1} |
| cmd.ro.madstuntman | キルを {0} 回まで耐える、本人に通知: {1} | Survives {0} kill(s), notify stuntman: {1} | 可承受 {0} 次击杀，通知本人：{1} |
| cmd.ro.samurai | 斬撃CD {0}、範囲 {1:0.#}、間隔 {2:0.#}秒、味方も斬る: {3} | Slash cooldown {0}, range {1:0.#}, stagger {2:0.#}s, hits allies: {3} | 斩击冷却 {0}，范围 {1:0.#}，间隔 {2:0.#} 秒，也斩同伴：{3} |
| cmd.ro.samurai.samecd | キルと同じ | same as kill | 与击杀相同 |
| cmd.ro.serialkiller | キルCD {0:0.#}秒、自殺まで {1:0.#}秒、会議でリセット: {2} | Kill cooldown {0:0.#}s, suicide after {1:0.#}s, reset at meetings: {2} | 击杀冷却 {0:0.#} 秒，{1:0.#} 秒未击杀则自灭，会议后重置：{2} |
| cmd.ro.worshipper | 崇拝 {0} 回、崇拝CD {1:0.#}秒、シェリフが撃てる: {2} | {0} worship(s), cooldown {1:0.#}s, Sheriff can shoot: {2} | 崇拜 {0} 次，冷却 {1:0.#} 秒，警长可射杀：{2} |

**C. `evilnekomata.*`** — after `compat.welcome`, before `gm.notice` (no existing key starts with d/e/f)

| key | ja | en | zh |
|---|---|---|---|
| evilnekomata.drag.all | {0} は追放された {1}（イビル猫又）の道連れになりました。 | {0} was dragged along by the ejected Evil Nekomata {1}. | {0} 被放逐的邪恶猫又 {1} 拖走了。 |
| evilnekomata.dragged | 追放された {0} の道連れになりました。追放画面の後に死亡します。 | You were dragged along by the ejected {0}. You die after the ejection screen. | 你被放逐的 {0} 拖走了，放逐画面结束后你将死亡。 |

**D. `kill.*`** — `kill.slash` after `kill.sheriff.misfire` / before `kill.spell`; the rest after `kill.spell` / before `lang.name.en` (order: `.` sorts before letters, so `kill.worship.none` precedes `kill.worshipped`)

| key | ja | en | zh |
|---|---|---|---|
| kill.slash | {0} を斬りました。続けて倒れる {1} 人: {2} | You slashed {0}; {1} more fall next: {2} | 你斩了 {0}，接着倒下 {1} 人：{2} |
| kill.stuntman.guarded | {0} はキルを耐えました（マッドスタントマン、残り {1} 回）。 | {0} survived your kill (Mad Stuntman, {1} left). | {0} 挡下了你的击杀（疯狂特技演员，还剩 {1} 次）。 |
| kill.stuntman.guarded.anon | {0} はキルを耐えました。 | {0} survived your kill. | {0} 挡下了你的击杀。 |
| kill.stuntman.guarded.last | {0} はキルを耐えました（マッドスタントマン、次は死にます）。 | {0} survived your kill (Mad Stuntman, the next one kills). | {0} 挡下了你的击杀（疯狂特技演员，下一次会死）。 |
| kill.stuntman.survived | キルを耐えました（残り {0} 回）。 | You survived a kill attempt ({0} left). | 你挡下了一次击杀（还剩 {0} 次）。 |
| kill.stuntman.survived.last | キルを耐えました。次は耐えられません。 | You survived a kill attempt. The next one kills you. | 你挡下了一次击杀。下一次将无法挡下。 |
| kill.worship | {0} を崇拝しました。{0} はマッドメイトになりました（残り {1} 回）。 | You worshipped {0}: they are now a Madmate ({1} left). | 你崇拜了 {0}，对方成为了内鬼狂粉（还剩 {1} 次）。 |
| kill.worship.impostor | {0} はインポスターでした。崇拝は失敗し、あなたは自爆しました。 | {0} was an Impostor. The worship failed and you died. | {0} 是内鬼。崇拜失败，你自爆身亡了。 |
| kill.worship.invalid | {0} は崇拝できません。 | {0} cannot be worshipped. | {0} 无法被崇拜。 |
| kill.worship.none | 崇拝の回数を使い切りました（これ以降はキル失敗の演出だけです）。 | You have no worships left (the button only fails from now on). | 崇拜次数已用完（之后只会显示击杀失败）。 |
| kill.worshipped | 崇拝者に崇拝され、あなたはマッドメイトになりました。インポスターの名前が赤く見えます。 | A Worshipper converted you: you are now a Madmate. Impostors appear in red. | 你被崇拜者崇拜，成为了内鬼狂粉。内鬼的名字会显示为红色。 |

**E. `opt.desc.*`** — `evilnekomata.*` after `opt.desc.assassin.first` / before `opt.desc.jackal`; `jackalfriends*` after `opt.desc.jackal.vent` / before `opt.desc.jester`; `madhawk.speed` after `opt.desc.lovers.lastthree` / before `opt.desc.madmate`; `madmayor*`, `madstuntman*` after `opt.desc.madmate` / before `opt.desc.mayor`; `samurai*`, `serialkiller*` after `opt.desc.mayor` / before `opt.desc.sheriff`; `worshipper` after `opt.desc.witch.mark` / before `opt.empty`

| key | ja | en | zh |
|---|---|---|---|
| opt.desc.evilnekomata.anyone | ␠生存者全員から | , any player | ␠从所有存活者中 |
| opt.desc.evilnekomata.impok | ␠インポスターも対象 | , impostors too | ␠内鬼也可能 |
| opt.desc.evilnekomata.silent | ␠非公開 | , silent | ␠不公开 |
| opt.desc.jackalfriends | ␠ジャッカルに公開 | ␠known to Jackal | ␠对豺狼公开 |
| opt.desc.jackalfriends.sheriff | ␠シェリフ不可 | , Sheriff cannot shoot | ␠警长不可击杀 |
| opt.desc.madhawk.speed | ␠速度x{0:0.##} | , speed x{0:0.##} | ␠速度x{0:0.##} |
| opt.desc.madmayor | ␠{0}票 | ␠{0} votes | ␠{0}票 |
| opt.desc.madmayor.known | ␠インポスターに公開 | , known to impostors | ␠对内鬼公开 |
| opt.desc.madstuntman | ␠耐久{0}回 | ␠survives {0} | ␠承受{0}次 |
| opt.desc.madstuntman.notify | ␠本人に通知 | , notifies | ␠通知本人 |
| opt.desc.samurai | ␠斬撃CD{0:0.#}秒 | ␠slash CD {0:0.#}s | ␠斩击CD{0:0.#}秒 |
| opt.desc.samurai.range | ␠範囲{0:0.#} | , range {0:0.#} | ␠范围{0:0.#} |
| opt.desc.samurai.stagger | ␠間隔{0:0.#}秒 | , stagger {0:0.#}s | ␠间隔{0:0.#}秒 |
| opt.desc.samurai.teammates | ␠味方も斬る | , hits allies | ␠也斩同伴 |
| opt.desc.serialkiller | ␠キルCD{0:0.#}秒 制限{1:0.#}秒 | ␠KCD {0:0.#}s, limit {1:0.#}s | ␠击杀CD{0:0.#}秒 时限{1:0.#}秒 |
| opt.desc.serialkiller.noreset | ␠会議で継続 | , no reset at meetings | ␠会议不重置 |
| opt.desc.worshipper | ␠崇拝{0}回 CD{1:0.#}秒 | ␠{0} worship(s), CD {1:0.#}s | ␠崇拜{0}次 CD{1:0.#}秒 |

**F. `opt.name.*`** — `evilhawk.*`, `evilnekomata.*` after `opt.name.credits.show` / before `opt.name.general.ignoreversion`; `jackalfriends.*` after `opt.name.jackal.vent` / before `opt.name.jester.chance`; `madhawk.*` after `opt.name.lovers.lastthree` / before `opt.name.madmate.chance`; `madmayor.*`, `madstuntman.*` after `opt.name.madmate.known` / before `opt.name.mafia.chance`; `samurai.*`, `serialkiller.*` after `opt.name.roles.vanilla` / before `opt.name.sheriff.chance`; `worshipper.*` after `opt.name.witch.mark` / before `opt.nokey`. Every `.chance` = 確率 / Chance / 概率, every `.count` = 人数 / Count / 人数 (18 rows, not repeated below).

| key | ja | en | zh |
|---|---|---|---|
| opt.name.evilhawk.chance, .count | (generic) | | |
| opt.name.evilhawk.vision | 視界倍率 | Vision multiplier | 视野倍率 |
| opt.name.evilnekomata.announce | 道連れを全員に通知 | Announce the drag | 通知所有人 |
| opt.name.evilnekomata.chance, .count | (generic) | | |
| opt.name.evilnekomata.excludeimp | インポスター陣営を除外 | Exclude impostor team | 排除内鬼阵营 |
| opt.name.evilnekomata.voters | 道連れは投票者から | Drag a voter only | 只从投票者中选 |
| opt.name.jackalfriends.chance, .count | (generic) | | |
| opt.name.jackalfriends.known | ジャッカルに公開 | Known to Jackal | 对豺狼公开 |
| opt.name.jackalfriends.sheriff | シェリフに撃たれる | Sheriff can shoot | 可被警长击杀 |
| opt.name.madhawk.chance, .count | (generic) | | |
| opt.name.madhawk.speed | 速度倍率 | Speed multiplier | 速度倍率 |
| opt.name.madhawk.vision | 視界倍率 | Vision multiplier | 视野倍率 |
| opt.name.madmayor.chance, .count | (generic) | | |
| opt.name.madmayor.known | インポスターに公開 | Known to impostors | 对内鬼公开 |
| opt.name.madmayor.votes | 票数 | Votes | 票数 |
| opt.name.madstuntman.chance, .count | (generic) | | |
| opt.name.madstuntman.lives | 耐えられるキル回数 | Kills survived | 可承受击杀次数 |
| opt.name.madstuntman.notify | 本人に通知 | Notify stuntman | 通知本人 |
| opt.name.samurai.chance | 確率 | Chance | 概率 |
| opt.name.samurai.cooldown | 斬撃のクールダウン | Slash cooldown | 斩击冷却 |
| opt.name.samurai.count | 人数 | Count | 人数 |
| opt.name.samurai.range | 斬撃の範囲 | Slash range | 斩击范围 |
| opt.name.samurai.stagger | 倒れる間隔 | Death interval | 倒下间隔 |
| opt.name.samurai.teammates | 味方も斬る | Hits allies | 也斩同伴 |
| opt.name.serialkiller.chance | 確率 | Chance | 概率 |
| opt.name.serialkiller.cooldown | キルクールダウン | Kill cooldown | 击杀冷却 |
| opt.name.serialkiller.count | 人数 | Count | 人数 |
| opt.name.serialkiller.meetingreset | 会議でタイマーをリセット | Timer resets at meetings | 会议后重置计时 |
| opt.name.serialkiller.time | 自殺までの時間 | Time until suicide | 自灭时限 |
| opt.name.worshipper.chance | 確率 | Chance | 概率 |
| opt.name.worshipper.cooldown | 崇拝のクールダウン | Worship cooldown | 崇拜冷却 |
| opt.name.worshipper.count | 人数 | Count | 人数 |
| opt.name.worshipper.uses | 崇拝回数 | Worships | 崇拜次数 |

(`opt.name.*` and `opt.section.*` are read with the 3-arg `Lang.T`: their Chinese exists **only** in `zh-CN.json`. Ordinal sanity: within `samurai.*` the order is chance < cooldown < count < range < stagger < teammates; within `serialkiller.*` chance < cooldown < count < meetingreset < time; within `evilnekomata.*` announce < chance < count < excludeimp < voters.)

**G. `opt.section.*`** — `evilhawk`, `evilnekomata` after `opt.section.cosmetics(hostonly)` / before `opt.section.general`; `jackalfriends` after `opt.section.jackal` / before `opt.section.jester`; `madhawk` after `opt.section.lovers` / before `opt.section.madmate`; `madmayor`, `madstuntman` after `opt.section.madmate` / before `opt.section.mafia`; `samurai`, `serialkiller` after `opt.section.opportunist` / before `opt.section.sheriff`; `worshipper` after `opt.section.witch` / before `opt.tip.arsonist.chance`

| key | ja | en | zh |
|---|---|---|---|
| opt.section.evilhawk | イビルホーク | Evil Hawk | 邪恶鹰眼 |
| opt.section.evilnekomata | イビル猫又 | Evil Nekomata | 邪恶猫又 |
| opt.section.jackalfriends | ジャッカルフレンズ | Jackal Friends | 豺狼之友 |
| opt.section.madhawk | マッドホーク | Mad Hawk | 鹰眼狂粉 |
| opt.section.madmayor | マッドメイヤー | Mad Mayor | 狂粉市长 |
| opt.section.madstuntman | マッドスタントマン | Mad Stuntman | 疯狂特技演员 |
| opt.section.samurai | 侍 | Samurai | 武士 |
| opt.section.serialkiller | シリアルキラー | Serial Killer | 连环杀手 |
| opt.section.worshipper | 崇拝者 | Worshipper | 崇拜者 |

**H. `opt.tip.*`** — same neighbours as F with `opt.tip.` (`evilhawk.*`, `evilnekomata.*` after `opt.tip.credits.show` / before `opt.tip.general.ignoreversion`; `jackalfriends.*` after `opt.tip.jackal.vent`; `madhawk.*` after `opt.tip.lovers.lastthree`; `madmayor.*`, `madstuntman.*` after `opt.tip.madmate.known`; `samurai.*`, `serialkiller.*` after `opt.tip.roles.vanilla`; `worshipper.*` after `opt.tip.witch.mark` / before `opt.unknown`). Every `.chance` = 各枠にこの役職が実際に割り当てられる確率（%）。 / Chance (%) that each slot of this role is actually assigned. / 每个名额实际分配此职业的概率（%）。 and every `.count` = この役職を最大何人まで出すか（0 = 出さない）。 / Maximum number of this role per game (0 = never). / 此职业每局最多出现的人数（0 = 不出现）。 (17 rows, not repeated) **except** `opt.tip.jackalfriends.count`, which extends it (below).

| key | ja | en | zh |
|---|---|---|---|
| opt.tip.evilhawk.vision | イビルホークの視界の倍率（インポスターの視界に掛けます。常時有効）。 | Vision multiplier of the Evil Hawk (applied to the impostor vision, always on). | 邪恶鹰眼的视野倍率（乘以内鬼视野，始终有效）。 |
| opt.tip.evilnekomata.announce | オンなら追放画面の後に「○○ は △△ の道連れになりました」と全員に届きます。オフなら本人にだけ届きます。 | On: after the ejection screen everyone reads who was dragged along. Off: only the victim is told. | 开启后放逐画面结束时所有人都会看到谁被拖走；关闭则只通知本人。 |
| opt.tip.evilnekomata.excludeimp | オンならインポスター陣営（マッド系役職含む）は道連れになりません。 | On: Impostor-team players (Madmate family included) are never dragged. | 开启后内鬼阵营（含狂粉系职业）不会被拖走。 |
| opt.tip.evilnekomata.voters | オンなら自分に投票した人の中から、オフなら生存者全員の中から道連れを選びます。 | On: the victim is one of the players who voted for you. Off: any living player. | 开启：从投票给你的人中选择；关闭：从所有存活玩家中选择。 |
| opt.tip.jackalfriends.count | この役職を最大何人まで出すか（0 = 出さない）。ジャッカルが配られた試合だけ配られます。 | Maximum number of this role per game (0 = never). Only assigned in games that have a Jackal. | 此职业每局最多出现的人数（0 = 不出现）。只有本局分配了豺狼时才会分配。 |
| opt.tip.jackalfriends.known | オンならジャッカルにジャッカルフレンズの名前が青く見えます。 | On: the Jackal sees the Jackal Friends' names in blue. | 开启后豺狼能看到豺狼之友的名字（蓝色）。 |
| opt.tip.jackalfriends.sheriff | オンならシェリフはジャッカルフレンズを撃っても死にません。オフなら誤射扱いでシェリフが死にます。 | On: the Sheriff may shoot Jackal Friends without dying. Off: shooting one is a misfire (the Sheriff dies). | 开启后警长射杀豺狼之友不会死亡；关闭则视为误杀，警长死亡。 |
| opt.tip.madhawk.speed | マッドホークの移動速度の倍率（1 = 通常。広い視界の代償に遅くするなら 1 未満）。 | Movement speed multiplier of the Mad Hawk (1 = normal; below 1 to pay for the wide vision). | 鹰眼狂粉的移动速度倍率（1 = 普通；小于 1 可作为大视野的代价）。 |
| opt.tip.madhawk.vision | マッドホークの視界の倍率（停電中は、狭くなった視界にこの倍率がかかります）。 | Vision multiplier of the Mad Hawk (during a blackout the shrunken vision is multiplied). | 鹰眼狂粉的视野倍率（停电时是缩小后视野的倍数）。 |
| opt.tip.madmayor.known | オンならインポスターに誰がマッドメイヤーか表示されます。 | On: Impostors see who the Mad Mayor is. | 开启后内鬼可以看到谁是狂粉市长。 |
| opt.tip.madmayor.votes | マッドメイヤーの1票を何票として数えるか。 | How many votes the Mad Mayor's single vote counts as. | 狂粉市长的一票算作几票。 |
| opt.tip.madstuntman.lives | この回数まではキルされても死にません（投票による追放は防げません）。 | Kill attempts the Mad Stuntman survives before one goes through (votes are never blocked). | 在此次数内被击杀也不会死（无法阻止投票放逐）。 |
| opt.tip.madstuntman.notify | オンならキルを耐えたことと残り回数を本人にチャットで知らせます（ヴァンパイアの噛みつきや魔女の呪いを耐えた時も知らせます。キルした側にはいつも知らせます）。 | On: the stuntman is told in chat that it survived and how many attempts are left (also for an absorbed Vampire bite or Witch spell; the killer is always told). | 开启后会用聊天告诉本人挡下了击杀以及剩余次数（挡下吸血鬼的咬或女巫的诅咒时也会告知；击杀者始终会被告知）。 |
| opt.tip.samurai.cooldown | 斬撃から次の斬撃までの秒数（0 = キルクールダウンと同じ）。 | Seconds between two slashes (0 = same as the kill cooldown). | 两次斩击之间的秒数（0 = 与击杀冷却相同）。 |
| opt.tip.samurai.range | 侍を中心にした半径。バニラのキル距離はおよそ 短1 / 中1.8 / 長2.5。 | Radius around the Samurai. Vanilla kill distances are roughly short 1 / medium 1.8 / long 2.5. | 以武士为中心的半径。原版击杀距离约为 短1 / 中1.8 / 长2.5。 |
| opt.tip.samurai.stagger | 巻き込まれた人が順に倒れる間隔の秒数（0.3 = 公式サーバーの送信間隔）。 | Seconds between two bystander deaths (0.3 = the official server's packet spacing). | 被波及者依次倒下的间隔秒数（0.3 = 官方服务器的发送间隔）。 |
| opt.tip.samurai.teammates | オンなら範囲内のインポスター陣営（インポスター・マッド系役職など）も死にます。 | On: Impostor-team players in range (Impostors, Madmate family …) die too. | 开启后范围内的内鬼阵营（内鬼、狂粉系职业等）也会死亡。 |
| opt.tip.serialkiller.cooldown | シリアルキラーがキルボタンを再び使えるまでの秒数。 | Seconds before the Serial Killer can kill again. | 连环杀手再次击杀所需的秒数。 |
| opt.tip.serialkiller.meetingreset | オンなら会議が終わるたびにタイマーが最初から始まります。オフなら残り時間を引き継ぎます。 | On: the timer restarts after every meeting. Off: the remaining time carries over. | 开启后每次会议结束计时重新开始；关闭则沿用剩余时间。 |
| opt.tip.serialkiller.time | 前のキルからこの秒数キルしないと自滅します（会議中は止まります。キルCD+5 秒未満には下がりません）。 | Seconds without a kill before the Serial Killer dies by itself (paused during meetings; never below kill cooldown + 5). | 距上次击杀超过此秒数未击杀则自灭（会议中暂停；不会低于击杀冷却+5 秒）。 |
| opt.tip.worshipper.cooldown | 崇拝してから次に崇拝できるまでの秒数（キルボタンのクールダウン）。 | Seconds between two worships (the kill button's cooldown). | 两次崇拜之间的秒数（击杀键冷却）。 |
| opt.tip.worshipper.uses | 1 試合に崇拝できる回数（成功した分だけ数えます）。 | How many players the Worshipper may convert per game (only successes count). | 每局可以崇拜的次数（只计成功的次数）。 |

**I. `role.*`** — `evilhawk.*`, `evilnekomata.*` after `role.bait.name` / before `role.jackal.desc`; `jackalfriends.*` after `role.jackal.name` / before `role.jester.desc`; `madhawk.*` after `role.lovers.name` / before `role.madmate.desc`; `madmayor.*`, `madstuntman.*` after `role.madmate.name` / before `role.mafia.desc`; `samurai.*`, `serialkiller.*` after `role.opportunist.name` / before `role.sheriff.desc`; `worshipper.*` after `role.witch.name` / before `roleinfo.desync`. Every `role.<key>.desc` = the `DescJa / DescEn / DescZh` literal of §1.2 (JSON overrides the inline text — keep them identical; the ≤ 100-char rule of §0.1-13 applies to the JSON value too); every `role.<key>.name` = `NameJa / NameEn / NameZh` (18 rows: マッドメイヤー / Mad Mayor / 狂粉市长, マッドスタントマン / Mad Stuntman / 疯狂特技演员, マッドホーク / Mad Hawk / 鹰眼狂粉, 崇拝者 / Worshipper / 崇拜者, ジャッカルフレンズ / Jackal Friends / 豺狼之友, イビルホーク / Evil Hawk / 邪恶鹰眼, イビル猫又 / Evil Nekomata / 邪恶猫又, シリアルキラー / Serial Killer / 连环杀手, 侍 / Samurai / 武士).

**J. `roleinfo.*`** — `evilnekomata.anyone` after `roleinfo.desync`; `jackalfriends.jackal`, `jackalfriends.nojackal` next, before `roleinfo.lovers.partner`; `madstuntman.lives`, `samurai` after `roleinfo.lovers.partner` / before `roleinfo.sep`; `serialkiller.limit` after `roleinfo.sep` / before `roleinfo.vanilla.crew` (`"sep" < "serialkiller"`: 'p' < 'r'); `worshipper.converted`, `worshipper.uses` after `roleinfo.witch.spelled` / before `sabotage.blocked`

| key | ja | en | zh |
|---|---|---|---|
| roleinfo.evilnekomata.anyone | ※この部屋の設定では、道連れは生存者全員からランダムに選ばれます。 | Note: in this lobby the drag picks any living player, not only your voters. | ※本房间设置下，道连对象从所有存活者中随机选出。 |
| roleinfo.jackalfriends.jackal | ジャッカル: {0} | Jackal: {0} | 豺狼：{0} |
| roleinfo.jackalfriends.nojackal | 生きているジャッカルがいません。 | No Jackal is alive. | 没有存活的豺狼。 |
| roleinfo.madstuntman.lives | 耐えられるキル: 残り {0} 回 | Kills you can still survive: {0} | 还能承受的击杀：{0} 次 |
| roleinfo.samurai | 斬撃の範囲: 半径 {0:0.#}、味方のインポスターも斬る: {1} | Slash radius {0:0.#}; hits fellow Impostors: {1} | 斩击范围：半径 {0:0.#}，也斩内鬼同伴：{1} |
| roleinfo.serialkiller.limit | 制限時間: {0:0.#}秒（キルするたびにリセット。会議中は停止） | Time limit: {0:0.#} s (restarts with every kill, paused during meetings) | 时限：{0:0.#} 秒（每次击杀后重置，会议中暂停） |
| roleinfo.worshipper.converted | 崇拝した相手: {0} | Worshipped: {0} | 已崇拜：{0} |
| roleinfo.worshipper.uses | 崇拝の残り回数: {0} | Worships left: {0} | 剩余崇拜次数：{0} |

**K. `serialkiller.*`** — after `sabotage.blocked`, before `start.cancel`

| key | ja | en | zh |
|---|---|---|---|
| serialkiller.resume | 残り {0:0.#} 秒以内にキルしてください。 | {0:0.#} s left to kill. | 剩余 {0:0.#} 秒内必须击杀。 |
| serialkiller.timeout | 時間切れ…キルできなかったため、あなたは死亡しました。 | Time is up... you did not kill in time and died. | 时间到……你没有及时击杀，因此死亡了。 |
| serialkiller.warn | あと {0:0.#} 秒以内にキルしないと死亡します！ | Kill within {0:0.#} s or you die! | 再过 {0:0.#} 秒不击杀你就会死亡！ |

**L. Changed values of existing keys** (same line in all three files today; the family wording of §0.1-11)

| key | ja | en | zh |
|---|---|---|---|
| cmd.ro.madmate (138) | インポスターはマッド系役職が誰か分かる | Impostors know who the Mad-type players are | 内鬼知道谁是狂粉系职业 |
| cmd.ro.sheriff (140) | キルCD {0:0.#}秒、マッド系は撃てない | Kill cooldown {0:0.#}s, cannot shoot Mad roles | 击杀冷却 {0:0.#} 秒，不能射杀狂粉系职业 |
| cmd.ro.sheriff.madmate (141) | キルCD {0:0.#}秒、マッド系を撃てる | Kill cooldown {0:0.#}s, can shoot Mad roles | 击杀冷却 {0:0.#} 秒，可以射杀狂粉系职业 |
| opt.name.sheriff.killmadmate (377) | マッド系を撃てる | Can kill Mad roles | 可射杀狂粉系 |
| opt.tip.madmate.known (497) | オンならインポスターにマッド系役職（マッドメイト・マッドスタントマン・マッドホーク・崇拝者）が誰か表示されます（マッドメイヤーは別設定）。 | On: Impostors see who the Mad-type players are (Madmate, Mad Stuntman, Mad Hawk; Ⓦ for the Worshipper; the Mad Mayor has its own switch). | 开启后内鬼可以看到谁是狂粉系职业（内鬼狂粉、疯狂特技演员、鹰眼狂粉；崇拜者为 Ⓦ；狂粉市长另有设置）。 |
| opt.tip.sheriff.killmadmate (514) | オンならマッド系役職（マッドメイト・マッドメイヤー・マッドスタントマン・マッドホーク・崇拝者）を撃っても自分は死にません。 | On: shooting a Mad-type role (Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper) does not kill the Sheriff. | 开启后射杀狂粉系职业（内鬼狂粉、狂粉市长、疯狂特技演员、鹰眼狂粉、崇拜者）不会让警长死亡。 |
| role.sheriff.desc (627) | = §1.2-4 DescJa | = DescEn | = DescZh |

Kept as is (declared, not forgotten): `opt.desc.sheriff.madmate` (compact `/show` token), `opt.name.madmate.known` (no role word), `opt.desc.madmate` (leading space: first fragment after `xN`). No `win.*` key (no new win kind), no key for the test-mode count line (log only).

**Gate after editing** (all three files, LF, no BOM, 2-space indent, `"key": "value",`, last entry without comma): `diff <(grep -o '^  "[^"]*"' lang/ja.json) <(grep -o '^  "[^"]*"' lang/en.json)` and the same vs `zh-CN.json` must be empty; `grep -oE '^  "[^"]+"' lang/en.json | LC_ALL=C sort -c` silent; `grep -c '^  "' lang/*.json` = **944** each. Copy the three files into the game's `BepInEx\PocketRoles\lang\` (the release zips overwrite them): `MergeMissingKeys` adds the 160 new keys to an old user file but **never updates the seven changed values** (release-note line, §13).

---
## 2. マッドメイヤー / Mad Mayor / 狂粉市长 (Implementer B — no new file)

SNR: faction インポスター陣営（マッドメイト）, win = インポスター陣営の勝利, passive「市長権限」= higher voting power, one setting「マッドメイヤーの票数」. SNR's common madmate options (task-gated reveal, vent) are not adopted: this mod has one Madmate model and the Mad Mayor reuses it unchanged.

**Decisions** (verbatim from the spec, reconciled against §1):

| Aspect | Decision |
|---|---|
| Identity | `CustomRole.MadMayor`, row in §1.2 (crew pool, `Team.Impostor`, `ImpostorColor`, no kill / vent / sabotage, fake tasks, aliases `mmy madmy mmayor`). Vanilla **Crewmate** for everyone incl. itself: crew intro, real task list (not counted), no buttons. `RoleAssignment.View` needs nothing. |
| Ability | Its counted vote (a player or skip) is tallied with weight `[MadMayor] Votes` (1–5, default 2) by the shared Mayor tally (`Meetings.VoteWeightOf`, §1.7); the duplicated `VoterState` entries make every client draw one vote icon per vote → **at the results everyone sees a "Mayor"**. With `Votes = 1` nothing is duplicated and `AliveMayorVoted` ignores it (a plain Madmate). |
| Own options | `[MadMayor] Votes`, `[MadMayor] KnownToImpostors` (default off → red `Ⓜ` for impostor-team killers, §1.11 `MadKnownToImpostors`). Sees impostor-team killers red always (rule 2). |
| Counting / Sheriff / marker | Through `Roles.IsMadType` (§0.1-1): subtracted from `crewForCount`, wins `WinKind.Impostor` dead or alive (`Team.Impostor`), tasks excluded, `[Sheriff] CanKillMadmate` covers it. |
| Chat | No extra line. `/cmd r madmayor` = head / description / option line = 3 messages in every language (DescEn is 99 chars on purpose — keep `role.madmayor.desc` ≤ 100 in all three files). |
| Default count | 0. |

**Files / hooks**: all in §1 — row (§1.2), options (§1.4 `madmayor.votes|known`), `RoleOptionText` (§1.5), `VoteWeightOf` + `TryEndVotingWithMayor` + `AliveMayorVoted` (§1.7), `IsMadType` consumers (§1.6-3, §1.8-1, §1.11 rules 2/3), lang keys (§1.15). Nothing in `GameState` (no state), `OptionsDesync` (base options), `Chat.RoleInfoText`, `RoleAssignment`, `TestMode`, `AntiBlackout`, `Assassin`. Implementer B only verifies the wiring and runs §12.

**Facts that make the tally correct without more code**: `Meetings_CheckForEndVotingPatch` prefixes every `CheckForEndVoting()` (CastVote, timer, `MeetingTools.EndMeetingNow()`), so `/endmeeting` is weighted too; `AntiBlackout.Prepare(exiledId)` runs inside the replacement tally **before** `RpcVotingComplete` (i.e. before the exile hooks of `Meetings_VotingCompletePatch`, which then skips its own `Prepare`) — inherited Mayor ordering, accepted by the shipped code (a lover partner is still alive for `WouldContinue`; every meeting-end death is a bite after `Restore`), only made more common by a Mad Mayor; `if (ps.AmDead) continue;` keeps a Mad Mayor assassinated mid-meeting at weight 0; skip is a counted (weighted) vote; the Judge overrule branch is untouched; nothing is stored between meetings; **the Evil Nekomata's `VotersFor` collapses the duplicate entries**, so a Mad Mayor's extra icons never raise its drag odds.

**Edge cases** (each is existing behaviour or a §1 edit):

| Case | Behaviour |
|---|---|
| Mayor and Mad Mayor both vote | Each weighted by its own option; the tally sums; icons duplicated for both (`states` = players + Σ(weight−1)). Log `Meetings: Mayor tally → exiled=… votes=N`. |
| `[Mayor] Votes = 1`, `[MadMayor] Votes = 3` | `AliveMayorVoted` no longer early-returns; Mad Mayor 3, Mayor 1. Both = 1 → vanilla `CheckForEndVoting` (no replacement, no log line). |
| Dead before the meeting / assassinated mid-meeting | `ps.AmDead` → skipped; without another weighted voter the vanilla tally decides and **no `Meetings: Mayor tally` line is written**. |
| Assassin guesses it | `madmayor` / `mmy` / `マッドメイヤー` / `マッドメイヤ` / `狂粉市长` / `狂` correct; `マッド` / `マッドメイ` → Madmate, `mad` / `mm` → Madmate, `内鬼` → vanilla Impostor keyword → **wrong, the Assassin dies** (README alias sentence + guess row, §13). |
| Disconnects mid-meeting | Not `IsAlive` → weight 1 if vanilla still holds a vote for it (vanilla normally removes it). Same as today's Mayor. |
| Exile / kill / bite / curse / Sheriff | Nothing to do: `CrewmateGhost` for every viewer, still wins an Impostor win; Sheriff per `CanKillMadmate` (on → dies; off → `kill.sheriff.misfire`). `Kills.OnMurder` → `Check()` re-counts (one fewer subtracted mad). |
| Evil Nekomata exile | A Mad Mayor is impostor-team → never dragged with `ExcludeImpostors` on; with it off it is one candidate (dedupe). |
| Worshipper presses on it | `TeamOf == Impostor` → `Verdict.Invalid` (failed kill, no use). |
| Samurai / Serial Killer | An ordinary living player (spared by the Samurai's default `HitTeammates = false`). |
| Win counting | Impostor 1 + Mad Mayor + 1 crew: `others 2, madmate 1, crewForCount 1, imp 1 ≥ 1` → Impostor win at the next `Check`. |
| Host is the Mad Mayor | Own tag via `ApplyLocal`; the host's own vote runs `CheckForEndVoting` locally → same prefix; the host sees its own extra icons. GM host never has a role. |
| `/assign` | `/assign <who> madmayor` (or `mmy`, `マッドメイヤー`, `狂粉市长`); on a vanilla impostor → basis `Crewmate` (`EnsureImpostorPresent` promotes another player if needed). Count-based outcomes are invisible with `/test on` (§0 test mode); use the §1.8 count line or `/test off`. |
| Option changed mid-game | `madmayor.votes` applies at the next tally (read live); `madmayor.known` at the next `NameTags.RefreshAll` (next meeting / murder / intro end). |

**Risks / open** (carried from the spec): 5 icons per voter (`Votes = 5`) or two weighted voters in one meeting are plausible but unverified on the official server (Mayor live-verified at 2 only) — §12 M3 watches for a kick; the Prepare-before-hooks ordering becomes common — inherited, harmless; every `role.madmayor.desc` must stay ≤ 100 chars or the remote `/cmd r` reply drops its option line again.

---

## 3. マッドスタントマン / Mad Stuntman / 疯狂特技演员 (Implementer B — `src/Game/MadStuntman.cs`)

SNR summary (the wiki page itself was unreachable): 「無条件でキルを指定回数防げる役職。スタントマン自身には通知等はありません。判定はクルーメイトですが、インポスター陣営のロールです。」 → blocks a configured number of kills, wins with the impostors, vanilla Crewmate, SNR does not notify the stuntman. `Lives = 1` and the option names are decisions (SNR's are unknown).

**Decisions** (spec §0.1, reconciled):

1. **Lives = `[MadStuntman] Lives`** (1–10, default 1). 2. **Every button press that would otherwise succeed is absorbed** (vanilla Impostor / Evil Hawk / Evil Nekomata / impostor Lover / Assassin / Vampire bite / Witch spell / Jackal / Sheriff with `CanKillMadmate` on / Mafia only when its gate allows / **Serial Killer / Samurai** — a pressed Stuntman absorbs the whole slash, no bystanders). The Arsonist's douse and the Worshipper's convert are not kills (the guard's `default` → their own case answers). Reason: absorbing at the press keeps `Game.Bites` / `Game.Spelled` untouched. 3. **Not absorbed**: vote exile, the Assassin's `/cmd guess`, disconnects; the Samurai bystander filter and the Nekomata candidate filter skip a shielded Stuntman via `Kills.IsShielded` (no life spent). 4. **Killer feedback**: cooldown restart with the killer's own cooldown (≥ 1 s) + one private line: impostor-team killers learn the role and the lives left (`kill.stuntman.guarded` / `.last`), a Sheriff / Jackal gets the role-neutral `kill.stuntman.guarded.anon` (they can still infer the role — only a Stuntman survives their shot; README §27). 5. **Stuntman feedback**: `[MadStuntman] NotifyStuntman` (default **off**, SNR fidelity; an absorbed bite / spell stays as silent as a real one); the role chat always carries the count (`roleinfo.madstuntman.lives`, §1.12). 6. Reuses `[Madmate] KnownToImpostors` (Ⓜ) and `[Sheriff] CanKillMadmate` through `IsMadType`. 7. No name-tag mark, no per-frame code, no death hooks, no win kind. 8. Row after the Mad Mayor; `マッドス` is the shortest certain prefix. 9. **Guard reset floor `Mathf.Max(1f, cooldown)`** (a 0 s lobby would otherwise cost a life per frame and burst GameDataTo; side effect: the guarded client keeps a 1 s `KillCooldown` option until the next `ResyncAll`). 10. **Presses vanilla itself would refuse are not guarded** for vanilla-path killers (`IsVanillaKillPath`: None / Lovers / Assassin / Evil Hawk / Evil Nekomata on an `inMovingPlat` / `onLadder` / `walkingToVent` target → vanilla answers, no life); custom killers' paths never checked these, so the guard holds for them. 11. **(batch)** A guarded press restarts a Serial Killer's countdown (`OnGuarded` → `SerialKiller.OnKill`).

**Files / hooks**: `Kills.TryStuntmanGuard` + `IsShielded` + `IsVanillaKillPath` (§1.6-2), `Game.StuntGuards` (§1.3), options `madstuntman.lives|notify` (§1.4), `RoleOptionText` (§1.5), `RoleInfoText` line (§1.12), `IsMadType` consumers, lang keys (§1.15). The class:

```csharp
using System;
using PocketRoles.Core;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;

    /// <summary>
    /// マッドスタントマン / Mad Stuntman (v0.5.0): Madmate variant (impostor team, no kill button, vanilla Crewmate on every client)
    /// that survives the first [MadStuntman] Lives kill attempts. The guard itself is Kills.TryStuntmanGuard (HandleCheckMurder, before
    /// the vanilla path): the kill never reaches MurderPlayer, only the killer's cooldown restarts (Rpc.ResetKillCooldown). Votes, the
    /// Assassin's guess and disconnects are never blocked; the Samurai bystander filter and the Evil Nekomata candidate filter skip a
    /// shielded Stuntman (Kills.IsShielded) without spending a life. Counting / name tags / Sheriff rule come from Roles.IsMadType.
    /// </summary>
    public static class MadStuntman
    {
        /// <summary>Kill attempts this Stuntman can still survive (0 for anyone else).</summary>
        internal static int Remaining(byte id)
        {
            if (Game.RoleOf(id) != CustomRole.MadStuntman) return 0;
            Game.StuntGuards.TryGetValue(id, out int used);
            return Math.Max(0, Options.MadStuntmanLives - used);
        }

        /// <summary>One kill attempt was absorbed (called by Kills.TryStuntmanGuard after the cooldown reset): bookkeeping, log, notices.</summary>
        internal static void OnGuarded(byte killerId, byte targetId, CustomRole killerRole)
        {
            try
            {
                Game.StuntGuards.TryGetValue(targetId, out int used);
                Game.StuntGuards[targetId] = used + 1;
                int left = Remaining(targetId);
                string name = Game.NameOf(targetId);
                PocketRolesPlugin.Logger.LogInfo($"Kills: {Game.NameOf(killerId)} ({killerRole}) hit Mad Stuntman {name} → guarded ({left} left)");
                // v0.5.0 batch decision: the Serial Killer did everything right — a guarded press restarts its countdown.
                if (killerRole == CustomRole.SerialKiller) SerialKiller.OnKill(killerId);
                // Impostor-team killers learn the role and the lives left (their own Madmate); a Sheriff / Jackal only learns
                // that the kill failed (a vanilla client shows nothing else on a guarded press).
                if (Game.IsImpostorTeamKiller(killerId))
                {
                    if (left > 0)
                        Kills.Notice(killerId, "kill.stuntman.guarded",
                            "{0} はキルを耐えました（マッドスタントマン、残り {1} 回）。",
                            "{0} survived your kill (Mad Stuntman, {1} left).", name, left);
                    else
                        Kills.Notice(killerId, "kill.stuntman.guarded.last",
                            "{0} はキルを耐えました（マッドスタントマン、次は死にます）。",
                            "{0} survived your kill (Mad Stuntman, the next one kills).", name);
                }
                else
                {
                    Kills.Notice(killerId, "kill.stuntman.guarded.anon", "{0} はキルを耐えました。", "{0} survived your kill.", name);
                }
                if (!Options.MadStuntmanNotify) return;   // default off (SNR: the stuntman is not told); the role chat always carries the count
                if (left > 0)
                    Kills.Notice(targetId, "kill.stuntman.survived", "キルを耐えました（残り {0} 回）。", "You survived a kill attempt ({0} left).", left);
                else
                    Kills.Notice(targetId, "kill.stuntman.survived.last", "キルを耐えました。次は耐えられません。", "You survived a kill attempt. The next one kills you.");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"MadStuntman.OnGuarded: {e}");
            }
        }
    }
}
```
No notice throttle: after the reset the killer's button is on a cooldown of ≥ 1 s, so at most one guard per second per killer. Nothing to add to `Kills.OnMurder`, `Kills.Tick`, `Meetings`, `Assassin`, `AntiBlackout`, `OptionsDesync`, `RoleAssignment`, `TestMode`.

**Behaviour reference**:

| Situation | What happens (host logic) |
|---|---|
| Vanilla impostor / Evil Hawk / Evil Nekomata / Assassin / impostor Lover presses on a Stuntman with lives | `Rpc.ResetKillCooldown(killer, max(1, lobby))`, life −1, log `Kills: A (None) hit Mad Stuntman B → guarded (N left)`, private line to the killer, line to the Stuntman only with `NotifyStuntman`. No MurderPlayer, no body, no `OnMurder`, no win check, no name refresh. |
| Mafia while another impostor lives | `wouldKill == false` → its own case: `FailKill` + throttled `kill.mafia.blocked`, no life. |
| Sheriff, `CanKillMadmate` on / off | On: guarded (Sheriff cooldown), role-neutral line; after the lives are spent the shot kills it (`CanSheriffKill` via `IsMadType`). Off: misfire (Sheriff dies), no life. |
| Jackal | Guarded (Jackal cooldown), neutral line. |
| Vampire / Witch | Bite / spell absorbed at the press (no `Bites` / `Spelled` entry, no †); role line with lives to the killer; the Stuntman is told nothing by default. Once lives are spent the normal bite / spell path runs. |
| Serial Killer | Guarded (SK cooldown), role line; **the SK's countdown restarts** (`OnKill`). |
| Samurai | Guarded (samurai cooldown), role line; **no slash at all** (the guard returns before `Samurai.Slash`). A Stuntman in a slash's range as a bystander is skipped (`IsShielded`) — no life spent. |
| Evil Nekomata drag | With `ExcludeImpostors` off, a shielded Stuntman is never a candidate (`IsShielded`); with lives spent it can be dragged (normal bite). |
| Worshipper | `TeamOf == Impostor` → invalid press, no life. |
| Assassin `/cmd guess` correct (`madstuntman` / `stunt` / `madstunt` / `mst` / `ms` / `マッドスタントマン` / `マッドス` / `疯狂特技演员` / `疯`) | Dies at once — not absorbed by decision. `madmate` / `mad` / `mm` / `マッド` / `内鬼狂粉` on a Stuntman → wrong → the **Assassin** dies; a two-word `mad stuntman` guess parses `stuntman` as the role and `<name> mad` as the player → no player found, no guess consumed (that is why `madstunt` exists). |
| Vote exile / disconnect | Dies normally / nothing. `StuntGuards` is cleared at `Reset`. |
| Target in a vent / GA-shielded / meeting / intro / `Ending` | `IsValidMurder` false (or the `Ending` guard) → normal path (vanilla decides for vanilla-path killers, `FailKill` for custom ones). No life. |
| Target `inMovingPlat` / `onLadder` / `walkingToVent` | Vanilla-path killers: guard steps aside → vanilla refuses as today. Custom killers: guard holds, life spent. |
| Lobby kill cooldown < 1 s | Reset to 1 s: at most one guard per second per killer; the client's `KillCooldown` option stays 1 s until the next `ResyncAll` (meeting end). |
| Host is the killer / the Stuntman | Killer: `SetKillTimer`, the host's own `CheckMurder` body never runs. Stuntman host: vanilla Crewmate (not desync) → host-target block skipped → guard applies to remote killers normally. |
| Two killers in the same frame | Sequential: each consumes one life; at `Remaining == 0` the next one kills. |
| Options changed mid-game | `Remaining` reads `Options.MadStuntmanLives` live (raising adds lives, lowering removes them). |
| Converted by the Worshipper | Impossible (`Judge` → `Invalid`); a stale `StuntGuards` entry would be harmless anyway (`Remaining` returns 0 once `RoleOf != MadStuntman`). |

**Risks / fallbacks** (spec §10, reconciled): a vanilla-Impostor client receiving `MurderPlayer(FailedProtected, target = itself)` for a `None`-role killer — same bytes as the live-tested Vampire / Witch / Arsonist clients, but what the client draws is unverified (cosmetic; fallback: `Rpc.FailKill` right after the reset if a button ever stays locked); vanilla's own kill-range check is bypassed when the prefix returns `false` for a vanilla-path killer (the mod's custom paths never checked distance either); role inference by crew-side killers is documented, not preventable; the five changed lang values need the JSON copy; SNR fidelity of `Lives = 1` / `NotifyStuntman = false` is a decision. **Reconciliation notes**: the spec's `Game.IsMadmate` became `Roles.IsMadType` / `Game.IsMadType`; `ms` stays an alias (no collision with the Samurai's `sm`); the guard's switch gained the four impostor-pool roles; the Samurai's shield gate (`IsShielded`) is implemented as decided in §0.1-4 (no charge for skipped bystanders / candidates).

---

## 4. マッドホーク / Mad Hawk / 鹰眼狂粉 (Implementer B — no new file)

SNR: マッドメイト faction, active「ホークアイ」(temporary wide vision, immobilised while active; options クールタイム / 継続時間). A Madmate-type player is a vanilla Crewmate with no button, so the only vision channel is `CrewLightMod` per client (Lighter pattern) — **the passive form is the only expressible one**.

**Decisions** (spec D1–D9): passive `[MadHawk] VisionMultiplier` (default **3**, 1–5) on the crew vision for the whole game; optional `[MadHawk] SpeedMultiplier` (default 1 = no effect, 0.5–1.5, product clamped to the vanilla-legal 0.5–3 in `BuildFor` and the host patch); everything else = Madmate through `IsMadType` (`Team.Impostor`, no kill / vent / sabotage, fake tasks, red killers, count subtraction, `[Sheriff] CanKillMadmate`, `[Madmate] KnownToImpostors` Ⓜ); no class, no state, no notices; blackout scales the shrunken radius (client-side arithmetic, documented); names / aliases per §1.2 (`hawk` deliberately unclaimed); default count 0; never a lover (one role per player).

**Files / hooks**: row (§1.2), options `madhawk.vision|speed` (§1.4), `RoleOptionText` (`cmd.ro.madhawk[.known]`, §1.5), `OptionsDesync.BuildFor` / `NeedsCustomOptions` (always) / speed patch / `ApplyHostLightMod` (§1.9), `IsMadType` consumers, lang keys. Nothing in `Kills`, `Meetings`, `Chat`, `RoleAssignment`, `TestMode`, `AntiBlackout`, `Assassin`.

**Per-viewer views**:

| Viewer → target | `View` | `NameFor` |
|---|---|---|
| Mad Hawk → itself | `Crewmate` (living) / `CrewmateGhost` | `<color=#ff1919>マッドホーク</color>\r\n<name>` |
| Mad Hawk → impostor-team killer (incl. Vampire / Mafia / Witch / Assassin / Evil Hawk / Nekomata / SK / Samurai / impostor lover) | vanilla impostor type in the table (a Crewmate client draws everyone plain anyway) | red name (rule 2) — the only channel that tells it its team |
| Mad Hawk → desync role / another Mad-family player / crew lover | `Crewmate` | plain |
| Impostor-team killer → Mad Hawk | `Crewmate` | `Ⓜ` when `[Madmate] KnownToImpostors`, else plain |
| Dead Mad Hawk, any viewer | `CrewmateGhost`; never a Guardian Angel | as above |

Chat: role text at +8 s and every meeting reminder (`マッドホーク [インポスター陣営] — …`), `/cmd r madhawk|mh|マッドホ|鹰` adds `cmd.ro.madhawk` (or `.known`). Timing: private options at dispatch, +8 s, intro end +1 s, WrapUp, WrapUp +2 s (paced); `RestoreAll` at lobby.

**Edge cases**: counting = Madmate (`others++`, `madmate++`); Impostor win dead or alive, loses every other kind; `WouldContinue` consistent; a Witch / Vampire / Nekomata (with `ExcludeImpostors` off) / Samurai (with `HitTeammates` on) can kill it as an ordinary living player; Sheriff per `CanKillMadmate`; disconnect → `ResyncAll` skips it; host as Mad Hawk: `ApplyHostLightMod` ×N on every map (Airship through the override patch) + clamped speed; `/assign <who> madhawk|mh|mhawk|マッドホーク|マッドホ|鹰眼狂粉|鹰` (`マッド`/`マッドメ` → Madmate), honoured with test mode off too; Worshipper → `Invalid` (so its vision is never dropped by a conversion — §0.1-6); Assassin guess must name `madhawk` (`madmate` is wrong).

**Risks / gates** (spec §11): (1) **first-ever explicit live verification of a Lighter-style `CrewLightMod ×N` on a vanilla client** — §12 gate G2 (ship / no-ship: if the radius does not widen, the role degrades to a plain Madmate and D1 must be revisited; there is no other vision channel to a crewmate-basis client); (2) `SpeedMultiplier < 1` is the first per-client `PlayerSpeedMod` below the lobby value — §12 gate G2b, fallback = raise the option minimum to 1.0 (bind range, `TrySet` bounds, descriptor, README); the clamp already covers products < 0.5; (3) vision product not clamped (Lighter precedent, up to ×50 with VanillaRanges); (4) blackout semantics differ from SNR (documented); (5) shortened settings-tab label `マッド系を撃てる` is a width guess (§12 A3); (6) changed JSON values need the file copy. **Reconciliation notes**: the spec's `Roles.IsMadType` naming was adopted for the whole family; its `[Madmate] KnownToImpostors` reuse stays (the Mad Mayor alone has its own switch); the spec's "D6 blackout" row and the mandatory worklog entry for the gate are carried into §12.

---

## 5. 崇拝者 / Worshipper / 崇拜者 (Implementer B — `src/Game/Worshipper.cs`)

SNR facts: SNR's own 崇拝者 is a self-sacrifice Madmate; the **converter** Madmate is マッドメーカー (once per game, kill button turns the target into a Madmate; target already an Impostor → the MadMaker self-destructs; no 狂信者 ability; after its single use no button). The user-agreed worklog line (`worklog-2026-09-09.md:48`) is the converter under the name 崇拝者 with no vent / sabotage and a uses option. **Blocking user decision (§0.1-12)**: the self-destruct rule is implemented; if rejected, `Judge` returns `Verdict.Invalid` for impostor-team killers, `SelfDestruct` / `kill.worship.impostor` / every 自爆 sentence (§13 rows) go, and §12 W1-7 / W2-4 disappear.

**Decisions** (spec, reconciled): row in §1.2 (crew pool, `IsKiller + ImpostorDesync`, no vent / sabotage, fake tasks; own client = vanilla Impostor with the kill button = worship, impostor vision kept; everyone else sees a Crewmate; the `roleinfo.desync` line is automatic). `Judge(t)`: impostor-team killer (`IsImpostorTeamKiller`: vanilla types, Vampire, Mafia, Witch, Assassin, Evil Hawk, Evil Nekomata, Serial Killer, Samurai, an impostor-side lover) → **the Worshipper dies** (no use spent); `TeamOf == Impostor` (every Mad-family member, another Worshipper, converts) → **fails**, no use; `IsDesyncImpostor` (Sheriff / Jackal / Arsonist) and a crew-side lover → **fails**, no use; everything else (vanilla crew incl. vanilla crew roles, Mayor, Snitch, Lighter, SpeedBooster, Bait, Jester, Opportunist, Terrorist, **Jackal Friends**) → converted to `CustomRole.Madmate` via `Game.ConvertRole`, permanently, use spent. **No uses left = never lethal** (`Remaining(w) <= 0` checked before `Judge`; `FailKill` + throttled `kill.worship.none`). The Worshipper does **not** see impostors red (rule 2 exclusion); it sees a red `Ⓜ` on the players it converted (`Worshipper.Mark`); impostors see `Ⓦ` on it under `[Madmate] KnownToImpostors` (`ImpostorViewMark`; fallback = a colour-distinct Ⓜ if an emulator shows □). Counting / Sheriff / marker through `IsMadType`; wins with the impostors dead or alive. `[Worshipper] Uses` (1–5, default 1, successes only), `[Worshipper] Cooldown` (2.5–180, default 30 → the client's kill timer through `BuildFor`). No death / exile / disconnect hooks (conversions are permanent; dead converts stay listed). `WinConditions.Check()` right after a conversion — the game may end at once as an Impostor win (numbers) **or** as a Crew task win (the convert held the last unfinished counted tasks: `EvaluateBase` runs the task check first and `Win_RecomputeTaskCountsPatch` drops the convert's tasks) — both mirror a kill of that player and are documented.

**Packet pacing (the reason for the shape of `Convert`)**: the worshipper's client gets the live-tested `Arsonist.Douse` burst (options ×2 urgent + `FailedProtected` + one chat); options / name tags go through the paced queue; the target's chat starts one `ChunkSpacing` later (never two clients' immediate packets in one frame); `OptionsDesync.Resync(t)` sends one packet only for a Lighter / SpeedBooster convert.

**Files / hooks**: row + Sheriff `Desc` (§1.2), `Game.Worshipped` + `ConvertRole` (§1.3), options `worshipper.uses|cooldown` (§1.4), `RoleOptionText` (§1.5), `Kills` switch case + `CanVent` Madmate line + `ResetNotices` (§1.6), `NameTags` mark + rule 3 Ⓦ (§1.11), `RoleInfoText` lines (§1.12), `OptionsDesync.BuildFor` / `NeedsCustomOptions` / `GetKillCooldownPatch` / `Resync` (§1.9), lang keys. The class:

```csharp
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
            CustomRole before = Game.ConvertRole(t, CustomRole.Madmate, "worshipped by " + Game.NameOf(w));
            if (!Game.Worshipped.TryGetValue(w, out var list)) Game.Worshipped[w] = list = new List<byte>();
            if (!list.Contains(t)) list.Add(t);
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
```
`HostDiesByExile && worshipper.AmOwner` is not a constant expression (no CS0162). Wire timeline of a successful worship (client `w`, client `t`): frame 0 → to `w` only: options ×2 (urgent) + `FailedProtected` + chat `kill.worship`; paced queue: `t`'s base options **only if** it was a Lighter / SpeedBooster (`Resync`), then one name-tag batch per client whose view changed; +0.5 s → `w`'s options back; +0.6 s → `t`'s `kill.worshipped`, +1.15 s → the Madmate role text. Host as `w` or `t`: `Chat.Local`, `Resync(host)` no-op.

**Behaviour / edge cases**: own client intro "インポスター" alone, kill button with `[Worshipper] Cooldown`, impostor vision, fake tasks, vent / sabotage buttons visible but refused (boot 0.5 s / dropped, notices every 10 s); role chat = head + desync line + description + `崇拝の残り回数: N` (+ `崇拝した相手: …`). On the convert's screen: "マッドメイト" above its own name, impostor-team killers red, `Ⓜ` for impostors (option), tasks stop counting, a Lighter / SpeedBooster convert loses its options at the paced packet, a converted Snitch / Bait / Terrorist / Jester / Opportunist / Jackal Friends loses its rules / solo / Jackal win, a converted vanilla Engineer keeps its vents. Instant end: (a) Impostor win by numbers, (b) Crew task win when the convert held the last unfinished tasks — accepted, documented; the +0.6 s notice is dropped by `Scheduler.Clear()` at game end. Impostor target (any `IsImpostorTeamKiller`, incl. Evil Hawk / Nekomata / SK / Samurai and an impostor-side lover): self-kill on the spot, `kill.worship.impostor`; the impostor sees a body next to it. Invalid target: failed kill, no reset, throttled `kill.worship.invalid` (info leak accepted). No uses left: failed kill for every target, impostors included. **Host as Worshipper** (§12 W2, blocking): own button → same switch; `SetKillTimer`; local Impostor role → self-destruct = `Rpc.Kill(lp, lp)` with a local-Impostor host as target — unverified; fallback `HostDiesByExile = true`; being shot by a Sheriff / killed by an impostor as a host Worshipper is the same primitive from the other side (host-target block). Host as target: plain-crew host → converted like anyone (`Chat.Local`, `ApplyLocal`); host Sheriff / Jackal / Arsonist → invalid; GM host dead → `IsValidMurder` rejects. Worshipper dies / exiled / disconnects: converts stay Madmates. Meetings / AntiBlackout: worship only outside meetings; a conversion changes no `View`. Target with a pending bite / curse: allowed (dies as a Madmate). Two Worshippers: separate lists; each other → invalid. Assassin: `Ⓦ` on the Worshipper, `Ⓜ` on Madmates / converts; `/cmd guess X worshipper|ws` correct, `madmate` on a Worshipper wrong (Assassin dies). Assignment: crew pool, shuffled rest, count 0. Test mode: `/assign X worshipper|ws|崇拝|崇拜`; on a vanilla impostor → `Crewmate` basis; `EnsureImpostorPresent` picks an unforced player; conversions / self-destructs happen, `EndGame` absent (the crew task win is not observable with `/test on`).

**Risks / open**: official-server burst kick if any future edit adds a second immediate client-addressed send in `Convert` / `SelfDestruct`; host self-kill unverified (fallback written but itself untested outside a meeting); Ⓦ font coverage inferred from Ⓜ (fallback documented); instant crew task win may surprise players (documented); info leak on failed worships (design); English `/cmd r worshipper` drops the option line for remote players (2-message en description); `role.sheriff.desc` changed value needs the JSON copy; naming (the SNR converter is マッドメーカー) — kept 崇拝者 as agreed. **Reconciliation notes**: the spec's `Game.IsMadmateType` became `IsMadType` (five members — `Judge`'s `TeamOf == Impostor` line already covered them); its private `Convert` bookkeeping moved into `Game.ConvertRole` (§1.3) so the first mid-game writer of `Game.Roles` is a shared helper; the Sheriff description now also names the Jackal Friends option; the Mad Hawk's / Evil Hawk's converter guards are satisfied by `Judge` lines 1–2.

---
## 6. ジャッカルフレンズ / Jackal Friends / 豺狼之友 (Implementer D — `src/Game/JackalFriends.cs`)

SNR: 「第三陣営(ジャッカル)」「非キル人外カウントのジャッカル陣営役職」「マッドメイトのジャッカル版」「クルーメイト役職としてアサインされる」「タスクはクルーメイト陣営の勝利にカウントされない」「ジャッカルからはわからない」, win = ジャッカル陣営の勝利.

**Decisions** (spec D1–D14, reconciled): vanilla Crewmate basis (no kill / vent / sabotage); `Team.Neutral`, `Color = JackalColor`, `TasksCount = false`; counting = Madmate parity (subtracted from `crewForCount` for both thresholds via the `friends` counter, never added to `jackal`); wins with the Jackal dead or alive (`ComputeWinners` Jackal case); no Jackal alive → can only lose (no promotion / death / conversion); always sees every Jackal in blue (rule 3a), Friends do not see each other, the Jackal sees Friends only with `[JackalFriends] KnownToJackal` (default off, rule 3b); the Jackal may kill its Friends (no block); `[JackalFriends] SheriffCanKill` (default on; off → misfire with the unchanged `kill.sheriff.misfire` text); **assigned only when a Jackal was assigned** (gate inside `AssignCustomRoles`, §1.10; forced `/assign` Friends are honoured with a log line); no new `GameState` state; no notice when the last Jackal dies (the reminder / `/cmd n` shows the alive Jackals or "no Jackal alive"); `DescEn` 99 chars (en `/cmd n` / `/cmd r jf` = exactly 3 messages); row after the Jackal (`ジャ`…`ジャッカル` = Jackal, `ジャッカルフ` = Friends; `豺` = Jackal, `豺狼之` = Friends). **Batch additions**: convertible by the Worshipper (§0.1-6); a Friends in a Samurai's range is a plain crew bystander; a Friends is a Nekomata candidate like any crew; SNR's "learns the Jackal only after finishing tasks" and "Friends see each other" stay deferred.

**Files / hooks**: row (§1.2), options `jackalfriends.known|sheriff` (§1.4), `RoleOptionText` (§1.5), `CanSheriffKill` line (§1.6-3), `EvaluateBase` `friends` + `ComputeWinners` (§1.8), assignment gate + `LogAssignment` (§1.10), rules 3a/3b (§1.11), `RoleInfoText` lines (§1.12), lang keys (17). Nothing in `GameState`, `Meetings`, `AntiBlackout`, `OptionsDesync`, `TestMode`, `Kills.OnMurder`. The class:

```csharp
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
```

**Per-viewer views**: Friends → itself `Crewmate` / `CrewmateGhost`, own tag `<color=#00b4eb>ジャッカルフレンズ</color>\r\nName`; Friends → Jackal (alive or dead) blue name; Friends → anyone else unchanged; Jackal → Friends blue only with `KnownToJackal`; everyone else → plain. Intro = normal crew intro; tasks shown and operable, never counted on the host (every vanilla client's own bar still moves — pre-existing Madmate / Jester behaviour, README §26).

**Worked numbers** (the subtraction proof, §12 J6): 3 alive = Jackal + Friends + vanilla Impostor → `imp 1, jackal 1, crewForCount 0` → Continue; Jackal kills the Impostor → Jackal win; Impostor kills the Jackal → Impostor win. 4 alive = Jackal + Friends + Impostor + plain crew: Jackal kills the Impostor → `others 2, friends 1, crewForCount 1, jackal 1 ≥ 1` → **Jackal wins at once** (a plain-crew B would give `crewForCount 2` → Continue).

**Edge cases**: meetings — vanilla voter, weight 1; exile of the Jackal → `WouldContinue` / `CheckNow` use the new counting (Crew win when `imp == 0`, Impostor win when `imp ≥ crewForCount`, else continue with a Friends that can only lose; next reminder `生きているジャッカルがいません。`); exile of a Friends — ordinary; AntiBlackout — normal Crewmate view; disconnect of the Jackal — nothing follows; Jackal kills a Friends — allowed; Sheriff per option; Vampire / Witch / Arsonist / impostor on a Friends — ordinary crew target (an Arsonist must douse it); Friends killed — ordinary crew death, no forced report (never a Bait); Arsonist / Lovers precedence unchanged; two Jackals — both blue; several Friends — independent, do not see each other; no Jackal drawn (chance) → skipped, log `RoleAssignment: Jackal Friends skipped (no Jackal this game)`; `Jackal.Count = 0` → same + host-local `assign.jackalfriends.nojackal`; forced Friends without a Jackal → kept, log line; host as Friends / Jackal → local tag / colour; GM host never drawn; **test mode never runs the counting** (`/end` = Crew win, Friends without ☆; a Jackal-win summary cannot be produced with `/test on` — use the §1.8 count line or `/test off` + `/start`); compat / haison — nothing; Lovers — never; Assassin — `/cmd guess X jackalfriends|jf` correct, `crew` wrong (Assassin dies); Evil Nekomata — a Friends is a normal candidate (voter rule); Samurai — plain bystander; Worshipper — converted to Madmate (loses the Jackal win).

**Risks / open**: the en chat cap is at the exact limit (3 of 3 messages) — any longer `DescEn` / `cmd.ro.jackalfriends` drops a line; the counting change is provable only with `/test off` or the count line; `EnsureImpostorPresent` promotion is random among unforced players in 4-device runs (read the assignment log); the Friends' tasks move every vanilla client's bar (documented); `kill.sheriff.misfire` says "crewmate" (acceptable); mid-game `jackalfriends.known` applies at the next refresh. Deferred: task-gated Jackal reveal, Friends seeing each other, a "last Jackal died" notice, listing the Friends flag in `/cmd r sheriff`. **Reconciliation notes**: the spec's optional §7.3 count line is now the batch diagnostic (§1.8); the Sheriff `Desc*` / `role.sheriff.desc` **are** changed (the Worshipper spec changed them anyway, so the Jackal Friends option is named there too — the spec's D7 "unchanged" is superseded); the optional `IsAdminOptKey` prefixes are included in §1.5.

---

## 7. イビル猫又 / Evil Nekomata / 邪恶猫又 (Implementer C — `src/Game/EvilNekomata.cs`)

SNR (adapted, user's brief): an Impostor; when it is voted out, one random player who voted for it dies too (道連れ); kills / vents / sabotages normally. The current SNR wiki drags "one random living player" with an "exclude the Impostor team" option — both variants are kept (voter rule default, SNR-wiki rule one option away).

**Rule set** (spec §0, reconciled):

| Rule | Decision |
|---|---|
| Trigger | **Exile only** (`VotingComplete` with `exiled == the Nekomata`). A killed Nekomata drags nobody. |
| Exile that ends the game | Decided at `VotingComplete`: `Game.SoloWinner != None ‖ !WinConditions.WouldContinue(exiledId)` → no victim, no bite, no notice (one log line). Test mode → always continues. |
| Victim pool (default) | Players whose counted vote was the Nekomata (`Meetings.VotersFor`, §1.7 — one entry per player, Mayor / Mad Mayor duplicates collapsed), alive at `VotingComplete`, not the Nekomata, not the GM host, not already dying (`Bites.ContainsKey`), **not shielded** (`Kills.IsShielded`, batch), and — `[EvilNekomata] ExcludeImpostors` (default on) — not impostor-team (`TeamOf == Impostor` ‖ `IsImpostorTeamKiller`: real impostors, the four impostor-pool roles, the whole Madmate family, an impostor lover). |
| Pool (option off) | `[EvilNekomata] VotersOnly = false` → every living player with the same exclusions. |
| How many | Exactly one (no `DragCount`; a second victim would need its own stagger slot). |
| No eligible voter (tie / skip / Judge overrule / all voters impostor-team or dead) | Nobody dies; log line. |
| When | `Game.Bites` entry (`Killer = exiledId`, `Reason = "nekomata"`) registered at `VotingComplete`; the WrapUp postfix flattens it to +2 s; `EvilNekomata.OnMeetingEnd` (after `Witch.OnMeetingEnd`) **re-arms it to +2.5 s** — 0.5 s behind curses / lover follows / a postponed vampire bite: one broadcast `MurderPlayer` per frame. `Kills.Tick` executes `Rpc.Kill(victim, victim)`. |
| Who is told | Victim: private line at `VotingComplete` (results screen; the host is still alive on the wire, so a host Nekomata sends it like the lover notice). Everyone: one public line **2 s after WrapUp** (after `AntiBlackout.Restore` +1.5 s, 0.5 s before the drag), `[EvilNekomata] Announce` (default on). |
| Kill / vent / sabotage / view / counting | Vanilla impostor kill (`default: return true`; host-target whitelist via `VanillaKillsHost`), `CanVent = CanSabotage = true`, lobby cooldown, real vanilla Impostor (`FromImpostorPool`), listed in `IsImpostorTeamKiller` (→ `imp`, Sheriff-shootable, red for Madmates, Mafia gate, `ImpostorGhost`), `Team.Impostor` win. |

**Files / hooks**: row (§1.2), `Game.NekomataDragged` + `IsImpostorTeamKiller` case (§1.3), options `evilnekomata.voters|excludeimp|announce` (§1.4), `RoleOptionText` (§1.5), `VanillaKillsHost` (§1.6-1), `Meetings_VotingCompletePatch` `states` binding + hook and `Meetings_ExileWrapUpPatch` `OnMeetingEnd` (§1.7), `RoleInfoText` note (§1.12), lang keys (22). Nothing in `OptionsDesync`, `NameTags`, `WinConditions` (`WouldContinue` reused), `RoleAssignment` / `TestMode` (`FromImpostorPool` basis), `AntiBlackout`, `Kills.Tick/FlushBites/ExecuteBite` (generic bite). The class:

```csharp
using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;
    using HrChat = PocketRoles.Chat.Chat;

    /// <summary>
    /// イビル猫又 / Evil Nekomata (v0.5.0): vanilla Impostor (kills, vents, sabotages normally). When it is voted out and the
    /// exile does not end the game, one random player who voted for it ([EvilNekomata] VotersOnly; off = any living player)
    /// dies with it: a Game.Bites entry registered at MeetingHud.VotingComplete (reason "nekomata", credited to the Nekomata)
    /// that OnMeetingEnd re-arms to 2.5 s after the exile screen — 0.5 s behind the curse / lover-follow bites the WrapUp
    /// postfix put at +2 s, so one broadcast MurderPlayer per frame — executed by Kills.Tick as Rpc.Kill(victim, victim).
    /// The public line goes out 2 s after WrapUp (after AntiBlackout.Restore at +1.5 s). Impostor-team players are never
    /// dragged with [EvilNekomata] ExcludeImpostors; a shielded Mad Stuntman is never a candidate (Kills.IsShielded).
    /// Hooks: Meetings_VotingCompletePatch / Meetings_ExileWrapUpPatch; the voter list comes from Meetings.VotersFor.
    /// </summary>
    public static class EvilNekomata
    {
        /// <summary>Seconds after ExileController.WrapUp until the drag executes: 0.5 s behind the +2 s bites of the same meeting (curses, lover follow, postponed vampire bite).</summary>
        private const float DragDelay = 2.5f;
        /// <summary>Seconds after WrapUp until the public line: after AntiBlackout.Restore (+1.5 s), 0.5 s before the drag.</summary>
        private const float AnnounceDelay = 2f;
        private static readonly System.Random Rand = new System.Random();

        /// <summary>
        /// MeetingHud.VotingComplete postfix (exiled != null): when the exile does not end the game, picks the victim among
        /// the voters (or everyone) and queues its death. <paramref name="states"/> is the tallied vote array (duplicates
        /// per extra Mayor / Mad Mayor vote); <paramref name="hud"/> only serves the fallback of Meetings.VotersFor.
        /// </summary>
        internal static void OnPlayerExiled(byte exiledId, Il2CppStructArray<MeetingHud.VoterState> states, MeetingHud hud)
        {
            try
            {
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                if (Game.RoleOf(exiledId) != CustomRole.EvilNekomata) return;
                // Not Game.IsAlive(exiledId): in the Mayor / Mad Mayor path AntiBlackout.Prepare already ran with RealIsDead[exiledId] = true.
                var exInfo = Game.Info(exiledId);
                if (exInfo == null || exInfo.Disconnected) return;
                // The exile itself ends the game at WrapUp (last impostor → crew win, lovers rule, Arsonist, or a solo win
                // pending from a mid-meeting assassination): no victim, no bite, no notice. Same evaluation AntiBlackout.Prepare
                // runs (before this hook in the Mayor path, right after it in the vanilla path); test mode → always continues.
                if (Game.SoloWinner != CustomRole.None || !WinConditions.WouldContinue(exiledId))
                {
                    PocketRolesPlugin.Logger.LogInfo($"EvilNekomata: {Game.NameOf(exiledId)} exiled, the exile ends the game → no drag");
                    return;
                }

                var candidates = new List<byte>();
                int voters = 0;
                if (Options.EvilNekomataVotersOnly)
                {
                    foreach (var v in Meetings.VotersFor(exiledId, states, hud))
                    {
                        voters++;
                        if (IsCandidate(v, exiledId)) candidates.Add(v);
                    }
                }
                else
                {
                    foreach (var id in Game.AllPlayerIds()) if (IsCandidate(id, exiledId)) candidates.Add(id);
                }
                if (candidates.Count == 0)
                {
                    PocketRolesPlugin.Logger.LogInfo($"EvilNekomata: {Game.NameOf(exiledId)} exiled, nobody to drag (voters={voters}, mode={(Options.EvilNekomataVotersOnly ? "voters" : "anyone")})");
                    return;
                }
                byte victim = candidates[Rand.Next(candidates.Count)];
                // DueAt is a placeholder: Kills.Tick never runs during the results / exile screen, Meetings_ExileWrapUpPatch
                // flattens it to +2 s and OnMeetingEnd re-arms it to +2.5 s.
                Game.Bites[victim] = new Game.VampireBite { Killer = exiledId, DueAt = Time.time + DragDelay, Reason = "nekomata" };
                Game.NekomataDragged.Add(victim);
                PocketRolesPlugin.Logger.LogInfo($"EvilNekomata: {Game.NameOf(exiledId)} exiled → drags {Game.NameOf(victim)} (picked from {candidates.Count} candidate(s), {voters} voter(s)); dies {DragDelay:0.#} s after the exile screen");
                // The host is still alive on the wire here (vanilla exiles at WrapUp): a host Nekomata sends this like the lover notice.
                Kills.Notice(victim, "evilnekomata.dragged",
                    "追放された {0} の道連れになりました。追放画面の後に死亡します。",
                    "You were dragged along by the ejected {0}. You die after the ejection screen.", Game.NameOf(exiledId));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"EvilNekomata.OnPlayerExiled({exiledId}): {e}");
            }
        }

        /// <summary>
        /// ExileController.WrapUp (after the end checks, the bite postponement and Witch.OnMeetingEnd): re-arms the drag
        /// bite to +2.5 s (the WrapUp postfix put every bite at +2 s: the drag must not share a frame with a curse or a
        /// lover follow) and schedules the public line for +2 s.
        /// </summary>
        internal static void OnMeetingEnd(byte exiledId)
        {
            try
            {
                if (Game.NekomataDragged.Count == 0) return;
                var victims = new List<byte>(Game.NekomataDragged);
                Game.NekomataDragged.Clear();
                if (!Game.InProgress || Game.Ending) return;
                foreach (var v in victims)
                {
                    // Disconnected meanwhile, or the bite was replaced: nothing to re-arm or announce (Kills drops such bites silently).
                    if (!Game.IsAlive(v) || !Game.Bites.TryGetValue(v, out var bite) || bite.Reason != "nekomata") continue;
                    bite.DueAt = Time.time + DragDelay;
                    Game.Bites[v] = bite;
                    PocketRolesPlugin.Logger.LogInfo($"EvilNekomata: drag on {Game.NameOf(v)} (by {Game.NameOf(exiledId)}) executes {DragDelay:0.#} s after the exile screen");
                    if (!Options.EvilNekomataAnnounce) continue;
                    byte victim = v, neko = exiledId;
                    // Deferred past AntiBlackout.Restore (+1.5 s): chat from a just-exiled host goes through
                    // Rpc.TempReviveHostForChat (an urgent Data(IsDead=false) for the host), which must not land inside the
                    // WrapUp window the bite postponement protects (clients run their own WrapUp end check up to a ping later).
                    Scheduler.After(AnnounceDelay, () => Announce(victim, neko));
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"EvilNekomata.OnMeetingEnd({exiledId}): {e}");
            }
        }

        /// <summary>+2 s after WrapUp: tell everyone who is dragged along (the drag executes 0.5 s later).</summary>
        private static void Announce(byte victim, byte neko)
        {
            try
            {
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                var info = Game.Info(victim);
                if (info == null || info.Disconnected) return;   // left meanwhile: Kills drops the bite silently, nothing to announce
                bool pending = Game.Bites.TryGetValue(victim, out var bite) && bite.Reason == "nekomata";
                if (!pending && Game.IsAlive(victim)) return;    // the bite was replaced by another death path: nothing to announce
                // pending, or already executed by FlushBites (a body reported in the meantime): announce
                HrChat.All(HrChat.Title, () => Lang.TF("evilnekomata.drag.all",
                    "{0} は追放された {1}（イビル猫又）の道連れになりました。",
                    "{0} was dragged along by the ejected Evil Nekomata {1}.", Game.NameOf(victim), Game.NameOf(neko)));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"EvilNekomata.Announce({victim}): {e}");
            }
        }

        /// <summary>Alive, not the Nekomata, not the GM host, not already dying, not shielded, and (option) not impostor-team.</summary>
        private static bool IsCandidate(byte id, byte exiledId)
        {
            if (id == exiledId || !Game.IsAlive(id)) return false;
            if (Game.GameMasterActive && Game.IsHost(id)) return false;   // already dead in practice (GameMaster.Apply); kept explicit like Lovers.IsCandidate
            if (Game.Bites.ContainsKey(id)) return false;   // postponed vampire bite, lover follow, slash: it dies anyway, pick someone else
            if (Kills.IsShielded(id)) return false;         // v0.5.0 batch: a Mad Stuntman with lives left is never dragged (no life spent)
            if (Options.EvilNekomataExcludeImpostors && (Game.TeamOf(id) == Team.Impostor || Game.IsImpostorTeamKiller(id))) return false;   // impostors, Madmate family, impostor lover
            return true;
        }
    }
}
```
Facts used: `Il2CppStructArray<T>` has `Length` and an indexer; `Kills.Notice` internal; `HrChat.All(title, Func<string>)` per recipient; `WinConditions.WouldContinue` public (test mode → `true`); `Game.SoloWinner / Info / GameMasterActive` public; `Scheduler` in `PocketRoles.Core`. `Rpc.SendChatTo` with a dead host opens `TempReviveHostForChat(…, 1f, clientId)`: at `VotingComplete` a host Nekomata is still `IsDead == false` (no revive); at +2 s the host is dead → the public line opens a 1 s revive window (existing dead-host chat path, now outside the WrapUp / Restore window).

**Sequence at an exile of the Nekomata**: `VotingComplete` → `SoloWinner == None && WouldContinue(exiledId)`? (no → log, nothing else) → bite queued + private notice → `WrapUp`: `Restore` @+1.5 s, every bite → +2 s, `SerialKiller.OnExileWrapUp`, `CheckNow()` (a disconnect during the results screen may still end the game here — the bite is never executed) → `Witch.OnMeetingEnd` (curses +2 s) → `EvilNekomata.OnMeetingEnd` (drag → +2.5 s, announce @+2 s) → +2 s: `Announce`, curses / lover follows / postponed vampire bite execute, the WrapUp +2 s `ResyncAll + SerialKiller.OnMeetingEnd + RefreshAll + Check()` → +2.5 s: `Kills.Tick` → `Rpc.Kill(victim, victim)` → `OnMurder` (hooks: a lover victim's partner follows +0.5 s, a Witch victim's spells clear, Bait skipped because the Nekomata is dead) → `Check()` +0.1 s.

**Edge cases**: tie / skip → nothing; Judge overrule → only tallied voters for the overruled count (often none → `nobody to drag (voters=0 …)`); Mayor / Mad Mayor duplicates → one candidate; dead voters → `DeadVote` never equals a player id and `IsAlive` filters; self-vote excluded; all voters impostor-team / only the GM host → no drag; victim already dying → not a candidate; exile ends the game → skipped at `VotingComplete` (a disconnect during the results / exile screen can flip the evaluation: continue → end leaves a sent "you die" notice with no death, same as the lover follow; end → continue = no drag, conservative); an exiled Nekomata is never a Jester (one role); victim = host → `Rpc.Kill(host, host)` (live-verified as vampire bite on the host); **host is the Nekomata and is exiled** → private notice sent while the host is still alive on the wire; the public line at +2 s goes through `TempReviveHostForChat` after `Restore` and after the host's paced `ImpostorGhost` SetRole — clients may show the exiled host "alive" for ~1–1.5 s around +2 s (cosmetic, new timing, §12 N11 watches both phones); victim disconnects before +2.5 s → bite dropped, nothing announced; victim reports a body in the 0.5 s window → `FlushBites` executes the drag at the report (the reporter's own bite is parked — existing rule; `Announce` still fires); Witch curse + drag in one meeting → curses at +2 s, drag at +2.5 s; victim spelled → the drag was queued first, `Witch.OnMeetingEnd` skips it; victim is a Witch with spells → its curses strike at +2 s, it dies 0.5 s later; victim is a Lover → partner follows +3.0 s; victim is Bait → no forced report; victim is a Terrorist with tasks done → Terrorist win at +2.5 s; two Nekomata → only the exiled one drags; Nekomata killed / assassinated / disconnected → no drag; a Serial Killer as victim → its countdown entry is dropped at death; vanilla clients see only chat (paced), the existing self-kill `MurderPlayer` at +2.5 s and its ghost `SetRole`.

**Test mode / `/assign`**: `/assign <who> evilnekomata|neko|nekomata|イビル猫又|イビル猫|邪恶猫又|邪恶猫` → basis `Impostor` for a crewmate (`TestMode: forced #n name -> Evil Nekomata (vanilla Crewmate -> Impostor)`). `TestMode_AdjustedNumImpostorsPatch` keeps ≥ 1 vanilla impostor, so with 3 players force **all three** roles so the vanilla slot collapses onto the Nekomata (a crew-pool role on a vanilla impostor → `Crewmate`). `/test off` clears `ForcedRoles` — switch it off first, then `/assign`, then `/start`. `WouldContinue` returns `true` in test mode, so a 3-player exile of the only impostor still executes the drag (good for testing).

**Risks / open**: `TempReviveHostForChat` at +2 s for an exiled host Nekomata is a new timing (cosmetic flicker expected, §12 N11); two `MurderPlayer` broadcasts 0.5 s apart (curse + drag) are new on the official server (§12 N12); the `states` binding is the only new patch surface (`PatchAll failed` gate G0: attribute form → by-name `states` → `hud.playerStates` fallback inside `VotersFor`); `WouldContinue` at `VotingComplete` vs `CheckNow` at WrapUp can disagree after a disconnect; the `hud.playerStates` fallback depends on `VotedForId` surviving `ClearForResults()` (unverifiable; fallback only); whether to announce at all when the exiled Nekomata is the host — kept (option-controlled) pending N11; the README meeting bullet distinguishes 2 s (curse / lover) from 2.5 s (drag). **Reconciliation notes**: the spec's private `Voters()` became `Meetings.VotersFor` (the batch's generic reader); `IsCandidate` gained the `IsShielded` skip; `ExcludeImpostors` covers the whole Madmate family through `TeamOf == Impostor` (unchanged code, wider effect); the option tips / bind texts say "Madmate family".

---
## 8. シリアルキラー / Serial Killer / 连环杀手 (Implementer C — `src/Game/SerialKiller.cs`)

SNR (wiki.supernewroles.com): 「キルを行ってから再度キルを行うまでに猶予時間が経過してしまうと、その場で自殺します」; options キルクールタイム, 自殺までの時間, 会議に入るとリセットされる, ベント可, サボタージュ可; wins as an Impostor. No numeric defaults on the wiki.

**Decisions** (spec D1–D11, reconciled):

| # | Decision |
|---|---|
| D1 | A real vanilla Impostor (`FromImpostorPool`, `Team.Impostor`, `IsKiller`, vent + sabotage on); **its kill is host-executed** (`case CustomRole.SerialKiller:` → `Rpc.Kill`, §1.6-5) so nothing depends on vanilla's `CheckMurder` body validating a timer shorter than the lobby cooldown; the client's short cooldown is its private `KillCooldown` option (§1.9). Not in the host-target whitelist (own case). |
| D2 | `[SerialKiller] KillCooldown` (1–60, default **10**), `SuicideTime` (10–300, default **30**), `ResetAtMeeting` (default on). |
| D3 | Countdown = seconds remaining per SK, decremented by `Time.deltaTime` in `SerialKiller.Tick()` (called right before `Kills.Tick()`, §1.13); runs only while playable (no intro / meeting / exile screen, not `Ending`, not `Game.SerialKillerPaused` = WrapUp → +2 s). |
| D4 | The death is a `Game.Bites` entry `{ Killer = sk, DueAt = now, Reason = "serialkiller" }` executed by `Kills.Tick` in the same frame (self-kill + body; postponed in a vent / on a ladder / platform; flushed at a report); the timer entry stays (`Remaining = 1 s` retry) until `!IsAlive` drops it (Guardian-Angel protect flash is not a death); **at most one time-out bite per frame**. |
| D5 | Timer restarts on every successful kill (`Kills.OnMurder` → `OnKill`, §1.6-6) **and on a guarded press on a Mad Stuntman** (batch, §3). `OnKill` cancels a pending own `"serialkiller"` bite (kill inside the ≤ 1 s vent-exit window) and ignores foreign bites. |
| D6 | Post-meeting resume at WrapUp + 2 s with an **authoritative kill-timer reset** (`Rpc.ResetKillCooldown(pc, KillCooldown)`, staggered 0.75 s per SK): the client sets its post-meeting timer from the option it holds at its own WrapUp, which vanilla's re-broadcast may have overwritten — lethal for a 15–30 s countdown. `ResetAtMeeting` on → full limit, off → `max(remaining, KillCooldown + 5)`; private "N s left" line. |
| D7 | Effective limit = `max(SuicideTime, KillCooldown + 5)` (one warning log per game, `_limitWarned` reset from `ResetRoleState`); warning at `min(10 s, limit / 2)` left. |
| D8 | First cycle armed and reset **after the last client's intro** (`Assign_IntroCutsceneOnDestroyPatch` → `OnIntroEnd` → arm at `1 s + Rpc.Queue.Interval × remote clients`, tag `"serialkiller.arm"`, game-scoped). No pre-intro fallback (`InProgress` is true before the cutscene exists). |
| D9 | Feedback = private chat (warning, time-out, resume) + `roleinfo.serialkiller.limit` + the own-name-tag countdown in 5 s steps (`シリアルキラー 20s`, red; `NameTags.RefreshAll()` unforced when the step changes — one paced `SetName` for the (sk, sk) pair). No public announcement. |
| D10 | Listed in `IsImpostorTeamKiller` (§1.3) → everything else follows. |
| D11 | No per-death hooks: `Tick` / `OnMeetingEnd` drop dead / disconnected / re-roled entries. |

**Files / hooks**: row (§1.2), `SerialKillerTimer` / `SerialKillerTimers` / `SerialKillerPaused` / `ResetState` call / `IsImpostorTeamKiller` case / `"serialkiller.arm"` tag (§1.3), options `serialkiller.cooldown|time|meetingreset` (§1.4), `RoleOptionText` (§1.5), `Kills` switch case + `OnMurder` hook (§1.6), `Meetings_ExileWrapUpPatch` `OnExileWrapUp` + `OnMeetingEnd` (§1.7), `OptionsDesync` cooldown case / `NeedsCustomOptions` / `GetKillCooldownPatch` (§1.9), `RoleAssignment` intro hook (§1.10), `NameFor` rule 1 countdown (§1.11), `RoleInfoText` line (§1.12), `Plugin_TickPatch` (§1.13), lang keys (20). Nothing in `WinConditions`, `AntiBlackout`, `TestMode`, `SettingsTab`, the host-target block. The class:

```csharp
using System;
using System.Collections.Generic;
using PocketRoles.Core;
using PocketRoles.Net;
using UnityEngine;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;

    /// <summary>
    /// シリアルキラー / Serial Killer (v0.5.0, after SuperNewRoles): vanilla Impostor with a short kill cooldown
    /// ([SerialKiller] KillCooldown, sent as its private KillCooldown option by OptionsDesync; kills are host-executed in
    /// Kills.HandleCheckMurder) that dies by itself when it has not killed for [SerialKiller] SuicideTime seconds.
    /// The countdown (Game.SerialKillerTimers, seconds left) runs only while the round is playable (no intro / meeting /
    /// exile screen, not while Game.SerialKillerPaused = WrapUp → +2 s), restarts on every kill (Kills.OnMurder → OnKill;
    /// a guarded press on a Mad Stuntman counts too) and, with [SerialKiller] ResetAtMeeting, after every meeting
    /// (OnMeetingEnd at WrapUp + 2 s). Whenever the countdown (re)starts without a kill — after the last client's intro and
    /// after every meeting — the client's kill timer is set authoritatively with Rpc.ResetKillCooldown, because a vanilla
    /// client sets its timer at its own intro end / WrapUp from whatever KillCooldown option it holds then (Meetings.cs
    /// "Vanilla re-broadcasts the true options around the meeting").
    /// The death itself is a Game.Bites entry (reason "serialkiller", credited to the player) executed by Kills.Tick in the
    /// same frame: self-kill animation + body on every client; postponed while in a vent / on a ladder / platform or
    /// GA-protected, flushed by the next report, retried while the player is still alive (a protect flash is not a death).
    /// Only the Serial Killer is told (private notices + a 5 s-step countdown in its own name tag); nobody else learns why it died.
    /// </summary>
    public static class SerialKiller
    {
        /// <summary>Private "kill within N s" notice, once per countdown, at this many seconds left (half the limit when the limit is shorter than 20 s).</summary>
        private const float WarnSeconds = 10f;
        /// <summary>The limit never goes below KillCooldown + this margin: the client's kill timer is the full cooldown after every kill and after every reset.</summary>
        private const float CooldownMargin = 5f;
        /// <summary>Retry interval after the time-out while the player is still alive (vent, Guardian Angel protection, one death per frame).</summary>
        private const float RetrySeconds = 1f;
        /// <summary>Own-name-tag countdown granularity (seconds).</summary>
        private const int TagStep = 5;
        /// <summary>Spacing between two Serial Killers' Rpc.ResetKillCooldown (its two urgent packets are 0.5 s apart: 0/0.5, 0.75/1.25, … never share a frame).</summary>
        private const float ResetStagger = 0.75f;
        /// <summary>Lead added to Rpc.Queue.Interval × remote clients: the arm waits for the last client's own intro end (paced role dispatch).</summary>
        private const float IntroArmLead = 1f;
        internal const string IntroArmTag = "serialkiller.arm";
        internal const string BiteReason = "serialkiller";

        private static readonly List<byte> Scratch = new List<byte>();
        private static bool _limitWarned;

        /// <summary>Game.ResetRoleState: one "limit raised" warning per game.</summary>
        internal static void ResetState() => _limitWarned = false;

        /// <summary>Effective countdown length: [SerialKiller] SuicideTime, raised to KillCooldown + 5 s when set lower (one warning per game).</summary>
        internal static float Limit()
        {
            float t = Options.SerialKillerSuicideTime;
            float min = Options.SerialKillerKillCooldown + CooldownMargin;
            if (t >= min) return t;
            if (!_limitWarned)
            {
                _limitWarned = true;
                PocketRolesPlugin.Logger.LogWarning($"SerialKiller: SuicideTime {t:0.#} s < KillCooldown + {CooldownMargin:0.#} s → limit raised to {min:0.#} s");
            }
            return min;
        }

        private static float WarnAt() => Mathf.Min(WarnSeconds, Limit() * 0.5f);

        private static int StepOf(float remaining) => remaining <= 0f ? 0 : Mathf.Max(TagStep, Mathf.CeilToInt(remaining / TagStep) * TagStep);

        /// <summary>Own-name-tag suffix for a living Serial Killer ("20s" in the impostor colour), null while nothing counts or the death is pending.</summary>
        internal static string CountdownTag(byte id)
        {
            if (!Game.SerialKillerTimers.TryGetValue(id, out var t) || t.Remaining <= 0f) return null;
            return "<color=" + Roles.ImpostorColor + ">" + StepOf(t.Remaining) + "s</color>";
        }

        // ------------------------------------------------------------------ countdown

        /// <summary>Per frame (Plugin_TickPatch, right before Kills.Tick): counts every living Serial Killer down and queues the death at 0.</summary>
        public static void Tick()
        {
            try
            {
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                if (Game.SerialKillerTimers.Count == 0 || Game.SerialKillerPaused) return;   // managed checks only while nobody counts
                if (MeetingHud.Instance != null || ExileController.Instance != null || IntroCutscene.Instance != null) return;

                float dt = Time.deltaTime, now = Time.time;
                bool refreshTags = false, queued = false;
                Scratch.Clear();
                foreach (var kv in Game.SerialKillerTimers) Scratch.Add(kv.Key);
                for (int i = 0; i < Scratch.Count; i++)
                {
                    byte id = Scratch[i];
                    if (!Game.SerialKillerTimers.TryGetValue(id, out var t)) continue;
                    if (Game.RoleOf(id) != CustomRole.SerialKiller || !Game.IsAlive(id))
                    {
                        Game.SerialKillerTimers.Remove(id);   // died, exiled, assassinated, disconnected
                        refreshTags = true;
                        continue;
                    }
                    t.Remaining -= dt;
                    if (!t.Warned && t.Remaining <= WarnAt())
                    {
                        t.Warned = true;
                        PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} has {Mathf.Max(0f, t.Remaining):0.#} s left to kill");
                        Kills.Notice(id, "serialkiller.warn", "あと {0:0.#} 秒以内にキルしないと死亡します！", "Kill within {0:0.#} s or you die!", Mathf.Max(0f, t.Remaining));
                    }
                    if (t.Remaining > 0f)
                    {
                        int step = StepOf(t.Remaining);
                        if (step != t.TagStep) { t.TagStep = step; refreshTags = true; }
                        Game.SerialKillerTimers[id] = t;
                        continue;
                    }

                    // Time is up. The entry stays (retry every second) until !IsAlive drops it: a vent, a Guardian Angel
                    // protection (protect flash instead of a death, OnMurder still runs) or the one-death-per-frame rule
                    // may leave the player alive after the bite.
                    t.Remaining = RetrySeconds;
                    if (t.TagStep != 0) { t.TagStep = 0; refreshTags = true; }
                    if (Game.Bites.ContainsKey(id))
                    {
                        if (!t.TimedOut) PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} ran out of time but is already dying (bite / curse / lover / slash / own time-out pending)");
                        t.TimedOut = true;
                        Game.SerialKillerTimers[id] = t;
                        continue;
                    }
                    var pc = Game.Player(id);
                    if (pc == null) { Game.SerialKillerTimers[id] = t; continue; }
                    if (pc.protectedByGuardianThisRound)
                    {
                        // Rpc.Kill sends Succeeded without DecisionByHost: a protected target shows the protect flash instead of dying. Wait it out.
                        if (!t.TimedOut) PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} ran out of time while protected by a Guardian Angel → death postponed");
                        t.TimedOut = true;
                        Game.SerialKillerTimers[id] = t;
                        continue;
                    }
                    if (queued) { Game.SerialKillerTimers[id] = t; continue; }   // one broadcast MurderPlayer per frame (unverified on the official server otherwise)
                    queued = true;
                    // Credited to itself: self-kill animation + body, reporter == victim (no Bait report); Kills.Tick executes it this frame.
                    Game.Bites[id] = new Game.VampireBite { Killer = id, DueAt = now, Reason = BiteReason };
                    if (!t.TimedOut)
                    {
                        PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} ran out of time → dies");
                        Kills.Notice(id, "serialkiller.timeout", "時間切れ…キルできなかったため、あなたは死亡しました。", "Time is up... you did not kill in time and died.");
                    }
                    else PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} still alive after the time-out → death retried");
                    t.TimedOut = true;
                    Game.SerialKillerTimers[id] = t;
                }
                Scratch.Clear();
                if (refreshTags) NameTags.RefreshAll();   // only changed strings leave: the (sk, sk) pair, one paced SetName
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SerialKiller.Tick: {e}");
            }
        }

        private static void Arm(byte id, float remaining, string why)
        {
            Game.SerialKillerTimers[id] = new Game.SerialKillerTimer { Remaining = remaining, Warned = remaining <= WarnAt(), TimedOut = false, TagStep = -1 };
            PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} timer {remaining:0.#} s ({why})");
        }

        /// <summary>
        /// Sets the client's kill button to the short cooldown no matter which KillCooldown option it held when it last set its
        /// timer (own intro end / own WrapUp). Host: SetKillTimer. Client: the live-verified FailedProtected trick (a protect
        /// flash on its screen). Staggered per Serial Killer so two urgent GameDataTo never share a frame.
        /// </summary>
        private static void ScheduleCooldownReset(byte id, int index, string why)
        {
            void Reset()
            {
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                if (Game.RoleOf(id) != CustomRole.SerialKiller || !Game.IsAlive(id) || Game.Bites.ContainsKey(id)) return;
                var pc = Game.Player(id);
                if (pc == null) return;
                Rpc.ResetKillCooldown(pc, Options.SerialKillerKillCooldown);
                PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} kill timer set to {Options.SerialKillerKillCooldown:0.#} s ({why})");
            }
            if (index <= 0) Reset();
            else Scheduler.After(ResetStagger * index, Reset);
        }

        // ------------------------------------------------------------------ hooks

        /// <summary>
        /// "assign.introend" (host intro end + 1 s): the countdown and the kill-timer reset are scheduled for after the LAST
        /// client's own intro (client n starts its intro ~Rpc.Queue.Interval × n later than the host: paced role dispatch).
        /// A kill made before that moment already armed the killer (OnKill) and is kept.
        /// </summary>
        internal static void OnIntroEnd()
        {
            try
            {
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                int remote = 0;
                foreach (var _ in Rpc.AllClientIds(false)) remote++;
                float delay = IntroArmLead + Rpc.Queue.Interval * remote;
                Scheduler.Cancel(IntroArmTag);
                Scheduler.After(delay, () =>
                {
                    if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                    Game.SerialKillerPaused = false;
                    float limit = Limit();
                    int index = 0;
                    foreach (var id in Game.AllPlayerIds())
                    {
                        if (Game.RoleOf(id) != CustomRole.SerialKiller || !Game.IsAlive(id) || Game.Bites.ContainsKey(id)) continue;
                        if (!Game.SerialKillerTimers.ContainsKey(id)) Arm(id, limit, "intro end");
                        ScheduleCooldownReset(id, index++, "intro end");
                    }
                }, IntroArmTag);
                PocketRolesPlugin.Logger.LogInfo($"SerialKiller: countdown starts {delay:0.#} s after the intro-end hook ({remote} remote client(s))");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SerialKiller.OnIntroEnd: {e}");
            }
        }

        /// <summary>MurderPlayer succeeded with this killer (Kills.OnMurder, target ≠ killer) or a guarded Mad Stuntman press: the countdown restarts.</summary>
        internal static void OnKill(byte killerId)
        {
            try
            {
                if (Game.RoleOf(killerId) != CustomRole.SerialKiller || !Game.IsAlive(killerId)) return;
                if (Game.Bites.TryGetValue(killerId, out var bite))
                {
                    if (bite.Reason != BiteReason) return;   // dying for another reason (vampire / curse / lover / slash): no restart, no notice
                    Game.Bites.Remove(killerId);             // own time-out pending (vent exit window): the kill came in time
                    PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(killerId)} killed before its time-out death executed → cancelled");
                }
                Arm(killerId, Limit(), "kill");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SerialKiller.OnKill({killerId}): {e}");
            }
        }

        /// <summary>ExileController.WrapUp (synchronous, after the bite postponement): countdowns hold until OnMeetingEnd (WrapUp + 2 s).</summary>
        internal static void OnExileWrapUp()
        {
            Game.SerialKillerPaused = true;
        }

        /// <summary>
        /// WrapUp + 2 s (inside the existing resync lambda; the scheduler runs before Kills.Tick, so a bite due now is still
        /// pending here): [SerialKiller] ResetAtMeeting → full limit; off → the remainder, never below KillCooldown + 5 s.
        /// Every surviving Serial Killer gets its kill timer reset to the short cooldown and a private "N s left" line;
        /// one that is already dying (own parked time-out, curse, bite, lover, drag) is dropped silently.
        /// </summary>
        internal static void OnMeetingEnd()
        {
            try
            {
                Game.SerialKillerPaused = false;
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                float limit = Limit();
                float floor = Options.SerialKillerKillCooldown + CooldownMargin;
                bool reset = Options.SerialKillerResetAtMeeting;
                int index = 0;
                foreach (var id in Game.AllPlayerIds())
                {
                    if (Game.RoleOf(id) != CustomRole.SerialKiller) continue;
                    if (!Game.IsAlive(id)) { Game.SerialKillerTimers.Remove(id); continue; }   // exiled / dead / disconnected
                    if (Game.Bites.ContainsKey(id))
                    {
                        Game.SerialKillerTimers.Remove(id);
                        PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} is already dying → not re-armed after the meeting");
                        continue;
                    }
                    float remaining = reset || !Game.SerialKillerTimers.TryGetValue(id, out var t) ? limit : Mathf.Max(t.Remaining, floor);
                    Arm(id, remaining, reset ? "meeting end, reset" : "meeting end, carried over");
                    ScheduleCooldownReset(id, index++, "meeting end");
                    Kills.Notice(id, "serialkiller.resume", "残り {0:0.#} 秒以内にキルしてください。", "{0:0.#} s left to kill.", remaining);
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SerialKiller.OnMeetingEnd: {e}");
            }
        }
    }
}
```
Notes: `Rpc.AllClientIds(bool)` (`IEnumerable<int>`), `Rpc.Queue.Interval` (0.3 f), `Scheduler.After/Cancel`, `NameTags.RefreshAll()` exist. `Arm` marks `Warned` when the value is already at / below the threshold (no double line). Per-frame cost with no SK: three flag reads + one dictionary count.

**Timeline of one round** (SK = E1, 2 clients, limit 30 s, cooldown 10 s): host intro end +1 s → `SerialKiller: countdown starts 1.6 s after the intro-end hook (2 remote client(s))`; +2.6 s → `timer 30 s (intro end)`, `kill timer set to 10 s (intro end)` (E1 sees a short shield flash, its button restarts at 10 s), tag `シリアルキラー 30s`; 20 s later → `has 10.0 s left`, chat `あと 10 秒以内に…`, tag `10s`; kill → `Kills: Serial Killer E1 killed E2`, `timer 30 s (kill)`, button restarts at 10 s by the client itself; report → meeting → countdown frozen; WrapUp (`SerialKillerPaused`) → +2 s: `timer 30 s (meeting end, reset)`, `kill timer set to 10 s (meeting end)`, chat `残り 30 秒以内に…`; 30 s without a kill → `ran out of time → dies` + `Kills: serialkiller on E1 (by E1) executes` → E1 falls → `OnMurder` → `Check()` +0.1 s.

**Edge cases**: SK kills a host Sheriff / Jackal / Arsonist → own switch case (`Rpc.Kill`), log `Kills: Serial Killer X killed Y` (the host-target block is not involved); host is the SK → `GetKillCooldownPatch` cooldown, `SetKillTimer` resets (no flash), `Chat.Local` notices, time-out `Rpc.Kill(lp, lp)` (verified pattern); SK in a vent / on a ladder at 0 → bite postponed 1 s per tick, dies on leaving (or at the next report); a kill inside the ≤ 1 s exit window cancels the pending bite; GA-protected → postponed / retried; report while pending → dies at the report (own report → parked, executes 2 s after WrapUp, not re-armed); timer never expires inside meeting / exile / intro; `ResetAtMeeting` off with 3 s left → `max(3, 15) = 15 s`; `SuicideTime < KillCooldown + 5` → raised, one warning; SK already bitten / cursed / dragged / slashed → no second bite, foreign reason → `OnKill` ignores; SK killed by Sheriff / bite / curse / exile / assassination → entry dropped next tick; disconnect → dropped, scheduled resets return; two SKs time out together → consecutive frames; SK kills a Bait → forced report → paused → reset after WrapUp + 2 s; SK kills a Terrorist with tasks done → game ends; Mafia + SK → Mafia blocked; Madmate family sees it red; a **guarded press on a Mad Stuntman restarts the timer**; the Samurai's `SlashInProgress` hold delays `Check()` only (an SK time-out inside a slash window is still executed by `Kills.Tick`); Evil Nekomata drag on an SK → foreign bite; `Game.Ending` → `Tick` returns, resets return; test mode → deaths happen, **no `EndGame` line** (`CheckNow` returns before `Evaluate`), end with `/end`; compat → no roles.

**Risks / open**: the client's timer behaviour at its own intro end / WrapUp and the `FailedProtected` halving are known from TECH-NOTES and other roles' live tests — the SK is the first role whose life depends on them (§12 K2 / K4 gate); a visible shield flash after the intro and after every exile screen (documented); the intro arm waits `1 s + 0.3 s × remote clients` — a large paced backlog could make a client's intro end later (raise `IntroArmLead` if seen); `protectedByGuardianThisRound` replication assumed (retry covers it); multi-SK + other bites can still execute in one frame through `Kills.Tick` (shipped behaviour); `Rpc.Kill` bypasses vanilla's distance check (Sheriff / Jackal precedent). Open: skip the intro-end reset when the SK already killed (it restarts a mid-cooldown button with a flash); 1 s steps for the last 10 s of the tag. **Reconciliation notes**: the `Kills.Tick` / `FlushBites` `Ending` re-check and the ladder / platform postponement (Samurai batch edits) apply to the SK bite as well; the guard's `OnKill` call is new (§0.1-4).

---

## 9. 侍 / Samurai / 武士 (Implementer D — `src/Game/Samurai.cs`)

SNR (github wiki): インポスター陣営, active「必殺技」— once per game, kills every crew around the samurai. With one vanilla button the slash **is** the kill; the once-per-game limit becomes a long cooldown (the brief: "wide slash, long cooldown, pressed target included, can vent/sabotage").

**Decisions** (spec S.0, reconciled): 1 every press is a slash, `[Samurai] KillCooldown` = 45 s (0 = lobby, Witch pattern), no per-game cap; 2 the pressed target dies by a normal `Rpc.Kill(samurai, target)` (even outside the radius, even an ally) — subject to the Mad Stuntman guard, which absorbs the **whole** press (§0.1-4); the samurai client's timer restart after the press relies on the vanilla owner-side `MurderPlayer(Succeeded)` path (§12 X5 gate, fallback `Rpc.ResetKillCooldown` after `Rpc.Kill`); 3 bystanders = every other slashable player within `[Samurai] Range` (2.0 map units) of the samurai's host-side position at press time, queued **before** the target dies as self-kills credited to the samurai (`Reason = "slash"`, `DueAt = now + Stagger × (i + 1)`, `[Samurai] Stagger` 0.3 s, 0.1–1.0, executed by the per-frame `Kills.Tick`; the press frame carries exactly one broadcast `MurderPlayer`); 4 allies spared by default (`[Samurai] HitTeammates = false`: `IsImpostorTeamKiller` ‖ `TeamOf == Impostor` = impostors, the four impostor-pool roles, a second Samurai, an impostor lover, the whole Madmate family); 5 excluded: dead / disconnected, `inVent` / `onLadder` / `inMovingPlat`, GA-protected, **shielded (`Kills.IsShielded`, no charge)**, the samurai, the pressed target; a player that already has a `Bites` entry is not skipped — its `DueAt` is brought forward, `Killer` / `Reason` kept; 6 silent to everyone else, one private notice to the samurai listing the bystanders; 7 vanilla Impostor (real, vent + sabotage, red for Madmates, Sheriff-shootable, `IsImpostorTeamKiller`); 8 range = absolute radius on `GetTruePosition()` (fallback `transform.position`), lobby kill distance only logged; 9 no new state beyond `Kills._slashUntil` (§1.6-8); 10 **the win check and the Bait auto-report wait for a running slash**, bounded to `Stagger × N + 1 s` (§1.6-11, §1.8-3); 11 a synchronous `EndGame` inside the batch stops it (§1.6-9, `Slash` re-checks `Ending` after the target kill); 12 shield gate = `Kills.IsShielded` (§0.1-4).

**Files / hooks**: row (§1.2), `IsImpostorTeamKiller` case (§1.3), options `samurai.cooldown|range|stagger|teammates` (§1.4), `RoleOptionText` (§1.5), `Kills` switch case, `IsShielded`, `_slashUntil` / `MarkSlash` / `SlashInProgress`, loop `Ending` re-checks, `ExecuteBite` ladder / platform postponement, `ForceReport` wait (§1.6), `WinConditions.Check()` hold (§1.8-3), `OptionsDesync` Witch-pattern cooldown (§1.9), `RoleInfoText` line (§1.12), lang keys (23). Not in the host-target whitelist (own case: a host Sheriff / Jackal / Arsonist as target goes through the switch, Arsonist / Witch precedent — **unverified** primitive, §12 X10 / X11). The class:

```csharp
using System;
using System.Collections.Generic;
using PocketRoles.Core;
using PocketRoles.Net;
using UnityEngine;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;

    /// <summary>
    /// 侍 / Samurai (v0.5.0): vanilla Impostor whose kill is an area slash. Every other slashable player within
    /// [Samurai] Range of the samurai (host-side positions) is queued as a self-kill credited to the samurai
    /// (Game.Bites, reason "slash", [Samurai] Stagger seconds apart, executed by the per-frame Kills.Tick — the
    /// Witch-curse / lover-follow visual: the victim drops where it stands, the samurai does not move); then the
    /// pressed target dies by a normal kill. The frame of the press carries exactly one MurderPlayer RPC.
    /// Hooks: Kills.HandleCheckMurder case (after the Mad Stuntman guard: a shielded pressed target absorbs the whole
    /// press), Game.IsImpostorTeamKiller, OptionsDesync (cooldown), Chat.RoleInfoText, Kills.MarkSlash / SlashInProgress
    /// (win check + Bait report wait for the batch). No per-game state here.
    /// </summary>
    public static class Samurai
    {
        /// <summary>Grace added to the stagger window for Kills.MarkSlash (the last bite may be postponed 1 s by a vent).</summary>
        private const float HoldGrace = 1f;

        /// <summary>[Samurai] KillCooldown, or the lobby kill cooldown when 0 (log line; Stuntman guard; risk-#4 fallback reset).</summary>
        internal static float KillCooldown()
        {
            float cd = Options.SamuraiKillCooldown;
            return cd > 0f ? cd : Kills.LobbyKillCooldown();
        }

        // ------------------------------------------------------------------ slash

        /// <summary>
        /// Kill button pressed on <paramref name="target"/> (after Kills.IsValidMurder: game running, no meeting / exile /
        /// intro, both alive, target not in a vent / GA-protected; a shielded target never reaches this). Queues everyone
        /// else in range, then kills the target normally. Nothing else leaves the host in this frame.
        /// </summary>
        internal static void Slash(PlayerControl samurai, PlayerControl target)
        {
            try
            {
                if (samurai == null || target == null) return;
                if (samurai.Data == null || target.Data == null) return;
                byte s = samurai.PlayerId, t = target.PlayerId;
                float range = Options.SamuraiRange;
                float stagger = Options.SamuraiStagger;
                Vector2 center = PositionOf(samurai);

                // 1. Bystanders: fixed and queued BEFORE the target dies, so the target's death hooks (Lovers.Follow for a
                //    partner in range) see the slash entry and do not add their own. Self-kills credited to the samurai
                //    (Bait auto-report, log), one per HudManager tick, Stagger seconds apart.
                var queued = new List<byte>();
                var names = new List<string>();
                float now = Time.time;
                foreach (var pc in Game.AllPlayers())
                {
                    byte id = pc.PlayerId;
                    if (id == s || id == t) continue;
                    if (!Game.IsAlive(id)) continue;                                                       // dead / disconnected (the GM host is dead)
                    if (pc.inVent || pc.onLadder || pc.inMovingPlat || pc.protectedByGuardianThisRound) continue; // the states vanilla CheckMurder refuses
                    if (Kills.IsShielded(id)) continue;                                                    // Mad Stuntman gate (v0.5.0 shared, no life spent)
                    if (!Options.SamuraiHitTeammates && IsAlly(id)) continue;                              // fellow impostors / Madmate family / impostor lover
                    if (Vector2.Distance(center, PositionOf(pc)) > range) continue;
                    float due = now + stagger * (queued.Count + 1);
                    if (Game.Bites.TryGetValue(id, out var existing))
                    {
                        // Already dying (vampire bite / lover follow / curse / drag): the slash only brings the death forward;
                        // the original killer keeps the credit, the reason stays for the log.
                        if (existing.DueAt > due) { existing.DueAt = due; Game.Bites[id] = existing; }
                    }
                    else
                    {
                        Game.Bites[id] = new Game.VampireBite { Killer = s, DueAt = due, Reason = "slash" };
                    }
                    queued.Add(id);
                    names.Add(Game.NameOf(id));
                }
                if (queued.Count > 0) Kills.MarkSlash(stagger * queued.Count + HoldGrace);   // win check + Bait report wait for the batch

                // 2. The pressed target: a normal kill (kill animation on both screens, body at the target's position;
                //    the samurai client is expected to reset its timer from its private KillCooldown option when this arrives).
                PocketRolesPlugin.Logger.LogInfo($"Kills: Samurai {Game.NameOf(s)} slashed {Game.NameOf(t)} (range {range:0.##}, lobby kill distance {LobbyKillDistance()}, cooldown {KillCooldown():0.#}s)");
                Rpc.Kill(samurai, target);

                // 3. The target's death may have ended the game synchronously (Terrorist with all tasks done → EndGame in
                //    OnMurder): nothing more may go out, and no stale entries stay behind.
                if (Game.Ending || !Game.InProgress)
                {
                    int removed = 0;
                    foreach (var id in queued)
                    {
                        if (Game.Bites.TryGetValue(id, out var b) && b.Reason == "slash" && b.Killer == s && Game.Bites.Remove(id)) removed++;
                    }
                    if (queued.Count > 0)
                        PocketRolesPlugin.Logger.LogInfo($"Kills: Samurai {Game.NameOf(s)}'s slash cancelled: game over after the target's death ({removed} pending removed)");
                    return;
                }
                if (queued.Count == 0) return;

                string list = string.Join(", ", names);
                PocketRolesPlugin.Logger.LogInfo($"Kills: Samurai {Game.NameOf(s)}'s slash also hits {queued.Count} ({stagger:0.#} s apart): {list}");
                Kills.Notice(s, "kill.slash", "{0} を斬りました。続けて倒れる {1} 人: {2}", "You slashed {0}; {1} more fall next: {2}", Game.NameOf(t), queued.Count, list);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Samurai.Slash: {e}");
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Impostor-team player: impostor-side killers (IsImpostorTeamKiller) and Team.Impostor non-killers (the Madmate family).</summary>
        private static bool IsAlly(byte id) => Game.IsImpostorTeamKiller(id) || Game.TeamOf(id) == Team.Impostor;

        /// <summary>Host-side position (replicated CustomNetworkTransform for remote players; a few hundred ms behind).</summary>
        private static Vector2 PositionOf(PlayerControl pc)
        {
            try { return pc.GetTruePosition(); }
            catch (Exception) { return pc.transform.position; }
        }

        /// <summary>Lobby kill distance, log only (calibration of [Samurai] Range); "?" when the vanilla call fails.</summary>
        private static string LobbyKillDistance()
        {
            try
            {
                var gm = GameManager.Instance;
                if (gm != null && gm.LogicOptions != null) return gm.LogicOptions.GetKillDistance().ToString("0.##");
            }
            catch (Exception) { }
            return "?";
        }
    }
}
```
Execution trace with bystanders B, C: queued (B +0.3 s, C +0.6 s), `MarkSlash(1.6)` → `Kills: Samurai S slashed A (range 2, lobby kill distance 1.8, cooldown 45s)` → `Rpc.Kill(S, A)` (the only RPC of the frame) → `OnMurder(S, A)` (hooks; a lover partner in range is skipped by `Follow`'s `ContainsKey`; Bait `ForceReport` +0.2 s; `Check()` +0.1 s) → `Ending` re-check → `Kills: Samurai S's slash also hits 2 (0.3 s apart): B, C` → private chat → +0.1 s `Check()` returns (`SlashInProgress`) → +0.2 s `ForceReport` re-schedules itself → +0.3 s `Kills: slash on B (by S) executes` → +0.6 s same for C → +0.7 s `Check()` → `CheckNow()`; the Bait report goes out on its next 0.3 s retry. `Kills.Tick()` is never called from here.

**Views**: own client vanilla Impostor (intro with teammates, kill button timer = `[Samurai] KillCooldown` via desync or the lobby value, vents, sabotage, fake tasks), own tag `侍`, role chat + `roleinfo.samurai` line at start; other impostors see an Impostor; desync viewers a Crewmate; Madmate family red; Snitch-done red; slash victims: the target gets the normal kill animation (samurai shown as killer), bystanders a self-kill animation 0.3 s apart, others see bodies appear one after another; dead samurai `ImpostorGhost` / `CrewmateGhost` per viewer.

**Edge cases**: press in a meeting / exile / intro, target dead / in a vent / GA-protected / `Ending` → `FailKill`, no slash; **pressed target shielded (Mad Stuntman)** → the guard absorbs the whole press (life −1, samurai cooldown reset, no slash, `kill.stuntman.guarded` line); pressed target = a host with a desync role → switch → `Rpc.Kill(samurai, hostPc)` (unverified, X10; fallback = treat `IsHost(t) && IsDesyncImpostor(t)` like `IsValidMurder` false and exclude such a host from the bystander loop); host as a bystander → `Rpc.Kill(host, host)` (vampire-bite-on-host path; host with a desync role → X11); host is the samurai → same code, host positions; ally in range spared silently (a pressed ally dies — vanilla semantics); bystander already dying → `DueAt` brought forward, credit kept; pressed target a lover with the partner in range → the partner already holds the slash entry (`Follow` skips), dies at its slot credited to the samurai; partner out of range → follows +0.5 s; bystander lover → partner in range already queued / out of range follows; samurai impostor-lover with `HitTeammates` on and the partner in range → follows itself; bystander Witch → spells cleared; Arsonist → douses vanish; target or bystander Bait → forced report waits for the batch (0.3 s retries) then the samurai reports with every victim dead; target Terrorist with tasks done → `EndGame` → queued entries removed, no notice; bystander Terrorist → its bite ends the game, the `Tick` loop breaks, the rest never execute; a player-initiated report / emergency inside the window → `FlushBites` executes the remaining slash bites at once (existing report semantics; the reporter's own entry is parked → a bystander that reports before its turn survives into the meeting and dies ~2 s after WrapUp); game ends on the target's death (last crew slashed) → `Check()` held until the last bystander's `OnMurder`; a bystander that enters a vent / ladder / platform or disconnects inside the window can outlive the end (bounded hold, documented); meetings / AntiBlackout: slashes never inside meetings, pending entries pushed to WrapUp + 2 s like every bite; with cooldown 0 and lobby 0 a slash is possible from the first frame after WrapUp while `AntiBlackout.Active` (same exposure as every killer, N-fold; README recommends ≥ 2.5 s); disconnects (bystander → never queued / dropped; samurai leaving → its entries still execute); two Samurai → spare each other by default, `_slashUntil` = later mark; Mafia blocked while a Samurai lives; Sheriff may shoot it, a Sheriff in range dies like any crew; a Serial Killer as bystander → foreign bite (its countdown entry is dropped at death); Evil Nekomata drag + slash → drag entry brought forward; Jackal Friends / Worshipper (`Team.Impostor` → ally) / Mad family (allies) per `IsAlly`; a vanished Phantom in range → ally by default; GA-protected bystander spared; test mode → all bystanders die (`CheckNow` never ends the game), `EndGame(Impostor) suppressed: test mode` only for direct callers; compat / haison / GM host → nothing / dead.

**Risks / fallbacks**: residual same-frame bursts through a player-initiated report inside the window (existing `FlushBites` semantics; pacing `FlushBites` is out of scope); `GetTruePosition` semantics / replication lag (tune `Range` from the logged lobby kill distance); host desync as target / bystander unverified (X10 / X11, exclusion fallback); samurai client timer after a plain `Rpc.Kill` unverified (X5, `ResetKillCooldown` fallback); the bounded win-check hold delays every `Check()` by ≤ `Stagger × N + 1 s` (remove one line in `WinConditions.Check()` to restore "end on the target's death" if it misbehaves; then README §27 must say survivors of a killing blow stay alive); ghost-role packets N deaths × M clients paced 0.3 s (cosmetic); balance numbers are first guesses (all options). **Reconciliation notes**: the spec's shared-gate proposal (`IsShielded` in `IsValidMurder` / `ExecuteBite`) was narrowed per §0.1-4 (the Stuntman guard owns presses; `IsShielded` gates bystanders and Nekomata candidates only); the Stuntman decision "no charge for a skipped bystander" is confirmed; the spec's alias-table reservation is fulfilled by §1.2; `IsValidMurder` is not widened (§0.1-8).

---

## 10. イビルホーク / Evil Hawk / 邪恶鹰眼 (Implementer D — no new file)

SNR (wiki + `Roles/Impostor/EvilHawk.cs`): an Impostor with an active「ホークアイ」button — cooldown 30 s, duration 10 s, camera zoom-out ×2 (1.25–10), cannot walk while active. A vanilla client has no button and cannot zoom; the only widening channel for an Impostor-basis client is its own `ImpostorLightMod` option (the Sheriff's `ImpostorLightMod = CrewLightMod` line writes the same field) → **passive** `[EvilHawk] VisionMultiplier` (default **2**, 1–5, step 0.25), always on. Evidence status: the per-client vision line is shipped since v0.4.0 but **never observed live in the tree** — §12 gate G3 is ship / no-ship for this role (benign failure mode: silently a plain impostor).

**Decisions**: no second option (no cooldown / duration / "cannot walk"; lights sabotage never affects impostor vision); plain vanilla impostor kill (`default: return true`, lobby cooldown) — `VanillaKillsHost` lists it (§1.6-1); vent + sabotage; `Team.Impostor`, `FromImpostorPool`, listed in `IsImpostorTeamKiller` (§1.3); real vanilla Impostor client (impostor intro, sees other impostors); names / alias `eh` only, **first** among the v0.5.0 impostor-pool rows (§1.2 prefix rule: `イビ` / `イビル` / `邪` / `邪恶` → Evil Hawk; `イビルホ` / `邪恶鹰` certain). No class, no state, no notices, no win kind, no scheduler tag, no `Bites`.

**Files / hooks**: row (§1.2), `IsImpostorTeamKiller` (§1.3), option `evilhawk.vision` (§1.4; `/show` inline `x{N}`), `RoleOptionText` (§1.5), `VanillaKillsHost` (§1.6-1), `OptionsDesync.BuildFor` (`ImpostorLightMod × N`, inside the switch), `NeedsCustomOptions` (`> 1f`, Witch pattern), `ApplyHostLightMod` (§1.9), lang keys (10). Nothing in `GetKillCooldownPatch`, `NameTags`, `WinConditions`, `Meetings`, `AntiBlackout`, `RoleAssignment`, `TestMode`, `Chat` (optional `roleinfo.evilhawk.vision` line not adopted).

**Behaviour**: own client — impostor intro, kill button (lobby cooldown), vents / sabotage, fake tasks; light radius = `MaxLightRadius × ImpostorLightMod × N` (vanilla defaults impostor 1.5 → ×2 = 3.0× crew vision; above ~3 the whole screen); blackout does not touch it; own tag 1 s after the intro; role chat `イビルホーク [インポスター陣営]` + description (two chunks). Windows where its vision is briefly normal: from a vanilla option re-broadcast until the next paced private copy (game start: behind the role tables; meeting: from the vanilla re-broadcast until the WrapUp `ResyncAll` packet drains behind the exiled player's ghost-role packets, +2 s resend as the safety net) — README §27 sentence extended. Others: real impostors see an Impostor; desync viewers a Crewmate; Madmate family red; Snitch-done red; nobody can tell it from a normal impostor. Kills: vanilla path; a host Sheriff / Jackal / Arsonist target → host-target block → `Rpc.Kill`, log `Kills: <hawk> killed the host (<role>)`; Sheriff may shoot it; Mafia blocked while it lives; Vampire / Witch / Jackal / Arsonist / Nekomata (with `ExcludeImpostors` off) / Samurai (with `HitTeammates` on) act on it like any player; **Assassin**: a correct guess must name `evilhawk` / `eh` / `イビルホーク` / `邪恶鹰眼` / an unambiguous prefix — `impostor` on an Evil Hawk is wrong (Assassin dies; README Assassin row). Meetings / exile: nothing role-specific (AntiBlackout `Prepare` may temporarily send it `Crewmate`; `Restore` + `ResyncAll` put vision back). Death: `ImpostorGhost` per viewer; a dead Evil Hawk keeps receiving its options (harmless), base back at lobby. Host as Evil Hawk: vanilla button, `ApplyHostLightMod` on every map (Fungle inherits the base patch — plain regression check). Two Evil Hawks: independent. Worshipper → `Verdict.Impostor` (self-destruct, never converted). Test mode: `/assign <who> evilhawk|eh|イビルホーク|イビルホ|邪恶鹰眼|邪恶鹰` (not the spaced `evil hawk` — `/assign` / `/set` take the last token; only `/cmd r evil hawk` accepts the two-word form); `/test off` clears forced roles (re-`/assign`); `EndGame(Impostor) suppressed: test mode` in test games.

**Risks**: the ability is inferred, not verified (G3; a failed gate drops the role from v0.5.0 — no caveat shipping); host vision per map is closed (no extra `CalculateLightRadius` patch, ever); unclamped product (`ImpostorLightMod` up to 10 × 5; fallback `Mathf.Min(10f, …)` if a client rejects it); name resolution fixed by §1.2; SNR fidelity (always on — README says 常時); Mafia + Evil Hawk blocks the Mafia (intended). **Reconciliation notes**: the spec's shared-switch proposals for `IsImpostorTeamKiller` and the host-target whitelist are the §1.3 / §1.6-1 shapes; the spec's converter guard is satisfied by `Worshipper.Judge` line 1; the `IsAdminOptKey` `eh.` prefix is dropped (fallback admits it), `evilhawk.` kept for symmetry.

---
## 11. Work split (four implementers; nobody edits the same file region concurrently)

| Implementer | Owns | Order / rule |
|---|---|---|
| **A** (first, alone) | §1.1 version + CHANGELOG; §1.2 `Roles.cs`; §1.3 `GameState.cs`; §1.4 `Options.cs`; §1.5 `Commands.cs`; §1.6 `Kills.cs`; §1.7 `Meetings.cs`; §1.8 `WinConditions.cs`; §1.9 `OptionsDesync.cs`; §1.10 `RoleAssignment.cs`; §1.11 `NameTags.cs`; §1.12 `Chat.cs`; §1.13 `PocketRolesPlugin.cs`; §1.14 the six class files **with stub bodies**; §1.15 the three lang files. | Steps in the numbered order; `dotnet build -c Release` must be 0 errors after §1.14 (the stubs make every shared call compile: `MadStuntman.Remaining → 0` means the guard never fires, `Worshipper.Worship → return` means a worship press does nothing, `SerialKiller.Tick → return`, `Samurai.Slash → return`, `JackalFriends.AnyJackal → false` (the gate skips Friends until D fills it — acceptable in the skeleton), `EvilNekomata.OnPlayerExiled → return`). Runs §12 A1-A3 (lobby / commands / settings tab) before handing over. After A, **no shared file is edited by anyone**; a missing shared line is a ticket back to A. |
| **B** | §2 Mad Mayor (no file — verifies §1 wiring), §3 `src/Game/MadStuntman.cs`, §4 Mad Hawk (no file), §5 `src/Game/Worshipper.cs` | Fills the two class bodies exactly as printed; runs §12 M, S, H, W. |
| **C** | §7 `src/Game/EvilNekomata.cs`, §8 `src/Game/SerialKiller.cs` | Fills the two class bodies; runs §12 N, K. |
| **D** | §6 `src/Game/JackalFriends.cs`, §9 `src/Game/Samurai.cs`, §10 Evil Hawk (no file) | Fills the two class bodies; runs §12 J, X, E. |
| **All** | §12 C (cross-role) and R (real-end runs) after B/C/D report green; the docs agent runs §13 in parallel from A's §1.2 / §1.15 texts. | Every log line quoted in §12 is the acceptance criterion; results of the ship / no-ship gates go into `support/worklog-2026-09-09.md` under `## v0.5.0 実機テスト`. |

One-line insertion points B/C/D may touch **inside their own files only**: none in shared files. The constants that a live test may flip are inside the owned files: `Worshipper.HostDiesByExile` (B), `Worshipper.ImpostorViewMark` (B), `SerialKiller.IntroArmLead` (C), `Samurai.HoldGrace` (D); the two fallbacks that touch a shared file — `Rpc.ResetKillCooldown` after `Rpc.Kill` in `Samurai.Slash` (owned file, D) and raising `[MadHawk] SpeedMultiplier`'s minimum to 1.0 (`Options.cs`, A on request) — are listed in §12 with their owner.

---

## 12. Live-test plan (PC host + 2 MuMu emulator clients A, B; registered lobby; `[Diagnostics] WireLog = true` recommended)

**Setup for every run**: game closed → `dotnet build -c Release` (0 errors) → copy the DLL and `lang/*.json` into `BepInEx\PocketRoles` → start; startup log `PocketRoles v0.5.0 loaded…` and **no `PatchAll failed`**. `/test on` unless stated (deaths, tags, chat, guards, conversions, drags and time-outs all happen with `/test on`; **count-based ends never do** — `WinConditions.CheckNow` returns before `Evaluate`, `WouldContinue` is `true`, and only the new `WinConditions(test): outcome=… crewForCount=…` line shows the counts). With 3 players `TestMode_AdjustedNumImpostorsPatch` keeps ≥ 1 vanilla impostor: force **all three** roles whenever the test needs a deterministic impostor (a crew-pool forced role on a vanilla impostor makes it a Crewmate; `EnsureImpostorPresent` promotes an unforced player when a forced role removed the last impostor — its warning line is expected then). `/test off` **clears forced roles** and is refused in-game: for a real-end run switch it off in the lobby, re-`/assign`, `/start` (forced start keeps `MinPlayers = 1`; confirm the vanilla "4 人でプレイ可能ですが…" popup by hand if it appears). `<host>` = the host's name or `#id`.

**Gates (ship / no-ship or fallback decisions; record each outcome in the worklog)**

| Gate | Step | Pass | Fail → action (owner) |
|---|---|---|---|
| G0 `PatchAll` | A1 | no `PatchAll failed` | drop `[HarmonyArgument(0)]` in `Meetings_VotingCompletePatch` (plain by-name `states`), then drop the parameter and rely on `VotersFor`'s `hud.playerStates` fallback; retest N3 / N8 (A) |
| G1 settings-tab label | A3 | `マッド系を撃てる` / `Can kill Mad roles` not clipped | revert to `マッドメイトを撃てる` / `Can kill Madmate` (+ JSON) (A) |
| G2 Mad Hawk vision | H3 | A's lit radius visibly ×3 vs B | **no ship**: the Mad Hawk degrades to a plain Madmate — remove the role from v0.5.0 (rows, options, keys, docs) (B + A) |
| G2b Mad Hawk speed | H4 | A slower with `madhawk.speed 0.5`; lobby 0.5 × 0.5 clamps to 0.5 without a kick | raise the option minimum to 1.0 (bind, `TrySet`, descriptor, README) (A) |
| G3 Evil Hawk vision | E3 | A's lit area visibly wider than the vanilla impostor B at ×5 | **no ship**: remove the Evil Hawk from v0.5.0 (D + A) |
| G4 Worshipper host self-kill | W2-4 | host dies on every screen | `Worshipper.HostDiesByExile = true`, rebuild, repeat (B) |
| G4b Worshipper host as victim | W2-3 | host Worshipper dies when shot by a Sheriff | file as a gap of the shipped host-target path (host Sheriff / Jackal / Arsonist share it) and fix with G4's fallback pattern in `Kills`' host branches (A) |
| G5 Ⓦ glyph | W2-1 | emulator shows `Ⓦ` | `Worshipper.ImpostorViewMark` = colour-distinct `Ⓜ`, update §13 (B) |
| G6 Samurai client timer | X5 | A's kill timer restarts at 45 s by itself after a slash | `Rpc.ResetKillCooldown(samurai, Samurai.KillCooldown())` after `Rpc.Kill` in `Slash` (D) |
| G7 host desync target / bystander | X10 / X11 | host Sheriff dies when slashed / as a bystander | exclusion fallback in `Slash` (`IsHost && IsDesyncImpostor` → `FailKill` + notice; skip as bystander), README §27 (D) |
| G8 Serial Killer timer resets | K2 / K4 | E1's button shows **5 s, not 45** after the intro reset and ~2 s after the exile screen | raise `SerialKiller.IntroArmLead` (intro) / investigate the WrapUp +2 s reset (C) |
| G9 paced multi-kills | X17 / N12 / K9 | no "Hacking" disconnect after a 3-victim slash at 0.3 s, a curse + drag 0.5 s apart, two SK resets 0.75 s apart | raise `samurai.stagger` to 0.5 / report in the worklog (D, C) |

**A. Lobby / commands / settings (Implementer A, after §1)**
* A1 Startup log: `[Roles] MadMayor.Count` … `Samurai.Chance` bound; sections `[MadMayor]` `[MadStuntman]` `[MadHawk]` `[Worshipper]` `[JackalFriends]` `[EvilNekomata]` `[SerialKiller]` `[Samurai]` `[EvilHawk]` in `jp.pocketroles.mod.cfg`; no `PatchAll failed` (G0); lang parity `diff` empty, `sort -c` silent, 944 keys each.
* A2 `/cmd r` lists 26 roles: impostor group Madmate → Mad Mayor → Mad Stuntman → Mad Hawk → Worshipper → Vampire → Mafia → Witch → Assassin → Evil Hawk → Evil Nekomata → Serial Killer → Samurai; neutral group Jackal → Jackal Friends → Lovers → Arsonist. Every alias and the shortest prefixes of §1.2 resolve (`/cmd r mmy|stunt|mh|ws|jf|eh|neko|sk|sam`, `/cmd r マッドメイヤ|マッドス|マッドホ|崇拝|ジャッカルフ|イビルホ|イビル猫|シリ|侍`, `/cmd r 狂|疯|鹰|崇|豺狼之|邪恶鹰|邪恶猫|连|武`); `/cmd r マッド` and `/cmd r マッドメイ` → Madmate; `/cmd r イビ` → Evil Hawk; `/cmd r hawk` → `cmd.badrole`; `/cmd r sheriff` → `キルCD 30秒、マッド系を撃てる`; `/cmd r madmate` (with `madmate.known on`) → `インポスターはマッド系役職が誰か分かる`. From an emulator: `/cmd r madmayor` = 3 messages, the last `投票は 2票分、インポスターに公開: オフ`; in English (`/cmd lang en`) `/cmd r madmayor` and `/cmd r jf` = exactly 3 messages with the option line intact (no `…`); `/cmd r worshipper` ja / zh = 3 messages. `/set <each role> 1` and `/opt <each key>` (§1.4) accepted; `/opt samurai.stagger 0` → `samurai.stagger = 0.1`; `/show` lines: `マッドメイヤー x1 2票`, `マッドスタントマン x1 耐久1回`, `マッドホーク x1 x3`, `崇拝者 x1 崇拝1回 CD30秒`, `ジャッカルフレンズ x1`, `イビルホーク x1 x2`, `イビル猫又 x1`, `シリアルキラー x1 キルCD10秒 制限30秒`, `侍 x1 斬撃CD45秒 範囲2`. An Admin.txt emulator may `/cmd opt madstuntman.lives 3`, `/cmd opt jf.sheriff off`, `/cmd opt neko.announce off`, `/cmd opt sk.time 20`.
* A3 Settings tab → 役職 page: nine new headers (impostor red / Jackal blue) with 人数・確率 + their rows; the Sheriff row label `マッド系を撃てる` (G1); "?" help shows Kill / Vent / Sabotage / Tasks ○× per row (Worshipper: Kill ○ Vent × Sabo × Tasks ×; Samurai ○ ○ ○ ×). `/vset` pages unchanged. Reset every option afterwards.

**M. Mad Mayor (B)** — `/opt madmayor.votes 3`, `/set madmayor 1`.
* M1 `/assign A madmayor`, `/assign B sheriff` (host = vanilla impostor by promotion). Log `TestMode: forced #… -> Mad Mayor (vanilla Crewmate -> Crewmate)` (or `Impostor -> Crewmate` + the promotion warning), `… -> Mad Mayor [Impostor]`. A: crew intro, task list, no buttons; red `マッドメイヤー` above its own name; the host's name red; chat `マッドメイヤー [インポスター陣営] …`. Host / B see A plain.
* M2 Vote weight: B calls a meeting; A votes the host, host + B skip → A's icon **3×** under the host on all three screens, host exiled; log `Meetings: Mayor tally → exiled=<host> tie=False overruled=False votes=5`, then the `AntiBlackout: …` lines and `WinConditions(test): outcome=Crew …` (no end). `/end`.
* M3 `/opt madmayor.votes 5` → 5 icons, no kick (G9-adjacent). `/opt madmayor.votes 1` (Mayor 1) → no `Mayor tally` line, single icon, vanilla result. Both a Mayor and a Mad Mayor (`/assign B mayor`, `mayor.votes 2`) voting → icons for both, `votes=` = players + 3.
* M4 `/opt madmayor.known on` → `Ⓜ` before A's name on the host (impostor) screen only; off → gone next game.
* M5 Sheriff: `sheriff.killmadmate on` → B's shot kills A; off → B dies (`誤射！…`).
* M6 Dead / mid-meeting: kill A, meeting → no tally line. Host Assassin: `/cmd guess A madmayor` → A dies mid-meeting, no `Mayor tally` line (vanilla tally decides); next game `/cmd guess A マッドメイ` → `外れ！… マッドメイト …` and the host dies; `/cmd guess A マッドメイヤ` → correct.
* M7 Host as Mad Mayor: `/assign <host> madmayor` → own red tag, its vote shows 3 icons on both emulators.

**S. Mad Stuntman (B)** — `/assign A madstuntman`, `/assign B jester` (host = vanilla impostor).
* S1 Log `… -> Mad Stuntman (vanilla Crewmate -> Crewmate)`, `[Impostor]`; A: crew intro, no button, red own tag, host red; role chat ends `耐えられるキル: 残り 1 回`; `/cmd n` repeats it; `/cmd r stunt` → `キルを 1 回まで耐える、本人に通知: オフ`; `Ⓜ` on the host only with `madmate.known on`.
* S2 Host kills A → nothing happens to A, the host's button restarts (lobby cooldown), host chat `A はキルを耐えました（マッドスタントマン、次は死にます）。`, **no** chat on A, log `Kills: <host> (None) hit Mad Stuntman A → guarded (0 left)`, no `Kills_MurderPlayerPatch` activity; A's next reminder `残り 0 回`. `/opt madstuntman.notify on` (new game) → A additionally reads `キルを耐えました。次は耐えられません。`.
* S3 Second kill after the cooldown → A dies normally (`CrewmateGhost`); `/opt madstuntman.lives 2` → two guards (`残り 1 回`, then the `.last` line), third kill lands.
* S4 Client as killer, host as Stuntman: `/assign <host> madstuntman`, `/assign B jester` (A = impostor): A's kill on the host → host survives, A's timer restarts with no kill animation (the vanilla-Impostor-client variant of the Vampire reset — watch it), A gets the role line; second kill → host dies.
* S5 Sheriff: `/assign B sheriff`, `sheriff.killmadmate on` → guarded, B's chat exactly `A はキルを耐えました。`; second shot kills. `off` → B dies, A's reminder still `残り 1 回`.
* S6 Vampire / Witch: `/assign <host> vampire` → bite guarded (no `Kills: Vampire … bit …`, A never dies later, no chat on A by default); `/assign <host> witch` → spell guarded, no `†`, the next meeting ends with nobody dying.
* S7 Not blocked: vote A out → exiled; host Assassin `/cmd guess A madstuntman` → A dies in the meeting; `/cmd guess A madmate` → the host dies; `/cmd guess A mad stuntman` → player-not-found reply, no guess consumed.
* S8 0 s cooldown floor (optional): lobby kill cooldown 0, `madstuntman.lives 5`, mash → at most one `guarded` line per second, host timer 1 s after each guard, no kick.

**H. Mad Hawk (B)** — `/assign A madhawk`, `/opt madhawk.speed 1`.
* H1 Lobby: `/cmd r mh|マッドホ|鹰` → description + `視界 x3、速度 x1`; `/set mh 1`; `/opt madhawk.vision 4` → `/show` `マッドホーク x1 x4`; `/opt madhawk.speed 0.5` → ` 速度x0.5` appended.
* H2 Assignment: log `… -> Mad Hawk (vanilla Crewmate -> Crewmate)`, `OptionsDesync: captured base options`; with WireLog a GameDataTo(6) to A right after dispatch, at +8 s and 1 s after the intro. A: crew intro, red `マッドホーク`, impostor red; B and the host see A plain.
* H3 **G2 vision**: A and B side by side (Skeld cafeteria) → A sees ~3× farther; lights sabotage → both shrink, A still wider; fix → back; meeting → after the exile screen A's vision is still ×3 within ~2 s.
* H4 **G2b speed**: `madhawk.speed 0.5` → A walks at half of B's speed; `/vset speed 0.5` + `madhawk.speed 0.5` → product clamped to 0.5 (A = B), nobody kicked, no `InvalidGameOptions` on the next CreateGame; `1.5` faster; `1` equal.
* H5 Host as Mad Hawk → host vision ×3 on Skeld and Airship, speed per option, own tag.
* H6 `Ⓜ` with `madmate.known on`; Sheriff per `sheriff.killmadmate`; `/cmd guess A madhawk` correct, `madmate` wrong. Regression: Lighter ×2 and SpeedBooster ×1.5 still work.

**W. Worshipper (B)** — `/set worshipper 1`.
* W1 (client Worshipper): `/opt worshipper.uses 2`, `/opt madmate.known on`, `/assign A worshipper`, `/assign B lighter`, `/assign <host> vampire`. 1 Log `… A -> Worshipper (…)`, `… Crewmate -> Worshipper [Impostor]`, B Lighter, host Vampire. 2 A: Impostor intro alone, kill button, red `崇拝者`, chat head + `※本体の表示…崇拝者です。` + description + `崇拝の残り回数: 2`; host and B look like Crewmates to A. 3 A vents → booted (`vent.blocked`), sabotages → `sabotage.blocked`. 4 **A presses on B**: B does not die, A's button restarts at 30 s; A's chat at once `B を崇拝しました。B はマッドメイトになりました（残り 1 回）。`; **~0.6 s later** B reads `崇拝者に崇拝され、あなたはマッドメイトになりました。…` then the Madmate text ~0.55 s after; B's vision shrinks to normal (`Resync`); B's screen: `マッドメイト` above its name, host red; A's screen: `Ⓜ` before B; host: `Ⓜ` before B, **`Ⓦ` before A**. Log `Game: #b B Lighter -> Madmate (worshipped by A)`, `Kills: Worshipper A worshipped B (Lighter -> Madmate, 1 left)`, `WinConditions(test): … madmate=1 …`. 5 A presses B again → failed kill, `B は崇拝できません。`, remaining 1. 6 Meeting: B's reminder = Madmate; A's ends `崇拝の残り回数: 1` / `崇拝した相手: B`. 7 A presses the host (Vampire) → A dies on the spot (self-kill animation, body), A's chat `<host> はインポスターでした…`, log `… → self-destruct`, A = impostor ghost. 8 (`uses 1` variant) after one worship a press on the host → failed kill + `崇拝の回数を使い切りました…`, A survives.
* W2 (host Worshipper): `/opt worshipper.uses 1`, `/opt madmate.known on`, `/opt sheriff.killmadmate on`, `/assign <host> worshipper`, `/assign A sheriff`, B unforced (vanilla Impostor). 1 Host: 30 s kill button, impostor vision, fake tasks, own tag, local role chat with the desync line; **B's screen: `Ⓦ` before the host's name (G5)**. 2 Host presses A (Sheriff) → failed kill + `A は崇拝できません。`. 3 **G4b**: A shoots the host → host dies normally (`Kills: Sheriff A shot <host>`); `sheriff.killmadmate off` → A misfires. 4 **G4**: host presses B (Impostor) → self-kill animation on the host, body on A / B, `B はインポスターでした…`, `… → self-destruct`, host ghost, game continues.
* W3 Commands: `/cmd r worshipper|ws|崇拝` → 3 messages incl. `崇拝 1 回、崇拝CD 30秒、シェリフが撃てる: オン`; `/opt worshipper.uses 3`, `/opt worshipper.cd 10`, `/show` `崇拝者 x1 崇拝3回 CD10秒`; `/cmd r sheriff` shows the updated description; emulator in en / zh → localized notices.

**J. Jackal Friends (D)** — items 1-5, 8-12 with `/test on`; 6-7 with `/test off` + `/start`.
* J1 `/assign A jackal`, `/assign B jackalfriends` (host promoted to vanilla Impostor). Log `… B -> Jackal Friends (vanilla … -> Crewmate)`, `… Crewmate -> Jackal Friends [Neutral]`, `WinConditions(test): outcome=Continue alive=3 imp=1 jackal=1 others=1 madmate=0 friends=1 crewForCount=0`. B: crew intro, blue `ジャッカルフレンズ` own tag, A blue, host white; A sees B white; no buttons on B.
* J2 Role chat: `ジャッカルフレンズ [第三陣営]` / description / `ジャッカル: A` (ja 2 messages, zh 1, en 3); `/cmd n` on B (en) = exactly 3 with `Jackal: A`; `/cmd r jf` → `ジャッカルに公開: off、シェリフに撃たれる: on`.
* J3 Tasks: B's tasks never count on the host (`1/0` phantom total in a 3-player game); the clients' own bars move (expected).
* J4 Meeting: A blue in B's list, B's row `B <size=70%>ジャッカルフレンズ</size>`; reminder `ジャッカル: A`. J4b: host kills A, B's next reminder `生きているジャッカルがいません。`, skip → WrapUp → nothing.
* J5 `/opt jackalfriends.known on` → reply `jackalfriends.known = on`, `/show` `ジャッカルフレンズ x1 ジャッカルに公開`; next game A sees B blue; mid-game toggle applies at the next refresh.
* J6 **Real game** (`/test off`, re-`/assign`, `/start`): 6a A kills the host → `Kills: Jackal A killed …`, `EndGame: Jackal solo=255 winners=[<A>,<B>] text=ジャッカル勝利`, summary `☆A: ジャッカル`, `☆B: ジャッカルフレンズ`, `×Host: インポスター`. 6b host kills A → `EndGame: Impostor …`, B without ☆. 6c (4th client C, or the `WinConditions(test)` line with `/test on`): A kills the Impostor → Jackal win at once (`friends=1 crewForCount=1`); control `/assign B none` → continues (`crewForCount=2`). 6d Impostor kills A → Impostor win at once; control continues.
* J7 Jackal exiled (`/test off`): vote A out → `AntiBlackout: host will end the game after this exile (exiled=<A>); nothing to do`, `EndGame: Impostor …` at WrapUp, B without ☆.
* J8 Sheriff: `/assign <host> sheriff`, `/assign B jackalfriends` → host shoots B → B dies (`Kills: Sheriff Host shot B`); `/opt jackalfriends.sheriff off` → host dies (`misfired on B`, `誤射！…`).
* J9 No Jackal: `/assign clear`, `/set jackal 0`, `/set jf 1` → host chat `ジャッカルフレンズはジャッカルが配られた試合だけ…`, log `RoleAssignment: Jackal Friends skipped (no Jackal this game)`; `/set jackal 1` → both drawn; `/opt jackal.chance 0` → log only.
* J10 Forced Friends without a Jackal → `Jackal Friends without a Jackal (forced by /assign)`, reminder `生きているジャッカルがいません。`. J11 A disconnects → nothing on B. J12 Settings: blue `Jackal Friends` header with 4 rows between ジャッカル and ラバーズ; Admin `/cmd opt jf.sheriff off` works.

**N. Evil Nekomata (C)** — every drag case forces all three roles.
* N1 `/set neko 1`, `/assign <host> neko`, `/assign A bait`, `/assign B madmate`: log `… -> Evil Nekomata (vanilla … -> Impostor)`, `[Impostor]`; host impostor intro + kill button; role chat `イビル猫又 [インポスター陣営] — …`; `/cmd r neko` → description + `道連れ: 投票者から 1 人、インポスター陣営を除外: on、全員に通知: on`; B (Madmate) sees the host red.
* N2 Normal kill / vent / sabotage: host kills A (normal), vents, sabotages (no blocked notices). `/assign A sheriff` (host neko) → A shoots the host → `Kills: Sheriff A shot <host>`. `/assign A neko`, `/assign <host> sheriff`, `/assign B bait` → A kills the host → `Kills: A killed the host (Sheriff)` (whitelist).
* N3 Drag: `/assign A neko`, `/assign <host> lighter`, `/assign B bait`; host + B vote A → log `EvilNekomata: A exiled → drags <host|B> (picked from 2 candidate(s), 2 voter(s)); dies 2.5 s after the exile screen`; the victim reads `追放された A の道連れになりました…` during the results; WrapUp log `EvilNekomata: drag on <victim> (by A) executes 2.5 s after the exile screen`; ~2 s after the screen everyone reads `<victim> は追放された A（イビル猫又）の道連れになりました。`; ~0.5 s later `Kills: nekomata on <victim> (by A) executes`, the victim drops (self-kill, body). Repeat until both the host and B were victims (host victim → host ghost, `/end` still works; B = Bait → no forced report).
* N4 Voter filter: `/assign B madmate` → `picked from 1 candidate(s), 2 voter(s)`, always the host; `/opt evilnekomata.excludeimp off` → B may be picked (`2 candidate(s)`). Mad family test: `/assign B madstuntman` (lives left) with `excludeimp off` → B never a candidate (shielded).
* N5 Nobody eligible: `/assign <host> madmate`, `/assign B madmate`, both vote A → `nobody to drag (voters=2, mode=voters)`, no chat, nobody dies.
* N6 Anyone mode: `/opt evilnekomata.voters off`, setup of N3, host + B vote A → `picked from 2 candidate(s), 0 voter(s)` (the voter loop is skipped by design); A's start chat contains `※この部屋の設定では…`. Reset.
* N7 Silent: `/opt evilnekomata.announce off` → only the victim's private line; the WrapUp `drag on …` line still appears; death at +2.5 s; no public line. Reset.
* N8 Mayor / Mad Mayor: `/assign <host> mayor` (or `madmayor`, votes 2) → `Meetings: Mayor tally → … votes=4` then `… 2 voter(s)` (duplicates collapsed).
* N9 Disconnect: B chosen as victim closes the app during the exile screen → no public line, no `executes` line, no error.
* N10 Exile ends the game (`/test off` first, then `/assign A neko`, `/assign <host> lighter`, `/assign B bait`, `/start`): host + B vote A → `EvilNekomata: A exiled, the exile ends the game → no drag`, no private line, `AntiBlackout: host will end the game after this exile …`, `EndGame: Crew …`.
* N11 Host Nekomata exiled, Announce on: `/assign <host> neko`, `/assign A lighter`, `/assign B bait`; A + B vote the host → the victim's private line during the results (no temp-revive yet); ~2 s after the screen both phones read the public line (temp-revive Data then the restore ~1 s later); the host ghost stays a ghost after the restore (brief "alive" ~+2 s = cosmetic); victim drops at +2.5 s; `/end` works.
* N12 **G9** curse + drag: `/assign A neko`, `/assign <host> witch`, `/assign B bait`, `excludeimp off`; host curses B; host + B vote A; when the host is picked → `Witch: curse strikes 1 player(s) 2 s after …` and `EvilNekomata: drag on <host> … 2.5 s …`, then `Kills: curse on B (by <host>) executes` and ~0.5 s later `Kills: nekomata on <host> (by A) executes`; both bodies, no kick. When B is picked → no `curse strikes` line (already dying), B dies as a drag at +2.5 s. Reset `excludeimp on`.
* N13 Options: Admin `/opt evilnekomata.voters off` accepted, `/show` ` 生存者全員から`; `/opt neko.count 2`, `/set neko 0`; the cfg gets `[EvilNekomata]`. N14 zh client sees zh notices.

**K. Serial Killer (C)** — `/opt serialkiller.time 20`, `/opt sk.cd 5`, lobby KillCooldown 45 s.
* K1 `/cmd r sk` → `シリアルキラー [インポスター陣営] — …` + `キルCD 5秒、自殺まで 20秒、会議でリセット: on`; `/show` `シリアルキラー x1 キルCD5秒 制限20秒`; settings header with 5 rows; `[SerialKiller]` in the cfg; Admin `/opt sk.reset off`; zh `连环杀手`.
* K2 **G8 (intro)**: `/assign E1 sk`, host + E2 crew (E1 = A). Impostor intro; log `TestMode: forced … -> Serial Killer`, ~1 s after the host's intro `SerialKiller: countdown starts 1.6 s after the intro-end hook (2 remote client(s))`, then `E1 timer 20 s (intro end)` + `kill timer set to 5 s (intro end)`; E1 sees a short shield flash and its button counting from **5 s** (not 45); own tag `シリアルキラー 20s` → `15s` → `10s` → `5s`; role chat ends `制限時間: 20秒（…）`; at 10 s left `has 10.0 s left` + chat `あと 10 秒以内に…`; at 0 `ran out of time → dies`, `Kills: serialkiller on E1 (by E1) executes`, self-kill + body, `時間切れ…`, impostor ghost; nothing on E2 / host chat; no `EndGame` line. `/end`.
* K3 Kill path: E1 kills E2 at ~12 s → `Kills: Serial Killer E1 killed E2`, `timer 20 s (kill)`, button restarts at 5 s by itself (no flash), tag `20s`; dies 20 s after the kill.
* K4 **G8 (meeting)**: emergency at ~5 s left; sit past 5 s → no log / death; skip; ~2 s after the exile screen `timer 20 s (meeting end, reset)`, `kill timer set to 5 s (meeting end)`, chat `残り 20 秒…`, flash, **button 5 s not 45**, tag `20s`; E1 kills E2 ~6 s after the screen and survives. `/opt sk.reset off` → `timer 10 s (meeting end, carried over)` (floor 5 + 5), death ~12 s after WrapUp without a kill.
* K5 Vent / report / exit kill: E1 in a vent across the deadline → `ran out of time → dies` but no body until leaving; leave next to E2 and kill within 1 s → `killed before its time-out death executed → cancelled`, `timer 20 s (kill)`. E2 reports while E1's bite is pending → E1 dies at the report; E1 reporting itself → dies 2 s after WrapUp, `is already dying → not re-armed after the meeting`, no `残り…` line.
* K6 Host SK: `/assign <host> sk` → `kill timer set to 5 s` (no flash), warnings in host chat, time-out → host self-kill, body on E1 / E2.
* K7 Desync host / Sheriff: host sheriff, E1 sk → E1 kills the host → `Kills: Serial Killer E1 killed <host>` (switch case); E2 sheriff shoots E1 → E1 dies, E2 lives.
* K8 Madmate / Mafia: E2 madmate → E1 red (Ⓜ with `madmate.known on`); E2 mafia → its kill fails while E1 lives.
* K9 **G9** two SKs: `/assign E1 sk`, `/assign E2 sk` → two `timer 20 s (intro end)` lines, two `kill timer set …` 0.75 s apart, no kick; both time out → consecutive-frame deaths, two bodies.
* K10 Disconnect: E1 leaves mid-countdown → no error, entry gone; leave during the exile screen → no `kill timer set` line for E1. K11 Full game (`/test off`, `SerialKiller.Count = 1`, 1 impostor) → that impostor is the SK; time-out → `クルー勝利`, summary `×E1:シリアルキラー`. K12 Lang: `roleinfo.serialkiller.limit` sits after `roleinfo.sep`; no `Lang: bad format` line.

**X. Samurai (D)** — every item `/assign`s all three players (Mayor / Lighter as neutral fillers); confirm exactly one `-> Samurai [Impostor]` in the log.
* X1 Build & tables: settings header 侍 with 6 rows; `/cmd r samurai|侍|武士|武|sam|sm` → description + `斬撃CD 45s、範囲 2、間隔 0.3秒、味方も斬る: オフ`; `/opt samurai.cooldown 0` → `斬撃CD キルと同じ`; `/opt samurai.range 3`, `/opt samurai.stagger 0.5`, `/opt samurai.teammates on` persist; `/opt samurai.stagger 0` → clamped reply; `/show` `侍 x1 斬撃CD45秒 範囲2`. Reset.
* X2 Host samurai, both in range: `/assign <host> samurai`, `/assign A mayor`, `/assign B lighter`; role chat ends `斬撃の範囲: 半径 2、味方のインポスターも斬る: オフ`; button 45 s; A and B adjacent, press on A → A normal kill, **B drops ~0.3 s later** (self-kill on its own screen); log order `Kills: Samurai H slashed A (range 2, lobby kill distance 1.8, cooldown 45s)` → `Kills: Samurai H's slash also hits 1 (0.3 s apart): B` → `Kills: slash on B (by H) executes`; host chat `A を斬りました。続けて倒れる 1 人: B`; cooldown restarts at 45 s.
* X3 Out of range (B ≥ 3 units away) → only A dies, no "also hits" line. X4 `samurai.stagger 1` → B falls ~1 s after A.
* X5 **G6** client samurai: `/assign A samurai`, `/assign <host> mayor`, `/assign B lighter`; A's timer starts at 45 s; A slashes B with the host adjacent → B dies, the host drops ~0.3 s later (`slash on H (by A) executes`), A reads the chat line; **A's timer must restart at 45 s by itself**. Repeat with `samurai.cooldown 0` (lobby value) and `samurai.range 0.5`.
* X6 Allies spared: Impostors = 2, `/assign A samurai`, `/assign B madmate`, `/assign <host> mayor` → B survives; `samurai.teammates on` → B dies too. B as vanilla impostor (`/assign B none`, verify `Impostor -> (none)`) → not targetable, survives by default.
* X7 Vent / ladder: B in a vent → survives; Airship ladder → survives; B enters a vent within 0.3 s after the press → `slash on B` only after leaving (postponed).
* X8 Bait target: `/assign B bait`, `/assign A samurai`, `/assign <host> mayor`; A slashes B with the host adjacent → `Kills: slash on H (by A) executes` **before** `Kills: Bait B died → A reports the body`; the meeting opens with B and the host dead, A reporter.
* X9 Report inside the window (`stagger 1`): A slashes the host with B in range; B reports within 1 s → B survives into the meeting, dies ~2 s after the exile screen. Reset.
* X10 **G7** host desync as target: `/assign <host> sheriff`, `/assign A samurai`, `/assign B lighter`; A presses the host → `Kills: Samurai A slashed H …`, host dies with the normal animation. X11 host desync as bystander: A slashes B with the host adjacent → `slash on H (by A) executes`, host dies.
* X12 Sheriff vs samurai: B sheriff shoots A → A dies. Madmate host sees A red; Snitch-done sees A red.
* X13 Lovers: `/assign B lovers`, `/assign <host> lovers`, `/assign A samurai`, `samurai.teammates on`; A slashes B with the host in range → `also hits 1 …: H`, **no** `Lovers: B died → H follows` line, `slash on H (by A) executes`; host out of range → `Lovers: B died → H follows in 0.5 s`, `lovers on H (by H) executes`. Witch chain: B witch cursed the host; A slashes B → `Witch: B died, 1 spell(s) cleared`.
* X14 Terrorist bystander (tasks done): A slashes the host with B in range → after B's bite `Kills: Terrorist B was killed with all tasks done → Terrorist win` → `EndGame(Terrorist) suppressed …` (test mode) / with `/test off` the game ends and no further `slash on …` line follows.
* X15 Meeting guard: press during a meeting → failed kill, no slash. X16 Real end (`/test off`, Impostors = 1, Samurai = 1): the samurai slashes both crew → target dies, bystander ~0.3 s later, **then** the impostor win screen (`EndGame: Impostor …` after the second `slash on … executes`); `/last` shows `侍` ☆ and × on both crew; variant: bystander steps into a vent right after the press → the win screen comes after ≤ 1.3 s with the venter alive (documented residual).
* X17 **G9**: after every multi-victim slash nobody is disconnected; with ≥ 3 victims at 0.3 s and a kick → `samurai.stagger 0.5`, report.
* X18 Stuntman pressed: `/assign B madstuntman`, `/assign A samurai`, `/assign <host> mayor`; A presses B with the host adjacent → nothing happens to B or the host, A's timer restarts (45 s), A reads `B はキルを耐えました（マッドスタントマン、次は死にます）。`, log `Kills: A (Samurai) hit Mad Stuntman B → guarded (0 left)`, **no** `slashed` line. Then A presses the host with B adjacent → host dies, B skipped (no `also hits`), B still `残り 0 回`? — no: B was not hit (skipped as shielded), its lives are unchanged at 0 from the first press; check with `lives 2` that a skipped bystander keeps its count.

**E. Evil Hawk (D)**
* E1 Lobby: `/cmd r evilhawk|eh|evil hawk|イビルホ|邪恶鹰` → description + `視界 x2`; `/cmd r hawk` → `cmd.badrole`; `/opt evilhawk.vision 3` and `/opt eh.vision 2`; `/set evil hawk 1` → `cmd.badrole` (positional, expected), `/set evilhawk 1` → `/show` `イビルホーク x1 x2`; settings header with 人数 / 確率 / 視界倍率.
* E2 `/assign A evilhawk` → `TestMode: forced #A … -> Evil Hawk (vanilla Crewmate -> Impostor)`, `Impostor -> Evil Hawk [Impostor]`, `OptionsDesync: captured base options`; A: impostor intro, kill / vent / sabotage buttons, `イビルホーク` own tag, role chat without the desync line; B / host see A plain.
* E3 **G3 vision**: (a) precondition `/assign A sheriff` → A's radius crew-sized (smaller than an impostor's) — if not, per-client `ImpostorLightMod` is not honoured at all (Sheriff known limitation too); (b) `/opt evilhawk.vision 5`, `/assign A evilhawk`, `/assign B vampire` (vanilla impostor with base options) on Skeld, both in Cafeteria → A's lit area visibly wider than B's (essentially the whole screen at ×5); lights sabotage → A unchanged; `/opt evilhawk.vision 1` → next game A = B (nothing sent; base back once if desynced). Back to 2. Record the outcome in the worklog.
* E4 Resync after a meeting: A's vision wide again within ~2 s after the exile screen (a sub-second dip is expected); WireLog: two GameDataTo → Data packets to A around WrapUp.
* E5 Kills: A kills B (normal); host Sheriff → A kills the host → `Kills: A killed the host (Sheriff)`; host Sheriff shoots A → `Kills: Sheriff … shot A`. Madmate B sees A red, `Ⓜ` for A with `madmate.known on`; B Mafia blocked while A lives.
* E6 Host as Evil Hawk: vision wider on Skeld, Airship and Fungle (regression, no fallback); real end (`/test off`, re-`/assign`): A kills B → `EndGame: Impostor solo=255 winners=[A]`, `☆A:イビルホーク`; the next vanilla-role game shows A's vision normal again (`RestoreAll`). Disconnect mid-game → no exceptions. Assassin: `/cmd guess A impostor` → 外れ (Assassin dies); `/cmd guess A eh` → 正解.

**C. Cross-role (all)**
* C1 Stuntman vs Serial Killer: `/assign A sk`, `/assign B madstuntman`, host crew; A presses B → guarded, A's cooldown reset to the SK cooldown, log `Kills: A (SerialKiller) hit Mad Stuntman B → guarded …` **and** `SerialKiller: A timer 20 s (kill)` (the guarded press restarts the countdown).
* C2 Stuntman vs Evil Hawk / Nekomata killer: `/assign A evilhawk` (or `neko`), `/assign B madstuntman` → guarded with `(EvilHawk)` / `(EvilNekomata)` in the log line; on an Airship ladder the guard steps aside (vanilla refuses).
* C3 Worshipper vs the batch: `/assign A worshipper`; targets `madmayor` / `madstuntman` / `madhawk` / `jackalfriends` / `evilhawk`: the first three → `… は崇拝できません。`; **Jackal Friends → converted** (`Game: #b B JackalFriends -> Madmate (worshipped by A)`, B's `ジャッカル: …` line gone, Madmate text); Evil Hawk → A self-destructs.
* C4 Jackal Friends counting with a Mad Mayor: `/assign A jackal`, `/assign B jackalfriends`, `/assign <host> madmayor` (no impostor → promotion warning is impossible since all forced; use 4 clients or read the `WinConditions(test)` line: `madmate=1 friends=1 crewForCount=0`).
* C5 Nekomata drag on a Serial Killer / Samurai victim → foreign bite; the SK's countdown entry is dropped at death (`SerialKiller` log silent), no re-arm at WrapUp (`is already dying`).
* C6 Samurai bystander = Mad Hawk / Mad Mayor (allies) → spared by default; `samurai.teammates on` → die as bystanders.
* C7 Mad Mayor + Evil Nekomata: the Mad Mayor's 3 icons count as one voter (`2 voter(s)`).
* C8 Evil Hawk + Sheriff + Mad family in one game: rules 2/3 per viewer (Sheriff sees plain names; Mad family sees the Evil Hawk red; Evil Hawk sees `Ⓜ`/`Ⓦ` with the options).

**R. Real-end runs (`/test off`, re-`/assign` after switching, `/start`)**: M-real (Mad Mayor's weighted vote exiles the host impostor → `AntiBlackout: host will end the game after this exile …`, `EndGame: Crew …`); S-real / M7-real (impostor kills the crew while a Mad Mayor / Stuntman lives → `EndGame: Impostor … winners=[host, mad]`, ☆ on both, no ☆ on the crew); H-real (Vampire + Mad Hawk + 2 crew: bite ends the game at once; control `/assign A none` continues); W-real (worship the last task holder → `EndGame: Crew …` at once — needs 4+ players and finished tasks; otherwise code review of `EvaluateBase` only); J6 / J7; N10; K11; X16; E6. Server health throughout: no "DC because Hacking" (the only new packet shapes are duplicated `VoterState` entries, paced self-kills 0.3–0.5 s apart, staggered `ResetKillCooldown` pairs, and the deferred dead-host chat).

---

## 13. Docs plan (docs agent; `README.md` ja master, mirror in `README.en.md` / `README.zh-CN.md`; all CRLF; CHANGELOG / release notes in Japanese)

### 13.1 Version strings and counts
* Replace every "current version" `0.4.1` string with `0.5.0`: ja lines 9, 29, 152 (×2), 330, 340 (×2), 529, 536, 1651, 1676; en 9, 29, 151, 327, 337, 526, 533, 1648, 1673; zh 9, 31, 153, 331, 341, 530, 537, 1652, 1677 (zip names `PocketRoles-Setup-0.5.0.zip` / `PocketRoles-0.5.0.zip`, `PocketRoles v0.5.0 loaded`, `[PocketRoles] 不具合報告 v0.5.0`). Leave the history mentions ("v0.4.1 から…", `### v0.4e（…`) untouched.
* Role count 17 → **26**: ja 9 (`シェリフやジャッカル、ジェスターなど 26 の役職が`), 12 (`**26 役職を 3 言語でこっそり通知**`), 91, 1409; en 9 (`Twenty-six roles`), 12, 90, 1406; zh 9, 12, 92, 1410.
* Role enumerations (ja 91 / en 90 / zh 92): `…マッドメイト、マッドメイヤー、マッドスタントマン、マッドホーク、崇拝者、ヴァンパイア、マフィア、魔女、アサシン、イビルホーク、イビル猫又、シリアルキラー、侍、ジェスター、オポチュニスト、テロリスト、ジャッカル、ジャッカルフレンズ、ラバーズ、放火魔` (en `…Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper, Vampire, Mafia, Witch, Assassin, Evil Hawk, Evil Nekomata, Serial Killer, Samurai, Jester, Opportunist, Terrorist, Jackal, Jackal Friends, Lovers, Arsonist`; zh `…内鬼狂粉、狂粉市长、疯狂特技演员、鹰眼狂粉、崇拜者、吸血鬼、黑手党、女巫、刺客、邪恶鹰眼、邪恶猫又、连环杀手、武士、小丑、投机者、恐怖分子、豺狼、豺狼之友、恋人、纵火犯`) — `Roles.All` order.

### 13.2 §10 役職一覧 table (insert in `Roles.All` order: four rows after マッドメイト, four after アサシン, one after ジャッカル)
After the Madmate row (ja 555 / en 552 / zh 556):
```
| マッドメイヤー (Mad Mayor / 狂粉市长) | インポスター | インポスター陣営のクルー。会議での投票が複数票として数えられます（投票アイコンも票数分表示されるので、他の人からはメイヤーに見えます）。インポスターの名前が赤く見えますがキルはできません。インポスターが勝つとあなたも勝ちです。タスクは進行に数えられず、インポスター勝利の人数判定でもクルーに数えられません。 | 票数 2、インポスターに公開: off |
| マッドスタントマン (Mad Stuntman / 疯狂特技演员) | インポスター | マッドメイトの仲間。インポスターの名前が赤く見えますがキルはできません。キルされても設定回数（既定 1 回）までは死なず、相手のクールダウンだけが戻ります。インポスター側のキラーには「マッドスタントマンがキルを耐えた（残り N 回）」、シェリフ・ジャッカルには「キルを耐えた」とだけチャットが届きます。本人への通知は設定（既定オフ。役職チャットにはいつも残り回数が出ます）。投票による追放・アサシンの推理は防げません。侍の斬撃に巻き込まれても回数を消費せずに無事です。シェリフはマッドメイトと同じ設定で撃てます。インポスターが勝つとあなたも勝ちです。タスクは進行に数えられず、人数判定でもクルーに数えられません。 | 耐えられる回数 1、本人に通知: off |
| マッドホーク (Mad Hawk / 鹰眼狂粉) | インポスター | 視界の広いマッドメイト。視界が通常の 3 倍（設定）になります。停電中も倍率はかかります（狭くなった視界の 3 倍）。インポスターの名前が赤く見えますがキルはできません。インポスターが勝つとあなたも勝ちです。タスクは進行に数えられず、人数判定でもクルーに数えられません。シェリフの「マッド系を撃てる」とインポスターへの公開（Ⓜ、`[Madmate] KnownToImpostors`）はマッドホークにもそのまま適用されます。 | 視界倍率 3、速度倍率 1 |
| 崇拝者 (Worshipper / 崇拜者) | インポスター | インポスター陣営のクルー。キルボタンが「崇拝」になり相手は死にません: 相手がクルー（通常役職のほか、メイヤー・スニッチ・ライター・スピードブースター・ベイト、ジェスター・オポチュニスト・テロリスト・ジャッカルフレンズも）ならその場で **マッドメイト** になります（本人にチャットで通知。回数制限）。相手がインポスター側のキラー（ヴァンパイア・マフィア・魔女・アサシン・イビルホーク・イビル猫又・シリアルキラー・侍・インポスター側のラバーズ含む）だと崇拝者が自爆します。マッド系役職・他の崇拝者・シェリフ・ジャッカル・放火魔・クルー側のラバーズには効きません（キル失敗の演出、回数は減りません）。回数を使い切った後はどの相手でもキル失敗の演出だけです（自爆もしません）。マッドメイトと違いインポスターが誰かは分かりません。本人のクライアントはインポスターとして動作（イントロ・偽タスク・インポスター視界）。ベント・サボタージュ不可。崇拝でクルーの人数と残りタスクが減るため、崇拝した瞬間にインポスター勝利（相手が最後の残りタスクを持っていた場合はクルー勝利）で終わることがあります。インポスター勝利で勝ち、人数判定ではクルーに数えません。シェリフは `CanKillMadmate` オンなら撃てます。`KnownToImpostors` オンならインポスターには名前の前に赤い **Ⓦ**（アサシンは `worshipper` で当てます）。 | 崇拝 1 回、崇拝CD 30 秒 |
```
en (after `| Madmate (マッドメイト / 内鬼狂粉) | …`):
```
| Mad Mayor (マッドメイヤー / 狂粉市长) | Impostor | A crewmate on the Impostor team whose vote counts as several votes (the vote icons show all of them, so to everyone else it looks like a Mayor). Sees Impostors in red but cannot kill. Wins with the Impostors. Tasks do not count, and it is not counted as crew in the Impostor win check. | 2 votes, known to impostors: off |
| Mad Stuntman (マッドスタントマン / 疯狂特技演员) | Impostor | A Madmate variant. Sees Impostors in red but cannot kill. Survives the first N kill attempts (default 1): the kill fails and only the killer's cooldown restarts. Impostor-side killers are told "Mad Stuntman survived (N left)" in chat, a Sheriff or Jackal only "the kill failed". Telling the stuntman itself is an option (default off; the role chat always shows the remaining count). A vote or the Assassin's guess still kills it; a Samurai's slash spares it without spending an attempt. The Sheriff may shoot it under the same setting as the Madmate. Wins with the Impostors; tasks do not count and it is not counted as crew in the win check. | kills survived 1, notify stuntman: off |
| Mad Hawk (マッドホーク / 鹰眼狂粉) | Impostor | A Madmate with wide vision: 3× the crew vision (option); the multiplier still applies during a blackout (3× the shrunken radius). Sees Impostors in red but cannot kill. Wins with the Impostors. Tasks do not count and the Mad Hawk is not counted as crew in the win check. The Sheriff's "can kill Mad roles" and the Ⓜ marker (`[Madmate] KnownToImpostors`) apply to it as well. | vision ×3, speed ×1 |
| Worshipper (崇拝者 / 崇拜者) | Impostor | A crewmate on the Impostor team. The kill button worships instead of killing: a crew target (plain crew, Mayor, Snitch, Lighter, Speed Booster, Bait, and also Jester, Opportunist, Terrorist, Jackal Friends) becomes a **Madmate** on the spot (told in chat; limited uses). Worshipping an Impostor-side killer (incl. Vampire, Mafia, Witch, Assassin, Evil Hawk, Evil Nekomata, Serial Killer, Samurai and an Impostor-side Lover) kills the Worshipper instead. Mad-type roles, other Worshippers, Sheriff, Jackal, Arsonist and a crew-side Lover cannot be worshipped (failed-kill animation, no use spent). After the last use every press is only a failed kill (never fatal). Unlike the Madmate it does not know who the Impostors are. Runs as Impostor on its own client (intro, fake tasks, impostor vision). No vents, no sabotage. A worship removes a crewmate and its remaining tasks, so the game may end at once: Impostors win by numbers, or the Crew wins by tasks when the target held the last unfinished ones. Wins with the Impostors and is not counted as crew. The Sheriff may shoot it when `CanKillMadmate` is on. With `KnownToImpostors` on, Impostors see a red **Ⓦ** before its name (the Assassin guesses it as `worshipper`). | 1 worship, worship cooldown 30 s |
```
zh (after `| 内鬼狂粉 (Madmate / マッドメイト) | …`):
```
| 狂粉市长 (Mad Mayor / マッドメイヤー) | 内鬼 | 内鬼阵营的船员，会议中的投票按多票计算（投票图标也会显示相应数量，在其他人看来就像市长）。内鬼的名字显示为红色，但不能击杀。内鬼获胜时你也获胜。任务不计入进度，在内鬼获胜的人数判定中也不算作船员。 | 票数 2，对内鬼公开：off |
| 疯狂特技演员 (Mad Stuntman / マッドスタントマン) | 内鬼 | 内鬼狂粉的变体。内鬼的名字显示为红色，但不能击杀。前 N 次（默认 1 次）被击杀不会死：击杀失败，只有击杀者的冷却重新开始。内鬼方的杀手会收到“疯狂特技演员挡下了击杀（还剩 N 次）”，警长、豺狼只会收到“击杀失败”。是否通知本人是设置项（默认关闭；职业聊天始终显示剩余次数）。投票放逐和刺客的猜测无法阻止；武士的斩击会跳过它且不消耗次数。警长可按与内鬼狂粉相同的设置射杀它。内鬼获胜时你也获胜；任务不计入进度，人数判定中也不算作船员。 | 可承受次数 1，通知本人：off |
| 鹰眼狂粉 (Mad Hawk / マッドホーク) | 内鬼 | 视野很大的内鬼狂粉：视野为普通船员的 3 倍（可设置），停电时倍率同样生效（缩小后视野的 3 倍）。内鬼的名字显示为红色，但不能击杀。内鬼获胜时你也获胜。任务不计入进度，人数判定中也不算作船员。警长的“可射杀狂粉系”和对内鬼公开（Ⓜ，`[Madmate] KnownToImpostors`）同样适用于鹰眼狂粉。 | 视野倍率 3、速度倍率 1 |
| 崇拜者 (Worshipper / 崇拝者) | 内鬼 | 内鬼阵营的船员。击杀键变为“崇拜”，对方不会死：对方是船员（普通职业，以及市长、告密者、点灯人、增速者、诱饵、小丑、投机者、恐怖分子、豺狼之友）时当场成为 **内鬼狂粉**（对方会收到聊天通知；次数有限）。崇拜内鬼方杀手（含吸血鬼、黑手党、女巫、刺客、邪恶鹰眼、邪恶猫又、连环杀手、武士、内鬼方的恋人）时崇拜者自爆身亡。狂粉系职业、其他崇拜者、警长、豺狼、纵火犯、船员方的恋人无法被崇拜（击杀失败的效果，不消耗次数）。次数用完后无论对谁都只是击杀失败的效果（不会自爆）。与内鬼狂粉不同，不知道谁是内鬼。自己的客户端以内鬼身份运行（开场、假任务、内鬼视野）。不能跳管、不能破坏。崇拜会减少船员人数和剩余任务，因此可能当场结束游戏：内鬼按人数获胜，或对方持有最后的未完成任务时船员按任务获胜。内鬼获胜时获胜，人数判定中不算作船员。`CanKillMadmate` 开启时警长可以射杀。`KnownToImpostors` 开启时内鬼会在其名字前看到红色 **Ⓦ**（刺客用 `worshipper` 猜测）。 | 崇拜 1 次，崇拜冷却 30 秒 |
```
After the Assassin row (ja 559 / en 556 / zh 560):
```
| イビルホーク (Evil Hawk / 邪恶鹰眼) | インポスター | インポスター枠。視界が通常のインポスターよりずっと広くなります（インポスターの視界 × 倍率、常時。停電の影響は通常のインポスターと同じく受けません）。キル・ベント・サボタージュは通常どおり。SNR の「ホークアイ」ボタンはバニラ参加者に作れないため常時効果です。 | 視界 ×2.0 |
| イビル猫又 (Evil Nekomata / 邪恶猫又) | インポスター | インポスター枠。通常どおりキル・ベント・サボタージュができます。会議で追放されると、自分に投票した人の中からランダムで 1 人を道連れにします（追放画面の約 2.5 秒後にその場で倒れます）。投票者がいない（全員インポスター陣営・死亡・切断）時は誰も死にません。追放で試合が終わる時は道連れは起きません（本人への通知もありません）。 | 道連れは投票者から: on、インポスター陣営を除外: on、全員に通知: on |
| シリアルキラー (Serial Killer / 连环杀手) | インポスター | インポスター枠（SuperNewRoles 準拠）。キルクールダウンが短い代わりに、前のキルから設定秒数キルしないとその場で自滅します（自分で自分をキルする演出、死体あり）。タイマーはキルのたびにリセット、会議中は停止、既定では会議後にリセット。残り時間は自分の名前タグに 5 秒刻みで表示、残り 10 秒（制限が 20 秒未満なら半分）で本人にだけ警告チャット。ベント・サボタージュ可。 | キルCD 10 秒、自殺まで 30 秒、会議でリセット: on |
| 侍 (Samurai / 武士) | インポスター | インポスター枠。キルが「斬撃」になり、キルした相手に続いて、その瞬間に侍の周囲（設定の半径）にいた人も約 0.3 秒間隔で次々にその場で倒れます（既定では仲間のインポスター・マッド系役職は巻き込みません。ベントの中・はしご・動く床の上・守護天使に守られている人・回数の残っているマッドスタントマンも無事）。巻き込まれた人は自分で自分をキルする演出で倒れ、侍は動きません。クールダウンは長め。ベント・サボタージュ可。 | 斬撃CD 45 秒（0 = キルCD）、範囲 2.0、間隔 0.3 秒、味方も斬る: off |
```
en / zh rows for the four impostor-pool roles: the texts of the role specs (§10 / §7 §13 / §8 §4 / §9 S.12) with the Samurai's exclusion list extended by "or a Mad Stuntman with attempts left" / "或尚有次数的疯狂特技演员" and the Evil Nekomata's "impostor-team" reading "内鬼阵营（含狂粉系职业）". After the Jackal row (ja 563 / en 560 / zh 564):
```
| ジャッカルフレンズ (Jackal Friends / 豺狼之友) | 第三陣営 | ジャッカル陣営のクルー（マッドメイトのジャッカル版）。ジャッカルの名前が青く見えますがキルはできません。ジャッカルが勝つとあなたも勝ちです（死んでいても）。ジャッカルが配られた試合にだけ配られます。タスクは進行に数えられず、人数判定でもクルーに数えられません。ジャッカルからは分かりません（設定でジャッカルにも青く見せられます）。崇拝者に崇拝されるとマッドメイトになります。 | ジャッカルに公開: off、シェリフに撃たれる: on |
| Jackal Friends (ジャッカルフレンズ / 豺狼之友) | Neutral | A crewmate on the Jackal's side (the Jackal's Madmate). Sees the Jackal in blue but cannot kill. Wins with the Jackal (even when dead). Only assigned in games that have a Jackal. Tasks do not count, and the Friends are not counted as crew in the win checks. The Jackal does not know them (a setting shows them to the Jackal in blue). A Worshipper can turn one into a Madmate. | known to Jackal: off, Sheriff can shoot: on |
| 豺狼之友 (Jackal Friends / ジャッカルフレンズ) | 中立 | 豺狼阵营的船员（豺狼版的内鬼狂粉）。豺狼的名字显示为蓝色，但不能击杀。豺狼获胜时你也获胜（即使已死亡）。只有本局分配了豺狼时才会分配。任务不计入进度，在人数判定中也不算作船员。豺狼不知道你是谁（可通过设置让豺狼看到蓝色名字）。被崇拜者崇拜后会变成内鬼狂粉。 | 对豺狼公开：off，可被警长击杀：on |
```
Existing rows to edit:
* Sheriff (ja 549 / en 546 / zh 550): ja `キルボタンでインポスター（ヴァンパイア・マフィア・魔女・アサシン・イビルホーク・イビル猫又・シリアルキラー・侍含む）とジャッカル、放火魔を撃てます。マッド系役職（マッドメイト・マッドメイヤー・マッドスタントマン・マッドホーク・崇拝者）とジャッカルフレンズは設定次第（既定: 撃てる）。撃てない相手（…）を撃つと自分が死にます。…` with the settings cell `キルCD 30 秒、マッド系を撃てる: on`; en `…to shoot Impostors (incl. Vampire, Mafia, Witch, Assassin, Evil Hawk, Evil Nekomata, Serial Killer and Samurai), the Jackal and the Arsonist. Mad-type roles (Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper) and Jackal Friends by setting (default: can). …` / `Kill cooldown 30 s, can kill Mad roles: on`; zh `…射杀内鬼（含吸血鬼、黑手党、女巫、刺客、邪恶鹰眼、邪恶猫又、连环杀手、武士）、豺狼和纵火犯。狂粉系职业（内鬼狂粉、狂粉市长、疯狂特技演员、鹰眼狂粉、崇拜者）和豺狼之友取决于设置（默认可击杀）。…` / `击杀冷却 30 秒，可射杀狂粉系：on`.
* Madmate settings cell: `インポスターに公開（マッドスタントマン・マッドホーク・崇拝者にも適用。崇拝者は Ⓦ）: off` / `known to impostors (also Mad Stuntman / Mad Hawk / Worshipper; Ⓦ for the Worshipper): off` / `对内鬼公开（也适用于疯狂特技演员、鹰眼狂粉、崇拜者；崇拜者为 Ⓦ）：off`.
* Assassin row: append ja 「ヴァンパイア・マフィア・魔女・イビルホーク・イビル猫又・シリアルキラー・侍などインポスター枠の役職は `impostor` ではなく役職名で、マッド系役職・崇拝者・ジャッカルフレンズも役職名で当てる必要があります（`madmate` は崇拝者に外れ。インポスターには崇拝者が Ⓦ で見えます）。」; en "Impostor-slot roles (Vampire, Mafia, Witch, Evil Hawk, Evil Nekomata, Serial Killer, Samurai …) must be guessed by their role name, not as `impostor`; Mad-type roles, the Worshipper and Jackal Friends by their exact name too (`madmate` misses a Worshipper; Impostors see it as Ⓦ)."; zh 「吸血鬼、黑手党、女巫、邪恶鹰眼、邪恶猫又、连环杀手、武士等内鬼名额职业必须用职业名猜，不能猜 `impostor`；狂粉系职业、崇拜者、豺狼之友也要用准确的职业名（`madmate` 对崇拜者算错；内鬼看到的是 Ⓦ）。」

### 13.3 Alias sentence (replace the whole line ja 567 / en 564 / zh 568)
* ja: ``チャットの `/cmd r` で一覧、`/cmd r <役職名>` で説明と現在の設定が見られます。役職名は英語名、日本語名、中国語名、短縮形（`sh`、`my`、`sn`、`lt`、`sb`、`bt`、`mad`、`mmy`、`stunt`、`mh`、`ws`、`vamp`、`mf`、`wt`、`as`、`eh`、`neko`、`sk`、`sam`、`js`、`opp`、`tr`、`jk`、`jf`、`lv`、`ars` など）のどれでも指定できます。日本語名・中国語名は先頭の数文字だけでも通ります（`シェリ`、`警`）。同じ文字で始まる役職は役職表で先にある方が優先です: `マッド`・`マッドメイ` はマッドメイト（マッドメイヤーは `マッドメイヤ`、マッドスタントマンは `マッドス`、マッドホークは `マッドホ`）、`ジャ`〜`ジャッカル` はジャッカル（ジャッカルフレンズは `ジャッカルフ`）、`イビ`・`イビル`・`邪`・`邪恶` はイビルホーク（イビル猫又は `イビル猫` / `邪恶猫`。確実なのは `イビルホ` / `邪恶鹰`）、`豺` はジャッカル（豺狼之友は `豺狼之`）。「侍」は 1 文字なので `侍` そのもの（または `武` / `武士`）で指定してください。アサシンの `/cmd guess` も同じ解釈です。``
* en: ``Type `/cmd r` in chat for the list and `/cmd r <role>` for a role's description and current settings. A role can be named by its English, Japanese or Chinese name or an alias (`sh`, `my`, `sn`, `lt`, `sb`, `bt`, `mad`, `mmy`, `stunt`, `mh`, `ws`, `vamp`, `mf`, `wt`, `as`, `eh`, `neko`, `sk`, `sam`, `js`, `opp`, `tr`, `jk`, `jf`, `lv`, `ars` …). The first characters of a Japanese / Chinese name are enough (`シェリ`, `警`); for names sharing a prefix the role listed first in the table wins: `マッド` / `マッドメイ` = Madmate (`マッドメイヤ` = Mad Mayor, `マッドス` = Mad Stuntman, `マッドホ` = Mad Hawk), `ジャ`…`ジャッカル` = Jackal (`ジャッカルフ` = Jackal Friends), `イビ` / `イビル` / `邪` / `邪恶` = Evil Hawk (`イビル猫` / `邪恶猫` = Evil Nekomata; `イビルホ` / `邪恶鹰` are always safe), `豺` = Jackal (`豺狼之` = Jackal Friends). 侍 is a single character, so type `侍` itself (or `武` / `武士`). The Assassin's `/cmd guess` parses names the same way.``
* zh: ``在聊天中输入 `/cmd r` 查看列表，`/cmd r <职业名>` 查看说明和当前设置。职业名可用英文名、日文名、中文名或缩写（`sh`、`my`、`sn`、`lt`、`sb`、`bt`、`mad`、`mmy`、`stunt`、`mh`、`ws`、`vamp`、`mf`、`wt`、`as`、`eh`、`neko`、`sk`、`sam`、`js`、`opp`、`tr`、`jk`、`jf`、`lv`、`ars` 等）中的任意一种。日文名、中文名只输入开头几个字也可以（`シェリ`、`警`）；开头相同的职业以职业表中靠前的为准：`マッド`・`マッドメイ` 为内鬼狂粉（狂粉市长请用 `マッドメイヤ` 或 `狂`，疯狂特技演员 `マッドス` / `疯`，鹰眼狂粉 `マッドホ` / `鹰`），`ジャ`〜`ジャッカル` 和 `豺` 为豺狼（豺狼之友请用 `ジャッカルフ` / `豺狼之`），`イビ`・`イビル`・`邪`・`邪恶` 为邪恶鹰眼（邪恶猫又请用 `イビル猫` / `邪恶猫`；`イビルホ` / `邪恶鹰` 一定不会有歧义）。“侍”只有一个字，请直接输入 `侍`（或 `武` / `武士`）。刺客的 `/cmd guess` 同样如此。``

### 13.4 Win table (ja 573-582 / en 570-579 / zh 574-583)
* Crew-win row: add イビルホーク、イビル猫又、シリアルキラー、侍 after アサシン (en `Evil Hawk, Evil Nekomata, Serial Killer, Samurai`; zh `邪恶鹰眼、邪恶猫又、连环杀手、武士`).
* Impostor-count row: `（マッドメイトを除く） | インポスター勝利（マッドメイトも勝ち）` → `（マッド系役職・ジャッカルフレンズを除く） | インポスター勝利（マッド系役職と崇拝された人も勝ち）`; en `(Madmates excluded) | Impostors win (Madmates too)` → `(Mad-type roles and Jackal Friends excluded) | Impostors win (Mad-type roles and worshipped players too)`; zh `（不含内鬼狂粉） | 内鬼获胜（内鬼狂粉也获胜）` → `（不含狂粉系职业、豺狼之友） | 内鬼获胜（狂粉系职业和被崇拜的人也获胜）`.
* Jackal-count row: `（マッドメイトを除く） | ジャッカル勝利` → `（マッド系役職・ジャッカルフレンズを除く） | ジャッカル勝利（ジャッカルフレンズも勝ち）`; en `(Madmates excluded) | Jackal wins` → `(Mad-type roles and Jackal Friends excluded) | Jackal wins (Jackal Friends too)`; zh → `（不含狂粉系职业、豺狼之友） | 豺狼获胜（豺狼之友也获胜）`.
* Paragraph after the table (ja 584): append `崇拝者の崇拝は相手をマッドメイトにするので、崇拝した瞬間にこの表の判定（人数、またはタスク完了）で終わることがあります。` (en `A Worshipper's worship turns the target into a Madmate, so the game can end on the spot by the rules above (numbers, or tasks completed).`; zh `崇拜者的崇拜会把对方变成内鬼狂粉，因此崇拜的瞬间就可能按上表判定结束（人数或任务完成）。`).

### 13.5 §8 settings-tab bullet (ja 464 / en 461 / zh 465; also catch up the v0.4.1 roles)
Append after `マッドメイト: インポスターに公開`: `、マッドメイヤー: 票数・インポスターに公開、マッドスタントマン: 耐えられるキル回数・本人に通知、マッドホーク: 視界倍率・速度倍率、崇拝者: 崇拝回数・崇拝のクールダウン、ジャッカルフレンズ: ジャッカルに公開・シェリフに撃たれる、イビルホーク: 視界倍率、イビル猫又: 道連れは投票者から・インポスター陣営を除外・全員に通知、シリアルキラー: キルクールダウン・自殺までの時間・会議でタイマーをリセット、侍: 斬撃のクールダウン・範囲・倒れる間隔・味方も斬る、ラバーズ: インポスターも恋人になる・残り3人で勝利、放火魔: 油のクールダウン・ベント使用、魔女: 呪いのクールダウン・呪われた本人に印、アサシン: 会議ごとの推理回数・初回会議でも推理可` and `シェリフ: キルクールダウン・マッドメイトを撃てる` → `シェリフ: キルクールダウン・マッド系を撃てる` (en / zh equivalents with the §1.4 label texts).

### 13.6 §11 `/opt` table — after the `assassin.firstmeeting` row (ja 719 / en 716 / zh 720)
```
| `madmayor.votes` | 1〜5 | `[MadMayor] Votes` |
| `madmayor.known` | on / off | `[MadMayor] KnownToImpostors` |
| `madstuntman.lives` | 1〜10 | `[MadStuntman] Lives` |
| `madstuntman.notify` | on / off | `[MadStuntman] NotifyStuntman` |
| `madhawk.vision` | 1〜5 | `[MadHawk] VisionMultiplier` |
| `madhawk.speed` | 0.5〜1.5 | `[MadHawk] SpeedMultiplier` |
| `worshipper.uses` | 1〜5 | `[Worshipper] Uses` |
| `worshipper.cooldown` | 2.5〜180 | `[Worshipper] Cooldown` |
| `jackalfriends.known` | on / off | `[JackalFriends] KnownToJackal` |
| `jackalfriends.sheriff` | on / off | `[JackalFriends] SheriffCanKill` |
| `evilhawk.vision` | 1〜5 | `[EvilHawk] VisionMultiplier` |
| `evilnekomata.voters` | on / off | `[EvilNekomata] VotersOnly` |
| `evilnekomata.excludeimp` | on / off | `[EvilNekomata] ExcludeImpostors` |
| `evilnekomata.announce` | on / off | `[EvilNekomata] Announce` |
| `serialkiller.cooldown` | 1〜60 | `[SerialKiller] KillCooldown` |
| `serialkiller.time` | 10〜300 | `[SerialKiller] SuicideTime` |
| `serialkiller.meetingreset` | on / off | `[SerialKiller] ResetAtMeeting` |
| `samurai.cooldown` | 0〜180（0 = キルCD） | `[Samurai] KillCooldown` |
| `samurai.range` | 0.5〜5 | `[Samurai] Range` |
| `samurai.stagger` | 0.1〜1 | `[Samurai] Stagger` |
| `samurai.teammates` | on / off | `[Samurai] HitTeammates` |
```
(en ranges with en dashes `1–5` …, `0–180 (0 = kill cooldown)`; zh `0〜180（0 = 击杀冷却）`.) §11 `guess` row (ja 614 / en 611 / zh 615): append `役職名の解釈は /cmd r と同じ（日本語名の先頭一致は役職表で先にある役職が優先。役職名は最後の 1 語だけが読まれるので `mad stuntman` と分けて書くと相手の名前と解釈されます）` (en `Role names are parsed like /cmd r (a Japanese prefix picks the first matching role in the table; only the last word is read as the role, so a spaced "mad stuntman" is taken as part of the player's name)`; zh likewise).

### 13.7 §12 config sample
`[Roles]` block: after `Madmate.Chance = 100` add `MadMayor.Count = 0` / `MadMayor.Chance = 100` / `MadStuntman.Count = 0` / `MadStuntman.Chance = 100` / `MadHawk.Count = 0` / `MadHawk.Chance = 100` / `Worshipper.Count = 0` / `Worshipper.Chance = 100`; after `Assassin.Chance = 100` add `EvilHawk.Count = 0` / `EvilHawk.Chance = 100` / `EvilNekomata.Count = 0` / `EvilNekomata.Chance = 100` / `SerialKiller.Count = 0` / `SerialKiller.Chance = 100` / `Samurai.Count = 0` / `Samurai.Chance = 100`; after `Jackal.Chance = 100` add `JackalFriends.Count = 0` / `JackalFriends.Chance = 100`. Comments of the two reused keys: `[Sheriff] CanKillMadmate = true` → `# マッド系役職（マッドメイト・マッドメイヤー・マッドスタントマン・マッドホーク・崇拝者）を撃っても死なない`; `[Madmate] KnownToImpostors = false` → `# インポスターがマッド系役職を見分けられる（Ⓜ。崇拝者は Ⓦ。マッドメイヤーは別設定）`. New sections (ja; comment column at character 33; en / zh comments from the role specs), after the `[Madmate]` section:
```
[MadMayor]
Votes = 2                       # 票数（1〜5）
KnownToImpostors = false        # インポスターがマッドメイヤーを見分けられる

[MadStuntman]
Lives = 1                       # キルを耐えられる回数（1〜10。投票による追放は防げません）
NotifyStuntman = false          # 耐えたことと残り回数を本人にチャットで知らせる（既定オフ。キルした側にはいつも知らせる）

[MadHawk]
VisionMultiplier = 3            # 視界の倍率（1〜5。停電中も倍率がかかる）
SpeedMultiplier = 1             # 移動速度の倍率（0.5〜1.5。1 = 通常。結果は 0.5〜3 に収める）

[Worshipper]
Uses = 1                        # 1 試合に崇拝できる回数（1〜5。成功した分だけ数える）
Cooldown = 30                   # 崇拝の間隔の秒数（2.5〜180）
```
and after the `[Assassin]` section (before the closing fence):
```
[EvilHawk]
VisionMultiplier = 2            # インポスターの視界に掛ける倍率（1〜5。常時有効）

[EvilNekomata]
VotersOnly = true               # 道連れは自分に投票した人から選ぶ（false = 生存者全員から）
ExcludeImpostors = true         # インポスター陣営（マッド系役職含む）は道連れにしない
Announce = true                 # 道連れを全員にチャットで知らせる（false = 本人だけ）

[SerialKiller]
KillCooldown = 10               # シリアルキラーのキルクールダウン（秒、1〜60）
SuicideTime = 30                # 前のキルからこの秒数キルしないと自滅（10〜300。会議中は停止、キルCD+5 秒未満には下がらない）
ResetAtMeeting = true           # 会議が終わるたびにタイマーを最初から（false = 残り時間を引き継ぐ）

[Samurai]
KillCooldown = 45               # 斬撃の間隔の秒数（0〜180。0 = ロビーのキルクールダウン。2.5 以上推奨）
Range = 2                       # 斬撃の半径（0.5〜5。バニラのキル距離はおよそ 短1 / 中1.8 / 長2.5）
Stagger = 0.3                   # 巻き込んだ人が倒れる間隔の秒数（0.1〜1。0.3 = 公式サーバーの送信間隔）
HitTeammates = false            # 範囲内の仲間（インポスター・マッド系役職）も斬る

[JackalFriends]
KnownToJackal = false           # ジャッカルがジャッカルフレンズを見分けられる（青い名前）
SheriffCanKill = true           # シェリフはジャッカルフレンズを撃っても死なない
```

### 13.8 Other README places (each in ja / en / zh)
* §1 desync sentence (ja 146 / en 145 / zh 147): `シェリフ・ジャッカル・放火魔・崇拝者`.
* §23 `/assign` note (ja 1351 / en 1348 / zh 1352): `インポスター枠の役職（ヴァンパイア・マフィア・魔女・アサシン・イビルホーク・イビル猫又・シリアルキラー・侍）`; note that `/test off` clears forced roles.
* §26 イントロ table: desync row `| シェリフ、ジャッカル、放火魔、崇拝者 | …`; impostor row `| ヴァンパイア、マフィア、魔女、アサシン、イビルホーク、イビル猫又、シリアルキラー、侍 | 通常のインポスターのイントロ… |`; crew row `| マッドメイト、マッドメイヤー、マッドスタントマン、マッドホーク、ジャッカルフレンズ、ジェスター、… |`; the "仲間として表示されず" row adds 崇拝者. タスク table: desync row adds 崇拝者; fake-task row `| ジェスター、オポチュニスト、マッドメイト、マッドメイヤー、マッドスタントマン、マッドホーク、ジャッカルフレンズ、ラバーズ | …|`.
* §26 キルの演出 table — insert before `| 試合終了処理中のキル | 失敗扱い |` the five rows of the role specs: マッドスタントマンへのキル (§3 spec §7 text, with "侍の斬撃の巻き込みは回数を消費せずに無事" added), 崇拝者の崇拝 (Worshipper spec §4.5), 侍の斬撃 (Samurai spec S.12-9), イビル猫又の道連れ (Nekomata spec §13), シリアルキラーの自滅 (SK spec §4) — ja / en / zh each, verbatim from those specs.
* §26 ベント・サボタージュ bullet: `シェリフ・崇拝者（ベント不可、サボ不可）`, `マッドメイト・マッドメイヤー・マッドスタントマン・マッドホーク・ジャッカルフレンズ（バニラのクルーと同じ）`, `インポスターのシェリフ・ジャッカル・放火魔・崇拝者には…`.
* §26 名前タグ table: after the first row add `| シリアルキラー本人 | 役職名の横に残り時間が 5 秒刻みで赤く表示（例: 「シリアルキラー 20s」。会議中は表示しません） |`; Madmate row → `| マッドメイト、マッドメイヤー、マッドスタントマン、マッドホーク（崇拝された人も） | インポスター（ヴァンパイア・マフィア・魔女・アサシン・イビルホーク・イビル猫又・シリアルキラー・侍含む）の名前が赤 |`; Ⓜ row → `| インポスター（`KnownToImpostors = true` の時） | マッド系役職（崇拝された人も含む）の名前の前に赤い **Ⓜ**、崇拝者の名前の前に赤い **Ⓦ**（マッドメイヤーは `[MadMayor] KnownToImpostors`） |`; before `| 通常役職 | …|` add `| 崇拝者 | 崇拝した相手の名前の前に赤い **Ⓜ**（インポスターは赤く見えません） |`, `| ジャッカルフレンズ | ジャッカルの名前が青 |`, `| ジャッカル（`KnownToJackal = true` の時） | ジャッカルフレンズの名前が青 |`.
* §26 チャット bullet (ja 1529): add 崇拝者 to the desync trio. §26 会議 bullets: Mayor bullet → `メイヤー・マッドメイヤーの投票は、開票時に **投票アイコンが票数分** 並びます（マッドメイヤーは他の人からメイヤーに見えます）。`; death-timing bullet → `魔女に呪われた人とラバーズの後追いは追放画面の約 2 秒後に、イビル猫又の道連れはその約 0.5 秒後（追放画面の約 2.5 秒後）に死にます。`; new bullet on Assassin guessing: `アサシンがマッドスタントマンを当てるには `madstuntman` / `stunt` / `madstunt` / `ms` / `マッドスタントマン` / `疯狂特技演员`（`疯` でも可）と入力します。`madmate` / `mad` / `マッド` はマッドメイトの指定になり、外れ扱いでアサシンが死にます。`.
* §27: intro bullet (ja 1588) adds 崇拝者; font caveat (ja 1597) adds `Ⓦ（崇拝者）` and `Worshipper.ImpostorViewMark`; pool bullet (ja 1614) adds `・イビルホーク・イビル猫又・シリアルキラー・侍`; per-client options bullet (ja 1616) adds `・マッドホーク・崇拝者・シリアルキラー・侍（斬撃CD 設定時）・イビルホーク` and `（シリアルキラーはイントロ後と会議後にキルボタンを直接合わせ直すので影響しません）`; Jackal bullet adds 「ジャッカルフレンズも名前タグの色だけで見分けます（`KnownToJackal` オン時）」; new bullets: Stuntman (button-only guard; Sheriff / Jackal inference; 1 s cooldown after a guard in sub-second lobbies), Samurai (host-side positions / edge; stagger + report survival; 0 s cooldown window), Worshipper (instant end; failed-press leak), Nekomata (host-exiled announce timing).
* Docs that need no rows: `DESIGN*.md`, `TECH-NOTES.md`, `ROADMAP.md`, issue templates, FAQ / reply guides (none mention v0.4.1 roles either).

### 13.9 CHANGELOG / release notes / lang deployment
* `CHANGELOG.md`: the §1.1 block (implementers write it with the code).
* `support/release-notes-v0.5.0.md` (LF), shape of `release-notes-v0.4.1.md`: `# GitHub Release 本文（v0.5.0）`; tag `v0.5.0`, title `PocketRoles v0.5.0 (Among Us 2026.8.18)`, attachments `dist\PocketRoles-Setup-0.5.0.zip`, `dist\PocketRoles-0.5.0.zip`, `dist\SHA256SUMS.txt`; `## PocketRoles v0.5.0 — 役職 9 つ追加（薄い陣営向け: マッド系 4・ジャッカル側 1・インポスター枠 4）`; pitch (`26 役職`); README links; `### v0.5.0 の新しい役職` + `すべて既定では **オフ**（人数 0）です。…` + table `| 役職 | 陣営 | どんな役職か | 既定の設定 |` with nine rows: `| **マッドメイヤー**（Mad Mayor / 狂粉市长） | インポスター陣営（クルー枠） | 会議の投票が複数票分。他の人からはメイヤーに見える | `MadMayor.Count = 0`、`Votes = 2`、`KnownToImpostors = false` |`, `| **マッドスタントマン**（Mad Stuntman / 疯狂特技演员） | インポスター陣営（クルー枠） | キルされても設定回数までは死なず、キルした側はクールダウンだけが戻る（追放・推理は防げない） | `MadStuntman.Count = 0`、`Lives = 1`、`NotifyStuntman = false` |`, `| **マッドホーク**（Mad Hawk / 鹰眼狂粉） | インポスター陣営（クルー枠） | 視界が広いマッドメイト（常時） | `MadHawk.Count = 0`、`VisionMultiplier = 3`、`SpeedMultiplier = 1` |`, `| **崇拝者**（Worshipper / 崇拜者） | インポスター陣営（クルー枠） | キルボタンで相手を崇拝してマッドメイトにする。インポスターを崇拝すると自爆、回数切れ後は失敗だけ | `Worshipper.Count = 0`、`Uses = 1`、`Cooldown = 30` |`, `| **ジャッカルフレンズ**（Jackal Friends / 豺狼之友） | 第三陣営（ジャッカル側） | ジャッカルの名前が青く見え、ジャッカルが勝つと勝つ。ジャッカルが配られた試合だけ | `JackalFriends.Count = 0`、`KnownToJackal = false`、`SheriffCanKill = true` |`, `| **イビルホーク**（Evil Hawk / 邪恶鹰眼） | インポスター枠 | 視界が通常のインポスターよりずっと広い（常時、倍率設定。SNR のホークアイボタンはバニラ参加者には作れないため常時）。アサシンは `evilhawk` / `eh` で当てる（`impostor` は外れ） | `EvilHawk.Count = 0`、`VisionMultiplier = 2` |`, `| **イビル猫又**（Evil Nekomata / 邪恶猫又） | インポスター枠 | 通常どおりキルでき、追放されると自分に投票した人から 1 人を道連れにします（追放画面の約 2.5 秒後に死亡） | `EvilNekomata.Count = 0`、`VotersOnly = true`、`ExcludeImpostors = true`、`Announce = true` |`, `| **シリアルキラー**（Serial Killer / 连环杀手） | インポスター枠 | キルクールダウンが短い代わりに、前のキルから設定秒数キルしないとその場で自滅（キルでリセット、会議中は停止、既定で会議後にリセット。残り時間は名前タグに表示） | `SerialKiller.Count = 0`、`KillCooldown = 10`、`SuicideTime = 30`、`ResetAtMeeting = true` |`, `| **侍**（Samurai / 武士） | インポスター枠 | キルが「斬撃」になり、キルした相手に続いてその瞬間に周囲にいた人が約 0.3 秒間隔で次々に死にます（既定では仲間は無事） | `Samurai.Count = 0`、`KillCooldown = 45`（0 = キルクールダウン）、`Range = 2`、`Stagger = 0.3`、`HitTeammates = false` |`; bullets: the Mad-family common rules (`CanKillMadmate` / `KnownToImpostors` / 人数判定), the Sheriff description update, the `/opt` keys and aliases (`mmy stunt mh ws jf eh neko sk sam`), **「`lang\*.json` を上書きコピーしてください（設定の説明文とシェリフの説明文が変わったため。ランチャー更新では自動）」**, the shield-flash note for the Serial Killer, the `Ⓦ` font caveat; `変更履歴の詳細は `CHANGELOG.md`。`; `### 更新のしかた（v0.4.3 から）`; `### ダウンロード` (table + `<SHA256 …>` placeholders); `### 既知の制限（v0.5.0）` (the §12 gate outcomes and the documented residuals: sub-second vision dips after a meeting, a bystander that vents during a slash may survive the end, a Stuntman is inferable by a Sheriff / Jackal, the host briefly "alive" ~2 s after its own exile as a Nekomata); `### 困ったら`; `### 免責`; `## 简体中文（简版）` (4 bullets: 更新 / 安装 / 注意 incl. 覆盖 `lang\*.json` / 说明书); `## English (short)` (`v0.5.0 adds nine roles (26 in total): **Mad Mayor** …`); the Innersloth disclaimer quote.
* Lang deployment: the release zips carry the three JSON files; the launcher update copies them; a manual install must overwrite `BepInEx\PocketRoles\lang\*.json` (the seven changed values never merge).

### 13.10 Docs-level risks
* Remote `/cmd r` for en descriptions above 100 chars (Mad Stuntman, Mad Hawk, Worshipper, Evil Nekomata, Serial Killer, Samurai) drops the option line — pre-existing for Lovers / Witch / Sheriff; the README `/cmd r` sentence does not promise the option line for remote players.
* The README alias sentence must be re-checked if any later role changes `Roles.All` order (the prefix rule is order-dependent).
* README "current version" strings were stale for two releases; the docs pass replaces them all (§13.1) — grep `0.4.1` afterwards and keep only the history mentions.
