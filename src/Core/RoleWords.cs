using System;
using System.Collections.Generic;

namespace PocketRoles.Core
{
    /// <summary>
    /// v0.5.5: the vanilla role words players type, and the Japanese / Chinese prefix match of <see cref="Roles.TryParse"/>,
    /// without any game type (tools/LangTool compiles this file and tests it). The Madmate's Chinese name of the v0.5.5 drafts
    /// started with the official Impostor name (伪装者狂粉, still accepted as input), so a 1-character zh prefix turned "伪装者"
    /// into the Madmate: /set 伪装者 2 set two Madmates, /cmd r 伪装者 explained the Madmate. PR #1 renamed it 狂信徒.
    /// </summary>
    internal static class RoleWords
    {
        /// <summary>
        /// The vanilla roles in the official Simplified / Traditional Chinese, Japanese and English of the game
        /// (Addressables tables), plus the older words players still type. Never a mod role.
        /// </summary>
        public static readonly string[] Vanilla =
        {
            // zh-CN (official)
            "伪装者", "船员", "工程师", "科学家", "守护天使", "变形者", "大嗓门", "侦察员", "幻象师", "侦探", "毒蛇", "法官",
            // zh-TW (official; a Traditional Chinese game shows these)
            "偽裝者", "船員", "工程師", "科學家", "守護天使", "變形者", "警示者", "追蹤者", "魅影", "偵探",
            // ja (official)
            "インポスター", "クルー", "エンジニア", "科学者", "シェイプシフター", "ノイズメーカー", "トラッカー", "ファントム", "探偵", "バイパー", "ジャッジ",
            // en (compared without case and spaces)
            "impostor", "crewmate", "engineer", "scientist", "guardianangel", "shapeshifter", "noisemaker", "tracker", "phantom", "detective", "viper", "judge",
            // older words (the mod's texts before v0.5.5, the game's older Japanese), still typed
            "内鬼", "クルーメイト", "サイエンティスト", "ヴァイパー", "幻影", "追踪者", "噪音制造者", "审判官",   // terms-ok
            // other names players use (PR #1 translated the Phantom as 幻术师; the game says 幻象师)
            "幻术师",   // terms-ok
        };

        private static string Fold(string s) => (s ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");

        /// <summary>True when <paramref name="text"/> is a vanilla role word (whole word; Latin letters without case and spaces).</summary>
        public static bool IsVanilla(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Length == 0) return false;
            string f = Fold(t);
            foreach (var w in Vanilla)
                if (w == t || w == f) return true;
            return false;
        }

        /// <summary>
        /// True when <paramref name="modName"/> begins with a vanilla role word and <paramref name="text"/> does not reach
        /// past it: "伪" / "伪装" / "伪装者" against 伪装者狂粉 (the player means the Impostor), not "伪装者狂".
        /// </summary>
        private static bool InsideVanillaWord(string modName, string text)
        {
            foreach (var w in Vanilla)
                if (w.StartsWith(text, StringComparison.Ordinal) && modName.StartsWith(w, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// The partial match of <see cref="Roles.TryParse"/> (after its exact names and aliases): the first role whose
        /// Japanese name starts with <paramref name="text"/> (2 characters or more, e.g. "シェリ"), else the first whose
        /// Chinese name does (1 character or more, e.g. "警"). Returns the index into the two lists (the order of
        /// Roles.All), or -1. A vanilla role word never matches, nor does text that stays inside the vanilla word a mod
        /// role's name starts with (伪 / 伪装 → not 伪装者狂粉).
        /// </summary>
        public static int PrefixMatch(IList<string> namesJa, IList<string> namesZh, string text) => PrefixMatch(namesJa, namesZh, null, text);

        /// <summary>
        /// <see cref="PrefixMatch(IList{string}, IList{string}, string)"/>, then the former Chinese names of each role
        /// (<paramref name="formerZh"/>, same order; RoleInfo.FormerZh) when no current name matched: 疯 / 鹰 / 崇 / 豺狼之 keep
        /// naming the roles they named before PR #1's names. The vanilla-word rule is the same (伪 / 伪装 → not 伪装者狂粉).
        /// </summary>
        public static int PrefixMatch(IList<string> namesJa, IList<string> namesZh, IList<string[]> formerZh, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return -1;
            string t = text.Trim();
            if (IsVanilla(t)) return -1;
            if (t.Length >= 2 && namesJa != null)
            {
                for (int i = 0; i < namesJa.Count; i++)
                    if (!string.IsNullOrEmpty(namesJa[i]) && namesJa[i].StartsWith(t, StringComparison.Ordinal))
                        return InsideVanillaWord(namesJa[i], t) ? -1 : i;
            }
            if (namesZh != null)
            {
                for (int i = 0; i < namesZh.Count; i++)
                    if (!string.IsNullOrEmpty(namesZh[i]) && namesZh[i].StartsWith(t, StringComparison.Ordinal))
                        return InsideVanillaWord(namesZh[i], t) ? -1 : i;
            }
            if (formerZh != null)
            {
                for (int i = 0; i < formerZh.Count; i++)
                {
                    if (formerZh[i] == null) continue;
                    foreach (var n in formerZh[i])
                        if (!string.IsNullOrEmpty(n) && n.StartsWith(t, StringComparison.Ordinal))
                            return InsideVanillaWord(n, t) ? -1 : i;
                }
            }
            return -1;
        }
    }
}
