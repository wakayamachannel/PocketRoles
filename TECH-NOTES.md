# HostRoles — Verified technical notes for Among Us 2026.8.18 (v18.0.0)

Sources: live source of EndlessHostRoles (EHR, targets 2026.8.18), TOHE (2025.9.9), TOH, Innersloth's official
`AmongUsModdingInformation` README and mod policy (2026-07-30), plus the decompiled interop of THIS game build
(`%SCRATCH%/api/AssemblyCSharp/**`). Everything below was confirmed against at least one of those.

## Wire formats (Hazel)

Root message tags: `5` = GameData (broadcast), `6` = GameDataTo (one client), `26` = PackedGameDataTo (container).
Sub-message tags inside 5/6: `1` = Data (InnerNetObject serialization), `2` = RPC.

```
GameData (broadcast):   w.StartMessage(5); w.Write(GameId); [sub-messages]; w.EndMessage();
GameDataTo (1 client):  w.StartMessage(6); w.Write(GameId); w.WritePacked(clientId); [sub-messages]; w.EndMessage();
RPC sub-message:        w.StartMessage(2); w.WritePacked(netId); w.Write((byte)callId); ...payload...; w.EndMessage();
Data sub-message:       w.StartMessage(1); w.WritePacked(netId); obj.Serialize(w, false); w.EndMessage();
then: AmongUsClient.Instance.SendOrDisconnect(w); w.Recycle();
```
`MessageWriter.Get(SendOption.Reliable)`. Keep one packet ≤ 1200 bytes including header (official limit); split at ~500 bytes like TOHE.
Single targeted RPC shortcut: `var w = AmongUsClient.Instance.StartRpcImmediately(netId, (byte)call, SendOption.Reliable, clientId); ...; AmongUsClient.Instance.FinishRpcImmediately(w);` (`clientId = -1` broadcasts).
`PlayerControl.OwnerId` (InnerNetObject) == the client id that owns the player. Host: `AmongUsClient.Instance.ClientId`, `AmongUsClient.Instance.HostId`.

### RPC payloads (2026.8.18)
| RPC | id | payload written by host |
|---|---|---|
| SetRole | 44 | `ushort role`, `bool canOverride` (**always true**) — send on `player.NetId` |
| SetName | 6 | `uint player.Data.NetId`, `string name`, `bool false` (trailing bool exists in 2026; if true the receiver discards) — on `player.NetId` |
| SendChat | 13 | `string text` — on the **host's own** `PlayerControl.NetId` (official server only relays chat from the owning connection) |
| MurderPlayer | 12 | `WriteNetObject(target)` (= `WritePacked(target.NetId)`), `int flags` (`MurderResultFlags`) — on `killer.NetId` |
| ProtectPlayer | 45 | `WriteNetObject(target)`, `int colorId` — on protector's NetId |
| Exiled | 4 | *(empty)* — on the exiled player's NetId; receiver runs `Exiled()` (dies without body) |
| VotingComplete | 23 | `VoterState[] states` (`byte VoterId, byte VotedForId` each), `byte exiledPlayerId(255 none)`, `bool tie`, `bool wasOverruled`, `ushort overruleNonce` — use `MeetingHud.RpcVotingComplete(states, exiled, tie, false, 0)` |
| CastVote | 24 | from clients: `byte playerId, sbyte suspect` — handled by vanilla `MeetingHud.CastVote` |
| CheckMurder | 47 | from clients: `WriteNetObject(target)` — vanilla routes to host `PlayerControl.CheckMurder(target)` (patch this) |
| EnterVent | 19 | from clients: `WritePacked(ventId)` on `PlayerPhysics.NetId` |
| BootFromVent | 34 | `PlayerPhysics.RpcBootFromVent(ventId)` |
| UpdateSystem | 35 | from clients to host: `byte systemType, NetObject player, byte amount` — vanilla routes to `ShipStatus.UpdateSystem(SystemTypes, PlayerControl, MessageReader)` (patch this to block sabotage) |
| SyncSettings | 2 | legacy — do NOT use for per-client options |

