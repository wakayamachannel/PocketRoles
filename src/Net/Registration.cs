using System;
using HarmonyLib;
using PocketRoles.Core;

namespace PocketRoles.Net
{
    /// <summary>
    /// Innersloth mod policy (2026-07-30): a host-only mod on official servers must register the lobby by adding 25 to
    /// the broadcast protocol revision ("host authority mode"). Only applied while WE create the lobby, so a PocketRoles
    /// user can still join other people's vanilla lobbies.
    /// </summary>
    public static class Registration
    {
        /// <summary>True between CoCreateOnlineGame and leaving that lobby (or the next join attempt).</summary>
        public static bool Hosting;

        /// <summary>The value of Options.HostAuthorityMode when the current lobby was created (a later /opt change applies to the next lobby).</summary>
        public static bool Registered;

        public static bool ShouldRegister => Hosting && Registered;

        /// <summary>
        /// Unregistered-compatible mode ("互換モード" / 便利ホスト, findings #21/#22/#28): we host an online lobby that
        /// was created with RegisterAsModdedLobby=false (no +25), so the lobby appears in the vanilla public list but
        /// the official server disconnects the host ("DC because Hacking") for ANY client-addressed message
        /// (GameDataTo / per-client RPC — verified live 2026-09-08). Same predicate as <see cref="Rpc.SafeMode"/>
        /// (kept in sync by <see cref="UpdateSafeMode"/>). In this mode the mod is a pure helper host: no custom
        /// roles (vanilla game), no SetName / SetRole / options / chat to individual clients; every mod message is
        /// ONE public SendChat from the host's own player ("[PocketRoles] …", replies to one player prefixed "@name"),
        /// the welcome is one public line per join, "/cmd …" replies are public and translation is broadcast only.
        /// </summary>
        public static bool CompatMode => Rpc.SafeMode;

        /// <summary>Recomputes <see cref="Rpc.SafeMode"/>: we host an online lobby that was created unregistered.</summary>
        public static void UpdateSafeMode(string reason)
        {
            var client = AmongUsClient.Instance;
            bool safe = client != null && client.AmHost && client.NetworkMode == NetworkModes.OnlineGame && Hosting && !Registered;
            if (safe != Rpc.SafeMode)
                PocketRolesPlugin.Logger.LogInfo($"Registration: safe/compat mode {(safe ? "ON (unregistered lobby)" : "off")} ({reason})");
            Rpc.SafeMode = safe;
            if (safe && reason == "OnGameJoined") LogCompatDetails();
        }

        /// <summary>One log block describing what the compat mode changes (lobby creation / join).</summary>
        internal static void LogCompatDetails()
        {
            PocketRolesPlugin.Logger.LogWarning(
                "Compat mode ON (RegisterAsModdedLobby=false, no +25): listed in the vanilla public list; the server disconnects the host for any client-addressed message, so: " +
                "no custom roles (vanilla game); no SetName / SetRole / options / chat to individual clients; " +
                "every mod message = ONE public SendChat from the host ('[PocketRoles] …', replies prefixed '@name'); welcome = 1 public line per join; " +
                "'/cmd …' replies are public; translation = broadcast only; " +
                "queue 0.3 s, packets ≤ 400 bytes, no silent Exiled, no dead-host revive trick.");
        }
    }

