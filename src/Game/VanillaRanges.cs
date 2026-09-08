using System;
using System.Globalization;
using System.Text;
using AmongUs.GameOptions;
using HarmonyLib;
using PocketRoles.Core;

namespace PocketRoles.Game
{
    /// <summary>
    /// v0.4b "vanilla extended ranges": lets the host set the vanilla numeric settings (kill cooldown, voting /
    /// discussion time, emergency cooldown, task counts, player speed, vision) below and above the vanilla limits.
    /// The lobby settings screen keeps its own rows; we only widen <see cref="NumberOption.ValidRange"/> and refine
    /// <see cref="NumberOption.Increment"/> of the rows whose option name is one of ours (never by display title), so the
    /// vanilla arrows write the value through the normal path (NumberOption.UpdateValue → IGameOptions.SetFloat/SetInt →
    /// GameOptionsMenu.ValueChanged → LogicOptions.SyncOptions) and vanilla clients receive it with the usual settings
    /// sync. <see cref="TrySet"/> is the same thing for the chat command /vset.
    /// </summary>
    public static class VanillaRanges
    {
        // ------------------------------------------------------------------ table

        private sealed class Spec
        {
            public string Key;                       // /vset key
            public string[] Aliases;
            public FloatOptionNames Float = FloatOptionNames.Invalid;
            public Int32OptionNames Int = Int32OptionNames.Invalid;
            public bool IsInt => Int != Int32OptionNames.Invalid;
            // vanilla limits (fallback when the settings screen has not told us the real ones yet)
            public float VanMin, VanMax, VanStep;
            public Func<float> Min, Max, Step;       // extended limits from Options
            public StringNames Title;
            public bool Seconds;                     // unit: seconds (else multiplier)
            public string NameJa, NameEn;
            public string Name => Lang.T("vset.name." + Key, NameJa, NameEn);
            /// <summary>Real vanilla range once seen on a settings row (null until the menu was opened).</summary>
            public float? SeenMin, SeenMax, SeenStep;
        }

