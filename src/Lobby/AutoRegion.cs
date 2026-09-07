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
    /// <see cref="Options.AutoRegion"/> is on the probe runs when the online menu opens (never while connected) and
    /// <c>ServerManager.SetRegion</c> switches to the best one if it differs from the current region. Results are cached
    /// for 10 minutes; the table is logged and shown once in the host's next lobby chat.
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
            else if (!CanSelectNow())
            {
                action = Lang.TF("region.later", "最速は {0}（現在: {1}）。接続中は変更しません。次にホストする前に切り替わります。",
                    "Fastest: {0} (current: {1}). Not changed while connected; it switches before the next hosting.", best.Name, current);
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

        private static bool CanSelectNow()
        {
            var client = AmongUsClient.Instance;
            if (client == null) return true;
            return !client.AmConnected;
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
                return CurrentRegionName() == best.Name;
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
        }

        /// <summary>Online menu opened (not connected): probe when AutoRegion is on and the cache is stale.</summary>
        internal static void OnOnlineMenuOpened()
        {
            if (!Options.AutoRegion) return;
            if (!CanSelectNow()) return;
            float now = Time.realtimeSinceStartup;
            if (_lastProbeAt >= 0f && now - _lastProbeAt < CacheSeconds && _lastBest != null)
            {
                // Cached result: still apply it if the region was changed back by hand? No — respect the user's choice.
                return;
            }
            _autoTriggered = true;
            ProbeAndSelect(null, true);
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

    /// <summary>Online menu: probe before the user can host (CoCreateOnlineGame is already connecting).</summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenOnlineMenu))]
    internal static class AutoRegion_OpenOnlineMenuPatch
    {
        private static void Postfix()
        {
            try
            {
                AutoRegion.OnOnlineMenuOpened();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoRegion_OpenOnlineMenuPatch: {e}");
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
