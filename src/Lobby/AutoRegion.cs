using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using PocketRoles.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace PocketRoles.Lobby
{
    /// <summary>
    /// Lowest-latency official region (DESIGN-v0.4 §D, SuperNewRoles technique).
    /// <para>
    /// Every entry of <c>ServerManager.DefaultRegions</c> (North America / Europe / Asia, PingServer =
    /// https://matchmaker[-eu|-as].among.us) is probed with HTTPS HEAD requests (UnityWebRequest, 3-s timeout) from a
    /// coroutine on the ServerManager; the median RTT of the successful samples ranks the regions. When
    /// <see cref="Options.AutoRegion"/> is on the probe runs the moment the CREATE-GAME screen opens (v0.5.5, design
    /// review §8; never while connected) and <c>ServerManager.SetRegion</c> switches to the best one if it differs from
    /// the current region. Opening the online menu, "find game" and joining by code never probe and never switch, so
    /// somebody who only wants to join a room by its code keeps the region they picked. Results are cached for 10
    /// minutes; the table is logged and shown once in the host's next lobby chat.
    /// </para>
    /// <para>
    /// A probe takes seconds (3 regions × 4 sequential HTTPS HEADs, 3-s timeout each), so the screen it started from
    /// is asked for again when the probe ENDS, not only when it began (review 2026-09-23): the switch happens only
    /// while that create-game screen is still open and nothing is connecting, and never over a region the user picked
    /// by hand meanwhile. A switch that could not be carried out is remembered and applied — from the cached result,
    /// with no second probe — the next time the create-game screen opens.
    /// </para>
    /// </summary>
    public static class AutoRegion
    {
        private const int Samples = 3;
        private const int TimeoutSeconds = 3;
        private const float CacheSeconds = 600f;

        private sealed class Probe
        {
            public string Name;
            public string Url;
            public IRegionInfo Region;
            public List<float> Rtts = new List<float>();
            public int Failures;
            public float Median = -1f;
        }

        private static bool _probing;
        private static float _lastProbeAt = -1f;
        private static string _lastTable;
        private static string _lastBest;
        private static readonly List<Action<string>> _reporters = new List<Action<string>>();

        /// <summary>Report text waiting to be shown in the host's next lobby chat (null when none).</summary>
        private static string _pendingLobbyReport;

        // ------------------------------------------------------------------ public API

        /// <summary>A probe is running.</summary>
        public static bool Probing => _probing;

        /// <summary>Last result table ("Region: 123 ms …"), or null.</summary>
        public static string LastTable => _lastTable;

        /// <summary>Name of the region the last probe found fastest, or null.</summary>
        public static string LastBest => _lastBest;

        /// <summary>Latency table lines of the last probe ("Region: 123 ms" per line), or null when nothing was measured yet.</summary>
        public static string TableText()
        {
            return string.IsNullOrWhiteSpace(_lastTable) ? null : _lastTable;
        }

        /// <summary>Name of the region the client currently uses ("?" when unknown).</summary>
        public static string CurrentRegionName()
        {
            try
            {
                if (!ServerManager.InstanceExists) return "?";
                var sm = ServerManager.Instance;
                var cur = sm != null ? sm.CurrentRegion : null;
                return cur != null && !string.IsNullOrEmpty(cur.Name) ? cur.Name : "?";
            }
            catch (Exception)
            {
                return "?";
            }
        }

        /// <summary>
        /// Probes the official regions and reports the table through <paramref name="report"/> (main thread, once the
        /// probe is done; a cached result younger than 10 minutes is reported immediately). The best region is applied
        /// with <c>SetRegion</c> only when the client is not connected (never while hosting / in a lobby).
        /// </summary>
        public static void ProbeAndSelect(Action<string> report)
        {
            ProbeAndSelect(report, false);
        }

        /// <summary>Same as <see cref="ProbeAndSelect(Action{string})"/>; <paramref name="force"/> ignores the cache.</summary>
        public static void ProbeAndSelect(Action<string> report, bool force)
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (!force && _lastTable != null && _lastProbeAt >= 0f && now - _lastProbeAt < CacheSeconds)
                {
                    Report(report, _lastTable + "\n" + CurrentLine());
                    return;
                }
                if (report != null) _reporters.Add(report);
                if (_probing) return;
                if (!ServerManager.InstanceExists || ServerManager.Instance == null)
                {
                    Finish(Lang.T("region.noserver", "サーバー一覧がまだ読み込まれていません。", "The server list is not loaded yet."), null);
                    return;
                }
                var probes = BuildProbes();
                if (probes.Count == 0)
                {
                    Finish(Lang.T("region.none", "公式の地域が見つかりません。", "No official region found."), null);
                    return;
                }
                _probing = true;
                ServerManager.Instance.StartCoroutine(CoProbe(probes).WrapToIl2Cpp());
            }
            catch (Exception e)
            {
                _probing = false;
                PocketRolesPlugin.Logger.LogError($"AutoRegion.ProbeAndSelect: {e}");
                Finish(Lang.T("region.error", "地域の計測に失敗しました。", "Region probe failed."), null);
            }
        }

        // ------------------------------------------------------------------ internals

        private static void Report(Action<string> report, string text)
        {
            if (report == null) return;
            try { report(text); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"AutoRegion report callback: {e}"); }
        }

        private static string CurrentLine()
        {
            return Lang.TF("region.current", "現在の地域: {0}", "Current region: {0}", CurrentRegionName());
        }

        private static List<Probe> BuildProbes()
        {
            var list = new List<Probe>();
            var defaults = ServerManager.DefaultRegions;
            if (defaults == null) return list;
            foreach (var r in defaults)
            {
                if (r == null) continue;
                string name = r.Name;
                string url = r.PingServer;
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
                if (!url.EndsWith("/")) url += "/";
                list.Add(new Probe { Name = name, Url = url, Region = r });
            }
            return list;
        }

        private static IEnumerator CoProbe(List<Probe> probes)
        {
            // Warm-up (TLS handshake) then the measured samples, region by region.
            foreach (var p in probes)
            {
                for (int i = 0; i <= Samples; i++)
                {
                    UnityWebRequest req = null;
                    float t0 = 0f;
                    bool started = false;
                    try
                    {
                        req = UnityWebRequest.Head(p.Url);
                        req.timeout = TimeoutSeconds;
                        t0 = Time.realtimeSinceStartup;
                        started = true;
                    }
                    catch (Exception e)
                    {
                        PocketRolesPlugin.Logger.LogWarning($"AutoRegion: request setup failed for {p.Name}: {e.Message}");
                    }
                    if (!started || req == null)
                    {
                        p.Failures++;
                        continue;
                    }
                    yield return req.SendWebRequest();
                    try
                    {
                        float rtt = (Time.realtimeSinceStartup - t0) * 1000f;
                        bool ok = req.responseCode > 0; // any HTTP answer counts (405 on HEAD is still a round trip)
                        if (i > 0)
                        {
                            if (ok) p.Rtts.Add(rtt);
                            else p.Failures++;
                        }
                    }
                    catch (Exception e)
                    {
                        p.Failures++;
                        PocketRolesPlugin.Logger.LogWarning($"AutoRegion: probe of {p.Name} failed: {e.Message}");
                    }
                    finally
                    {
                        try { req.Dispose(); } catch (Exception) { }
                    }
                }
                if (p.Rtts.Count > 0)
                {
                    p.Rtts.Sort();
                    int n = p.Rtts.Count;
                    p.Median = n % 2 == 1 ? p.Rtts[n / 2] : (p.Rtts[n / 2 - 1] + p.Rtts[n / 2]) / 2f;
                }
            }
            try
            {
                Conclude(probes);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoRegion.Conclude: {e}");
                _probing = false;
                Finish(Lang.T("region.error", "地域の計測に失敗しました。", "Region probe failed."), null);
            }
        }

        private static void Conclude(List<Probe> probes)
        {
            string current = CurrentRegionName();
            Probe best = null;
            var sb = new StringBuilder();
            foreach (var p in probes)
            {
                if (sb.Length > 0) sb.Append('\n');
                if (p.Median >= 0f)
                {
                    sb.Append(p.Name).Append(": ").Append((int)p.Median).Append(" ms");
                    if (best == null || p.Median < best.Median || (Math.Abs(p.Median - best.Median) < 0.5f && p.Name == current)) best = p;
                }
                else
                {
                    sb.Append(p.Name).Append(": ").Append(Lang.T("region.fail", "失敗", "failed"));
                }
            }
            string table = sb.ToString();
            _lastTable = table;
            _lastBest = best != null ? best.Name : null;
            _lastBestRegion = best != null ? best.Region : null;
            _lastProbeAt = Time.realtimeSinceStartup;
            PocketRolesPlugin.Logger.LogInfo("AutoRegion: " + table.Replace('\n', ' ') + $" → best={_lastBest ?? "none"}, current={current}");

            string action;
            if (best == null)
            {
                action = Lang.T("region.allfail", "どの地域にも接続できませんでした。", "No region answered.");
            }
            else if (best.Name == current)
            {
                action = Lang.TF("region.keep", "最速は {0}（現在の地域のままです）", "Fastest: {0} (already selected)", best.Name);
            }
            else if (_autoTriggered && PickedByHandDuringProbe(current))
            {
                // v0.5.5 review: the user picked a region BY HAND in the create-game dropdown while the probe ran.
                action = Lang.TF("region.manual", "最速は {0}（現在: {1}）。手で選んだ地域はそのままにします。",
                    "Fastest: {0} (current: {1}). The region you picked by hand is kept.", best.Name, current);
            }
            else if (!CanApplyNow())
            {
                // v0.5.5 review: the probe takes seconds, so it often ends after the create-game screen is gone — the
                // user backed out, or is already connecting (AmConnected stays false for the whole connect, which is
                // exactly the "joined by code" case this feature must never touch). Remember the switch instead and
                // apply it from the cache the next time the create-game screen opens, which is what region.later says.
                // With AutoRegion off nothing will ever open that path, so promise nothing there.
                _switchPending = Options.AutoRegion;
                action = _switchPending
                    ? Lang.TF("region.later", "最速は {0}（現在: {1}）。今は切り替えません。次に「部屋を作る」の画面を開いた時に切り替えます。",
                        "Fastest: {0} (current: {1}). Not switched now; it switches the next time you open the CREATE GAME screen.", best.Name, current)
                    : Lang.TF("region.notnow", "最速は {0}（現在: {1}）。接続中なので切り替えません（ゲームの地域選択で手で変えられます）。",
                        "Fastest: {0} (current: {1}). Not switched while connected; you can pick it by hand in the game's region menu.", best.Name, current);
            }
            else if (!Options.AutoRegion && _autoTriggered)
            {
                action = Lang.TF("region.suggest", "最速は {0}（現在: {1}）", "Fastest: {0} (current: {1})", best.Name, current);
            }
            else
            {
                bool applied = ApplyRegion(best);
                action = applied
                    ? Lang.TF("region.switched", "地域を {0} に切り替えました（{1} → {0}）", "Region switched to {0} ({1} → {0})", best.Name, current)
                    : Lang.TF("region.failed", "地域 {0} への切り替えに失敗しました。", "Could not switch to region {0}.", best.Name);
            }
            _probing = false;
            Finish(table + "\n" + action, best);
        }

        private static bool _autoTriggered;

        /// <summary>
        /// Nothing is connecting. <c>AmConnected</c> stays false for the WHOLE duration of a connect, so on its own
        /// this does not tell a create-game screen from somebody who is joining a room by its code — see
        /// <see cref="CanApplyNow"/>.
        /// </summary>
        private static bool CanSelectNow()
        {
            var client = AmongUsClient.Instance;
            if (client == null) return true;
            return !client.AmConnected;
        }

        /// <summary>The create-game screen the running probe was started from (v0.5.5): its region line is refreshed after a switch.</summary>
        private static MainMenuManager _createMenu;

        /// <summary>The region selected when the running probe started: a region picked BY HAND meanwhile is never overwritten.</summary>
        private static string _regionAtProbeStart;

        /// <summary>The region info of the last probe's fastest region (for a switch applied from the cache).</summary>
        private static IRegionInfo _lastBestRegion;

        /// <summary>A switch the probe could not apply (screen closed / connecting): applied at the next create-game screen.</summary>
        private static bool _switchPending;

        /// <summary>The create-game screen this probe was started from is still on screen.</summary>
        private static bool CreateScreenOpen()
        {
            try
            {
                var menu = _createMenu;
                if (menu == null) return false;
                var screen = menu.createGameScreen;
                if (screen == null) return false;
                var go = screen.gameObject;
                return go != null && go.activeInHierarchy;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"AutoRegion: could not read the create-game screen: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// May the region be switched at THIS moment? v0.5.5 review: the probe is started on the create-game screen
        /// but takes seconds, so the answer has to be asked again when it ends, not only when it began. An automatic
        /// switch needs the very create-game screen that started it to be still open — otherwise the user has backed
        /// out and may already be connecting to somebody else's room by code, where a SetRegion is exactly the bug
        /// this feature had. A manual /region probe keeps the old rule (not connected).
        /// </summary>
        private static bool CanApplyNow()
        {
            if (!CanSelectNow()) return false;
            if (!_autoTriggered) return true;
            return CreateScreenOpen();
        }

        /// <summary>The selected region changed while the probe ran, and both names are known ("?" means unreadable).</summary>
        private static bool PickedByHandDuringProbe(string current)
        {
            if (string.IsNullOrEmpty(_regionAtProbeStart) || _regionAtProbeStart == "?") return false;
            if (string.IsNullOrEmpty(current) || current == "?") return false;
            return current != _regionAtProbeStart;
        }

        /// <summary>
        /// Show the new region on the create-game screen that is still open (the same text vanilla writes when the
        /// region is chosen in its own dropdown). Best effort: a closed / destroyed screen is simply skipped.
        /// </summary>
        private static void RefreshCreateScreen()
        {
            try
            {
                var menu = _createMenu;
                if (menu == null) return;
                var screen = menu.createGameScreen;
                if (screen == null) return;
                screen.SetCurrentServer();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"AutoRegion: could not refresh the create-game screen: {e.Message}");
            }
        }

        private static bool ApplyRegion(Probe best)
        {
            try
            {
                var sm = ServerManager.Instance;
                if (sm == null) return false;
                IRegionInfo target = null;
                var available = sm.AvailableRegions;
                if (available != null)
                {
                    foreach (var r in available)
                    {
                        if (r != null && r.Name == best.Name) { target = r; break; }
                    }
                }
                if (target == null) target = best.Region;
                if (target == null) return false;
                sm.SetRegion(target);
                PocketRolesPlugin.Logger.LogInfo($"AutoRegion: SetRegion({best.Name})");
                bool ok = CurrentRegionName() == best.Name;
                if (ok) RefreshCreateScreen();   // v0.5.5: the create-game screen still shows the old region name
                return ok;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoRegion.ApplyRegion: {e}");
                return false;
            }
        }

        private static void Finish(string text, Probe best)
        {
            _probing = false;
            var reporters = _reporters.ToArray();
            _reporters.Clear();
            foreach (var r in reporters) Report(r, text);
            if (_autoTriggered)
            {
                _autoTriggered = false;
                _pendingLobbyReport = text;
            }
            _createMenu = null;   // v0.5.5: never hold the menu of a screen that is long closed
            _regionAtProbeStart = null;
        }

        /// <summary>
        /// The CREATE-GAME screen opened (v0.5.5, design review §8): the only moment the region may be switched by
        /// itself, because this is the one path that ends in hosting a NEW room. Probes when AutoRegion is on, the
        /// client is not connected and the cache is stale; with a fresh cache it only carries out a switch the last
        /// probe could not apply. Joining by code, "find game" and the online menu itself never come here.
        /// </summary>
        internal static void OnCreateGameOpened(MainMenuManager menu)
        {
            if (!Options.AutoRegion) return;
            if (!CanSelectNow()) return;
            _createMenu = menu;
            float now = Time.realtimeSinceStartup;
            if (_lastProbeAt >= 0f && now - _lastProbeAt < CacheSeconds && _lastBest != null)
            {
                // Fresh result (10 min): no second probe. A switch the last probe could NOT apply is carried out now
                // from the cache — that is what region.later promises, and it is instant, so no race with Create.
                // Without a pending switch the cached result is never re-applied: a region the user picked by hand
                // after the last switch stays.
                if (_switchPending) ApplyPendingSwitch();
                return;
            }
            _autoTriggered = true;
            _switchPending = false;
            _regionAtProbeStart = CurrentRegionName();
            ProbeAndSelect(null, true);
        }

        /// <summary>
        /// The create-game screen opened again and the last probe's switch never happened (v0.5.5 review): apply it
        /// from the cached result, without probing, and show the outcome in the next lobby like a normal probe.
        /// </summary>
        private static void ApplyPendingSwitch()
        {
            _switchPending = false;
            string current = CurrentRegionName();
            if (string.IsNullOrEmpty(_lastBest) || _lastBest == current) return;
            bool applied = ApplyRegion(new Probe { Name = _lastBest, Region = _lastBestRegion });
            string action = applied
                ? Lang.TF("region.switched", "地域を {0} に切り替えました（{1} → {0}）", "Region switched to {0} ({1} → {0})", _lastBest, current)
                : Lang.TF("region.failed", "地域 {0} への切り替えに失敗しました。", "Could not switch to region {0}.", _lastBest);
            PocketRolesPlugin.Logger.LogInfo($"AutoRegion: pending switch from the cached probe: {current} → {_lastBest}, applied={applied}");
            _pendingLobbyReport = (_lastTable != null ? _lastTable + "\n" : "") + action;
        }

        /// <summary>Host's lobby chat: show the table of the probe that ran before hosting (once).</summary>
        internal static void OnLobbyStart()
        {
            string text = _pendingLobbyReport;
            if (text == null) return;
            _pendingLobbyReport = null;
            string full = Lang.T("region.title", "地域の自動選択:", "Auto region:") + "\n" + text;
            Chat.Chat.LocalWhenReady(() => full);
        }
    }

    // ---------------------------------------------------------------------- patches

    /// <summary>
    /// v0.5.5 (design review §8): the create-game screen, i.e. the user is about to host a NEW room — the probe (and
    /// with it the only automatic <c>SetRegion</c>) runs here and nowhere else, well before CoCreateOnlineGame starts
    /// connecting. Before v0.5.5 this sat on <c>OpenOnlineMenu</c>, which also runs for somebody who only wants to
    /// join a room by its code.
    /// </summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenCreateGame))]
    internal static class AutoRegion_OpenCreateGamePatch
    {
        private static void Postfix(MainMenuManager __instance)
        {
            try
            {
                AutoRegion.OnCreateGameOpened(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoRegion_OpenCreateGamePatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    internal static class AutoRegion_LobbyStartPatch
    {
        private static void Postfix()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return;
                AutoRegion.OnLobbyStart();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoRegion_LobbyStartPatch: {e}");
            }
        }
    }
}