        private static readonly Spec[] Specs =
        {
            new Spec { Key = "killcd", Aliases = new[] { "kill", "killcooldown", "cooldown", "kcd", "キル" }, Float = FloatOptionNames.KillCooldown,
                VanMin = 10f, VanMax = 60f, VanStep = 2.5f, Min = () => Options.KillCooldownMin, Max = () => Options.KillCooldownMax, Step = () => Options.KillCooldownStep,
                Title = StringNames.GameKillCooldown, Seconds = true, NameJa = "キルクールダウン", NameEn = "Kill cooldown" },
            new Spec { Key = "vote", Aliases = new[] { "voting", "votingtime", "votetime", "投票" }, Int = Int32OptionNames.VotingTime,
                VanMin = 15f, VanMax = 300f, VanStep = 15f, Min = () => Options.VotingTimeMin, Max = () => Options.VotingTimeMax, Step = () => 5f,
                Title = StringNames.GameVotingTime, Seconds = true, NameJa = "投票時間", NameEn = "Voting time" },
            new Spec { Key = "discuss", Aliases = new[] { "discussion", "discussiontime", "talk", "会議" }, Int = Int32OptionNames.DiscussionTime,
                VanMin = 0f, VanMax = 120f, VanStep = 15f, Min = () => 0f, Max = () => Options.DiscussionTimeMax, Step = () => 5f,
                Title = StringNames.GameDiscussTime, Seconds = true, NameJa = "会議時間", NameEn = "Discussion time" },
            new Spec { Key = "emergency", Aliases = new[] { "emergencycd", "emergencycooldown", "meetingcd", "緊急" }, Int = Int32OptionNames.EmergencyCooldown,
                VanMin = 0f, VanMax = 60f, VanStep = 5f, Min = () => 0f, Max = () => Options.EmergencyCooldownMax, Step = () => 5f,
                Title = StringNames.GameEmergencyCooldown, Seconds = true, NameJa = "緊急会議クールダウン", NameEn = "Emergency cooldown" },
            new Spec { Key = "common", Aliases = new[] { "commontasks", "commontask", "コモン" }, Int = Int32OptionNames.NumCommonTasks,
                VanMin = 0f, VanMax = 2f, VanStep = 1f, Min = () => 0f, Max = () => Options.TaskCountMax, Step = () => 1f,
                Title = StringNames.GameCommonTasks, Seconds = false, NameJa = "コモンタスク数", NameEn = "Common tasks" },
            new Spec { Key = "short", Aliases = new[] { "shorttasks", "shorttask", "ショート" }, Int = Int32OptionNames.NumShortTasks,
                VanMin = 0f, VanMax = 5f, VanStep = 1f, Min = () => 0f, Max = () => Options.TaskCountMax, Step = () => 1f,
                Title = StringNames.GameShortTasks, Seconds = false, NameJa = "ショートタスク数", NameEn = "Short tasks" },
            new Spec { Key = "long", Aliases = new[] { "longtasks", "longtask", "ロング" }, Int = Int32OptionNames.NumLongTasks,
                VanMin = 0f, VanMax = 3f, VanStep = 1f, Min = () => 0f, Max = () => Options.TaskCountMax, Step = () => 1f,
                Title = StringNames.GameLongTasks, Seconds = false, NameJa = "ロングタスク数", NameEn = "Long tasks" },
            // speed / vision have no Options entries (fixed extension, user request "if easy").
            // Speed stays inside the vanilla-legal range: NormalGameOptions.AreInvalid rejects PlayerSpeedMod <= 0 or > 3
            // and a saved value outside it makes every following CreateGame fail with InvalidGameOptions.
            new Spec { Key = "speed", Aliases = new[] { "playerspeed", "movespeed", "速度" }, Float = FloatOptionNames.PlayerSpeedMod,
                VanMin = 0.5f, VanMax = 3f, VanStep = 0.25f, Min = () => SpeedLegalMin, Max = () => SpeedLegalMax, Step = () => 0.25f,
                Title = StringNames.GamePlayerSpeed, Seconds = false, NameJa = "移動速度", NameEn = "Player speed" },
            new Spec { Key = "vision", Aliases = new[] { "crewvision", "crewlight", "light", "視界" }, Float = FloatOptionNames.CrewLightMod,
                VanMin = 0.25f, VanMax = 5f, VanStep = 0.25f, Min = () => 0.1f, Max = () => 10f, Step = () => 0.25f,
                Title = StringNames.GameCrewLight, Seconds = false, NameJa = "クルー視界", NameEn = "Crewmate vision" },
            new Spec { Key = "impvision", Aliases = new[] { "impostorvision", "implight", "impostorlight", "インポ視界" }, Float = FloatOptionNames.ImpostorLightMod,
                VanMin = 0.25f, VanMax = 5f, VanStep = 0.25f, Min = () => 0.1f, Max = () => 10f, Step = () => 0.25f,
                Title = StringNames.GameImpostorLight, Seconds = false, NameJa = "インポスター視界", NameEn = "Impostor vision" },
        };

        /// <summary>Player speed accepted by the vanilla option validation (IGameOptions.AreInvalid): 0 &lt; speed ≤ 3.</summary>
        private const float SpeedLegalMin = 0.5f, SpeedLegalMax = 3f;
        /// <summary>Max players passed to AreInvalid (the vanilla lobby limit).</summary>
        private const int MaxExpectedPlayers = 15;

        private static Spec Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string k = key.Trim().ToLowerInvariant();
            foreach (var s in Specs)
            {
                if (s.Key == k) return s;
                foreach (var a in s.Aliases) if (a == k) return s;
            }
            return null;
        }

        private static Spec Find(FloatOptionNames f, Int32OptionNames i)
        {
            foreach (var s in Specs)
            {
                if (s.IsInt) { if (i != Int32OptionNames.Invalid && s.Int == i) return s; }
                else if (f != FloatOptionNames.Invalid && s.Float == f) return s;
            }
            return null;
        }