`MurderResultFlags`: Succeeded=1, FailedError=2, FailedProtected=4, DecisionByHost=8.
Vanilla `RpcMurderPlayer(target, didSucceed)` sends `Succeeded` or `FailedError|DecisionByHost`. A vanilla client that receives
`FailedProtected` for a murder where it is the killer plays the "protected" flash and resets its kill timer to
**half** of its current `KillCooldown` option. That is the cooldown-reset trick (see below).

### Per-client game options (settings desync)
Not an RPC. It is a Data update of the GameManager's `LogicOptions` component, targeted to one client:
```csharp
byte idx = index of the LogicOptions component in GameManager.Instance.LogicComponents (TryCast<LogicOptions>() != null) — compute at runtime
Il2CppStructArray<byte> bytes = GameOptionsManager.Instance.gameOptionsFactory.ToBytes(opts, false);
var w = MessageWriter.Get(SendOption.Reliable);
w.StartMessage(6); w.Write(AmongUsClient.Instance.GameId); w.WritePacked(clientId);
  w.StartMessage(1); w.WritePacked(GameManager.Instance.NetId);
    w.StartMessage(idx); w.WriteBytesAndSize(bytes); w.EndMessage();
  w.EndMessage();
w.EndMessage();
AmongUsClient.Instance.SendOrDisconnect(w); w.Recycle();
```
Clone the true options with `factory.FromBytes(baseBytes)` (base = `factory.ToBytes(GameOptionsManager.Instance.CurrentGameOptions, false)` captured at game start), then `opts.SetFloat(FloatOptionNames.KillCooldown / CrewLightMod / ImpostorLightMod / PlayerSpeedMod, v)`.
Vanilla re-broadcasts the true options whenever the host marks them dirty (lobby edits, sometimes game start), so re-send per-client options after roles are dispatched, at intro end, and after every meeting.
Vision: crewmates use `CrewLightMod`, impostor-basis clients use `ImpostorLightMod`; during Electrical sabotage crew vision is scaled down by the game itself. A Sheriff (Impostor basis) gets crew vision by setting `ImpostorLightMod = CrewLightMod` in *their* options.
Kill cooldown per client = that client's `KillCooldown` option (they compute their own timer).
**Host-local** modifications must NOT go through `LogicOptions.SetGameOptions` (it marks dirty and broadcasts to everyone). Patch the effect for the local player instead: `LogicOptions.GetKillCooldown()` postfix, `LogicOptions.GetPlayerSpeedMod(PlayerControl)` postfix, `ShipStatus.CalculateLightRadius(NetworkedPlayerInfo)` postfix.

### Kill-cooldown reset for a vanilla client (EHR "SetKillCooldown + RpcGuardAndKill")
1. Send that client options with `KillCooldown = wanted * 2`.
2. Send **only to that client** an RPC MurderPlayer on `killer.NetId` with target = killer, flags = `FailedProtected` (no ProtectPlayer needed).
3. ~0.5 s later send the client its normal options again (`KillCooldown = wanted`).
Host itself: `PlayerControl.LocalPlayer.SetKillTimer(wanted)`.
A *rejected* kill that should just fail without a reset: `killer.RpcMurderPlayer(target, false)` (vanilla client shows failed kill, button stays usable).

### Private chat to one client (TOHE `SendMessage`)
Three RPCs in one GameDataTo(6) message for that client, all on the host's `PlayerControl.NetId`:
`SetName(host → title)` , `SendChat(text)` , `SetName(host → the name that client should see for the host)`.
The bubble is rendered with the sender's name at reception time, so the title appears as the sender.
If the host is **dead**, living vanilla clients hide its chat: temporarily set `host.Data.IsDead = false`, send that
NetworkedPlayerInfo as a Data message (`SetDirtyBit(uint.MaxValue)` + explicit send), send the chat, then ~1 s later set
`IsDead = true` and send again; afterwards re-send private names (a NetworkedPlayerInfo Data message carries the outfit
name and resets the private name on that client).
Chat validation on official servers: avoid many hex-colour tags and digits in chat text. Convert digits outside rich-text
tags to full-width (`０-９`), use at most one `<color=#rrggbb>` tag per message, keep a message ≤ ~120 characters,
split longer text at newlines into several SendChat RPCs, ≤ 2 chat RPCs per second per client.
Host-local chat: `HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, text)` (no network).
With the +25 host-authority flag, a non-host message starting with `/cmd` is delivered ONLY to the host by the server
(vanilla players can send private commands: `/cmd n`). Without it every player sees the command text.
A vanilla client closes the chat box for a LIVING player outside a meeting, but nothing stops a modded one from sending
`/cmd` anyway, so no reply may carry the running game's state: v0.5.5 answers such a `/cmd n` with the role text that
player was last sent (`Chat.LastRoleInfo`, decided in `RoleCmdCore.Decide`, one answer per 10 s per player, and the
slot is only spent where an answer is really produced). "Outside a meeting" is `MeetingHud.Instance != null &&
ExileController.Instance == null && IntroCutscene.Instance == null`, like the rest of the mod, so the intro and the
ejection screen count as the round. Which lines of a role text read the running game is declared per line
(`RoleCmdCore.Settled` / `LiveState`) and applied in one place (`RoleCmdCore.Compose`).

