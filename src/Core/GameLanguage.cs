using System;
using System.Threading;
using HarmonyLib;

namespace PocketRoles.Core
{
    /// <summary>
    /// v0.5.5: the game's own display language (Among Us Settings → Language) as "ja" / "zh" / "en", for
    /// [General] Language = auto (<see cref="Options.Language"/>). Read from the TranslationController on the main
    /// thread only (at most every half second); other threads get the last value. null until the game has one.
    /// </summary>
    internal static class GameLanguage
    {
        private static volatile string _code;
        private static int _mainThread = -1;
        private static int _lastRead;

        /// <summary>"ja" | "zh" | "en", or null while the game's language is not known yet.</summary>
        public static string Code
        {
            get
            {
                Refresh(false);
                return _code;
            }
        }

        /// <summary>Called from Plugin.Load (the Unity main thread): only that thread touches the game objects.</summary>
        public static void MarkMainThread() => _mainThread = Thread.CurrentThread.ManagedThreadId;

        /// <summary>Re-reads the game's language (main thread only; throttled unless <paramref name="force"/>).</summary>
        public static void Refresh(bool force)
        {
            if (_mainThread < 0 || Thread.CurrentThread.ManagedThreadId != _mainThread) return;
            int now = Environment.TickCount;
            if (!force && _code != null && unchecked(now - _lastRead) < 500) return;
            _lastRead = now;
            try
            {
                if (!TranslationController.InstanceExists) return;
                var unit = TranslationController.Instance.currentLanguage;
                if (unit == null) return;
                string code = LangCore.FromGameLanguage(unit.languageID.ToString());
                if (code != null && code != _code)
                {
                    PocketRolesPlugin.Logger?.LogInfo($"GameLanguage: the game runs in {unit.languageID} → {code}");
                    _code = code;
                }
            }
            catch (Exception e)
            {
                if (force) PocketRolesPlugin.Logger?.LogWarning($"GameLanguage.Refresh: {e.Message}");
            }
        }
    }

    /// <summary>
    /// The main menu is up: the game's language is known now, so the one-time v0.5.5 language migration
    /// (<see cref="Options.ApplyLanguageMigration"/>) can decide.
    /// </summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    internal static class GameLanguage_MainMenuPatch
    {
        private static void Postfix()
        {
            try
            {
                GameLanguage.Refresh(true);
                Options.ApplyLanguageMigration();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogError($"GameLanguage_MainMenuPatch: {e}");
            }
        }
    }

    /// <summary>
    /// The host's first lobby after the migration: one line on the HOST's own screen (never sent to anyone), in the
    /// language PocketRoles now uses.
    /// </summary>
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    internal static class GameLanguage_LobbyNoticePatch
    {
        private static void Postfix()
        {
            try
            {
                if (!Options.LanguageMigrationNoticePending) return;
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return;
                Options.LanguageMigrationNoticePending = false;
                Chat.Chat.LocalWhenReady(() => string.Format(Lang.T("lang.migrated",
                    "PocketRoles の言語をゲームに合わせて{0}にしました。/lang ja で日本語に戻せます",
                    "PocketRoles now follows the game's language: {0}. /lang ja switches back to Japanese",
                    "PocketRoles 的语言已跟随游戏设为{0}。输入 /lang ja 可改回日语"),
                    Lang.DisplayName(Lang.Default)));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogError($"GameLanguage_LobbyNoticePatch: {e}");
            }
        }
    }
}
