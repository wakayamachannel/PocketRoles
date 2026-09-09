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
        Jester, Opportunist, Terrorist, Jackal,
        // v0.4.1
        Lovers, Arsonist, Witch, Assassin,
        // v0.5.0 (values are never persisted; order = Roles.All order)
        MadMayor, MadStuntman, MadHawk, Worshipper, JackalFriends, EvilHawk, EvilNekomata, SerialKiller, Samurai
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
        public const string LoversColor = "#ff69b4";
        public const string ArsonistColor = "#ff6633";

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
                DescJa = "キルボタンでインポスターやジャッカル、放火魔を撃てます（設定でマッド系役職・ジャッカルフレンズも）。クルーを撃つと自分が死にます。ベントとサボタージュは使えず、タスクは偽物です。",
                DescEn = "You have a kill button to shoot Impostors, the Jackal and the Arsonist (Mad-type roles and Jackal Friends by option). Shooting anyone else kills you instead. No venting or sabotage; your tasks are fake.",
                NameZh = "警长", DescZh = "可用击杀键射杀内鬼、豺狼和纵火犯（按设置也可射杀狂粉系职业、豺狼之友）。误杀其他人会让自己死亡。不能跳管和破坏，任务是假的。",
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
            // ---- v0.5.0 Madmate family (crew pool, vanilla Crewmate on every client, see Roles.IsMadType).
            // Ordering matters: Roles.TryParse resolves a ≥2-char ja prefix / ≥1-char zh prefix to the FIRST row, so `マッド` and
            // `マッドメイ` stay Madmate (row above); the shortest unique prefixes are マッドメイヤ / マッドス / マッドホ / 崇拝 and 狂 / 疯 / 鹰 / 崇
            // (README alias sentence quotes them). DescEn of the Mad Mayor is ≤ 100 chars on purpose: /cmd r <role> is capped at 3 messages.
            new RoleInfo { Id = CustomRole.MadMayor, Key = "madmayor", NameJa = "マッドメイヤー", NameEn = "Mad Mayor", NameZh = "狂粉市长", Team = Team.Impostor, Color = ImpostorColor,
                TasksCount = false,
                DescJa = "インポスター陣営のクルーです。会議での投票が複数票として数えられます。インポスターの名前が赤く見えますがキルはできません。インポスターが勝つとあなたも勝ちです。タスクは偽物です。",
                DescEn = "A Madmate whose vote counts as several votes. You see Impostors in red but cannot kill; fake tasks.",
                DescZh = "内鬼阵营的船员，会议中的投票按多票计算。内鬼的名字显示为红色，但你不能击杀。内鬼获胜时你也获胜。任务是假的。",
                Aliases = new[] { "mmy", "madmy", "mmayor" } },
            new RoleInfo { Id = CustomRole.MadStuntman, Key = "madstuntman", NameJa = "マッドスタントマン", NameEn = "Mad Stuntman", NameZh = "疯狂特技演员", Team = Team.Impostor, Color = ImpostorColor,
                // The first [MadStuntman] Lives kill attempts on it fail (Kills.TryStuntmanGuard) — votes, the Assassin's guess and disconnects still kill it.
                TasksCount = false,
                DescJa = "インポスター陣営のクルーです。インポスターの名前が赤く見えます。キルはできませんが、キルされても設定回数までは死にません（追放は防げません）。インポスターが勝つとあなたも勝ちです。",
                DescEn = "A crewmate on the Impostor team. You see Impostors in red and cannot kill, but you survive the first few kill attempts (a vote still gets you). You win with the Impostors.",
                DescZh = "内鬼阵营的船员。内鬼的名字显示为红色，你不能击杀，但前几次被击杀不会死（投票放逐无法避免）。内鬼获胜时你也获胜。",
                Aliases = new[] { "stunt", "madstunt", "mst", "ms" } },
            new RoleInfo { Id = CustomRole.MadHawk, Key = "madhawk", NameJa = "マッドホーク", NameEn = "Mad Hawk", NameZh = "鹰眼狂粉", Team = Team.Impostor, Color = ImpostorColor,
                // Passive Lighter-style multiplier on a crewmate-basis client (SNR's active "hawk eye" needs a button a vanilla crewmate does not have).
                TasksCount = false,
                DescJa = "インポスター陣営のクルーです。視界が通常よりずっと広くなります。インポスターの名前が赤く見えます。キルはできません。インポスターが勝つとあなたも勝ちです。",
                DescEn = "A crewmate on the Impostor team with much wider vision. You see Impostors in red but cannot kill. You win with the Impostors.",
                DescZh = "内鬼阵营的船员，视野比普通船员大得多。内鬼的名字显示为红色，但你不能击杀。内鬼获胜时你也获胜。",
                Aliases = new[] { "mh", "mhawk" } },
            // Madmate with a button: its own client is an Impostor (kill button = worship), everyone else sees a Crewmate.
            new RoleInfo { Id = CustomRole.Worshipper, Key = "worshipper", NameJa = "崇拝者", NameEn = "Worshipper", NameZh = "崇拜者", Team = Team.Impostor, Color = ImpostorColor,
                IsKiller = true, ImpostorDesync = true, CanVent = false, CanSabotage = false, TasksCount = false,
                DescJa = "インポスター陣営のクルー。インポスターは分かりません。キルボタンで相手を崇拝しマッドメイトにします（回数制限）。インポスターを崇拝すると自爆、回数切れ後は失敗だけ。ベント・サボ不可、タスクは偽物。",
                DescEn = "A crewmate on the Impostor team; you do not know who the Impostors are. Your kill button worships: the target becomes a Madmate (limited uses). Worshipping an Impostor kills you; after the last use the button only fails. No venting or sabotage; your tasks are fake.",
                DescZh = "内鬼阵营的船员（不知道谁是内鬼）。击杀键变为“崇拜”，对方成为内鬼狂粉（次数有限）。崇拜内鬼会自爆，次数用完后只会失败。不能跳管和破坏，任务是假的。",
                Aliases = new[] { "ws", "worship" } },
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
            new RoleInfo { Id = CustomRole.Witch, Key = "witch", NameJa = "魔女", NameEn = "Witch", NameZh = "女巫", Team = Team.Impostor, Color = ImpostorColor,
                IsKiller = true, CanVent = true, CanSabotage = true, FromImpostorPool = true,
                DescJa = "インポスターです。キルは「呪い」になり、次の会議が終わった後に呪った相手が全員死にます。あなたが追放・死亡すると呪いは消えます。",
                DescEn = "An Impostor whose kill is a curse: everyone you cursed dies right after the next meeting. The curse fades if you are ejected or die.",
                DescZh = "内鬼。你的击杀变成“诅咒”，下次会议结束后所有被诅咒的人一起死亡。你被投出或死亡则诅咒失效。",
                Aliases = new[] { "wt", "wi" } },
            new RoleInfo { Id = CustomRole.Assassin, Key = "assassin", NameJa = "アサシン", NameEn = "Assassin", NameZh = "刺客", Team = Team.Impostor, Color = ImpostorColor,
                IsKiller = true, CanVent = true, CanSabotage = true, FromImpostorPool = true,
                DescJa = "インポスターです。会議中に /cmd guess 名前 役職 と打つと、正解なら相手が死に、外れるとあなたが死にます。",
                DescEn = "An Impostor. In a meeting type /cmd guess <name> <role>: a correct guess kills the target, a wrong one kills you.",
                DescZh = "内鬼。会议中输入 /cmd guess 名字 职业：猜对则对方死亡，猜错则你死亡。",
                Aliases = new[] { "as", "asn" } },
            // ---- v0.5.0 impostor-pool roles (real vanilla Impostor on the client; listed in Game.IsImpostorTeamKiller).
            // Evil Hawk stays FIRST: TryParse's prefix passes take the first row, so イビ / イビル / 邪 / 邪恶 resolve here; イビルホ / イビル猫 and
            // 邪恶鹰 / 邪恶猫 are the prefixes that are certain whatever the order (README alias sentence).
            new RoleInfo { Id = CustomRole.EvilHawk, Key = "evilhawk", NameJa = "イビルホーク", NameEn = "Evil Hawk", NameZh = "邪恶鹰眼", Team = Team.Impostor, Color = ImpostorColor,
                IsKiller = true, CanVent = true, CanSabotage = true, FromImpostorPool = true,
                DescJa = "インポスターです。視界が通常のインポスターよりずっと広くなります。キル・ベント・サボタージュは通常どおりです。",
                DescEn = "An Impostor who sees much further than a normal Impostor. Kills, vents and sabotages as usual.",
                DescZh = "内鬼。视野比普通内鬼大得多。击杀、跳管、破坏与普通内鬼相同。",
                Aliases = new[] { "eh" } },
            new RoleInfo { Id = CustomRole.EvilNekomata, Key = "evilnekomata", NameJa = "イビル猫又", NameEn = "Evil Nekomata", NameZh = "邪恶猫又", Team = Team.Impostor, Color = ImpostorColor,
                IsKiller = true, CanVent = true, CanSabotage = true, FromImpostorPool = true,
                DescJa = "インポスターです。通常どおりキルできます。会議で追放されると、あなたに投票した人の中からランダムで 1 人を道連れにします。",
                DescEn = "An Impostor who kills normally. When you are voted out, one random player who voted for you dies with you.",
                DescZh = "内鬼，可以正常击杀。当你在会议中被投出时，会从投票给你的人中随机拖一人一起死。",
                Aliases = new[] { "nekomata", "neko", "eneko" } },
            // After SuperNewRoles: vanilla Impostor with a short kill cooldown that dies by itself when it has not killed for [SerialKiller] SuicideTime.
            new RoleInfo { Id = CustomRole.SerialKiller, Key = "serialkiller", NameJa = "シリアルキラー", NameEn = "Serial Killer", NameZh = "连环杀手", Team = Team.Impostor, Color = ImpostorColor,
                IsKiller = true, CanVent = true, CanSabotage = true, FromImpostorPool = true,
                DescJa = "インポスターです。キルクールダウンが短い代わりに、前のキルから一定時間キルしないとその場で自滅します（キルするたびにリセット、会議中は停止）。ベント・サボタージュ可。",
                DescEn = "An Impostor with a very short kill cooldown. If you do not kill within the time limit after your last kill you die on the spot (the timer restarts with every kill and pauses during meetings). Can vent and sabotage.",
                DescZh = "内鬼。击杀冷却很短，但距上次击杀超过限定时间仍未击杀就会当场自灭（每次击杀后重置，会议中暂停）。可跳管、可破坏。",
                Aliases = new[] { "sk", "serial" } },
            new RoleInfo { Id = CustomRole.Samurai, Key = "samurai", NameJa = "侍", NameEn = "Samurai", NameZh = "武士", Team = Team.Impostor, Color = ImpostorColor,
                IsKiller = true, CanVent = true, CanSabotage = true, FromImpostorPool = true,
                DescJa = "インポスターです。キルが「斬撃」になり、キルした相手に続いて、その瞬間にあなたの周囲にいた人が次々に死にます。クールダウンは長めです。",
                DescEn = "An Impostor whose kill is a slash: your target dies, then everyone who was near you at that moment falls one after another. Long cooldown.",
                DescZh = "内鬼。你的击杀变成“斩击”：击杀目标之后，那一刻你周围的所有人也会接连死亡。冷却较长。",
                Aliases = new[] { "sam", "sm" } },
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
            // v0.5.0: the Jackal's Madmate — vanilla Crewmate on its client (no kill button), Team.Neutral so its tasks never count on the
            // host and it wins only through the Jackal (WinConditions.ComputeWinners). DescEn ≤ 100 chars (3-message /cmd r cap).
            new RoleInfo { Id = CustomRole.JackalFriends, Key = "jackalfriends", NameJa = "ジャッカルフレンズ", NameEn = "Jackal Friends", NameZh = "豺狼之友", Team = Team.Neutral, Color = JackalColor,
                TasksCount = false,
                DescJa = "ジャッカル陣営のクルーです。ジャッカルの名前が青く見えます。キルはできません。ジャッカルが勝つとあなたも勝ちです。タスクは偽物です。",
                DescEn = "Jackal-side crewmate: you see the Jackal in blue, cannot kill, and win with the Jackal. Fake tasks.",
                DescZh = "豺狼阵营的船员。豺狼的名字显示为蓝色，但你不能击杀。豺狼获胜时你也获胜。任务是假的。",
                Aliases = new[] { "jf", "friends", "friend" } },
            new RoleInfo { Id = CustomRole.Lovers, Key = "lovers", NameJa = "ラバーズ", NameEn = "Lovers", NameZh = "恋人", Team = Team.Neutral, Color = LoversColor,
                TasksCount = false,
                DescJa = "恋人です。相手の名前に♥が見えます。片方が死ぬともう片方も死にます。2人とも生きて試合が終わる（または残り3人になる）と2人だけの勝利です。タスクは偽物です。",
                DescEn = "Lovers. You see a ♥ on your partner. If one of you dies the other dies too. Both alive when the game ends (or as the last 3 players) = you two win. Your tasks are fake.",
                DescZh = "恋人。你能看到伴侣名字上的♥。一方死亡另一方也会死。游戏结束时（或只剩3人时）两人都存活即两人获胜。任务是假的。",
                Aliases = new[] { "lover", "lv", "love" } },
            new RoleInfo { Id = CustomRole.Arsonist, Key = "arsonist", NameJa = "放火魔", NameEn = "Arsonist", NameZh = "纵火犯", Team = Team.Neutral, Color = ArsonistColor,
                IsKiller = true, ImpostorDesync = true, CanVent = false, CanSabotage = false, TasksCount = false,
                DescJa = "第三陣営です。キルボタンは「油をかける」で相手は死にません。生きている他の全員に油をかけると即座に単独勝利します。サボタージュ不可、タスクは偽物です。",
                DescEn = "Neutral. Your kill button douses instead of killing. Douse every other living player to win alone at once. No sabotage; your tasks are fake.",
                DescZh = "中立阵营。击杀键变成“浇油”，不会杀人。给所有其他存活玩家浇油后立即单独获胜。不能破坏，任务是假的。",
                Aliases = new[] { "arso", "ars" } },
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
        /// The Madmate family (v0.5.0): Team.Impostor without a real kill — Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper
        /// (and every player the Worshipper converts, which becomes a literal Madmate). Drives exactly three rules: not crew for the
        /// count thresholds (WinConditions.EvaluateBase), [Sheriff] CanKillMadmate (Kills.CanSheriffKill), the impostor-side Ⓜ/Ⓦ marker
        /// (NameTags rule 3). The red-impostor-names rule (NameTags rule 2) is IsMadType minus the Worshipper. A converter must refuse
        /// a target for which this is true (Game.ConvertRole doc).
        /// </summary>
        public static bool IsMadType(CustomRole r) =>
            r == CustomRole.Madmate || r == CustomRole.MadMayor || r == CustomRole.MadStuntman || r == CustomRole.MadHawk || r == CustomRole.Worshipper;

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