### Names
`SetName` is per-client, so each viewer can see a different string for every player (TextMeshPro rich text works on all
platforms: `<color=#f8cd46>`, `<size=70%>`, `\r\n`). EHR strips `color=` and uses `<#rrggbb>` short tags to save bytes —
both work. Always keep `Game.OriginalNames` and broadcast them back at game end.
Any NetworkedPlayerInfo Data sync from the host (a death, a disconnect, AntiBlackout) resets the viewer's copy of that
player's name to the host's `Data.PlayerName` → re-send private names ~0.5 s after such events (dedupe with a
`(viewer,target) → lastSent` cache, `force` to bypass).

### Role assignment
* Vanilla host: `AmongUsClient.CoStartGameHost` → `RoleManager.SelectRoles()` → `LogicRoleSelectionNormal.AssignRolesForTeam` → `player.RpcSetRole(role)` per player (broadcast + local `CoSetRole`).
* Block the broadcasts with a `[HarmonyPriority(Priority.High)]` prefix on `PlayerControl.RpcSetRole` while a `Collecting` flag is set; record `(player, role)` and apply locally with `player.StartCoroutine(player.CoSetRole(role, true))`.
* Then send per client one GameDataTo batch of `SetRole` RPCs: **all other players first, the client's own role last** (the own role triggers the intro/team screen on that client). Send batches spaced ≥ 0.1 s apart (rate limit).
* Desync rule (EHR/TOHE): a non-impostor with a kill button (Sheriff, Jackal) sees themselves as `Impostor` and every other player as `Crewmate`; everyone else sees them as a crewmate-looking role (`Crewmate`); real impostors see each other normally.
* A vanilla client that becomes a desync-Impostor sees the vanilla "Impostor" intro with only themselves on the team — known and accepted; the private name tag and the chat explain the real role.
* `canOverrideRole` must be true for every SetRole after the first, otherwise the client ignores it.
* Ghost roles: when a player dies vanilla host calls `RpcSetRole(CrewmateGhost/ImpostorGhost)` (via `LogicRoleSelectionNormal.OnPlayerDeath` / `RoleManager.AssignRoleOnDeath`); intercept in the same `RpcSetRole` prefix and send our own (ImpostorGhost for true impostor-team killers, CrewmateGhost otherwise; GuardianAngel only for plain vanilla crew).
* `PlayerControl.roleAssigned` and `hasBeenSerialized` exist. AUR notes: on 2026.8.18 the server kicks the host for "hacking" if Start is pressed while some player is not yet serialized — guard `GameStartManager.BeginGame` with `AllPlayerControls.All(p => p.hasBeenSerialized)`.

### Meetings
* Host owns `MeetingHud`; clients send CastVote; host `CheckForEndVoting()` tallies and calls `RpcVotingComplete(states, exiled, tie, wasOverruled, nonce)`.
* Extra votes (Mayor): add duplicate `VoterState { VoterId = mayorId, VotedForId = x }` entries (vanilla clients draw one icon per entry) and add to the tally.
* 2026 field names: `PlayerVoteArea.PlayerId` (was TargetPlayerId), `PlayerVoteArea.VotedForId : PlayerId`, `DidVote`, `AmDead`; constants `PlayerVoteArea.HasNotVoted / MissedVote / SkippedVote / DeadVote` (static bytes, read at runtime). `MeetingHud.TryGetWinningOverrule(out JudgeOverrule, out NetworkedPlayerInfo judge, out NetworkedPlayerInfo overruled)` exists for the vanilla Judge.
* Exile screen: `ExileController.BeginForGameplay(NetworkedPlayerInfo, bool voteTie, bool wasOverruled)` … `WrapUp()`.

