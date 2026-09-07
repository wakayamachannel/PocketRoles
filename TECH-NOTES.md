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