        /// <summary>Effective limits: the vanilla ones, widened (never narrowed) by Options when ExtendedRanges is on.</summary>
        private static void Limits(Spec s, out float min, out float max, out float step)
        {
            float vMin = s.SeenMin ?? s.VanMin, vMax = s.SeenMax ?? s.VanMax, vStep = s.SeenStep ?? s.VanStep;
            min = vMin; max = vMax; step = vStep;
            if (!Options.ExtendedRanges || Net.Rpc.CompatMode) return; // unregistered lobby: vanilla ranges only (server validation)
            try
            {
                float m = s.Min(), x = s.Max(), st = s.Step();
                if (m < min) min = m;
                if (x > max) max = x;
                if (st > 0f && st < step) step = st;
                if (s.IsInt) { min = (float)Math.Round(min); max = (float)Math.Round(max); step = Math.Max(1f, (float)Math.Round(step)); }
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"VanillaRanges: limits of {s.Key}: {e.Message}"); }
            if (!s.IsInt && s.Float == FloatOptionNames.PlayerSpeedMod)
            {
                // never offer a speed the vanilla validation rejects (see the spec comment)
                if (min < SpeedLegalMin) min = SpeedLegalMin;
                if (max > SpeedLegalMax) max = SpeedLegalMax;
            }
            if (max < min) max = min;
        }

        // ------------------------------------------------------------------ settings-screen rows

        /// <summary>Widen the row if it is one of ours. Returns true when something was applied.</summary>
        internal static bool Apply(NumberOption n)
        {
            if (n == null) return false;
            // PocketRoles' own rows (SettingsTab clones of numberOptionOrigin) carry Invalid option names, but never
            // touch them even if a prefab default leaks through: their range comes from the OptionDescriptor.
            try { if (UI.SettingsTab.Find(n, out _)) return false; } catch (Exception) { }
            var s = Find(n.floatOptionName, n.intOptionName);
            if (s == null) return false;
            // remember the real vanilla range once (from the FloatGameSetting / IntGameSetting the row was set up with)
            if (s.SeenMin == null)
            {
                try
                {
                    var fs = n.data != null ? n.data.TryCast<FloatGameSetting>() : null;
                    var ints = n.data != null ? n.data.TryCast<IntGameSetting>() : null;
                    if (fs != null && fs.ValidRange != null) { s.SeenMin = fs.ValidRange.min; s.SeenMax = fs.ValidRange.max; s.SeenStep = fs.Increment; }
                    else if (ints != null && ints.ValidRange != null) { s.SeenMin = ints.ValidRange.min; s.SeenMax = ints.ValidRange.max; s.SeenStep = ints.Increment; }
                }
                catch (Exception) { }
            }
            if (!Options.ExtendedRanges || Net.Rpc.CompatMode) return false;
            Limits(s, out float min, out float max, out float step);
            n.ValidRange = new FloatRange(min, max);
            n.Increment = step;
            try { n.AdjustButtonsActiveState(); } catch (Exception) { }
            return true;
        }

        // ------------------------------------------------------------------ /vset

        public static string Usage()
        {
            return Lang.T("vset.usage",
                "使い方: /vset <killcd|vote|discuss|emergency|common|short|long|speed|vision|impvision> <値>",
                "Usage: /vset <killcd|vote|discuss|emergency|common|short|long|speed|vision|impvision> <value>");
        }

        /// <summary>Current values of the settings /vset can change (one line).</summary>
        public static string Show()
        {
            try
            {
                var gom = GameOptionsManager.Instance;
                var o = gom != null ? gom.CurrentGameOptions : null;
                if (o == null) return Usage();
                string sec = Lang.T("vset.unit.sec", "秒", "s");
                string x = Lang.T("vset.unit.x", "倍", "x");
                return Lang.TF("vset.current",
                    "現在: キルCD {0}{9}, 投票 {1}{9}, 会議 {2}{9}, 緊急CD {3}{9}, タスク {4}/{5}/{6}, 速度 {7}{10}, 視界 {8}{10}",
                    "Now: kill {0}{9}, vote {1}{9}, discuss {2}{9}, emergency {3}{9}, tasks {4}/{5}/{6}, speed {7}{10}, vision {8}{10}",
                    Fmt(o.GetFloat(FloatOptionNames.KillCooldown)), o.GetInt(Int32OptionNames.VotingTime), o.GetInt(Int32OptionNames.DiscussionTime),
                    o.GetInt(Int32OptionNames.EmergencyCooldown), o.GetInt(Int32OptionNames.NumCommonTasks), o.GetInt(Int32OptionNames.NumShortTasks),
                    o.GetInt(Int32OptionNames.NumLongTasks), Fmt(o.GetFloat(FloatOptionNames.PlayerSpeedMod)),
                    Fmt(o.GetFloat(FloatOptionNames.CrewLightMod)) + "/" + Fmt(o.GetFloat(FloatOptionNames.ImpostorLightMod)), sec, x);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"VanillaRanges.Show: {e}");
                return Usage();
            }
        }

        /// <summary>
        /// Logs the current vanilla numeric settings and the vanilla validator's verdict (2026-09-09: an unregistered
        /// lobby gets its host disconnected for "Hacking" when the synced options fail the official validation, which the
        /// relaxed +25 anti-cheat tolerates — suspected cause of the compat-mode kicks at settings close / player join).
        /// </summary>
        public static void LogHealth(string where)
        {
            try
            {
                var gom = GameOptionsManager.Instance;
                var o = gom != null ? gom.CurrentGameOptions : null;
                if (o == null) { PocketRolesPlugin.Logger.LogInfo($"VanillaRanges: options not loaded ({where})"); return; }
                bool invalid = false;
                try { invalid = o.AreInvalid(MaxExpectedPlayers); } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"VanillaRanges: AreInvalid: {e.Message}"); }
                string line;
                using (Lang.Scope("en")) line = Show();
                PocketRolesPlugin.Logger.LogInfo($"VanillaRanges: {where}: vanilla validation {(invalid ? "FAILS" : "ok")}; map={o.MapId} imps={o.NumImpostors} maxPlayers={o.MaxPlayers}; {line}");
                if (invalid) PocketRolesPlugin.Logger.LogWarning("VanillaRanges: the current options fail the vanilla validation - an unregistered (compat) lobby will disconnect the host when they are synced");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"VanillaRanges.LogHealth: {e}");
            }
        }

        /// <summary>
        /// Unregistered (compat) lobby: pull every /vset-able option back into its vanilla range and step. The extended
        /// values a registered lobby tolerates (3 common tasks, 10x impostor vision, a 27-s emergency cooldown …) fail the
        /// official server's option validation in an unregistered lobby, which disconnects the host ("Hacking") at the
        /// next options sync — settings close or a player join (2026-09-08/09). Returns the number of changed options.
        /// </summary>
        public static int ClampToVanilla(string where)
        {
            int changed = 0;
            try
            {
                var gom = GameOptionsManager.Instance;
                var o = gom != null ? gom.CurrentGameOptions : null;
                if (o == null) return 0;
                var sb = new StringBuilder();
                foreach (var s in Specs)
                {
                    float vMin = s.SeenMin ?? s.VanMin, vMax = s.SeenMax ?? s.VanMax, vStep = s.SeenStep ?? s.VanStep;
                    float cur;
                    try { cur = s.IsInt ? o.GetInt(s.Int) : o.GetFloat(s.Float); } catch (Exception) { continue; }
                    float v = cur;
                    if (v < vMin) v = vMin;
                    if (v > vMax) v = vMax;
                    if (vStep > 0f) v = vMin + (float)Math.Round((v - vMin) / vStep) * vStep;
                    if (v > vMax) v = vMax;
                    if (s.IsInt) v = (float)Math.Round(v);
                    if (Math.Abs(v - cur) < 0.001f) continue;
                    try
                    {
                        if (s.IsInt) o.SetInt(s.Int, (int)v); else o.SetFloat(s.Float, v);
                        changed++;
                        sb.Append($" {s.Key} {Fmt(cur)}→{Fmt(v)};");
                    }
                    catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"VanillaRanges: clamp {s.Key}: {e.Message}"); }
                }
                if (changed > 0)
                {
                    try { o.SetInt(Int32OptionNames.RulePreset, (int)RulesPresets.Custom); } catch (Exception) { }
                    try { gom.GameHostOptions = o; } catch (Exception) { }
                    PocketRolesPlugin.Logger.LogWarning($"VanillaRanges: {where}: {changed} option(s) pulled back into the vanilla range for the unregistered lobby:{sb}");
                }
                else PocketRolesPlugin.Logger.LogInfo($"VanillaRanges: {where}: every option is inside the vanilla range");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"VanillaRanges.ClampToVanilla: {e}");
            }
            return changed;
        }

        /// <summary>
        /// /vset &lt;key&gt; &lt;value&gt;: writes the vanilla option directly (host only, lobby only) and syncs it to the
        /// clients like the settings screen does. <paramref name="msg"/> is the translated confirmation / error.
        /// </summary>
        public static bool TrySet(string key, string value, out string msg)
        {
            msg = "";
            try
            {
                if (!Core.Game.IsHostActive)
                {
                    msg = Lang.T("cmd.modoff", "MODは現在オフです（バニラの試合）。", "The mod is currently off (vanilla game).");
                    return false;
                }
                if (string.IsNullOrEmpty(key) || key.Trim().ToLowerInvariant() is "show" or "list" or "?" or "help")
                {
                    msg = Usage() + "\n" + Show();
                    return false;
                }
                var s = Find(key);
                if (s == null)
                {
                    msg = Lang.TF("vset.unknown", "不明な項目: {0}", "Unknown setting: {0}", key.Trim()) + "\n" + Usage();
                    return false;
                }
                if (Core.Game.InProgress || Core.Game.Ending)
                {
                    msg = Lang.T("vset.ingame", "試合中は変更できません（ロビーで使ってください）。", "Cannot change settings during a game (use it in the lobby).");
                    return false;
                }
                if (!TryParseNumber(value, out float v))
                {
                    msg = Lang.TF("vset.badvalue", "数値を指定してください: {0}", "Please give a number: {0}", value ?? "");
                    return false;
                }
                Limits(s, out float min, out float max, out float step);
                if (s.IsInt) v = (float)Math.Round(v);
                if (v < min || v > max)
                {
                    msg = Lang.TF("vset.range", "{0} は {1}〜{2} の範囲で指定してください。", "{0} must be between {1} and {2}.", s.Name, Fmt(min), Fmt(max));
                    if (!Options.ExtendedRanges)
                        msg += Lang.T("vset.hint", "（範囲拡張はオフです: /opt vanilla.ranges on）", " (extended ranges are off: /opt vanilla.ranges on)");
                    return false;
                }

                var gom = GameOptionsManager.Instance;
                var o = gom != null ? gom.CurrentGameOptions : null;
                if (o == null)
                {
                    msg = Lang.T("vset.noopts", "設定がまだ読み込まれていません。", "Game options are not loaded yet.");
                    return false;
                }
                int oldInt = 0; float oldFloat = 0f;
                try { if (s.IsInt) oldInt = o.GetInt(s.Int); else oldFloat = o.GetFloat(s.Float); } catch (Exception) { }
                if (s.IsInt) o.SetInt(s.Int, (int)v); else o.SetFloat(s.Float, v);
                // The matchmaker refuses options the vanilla validation rejects (DisconnectReasons.InvalidGameOptions) and
                // that value would be saved for every later lobby: revert instead of persisting it.
                try
                {
                    if (o.AreInvalid(MaxExpectedPlayers))
                    {
                        if (s.IsInt) o.SetInt(s.Int, oldInt); else o.SetFloat(s.Float, oldFloat);
                        msg = Lang.TF("vset.invalid", "{0} = {1} はバニラの検証で無効になるため設定できません。", "{0} = {1} fails the vanilla option validation and was not applied.", s.Name, Fmt(v));
                        return false;
                    }
                }
                catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"VanillaRanges: AreInvalid check: {e.Message}"); }
                try { o.SetInt(Int32OptionNames.RulePreset, (int)RulesPresets.Custom); } catch (Exception) { }
                try { gom.GameHostOptions = o; } catch (Exception) { }
                try { gom.SaveNormalHostOptions(); } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"VanillaRanges: save: {e.Message}"); }

                // same broadcast the settings screen triggers (GameOptionsMenu.ValueChanged)
                var gm = GameManager.Instance;
                if (gm != null && gm.LogicOptions != null) gm.LogicOptions.SyncOptions();
                else PocketRolesPlugin.Logger.LogWarning("VanillaRanges: no LogicOptions to sync");

                RefreshOpenMenu(s, v);
                string shown = Fmt(v) + (s.Seconds ? Lang.T("vset.unit.sec", "秒", "s") : Lang.T("vset.unit.x", "倍", "x"));
                try
                {
                    var hud = HudManager.Instance;
                    if (hud != null && hud.Notifier != null) hud.Notifier.AddSettingsChangeMessage(s.Title, shown, true);
                }
                catch (Exception) { }
                PocketRolesPlugin.Logger.LogInfo($"VanillaRanges: /vset {s.Key} = {Fmt(v)}");
                msg = Lang.TF("vset.ok", "{0} を {1} に設定しました。", "{0} set to {1}.", s.Name, shown);
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"VanillaRanges.TrySet: {e}");
                msg = Lang.T("vset.error", "設定に失敗しました（ログを確認してください）。", "Failed to apply the setting (see the log).");
                return false;
            }
        }

        /// <summary>If the settings screen is open, move its row to the new value so the arrows continue from there.</summary>
        private static void RefreshOpenMenu(Spec s, float v)
        {
            try
            {
                var menu = GameSettingMenu.Instance;
                if (menu == null || menu.GameSettingsTab == null || menu.GameSettingsTab.Children == null) return;
                var children = menu.GameSettingsTab.Children;
                for (int i = 0; i < children.Count; i++)
                {
                    var n = children[i] != null ? children[i].TryCast<NumberOption>() : null;
                    if (n == null) continue;
                    if (Find(n.floatOptionName, n.intOptionName) != s) continue;
                    n.Value = v; // FixedUpdate repaints the text when oldValue != Value
                    Apply(n);
                }
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"VanillaRanges: menu refresh: {e.Message}"); }
        }

        private static bool TryParseNumber(string text, out float v)
        {
            v = 0f;
            if (string.IsNullOrEmpty(text)) return false;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text.Trim())
            {
                if (c >= '０' && c <= '９') sb.Append((char)('0' + (c - '０')));
                else if (c == '．' || c == '。') sb.Append('.');
                else if (c == '－' || c == 'ー') sb.Append('-');
                else if (c == 's' || c == 'x' || c == '秒' || c == '倍') continue;
                else sb.Append(c);
            }
            return float.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v);
        }

        private static string Fmt(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ startup log

        private static bool _logged;

        internal static void LogOnce()
        {
            if (_logged) return;
            _logged = true;
            try
            {
                if (!Options.ExtendedRanges) { PocketRolesPlugin.Logger.LogInfo("VanillaRanges: extended ranges off ([Vanilla] ExtendedRanges=false)"); return; }
                var sb = new StringBuilder("VanillaRanges: extended ranges on:");
                foreach (var s in Specs)
                {
                    Limits(s, out float min, out float max, out float step);
                    sb.Append($" {s.Key} {Fmt(min)}-{Fmt(max)} step {Fmt(step)} (vanilla {Fmt(s.VanMin)}-{Fmt(s.VanMax)});");
                }
                PocketRolesPlugin.Logger.LogInfo(sb.ToString());
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"VanillaRanges.LogOnce: {e}"); }
        }
    }

    // ====================================================================== patches

    /// <summary>Startup log of the widened ranges (once).</summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    internal static class VanillaRanges_MainMenuStartPatch
    {
        private static void Postfix()
        {
            try { VanillaRanges.LogOnce(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"VanillaRanges_MainMenuStartPatch: {e}"); }
        }
    }

    /// <summary>The vanilla row copies ValidRange / Increment from its FloatGameSetting / IntGameSetting here: widen ours.</summary>
    [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.SetUpFromData))]
    internal static class VanillaRanges_NumberSetUpPatch
    {
        private static void Postfix(NumberOption __instance)
        {
            try
            {
                if (!Core.Game.IsHostActive) return;
                VanillaRanges.Apply(__instance);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"VanillaRanges_NumberSetUpPatch: {e}"); }
        }
    }

    /// <summary>Initialize re-reads the value from the options each time the tab opens: re-apply so the buttons stay open.</summary>
    [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Initialize))]
    internal static class VanillaRanges_NumberInitializePatch
    {
        private static void Postfix(NumberOption __instance)
        {
            try
            {
                if (!Core.Game.IsHostActive) return;
                VanillaRanges.LogOnce();
                VanillaRanges.Apply(__instance);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"VanillaRanges_NumberInitializePatch: {e}"); }
        }
    }
}
