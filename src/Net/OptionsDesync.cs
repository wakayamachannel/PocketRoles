using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using PocketRoles.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace PocketRoles.Net
{
    /// <summary>
    /// Per-client game options ("settings desync"): a vanilla client computes its own kill cooldown, speed and vision
    /// from the options it holds, so the host sends each custom-role player a private copy of the options
    /// (a Data update of the GameManager's LogicOptions component targeted to one client, see TECH-NOTES.md).
    /// The host's own effects are applied by postfix patches instead (never via SetGameOptions, which broadcasts).
    /// </summary>
    public static class OptionsDesync
    {
        /// <summary>Players that have received custom options at least once (need a base re-send later).</summary>
        private static readonly HashSet<byte> Desynced = new HashSet<byte>();

        public static void Capture()
        {
            try
            {
                var gom = GameOptionsManager.Instance;
                if (gom == null || gom.gameOptionsFactory == null || gom.CurrentGameOptions == null) return;
                var bytes = gom.gameOptionsFactory.ToBytes(gom.CurrentGameOptions, false);
                Core.Game.BaseOptionBytes = ToManaged(bytes);
                PocketRolesPlugin.Logger.LogInfo($"OptionsDesync: captured base options ({Core.Game.BaseOptionBytes.Length} bytes)");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"OptionsDesync.Capture: {e}");
            }
        }

        private static byte[] ToManaged(Il2CppStructArray<byte> arr)
        {
            if (arr == null) return null;
            var result = new byte[arr.Length];
            for (int i = 0; i < result.Length; i++) result[i] = arr[i];
            return result;
        }

        public static IGameOptions CloneBase()
        {
            var gom = GameOptionsManager.Instance;
            if (gom == null || gom.gameOptionsFactory == null) return null;
            if (Core.Game.BaseOptionBytes == null) Capture();
            if (Core.Game.BaseOptionBytes == null) return null;
            Il2CppStructArray<byte> raw = Core.Game.BaseOptionBytes;
            return gom.gameOptionsFactory.FromBytes(raw);
        }

        /// <summary>Base options + role modifiers for that player. Always returns a fresh clone (null only on failure).</summary>
        public static IGameOptions BuildFor(byte playerId, float? killCooldownOverride = null)
        {
            var opts = CloneBase();
            if (opts == null) return null;
            try
            {
                var role = Core.Game.RoleOf(playerId);
                switch (role)
                {
                    case CustomRole.Sheriff:
                        opts.SetFloat(FloatOptionNames.KillCooldown, Mathf.Max(0.02f, Options.SheriffKillCooldown));
                        // Sheriff is an Impostor on its own client → give it crew vision.
                        opts.SetFloat(FloatOptionNames.ImpostorLightMod, opts.GetFloat(FloatOptionNames.CrewLightMod));
                        break;
                    case CustomRole.Jackal:
                        opts.SetFloat(FloatOptionNames.KillCooldown, Mathf.Max(0.02f, Options.JackalKillCooldown));
                        break;
                    case CustomRole.Lighter:
                        opts.SetFloat(FloatOptionNames.CrewLightMod, opts.GetFloat(FloatOptionNames.CrewLightMod) * Options.LighterVision);
                        break;
                    case CustomRole.SpeedBooster:
                        opts.SetFloat(FloatOptionNames.PlayerSpeedMod, opts.GetFloat(FloatOptionNames.PlayerSpeedMod) * Options.SpeedBoosterSpeed);
                        break;
                    // v0.4.1: the douse / spell cooldown drives the client's kill button timer.
                    case CustomRole.Arsonist:
                        opts.SetFloat(FloatOptionNames.KillCooldown, Mathf.Max(0.02f, Options.ArsonistDouseCooldown));
                        break;
                    case CustomRole.Witch:
                        if (Options.WitchSpellCooldown > 0f) opts.SetFloat(FloatOptionNames.KillCooldown, Mathf.Max(0.02f, Options.WitchSpellCooldown));
                        break;
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
                    // Vampire / Mafia: vanilla impostor kill cooldown (base value) unless overridden below.
                }
                if (killCooldownOverride.HasValue)
                    opts.SetFloat(FloatOptionNames.KillCooldown, Mathf.Max(0.02f, killCooldownOverride.Value));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"OptionsDesync.BuildFor({playerId}): {e}");
            }
            return opts;
        }

        public static bool NeedsCustomOptions(byte playerId)
        {
            switch (Core.Game.RoleOf(playerId))
            {
                case CustomRole.Sheriff:
                case CustomRole.Jackal:
                case CustomRole.Lighter:
                case CustomRole.SpeedBooster:
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
            }
        }

        private static int LogicOptionsIndex()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.LogicComponents == null) return -1;
            var comps = gm.LogicComponents;
            int count = comps.Count;
            for (int i = 0; i < count; i++)
            {
                var c = comps[i];
                if (c == null) continue;
                if (c.TryCast<LogicOptions>() != null) return i;
            }
            return -1;
        }

        /// <summary>Send private options to one vanilla client. No-op for the host itself.</summary>
        public static void SendTo(PlayerControl pc, IGameOptions opts, bool urgent = false)
        {
            try
            {
                if (pc == null || opts == null || pc.AmOwner) return;
                if (Rpc.CompatBlocked("OptionsDesync.SendTo")) return; // unregistered lobby: no GameDataTo (findings #21/#28)
                var client = AmongUsClient.Instance;
                var gom = GameOptionsManager.Instance;
                var gm = GameManager.Instance;
                if (client == null || gom == null || gom.gameOptionsFactory == null || gm == null) return;
                int idx = LogicOptionsIndex();
                if (idx < 0)
                {
                    PocketRolesPlugin.Logger.LogWarning("OptionsDesync.SendTo: LogicOptions component not found");
                    return;
                }
                int clientId = pc.OwnerId;
                if (clientId < 0) return;
                var bytes = gom.gameOptionsFactory.ToBytes(opts, false);
                MessageWriter w = null;
                try
                {
                    w = MessageWriter.Get(SendOption.Reliable);
                    w.StartMessage(6);
                    w.Write(client.GameId);
                    w.WritePacked(clientId);
                    w.StartMessage(1);
                    w.WritePacked(gm.NetId);
                    w.StartMessage((byte)idx);
                    w.WriteBytesAndSize(bytes);
                    w.EndMessage();
                    w.EndMessage();
                    w.EndMessage();
                }
                catch (Exception)
                {
                    if (w != null) { try { w.Recycle(); } catch { } }
                    throw;
                }
                Desynced.Add(pc.PlayerId);
                if (urgent) Rpc.SendNow(w);
                else Rpc.Queue.Enqueue(w);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"OptionsDesync.SendTo: {e}");
            }
        }

        /// <summary>
        /// Game end: every client that ever received custom options gets the true (base) options back, so nothing leaks
        /// into the lobby / the next game. Works even when the mod was switched off on the end screen (host check only).
        /// </summary>
        public static void RestoreAll()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || Desynced.Count == 0)
                {
                    Desynced.Clear();
                    return;
                }
                foreach (var pc in Core.Game.AllPlayers())
                {
                    if (pc.AmOwner || !Desynced.Contains(pc.PlayerId)) continue;
                    if (pc.Data == null || pc.Data.Disconnected) continue;
                    var opts = CloneBase();
                    if (opts == null) break;
                    SendTo(pc, opts, false);
                }
                Desynced.Clear();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"OptionsDesync.RestoreAll: {e}");
                Desynced.Clear();
            }
        }

        /// <summary>Re-send custom options to everyone that needs them and base options to anyone that no longer does.</summary>
        public static void ResyncAll()
        {
            try
            {
                if (!Core.Game.IsHostActive) return;
                foreach (var pc in Core.Game.AllPlayers())
                {
                    if (pc.AmOwner) continue;
                    byte id = pc.PlayerId;
                    if (pc.Data == null || pc.Data.Disconnected) continue;
                    bool needs = NeedsCustomOptions(id);
                    if (!needs && !Desynced.Contains(id)) continue;
                    var opts = BuildFor(id);
                    if (opts == null) continue;
                    SendTo(pc, opts, false);
                    if (!needs) Desynced.Remove(id); // base options restored
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"OptionsDesync.ResyncAll: {e}");
            }
        }

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

        public static void Reset()
        {
            Desynced.Clear();
        }

        // ------------------------------------------------------------------ host-local effects

        internal static bool IsHostPlayer(byte playerId)
        {
            var lp = PlayerControl.LocalPlayer;
            return lp != null && lp.PlayerId == playerId;
        }
    }

    /// <summary>Host's own kill cooldown for custom killer roles (Sheriff / Jackal).</summary>
    [HarmonyPatch(typeof(LogicOptions), nameof(LogicOptions.GetKillCooldown))]
    internal static class OptionsDesync_GetKillCooldownPatch
    {
        /// <summary>The role's own kill cooldown for the host's button (-1 = the lobby value applies). Shared with Kills.ApplyHostCustomCooldown.</summary>
        internal static float HostCustomCooldown(CustomRole role)
        {
            switch (role)
            {
                case CustomRole.Sheriff: return Mathf.Max(0.02f, Options.SheriffKillCooldown);
                case CustomRole.Jackal: return Mathf.Max(0.02f, Options.JackalKillCooldown);
                case CustomRole.Arsonist: return Mathf.Max(0.02f, Options.ArsonistDouseCooldown);
                case CustomRole.Witch: return Options.WitchSpellCooldown > 0f ? Mathf.Max(0.02f, Options.WitchSpellCooldown) : -1f;
                case CustomRole.Worshipper: return Mathf.Max(0.02f, Options.WorshipperCooldown);
                case CustomRole.SerialKiller: return Mathf.Max(0.02f, Options.SerialKillerKillCooldown);
                case CustomRole.Samurai: return Options.SamuraiKillCooldown > 0f ? Mathf.Max(0.02f, Options.SamuraiKillCooldown) : -1f;
                default: return -1f;
            }
        }

        private static void Postfix(ref float __result)
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                var lp = PlayerControl.LocalPlayer;
                if (lp == null) return;
                switch (Core.Game.RoleOf(lp.PlayerId))
                {
                    case CustomRole.Sheriff: __result = Mathf.Max(0.02f, Options.SheriffKillCooldown); break;
                    case CustomRole.Jackal: __result = Mathf.Max(0.02f, Options.JackalKillCooldown); break;
                    case CustomRole.Arsonist: __result = Mathf.Max(0.02f, Options.ArsonistDouseCooldown); break;
                    case CustomRole.Witch: if (Options.WitchSpellCooldown > 0f) __result = Mathf.Max(0.02f, Options.WitchSpellCooldown); break;
                    // v0.5.0
                    case CustomRole.Worshipper: __result = Mathf.Max(0.02f, Options.WorshipperCooldown); break;
                    case CustomRole.SerialKiller: __result = Mathf.Max(0.02f, Options.SerialKillerKillCooldown); break;
                    case CustomRole.Samurai: if (Options.SamuraiKillCooldown > 0f) __result = Mathf.Max(0.02f, Options.SamuraiKillCooldown); break;
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"OptionsDesync_GetKillCooldownPatch: {e}");
            }
        }
    }

    /// <summary>Host's own speed when it is a SpeedBooster.</summary>
    [HarmonyPatch(typeof(LogicOptions), nameof(LogicOptions.GetPlayerSpeedMod))]
    internal static class OptionsDesync_GetPlayerSpeedModPatch
    {
        private static void Postfix(PlayerControl pc, ref float __result)
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                if (pc == null || !pc.AmOwner) return;
                var role = Core.Game.RoleOf(pc.PlayerId);
                if (role == CustomRole.SpeedBooster) __result *= Options.SpeedBoosterSpeed;
                else if (role == CustomRole.MadHawk) __result = Mathf.Clamp(__result * Options.MadHawkSpeed, 0.5f, 3f);   // v0.5.0, same clamp as BuildFor
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"OptionsDesync_GetPlayerSpeedModPatch: {e}");
            }
        }
    }

    /// <summary>Host's own vision: Lighter ×, Mad Hawk × (crew basis), Evil Hawk × (impostor basis), Sheriff gets crew-equivalent vision (its local role is Impostor).</summary>
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.CalculateLightRadius))]
    internal static class OptionsDesync_CalculateLightRadiusPatch
    {
        internal static void ApplyHostLightMod(NetworkedPlayerInfo player, ref float result)
        {
            if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
            if (player == null || !OptionsDesync.IsHostPlayer(player.PlayerId)) return;
            var role = Core.Game.RoleOf(player.PlayerId);
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
            {
                var gom = GameOptionsManager.Instance;
                var opts = gom != null ? gom.CurrentGameOptions : null;
                if (opts == null) return;
                float imp = opts.GetFloat(FloatOptionNames.ImpostorLightMod);
                float crew = opts.GetFloat(FloatOptionNames.CrewLightMod);
                if (imp > 0f) result = result / imp * crew;
            }
        }

        private static void Postfix(NetworkedPlayerInfo player, ref float __result)
        {
            try
            {
                ApplyHostLightMod(player, ref __result);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"OptionsDesync_CalculateLightRadiusPatch: {e}");
            }
        }
    }

    /// <summary>AirshipStatus overrides CalculateLightRadius; a patch on the base virtual is not inherited by the override.</summary>
    [HarmonyPatch(typeof(AirshipStatus), nameof(AirshipStatus.CalculateLightRadius))]
    internal static class OptionsDesync_AirshipCalculateLightRadiusPatch
    {
        private static void Postfix(NetworkedPlayerInfo player, ref float __result)
        {
            try
            {
                OptionsDesync_CalculateLightRadiusPatch.ApplyHostLightMod(player, ref __result);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"OptionsDesync_AirshipCalculateLightRadiusPatch: {e}");
            }
        }
    }
}
