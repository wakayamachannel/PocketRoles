using System;
using System.Collections.Generic;

namespace PocketRoles.Core
{
    public enum Team { Crew, Impostor, Neutral }

    public enum CustomRole
    {
        None = 0,
        Sheriff, Mayor, Snitch, Lighter, SpeedBooster, Bait,
        Madmate, Vampire, Mafia,
        Jester, Opportunist, Terrorist, Jackal
    }

    public sealed class RoleInfo
    {
        public CustomRole Id;
        /// <summary>English lowercase identifier used in commands and the config file (e.g. "sheriff").</summary>
        public string Key;
        public string NameJa;
        public string NameEn;
        /// <summary>Simplified Chinese name.</summary>
        public string NameZh;
        public string DescJa;
        public string DescEn;
        /// <summary>Simplified Chinese description.</summary>
        public string DescZh;
        public Team Team;
        /// <summary>"#rrggbb"</summary>
        public string Color;
        /// <summary>Has a kill button.</summary>
        public bool IsKiller;
        /// <summary>Own view = Impostor (kill button), everyone else's view = Crewmate.</summary>
        public bool ImpostorDesync;
        public bool CanVent;
        public bool CanSabotage;
        /// <summary>Tasks count toward the crew task-win progress.</summary>
        public bool TasksCount;
        /// <summary>Assigned from the vanilla-Impostor pool (Vampire, Mafia) instead of the plain-Crewmate pool.</summary>
        public bool FromImpostorPool;
        public string[] Aliases = Array.Empty<string>();

        /// <summary>
        /// Kills of this role come from a player who is NOT a vanilla Impostor (Sheriff, Jackal): in the unregistered
        /// compat mode the server (no host authority) may reject them, so the role is only assigned with
        /// [Compat] AllowRiskyRoles (see <see cref="Roles.IsCompatBlocked"/>).
        /// </summary>
        public bool CompatRisky => IsKiller && !FromImpostorPool;

        /// <summary>Name in the current language (Lang.Current / Lang.Scope); the JSON table key "role.&lt;key&gt;.name" overrides.</summary>
        public string Name => Lang.T("role." + Key + ".name", NameJa, NameEn, NameZh);
        /// <summary>Description in the current language; the JSON table key "role.&lt;key&gt;.desc" overrides.</summary>
        public string Desc => Lang.T("role." + Key + ".desc", DescJa, DescEn, DescZh);
        public string Colored(string s) => "<color=" + Color + ">" + s + "</color>";
        public string ColoredName => Colored(Name);
    }

    public static class Roles
    {
        public const string ImpostorColor = "#ff1919";
        public const string CrewColor = "#8cffff";
        public const string JackalColor = "#00b4eb";
        public const string SnitchColor = "#b8fb4f";

        private static readonly RoleInfo NoneInfo = new RoleInfo
        {
            Id = CustomRole.None, Key = "none", NameJa = "なし", NameEn = "None", NameZh = "无", DescJa = "バニラの役職です。", DescEn = "Vanilla role.", DescZh = "原版职业。",
            Team = Team.Crew, Color = "#ffffff", TasksCount = true
        };

