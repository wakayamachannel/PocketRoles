using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Lobby
{
    /// <summary>
    /// 廃村 is slow for the host (user feedback): after Haison ends the game the host sits through the vanilla end
    /// screens ("インポスターが接続されていない" → 続行 → XP result → もう一度プレイ) and clicks twice. When the last game
    /// was ended by a haison (<see cref="Haison.LastGameWasHaison"/>) this runs the "play again" code path
    /// (<c>EndGameNavigation.NextGame</c>) about one second after the end-screen buttons appear
    /// (<c>EndGameManager.ShowButtons</c>), skipping the progression (XP) screen. Normal games are untouched.
    /// </summary>
    public static class HaisonReturn
    {
        /// <summary>Seconds between the end-screen buttons appearing and the automatic "play again".</summary>
        private const float ReturnDelay = 1.0f;

        /// <summary>NextGame() sent but no rejoin after this: show the buttons again and let the host click.</summary>
        private const float RejoinTimeout = 5f;
        private const string FallbackTag = "haisonreturn.fallback";

        private static bool _returning;
        private static bool _gaveUp;   // the fallback ran for this end screen: the host clicks, no second auto-return

        /// <summary>A haison auto-return is running for the current end screen.</summary>
        public static bool Returning => _returning;

        internal static void OnEndScreenReady(EndGameManager egm)
        {
            if (egm == null) return;
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost) return;
            if (!Haison.LastGameWasHaison)
            {
                return; // a normal game end: the host clicks through as usual
            }
            if (_gaveUp)
            {
                PocketRolesPlugin.Logger.LogInfo("Trace: haison return: buttons shown again after the rejoin timeout, the host clicks");
                return;
            }
            if (_returning)
            {
                PocketRolesPlugin.Logger.LogInfo("Trace: haison return already scheduled");
                return;
            }
            _returning = true;
            PocketRolesPlugin.Logger.LogInfo($"Trace: haison return: end screen shown, playing again in {ReturnDelay:0.0}s");
            try
            {
                var nav = egm.Navigation;
                if (nav != null)
                {
                    try { nav.HideButtons(); } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"HaisonReturn: HideButtons failed: {e.Message}"); }
                }
                egm.StartCoroutine(CoReturn(egm).WrapToIl2Cpp());
            }
            catch (Exception e)
            {
                _returning = false;
                PocketRolesPlugin.Logger.LogError($"HaisonReturn.OnEndScreenReady: {e}");
            }
        }

        private static IEnumerator CoReturn(EndGameManager egm)
        {
            yield return new WaitForSeconds(ReturnDelay);
            PlayAgain(egm);
        }

        /// <summary>Runs the vanilla "play again" handler once (EndGameNavigation.NextGame).</summary>
        private static void PlayAgain(EndGameManager egm)
        {
            try
            {
                if (egm == null)
                {
                    PocketRolesPlugin.Logger.LogInfo("Trace: haison return: end screen gone, nothing to do");
                    _returning = false;
                    return;
                }
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost)
                {
                    PocketRolesPlugin.Logger.LogInfo("Trace: haison return: not host any more, skipped");
                    _returning = false;
                    return;
                }
                var nav = egm.Navigation;
                if (nav == null)
                {
                    PocketRolesPlugin.Logger.LogWarning("Trace: haison return: EndGameNavigation missing, the host must click");
                    _returning = false;
                    return;
                }
                PocketRolesPlugin.Logger.LogInfo($"Trace: haison return: NextGame() (same as もう一度プレイ), state={client.GameState}");
                nav.NextGame();
                // No rejoin (server refused, matchmaker hiccup): OnGameJoined never clears _returning and the buttons
                // stay hidden. A successful rejoin drops this entry (Game.ResetForNewLobby clears the Scheduler).
                Scheduler.After(RejoinTimeout, () => RejoinFallback(egm), FallbackTag);
            }
            catch (Exception e)
            {
                _returning = false;
                PocketRolesPlugin.Logger.LogError($"HaisonReturn.PlayAgain: {e}");
            }
        }

        /// <summary>Still on the end screen after <see cref="RejoinTimeout"/>: give the buttons back to the host.</summary>
        private static void RejoinFallback(EndGameManager egm)
        {
            try
            {
                if (!_returning) return;
                var client = AmongUsClient.Instance;
                if (client == null || client.GameState != InnerNet.InnerNetClient.GameStates.Ended)
                {
                    PocketRolesPlugin.Logger.LogInfo($"Trace: haison return: rejoin in progress (state={(client == null ? "none" : client.GameState.ToString())}), no fallback");
                    _returning = false;
                    return;
                }
                PocketRolesPlugin.Logger.LogWarning($"Trace: haison return: no rejoin within {RejoinTimeout:0}s, showing the end-screen buttons again (the host clicks)");
                _returning = false;
                _gaveUp = true;
                if (egm != null) egm.ShowButtons(); // our ShowButtons postfix sees _gaveUp and does not re-schedule
            }
            catch (Exception e)
            {
                _returning = false;
                PocketRolesPlugin.Logger.LogError($"HaisonReturn.RejoinFallback: {e}");
            }
        }

        /// <summary>Lobby / disconnect / a new end screen: the previous end screen is over.</summary>
        internal static void Reset(string why)
        {
            if (_returning) PocketRolesPlugin.Logger.LogInfo($"Trace: haison return: done ({why})");
            _returning = false;
            _gaveUp = false;
            Scheduler.Cancel(FallbackTag);
        }
    }

    /// <summary>A new game ended: whatever the previous end screen left behind (a stuck latch) is forgotten.</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    internal static class HaisonReturn_OnGameEndPatch
    {
        private static void Postfix()
        {
            try
            {
                HaisonReturn.Reset("OnGameEnd");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"HaisonReturn_OnGameEndPatch: {e}");
            }
        }
    }

    // ---------------------------------------------------------------------- patches

    /// <summary>End screen: the buttons appeared (end of EndGameManager.CoBegin) → auto "play again" after a haison.</summary>
    [HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.ShowButtons))]
    internal static class HaisonReturn_ShowButtonsPatch
    {
        private static void Postfix(EndGameManager __instance)
        {
            try
            {
                HaisonReturn.OnEndScreenReady(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"HaisonReturn_ShowButtonsPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(EndGameNavigation), nameof(EndGameNavigation.NextGame))]
    internal static class HaisonReturn_NextGamePatch
    {
        private static void Prefix()
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                PocketRolesPlugin.Logger.LogInfo($"Trace: EndGameNavigation.NextGame (auto={HaisonReturn.Returning})");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"HaisonReturn_NextGamePatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class HaisonReturn_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            try
            {
                HaisonReturn.Reset("OnGameJoined");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"HaisonReturn_OnGameJoinedPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    internal static class HaisonReturn_OnDisconnectedPatch
    {
        private static void Postfix()
        {
            try
            {
                HaisonReturn.Reset("OnDisconnected");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"HaisonReturn_OnDisconnectedPatch: {e}");
            }
        }
    }
}