    /// <summary>Lobby created or joined: safe mode applies only to an unregistered lobby WE host.</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class Registration_OnGameJoinedPatch
    {
        /// <summary>The safe-mode notice was shown for this lobby (cleared on every join).</summary>
        internal static bool Warned;

        private static void Postfix()
        {
            try
            {
                Warned = false;
                Rpc.ResetCompatWarnings(); // compat-mode "send skipped" warnings: once per lobby
                Registration.UpdateSafeMode("OnGameJoined");
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Registration_OnGameJoinedPatch: {e}"); }
        }
    }

    /// <summary>Host-local warning once per unregistered lobby (the lobby scene exists here, so chat is available).</summary>
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    internal static class Registration_LobbyStartPatch
    {
        private static void Postfix()
        {
            try
            {
                Registration.UpdateSafeMode("LobbyBehaviour.Start");
                if (!Rpc.SafeMode || Registration_OnGameJoinedPatch.Warned) return;
                Registration_OnGameJoinedPatch.Warned = true;
                // The local player may not exist yet right after the lobby scene loaded: retry until it does.
                Chat.Chat.LocalWhenReady(() => Lang.T("register.safemode",
                    "登録オフ: 公開一覧に出ますが規約違反で、アンチチートで蹴られる可能性があります（/opt register on で戻せます）",
                    "Registration off: the lobby is listed publicly, but this violates the mod policy and the anti-cheat may kick you (/opt register on restores it)"));
                // What the compat mode changes for the host (the policy warning above stays).
                Chat.Chat.LocalWhenReady(() => Lang.T("compat.on.public",
                    "便利ホスト（登録オフ）: 役職なし・個別メッセージなし（挨拶や /cmd の返信も全員に見えます）・翻訳は全体流しのみ",
                    "Helper-host (unregistered) lobby: no roles, no private messages (the welcome and /cmd replies are public), translation is broadcast only",
                    "便利房（未注册）：无职业、无私信（欢迎语和 /cmd 回复所有人可见）、翻译仅全体广播"));
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Registration_LobbyStartPatch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(Constants), nameof(Constants.GetBroadcastVersion))]
    internal static class Registration_GetBroadcastVersionPatch
    {
        private static void Postfix(ref int __result)
        {
            try
            {
                if (!Registration.ShouldRegister) return;
                int rev = __result % 50;
                if (rev < 25) __result += 25;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Registration_GetBroadcastVersionPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(Constants), nameof(Constants.IsVersionModded))]
    internal static class Registration_IsVersionModdedPatch
    {
        private static bool Prefix(ref bool __result)
        {
            try
            {
                __result = Registration.ShouldRegister;
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Registration_IsVersionModdedPatch: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoCreateOnlineGame))]
    internal static class Registration_CoCreateOnlineGamePatch
    {
        private static bool Prefix()
        {
            try
            {
                // Inert because of a game-version mismatch: a plain vanilla game is hosted, so behave like vanilla
                // (no +25, listed normally, no modded-lobby notice for the clients). Not gated on Options.ModEnabled:
                // /mod on inside the lobby must not run mod features in an unregistered lobby.
                bool inert = Core.Game.VersionMismatch && !Options.IgnoreVersionMismatch;
                Registration.Registered = Options.HostAuthorityMode && !inert;
                Registration.Hosting = true;
                PocketRolesPlugin.Logger.LogInfo($"Registration: creating lobby, register(+25)={Registration.Registered}" + (inert && Options.HostAuthorityMode ? " (not registered: game version mismatch, the mod is inert)" : ""));
                try
                {
                    PocketRoles.Game.VanillaRanges.LogHealth("lobby creation");
                    if (!Registration.Registered && !inert)
                    {
                        if (Options.ClampInUnregistered) PocketRoles.Game.VanillaRanges.ClampToVanilla("unregistered lobby creation");
                        else PocketRolesPlugin.Logger.LogWarning("VanillaRanges: unregistered lobby creation: clamp skipped ([Vanilla] ClampInUnregistered=false) — extended values go to the server as they are");
                    }
                }
                catch (Exception) { }
                if (!Registration.Registered && !inert) Registration.LogCompatDetails();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Registration_CoCreateOnlineGamePatch: {e}");
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoJoinOnlineGameFromCode))]
    internal static class Registration_CoJoinOnlineGameFromCodePatch
    {
        private static bool Prefix()
        {
            try { Registration.Hosting = false; }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Registration_CoJoinOnlineGameFromCodePatch: {e}"); }
            return true;
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoJoinOnlineGameFromListing))]
    internal static class Registration_CoJoinOnlineGameFromListingPatch
    {
        private static bool Prefix()
        {
            try { Registration.Hosting = false; }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Registration_CoJoinOnlineGameFromListingPatch: {e}"); }
            return true;
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoJoinOnlineGameDirect))]
    internal static class Registration_CoJoinOnlineGameDirectPatch
    {
        private static bool Prefix()
        {
            try { Registration.Hosting = false; }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Registration_CoJoinOnlineGameDirectPatch: {e}"); }
            return true;
        }
    }

    /// <summary>
    /// Leaving the lobby ends the hosting session: the +25 flag must not leak into later matchmaker requests.
    /// Prefix: the game state is still the lobby's here; a disconnect that happens before any lobby was joined
    /// (inside the create handshake) keeps the flag, otherwise the lobby would be created unregistered.
    /// </summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    internal static class Registration_OnDisconnectedPatch
    {
        private static bool Prefix(AmongUsClient __instance)
        {
            try
            {
                if (__instance != null && __instance.GameState != InnerNet.InnerNetClient.GameStates.NotJoined)
                    Registration.Hosting = false;
                Rpc.SafeMode = false;
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Registration_OnDisconnectedPatch: {e}"); }
            return true;
        }
    }

    /// <summary>
    /// Host migration into a lobby we did not create: the lobby is not registered, so PocketRoles stays inert
    /// (Game.IsHostActive requires Registration.Hosting for online games). Tell the host why.
    /// </summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnBecomeHost))]
    internal static class Registration_OnBecomeHostPatch
    {
        private static void Postfix(AmongUsClient __instance)
        {
            try
            {
                if (__instance == null || Registration.Hosting) return;
                if (__instance.NetworkMode != NetworkModes.OnlineGame) return;
                PocketRolesPlugin.Logger.LogWarning("Registration: became host of a lobby we did not create (not registered) - PocketRoles stays off in this lobby");
                Chat.Chat.Local(Chat.Chat.Title, Lang.T("register.migrated",
                    "ホストを引き継ぎましたが、この部屋はMOD登録されていないため PocketRoles は無効のままです。新しく部屋を作ってください。",
                    "You became host of an unregistered lobby: PocketRoles stays off here. Create a new lobby to use it."));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Registration_OnBecomeHostPatch: {e}");
            }
        }
    }

    /// <summary>
    /// 2026.8.18: the server kicks the host for "hacking" when Start is pressed while a player is not yet serialized.
    /// </summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
    internal static class Registration_BeginGamePatch
    {
        private static bool Prefix()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return true; // the serialization guard is host-local: also with /mod off
                var all = PlayerControl.AllPlayerControls;
                if (all == null) return true;
                foreach (var pc in all)
                {
                    if (pc == null) continue;
                    if (!pc.hasBeenSerialized)
                    {
                        PocketRolesPlugin.Logger.LogWarning($"BeginGame blocked: player {pc.PlayerId} not yet serialized");
                        var hud = HudManager.Instance;
                        if (hud != null)
                            hud.ShowPopUp(Lang.T("start.wait", "プレイヤーの同期待ちです。数秒後にもう一度押してください。", "Waiting for player sync, press Start again in a few seconds."));
                        return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Registration_BeginGamePatch: {e}");
                return true;
            }
        }
    }
}