### AntiBlackout (mandatory when impostor counts are desynced)
At `ExileController.WrapUp` every vanilla client evaluates `LogicGameFlow.IsGameOverDueToDeath()` from ITS OWN view of roles
(`impostorsAlive == 0 || impostorsAlive >= crewAlive`). If that is true but the host does not end the game, the client
stays on a black screen. Cases: a Jackal killed the last real impostor; the exiled player is the last impostor-looking player
in some view while a Jackal lives; a desync-Impostor (Sheriff) with only one other player alive.
EHR fix ("optimal role types"): before sending VotingComplete, for every viewer whose view would be "over" while the host
continues, send that viewer a temporary `SetRole(dummy → Impostor)` for some alive non-exiled player (never the viewer
itself, never the exiled); if ≤ 2 players would be alive, additionally "revive" one dead player for that viewer
(`Data.IsDead=false` Data message + `SetRole(Crewmate)`). After `WrapUp` + ~1.5 s send the true views back
(`SetRole(View(viewer, dummy))`, restore `IsDead`, re-send ghost roles and names). If the exile itself would remove the
last impostor in a view, alternatively send `RpcVotingComplete(states, exiled: null, tie: false, …)` and perform the exile
after WrapUp via the `Exiled` RPC (`Rpc.ExileSilently`), telling everyone by chat who was ejected.

### Ending the game for vanilla clients
`GameManager.Instance.RpcEndGame(GameOverReason reason, bool showAd)`; each client decides Victory/Defeat from its
local role's `DidWin(reason)`. So before ending: broadcast `SetRole(ImpostorGhost)` to winners and `SetRole(CrewmateGhost)`
to losers (EHR uses ghost variants for everyone, reviving nobody), wait ~0.3 s, then `RpcEndGame(ImpostorsByKill, false)`
(for a crew-team win you may instead set winners → CrewmateGhost, losers → ImpostorGhost and send `CrewmatesByVote`).
Set `GameManager.Instance.ShouldCheckForGameEnd = false` first. Winners' names can carry the role text. After the end screen
send a chat summary in the lobby and restore names/options.

