# HostRoles — Design (v0.2, implementation contract)

Host-only role mod for Among Us **2026.8.18** (Steam x86, IL2CPP, Unity 2022.3.44f1) on **BepInEx 6.0.0-be.735**.
Only the host installs it; all other players are vanilla (PC / Switch / mobile / console).
Read `TECH-NOTES.md` first (verified wire formats and tricks). The decompiled API of this exact build is at
`%SCRATCH%/api/AssemblyCSharp/**` (one file per type) with summaries in `%SCRATCH%/api/sigs/*.txt` —
**grep it before using any member**; do not rely on memory of older versions.

## 0. Rules for every implementer

* C# latest, `net6.0`, no Reactor, no NuGet game libs (`HostRoles.csproj` references `..\Among Us HostRoles\BepInEx\{core,interop}`).
* Namespace layout: `HostRoles` (plugin), `HostRoles.Core`, `HostRoles.Net`, `HostRoles.Game`, `HostRoles.Chat`. Add `using` lines as needed; `ImplicitUsings` is off.
* **Only touch the files you own** (table in §12). Cross-module calls only through the public signatures written in this document. If you need something that is not in the contract, implement a private helper in your own file.
* All host-side logic is guarded by `Game.IsHostActive`; patches on non-host clients must be no-ops (`return true` / do nothing).
* Every Harmony patch: `[HarmonyPatch(typeof(T), nameof(T.Method))]` on a `static class` whose name starts with your module name (e.g. `Kills_CheckMurderPatch`), body wrapped in `try { … } catch (Exception e) { HostRolesPlugin.Logger.LogError($"…: {e}"); }`. Prefix methods return `bool`.
* Never call `LogicOptions.SetGameOptions`/`SyncOptions` for host-local effects (it broadcasts). Never use vanilla `RpcSetRole`/`RpcSetName`/`RpcSendChat` for per-client sends — use `Rpc.*`.
* Do not trust `Data.Role` / `Data.RoleType` for true teams once a game is running (the host's local view is desynced too). Use `Game.RoleOf/TeamOf/IsImpostorTeamKiller`.
* Strings shown to players: Japanese first, English second, via `Lang.T(key, ja, en)`. Chat text: ≤ 120 chars per message, at most one `<color=#rrggbb>` tag, digits converted with `Lang.FullWidthDigits` (see Chat).
* Logging: `HostRolesPlugin.Logger.LogInfo/LogWarning/LogError`. Keep per-frame logs out.

## 1. Plugin (`src/HostRolesPlugin.cs`, owner: core)

```csharp
[BepInPlugin(Id, Name, Version)] [BepInProcess("Among Us.exe")]
public class HostRolesPlugin : BasePlugin {
  public const string Id = "jp.hostroles.mod"; public const string Name = "HostRoles"; public const string Version = "0.1.0";
  public const string SupportedGameVersion = "2026.8.18";
  public static ManualLogSource Logger; public static HostRolesPlugin Instance;
  public Harmony Harmony { get; } = new Harmony(Id);
  public override void Load() { Instance = this; Logger = Log; Options.Init(Config); Harmony.PatchAll(Assembly.GetExecutingAssembly()); Log.LogInfo(...); }
}
```
Plus a `PingTracker.Update` postfix appending `HostRoles v… (host mod)` to `__instance.text.text`, and a `HudManager.Update` postfix (host or not) that calls `Scheduler.Tick(); Rpc.Queue.Tick(); Kills.Tick();` inside try/catch (the latter two are no-ops unless `Game.IsHostActive`).

## 2. Core (`src/Core/*.cs`, owner: core)

### Roles.cs
```csharp
public enum Team { Crew, Impostor, Neutral }
public enum CustomRole { None = 0, Sheriff, Mayor, Snitch, Lighter, SpeedBooster, Bait, Madmate, Vampire, Mafia, Jester, Opportunist, Terrorist, Jackal }
public sealed class RoleInfo {
  public CustomRole Id; public string Key /*"sheriff"…*/; public string NameJa; public string NameEn; public string DescJa; public string DescEn;
  public Team Team; public string Color /*"#f8cd46"*/;
  public bool IsKiller; public bool ImpostorDesync; /* own view = Impostor, others' view = Crewmate */
  public bool CanVent; public bool CanSabotage; public bool TasksCount /* tasks count toward crew progress */; public bool FromImpostorPool /* Vampire, Mafia */;
  public string Name => Lang.IsJa ? NameJa : NameEn; public string Desc => Lang.IsJa ? DescJa : DescEn; public string Colored(string s) => $"<color={Color}>{s}</color>";
}
public static class Roles {
  public static readonly RoleInfo[] All;                 // every CustomRole except None, in enum order
  public static RoleInfo Info(CustomRole r);            // never null; None → a "None" info (Team.Crew, Color "#ffffff")
  public static bool TryParse(string text, out CustomRole role); // matches Key (case-insensitive), NameJa, NameEn, and common aliases (e.g. "sb" → SpeedBooster, "opp" → Opportunist)
  public static string ColoredName(CustomRole r);       // "<color=#..>名前</color>"
  public static string TeamName(Team t);                // ja/en
}
```
Table (Team / Color / IsKiller / ImpostorDesync / CanVent / CanSabotage / TasksCount / FromImpostorPool):
Sheriff Crew #f8cd46 K D ¬V ¬S T ¬P · Mayor Crew #204d42 · Snitch Crew #b8fb4f · Lighter Crew #eee5be · SpeedBooster Crew #00ffff · Bait Crew #00f7ff · Madmate Impostor #ff1919 ¬K ¬D ¬V ¬S ¬T ¬P · Vampire Impostor #ff1919 K ¬D V S ¬T P · Mafia Impostor #ff1919 K ¬D V S ¬T P · Jester Neutral #ec62a5 ¬T · Opportunist Neutral #00ff00 ¬T · Terrorist Neutral #00ff00 ¬T (tasks real but not counted) · Jackal Neutral #00b4eb K D V(option) ¬S ¬T ¬P.
Crew non-killers: CanVent false, CanSabotage false, TasksCount true. Descriptions: 1–2 short sentences each (ja+en), e.g. Sheriff ja: "キルボタンでインポスター/ジャッカルを撃てます。クルーを撃つと自分が死にます。".

### Lang.cs
```csharp
public static class Lang {
  public static bool IsJa => Options.Language != "en";
  public static string T(string key, string ja, string en = null);   // returns ja or en (en ?? ja); key is only for logging/overrides
  public static string FullWidthDigits(string s);                    // converts 0-9 outside <...> tags to ０-９ (chat safety)
}
```

### Options.cs (BepInEx `ConfigFile`, file `BepInEx/config/jp.hostroles.mod.cfg`)
```csharp
public static class Options {
  public static void Init(ConfigFile cfg);
  public static bool ModEnabled { get; set; }            // [General] Enabled = true
  public static string Language { get; set; }            // [General] Language = ja | en
  public static bool HostAuthorityMode { get; set; }     // [General] RegisterAsModdedLobby (+25 flag) = true  — description must state: required by Innersloth's mod policy (2026-07-30) for lobbies on official servers; turning it off is a policy violation and disables /cmd private commands
  public static bool WelcomeMessage { get; set; }        // [Chat] WelcomeMessage = true
  public static bool RoleInfoAtMeeting { get; set; }     // [Chat] RoleInfoAtMeeting = true
  public static bool AntiCheatKick { get; set; }         // [AntiCheat] KickOnForgedRpc = false
  public static int Count(CustomRole r); public static int Chance(CustomRole r);      // [Roles] Sheriff.Count = 0 (0..15), Sheriff.Chance = 100 (0..100) … defaults: all 0 except Sheriff 1, Jester 1, Madmate 1 (chance 100)
  public static void SetCount(CustomRole r, int n); public static void SetChance(CustomRole r, int c);
  public static float SheriffKillCooldown;   // [Sheriff] KillCooldown = 30
  public static bool SheriffCanKillMadmate;  // [Sheriff] CanKillMadmate = true
  public static float JackalKillCooldown;    // [Jackal] KillCooldown = 30
  public static bool JackalCanVent;          // [Jackal] CanVent = true
  public static float VampireKillDelay;      // [Vampire] KillDelay = 10
  public static int MayorVotes;              // [Mayor] Votes = 2 (1..5)
  public static int SnitchTasksLeftToWarn;   // [Snitch] TasksLeftToWarn = 1
  public static float LighterVision;         // [Lighter] VisionMultiplier = 2.0
  public static float SpeedBoosterSpeed;     // [SpeedBooster] SpeedMultiplier = 1.5
  public static bool MadmateKnownToImpostors;// [Madmate] KnownToImpostors = false
  public static bool TrySet(string key, string value, out string message); // key formats: "<role>.count", "<role>.chance", "sheriff.cooldown", "sheriff.killmadmate", "jackal.cooldown", "jackal.vent", "vampire.delay", "mayor.votes", "snitch.tasks", "lighter.vision", "speedbooster.speed", "madmate.known", "lang", "enabled", "welcome", "roleinfo", "register"; validates ranges; saves config
  public static IEnumerable<string> DescribeLines();   // human-readable current settings, one line per enabled role + non-default options (ja/en)
  public static void Reload();                          // cfg.Reload()
}
```

### GameState.cs
```csharp
public static class Game {
  public static bool IsHostActive { get; }   // AmongUsClient.Instance != null && AmHost && Options.ModEnabled && GameOptionsManager.Instance?.currentGameMode == GameModes.Normal
  public static bool InProgress; public static bool Ending; public static bool AssigningRoles;
  public static Dictionary<byte, CustomRole> Roles;        // playerId → custom role (absent/None = vanilla)
  public static Dictionary<byte, RoleTypes> VanillaRoles;  // playerId → vanilla role chosen by RoleManager (true role)
  public static Dictionary<byte, string> OriginalNames;    // playerId → name at game start
  public static Dictionary<byte, VampireBite> Bites; public struct VampireBite { public byte Killer; public float DueAt; }
  public static HashSet<byte> ExtraWinners;                // Opportunists that won
  public static CustomRole SoloWinner; public static byte SoloWinnerId; public static byte LastExiled = 255;
  public static byte[] BaseOptionBytes;
  public sealed class SummaryEntry { public byte Id; public string Name; public CustomRole Role; public RoleTypes Vanilla; public bool Dead; public bool Winner; }
  public static List<SummaryEntry> LastSummary;            // filled by WinConditions.EndGame, shown by /last and lobby summary
  public static void Reset();                              // clears everything above (not LastSummary)
  public static CustomRole RoleOf(byte id);                // None if absent
  public static RoleInfo InfoOf(byte id);
  public static Team TeamOf(byte id);                      // custom team, else vanilla: RoleManager.IsImpostorRole(VanillaRoles[id]) ? Impostor : Crew
  public static bool IsImpostorTeamKiller(byte id);        // vanilla impostor-type role (Impostor/Shapeshifter/Phantom/Viper, incl. Vampire/Mafia) — NOT Madmate
  public static bool IsDesyncImpostor(byte id);            // InfoOf(id).ImpostorDesync
  public static bool IsAlive(byte id);                     // Info != null && !Disconnected && !IsDead (uses AntiBlackout.RealIsDead when AntiBlackout.Active)
  public static PlayerControl Player(byte id);             // null if gone
  public static NetworkedPlayerInfo Info(byte id);
  public static IEnumerable<PlayerControl> AllPlayers();   // PlayerControl.AllPlayerControls snapshot (non-null, Data != null)
  public static int TasksLeft(byte id); public static bool TasksDone(byte id);   // from Data.Tasks; 0 tasks → done=true only if the list is non-empty? → treat empty list as NOT done
  public static bool IsHost(byte id);                      // Player(id)?.AmOwner
}
```

### Scheduler.cs
```csharp
public static class Scheduler {
  public static void After(float seconds, Action action, string tag = null);  // run once after N seconds (game time, Time.time)
  public static void Cancel(string tag);
  public static void Tick();   // called from HudManager.Update postfix; runs due actions in try/catch; also runs in lobby
  public static void Clear();
}
```

## 3. Net (`src/Net/*.cs`, owner: net)

### Rpc.cs
```csharp
public static class Rpc {
  public static int HostClientId => AmongUsClient.Instance.ClientId;
  public static bool IsLocal(int clientId);
  public static int ClientIdOf(PlayerControl pc);                   // pc.OwnerId
  public static IEnumerable<int> AllClientIds(bool includeHost);    // from AmongUsClient.Instance.allClients (ClientData.Id)
  public static void SetRoleTo(PlayerControl target, RoleTypes role, int clientId);   // host-local (IsLocal) → target.StartCoroutine(target.CoSetRole(role, true)); else targeted RPC 44 (ushort role, true)
  public static void SetRoleAll(PlayerControl target, RoleTypes role);                // broadcast (-1) + local
  public static void SetNameTo(PlayerControl target, string name, int clientId);      // local → NameTags.ApplyLocal is NOT called here; local just sets target.cosmetics.SetName(name) (never Data.PlayerName); remote → RPC 6 (uint Data.NetId, string, bool false)
  public static void SetNameAll(PlayerControl target, string name);                   // broadcast + local (used for RestoreAll; for the local side also set target.Data.PlayerName? NO — Data.PlayerName already holds the original)
  public static void SendChatTo(int clientId, string title, string text);             // 3-RPC trick from TECH-NOTES; local → HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, $"<color=#a0a0a0>[{title}]</color> {text}"); handles dead host via TempRevive
  public static void SendChatAll(string title, string text);                          // every client incl. host; host name restore uses NameTags.NameFor(viewer, host)
  public static void Kill(PlayerControl killer, PlayerControl target);                // killer.RpcMurderPlayer(target, true) — works for any killer on the host; suicide = killer == target
  public static void FailKill(PlayerControl killer, PlayerControl target);            // killer.RpcMurderPlayer(target, false)
  public static void ResetKillCooldown(PlayerControl killer, float cooldown);         // TECH-NOTES trick (options ×2 → FailedProtected to killer only → restore after 0.5 s); host → LocalPlayer.SetKillTimer(cooldown)
  public static void ExileSilently(PlayerControl target);                             // target.Exiled() locally + broadcast RPC 4 (empty)
  public static void SendPlayerInfo(NetworkedPlayerInfo info, int clientId = -1);     // Data(1) message of that NetworkedPlayerInfo (SetDirtyBit(uint.MaxValue) then Serialize(w,false)) to one client or all
  public static void BootFromVent(PlayerControl pc, int ventId);                      // pc.MyPhysics.RpcBootFromVent(ventId)
  public sealed class Batch {                                                          // one GameData/GameDataTo packet, auto-split at 500 bytes
    public Batch(int clientId /* -1 = broadcast */);
    public Batch Rpc(uint netId, RpcCalls call, Action<MessageWriter> payload);
    public Batch SetRole(PlayerControl target, RoleTypes role);                      // convenience
    public Batch SetName(PlayerControl target, string name);
    public void Send(bool urgent = false);                                           // urgent → SendOrDisconnect now; else Rpc.Queue
    public bool IsEmpty { get; }
  }
  public static class Queue { public static void Enqueue(MessageWriter w); public static void Tick(); public static float Interval /* 0.1f */; public static void Clear(); }
  public static void TempReviveHostForChat(Action sendChat);  // if host dead: IsDead=false + SendPlayerInfo, sendChat(), Scheduler.After(1f → IsDead=true + SendPlayerInfo, then Scheduler.After(0.3f → NameTags.RefreshAll(force:true)))
}
```
`Batch.Rpc` must write `StartMessage(2); WritePacked(netId); Write((byte)call); payload(w); EndMessage();` inside a root `5`/`6` message exactly as in TECH-NOTES; when the local host is the target client, apply nothing (callers handle local separately) — for `SetRole`/`SetName` convenience methods with `IsLocal(clientId)` apply locally instead.

### OptionsDesync.cs
```csharp
public static class OptionsDesync {
  public static void Capture();                                   // Game.BaseOptionBytes = factory.ToBytes(GameOptionsManager.Instance.CurrentGameOptions, false)
  public static IGameOptions CloneBase();                         // factory.FromBytes(Game.BaseOptionBytes)
  public static IGameOptions BuildFor(byte playerId, float? killCooldownOverride = null); // clone + role modifiers: Sheriff/Jackal/Vampire/Mafia KillCooldown; Sheriff ImpostorLightMod = CrewLightMod; Lighter CrewLightMod ×; SpeedBooster PlayerSpeedMod ×; returns clone even if unchanged
  public static bool NeedsCustomOptions(byte playerId);           // role has any modifier
  public static void SendTo(PlayerControl pc, IGameOptions opts, bool urgent = false); // TECH-NOTES Data envelope; no-op for the host itself
  public static void ResyncAll();                                  // for every non-host player: send BuildFor if NeedsCustomOptions OR it was sent custom options before (to restore base); tracks a HashSet<byte> Desynced
  public static void Reset();                                      // clear tracking (game end)
  // host-local effect patches (postfix): LogicOptions.GetKillCooldown → host custom killer cooldown; LogicOptions.GetPlayerSpeedMod(pc) → ×SpeedBooster if pc is host; ShipStatus.CalculateLightRadius(NetworkedPlayerInfo) → host Lighter ×, host Sheriff crew-equivalent (result / ImpostorLightMod * CrewLightMod when ImpostorLightMod > 0)
}
```

### Registration.cs
`Constants.GetBroadcastVersion` postfix: `if (Options.HostAuthorityMode && Registration.Hosting) { int rev = __result % 50; if (rev < 25) __result += 25; }`. `Constants.IsVersionModded` prefix → `__result = Options.HostAuthorityMode && Hosting; return false;`. `Hosting` is set true in a prefix of `AmongUsClient.CoCreateOnlineGame`, false in prefixes of `CoJoinOnlineGameFromCode`, `CoJoinOnlineGameFromListing`, `CoJoinOnlineGameDirect`. Also `GameStartManager.BeginGame` prefix (host): if any `PlayerControl.AllPlayerControls` has `!hasBeenSerialized` → `HudManager.Instance.ShowPopUp(Lang.T("start.wait", "プレイヤーの同期待ちです。数秒後にもう一度押してください。", "Waiting for player sync, press Start again in a few seconds."))` and return false.

### AntiCheat.cs
`PlayerControl.HandleRpc(byte callId, MessageReader reader)` prefix (host only): if callId ∈ {2,3,4,6,12,14,29,44,45,46,63,65} → drop (return false), log `callId` + `__instance.Data.PlayerName`, increment strikes for `__instance.OwnerId`; if `Options.AntiCheatKick && strikes >= 3` → `AmongUsClient.Instance.KickPlayer(__instance.OwnerId, false)`. `MeetingHud.HandleRpc` prefix: drop 22/23. Never drop when `!AmongUsClient.Instance.AmHost`. Notify host via `Chat.Local` once per player (use `Chat.Local` only if it exists — it does, see §9).

## 4. Role assignment (`src/Game/RoleAssignment.cs`, owner: assign)
```csharp
public static class RoleAssignment {
  public static RoleTypes View(byte viewerId, byte targetId);  // per TECH-NOTES desync rule; special vanilla roles untouched; ghost handling: if target dead → ghost equivalent (ImpostorGhost when IsImpostorTeamKiller(target) and viewer is not a desync-impostor viewing an impostor… keep simple: dead → IsImpostorTeamKiller ? ImpostorGhost : CrewmateGhost)
  public static void DispatchInitialRoles();                    // called from SelectRoles postfix
  public static void SendGhostRole(PlayerControl dead);          // broadcast ghost per rule
}
```
Patches:
1. `RoleManager.SelectRoles` prefix (host): `Game.Reset(); Game.AssigningRoles = true; OptionsDesync.Capture(); Game.OriginalNames = Data.PlayerName of every player`.
2. `PlayerControl.RpcSetRole(RoleTypes roleType, bool canOverrideRole)` prefix `[HarmonyPriority(Priority.High)]` (host only):
   * if `Game.AssigningRoles` → `Game.VanillaRoles[id] = roleType; __instance.StartCoroutine(__instance.CoSetRole(roleType, true)); return false;`
   * else if `Game.InProgress` and roleType is CrewmateGhost/ImpostorGhost → `SendGhostRole(__instance); return false;`
   * else if `Game.InProgress` and roleType == GuardianAngel and `Game.RoleOf(id) != None` → send CrewmateGhost instead; return false.
   * else return true.
3. `RoleManager.SelectRoles` postfix (host): `Game.AssigningRoles = false; DispatchInitialRoles();`.
`DispatchInitialRoles()`:
   * pools: plainCrew = VanillaRoles == Crewmate, plainImp = VanillaRoles == Impostor. For each role in `Roles.All` (random order for fairness, but Jackal/Sheriff first is fine): for `i < Options.Count(role)`: if `rand.Next(100) < Options.Chance(role)` pick a random player from the matching pool (FromImpostorPool → plainImp) not yet assigned; set `Game.Roles[id] = role`.
   * for each remote client: `new Rpc.Batch(clientId)` → `SetRole(target, View(viewer, target))` for all targets except the viewer, then the viewer's own; `Send()`. Host-local: apply `Rpc.SetRoleTo(target, View(host, target), HostClientId)` only where View differs from the vanilla role already set.
   * `Game.InProgress = true; NameTags.RefreshAll(force: true); OptionsDesync.ResyncAll(); Scheduler.After(8f, () => { Chat.SendRoleInfoToAll(false); OptionsDesync.ResyncAll(); NameTags.RefreshAll(force: true); });`
   * Log the full assignment.
4. `IntroCutscene.OnDestroy` postfix (host): `Scheduler.After(1f, () => { NameTags.RefreshAll(force:true); OptionsDesync.ResyncAll(); })`.
5. `AmongUsClient.OnGameEnd` postfix and `AmongUsClient.OnGameJoined` postfix and `AmongUsClient.OnDisconnected` postfix: `Game.InProgress = false; Game.AssigningRoles = false; Scheduler.Clear(); Rpc.Queue.Clear(); OptionsDesync.Reset();` (no name restore here — WinConditions does it in the lobby).

## 5. Name tags (`src/Game/NameTags.cs`, owner: names)
```csharp
public static class NameTags {
  public static string NameFor(byte viewerId, byte targetId, bool meeting = false);
  public static void RefreshAll(bool force = false, bool meeting = false);   // all (viewer,target) pairs incl. host as viewer; dedupe cache; one Rpc.Batch per client; Send()
  public static void RestoreAll();                                          // broadcast originals (Rpc.SetNameAll) + host local; clear cache
  public static void ApplyLocal(byte targetId, string name);                // host view: pc.cosmetics.SetName(name) (never Data.PlayerName)
  public static void ClearCache();
}
```
Rules for `NameFor` (base = `Game.OriginalNames[target]`, fall back to `Data.PlayerName`):
* viewer == target with custom role: in-game `"<color=hex>役職名</color>\r\n" + base`; meeting `base + " <size=70%><color=hex>役職名</color></size>"`.
* viewer is Madmate (or Jackal? no): impostor-team killers → `<color=#ff1919>base</color>`.
* viewer is impostor-team killer and `Options.MadmateKnownToImpostors`: Madmate targets → `"<color=#ff1919>Ⓜ</color>" + base`.
* Snitch alive with tasks left ≤ `SnitchTasksLeftToWarn`: viewers that are impostor-team killers or Jackal see Snitch as `"<color=#b8fb4f>★</color>" + base`; Snitch with all tasks done: Snitch sees impostor-team killers and Jackal as `<color=#ff1919>base</color>` (Jackal `#00b4eb`).
* Otherwise base.
Patches: `PlayerControl.CompleteTask` postfix (host) → `RefreshAll()` (Snitch progress). `PlayerControl.MurderPlayer` postfix (host, any flags) → `Scheduler.After(0.5f, () => RefreshAll(force: true))` (Data sync resets names).

## 6. Kills / vents / sabotage (`src/Game/Kills.cs`, owner: kills)
```csharp
public static class Kills { public static void Tick(); /* vampire bites due → Rpc.Kill(victim, victim); bait report */ }
```
* `PlayerControl.CheckMurder(PlayerControl target)` prefix (host): guards; per role as in v0.1 §6: Sheriff (`CanSheriffKill(target)`: IsImpostorTeamKiller || Jackal || (Madmate && Options.SheriffCanKillMadmate) → `Rpc.Kill(sheriff, target)` else `Rpc.Kill(sheriff, sheriff)`), Jackal → `Rpc.Kill`, Vampire → `Rpc.ResetKillCooldown(vampire, base cooldown)`, register bite (`Game.Bites[target] = (vampire, Time.time + Options.VampireKillDelay)`), private chat to vampire; Mafia → if any other impostor-team killer alive → `Rpc.FailKill` + private chat; else `return true`. Others → `return true`. Invalid (dead/meeting/null) for custom killers → `Rpc.FailKill; return false`.
* `PlayerControl.MurderPlayer(PlayerControl target, MurderResultFlags resultFlags)` postfix (host): if `resultFlags` has Succeeded: remove `Game.Bites[target]`; Bait → `Scheduler.After(0.2f, () => killer.ReportDeadBody(target.Data))` (only if no meeting); Terrorist with `Game.TasksDone` → `WinConditions.EndGame(WinConditions.WinKind.Terrorist, target.PlayerId)`; then `WinConditions.Check()` after 0.1 s.
* `PlayerPhysics.HandleRpc(byte callId, MessageReader reader)` prefix (host): if callId == EnterVent and owner (`__instance.myPlayer`) has custom role with `!CanVent` (Jackal: `Options.JackalCanVent`) → `int id = reader.ReadPackedInt32(); Rpc.BootFromVent(pc, id); return false;`.
* `ShipStatus.UpdateSystem(SystemTypes systemType, PlayerControl player, MessageReader msgReader)` prefix (host): `systemType == SystemTypes.Sabotage` and player has custom role with `!CanSabotage` → return false.
* `Tick()`: bites due & victim alive & no meeting → `Rpc.Kill(victim, victim)` + remove; if a meeting starts (`MeetingHud.Instance != null`) execute all due-or-not bites immediately? → **No**: Meetings calls `Kills.FlushBites()` from ReportDeadBody prefix. Provide `public static void FlushBites()`.

## 7. Meetings & AntiBlackout (`src/Game/Meetings.cs`, `src/Game/AntiBlackout.cs`, owner: meetings)
```csharp
public static class AntiBlackout { public static bool Active; public static Dictionary<byte,bool> RealIsDead; public static void Prepare(byte exiledId /*255 none*/); public static void Restore(); }
```
* `PlayerControl.ReportDeadBody` prefix (host): `Kills.FlushBites(); NameTags.RefreshAll(force: true, meeting: true); return true;`
* `MeetingHud.Start` postfix (host): `Scheduler.After(1f, () => { if (Options.RoleInfoAtMeeting) Chat.SendRoleInfoToAll(true); })`.
* `MeetingHud.CheckForEndVoting` prefix (host): if no alive Mayor voted → `return true` (vanilla incl. Judge). Else full replacement per v0.1 §7 (states with Mayor duplicates, tally, tie/skip, Judge via `TryGetWinningOverrule` if available) → then `AntiBlackout.Prepare(exiledId)` → `__instance.RpcVotingComplete(states, exiled, tie, wasOverruled, nonce); return false;`
* `MeetingHud.VotingComplete` postfix (host): `Game.LastExiled = exiled?.PlayerId ?? 255`; when the vanilla path was used (no Mayor) call `AntiBlackout.Prepare` here instead (guard against double call with a flag reset in Restore). If exiled is Jester → `Game.SoloWinner = Jester; SoloWinnerId = id`.
* `ExileController.WrapUp` postfix (host): `Scheduler.After(1.5f, AntiBlackout.Restore)`; then if `SoloWinner == Jester` → `WinConditions.EndGame(Jester, id)`; else if exiled is Terrorist with tasks done → `EndGame(Terrorist)`; else `Scheduler.After(2f, () => { OptionsDesync.ResyncAll(); NameTags.RefreshAll(force: true); WinConditions.Check(); })`.
* `AntiBlackout.Prepare`: compute per viewer (every client incl. host? host is modded — skip host) the counts at WrapUp using `RoleAssignment.View` and `Game.IsAlive` minus exiled; if `impostorsInView == 0 || impostorsInView >= othersInView` while host would continue (`WinConditions.WouldContinue(exiledId)`): pick `dummy` (alive, ≠ viewer, ≠ exiled, prefer a real impostor-team killer, else lowest id) and `Rpc.SetRoleTo(dummy, RoleTypes.Impostor, viewerClient)`; if alive-after-exile ≤ 2 also pick a dead `revived` (≠ exiled) → `info.IsDead = false; Rpc.SendPlayerInfo(info, viewerClient); Rpc.SetRoleTo(revived, Crewmate, viewerClient)`; record everything; `Active = true; RealIsDead` snapshot for all players (host logic must keep using real values → `Game.IsAlive` consults `RealIsDead` while Active, and the `revived` info's IsDead is set back to true in memory immediately after sending).
* `AntiBlackout.Restore`: for each record send `Rpc.SetRoleTo(dummy, View(viewer, dummy), viewerClient)`; for revived: `Rpc.SendPlayerInfo(info /*IsDead true*/, viewerClient)` + `Rpc.SetRoleTo(revived, View(viewer, revived), viewerClient)`; `Active = false`; `Scheduler.After(0.5f, () => NameTags.RefreshAll(force: true))`.

## 8. Win conditions (`src/Game/WinConditions.cs`, owner: win)
```csharp
public static class WinConditions {
  public enum WinKind { Crew, Impostor, Jackal, Jester, Terrorist }
  public static void Check();                              // evaluates rules; calls EndGame when met (no-op unless InProgress && !Ending)
  public static bool WouldContinue(byte exiledId);         // same rules assuming exiledId dead; true if game continues
  public static void EndGame(WinKind kind, byte soloId = 255);
}
```
* `GameData.RecomputeTaskCounts` prefix (host, replace): count only `Game.TeamOf(id) == Crew && InfoOf(id).TasksCount && Role.TasksCountTowardProgress && !Disconnected && (GhostsDoTasks || !IsDead)`.
* `LogicGameFlowNormal.CheckEndCriteria` prefix (host): `if (!Game.InProgress) return true; Check(); return false;` (rate-limit `Check` to every 0.25 s).
* Rules (alive = `Game.IsAlive`): imp = alive impostor-team killers; jackal = alive Jackals; others = alive rest; crewForCount = others − alive Madmates. Tasks: `GameData.TotalTasks > 0 && CompletedTasks >= TotalTasks` → Crew. `imp==0 && jackal==0` → Crew (if others==0 too → Crew anyway). `jackal==0 && imp >= crewForCount` → Impostor. `imp==0 && jackal >= crewForCount` → Jackal. else continue. Also if no players alive at all → Crew.
* `GameManager.RpcEndGame(GameOverReason endReason, bool showAd)` prefix (host): if `Game.InProgress && !Game.Ending` → map vanilla reason (`ImpostorsBy*`/`CrewmateDisconnect` → Impostor; `CrewmatesBy*`/`ImpostorDisconnect` → Crew) and call `EndGame(kind)`; return false. If `Ending` → return true.
* `EndGame`: winners = team members (Impostor kind includes Madmate, Vampire, Mafia; Crew kind = TeamOf == Crew) or the solo player; plus alive Opportunists → `Game.ExtraWinners`. Fill `Game.LastSummary`. `Game.Ending = true; Game.InProgress = false; GameManager.Instance.ShouldCheckForGameEnd = false;` For every player: `Rpc.SetRoleAll(pc, winner ? ImpostorGhost : CrewmateGhost)` in one broadcast `Rpc.Batch(-1)` sent urgent + host-local; `Scheduler.After(0.4f, () => GameManager.Instance.RpcEndGame(GameOverReason.ImpostorsByKill, false))`.
* `EndGameManager.SetEverythingUp` postfix (host only, cosmetic): set `__instance.WinText.text` to `Lang` text like "ジェスター勝利" / "クルー勝利" / "インポスター勝利" / "ジャッカル勝利" / "テロリスト勝利" with the role colour, and `__instance.WinText.color`.
* `LobbyBehaviour.Start` postfix (host): `NameTags.RestoreAll(); OptionsDesync.Reset(); Game.Ending = false;` then `Scheduler.After(2f, Chat.SendSummary)` if `Game.LastSummary != null && !SummaryShown`.

## 9. Chat & commands (`src/Chat/Chat.cs`, `src/Chat/Commands.cs`, owner: chat)
```csharp
public static class Chat {
  public static void Local(string title, string text);                        // host screen only
  public static void To(byte playerId, string title, string text);           // Rpc.SendChatTo(ClientIdOf) or Local for host; splits long text at '\n' into ≤120-char messages; applies Lang.FullWidthDigits
  public static void All(string title, string text);
  public static void SendRoleInfo(byte playerId, bool meeting);               // "<role> — desc" (vanilla-role players: short vanilla hint at game start only)
  public static void SendRoleInfoToAll(bool meeting);
  public static void Welcome(int clientId);                                   // mod notice + enabled roles + "/cmd h"
  public static void SendSummary();                                           // Game.LastSummary → lines "name: role (dead/alive)" + winners
  public const string Title = "HostRoles";
}
public static class Commands { public static bool Handle(PlayerControl sender, string text); /* returns true if handled */ }
```
* `ChatController.SendChat` prefix (host, local typing): `text = __instance.freeChatField.textArea.text`; if starts with `/` and `Commands.Handle(LocalPlayer, text)` → `__instance.freeChatField.textArea.Clear()` (check `TextBoxTMP.Clear()`/`SetText("")` in dump) and return false.
* `ChatController.AddChat(PlayerControl sourcePlayer, string chatText, bool censor)` prefix (host): if `sourcePlayer != LocalPlayer` and chatText starts with `/` → `Commands.Handle(sourcePlayer, chatText)`; return false when handled (host doesn't display it).
* Command grammar: strip leading `/cmd ` or `/`; first token = command: `h|help`, `n|now|me`, `r|role|roles [name]`, `l|last`, host-only: `set <role> <count> [chance]`, `opt <key> <value>`, `show`, `reset`, `reload`, `mod on|off`. Replies via `Chat.To(sender)`. Unknown → help hint. Host-only commands from non-host → "ホスト専用です".
* `AmongUsClient.OnPlayerJoined(ClientData data)` postfix (host, lobby only, `Options.WelcomeMessage`): `Scheduler.After(3f, () => Chat.Welcome(data.Id))`.
* Welcome text (ja): "この部屋はホスト専用MOD「HostRoles」を使用しています。役職付きの試合になります。/cmd h でヘルプ。有効な役職: …" (+ en line).

## 10. README.md (owner: docs, Japanese)
Sections: 概要 (host-only, vanilla players can join, works on Steam), Innersloth の mod ポリシーと公開部屋についての注意 (registration +25, public list likely hidden, join by code, policy quotes with URLs, no monetization, disclaimer text), 必要なもの, 導入手順 (Steam: copy game folder or install BepInEx be.735 x86 zip URL, first launch generates interop, put HostRoles.dll in BepInEx/plugins, launch `Among Us.exe` in the copy — Steam must be running), 遊び方 (create lobby → settings via chat `/set`, `/show`, players use `/cmd n`), 役職一覧 (table), コマンド一覧, 設定ファイル, 既知の制限 (intro shows Impostor for Sheriff/Jackal, commands typed without /cmd are visible to all, console/quick-chat players can read but not type, host migration unsupported, rate-limit kicks), ビルド方法 (dotnet 8 SDK user-local, `dotnet build -c Release`), ライセンス GPL-3.0 + Innersloth disclaimer.

## 11. Testing
Compile (`dotnet build -c Release` with `DOTNET_ROOT=%USERPROFILE%\.dotnet`), launch `Desktop\Among Us HostRoles\Among Us.exe`, verify `BepInEx/LogOutput.log` has no `Harmony`/patch errors and `HostRoles … loaded`. Online multi-client tests are not possible in this environment — reviewers must verify logic by reading.

## 12. File ownership
| owner | files |
|---|---|
| core | `src/HostRolesPlugin.cs`, `src/Core/Roles.cs`, `src/Core/Lang.cs`, `src/Core/Options.cs`, `src/Core/GameState.cs`, `src/Core/Scheduler.cs` |
| net | `src/Net/Rpc.cs`, `src/Net/OptionsDesync.cs`, `src/Net/Registration.cs`, `src/Net/AntiCheat.cs` |
| assign | `src/Game/RoleAssignment.cs` |
| names | `src/Game/NameTags.cs` |
| kills | `src/Game/Kills.cs` |
| meetings | `src/Game/Meetings.cs`, `src/Game/AntiBlackout.cs` |
| win | `src/Game/WinConditions.cs` |
| chat | `src/Chat/Chat.cs`, `src/Chat/Commands.cs` |
| docs | `README.md`, `LICENSE` |

## 13. v0.2 backlog (decided with the user on 2026-09-07; implement after v0.1 is verified)

1. **Fix**: Sheriff must have fake tasks (`TasksCount = false`, description "タスクはできません（偽タスク）") — a vanilla client with Impostor basis cannot use task consoles (`ImpostorRole.CanUse` returns false), exactly like Jackal. Also exclude Sheriff/Jackal tasks from `RecomputeTaskCounts` (already via TasksCount) and never wait for their tasks.
2. **Settings tab in the lobby computer**: a "HostRoles" tab in `GameSettingMenu` (host only) exposing every `Options` value (role Count/Chance + role options + general) with vanilla `NumberOption`/`ToggleOption`/`StringOption` prefabs; changes write through `Options` (same as `/set`/`/opt`) and are reflected by `/show`.
3. **AutoRehost / AutoPublic**: options `[Lobby] AutoRehost=false`, `AutoPublic=false`, `AutoPublicDelay=5`. On host disconnect from its own lobby (not a voluntary exit) re-create an online lobby with the previous create-options (map, impostors, chat type, region, game options) and, if AutoPublic, call `ChangeGamePublic(true)` after the delay; log + host chat notice with the new code.
4. **Welcome text**: `[Chat] WelcomeText` (multi-line, `\n`), placeholders `{roles}`, `{settings}`, `{help}`, `{version}`; `WelcomeIncludeSettings=true`; commands `/welcome <text>` (host), `/welcome show`, `/welcome reset`.
5. **Test mode** (host only, for testing with 1–3 players): `/test on|off` → `GameStartManager.MinPlayers = 1` (start button enabled alone), win-condition checks disabled (`/end` ends the game manually as crew win), `/assign <player name or id> <role>` forces a custom role for the next game (cleared after use), `/assign clear`, `/assign show`. Test mode is announced in chat to everyone and never persists across restarts (not written to config).
6. **Version check**: at startup compare `Application.version` with `HostRolesPlugin.SupportedGameVersion`; if different, log a warning, show a popup once in the main menu, set `Options.ModEnabled`-like runtime flag `Game.VersionMismatch = true` so `IsHostActive` is false (mod inert) unless `[General] IgnoreVersionMismatch=true`.
7. README: launcher section (HostRoles Launcher / update-game.cmd), v0.2 features, "what vanilla players see" section (kill animations, fake tasks for Sheriff/Jackal, Impostor intro, name tag, chat, end screen).
