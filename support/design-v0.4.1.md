# PocketRoles v0.4.1 — implementation plan: ラバーズ / 放火魔 / 魔女 / アサシン

Everything below was verified against the tree at `C:\Users\riotgames\Desktop\HostRoles` (v0.4.0, game 2026.8.18). Line numbers refer to the files at design time; anchor on the quoted code, not the numbers, because earlier edits shift them.

---

## 0. Ground truth the implementers rely on (read once)

| Concern | Where / signature | Fact |
|---|---|---|
| Role table | `src/Core/Roles.cs` `enum CustomRole { None=0, Sheriff…Jackal }`, `sealed class RoleInfo { Id, Key, NameJa/En/Zh, DescJa/En/Zh, Team, Color, IsKiller, ImpostorDesync, CanVent, CanSabotage, TasksCount, FromImpostorPool, Aliases }`, `Roles.All` (array order = display/assignment order), `Roles.TryParse(text, out role)` (key / NameEn / NameJa / NameZh / enum name / aliases; ja prefix ≥2 chars, zh prefix ≥1 char), `CompatRisky => IsKiller && !FromImpostorPool` | One custom role per player (`Core.Game.Roles : Dictionary<byte,CustomRole>`). Name/desc keys are `role.<Key>.name` / `role.<Key>.desc` (JSON overrides the inline literal). |
| Game state | `src/Core/GameState.cs` `static class Game`: `Roles`, `VanillaRoles`, `struct VampireBite { byte Killer; float DueAt; }`, `Bites : Dictionary<byte,VampireBite>`, `SoloWinner/SoloWinnerId`, `LastExiled`, `Reset()` (called from the `RoleManager.SelectRoles` prefix), `ResetForNewLobby(reason)`, `GameScopedTags`, `RoleOf/InfoOf/VanillaRoleOf/TeamOf/IsImpostorTeamKiller/IsDesyncImpostor/IsJackal/IsNonCrewKiller/IsAlive/IsDead/IsHost/NameOf/AllPlayerIds` | `IsImpostorTeamKiller`: `Vampire || Mafia → true; any other custom role → false; None → vanilla impostor-type`. `TeamOf`: custom → `RoleInfo.Team`, else vanilla side. `IsDead` honours `AntiBlackout.RealIsDead` and the chat temp-revive. |
| Assignment | `src/Game/RoleAssignment.cs` `AssignCustomRoles(players)`: pools `plainCrew` (vanilla `Crewmate`) / `plainImp` (vanilla `Impostor`, exactly — never Shapeshifter etc.), forced roles (`TestMode.ApplyForcedRoles`) are excluded from pools; killers first: `if (r.Id == Jackal || r.Id == Sheriff) order.Add(r)`; `Options.Count/Chance(role)` per slot. `View(viewer,target)`: desync viewer sees itself Impostor and everyone else Crewmate. `SendGhostRole(dead)` picks `ImpostorGhost` iff `IsImpostorTeamKiller`. `DispatchInitialRoles` → `NameTags.RefreshAll(force:true)`, `OptionsDesync.ResyncAll()`, role info chat after 8 s. | New roles drawn from the same two pools; `Lovers` is the only pair role and gets its own assign step. |
| Kill interception | `src/Game/Kills.cs`: `[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckMurder))] Kills_CheckMurderPatch.Prefix(__instance, target)` → `Kills.HandleCheckMurder(killer, target)` (return `true` = vanilla kill). Host-target special block: `(role == None || role == Mafia) && Game.IsHost(target) && Game.IsDesyncImpostor(target)` (vanilla would reject killing a host that is locally Impostor). `IsValidMurder` (no meeting/exile/intro, both alive, not in vent, not GA-protected). Patterns: **kill** `Rpc.Kill(killer,target)` = `killer.RpcMurderPlayer(target,true)`; **fail without cooldown reset** `Rpc.FailKill` = `RpcMurderPlayer(target,false)`; **ability with cooldown reset** = the Vampire pattern: `Rpc.ResetKillCooldown(killer, cd)` (host: `SetKillTimer`; client: options×2 + `MurderPlayer(FailedProtected, target=killer)` to that client + options back after 0.5 s) + mark + `Notice(...)`. `[HarmonyPatch(PlayerControl.MurderPlayer)] → Kills.OnMurder(killer,target,flags)` (Succeeded only): bite cleanup, Terrorist win, Bait report, `WinConditions.Check()` after 0.1 s. `Kills.Tick()` (per frame from `Plugin_TickPatch`, skipped while `MeetingHud.Instance != null || ExileController.Instance != null`) executes due `Bites` via `ExecuteBite` = `Rpc.Kill(victim, victim)`; `FlushBites()` is called by `Meetings_ReportDeadBodyPatch` (the reporter's own bite is postponed); `Meetings_ExileWrapUpPatch` pushes every pending bite to `now + 2 s`. `Notice(playerId, key, ja, en, args)` is **private** (uses `Lang.T(key, ja, en)` → zh only from JSON). `LobbyKillCooldown()` is private. | `Game.Bites` is the generic "delayed, host-executed death" mechanism (survives meetings correctly). All four new roles reuse it; `Reason` is added for logs. |
| Meetings | `src/Game/Meetings.cs`: `Meetings_ReportDeadBodyPatch` (prefix), `Meetings_MeetingStartPatch` (`MeetingHud.Start` postfix; role reminder 1 s later), `Meetings_CheckForEndVotingPatch` (Mayor tally `TryEndVotingWithMayor`; it iterates `hud.playerStates`, counts `ps.DidVote` votes — does **not** skip `ps.AmDead`), `Meetings_VotingCompletePatch` (`MeetingHud.VotingComplete` postfix: `LastExiled`, `AntiBlackout.Prepare`, Jester pending solo), `Meetings_ExileWrapUpPatch` (`ExileController.WrapUp` postfix: `AntiBlackout.Restore` +1.5 s, bites → +2 s, Jester/Terrorist `EndGame`, `WinConditions.CheckNow()`, resync +2 s). `MeetingTools.EndMeetingNow/ShortenResults`. | Deaths *at* meeting end are applied as bites 2 s after WrapUp (after `AntiBlackout.Restore`). Vanilla API present in the dump: `MeetingHud.Update()`, `SetForegroundForDead()`, field `hasForegroundForDeadBeenSet`, `ClearVote(PlayerId, bool)`, `RpcClearVote(PlayerId)`, `CheckForEndVoting()`, `state`, `playerStates : Il2CppReferenceArray<PlayerVoteArea>`; `PlayerVoteArea.SetDead(bool isDead)`, `AmDead`, `DidVote`, `VotedForId`. No method bodies in the dump. |
| Chat / commands | `src/Chat/Commands.cs` `Handle(sender, text)` → `HandleInScope`: `/cmd …` = `explicitCmd`; everyone-commands switch (`h/n/r/l/lang/time`), `IsEveryoneCommand`, `IsHostCommand`, `IsAdminOptKey` (prefix list `sheriff.`, `jackal.`, …), `Reply(sender, text)` private (host: `Chat.Local`; client: `Chat.SendChunksTo`), `ReplyThrottled` (2 s per player). `RoleOptionText(role)` (`cmd.ro.*`), `Assign(tokens)` → `TestMode.TryAssign`. `src/Chat/Chat.cs`: `Chat_SendChatPatch` (host typing `/…` handled locally, never sent), `Chat_AddChatPatch` (incoming `/…` from a player → handled; `return false` hides it **on the host's screen only**). `To(playerId,title,text)`, `All(title, Func<string> builder)` (per-recipient language), `RoleInfoText(playerId, meeting)`, `MaxChars = 100`, `Split` (≤100 chars, full-width digits, one colour tag per message). | **Privacy of a player command**: the host cannot retract a message other clients already received. In a registered lobby the server delivers `"/cmd …"` chat to the host only (documented: `help.2`, README §26/§27, `Options` `RegisterAsModdedLobby` text). So `/cmd guess …` is private; `/guess …` is public. Compat (unregistered) lobbies have no roles at all (`Assign_SelectRolesPatch` returns early), so nothing to design there. |
| Wins | `src/Game/WinConditions.cs`: `enum WinKind { Crew, Impostor, Jackal, Jester, Terrorist }`, `private enum Outcome { Continue, Crew, Impostor, Jackal }`, `Check()` (guards: meeting/exile, pending `SoloWinner`), `CheckNow()` (TestMode → only sabotage end), `WouldContinue(exiledId)` (used by AntiBlackout), `Evaluate(exiledId)` (sabotage → tasks → counts `imp`/`jackal`/`others`/`madmate`), `EndGame(kind, soloId=255)`, `ComputeWinners`, `BuildSummary` (`win.*` keys, `_lastColor`), `EndFromOutcome`, `EndFromVanilla`. Test mode: `EndGame` is suppressed and logged `EndGame({kind}) suppressed: test mode`. | Solo wins: Jester/Terrorist pass `soloId`; `ComputeWinners` adds `Opportunist` alive to any win. |
| Name tags | `src/Game/NameTags.cs` `NameFor(viewerId, targetId, meeting)`: VIP star → own tag (`ColoredName + "\r\n" + name`, meeting: `name <size=70%>role</size>`) → Madmate red → Ⓜ → Snitch ★ → Snitch colours. `RefreshAll(force, meeting, urgent)` sends only changed strings; `ScheduleMurderRefresh()` (0.5 s) after every MurderPlayer / OnPlayerLeft (a Data sync resets private names). | Marks are prefixes inside the same string; they survive later colour wrapping. |
| Options | `src/Core/Options.cs`: per role `cfg.Bind("Roles", NameEn.Replace(" ","")+".Count"/".Chance")` (loop over `Roles.All`, `defCount` 1 only for Sheriff/Jester/Madmate); role-specific binds `[Sheriff] KillCooldown` etc.; getters; `BuildDescriptors()` (`Int/Float/Bool(...).Tip(ja,en,zh)`, key = `<roleKey>.<opt>`, `ColorHex` → roles page in `SettingsTab.PageOf` via `RoleOf(d)` matching `d.Key.StartsWith(r.Key + ".")`); `TrySet(key,value,out msg)` switch; `DescribeLines()` (`/show`, welcome `{settings}`, `opt.desc.*`). Descriptor labels use JSON keys `opt.section.<sectionEnSlug>`, `opt.name.<key>`, `opt.tip.<key>`. | `SetCount` clamps 0..15. |
| Lang | `src/Core/Lang.cs`: `T(key, ja, en)`, `T(key, ja, en, zh)`, `TF(key, ja, en, args)`; `lang/{ja,en,zh-CN}.json` are embedded (`PocketRoles.csproj` `<EmbeddedResource Include="lang\*.json" />`), 700 keys each, **identical key sets, alphabetically sorted, same line numbers**; missing keys are appended to the user's file at startup from the embedded defaults (`MergeMissingKeys`). | Every new key must be added to all three JSON files in sorted position. |
| Per-client options | `src/Net/OptionsDesync.cs` `BuildFor(playerId, killCooldownOverride)` (Sheriff/Jackal cooldown, Lighter, SpeedBooster), `NeedsCustomOptions`, `OptionsDesync_GetKillCooldownPatch` (host's own cooldown). | Add Arsonist (always) and Witch (only when its cooldown option > 0). |
| Test mode | `src/Game/TestMode.cs` `TryAssign(nameOrId, role)`, `ApplyForcedRoles` (crew-pool role on a vanilla impostor → basis `Crewmate`; impostor-pool role on a crewmate → basis `Impostor`, local `CoSetRole`), **private** `FindPlayer(text)` ("#3"/"3"/exact/unique substring). | `/assign` accepts anything `Roles.TryParse` accepts. |
| Misc | `Rpc.ExileSilently(target)` (Exiled RPC broadcast + local `target.Exiled()`, skipped in SafeMode) is already used by `GameMaster.Apply` to kill the host without a body, followed by `Rpc.SetRoleAll(lp, CrewmateGhost)`, `lp.Data.MarkDirty(); Rpc.SendPlayerInfo(lp.Data)`. `AntiCheat` drops client-sent `Exiled/MurderPlayer/SetRole/…` (host-sent are fine). `Scheduler.After(sec, action, tag)`, `Cancel(tag)`. `AmongUsClient.OnPlayerLeft(ClientData data, DisconnectReasons reason)`; `ClientData.Character : PlayerControl`. `Commands.OnOff(bool)` exists (private). | Used by the Assassin meeting kill. |

Design decisions taken (so nobody re-derives them):

* **Lovers = a normal `CustomRole` occupying the single role slot** (Team.Neutral, one pair per game, `Game.LoverA/LoverB`). An impostor lover keeps its vanilla Impostor role (kill button, counts as an impostor for the counting rules via one line in `IsImpostorTeamKiller`) but wins only as a lover (neutral). Jester/Terrorist solo ends keep precedence over the lovers' "any normal end" rule (they bypass `Evaluate`).
* **All delayed deaths (lover suicide, witch curse, assassin fallback) are `Game.Bites` entries** (executed by `Kills.Tick` outside meetings, flushed on report, postponed 2 s after WrapUp, credited to `Killer` for Bait). `VampireBite` gets a `Reason` string for the log.
* **Arsonist / Witch abilities use the Vampire pattern** (`Rpc.ResetKillCooldown` + mark + private notice; never `FailKill` on success).
* **Assassin `/cmd guess` is a player command; the kill during the meeting uses `Rpc.ExileSilently` + Data sync** with a compile-time fallback to "dies 2 s after the exile screen".

Work split: Implementer 1 = §1 (shared, incl. the four class skeletons) + §2 Lovers; Implementer 2 = §3 Arsonist; 3 = §4 Witch; 4 = §5 Assassin. After §1 the tree compiles with stub bodies.

---

## 1. Shared changes (Implementer 1, first)

### 1.1 Version bump
* `PocketRoles.csproj`: `<Version>0.4.0</Version>` → `0.4.1`.
* `src/PocketRolesPlugin.cs`: `public const string Version = "0.4.0";` → `"0.4.1"`.
* README*.md: replace `PocketRoles v0.4.0` and `` `v0.4.0 / Among Us 2026.8.18` `` with 0.4.1 where they describe the current version — leave the history headings (`### v0.4e（v0.4.0 の仕上げ…`) untouched. `PocketRolesLauncher.ps1` `$script:LauncherVersion` stays (launcher's own version). (The docs agent handles README; implementers handle csproj/plugin/CHANGELOG.)
* `CHANGELOG.md`: insert after line 3 (before `## v0.4.0 — 2026-09-08（初公開）`):
  ```
  ## v0.4.1 — 2026-09-xx

  - 役職を 4 つ追加: ラバーズ（第三陣営・2 人 1 組）、放火魔（第三陣営）、魔女（インポスター枠）、アサシン（インポスター枠、会議中の `/cmd guess`）。シェリフは放火魔も撃てるようになった
  - 設定: `[Lovers] AllowImpostor / WinAsLastThree`、`[Arsonist] DouseCooldown / CanVent`、`[Witch] SpellCooldown / SpelledSeeMark`、`[Assassin] GuessesPerMeeting / CanGuessFirstMeeting`（設定タブ「役職」ページ、`/opt lovers.* arsonist.* witch.* assassin.*`）
  ```

### 1.2 `src/Core/Roles.cs`
1. Enum (append, keep existing values):
   ```csharp
   public enum CustomRole
   {
       None = 0,
       Sheriff, Mayor, Snitch, Lighter, SpeedBooster, Bait,
       Madmate, Vampire, Mafia,
       Jester, Opportunist, Terrorist, Jackal,
       // v0.4.1
       Lovers, Arsonist, Witch, Assassin
   }
   ```
2. Colours next to `JackalColor`: `public const string LoversColor = "#ff69b4"; public const string ArsonistColor = "#ff6633";`
3. `Roles.All`: insert **after the Mafia row**:
   ```csharp
   new RoleInfo { Id = CustomRole.Witch, Key = "witch", NameJa = "魔女", NameEn = "Witch", NameZh = "女巫", Team = Team.Impostor, Color = ImpostorColor,
       IsKiller = true, CanVent = true, CanSabotage = true, FromImpostorPool = true,
       DescJa = "インポスターです。キルは「呪い」になり、次の会議が終わった後に呪った相手が全員死にます。あなたが追放・死亡すると呪いは消えます。",
       DescEn = "An Impostor whose kill is a curse: everyone you cursed dies right after the next meeting. The curse fades if you are ejected or die.",
       DescZh = "内鬼。你的击杀变成“诅咒”，下次会议结束后所有被诅咒的人一起死亡。你被投出或死亡则诅咒失效。",
       Aliases = new[] { "wt", "wi" } },
   new RoleInfo { Id = CustomRole.Assassin, Key = "assassin", NameJa = "アサシン", NameEn = "Assassin", NameZh = "刺客", Team = Team.Impostor, Color = ImpostorColor,
       IsKiller = true, CanVent = true, CanSabotage = true, FromImpostorPool = true,
       DescJa = "インポスターです。会議中に /cmd guess 名前 役職 と打つと、正解なら相手が死に、外れるとあなたが死にます。",
       DescEn = "An Impostor. In a meeting type /cmd guess <name> <role>: a correct guess kills the target, a wrong one kills you.",
       DescZh = "内鬼。会议中输入 /cmd guess 名字 职业：猜对则对方死亡，猜错则你死亡。",
       Aliases = new[] { "as", "asn" } },
   ```
   and **after the Jackal row**:
   ```csharp
   new RoleInfo { Id = CustomRole.Lovers, Key = "lovers", NameJa = "ラバーズ", NameEn = "Lovers", NameZh = "恋人", Team = Team.Neutral, Color = LoversColor,
       TasksCount = false,
       DescJa = "恋人です。相手の名前に♥が見えます。片方が死ぬともう片方も死にます。2人とも生きて試合が終わる（または残り3人になる）と2人だけの勝利です。タスクは偽物です。",
       DescEn = "Lovers. You see a ♥ on your partner. If one of you dies the other dies too. Both alive when the game ends (or as the last 3 players) = you two win. Your tasks are fake.",
       DescZh = "恋人。你能看到伴侣名字上的♥。一方死亡另一方也会死。游戏结束时（或只剩3人时）两人都存活即两人获胜。任务是假的。",
       Aliases = new[] { "lover", "lv", "love" } },
   new RoleInfo { Id = CustomRole.Arsonist, Key = "arsonist", NameJa = "放火魔", NameEn = "Arsonist", NameZh = "纵火犯", Team = Team.Neutral, Color = ArsonistColor,
       IsKiller = true, ImpostorDesync = true, CanVent = false, CanSabotage = false, TasksCount = false,
       DescJa = "第三陣営です。キルボタンは「油をかける」で相手は死にません。生きている他の全員に油をかけると即座に単独勝利します。サボタージュ不可、タスクは偽物です。",
       DescEn = "Neutral. Your kill button douses instead of killing. Douse every other living player to win alone at once. No sabotage; your tasks are fake.",
       DescZh = "中立阵营。击杀键变成“浇油”，不会杀人。给所有其他存活玩家浇油后立即单独获胜。不能破坏，任务是假的。",
       Aliases = new[] { "arso", "ars" } },
   ```
4. Sheriff row: replace the three descriptions (the Sheriff may now shoot the Arsonist):
   * DescJa `キルボタンでインポスターやジャッカル、放火魔を撃てます。クルーを撃つと自分が死にます。ベントとサボタージュは使えず、タスクは偽物です。`
   * DescEn `You have a kill button to shoot Impostors, the Jackal and the Arsonist. Shooting anyone else kills you instead. No venting or sabotage; your tasks are fake.`
   * DescZh `可用击杀键射杀内鬼、豺狼和纵火犯。误杀其他人会让自己死亡。不能跳管和破坏，任务是假的。`

   (Alias check: `sh my sn lt sb speed booster bt mad mm vamp vp mf js opp op terror tr jk` exist; `lv lover love ars arso wt wi as asn` are free. Ja prefix: ラバ vs ライ(ライター) differ; アサ, 魔女, 放火 unique. Zh first chars 恋/纵/女/刺 unique.)

### 1.3 `src/Core/GameState.cs`
Add after `Bites`:
```csharp
public struct VampireBite { public byte Killer; public float DueAt; public string Reason; }   // Reason: null = bite, "lovers", "curse", "assassin" (log only)

// ---- v0.4.1 role state (cleared in Reset / ResetForNewLobby)
/// <summary>Lovers pair (255 = none). Set by Lovers.Assign during AssignCustomRoles.</summary>
public static byte LoverA = 255, LoverB = 255;
/// <summary>arsonist playerId → players it doused.</summary>
public static Dictionary<byte, HashSet<byte>> Doused = new Dictionary<byte, HashSet<byte>>();
/// <summary>spelled target playerId → witch playerId (cleared at ExileController.WrapUp).</summary>
public static Dictionary<byte, byte> Spelled = new Dictionary<byte, byte>();
/// <summary>assassin playerId → guesses used in the current meeting (cleared at MeetingHud.Start).</summary>
public static Dictionary<byte, int> GuessesThisMeeting = new Dictionary<byte, int>();
/// <summary>Meetings started in this game (MeetingHud.Start postfix; the first meeting is 1).</summary>
public static int MeetingsHeld;

public static bool IsLover(byte id) => id != 255 && (id == LoverA || id == LoverB);
public static byte PartnerOf(byte id) => id == 255 ? (byte)255 : (id == LoverA ? LoverB : (id == LoverB ? LoverA : (byte)255));
```
`Reset()` and `ResetForNewLobby()` (next to `Bites.Clear()`): `LoverA = LoverB = 255; Doused.Clear(); Spelled.Clear(); GuessesThisMeeting.Clear(); MeetingsHeld = 0;`

`IsImpostorTeamKiller`:
```csharp
var role = RoleOf(id);
if (role == CustomRole.Vampire || role == CustomRole.Mafia || role == CustomRole.Witch || role == CustomRole.Assassin) return true;
if (role == CustomRole.Lovers) return IsVanillaImpostorRole(VanillaRoleOf(id)); // an impostor lover keeps its kill button and counts as an impostor
if (role != CustomRole.None) return false;
return IsVanillaImpostorRole(VanillaRoleOf(id));
```
`TeamOf` unchanged (Lovers → Neutral, Witch/Assassin → Impostor). `GameScopedTags`: no new fixed tags needed.

### 1.4 `src/Core/Options.cs`
Fields (after `_madmateKnownToImpostors`):
```csharp
private static ConfigEntry<bool> _loversAllowImpostor, _loversLastThree, _arsonistCanVent, _witchSpelledSeeMark, _assassinFirstMeeting;
private static ConfigEntry<float> _arsonistDouseCooldown, _witchSpellCooldown;
private static ConfigEntry<int> _assassinGuessesPerMeeting;
```
Count range (the `Roles.All` bind loop): `int maxCount = r.Id == CustomRole.Lovers ? 1 : 15;` and use it in the `AcceptableValueRange<int>(0, maxCount)`; description for Lovers: `"Lovers pairs per game (0 or 1)"`.
Binds after the role loop:
```csharp
_loversAllowImpostor = cfg.Bind("Lovers", "AllowImpostor", true, "The second lover may be a vanilla Impostor (keeps its kill button and counts as an Impostor for the win rules; wins only as a lover)");
_loversLastThree = cfg.Bind("Lovers", "WinAsLastThree", true, "The Lovers win as soon as both are alive and at most 3 players are alive");
_arsonistDouseCooldown = cfg.Bind("Arsonist", "DouseCooldown", 10f, new ConfigDescription("Seconds between two douses (the Arsonist's kill button)", new AcceptableValueRange<float>(2.5f, 180f)));
_arsonistCanVent = cfg.Bind("Arsonist", "CanVent", false, "Arsonist can use vents");
_witchSpellCooldown = cfg.Bind("Witch", "SpellCooldown", 0f, new ConfigDescription("Seconds between two spells (0 = the lobby's kill cooldown)", new AcceptableValueRange<float>(0f, 180f)));
_witchSpelledSeeMark = cfg.Bind("Witch", "SpelledSeeMark", false, "Spelled players see a mark on their own name (the Witch always sees it)");
_assassinGuessesPerMeeting = cfg.Bind("Assassin", "GuessesPerMeeting", 1, new ConfigDescription("Guesses (/cmd guess) per meeting", new AcceptableValueRange<int>(1, 5)));
_assassinFirstMeeting = cfg.Bind("Assassin", "CanGuessFirstMeeting", true, "The Assassin may guess in the first meeting of the game");
```
Getters:
```csharp
public static bool LoversAllowImpostor => _loversAllowImpostor == null || _loversAllowImpostor.Value;
public static bool LoversWinAsLastThree => _loversLastThree == null || _loversLastThree.Value;
public static float ArsonistDouseCooldown => _arsonistDouseCooldown?.Value ?? 10f;
public static bool ArsonistCanVent => _arsonistCanVent != null && _arsonistCanVent.Value;
/// <summary>0 = the lobby kill cooldown.</summary>
public static float WitchSpellCooldown => _witchSpellCooldown?.Value ?? 0f;
public static bool WitchSpelledSeeMark => _witchSpelledSeeMark != null && _witchSpelledSeeMark.Value;
public static int AssassinGuessesPerMeeting => _assassinGuessesPerMeeting?.Value ?? 1;
public static bool AssassinCanGuessFirstMeeting => _assassinFirstMeeting == null || _assassinFirstMeeting.Value;
```
`SetCount`: clamp to `r == CustomRole.Lovers ? 1 : 15`.
`BuildDescriptors`: count row max `role == CustomRole.Lovers ? 1 : 15`; Lovers count tip: `("ラバーズを出すか（1 = 1 組 2 人、0 = 出さない）。", "1 = one pair (two players), 0 = never.", "1 = 一对（两人），0 = 不出现。")`. New switch cases (after Madmate):
```csharp
case CustomRole.Lovers:
    _descriptors.Add(Bool("lovers.impostor", sJa, sEn, "インポスターも恋人になる", "Impostor may be a lover", _loversAllowImpostor, color)
        .Tip("オンなら2人目の恋人がインポスターから選ばれることがあります（キルはできたままです）。", "On: the second lover may be a vanilla Impostor (it keeps its kill button).", "开启后第二位恋人可能从内鬼中选出（仍可击杀）。"));
    _descriptors.Add(Bool("lovers.lastthree", sJa, sEn, "残り3人で勝利", "Win as last 3", _loversLastThree, color)
        .Tip("オンなら2人とも生きていて生存者が3人以下になった時点でラバーズの勝利です。", "On: the Lovers win as soon as both are alive and at most 3 players remain.", "开启后两人存活且存活者不超过3人时恋人立即获胜。"));
    break;
case CustomRole.Arsonist:
    _descriptors.Add(Float("arsonist.cooldown", sJa, sEn, "油のクールダウン", "Douse cooldown", _arsonistDouseCooldown, 2.5f, 180f, 2.5f, color)
        .Tip("油をかけてから次にかけられるまでの秒数。", "Seconds between two douses.", "两次浇油之间的冷却秒数。"));
    _descriptors.Add(Bool("arsonist.vent", sJa, sEn, "ベント使用", "Can vent", _arsonistCanVent, color)
        .Tip("放火魔がベントに入れるかどうか。", "Whether the Arsonist can use vents.", "纵火犯是否可以跳管。"));
    break;
case CustomRole.Witch:
    _descriptors.Add(Float("witch.cooldown", sJa, sEn, "呪いのクールダウン", "Spell cooldown", _witchSpellCooldown, 0f, 180f, 2.5f, color)
        .Tip("呪いをかけてから次にかけられるまでの秒数（0 = キルクールダウンと同じ）。", "Seconds between two spells (0 = same as the kill cooldown).", "两次诅咒之间的秒数（0 = 与击杀冷却相同）。"));
    _descriptors.Add(Bool("witch.mark", sJa, sEn, "呪われた本人に印", "Target sees mark", _witchSpelledSeeMark, color)
        .Tip("オンなら呪われた人の自分の名前に印が付きます（魔女にはいつも見えます）。", "On: a spelled player sees a mark on their own name (the Witch always sees it).", "开启后被诅咒者能在自己名字上看到标记（女巫始终可见）。"));
    break;
case CustomRole.Assassin:
    _descriptors.Add(Int("assassin.guesses", sJa, sEn, "会議ごとの推理回数", "Guesses per meeting", _assassinGuessesPerMeeting, 1, 5, 1, color)
        .Tip("1回の会議で /cmd guess を使える回数。", "How many /cmd guess an Assassin may use per meeting.", "每次会议可使用 /cmd guess 的次数。"));
    _descriptors.Add(Bool("assassin.firstmeeting", sJa, sEn, "初回会議でも推理可", "Can guess in 1st meeting", _assassinFirstMeeting, color)
        .Tip("オフなら試合の最初の会議では推理できません。", "Off: no guessing in the first meeting of the game.", "关闭后本局第一次会议不能猜测。"));
    break;
```
`TrySet` (before `case "sheriff.cooldown"`):
```csharp
case "lovers.impostor": case "lovers.allowimpostor": return SetBool(_loversAllowImpostor, value, "lovers.impostor", out message);
case "lovers.lastthree": case "lovers.last3": case "lovers.winaslastthree": return SetBool(_loversLastThree, value, "lovers.lastthree", out message);
case "arsonist.cooldown": case "arsonist.cd": case "arsonist.dousecooldown": return SetFloat(_arsonistDouseCooldown, value, 2.5f, 180f, "arsonist.cooldown", out message);
case "arsonist.vent": case "arsonist.canvent": return SetBool(_arsonistCanVent, value, "arsonist.vent", out message);
case "witch.cooldown": case "witch.cd": case "witch.spellcooldown": return SetFloat(_witchSpellCooldown, value, 0f, 180f, "witch.cooldown", out message);
case "witch.mark": case "witch.spelledseemark": return SetBool(_witchSpelledSeeMark, value, "witch.mark", out message);
case "assassin.guesses": case "assassin.guessespermeeting": return SetInt(_assassinGuessesPerMeeting, value, 1, 5, "assassin.guesses", out message);
case "assassin.firstmeeting": case "assassin.first": case "assassin.canguessfirstmeeting": return SetBool(_assassinFirstMeeting, value, "assassin.firstmeeting", out message);
```
Update the `TrySet` doc comment key list accordingly. `DescribeLines` switch (after Madmate):
```csharp
case CustomRole.Lovers:
    if (LoversAllowImpostor) line += Lang.T("opt.desc.lovers.impostor", " インポスター可", ", impostor allowed");
    if (LoversWinAsLastThree) line += Lang.T("opt.desc.lovers.lastthree", " 残り3人で勝利", ", win as last 3");
    break;
case CustomRole.Arsonist:
    line += Lang.TF("opt.desc.arsonist", " 油CD{0:0.#}秒", " douse CD {0:0.#}s", ArsonistDouseCooldown);
    if (ArsonistCanVent) line += Lang.T("opt.desc.arsonist.vent", " ベント可", ", can vent");
    break;
case CustomRole.Witch:
    if (WitchSpellCooldown > 0f) line += Lang.TF("opt.desc.witch", " 呪いCD{0:0.#}秒", " spell CD {0:0.#}s", WitchSpellCooldown);
    if (WitchSpelledSeeMark) line += Lang.T("opt.desc.witch.mark", " 本人に印", ", target sees mark");
    break;
case CustomRole.Assassin:
    line += Lang.TF("opt.desc.assassin", " 推理{0}回/会議", " {0} guess/meeting", AssassinGuessesPerMeeting);
    if (!AssassinCanGuessFirstMeeting) line += Lang.T("opt.desc.assassin.first", " 初回会議不可", ", not in 1st meeting");
    break;
```

### 1.5 `src/Chat/Commands.cs`
* `RoleOptionText` new cases:
  ```csharp
  case CustomRole.Lovers:
      return Lang.TF("cmd.ro.lovers", "インポスターも恋人になる: {0}、残り3人で勝利: {1}", "Impostor may be a lover: {0}, win as last 3: {1}", OnOff(Options.LoversAllowImpostor), OnOff(Options.LoversWinAsLastThree));
  case CustomRole.Arsonist:
      return Options.ArsonistCanVent
          ? Lang.TF("cmd.ro.arsonist.vent", "油CD {0:0.#}秒、ベント可", "Douse cooldown {0:0.#}s, vent on", Options.ArsonistDouseCooldown)
          : Lang.TF("cmd.ro.arsonist", "油CD {0:0.#}秒、ベント不可", "Douse cooldown {0:0.#}s, vent off", Options.ArsonistDouseCooldown);
  case CustomRole.Witch:
      return Lang.TF("cmd.ro.witch", "呪いCD {0}、呪われた本人に印: {1}", "Spell cooldown {0}, target sees mark: {1}",
          Options.WitchSpellCooldown > 0f ? Options.WitchSpellCooldown.ToString("0.#") + "s" : Lang.T("cmd.ro.witch.samecd", "キルと同じ", "same as kill"), OnOff(Options.WitchSpelledSeeMark));
  case CustomRole.Assassin:
      return Lang.TF("cmd.ro.assassin", "会議ごとに {0} 回推理、初回会議: {1}", "{0} guess(es) per meeting, first meeting: {1}", Options.AssassinGuessesPerMeeting, OnOff(Options.AssassinCanGuessFirstMeeting));
  ```
* `IsAdminOptKey`: append `|| k.StartsWith("lovers.") || k.StartsWith("arsonist.") || k.StartsWith("witch.") || k.StartsWith("assassin.")`.
* `IsEveryoneCommand`: add `case "guess": case "g": case "推理":`. In the everyone switch of `HandleInScope` (after `case "time"`):
  ```csharp
  case "guess": case "g": case "推理":
      Reply(sender, Assassin.Guess(sender, JoinArgs(tokens, 1), explicitCmd)); // never throttled: the guess itself is limited per meeting
      return true;
  ```
  (`using PocketRoles.Game;` is already present.)
* `TestMode.FindPlayer`: change `private` → `internal` (reused by `Assassin.Guess`).

### 1.6 `src/Game/Kills.cs` (shared parts)
* `Notice(...)` and `LobbyKillCooldown()`: `private` → `internal`.
* `HandleCheckMurder` host-target block: `(role == CustomRole.None || role == CustomRole.Mafia || role == CustomRole.Assassin || role == CustomRole.Lovers)` — Assassin/impostor-lover kills on a host Sheriff/Jackal/Arsonist need the `Rpc.Kill` path; `allowed` already uses `Game.IsImpostorTeamKiller(killerId)` (true for both after 1.3). Witch stays out of this block (its spell path never needs a vanilla kill).
* `CanVent`: `if (role == CustomRole.Arsonist) return Options.ArsonistCanVent;`
* `CanSheriffKill`: `if (role == CustomRole.Jackal || role == CustomRole.Arsonist) return true;`
* Role switch in `HandleCheckMurder`: add
  ```csharp
  case CustomRole.Arsonist: Arsonist.Douse(killer, target); return false;
  case CustomRole.Witch: Witch.Spell(killer, target); return false;
  ```
  (Assassin falls to `default: return true` = vanilla kill.)
* `Tick()`: also return while `IntroCutscene.Instance != null` (lover suicide after a disconnect during the intro must not fire inside the intro).
* `OnMurder` (after the bite bookkeeping, before the Terrorist check):
  ```csharp
  Witch.OnPlayerDied(targetId);     // dead witch → curse fades; dead target → forgotten
  Arsonist.OnPlayerDied(targetId);  // dead arsonist → douses vanish
  Lovers.OnPlayerDied(targetId);    // partner follows (delayed death through Game.Bites)
  ```
* `ExecuteBite` log: `$"Kills: {(bite.Reason ?? "bite")} on {Game.NameOf(victimId)} (by {Game.NameOf(bite.Killer)}) executes"`.

### 1.7 `src/Game/Meetings.cs` (shared hooks)
* `Meetings_MeetingStartPatch.Postfix`: before the `Scheduler.After(1f, …)`: `Core.Game.MeetingsHeld++; Core.Game.GuessesThisMeeting.Clear();`
* `TryEndVotingWithMayor` loop: after `states.Add(...)`, add `if (ps.AmDead) continue;` (a player killed mid-meeting must not be tallied).
* `Meetings_VotingCompletePatch.Postfix`: after `Core.Game.LastExiled = exiledId;`:
  ```csharp
  if (exiledId != 255) { Witch.OnPlayerExiled(exiledId); Arsonist.OnPlayerDied(exiledId); Lovers.OnPlayerExiled(exiledId); }
  ```
  (Order matters: before `AntiBlackout.Prepare` is fine — the lover's suicide is a bite that executes only 2 s after WrapUp, exactly like a postponed vampire bite, so `WouldContinue(exiledId)` stays consistent.)
* `Meetings_ExileWrapUpPatch.Postfix`: right after the bite-postponement block and before the Jester check: `Witch.OnMeetingEnd(exiledId);`

### 1.8 `src/Game/WinConditions.cs` (shared)
```csharp
public enum WinKind { Crew, Impostor, Jackal, Jester, Terrorist, Lovers, Arsonist }
private enum Outcome { Continue, Crew, Impostor, Jackal, Lovers, Arsonist }
/// <summary>Arsonist id found by the last Evaluate that returned Outcome.Arsonist.</summary>
private static byte _evalSoloId = 255;
```
* `CheckNow` switch and `EndFromOutcome`: add `case Outcome.Lovers: EndGame(WinKind.Lovers); break; case Outcome.Arsonist: EndGame(WinKind.Arsonist, _evalSoloId); break;`
* Rename the current `Evaluate` body to `EvaluateBase(byte exiledId)` and add:
  ```csharp
  private static Outcome Evaluate(byte exiledId)
  {
      Outcome o = EvaluateBase(exiledId);
      byte arsonist = Arsonist.WinnerId(exiledId);           // alive arsonist whose douses cover every other alive player
      if (arsonist != 255) { _evalSoloId = arsonist; return Outcome.Arsonist; }
      if (Lovers.BothAlive(exiledId) && (o != Outcome.Continue || (Options.LoversWinAsLastThree && AliveCount(exiledId) <= 3)))
          return Outcome.Lovers;
      return o;
  }
  private static int AliveCount(byte exiledId) { int n = 0; foreach (var id in Core.Game.AllPlayerIds()) if (id != exiledId && Core.Game.IsAlive(id)) n++; return n; }
  ```
* `EndGame`: the solo-kind condition becomes `(kind == WinKind.Jester || kind == WinKind.Terrorist || kind == WinKind.Arsonist)`.
* `ComputeWinners`: `case WinKind.Lovers: win = role == CustomRole.Lovers; break; case WinKind.Arsonist: win = id == soloId; break;` and wherever Jester/Terrorist solo kinds are listed add `|| kind == WinKind.Arsonist`.
* `BuildSummary`: `case WinKind.Lovers: plain = Lang.T("win.lovers", "ラバーズ勝利", "Lovers win"); color = Roles.Info(CustomRole.Lovers).Color; break; case WinKind.Arsonist: plain = Lang.T("win.arsonist", "放火魔勝利", "Arsonist wins"); color = Roles.Info(CustomRole.Arsonist).Color; break;`; suffix: Arsonist added to the `" (" + NameOf(soloId) + ")"` condition; Lovers: `text += " (" + NameOf(LoverA) + " & " + NameOf(LoverB) + ")"`.

### 1.9 `src/Net/OptionsDesync.cs`
`BuildFor` switch: `case CustomRole.Arsonist: opts.SetFloat(FloatOptionNames.KillCooldown, Mathf.Max(0.02f, Options.ArsonistDouseCooldown)); break; case CustomRole.Witch: if (Options.WitchSpellCooldown > 0f) opts.SetFloat(FloatOptionNames.KillCooldown, Mathf.Max(0.02f, Options.WitchSpellCooldown)); break;`
`NeedsCustomOptions`: `case CustomRole.Arsonist: return true; case CustomRole.Witch: return Options.WitchSpellCooldown > 0f;`
`OptionsDesync_GetKillCooldownPatch`: `case CustomRole.Arsonist: __result = Mathf.Max(0.02f, Options.ArsonistDouseCooldown); break; case CustomRole.Witch: if (Options.WitchSpellCooldown > 0f) __result = Mathf.Max(0.02f, Options.WitchSpellCooldown); break;`

### 1.10 `src/Game/RoleAssignment.cs` / `TestMode.cs`
* `AssignCustomRoles`: killers-first list → `r.Id == CustomRole.Jackal || r.Id == CustomRole.Sheriff || r.Id == CustomRole.Arsonist`. Before the `foreach (var role in order)` loop: `Lovers.Assign(plainCrew, plainImp, Rand);` and inside the loop `if (role.Id == CustomRole.Lovers) continue;` plus
  ```csharp
  if (role.Id == CustomRole.Assassin && !Assassin.Enabled)
  {
      if (Options.Count(role.Id) > 0) { PocketRolesPlugin.Logger.LogWarning("RoleAssignment: Assassin skipped (player commands disabled)"); Chat.Chat.Local(Chat.Chat.Title, Lang.T("assign.assassin.nocmd", "アサシンはプレイヤーのコマンドが有効な時だけ配られます（/opt chat.playercommands on）。", "The Assassin is only assigned while player commands are enabled (/opt chat.playercommands on).", "只有启用玩家命令时才会分配刺客（/opt chat.playercommands on）。")); }
      continue;
  }
  ```
* `LogAssignment`: append `if (Core.Game.LoverA != 255) sb.Append("\n  Lovers: #").Append(LoverA).Append(' ').Append(NameOf(LoverA)).Append(" & #").Append(LoverB).Append(' ').Append(NameOf(LoverB));`
* `TestMode.ApplyForcedRoles`: `else if (!info.FromImpostorPool && vanillaImp && role != CustomRole.Lovers) basis = RoleTypes.Crewmate;` — a forced lover keeps its vanilla side (so `/assign X lovers` on an impostor tests the impostor-lover).

### 1.11 `src/Game/NameTags.cs`
In `NameFor`, after the VIP mark and before `// 1. own role tag`:
```csharp
CustomRole targetRole = Core.Game.RoleOf(targetId);
CustomRole viewerRole = Core.Game.RoleOf(viewerId);
// v0.4.1 marks (prefixes; later colour wrapping keeps them)
string mark = "";
if (viewerId != targetId && Core.Game.PartnerOf(viewerId) == targetId) mark += Lovers.Heart;
if (viewerRole == CustomRole.Witch && Core.Game.Spelled.TryGetValue(targetId, out var witchId) && witchId == viewerId) mark += Witch.Mark;
if (viewerId == targetId && Options.WitchSpelledSeeMark && Core.Game.Spelled.ContainsKey(targetId)) mark += Witch.Mark;
if (viewerRole == CustomRole.Arsonist && Core.Game.Doused.TryGetValue(viewerId, out var set) && set.Contains(targetId)) mark += Arsonist.Mark;
baseName = mark + baseName;
```
(move the two existing `targetRole/viewerRole` declarations up). `ScheduleMurderRefresh` is already `internal`.

### 1.12 `src/Chat/Chat.cs` `RoleInfoText`
Restructure so extras survive the length branches:
```csharp
string result = <existing body result>;
if (role == CustomRole.Lovers) { byte p = Core.Game.PartnerOf(playerId); if (p != 255) result += "\n" + Lang.TF("roleinfo.lovers.partner", "あなたの恋人: {0}", "Your lover: {0}", Core.Game.NameOf(p)); }
if (role == CustomRole.Witch && meeting) { string s = Witch.SpelledNamesFor(playerId); if (s.Length > 0) result += "\n" + Lang.TF("roleinfo.witch.spelled", "呪い中: {0}", "Cursed: {0}", s); }
return result;
```
(skip the extras in the `meeting && Rpc.SafeMode` branch — one message only there).

### 1.13 Class skeletons (Implementer 1 creates; owners fill in)
`src/Game/Lovers.cs`, `Arsonist.cs`, `Witch.cs`, `Assassin.cs`, each `namespace PocketRoles.Game { using Game = PocketRoles.Core.Game; using HrChat = PocketRoles.Chat.Chat; … }` with exactly this surface (stub bodies `return`/`return 255`/`return false`/`return ""` until implemented):
```csharp
public static class Lovers   { public const string Heart = "<color=" + Roles.LoversColor + ">♥</color>";
    internal static void Assign(List<byte> plainCrew, List<byte> plainImp, Random rand); internal static bool BothAlive(byte exiledId);
    internal static void OnPlayerDied(byte id); internal static void OnPlayerExiled(byte id); internal static void OnPlayerLeft(byte id); }
public static class Arsonist { public const string Mark = "<color=" + Roles.ArsonistColor + ">♨</color>";
    internal static void Douse(PlayerControl arsonist, PlayerControl target); internal static int Remaining(byte arsonistId);
    internal static byte WinnerId(byte exiledId); internal static void OnPlayerDied(byte id); }
public static class Witch    { public const string Mark = "<color=" + Roles.ImpostorColor + ">†</color>";
    internal static float SpellCooldown(); internal static void Spell(PlayerControl witch, PlayerControl target);
    internal static void OnPlayerDied(byte id); internal static void OnPlayerExiled(byte id); internal static void OnMeetingEnd(byte exiledId); internal static string SpelledNamesFor(byte witchId); }
public static class Assassin { internal const bool KillDuringMeeting = true; internal static bool Enabled => Options.PlayerCommands && Options.AllCommands;
    internal static string Guess(PlayerControl sender, string args, bool explicitCmd); }
```
(Check the exact names of the two chat option getters in Options.cs — the ones behind `[Chat] PlayerCommands` / `AllCommands` — and use those.)

### 1.14 Lang keys (add to `lang/ja.json`, `lang/en.json`, `lang/zh-CN.json`, alphabetical position; ja / en / zh)
| key | ja | en | zh |
|---|---|---|---|
| assign.assassin.nocmd | アサシンはプレイヤーのコマンドが有効な時だけ配られます（/opt chat.playercommands on）。 | The Assassin is only assigned while player commands are enabled (/opt chat.playercommands on). | 只有启用玩家命令时才会分配刺客（/opt chat.playercommands on）。 |
| cmd.ro.arsonist | 油CD {0:0.#}秒、ベント不可 | Douse cooldown {0:0.#}s, vent off | 浇油冷却 {0:0.#} 秒，不可跳管 |
| cmd.ro.arsonist.vent | 油CD {0:0.#}秒、ベント可 | Douse cooldown {0:0.#}s, vent on | 浇油冷却 {0:0.#} 秒，可跳管 |
| cmd.ro.assassin | 会議ごとに {0} 回推理、初回会議: {1} | {0} guess(es) per meeting, first meeting: {1} | 每次会议猜测 {0} 次，首次会议：{1} |
| cmd.ro.lovers | インポスターも恋人になる: {0}、残り3人で勝利: {1} | Impostor may be a lover: {0}, win as last 3: {1} | 内鬼可成为恋人：{0}，剩3人获胜：{1} |
| cmd.ro.witch | 呪いCD {0}、呪われた本人に印: {1} | Spell cooldown {0}, target sees mark: {1} | 诅咒冷却 {0}，被诅咒者可见标记：{1} |
| cmd.ro.witch.samecd | キルと同じ | same as kill | 与击杀相同 |
| guess.badrole | 役職が見つかりません: {0}（/cmd r で一覧。crew / impostor も可） | Unknown role: {0} (/cmd r lists them; crew / impostor allowed) | 找不到职业：{0}（/cmd r 查看列表；也可填 crew / impostor） |
| guess.correct | 正解！{0} は {1} でした。 | Correct! {0} was {1}. | 猜对了！{0} 是 {1}。 |
| guess.dead | 死亡しているため推理できません。 | You are dead and cannot guess. | 你已死亡，不能猜测。 |
| guess.firstmeeting | 最初の会議では推理できません。 | No guessing in the first meeting. | 第一次会议不能猜测。 |
| guess.killed.all | {0} は暗殺されました。 | {0} was assassinated. | {0} 被刺杀了。 |
| guess.killed.later | {0} は暗殺されました。会議の後に死亡します。 | {0} was assassinated and dies after the meeting. | {0} 被刺杀，会议结束后死亡。 |
| guess.limit | この会議の推理回数（{0}回）を使い切りました。 | You used all {0} guess(es) of this meeting. | 本次会议的猜测次数（{0} 次）已用完。 |
| guess.nomeeting | 会議の投票中だけ使えます。 | Only during the voting phase of a meeting. | 只能在会议投票阶段使用。 |
| guess.noplayer | プレイヤー「{0}」が見つかりません。 | Player "{0}" not found. | 找不到玩家“{0}”。 |
| guess.notassassin | アサシンだけが使えます。 | Only the Assassin can guess. | 只有刺客可以猜测。 |
| guess.public | 注意: /cmd を付けずに打ったため全員に見えています。次からは /cmd guess を使ってください。 | Note: without /cmd everyone saw your guess; use /cmd guess next time. | 注意：没有加 /cmd，所有人都看到了你的猜测。下次请用 /cmd guess。 |
| guess.self | 自分は推理できません。 | You cannot guess yourself. | 不能猜自己。 |
| guess.targetdead | {0} はすでに死亡しています。 | {0} is already dead. | {0} 已经死亡。 |
| guess.usage | 使い方: /cmd guess <名前\|番号> <役職>  例: /cmd guess Taro sheriff（crew / impostor も可） | Usage: /cmd guess <name\|id> <role>  e.g. /cmd guess Taro sheriff (crew / impostor allowed) | 用法：/cmd guess <名字\|编号> <职业>  例：/cmd guess Taro sheriff（也可填 crew / impostor） |
| guess.wrong | 外れ！{0} は {1} ではありません。あなたが死亡しました。 | Wrong! {0} is not {1}. You die. | 猜错了！{0} 不是 {1}。你死亡了。 |
| kill.curse.all | 魔女の呪いが発動しました。 | The witch's curse strikes. | 女巫的诅咒发动了。 |
| kill.douse | {0} に油をかけました（残り {1} 人）。 | You doused {0} ({1} left). | 你给 {0} 浇了油（还剩 {1} 人）。 |
| kill.douse.already | {0} にはもう油をかけています。 | {0} is already doused. | {0} 已经被浇过油了。 |
| kill.spell | {0} に呪いをかけました。次の会議の後に死亡します。 | You cursed {0}. They die after the next meeting. | 你诅咒了 {0}，对方将在下次会议后死亡。 |
| lovers.follow | 恋人が死んだため、あなたも後を追います… | Your lover died. You follow them... | 你的恋人死了，你也随之而去…… |
| opt.desc.arsonist | ␠油CD{0:0.#}秒 | ␠douse CD {0:0.#}s | ␠浇油CD{0:0.#}秒 |
| opt.desc.arsonist.vent | ␠ベント可 | , can vent | ␠可跳管 |
| opt.desc.assassin | ␠推理{0}回/会議 | ␠{0} guess/meeting | ␠每会议猜{0}次 |
| opt.desc.assassin.first | ␠初回会議不可 | , not in 1st meeting | ␠首次会议不可 |
| opt.desc.lovers.impostor | ␠インポスター可 | , impostor allowed | ␠可含内鬼 |
| opt.desc.lovers.lastthree | ␠残り3人で勝利 | , win as last 3 | ␠剩3人获胜 |
| opt.desc.witch | ␠呪いCD{0:0.#}秒 | ␠spell CD {0:0.#}s | ␠诅咒CD{0:0.#}秒 |
| opt.desc.witch.mark | ␠本人に印 | , target sees mark | ␠本人可见标记 |
| opt.name.arsonist.chance / .count | 確率 / 人数 | Chance / Count | 概率 / 人数 |
| opt.name.arsonist.cooldown | 油のクールダウン | Douse cooldown | 浇油冷却 |
| opt.name.arsonist.vent | ベント使用 | Can vent | 可跳管 |
| opt.name.assassin.chance / .count | 確率 / 人数 | Chance / Count | 概率 / 人数 |
| opt.name.assassin.firstmeeting | 初回会議でも推理可 | Can guess in 1st meeting | 首次会议可猜测 |
| opt.name.assassin.guesses | 会議ごとの推理回数 | Guesses per meeting | 每次会议猜测次数 |
| opt.name.lovers.chance / .count | 確率 / 組数（0/1） | Chance / Pairs (0/1) | 概率 / 组数（0/1） |
| opt.name.lovers.impostor | インポスターも恋人になる | Impostor may be a lover | 内鬼也可成为恋人 |
| opt.name.lovers.lastthree | 残り3人で勝利 | Win as last 3 | 剩3人时获胜 |
| opt.name.witch.chance / .count | 確率 / 人数 | Chance / Count | 概率 / 人数 |
| opt.name.witch.cooldown | 呪いのクールダウン | Spell cooldown | 诅咒冷却 |
| opt.name.witch.mark | 呪われた本人に印 | Target sees mark | 被诅咒者可见标记 |
| opt.section.arsonist / assassin / lovers / witch | 放火魔 / アサシン / ラバーズ / 魔女 | Arsonist / Assassin / Lovers / Witch | 纵火犯 / 刺客 / 恋人 / 女巫 |
| opt.tip.<role>.chance (×4) | 各枠にこの役職が実際に割り当てられる確率（%）。 | Chance (%) that each slot of this role is actually assigned. | 每个名额实际分配此职业的概率（%）。 |
| opt.tip.<role>.count (arsonist/assassin/witch) | この役職を最大何人まで出すか（0 = 出さない）。 | Maximum number of this role per game (0 = never). | 此职业每局最多出现的人数（0 = 不出现）。 |
| opt.tip.lovers.count | ラバーズを出すか（1 = 1 組 2 人、0 = 出さない）。 | 1 = one pair (two players), 0 = never. | 1 = 一对（两人），0 = 不出现。 |
| opt.tip.lovers.impostor / lovers.lastthree / arsonist.cooldown / arsonist.vent / witch.cooldown / witch.mark / assassin.guesses / assassin.firstmeeting | = the `.Tip(...)` texts in 1.4 | | |
| role.arsonist.name / .desc, role.assassin.*, role.lovers.*, role.witch.* | = 1.2 texts | | |
| role.sheriff.desc | = 1.2 new Sheriff text (all three files) | | |
| roleinfo.lovers.partner | あなたの恋人: {0} | Your lover: {0} | 你的恋人：{0} |
| roleinfo.witch.spelled | 呪い中: {0} | Cursed: {0} | 已诅咒：{0} |
| win.arsonist | 放火魔勝利 | Arsonist wins | 纵火犯获胜 |
| win.lovers | ラバーズ勝利 | Lovers win | 恋人获胜 |

(`␠` = leading space, as in the existing `opt.desc.*` entries. After editing, re-run the parity check: the three files must keep identical key sets — `diff <(grep -o '^  "[^"]*"' lang/ja.json) <(grep -o '^  "[^"]*"' lang/en.json)` must be empty. Also copy the new keys into the game folder's `BepInEx\PocketRoles\lang\*.json` so the running copy matches, or rely on the startup merge.)

### 1.15 README rows (docs agent; ja `README.md`, mirror in `README.en.md` / `README.zh-CN.md`)
* Intro bullet `追加役職 13 種: …ジャッカル` → `17 種: …ジャッカル、ラバーズ、放火魔、魔女、アサシン` (en/zh likewise).
* §10 role table: after the マフィア row insert 魔女 / アサシン; after the ジャッカル row insert ラバーズ / 放火魔:
  ```
  | 魔女 (Witch / 女巫) | インポスター | インポスター枠。キルが「呪い」になり相手は死にません（相手には何も起きません）。次の会議が終わった直後（追放画面の約 2 秒後）に呪った相手が全員同時に死にます。魔女が追放・死亡すると呪いは消えます。ベント・サボタージュ可。 | 呪いCD 0（= キルCD）、本人に印: off |
  | アサシン (Assassin / 刺客) | インポスター | インポスター枠。通常どおりキルできるほか、会議中に `/cmd guess <名前> <役職>` で役職を推理。正解なら相手がその場で死亡、外れると自分が死亡（`/guess` と打つと全員に見えます。参加者コマンドが無効の部屋では配られません）。 | 会議ごとの推理 1 回、初回会議: on |
  | ラバーズ (Lovers / 恋人) | 第三陣営 | 2 人 1 組。お互いの名前に ♥ が見えます。片方が死ぬ（キル・追放・切断）ともう片方も後を追って死にます。2 人とも生きている状態で他の終了条件が成立した時、または（設定オン時）生存者が 3 人以下になった時に 2 人だけの勝利。2 人目はインポスターから選ばれることもあります（キルは可能、勝利はラバーズとしてのみ）。タスクは偽物。 | 組数 0/1、インポスター可: on、残り 3 人で勝利: on |
  | 放火魔 (Arsonist / 纵火犯) | 第三陣営 | キルボタンが「油をかける」になり相手は死にません（相手には何も起きません）。生きている他の全員に油をかけた瞬間に単独勝利。放火魔が死ぬと油は消えます。サボタージュ不可、ベントは設定。タスクは偽物。シェリフに撃たれます。 | 油CD 10 秒、ベント: off |
  ```
* Sheriff row: 「…インポスター（ヴァンパイア・マフィア含む）とジャッカル、放火魔を撃てます」.
* Alias list: add `lv`、`ars`、`wt`、`as`.
* Win table (before the オポチュニスト line):
  ```
  | 放火魔が生きている他の全員に油をかけた | 放火魔単独勝利 |
  | ラバーズが 2 人とも生きた状態で上記のどれか（ジェスター・テロリスト以外）が成立、または `WinAsLastThree` がオンで生存者 3 人以下 | ラバーズ勝利（2 人） |
  ```
* §11 everyone commands, after `r <役職名>`: `` | `guess <名前> <役職>`, `g` | アサシン専用（会議の投票中）。必ず `/cmd guess …` で（`/guess` は全員に見えます）。役職名のほか `crew` / `impostor` も指定可 | ``
* §11 `/opt` table after `madmate.known`: rows `lovers.impostor` on/off `[Lovers] AllowImpostor`; `lovers.lastthree` on/off `[Lovers] WinAsLastThree`; `arsonist.cooldown` 2.5〜180 `[Arsonist] DouseCooldown`; `arsonist.vent` on/off `[Arsonist] CanVent`; `witch.cooldown` 0〜180（0 = キルCD） `[Witch] SpellCooldown`; `witch.mark` on/off `[Witch] SpelledSeeMark`; `assassin.guesses` 1〜5 `[Assassin] GuessesPerMeeting`; `assassin.firstmeeting` on/off `[Assassin] CanGuessFirstMeeting`.
* §12 config sample: after `Jackal.Chance = 100` add `Lovers.Count = 0 / Lovers.Chance = 100 / Arsonist.* / Witch.* / Assassin.*`; after `KnownToImpostors = false` add the four sections with the defaults from 1.4 and `#` comments.
* §26 キルの演出 table (before `試合終了処理中のキル`): rows for 放火魔の油 (何も起きない、CD リセット、放火魔にチャット、全員にかけた瞬間に終了), 魔女の呪い (何も起きない、CD リセット、魔女にチャット; 追放画面の約 2 秒後に呪われた人がその場で倒れる — 自分で自分をキルする演出), アサシンの推理 (会議画面で相手/自分が死亡扱いになり ✕ が付く、全員に「○○ は暗殺されました」), ラバーズの後追い (0.5 秒後にその場で倒れる; 追放の場合は追放画面の後).
* §26 名前タグ table (before `通常役職`): `| ラバーズ | 相手の名前の前に ♥ |`, `| 魔女 | 呪った相手の前に †（`SpelledSeeMark` オンなら本人にも） |`, `| 放火魔 | 油をかけた相手の前に ♨ |`.
* §26 会議: bullet「アサシンの推理で死んだ人は会議画面で ✕ になり投票から外れます」.
* §27: bullets: 「♥ † ♨ の記号はクライアントのフォント次第で □ になることがあります（定数 `Lovers.Heart` などで変更可）」; 「アサシンは `[Chat] PlayerCommands` がオフの部屋では配られません。`/cmd` なしの `/guess` は全員に見えます」.

---

## 2. ラバーズ / Lovers (Implementer 1)

**Files**: `src/Game/Lovers.cs` (new), hooks already wired in §1 (`RoleAssignment.AssignCustomRoles`, `Kills.OnMurder`, `Meetings_VotingCompletePatch`, `WinConditions.Evaluate`, `NameTags.NameFor`, `Chat.RoleInfoText`, `TestMode.ApplyForcedRoles`).

**Assignment** (`Lovers.Assign(plainCrew, plainImp, rand)`, called before the random loop):
1. `forced` = ids with `Game.Roles[id] == Lovers` in `Game.AllPlayerIds()` order; if > 2, `Game.Roles.Remove` the extras (log warning). `a = forced[0] or 255`, `b = forced[1] or 255`.
2. If `a == 255`: return unless `Options.Count(Lovers) > 0 && rand.Next(100) < Options.Chance(Lovers)`; `a = take random from plainCrew` (return if empty).
3. If `b == 255`: candidates = `plainCrew` ∪ (`Options.LoversAllowImpostor ? plainImp : ∅`) minus `a`; pick uniformly; remove from its pool. If none: log `Lovers: no partner available, pair dropped`, remove `a` from `Game.Roles` if it was forced (a forced single lover without any partner candidate is dropped too), return.
4. `Game.Roles[a] = Game.Roles[b] = CustomRole.Lovers; Game.LoverA = a; Game.LoverB = b;` log `Lovers: #a name & #b name (impostor lover: yes/no)`.
   Game Master host: skip the host as a candidate when `Core.Game.GameMasterActive && Core.Game.IsHost(id)`.

**Death chain** (`Follow(deadId)` used by `OnPlayerDied/OnPlayerExiled/OnPlayerLeft`):
```csharp
if (!Game.InProgress || Game.Ending) return;
byte partner = Game.PartnerOf(deadId);
if (partner == 255 || !Game.IsAlive(partner) || Game.Bites.ContainsKey(partner)) return;
Game.Bites[partner] = new Game.VampireBite { Killer = partner, DueAt = Time.time + 0.5f, Reason = "lovers" };
PocketRolesPlugin.Logger.LogInfo($"Lovers: {Game.NameOf(deadId)} died → {Game.NameOf(partner)} follows in 0.5 s");
Kills.Notice(partner, "lovers.follow", "恋人が死んだため、あなたも後を追います…", "Your lover died. You follow them...");
```
Behaviour by cause: kill → `Kills.Tick` executes the bite 0.5 s later (`Rpc.Kill(partner, partner)`, self-kill animation + body; Bait auto-report skipped because `reporterId == targetId`); exile → registered at `VotingComplete`, executed 2 s after WrapUp (postponed by `Meetings_ExileWrapUpPatch`); report/meeting in between → `FlushBites` executes it at report (reporter's own bite is postponed by the existing code); assassination during a meeting → `Assassin` calls `Lovers.OnPlayerDied` → same bite → after WrapUp. Disconnect: add in `Lovers.cs`
```csharp
[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
internal static class Lovers_OnPlayerLeftPatch { private static void Postfix(ClientData data) { try { if (!Core.Game.IsHostActive || !Core.Game.InProgress || data == null || data.Character == null) return; Lovers.OnPlayerLeft(data.Character.PlayerId); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Lovers_OnPlayerLeft: {e}"); } } }
```
(`OnPlayerLeft(ClientData data, DisconnectReasons reason)` — Harmony binds `data` by name.)

**Win**: `BothAlive(exiledId)` = `LoverA != 255 && LoverB != 255 && A != exiledId && B != exiledId && Game.IsAlive(A) && Game.IsAlive(B)`. Rules are in §1.8: lovers override any `EvaluateBase` outcome (crew/impostor/jackal/task/sabotage) and, with `WinAsLastThree`, fire as soon as `AliveCount ≤ 3`. Jester exile / Terrorist deaths bypass `Evaluate` and keep precedence (documented). `ComputeWinners(WinKind.Lovers)` = both lovers (+ alive Opportunist as usual). An impostor lover in an Impostor win is **not** a winner (`TeamOf` = Neutral) — by design.

**Name tags / chat**: heart on the partner (§1.11); `RoleInfoText` partner line (§1.12) at game start (8 s) and at each meeting reminder. Nothing is shown to others.

**Edge cases**: partner disconnects → survivor dies (above); both lovers die in the same frame → second `Follow` finds partner dead → no-op; pending bite on the partner already (vampire) → skipped (they die anyway); haison → no assignment; compat → no roles; test mode → `EndGame(Lovers) suppressed: test mode` in the log; guardian angel: lovers are custom-role players → plain ghosts (existing `Assign_RpcSetRolePatch`).

**Solo test (PC host + phone)**: `/test on`, `/set lovers 1`, `/assign <phone> lovers`, `/assign <me> lovers` → start. Expect log `TestMode: forced #… -> Lovers`, `Lovers: #a … & #b …`, `Role assignment … -> Lovers [Neutral]` twice; both screens show ♥ before the partner's name; role chat after 8 s with `あなたの恋人: …`. Kill the phone → log `Lovers: … died → … follows in 0.5 s` then `Kills: lovers on … executes`.

---

## 3. 放火魔 / Arsonist (Implementer 2)

**Files**: `src/Game/Arsonist.cs`; hooks wired in §1 (`Kills.HandleCheckMurder` case, `CanVent`, `CanSheriffKill`, `OnMurder`, `VotingComplete`, `WinConditions.Evaluate`, `NameTags`, `OptionsDesync`, killers-first order).

**Client view**: `ImpostorDesync = true` → the arsonist's own client holds Impostor (kill button = douse), everyone else sees it as Crewmate, it sees everyone as Crewmate (`RoleAssignment.View`). `OptionsDesync.BuildFor` sets its `KillCooldown` = `ArsonistDouseCooldown` (impostor vision kept, like the Jackal). Fake tasks (`TasksCount=false`, desync skip in `Win_RecomputeTaskCountsPatch`). Vent by option, sabotage blocked (existing `HandleSabotage/HandleEnterVent`).

**Douse** (`Arsonist.Douse(killer, target)`, reached only after `IsValidMurder` passed, so both alive, no meeting, target not in vent / GA-protected):
```csharp
byte a = killer.PlayerId, t = target.PlayerId;
if (!Game.Doused.TryGetValue(a, out var set)) Game.Doused[a] = set = new HashSet<byte>();
if (set.Contains(t)) { Rpc.FailKill(killer, target); if (ShouldNotice(a, t)) Kills.Notice(a, "kill.douse.already", "{0} にはもう油をかけています。", "{0} is already doused.", Game.NameOf(t)); return; }
Rpc.ResetKillCooldown(killer, Options.ArsonistDouseCooldown);   // Vampire pattern; host: SetKillTimer
set.Add(t);
int left = Remaining(a);
PocketRolesPlugin.Logger.LogInfo($"Kills: Arsonist {Game.NameOf(a)} doused {Game.NameOf(t)} ({left} left)");
Kills.Notice(a, "kill.douse", "{0} に油をかけました（残り {1} 人）。", "You doused {0} ({1} left).", Game.NameOf(t), left);
NameTags.RefreshAll();                                           // ♨ to the arsonist only
if (left == 0) { PocketRolesPlugin.Logger.LogInfo($"Arsonist: {Game.NameOf(a)} doused everyone → win"); WinConditions.Check(); }
```
Keep a private `LastAlreadyNotice` dictionary with the same 10 s throttle as `Kills.ShouldNotice` (copy the helper; clear it from `Kills.ResetNotices`).
`Remaining(a)` = alive players ≠ a not in `set`. `WinnerId(exiledId)`: for each alive arsonist ≠ exiledId whose set contains every other alive player ≠ exiledId → its id; else 255. `OnPlayerDied(id)`: `if (Game.RoleOf(id) == Arsonist) Game.Doused.Remove(id)` (douses vanish; called for kills and exiles). Doused players who die simply stop counting; the win is re-evaluated by every `WinConditions.Check` (kills) and at WrapUp (`CheckNow`), so "last undoused player dies" also wins.

**Win**: `Outcome.Arsonist` → `EndGame(WinKind.Arsonist, id)` → winner = the arsonist (+ alive Opportunist), text `放火魔勝利 (name)`, colour `#ff6633`. Test mode: only the log line above (`EndGame` suppressed).

**Other interactions**: Sheriff may shoot it (§1.6); Snitch does not mark it (non-goal); Madmate/impostors see nothing; it counts as "others" in the crew/impostor count rules; a Witch can spell it, a Vampire can bite it; it never becomes GA.

**Edge cases**: dousing the host when the host is Sheriff/Jackal → goes through the normal switch (not the host-target block) → fine; two arsonists → separate sets, each must douse the other; douse during `Game.Ending` → `FailKill` (existing guard); compat → `CompatRisky` blocked (no roles there anyway); haison n/a.

**Solo test**: `/test on`, `/assign <me> arsonist` (or the phone), 2–3 players. Expect intro "インポスター" on the arsonist client, kill button; press on a player → nothing happens to the target, cooldown restarts (`ArsonistDouseCooldown`), chat `… に油をかけました（残り N 人）`, ♨ before that name on the arsonist's screen only; log `Kills: Arsonist … doused … (N left)`; last douse → log `Arsonist: … doused everyone → win` and (test mode) `EndGame(Arsonist) suppressed: test mode`.

---

## 4. 魔女 / Witch (Implementer 3)

**Files**: `src/Game/Witch.cs`; hooks wired in §1 (`Kills.HandleCheckMurder` case, `OnMurder`, `VotingComplete`, `ExileWrapUp`, `NameTags`, `OptionsDesync`, `RoleInfoText`).

**Client view**: a real vanilla Impostor (`FromImpostorPool`), like Vampire/Mafia: normal impostor intro, sees other impostors, vent/sabotage. Only the kill button is redirected.

**Spell** (`Witch.Spell(witch, target)`, after `IsValidMurder`):
```csharp
byte w = witch.PlayerId, t = target.PlayerId;
if (Game.Spelled.ContainsKey(t)) { Rpc.FailKill(witch, target); return; }        // already cursed (by any witch)
Rpc.ResetKillCooldown(witch, SpellCooldown());                                    // SpellCooldown() = Options.WitchSpellCooldown > 0 ? that : Kills.LobbyKillCooldown()
Game.Spelled[t] = w;
PocketRolesPlugin.Logger.LogInfo($"Kills: Witch {Game.NameOf(w)} cursed {Game.NameOf(t)} ({Game.Spelled.Count} pending)");
Kills.Notice(w, "kill.spell", "{0} に呪いをかけました。次の会議の後に死亡します。", "You cursed {0}. They die after the next meeting.", Game.NameOf(t));
NameTags.RefreshAll();   // † for the witch (and the target when WitchSpelledSeeMark)
```
The target sees nothing (no RPC reaches it; the mark only with the option).

**Curse** (`OnMeetingEnd(exiledId)` from the WrapUp postfix, after the existing bite postponement):
```csharp
if (Game.Spelled.Count == 0) return;
int cursed = 0;
foreach (var kv in new List<KeyValuePair<byte, byte>>(Game.Spelled))
{
    byte t = kv.Key, w = kv.Value;
    if (t == exiledId || !Game.IsAlive(t) || w == exiledId || !Game.IsAlive(w) || Game.Bites.ContainsKey(t)) continue;
    Game.Bites[t] = new Game.VampireBite { Killer = w, DueAt = Time.time + 2f, Reason = "curse" };
    cursed++;
}
Game.Spelled.Clear();
if (cursed > 0) { PocketRolesPlugin.Logger.LogInfo($"Witch: curse strikes {cursed} player(s) 2 s after the exile screen"); HrChat.All(HrChat.Title, () => Lang.T("kill.curse.all", "魔女の呪いが発動しました。", "The witch's curse strikes.")); }
```
The 2 s delay matches the postponed-bite rule (after `AntiBlackout.Restore` at 1.5 s). Victims die one after another in the same frame via `Rpc.Kill(victim, victim)`; `OnMurder` credits the witch (`bite.Killer`) so a Bait victim forces the witch to report. `OnPlayerDied(id)`: `Game.Spelled.Remove(id)`; `if (Game.RoleOf(id) == Witch)` remove every entry whose value is `id` (curse fades; log). `OnPlayerExiled(id)` = `OnPlayerDied(id)` (called at `VotingComplete`, i.e. before `OnMeetingEnd`, so an exiled witch's spells are already gone). `SpelledNamesFor(w)` = comma-joined names of alive targets with value `w` (meeting reminder line).

**Options**: `witch.cooldown` (0 = lobby kill cooldown; when > 0 `BuildFor/NeedsCustomOptions/GetKillCooldown` apply it so the very first spell timer is right too), `witch.mark`.

**Edge cases**: spelled player disconnects → skipped at WrapUp; report between spell and meeting → spells survive (`FlushBites` touches bites only); meeting ends the game (Jester/Terrorist/CheckNow) → pending bites are ignored (`Kills.Tick` guards `Ending`); witch killed during play → spells cleared immediately; two witches → per-owner removal; curse victim in a vent at +2 s → `ExecuteBite` postpones 1 s (existing); haison/compat n/a; test mode → deaths happen (only `EndGame` is suppressed).

**Solo test**: `/test on`, `/assign <me> witch`, phone crew. Kill button on the phone → phone unaffected, host cooldown resets, chat `… に呪いをかけました…`, † before the phone's name on the host; log `Kills: Witch … cursed …`. Call a meeting, skip → after the exile screen expect `Witch: curse strikes 1 player(s)…`, then `Kills: curse on … (by …) executes` ~2 s later; phone shows its self-kill animation. Repeat with the witch voted out → log `Witch: … exiled, N spell(s) cleared` and nobody dies.

---

## 5. アサシン / Assassin (Implementer 4)

**Files**: `src/Game/Assassin.cs`; hooks wired in §1 (`Commands` guess case, `IsEveryoneCommand`, `MeetingsHeld/GuessesThisMeeting`, `Kills` host-target list, assignment gate, `TestMode.FindPlayer` internal).

**Client view**: real vanilla Impostor (`FromImpostorPool`), normal kills (vanilla `CheckMurder` path; the host-target block handles a host Sheriff/Jackal/Arsonist target).

**Command privacy** (settled): `/cmd guess …` from a client reaches only the host in a registered lobby (server behaviour the mod already documents); the host cannot hide a plain `/guess` that other clients already received (`Chat_AddChatPatch` hides it on the host's screen only). Therefore: accept both, but when `explicitCmd == false` append `guess.public`. The host as Assassin types either form (handled by `Chat_SendChatPatch`, never sent). Requires `[Chat] PlayerCommands` + `AllCommands` (else `Commands.GatedPlayer` swallows it) → `Assassin.Enabled` gate in assignment (§1.10).

**`Guess(sender, args, explicitCmd)`** — returns the private reply text; checks in this order (each returns the listed key): not `IsHostActive/InProgress` → `cmd.nogame` (reuse the existing key if present, else a plain sentence); sender role ≠ Assassin → `guess.notassassin`; sender dead → `guess.dead`; `MeetingHud.Instance == null` or `state` ∉ {Discussion, NotVoted, Voted} → `guess.nomeeting`; `!Options.AssassinCanGuessFirstMeeting && Game.MeetingsHeld <= 1` → `guess.firstmeeting`; `GuessesThisMeeting[s] >= Options.AssassinGuessesPerMeeting` → `guess.limit`; tokens < 2 → `guess.usage`; last token = role text, the rest = name → `TestMode.FindPlayer(name)` null → `guess.noplayer`; target == self → `guess.self`; target dead/disconnected (or GM host) → `guess.targetdead`; role text unparsable → `guess.badrole`. Then `GuessesThisMeeting[s]++`, resolve, kill, reply `guess.correct` / `guess.wrong` (+ `guess.public`).

Role parsing (`TryParseGuess(text, out CustomRole custom, out RoleTypes vanilla, out string label)`) — **keywords first, then `Roles.TryParse`** (zh prefix matching would otherwise turn `内鬼` into Madmate): `crew|crewmate|c|クルー|クルーメイト|船员` → `RoleTypes.Crewmate`; `imp|impostor|i|インポスター|内鬼` → `RoleTypes.Impostor`; vanilla specials by enum name or `Chat.VanillaRoleName(r)` in ja/en/zh (Scientist, Engineer, Noisemaker, Tracker, Detective, Judge, Shapeshifter, Phantom, Viper) → that `RoleTypes`; else `Roles.TryParse` (custom role, label = `Roles.Info(custom).Name`). Match: `tr = Game.RoleOf(t)`; custom guess → `tr == custom`; vanilla guess and `tr != None` → false; `vanilla == Impostor` → `RoleAssignment.IsImpostorRole(Game.VanillaRoleOf(t))`; else exact `VanillaRoleOf(t) == vanilla`.

**Kill** (`victim` = target if correct else the assassin):
```csharp
if (KillDuringMeeting && !Rpc.SafeMode)
{
    var pc = Game.Player(victim);
    Rpc.ExileSilently(pc);                                     // Exiled RPC to all + local pc.Exiled(): dies without a body (GameMaster precedent)
    try { pc.Data.IsDead = true; pc.Data.MarkDirty(); Rpc.SendPlayerInfo(pc.Data); } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"Assassin: data sync: {e.Message}"); }   // every client's GameData says dead → vote area X (vanilla MeetingHud.Update)
    RoleAssignment.SendGhostRole(pc);                          // per-viewer ghost roles (Impostor victims: ImpostorGhost for impostor viewers)
    var hud = MeetingHud.Instance;
    try { hud.RpcClearVote(victim); hud.CheckForEndVoting(); } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"Assassin: vote clear: {e.Message}"); }   // drop its vote; end the vote now if everyone else already voted
    Witch.OnPlayerDied(victim); Arsonist.OnPlayerDied(victim); Lovers.OnPlayerDied(victim);
    NameTags.ScheduleMurderRefresh();                          // the Data sync resets private names
    HrChat.All(HrChat.Title, () => Lang.TF("guess.killed.all", "{0} は暗殺されました。", "{0} was assassinated.", Game.NameOf(victim)));
}
else
{
    Game.Bites[victim] = new Game.VampireBite { Killer = assassinId, DueAt = Time.time, Reason = "assassin" };   // executes 2 s after the exile screen
    HrChat.All(HrChat.Title, () => Lang.TF("guess.killed.later", "{0} は暗殺されました。会議の後に死亡します。", "{0} was assassinated and dies after the meeting.", Game.NameOf(victim)));
}
PocketRolesPlugin.Logger.LogInfo($"Assassin: {Game.NameOf(assassinId)} guessed {Game.NameOf(target)} = {label} → {(correct ? "correct" : "wrong")}; {Game.NameOf(victim)} dies ({(KillDuringMeeting ? "now" : "after the meeting")})");
```
`AntiBlackout.Prepare` runs at `VotingComplete` from `Game.IsDead` → the mid-meeting death is included; `WinConditions.CheckNow` at WrapUp ends the game if the assassin was the last impostor (crew win) or the victim the last crew.

**Edge cases**: guess after the vote closed (`Results/Proceeding`) → `guess.nomeeting`; target already voted → `RpcClearVote` + the Mayor tally skips `AmDead` (§1.7); target is a lover → partner follows after WrapUp; target is a Witch → its spells clear; `/cmd g` alias; wrong-guess host death → `ExileSilently(lp)` works for the host (GameMaster precedent); compat/SafeMode → automatically the fallback branch; test mode → deaths apply, only `EndGame` suppressed; guess limit resets at every `MeetingHud.Start`.

**Solo test**: `/test on`, `/assign <me> assassin`, `/assign <phone> sheriff`, start; open a meeting; on the host type `/cmd guess <phone> mayor` → reply `外れ！… あなたが死亡しました。`, host becomes a ghost in the meeting, log `Assassin: … guessed … = メイヤー → wrong; … dies (now)`. Next game: `/cmd guess <phone> sheriff` → phone's vote area gets ✕ on both screens, all chat `… は暗殺されました。`. If the ✕ does not appear on the phone or the vote continues incorrectly, set `KillDuringMeeting = false` and re-test.

---

## 6. Risks (not confirmable from the API dump — stubs only, no bodies) and fallbacks

1. **Killing during a meeting** (`Rpc.ExileSilently` + Data sync while `MeetingHud` is open). Evidence for: `MeetingHud.Update()`, `SetForegroundForDead()`, `hasForegroundForDeadBeenSet`, `PlayerVoteArea.SetDead(bool)`, `RpcClearVote`, and vanilla's own handling of disconnects/late deaths mid-meeting; the mod already uses `ExileSilently` outside meetings (Game Master) and the Exiled RPC is accepted from the host in registered lobbies. **Fallback**: `Assassin.KillDuringMeeting = false` → death applied as a bite 2 s after the exile screen, with the public chat line announcing it.
2. **Cancelling a kill via `CheckMurder`** (Arsonist douse, Witch spell): identical to the shipped Vampire path. Lowest risk. If a client's button ever stays locked, `Rpc.FailKill` after the reset is the known "unlock".
3. **Symbols ♥ † ♨ in the client font** (★ and Ⓜ are proven). Fallbacks in one constant each: `♡`/`❤`, `X`/`✝`, `▲`/`火`.
4. **Impostor lover counting**: `IsImpostorTeamKiller` now returns true for a Lovers role on a vanilla Impostor; `TeamOf` = Neutral means it does **not** win with the impostors — intended.
5. **Lovers rule precedence vs Jester/Terrorist** (they bypass `Evaluate`): documented.
6. **Shapeshift / Phantom / Viper**: Witch/Assassin are only drawn from plain `Impostor`; no shapeshift interception is needed.
7. **Mid-meeting death and the vanilla tally**: the Mayor replacement now skips `AmDead` explicitly; `RpcClearVote` covers the rest. Verify in the phone test that a vote cast by the assassinated player is not counted.
8. **`Kills.Tick` during the intro**: guarded now; the only new path that can queue a bite during the intro is a lover disconnecting.

Build/verify: `dotnet build -c Release` with the game closed (0 errors expected), then the lang parity diff (§1.14), then the solo/phone tests. All logs go through `PocketRolesPlugin.Logger`; the expected lines are quoted above.