### Ending a running game in an unregistered (compat) lobby (verified 2026-09-21)
`GameManager.RpcEndGame(GameOverReason.ImpostorDisconnect, false)` (reason 5) is accepted by the official server during a
running compat game even while impostors are still connected, and during a meeting (discussion / voting). Live logs of the
F7 haison: a 15-player public lobby, F7 during the vote — `EndGame(8)` body `…0500` sent 21:39:48.533, the echo 9 ms later,
the host back in the lobby 21:39:51.36 (HaisonReturn), the first player 21:39:52.99 and 9 players within 13 s; a 3-player
MuMu test, EndGame 18:57:00.944 during a meeting, the echo 135 ms later, host back 18:57:03.99, the players 0.2 s and 26 s
after the host. No "Hacking" disconnect in either. Every client shows the vanilla "impostor disconnected" end screen (crew
win, impostors lose; support/verify-findings #8): the result means nothing, a match cannot be voided.
- Players come back only when they press "play again" themselves (0.2–30 s after the host); a chat line sent before a
  player is back never reaches that player. Post-game lines therefore wait until the players are back (AegisMatchStop:
  60 % of the match's players and 3 s after the first one, or 12 s with anyone back for 3 s; dropped after 45 s with
  nobody). A line goes out once, so the stop's line is sent a second (last) time when more players came back after it:
  all of them back, or 20 s after the first line (3 s after the last arrival; given up 60 s after the first line or when
  the next game starts). Players are counted, not identified: a newcomer can count as one who came back.
- `AllClientIds` lists a client as soon as it joined, before its objects are spawned; every lobby line therefore waits
  3 s after the (first / last) arrival it is meant for.
- Never end during the intro or the loading before it: a compat game ended on a stuck intro got the host disconnected for
  "Hacking" (15 players, CHANGELOG v0.4.4). Never on the vote result / exile screens either: every client decides the end
  of an exile in WrapUp from its own view and waits on a black screen until the host ends the game (AntiBlackout).
  AegisMatchStop sends the end 0.5 s after those screens close and sets `HaisonActive` only in the frame it sends (set
  earlier, the vanilla end check after WrapUp would be skipped and the clients would sit on the black screen).
- An end vanilla already sent (e.g. the removed player was the last impostor) is never followed by a second one: the
  echo can take 100 ms and more while `IsGameStarted` is still true, so a `GameManager.RpcEndGame` postfix records it.
- `Haison.EndNow` turns `ShouldCheckForGameEnd` and `ShipStatus.enabled` off before `RpcEndGame`. When no end happens
  (RpcEndGame throws, or the end is not confirmed within 8 s) both must be given back: with the end checks down vanilla
  also refuses emergency meetings and reports ("waiting for the host") and no natural end can happen.
- Whether a relayed kill landed is only known after vanilla applied it: a `Succeeded` flag without `DecisionByHost` leaves
  a Guardian Angel's target alive. CheatDetector's HandleRpc prefix queues the removal as "kill pending"; a
  `PlayerControl.MurderPlayer` postfix (same call, before the next Tick) marks it landed when the target (alive before) is
  dead after it — the same check as `Kills.OnMurder`.
- Not verified yet in a live compat game: ending outside a meeting (a round with players in vents, tasks, sabotages, the
  Airship spawn picker). `/ac test <rule> <name> stop <seconds>` tests it with 3 devices (the stop alone, nobody removed:
  in a 3-player game any real removal leaves 1 against 1 and vanilla ends the game first).

### Registration (+25 host-authority flag) — REQUIRED by Innersloth's policy since 2026-07-30
Policy: "Any mod that changes the functionality of Among Us on official servers must be registered on lobby creation …
custom role behavior … modifying any part of a peer's experience."  Host-only mods register by adding 25 to the protocol
version ("host authority mode": server-side validation for most gameplay switches to the host; `/cmd` chat routing).
The vanilla client already contains helpers: `Constants.GetBroadcastVersion()`, `Constants.IsVersionModded()`,
`Constants.MODDED_REVISION_MODIFIER_VALUE` (=25), `Constants.GetVersionComponents(int)`.
EHR: `[HarmonyPatch(typeof(Constants), nameof(Constants.GetBroadcastVersion))] Postfix(ref int __result) { var revision = __result % 50; if (revision < 25) __result += 25; }` and `Constants.IsVersionModded` prefix → `__result = true; return false;`.
Only apply while **hosting** (set a flag in a prefix of `AmongUsClient.CoCreateOnlineGame`, clear it in prefixes of
`CoJoinOnlineGameFromCode/FromListing/Direct`), so a HostRoles user can still join other people's vanilla lobbies.
Known effect: registered/modded lobbies are reportedly NOT listed in the vanilla public lobby browser (Innersloth Help Center
"modded games are not able to be found via public lobby search"); players join by room code. Rate limits still apply
(mods self-limit to ~20 GameData packets/s and ≤ 1200-byte packets).

### Rate limiting
Official servers kick the host ("hacking") for bursts. Pace outgoing batches through a queue: ≤ 1 packet per 0.1 s for bulk
(role/name/options batches); urgent single RPCs (kills) may go immediately. Never loop `SendOrDisconnect` for 15 clients
in one frame.

### Anti-cheat on the host (hackers are common on official servers)
Incoming RPCs that only the host may legitimately send: SetRole(44), SetName(6), MurderPlayer(12), Exiled(4),
ProtectPlayer(45), StartMeeting(14), SyncSettings(2), SetInfected(3), SetTasks(29), Shapeshift(46), StartVanish(63),
StartAppear(65); and on MeetingHud: VotingComplete(23), CloseMeeting(22). The host never receives its own RPCs through
`HandleRpc`, so any of these arriving at the host is forged → drop (`return false`) and optionally kick
(`AmongUsClient.Instance.KickPlayer(clientId, false)`) after N strikes.

### BepInEx config defaults and the two opt-in features (v0.5.5)
`ConfigFile.Bind(section, key, default, description)` returns the value **already in the file** whenever the entry is
present and parsable; the `default` argument is only used when the entry is absent, empty or unparsable, and Bind then
writes that default into the file. Changing a `default` therefore **never** reaches a config file that already exists —
verified against the shipped `BepInEx.Core.dll` in `tools/OptDefaultsTests` (`C2`: a file holding `Enabled = true`
still reads `true` after the bound default became `false`; `C5`/`C7`: an empty, unreadable or corrupt value falls back
to the bound default, and the file keeps the host's other settings). This is the same behaviour `NgKickAtFromTestBuild`
in `Options.cs` works around when an *old* default has to be corrected on existing files.

#### What a config file can and cannot prove about "the host never touched this" (`OptInUpgrade`, v0.5.5)
The upgrade check in `src/Core/OptInUpgrade.cs` had to decide whether an `Enabled = true` in an upgrading host's file
was chosen or inherited. The evidence that exists, measured against the shipped `BepInEx.Core.dll` (be.735):

* **`# Default value: <x>`** — written above every entry from the bound default of the build that last saved the file,
  and **rewritten by the first save of the current run**. Readable only before any `Bind()`, which is why `Scan` runs
  next to `NgKickAtFromTestBuild` at the top of `Options.Init`.
* **Nothing else.** be.735 writes no `## Settings file was created by plugin X vY` header (BepInEx 5 did; the string
  is not in this DLL), no timestamps and no "edited by the user" flag, and every launch rewrites the file, so its
  modification time carries no information either.

`[Translate] Enabled` bound `true` in every released build up to v0.5.4, so an untouched file and the file of a host
who deliberately switched translation on are **byte-identical**: `Enabled = true` under `# Default value: true`.
Nothing outside the file closes the gap — a `deepl-key.txt` would only confirm the deliberate case, the `reports`
records in `aegis-bans.json` only prove the feature ran (which it did by default), and `LogOutput.log` is overwritten
on every launch (the launcher's copies are deleted after 30 days). So the migration **changes no value**: it tells the
host once and gives them `/opt upgrade off|undo|keep`. The cases that *are* decidable are decided silently —
`# Default value: false` above a `true` is the host's own choice under this build, `= false`, a missing key (no
released build had `[AntiCheat] AutoReport`, so every v0.5.4 file lacks it) and a missing file already mean off.

The record is `BepInEx/PocketRoles/opt-defaults.txt`; while it exists the scan never runs again. It also stores the
value last seen, so a value the host sets afterwards — chat, settings tab, or editing the file with the game closed —
is marked `chosen` and leaves the migration for good (`Refresh`, plus a `SettingChanged` handler per entry for changes
inside a session). `tools/OptDefaultsTests` drives the real `Options.Init` against temporary config files for all of
it (group `G`), by pointing `BepInEx.Paths.BepInExRootPath` at a temp folder and resolving `BepInEx.Unity.IL2CPP` /
`Il2CppInterop.Runtime` out of the game's `BepInEx/core` — no Unity, no game.

Four things follow from "the evidence exists for one moment only", all four found by the 2026-09-23 review of this
migration and all four now covered by `G10`–`G13` and `I1`–`I5`:

* **The record is written by `Scan`, not by `Apply`** (`stage = scan`), i.e. before the first `Bind()` of the run
  rewrites the `# Default value:` line it was read from. `Apply` fills in the bound values and moves it to
  `stage = done`. A run killed between the two — the game closed at BepInEx's splash, a crash, a power cut — is
  finished off by the next start from the record, instead of re-reading a config file that no longer holds the answer.
* **The check cannot be replayed.** Deleting `opt-defaults.txt` does *not* bring it back, because the clue in the
  config file is already gone: from the second start on, the same file reads as `ChosenOn`. The README, the CHANGELOG
  and the record's own header say so; `/opt translate.enabled off` and `/opt anticheat.autoreport off` are what a host
  who changes their mind later actually uses.
* **The record is written whole or not at all** — into `opt-defaults.txt.tmp`, then `File.Move(tmp, path, true)` — and
  `Load` reads it only when the `version` line and every field the class uses are there. A file cut off mid-write used
  to load as "all false", which the next `Refresh` then read as "the host changed these values themselves".
* **`Save` returns whether it worked**, and a run that cannot write the record (no `BepInEx/PocketRoles` folder, a
  read-only disk) says so: `/opt upgrade off` drops the "`/opt upgrade undo` puts it back" promise, and
  `/opt upgrade keep` does not claim it will stop asking. The settings themselves are still changed correctly.

The one notice is recorded the same way. `Chat.LocalWhenReady(text, onShown)` runs `onShown` only once the line has
really reached the host's screen, and the dropped-line warning no longer builds the text just to log it. "Shown" used
to be recorded *inside* the text builder, which that warning called — so a host whose chat was not ready within 10 s
had the notice marked as given, for ever, while it had only gone to `LogOutput.log`.

Consequence for the two privacy features, chat translation (`[Translate] Enabled`) and the automatic Among Us report
(`[AntiCheat] AutoReport`): both are bound with `false` in `Options.Init`, and both property getters use the
`_entry != null && _entry.Value` form so an unbound entry (Init not run, config unreadable) reads **off**, never on.
The `_entry == null || _entry.Value` form used by the other AntiCheat switches defaults to *on* and must never be used
for a feature that sends data off the PC. Neither feature can be switched on from anywhere else: the signed definitions
file carries no key for them, and `AegisBans.TryReport` has exactly two call sites — the automatic one behind
`Options.CheatAutoReport` and the host's own `/aegis report`.

An opt-in default also moves *when* a feature starts, and three places assumed it started before anyone joined
(2026-09-23 review):
* `Chat.TranslationNotice()` only ever reached players through the welcome, and the welcome is sent from the
  join hook alone. With the feature on from the start that was enough; with it off by default, "switch it on with
  the room already full" is the normal case and nobody in the room would be told. `[Translate] Enabled` now has a
  `SettingChanged` handler (`Chat.OnTranslationEnabledChanged`) that sends the notice through
  `Chat.All(title, builder)` — one public line in an unregistered lobby, one private copy per player in a registered
  one — and re-arms the host-screen notice when it is switched off again.
* `ClientUI_TranslateNoticePatch` keyed its "translation is off" line on `AmongUsClient.GameId`, i.e. once per
  lobby. That was once per host who had deliberately switched translation off; with the new default it would be
  every host who never uses translation, in every lobby they create. It is now once per session
  (`_noticedThisSession`) and silenceable with `[Translate] OffNotice = false` (`/opt translate.notice off`).
* `Options.TrySet` answered `key = on` and nothing else. The warnings about unrecallable reports and about chat text
  leaving the PC lived only in the settings-tab tooltips, and the README sends hosts to the chat command, so
  `anticheat.autoreport` and `translate.enabled` now append `opt.warn.autoreport` / `opt.warn.translate` to the reply
  when they are switched **on** (nothing is appended when they are switched off).

### IL2CPP / Harmony gotchas (be.735, x86)
* Plugins target net6.0; `BasePlugin.Load()`, `Log`, `Config`; `Harmony.PatchAll(Assembly.GetExecutingAssembly())`.
* Coroutines: `pc.StartCoroutine(pc.CoSetRole(role, true))` — the game's `CoSetRole` returns `Il2CppSystem.Collections.IEnumerator`, which `MonoBehaviour.StartCoroutine` accepts directly. For managed coroutines use `BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp()`. Prefer the `Scheduler` (HudManager.Update postfix) over coroutines.
* Delegates: cast lambdas: `(Il2CppSystem.Action)(() => …)`, `(UnityEngine.Events.UnityAction)(() => …)`.
* Arrays: `Il2CppStructArray<byte>` ⇐ `byte[]` implicit; `Il2CppStructArray<MeetingHud.VoterState>` from a managed array via `new Il2CppStructArray<MeetingHud.VoterState>(arr.Length)` + indexer, or the `T[]` implicit conversion (VoterState is blittable: two bytes).
* `Il2CppSystem.Collections.Generic.List<T>` supports `foreach`; `.Count`, `[i]`, `.ToArray()`.
* Do not patch generated field accessors; avoid `ref` non-blittable struct parameters on x86; `PlayerId` is a blittable struct (`byte Value`) with implicit conversions to/from `byte`.
* `Il2CppObjectBase.TryCast<T>()` / `Cast<T>()`; Unity null semantics for MonoBehaviours (`if (!pc)`).
* Wrap every patch body in try/catch; a throwing prefix aborts the vanilla method.
* Patch classes must have unique names across the assembly (prefix with the module name).