        public static readonly RoleInfo[] All =
        {
            new RoleInfo { Id = CustomRole.Sheriff, Key = "sheriff", NameJa = "シェリフ", NameEn = "Sheriff", Team = Team.Crew, Color = "#f8cd46",
                // A Sheriff holds the Impostor role on its own client and cannot use task consoles: its tasks are fake.
                IsKiller = true, ImpostorDesync = true, CanVent = false, CanSabotage = false, TasksCount = false,
                DescJa = "キルボタンでインポスターやジャッカルを撃てます。クルーを撃つと自分が死にます。ベントとサボタージュは使えず、タスクは偽物です。",
                DescEn = "You have a kill button to shoot Impostors and the Jackal. Shooting a crewmate kills you instead. No venting or sabotage; your tasks are fake.",
                NameZh = "警长", DescZh = "可用击杀键射杀内鬼和豺狼。误杀船员会让自己死亡。不能跳管和破坏，任务是假的。",
                Aliases = new[] { "sh" } },
            new RoleInfo { Id = CustomRole.Mayor, Key = "mayor", NameJa = "メイヤー", NameEn = "Mayor", Team = Team.Crew, Color = "#204d42",
                TasksCount = true,
                DescJa = "会議での投票が複数票として数えられます。",
                DescEn = "Your vote counts as several votes in meetings.",
                NameZh = "市长", DescZh = "你在会议中的投票按多票计算。",
                Aliases = new[] { "my" } },
            new RoleInfo { Id = CustomRole.Snitch, Key = "snitch", NameJa = "スニッチ", NameEn = "Snitch", Team = Team.Crew, Color = SnitchColor,
                TasksCount = true,
                DescJa = "タスクを全て終えるとインポスターとジャッカルの名前が赤く見えます。残りタスクが少なくなるとキラー側にはあなたが誰か分かります。",
                DescEn = "Finish all tasks to see Impostors and the Jackal in red. When you are almost done, killers can see who you are.",
                NameZh = "告密者", DescZh = "完成全部任务后，内鬼和豺狼的名字会显示为红色。任务快完成时，杀手会知道你是谁。",
                Aliases = new[] { "sn" } },
            new RoleInfo { Id = CustomRole.Lighter, Key = "lighter", NameJa = "ライター", NameEn = "Lighter", Team = Team.Crew, Color = "#eee5be",
                TasksCount = true,
                DescJa = "視界が通常より広くなります。",
                DescEn = "Your vision is wider than normal.",
                NameZh = "点灯人", DescZh = "你的视野比普通船员更大。",
                Aliases = new[] { "lt" } },
            new RoleInfo { Id = CustomRole.SpeedBooster, Key = "speedbooster", NameJa = "スピードブースター", NameEn = "Speed Booster", Team = Team.Crew, Color = "#00ffff",
                TasksCount = true,
                DescJa = "移動速度が通常より速くなります。",
                DescEn = "You move faster than normal.",
                NameZh = "增速者", DescZh = "你的移动速度比普通船员更快。",
                Aliases = new[] { "sb", "speed", "booster" } },
            new RoleInfo { Id = CustomRole.Bait, Key = "bait", NameJa = "ベイト", NameEn = "Bait", Team = Team.Crew, Color = "#00f7ff",
                TasksCount = true,
                DescJa = "あなたをキルした人は強制的に死体を通報します。",
                DescEn = "Whoever kills you is forced to report your body immediately.",
                NameZh = "诱饵", DescZh = "击杀你的人会被强制立即报告尸体。",
                Aliases = new[] { "bt" } },
            new RoleInfo { Id = CustomRole.Madmate, Key = "madmate", NameJa = "マッドメイト", NameEn = "Madmate", Team = Team.Impostor, Color = ImpostorColor,
                TasksCount = false,
                DescJa = "インポスター陣営のクルーです。インポスターの名前が赤く見えます。キルはできません。インポスターが勝つとあなたも勝ちです。",
                DescEn = "A crewmate on the Impostor team. You see Impostors in red but cannot kill. You win with the Impostors.",
                NameZh = "内鬼狂粉", DescZh = "内鬼阵营的船员。内鬼的名字显示为红色，但你不能击杀。内鬼获胜时你也获胜。",
                Aliases = new[] { "mad", "mm" } },
            new RoleInfo { Id = CustomRole.Vampire, Key = "vampire", NameJa = "ヴァンパイア", NameEn = "Vampire", Team = Team.Impostor, Color = ImpostorColor,
                IsKiller = true, CanVent = true, CanSabotage = true, FromImpostorPool = true,
                DescJa = "インポスターです。キルは「噛みつき」になり、相手は数秒後に死にます。",
                DescEn = "An Impostor whose kill is a bite: the victim dies a few seconds later.",
                NameZh = "吸血鬼", DescZh = "内鬼。你的击杀变成“咬人”，目标在几秒后死亡。",
                Aliases = new[] { "vamp", "vp" } },
            new RoleInfo { Id = CustomRole.Mafia, Key = "mafia", NameJa = "マフィア", NameEn = "Mafia", Team = Team.Impostor, Color = ImpostorColor,
                IsKiller = true, CanVent = true, CanSabotage = true, FromImpostorPool = true,
                DescJa = "インポスターです。他のインポスターが全員死ぬまでキルできません。",
                DescEn = "An Impostor who can only kill after all other Impostors are dead.",
                NameZh = "黑手党", DescZh = "内鬼。在其他内鬼全部死亡之前不能击杀。",
                Aliases = new[] { "mf" } },
            new RoleInfo { Id = CustomRole.Jester, Key = "jester", NameJa = "ジェスター", NameEn = "Jester", Team = Team.Neutral, Color = "#ec62a5",
                TasksCount = false,
                DescJa = "第三陣営です。会議で追放されると単独勝利します。タスクは偽物です。",
                DescEn = "Neutral. You win alone if you get voted out. Your tasks are fake.",
                NameZh = "小丑", DescZh = "中立阵营。在会议中被投出即单独获胜。任务是假的。",
                Aliases = new[] { "js" } },
            new RoleInfo { Id = CustomRole.Opportunist, Key = "opportunist", NameJa = "オポチュニスト", NameEn = "Opportunist", Team = Team.Neutral, Color = "#00ff00",
                TasksCount = false,
                DescJa = "第三陣営です。試合終了時に生きていれば勝者に加わります。タスクは偽物です。",
                DescEn = "Neutral. If you are alive when the game ends, you win too. Your tasks are fake.",
                NameZh = "投机者", DescZh = "中立阵营。游戏结束时仍存活即可与胜者一同获胜。任务是假的。",
                Aliases = new[] { "opp", "op" } },
            new RoleInfo { Id = CustomRole.Terrorist, Key = "terrorist", NameJa = "テロリスト", NameEn = "Terrorist", Team = Team.Neutral, Color = "#00ff00",
                TasksCount = false,
                DescJa = "第三陣営です。タスクを全て終えた後にキルまたは追放されると単独勝利します。",
                DescEn = "Neutral. Finish all your tasks, then get killed or ejected to win alone.",
                NameZh = "恐怖分子", DescZh = "中立阵营。完成全部任务后被击杀或投出即单独获胜。",
                Aliases = new[] { "terror", "tr" } },
            new RoleInfo { Id = CustomRole.Jackal, Key = "jackal", NameJa = "ジャッカル", NameEn = "Jackal", Team = Team.Neutral, Color = JackalColor,
                IsKiller = true, ImpostorDesync = true, CanVent = true, CanSabotage = false, TasksCount = false,
                DescJa = "第三陣営のキラーです。誰でもキルできます。インポスターを全滅させ、残りのクルーの数があなた以下になると勝利します。",
                DescEn = "A neutral killer. You can kill anyone. Win by eliminating the Impostors and outnumbering the remaining crew.",
                NameZh = "豺狼", DescZh = "中立杀手。可以击杀任何人。消灭全部内鬼，且剩余船员人数不超过你时获胜。",
                Aliases = new[] { "jk" } },
        };

        private static readonly Dictionary<CustomRole, RoleInfo> ById = Build();

        private static Dictionary<CustomRole, RoleInfo> Build()
        {
            var d = new Dictionary<CustomRole, RoleInfo>();
            foreach (var r in All) d[r.Id] = r;
            return d;
        }

        /// <summary>Never returns null; None → a placeholder info (Team.Crew, white).</summary>
        public static RoleInfo Info(CustomRole r)
        {
            return ById.TryGetValue(r, out var info) ? info : NoneInfo;
        }

        public static bool TryParse(string text, out CustomRole role)
        {
            role = CustomRole.None;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            string lower = t.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
            foreach (var r in All)
            {
                if (r.Key == lower || r.NameEn.ToLowerInvariant().Replace(" ", "") == lower || r.NameJa == t || r.NameZh == t || r.Id.ToString().ToLowerInvariant() == lower)
                {
                    role = r.Id;
                    return true;
                }
                foreach (var a in r.Aliases)
                {
                    if (a == lower) { role = r.Id; return true; }
                }
            }
            // Japanese / Chinese partial match (e.g. "シェリ", "警")
            if (t.Length >= 2)
            {
                foreach (var r in All)
                {
                    if (r.NameJa.StartsWith(t, StringComparison.Ordinal)) { role = r.Id; return true; }
                }
            }
            if (t.Length >= 1)
            {
                foreach (var r in All)
                {
                    if (!string.IsNullOrEmpty(r.NameZh) && r.NameZh.StartsWith(t, StringComparison.Ordinal)) { role = r.Id; return true; }
                }
            }
            return false;
        }

        public static string ColoredName(CustomRole r) => Info(r).ColoredName;

        /// <summary>
        /// True when <paramref name="r"/> must not be assigned right now: the lobby runs in the unregistered compat
        /// mode (<see cref="Net.Registration.CompatMode"/>), the role's kills come from a non-impostor
        /// (<see cref="RoleInfo.CompatRisky"/>) and [Compat] AllowRiskyRoles is off.
        /// </summary>
        public static bool IsCompatBlocked(CustomRole r)
        {
            if (r == CustomRole.None) return false;
            if (!Info(r).CompatRisky) return false;
            if (Options.AllowRiskyRoles) return false;
            try { return Net.Registration.CompatMode; }
            catch (Exception) { return false; }
        }

        public static string TeamName(Team t)
        {
            switch (t)
            {
                case Team.Impostor: return Lang.T("team.impostor", "インポスター陣営", "Impostor team", "内鬼阵营");
                case Team.Neutral: return Lang.T("team.neutral", "第三陣営", "Neutral", "中立阵营");
                default: return Lang.T("team.crew", "クルー陣営", "Crew team", "船员阵营");
            }
        }

        public static string TeamColor(Team t)
        {
            switch (t)
            {
                case Team.Impostor: return ImpostorColor;
                case Team.Neutral: return "#00ff00";
                default: return CrewColor;
            }
        }
    }
}
