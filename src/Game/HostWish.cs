using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using PocketRoles.Core;
using PocketRoles.Net;

namespace PocketRoles.Game
{
    /// <summary>
    /// "Next game I am …" (v0.5.1, 2026-09-14 request): the host picks Impostor / Crewmate / one vanilla role for the next
    /// game — without test mode, in registered AND unregistered lobbies — from the ホスト page button (/next in chat).
    /// Works inside vanilla's own SelectRoles: the RpcSetRole prefix (RoleAssignment.Assign_RpcSetRolePatch) hands every
    /// role vanilla is about to send to <see cref="Intercept"/>, which swaps recipients so that the wished role reaches
    /// the host and the host's own role goes to the player it was taken from (the "partner"). Only the broadcast
    /// RpcSetRole messages vanilla sends anyway are involved, so an unregistered lobby stays legal, every client still
    /// receives exactly one SetRole per player (2026.8.18 clients apply only the first) and the impostor count is
    /// unchanged. A vanilla-role wish bumps that role's rate to at least 1 / 100 % for this selection. Consumed by the
    /// game it applied to; cleared when the host leaves the lobby.
    /// </summary>
    public static class HostWish
    {
        public enum Kind { None, Impostor, Crewmate, Vanilla }

        public static Kind Wish = Kind.None;
        /// <summary>Kind.Vanilla: the exact vanilla role.</summary>
        public static RoleTypes VanillaRole = RoleTypes.Crewmate;

        /// <summary>Our own RpcSetRole calls re-enter the prefix: bookkeeping only, no watching / swapping.</summary>
        internal static bool Redirecting;

        // per-selection state
        private static bool _active, _satisfied, _hasPending, _pendingOverride;
        private static byte _me = 255, _partner = 255;
        /// <summary>v0.5.2: who really received my held role, for the host-local notice only (_partner keeps its interception meaning).</summary>
        private static byte _shownPartner = 255;
        /// <summary>v0.5.2: my crew special was dropped so that FillImpostors promotes me (vanilla issued too few impostor roles).</summary>
        private static bool _fillPending;
        private static RoleTypes _pendingRole;
        private static bool _rateSaved;
        private static int _savedCount, _savedChance;

        private static readonly RoleTypes[] VanillaChoices =
        {
            RoleTypes.Shapeshifter, RoleTypes.Phantom, RoleTypes.Viper,
            RoleTypes.Scientist, RoleTypes.Engineer, RoleTypes.Tracker, RoleTypes.Noisemaker, RoleTypes.Detective, RoleTypes.Judge,
        };
        private static readonly Dictionary<string, RoleTypes> JaNames = new Dictionary<string, RoleTypes>
        {
            { "シフター", RoleTypes.Shapeshifter }, { "シェイプシフター", RoleTypes.Shapeshifter }, { "変身", RoleTypes.Shapeshifter },
            { "ファントム", RoleTypes.Phantom }, { "ヴァイパー", RoleTypes.Viper }, { "バイパー", RoleTypes.Viper },   // terms-ok: older names still accepted
            { "サイエンティスト", RoleTypes.Scientist }, { "科学者", RoleTypes.Scientist }, { "エンジニア", RoleTypes.Engineer },   // terms-ok
            { "トラッカー", RoleTypes.Tracker }, { "ノイズメーカー", RoleTypes.Noisemaker }, { "探偵", RoleTypes.Detective }, { "ジャッジ", RoleTypes.Judge },
            { "变形者", RoleTypes.Shapeshifter }, { "幻象师", RoleTypes.Phantom }, { "毒蛇", RoleTypes.Viper }, { "科学家", RoleTypes.Scientist },
            { "工程师", RoleTypes.Engineer }, { "侦察员", RoleTypes.Tracker }, { "大嗓门", RoleTypes.Noisemaker }, { "侦探", RoleTypes.Detective }, { "法官", RoleTypes.Judge },
            // v0.5.5: the texts use the official Simplified Chinese names now; the older names keep working. terms-ok
            { "幻影", RoleTypes.Phantom }, { "追踪者", RoleTypes.Tracker }, { "噪音制造者", RoleTypes.Noisemaker }, { "审判官", RoleTypes.Judge },   // terms-ok
            // ... and the official Traditional Chinese names (a TChinese game shows them). terms-ok
            { "變形者", RoleTypes.Shapeshifter }, { "魅影", RoleTypes.Phantom }, { "科學家", RoleTypes.Scientist }, { "工程師", RoleTypes.Engineer },   // terms-ok
            { "追蹤者", RoleTypes.Tracker }, { "警示者", RoleTypes.Noisemaker }, { "偵探", RoleTypes.Detective },   // terms-ok
            { "幻术师", RoleTypes.Phantom },   // terms-ok: PR #1's name for the Phantom (the game says 幻象师)
        };

        public static bool IsSet => Wish != Kind.None;

        /// <summary>The Game Master never plays. GameMasterActive is only set inside SelectRoles (after OnBegin), so the option decides in the lobby.</summary>
        private static bool GameMasterMode => Core.Game.GameMasterActive || Options.GameMaster;

        // ------------------------------------------------------------------ text

        /// <summary>The wished role as the host's game shows it ("インポスター", "クルー", the vanilla role's own name).</summary>
        public static string RoleText()
        {
            switch (Wish)
            {
                case Kind.Impostor: return Lang.T("me.name.impostor", "インポスター", "Impostor", "伪装者");
                case Kind.Crewmate: return Lang.T("me.name.crew", "クルー", "Crewmate", "船员");
                case Kind.Vanilla: return VanillaName(VanillaRole);
                default: return Lang.T("me.name.auto", "おまかせ", "random", "随机");
            }
        }

        private static string VanillaName(RoleTypes r)
        {
            try
            {
                if (RoleManager.InstanceExists)
                {
                    var rb = RoleManager.Instance.GetRole(r);
                    if (rb != null && !string.IsNullOrEmpty(rb.NiceName)) return rb.NiceName;
                }
            }
            catch (Exception) { }
            return r.ToString();
        }

        /// <summary>Short state line for the top-left ping display and the ホスト page button.</summary>
        public static string Tag() => IsSet ? Lang.TF("me.tag", "次:自分={0}", "next: me={0}", RoleText()) : "";

        public static string ButtonLabel() => Lang.T("ui.host.me", "次の自分", "Next game, me", "下局的我") + ": " + RoleText();

        public static string Describe()
        {
            return IsSet
                ? Lang.TF("me.state", "次の試合の自分: {0}（1 試合だけ。解除は /next auto）", "Next game, me: {0} (one game; /next auto clears)", RoleText())
                : Lang.T("me.state.none", "次の試合の自分: おまかせ（/next impostor | crew | <本体の役職名> で指定）", "Next game, me: random (/next impostor | crew | <vanilla role> sets it)");
        }

        // ------------------------------------------------------------------ setting

        /// <summary>Parses "/me &lt;arg&gt;" (impostor / crew / auto / a vanilla role name in en, ja, zh or the game's own name).</summary>
        public static bool TryParse(string text, out Kind kind, out RoleTypes vanilla)
        {
            kind = Kind.None; vanilla = RoleTypes.Crewmate;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim();
            string l = t.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
            switch (l)
            {
                case "impostor": case "imp": case "impostors": case "インポ": case "インポスター": case "伪装者": case "偽裝者": case "内鬼": case "狼":   // terms-ok: players still type 内鬼; 偽裝者 = zh-TW
                    kind = Kind.Impostor; return true;
                case "crew": case "crewmate": case "crewmates": case "クルー": case "クルーメイト": case "船员": case "船員": case "村":   // terms-ok: older name, zh-TW still accepted
                    kind = Kind.Crewmate; return true;
                case "auto": case "none": case "off": case "clear": case "random": case "おまかせ": case "解除": case "なし": case "随机": case "取消":
                    kind = Kind.None; return true;
            }
            foreach (var r in VanillaChoices)
            {
                if (r.ToString().ToLowerInvariant() == l) { kind = Kind.Vanilla; vanilla = r; return true; }
                string nice = null;
                try { nice = VanillaName(r); } catch (Exception) { }
                if (!string.IsNullOrEmpty(nice) && nice.Replace(" ", "").ToLowerInvariant() == l) { kind = Kind.Vanilla; vanilla = r; return true; }
            }
            if (JaNames.TryGetValue(t, out var jr)) { kind = Kind.Vanilla; vanilla = jr; return true; }
            return false;
        }

        /// <summary>Sets the wish; the message explains what will happen (host only, any lobby mode).</summary>
        public static bool Set(Kind kind, RoleTypes vanilla, out string message)
        {
            message = "";
            if (!Core.Game.IsHostActive)
            {
                message = Lang.T("cmd.modoff", "MODは現在オフです（バニラの試合）。", "The mod is currently off (vanilla game).");
                return false;
            }
            if (GameMasterMode && kind != Kind.None)
            {
                message = Lang.T("me.gm", "ゲームマスターモード中は自分に役を付けられません。", "The Game Master does not play: no role wish while GM mode is on.");
                return false;
            }
            if (kind == Kind.Vanilla && !Registration.CompatMode && !Options.VanillaRolesEnabled && !RoleAssignment.IsImpostorRole(vanilla))
            {
                message = Lang.T("me.novanilla",
                    "登録ありで本体の役職がオフ（[Roles] VanillaRoles = off）の部屋では、本体のクルー役職は指定できません（impostor / crew は可）。",
                    "In a registered lobby with vanilla roles off ([Roles] VanillaRoles = off) a vanilla crew role cannot be wished (impostor / crew still work).");
                return false;
            }
            Wish = kind;
            VanillaRole = vanilla;
            if (kind == Kind.None)
            {
                message = Lang.T("me.cleared", "次の試合の自分: おまかせに戻しました。", "Next game, me: back to random.");
                PocketRolesPlugin.Logger.LogInfo("HostWish: cleared");
                return true;
            }
            message = Lang.TF("me.set", "次の試合の自分: {0}（次の 1 試合だけ。解除は /next auto）", "Next game, me: {0} (one game only; /next auto clears)", RoleText());
            if (kind == Kind.Vanilla && !Registration.CompatMode && !Options.VanillaRolesEnabled)
                message += Lang.T("me.set.plainimp", " この部屋では素のインポスターになります。", " In this lobby it becomes a plain Impostor.");
            if (Core.Game.InProgress) message += Lang.T("cmd.set.next", " — 次の試合から適用", " - applies from the next game");
            PocketRolesPlugin.Logger.LogInfo($"HostWish: set {kind} {(kind == Kind.Vanilla ? vanilla.ToString() : "")}");
            return true;
        }

        /// <summary>ホスト page button: おまかせ → インポスター → クルー → おまかせ.</summary>
        public static string Cycle()
        {
            Kind next = Wish == Kind.None ? Kind.Impostor : Wish == Kind.Impostor ? Kind.Crewmate : Kind.None;
            Set(next, RoleTypes.Crewmate, out var msg);
            return msg;
        }

        public static void Clear()
        {
            if (Wish != Kind.None) PocketRolesPlugin.Logger.LogInfo("HostWish: cleared (left the lobby)");
            Wish = Kind.None;
        }

        // ------------------------------------------------------------------ inside vanilla's SelectRoles

        private static bool Satisfies(RoleTypes r)
        {
            switch (Wish)
            {
                case Kind.Impostor: return RoleAssignment.IsImpostorRole(r);
                case Kind.Crewmate: return !RoleAssignment.IsImpostorRole(r);
                case Kind.Vanilla: return r == VanillaRole;
                default: return true;
            }
        }

        /// <summary>RoleAssignment.BeginVanillaSelection (not for haison games): arm the swap and bump a wished vanilla role's rate.</summary>
        internal static void OnBegin()
        {
            _active = false; _satisfied = false; _hasPending = false; _rateSaved = false; _partner = 255; _shownPartner = 255; _me = 255; _fillPending = false;
            try
            {
                if (Wish == Kind.None) return;
                var lp = PlayerControl.LocalPlayer;
                if (lp == null || lp.Data == null) return;
                if (GameMasterMode) { PocketRolesPlugin.Logger.LogInfo("HostWish: ignored (Game Master mode)"); Wish = Kind.None; return; }
                if (Wish == Kind.Vanilla && !Registration.CompatMode && !Options.VanillaRolesEnabled && !RoleAssignment.IsImpostorRole(VanillaRole))
                {
                    // [Roles] VanillaRoles was switched off after the wish was set: the crew special cannot exist in this game
                    PocketRolesPlugin.Logger.LogInfo($"HostWish: {VanillaRole} wish dropped — [Roles] VanillaRoles is off in a registered lobby");
                    Chat.Chat.Local(Chat.Chat.Title, Lang.T("me.novanilla",
                        "登録ありで本体の役職がオフ（[Roles] VanillaRoles = off）の部屋では、本体のクルー役職は指定できません（impostor / crew は可）。",
                        "In a registered lobby with vanilla roles off ([Roles] VanillaRoles = off) a vanilla crew role cannot be wished (impostor / crew still work)."));
                    Wish = Kind.None;
                    return;
                }
                _me = lp.PlayerId;
                _active = true;
                if (Wish == Kind.Vanilla)
                {
                    var ro = GameOptionsManager.Instance?.CurrentGameOptions?.RoleOptions;
                    if (ro != null)
                    {
                        int n = ro.GetNumPerGame(VanillaRole), c = ro.GetChancePerGame(VanillaRole);
                        if (n < 1 || c < 100)
                        {
                            _savedCount = n; _savedChance = c; _rateSaved = true;
                            ro.SetRoleRate(VanillaRole, Math.Max(1, n), 100);
                            PocketRolesPlugin.Logger.LogInfo($"HostWish: {VanillaRole} rate {n}/{c}% -> {Math.Max(1, n)}/100% for this selection");
                        }
                    }
                }
                PocketRolesPlugin.Logger.LogInfo($"HostWish: armed — #{_me} wants {Wish}{(Wish == Kind.Vanilla ? " " + VanillaRole : "")}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"HostWish.OnBegin: {e}");
                _active = false;
            }
        }

        /// <summary>
        /// Vanilla's own RpcSetRole during SelectRoles (after RoleAssignment.TakeImpostorDefault). Returns true when the
        /// call was handled here — the prefix then returns false (the original send is replaced by ours).
        /// </summary>
        internal static bool Intercept(PlayerControl pc, RoleTypes role, bool canOverride)
        {
            if (!_active || Redirecting || Core.Game.GameMasterActive || pc == null || pc.Data == null) return false;
            try
            {
                byte p = pc.PlayerId;
                if (p == _me)
                {
                    if (_satisfied)
                    {
                        // a second role for me (the crew pass, after the swap): the partner takes it when it still has none
                        var x = Find(_partner);
                        if (x != null && !x.roleAssigned) { Send(x, role, canOverride); PocketRolesPlugin.Logger.LogInfo($"HostWish: my later {role} -> partner #{_partner} {Core.Game.NameOf(_partner)}"); }
                        else PocketRolesPlugin.Logger.LogInfo($"HostWish: my later {role} dropped (partner already has a role)");
                        return true;
                    }
                    if (Satisfies(role))
                    {
                        _satisfied = true; _hasPending = false;
                        PocketRolesPlugin.Logger.LogInfo($"HostWish: vanilla gave me {role} itself");
                        return false;
                    }
                    if (Wish == Kind.Vanilla && RoleAssignment.IsImpostorRole(role) == RoleAssignment.IsImpostorRole(VanillaRole))
                    {
                        // same team as the wished vanilla role (vanilla made me the Phantom, I want the Shapeshifter): take it directly
                        _satisfied = true;
                        Send(pc, VanillaRole, canOverride);
                        PocketRolesPlugin.Logger.LogInfo($"HostWish: my {role} -> {VanillaRole} (same team)");
                        return true;
                    }
                    // hold my role: Impostor / vanilla-role wish — until the wished one shows up for somebody else;
                    // Crewmate wish — OnEnd hands it to a player vanilla left without any role (never one of its pending impostor picks)
                    _hasPending = true; _pendingRole = role; _pendingOverride = canOverride;
                    PocketRolesPlugin.Logger.LogInfo($"HostWish: holding my {role}");
                    return true;
                }
                if (p == _partner)
                {
                    // the partner's later role (crew pass) is mine when I still have none
                    var me = Find(_me);
                    if (me != null && !me.roleAssigned && !_satisfied && Satisfies(role))
                    {
                        _satisfied = true;
                        Send(me, role, canOverride);
                        PocketRolesPlugin.Logger.LogInfo($"HostWish: partner's later {role} -> me");
                    }
                    else PocketRolesPlugin.Logger.LogInfo($"HostWish: partner's later {role} dropped");
                    return true;
                }
                if (Wish == Kind.Crewmate && _hasPending && !_satisfied && !RoleAssignment.IsImpostorRole(role))
                {
                    // a crew role for somebody else while I hold an impostor role: swap — the crew role is mine, my impostor role is theirs
                    // (both are that player's / my first SetRole; role counts unchanged; the OnEnd fallback covers a game without crew specials)
                    var me = Find(_me);
                    if (me == null || me.roleAssigned) return false;
                    // v0.5.2 (Designate): a player Designate already served holds its first SetRole — its crew role is Designate's to route
                    // (a second SetRole is ignored by the clients and my held impostor role would be lost); a designated impostor takes my
                    // held impostor role first; a crew designee never gets it
                    if (pc.roleAssigned) return false;
                    var taker = Designate.Taker(p) ?? pc;
                    bool takerIsSender = taker.PlayerId == pc.PlayerId;   // (Il2Cpp wrappers are not reference-equal: compare ids)
                    if (takerIsSender && Designate.IsCrewDesignee(p)) return false;
                    _partner = p;
                    _shownPartner = taker.PlayerId;
                    _satisfied = true;
                    RoleTypes held = _pendingRole; bool heldOverride = _pendingOverride; _hasPending = false;
                    Send(me, role, canOverride);
                    Send(taker, held, heldOverride);
                    RoleAssignment.RestoredRedirected(_me, taker.PlayerId);
                    Designate.MarkTaken(taker.PlayerId, takerIsSender ? (byte)255 : p);
                    PocketRolesPlugin.Logger.LogInfo($"HostWish: {role} meant for #{p} {Core.Game.NameOf(p)} -> me; my held {held} -> #{taker.PlayerId}{(takerIsSender ? "" : " (designated impostor)")}");
                    return true;
                }
                if (!_satisfied && Wish != Kind.Crewmate && Satisfies(role))
                {
                    var me = Find(_me);
                    if (me == null || me.roleAssigned) return false;
                    // v0.5.2 (Designate): where my held role goes — a held impostor role to a designated impostor first, never to a crew
                    // designee or to a player Designate already served (its first SetRole is out): Designate.Hold finds a free non-designee
                    // in the crew pass or at OnEnd. A held crew role only to a player without a role yet (else it is dropped, count untouched).
                    PlayerControl pendingTo = pc;
                    bool holdForDesignate = false;
                    if (_hasPending)
                    {
                        if (RoleAssignment.IsImpostorRole(_pendingRole))
                        {
                            pendingTo = Designate.Taker(p);
                            if (pendingTo == null)
                            {
                                if (pc.roleAssigned || Designate.IsCrewDesignee(p)) holdForDesignate = true;
                                else pendingTo = pc;
                            }
                        }
                        else if (pc.roleAssigned) pendingTo = Designate.PartnerOf(p);
                    }
                    _partner = p;
                    _shownPartner = holdForDesignate ? (byte)255 : (pendingTo != null ? pendingTo.PlayerId : p);
                    _satisfied = true;
                    bool gavePending = _hasPending;
                    Send(me, role, canOverride);
                    string heldNote = "";
                    if (_hasPending)
                    {
                        if (holdForDesignate) { Designate.Hold(_pendingRole, _pendingOverride, p); heldNote = "; my held impostor role -> Designate"; }
                        else if (pendingTo != null)
                        {
                            Send(pendingTo, _pendingRole, _pendingOverride);
                            Designate.MarkTaken(pendingTo.PlayerId, pendingTo.PlayerId == p ? (byte)255 : p);
                            heldNote = $"; my held {_pendingRole} -> #{pendingTo.PlayerId}";
                        }
                        else heldNote = $"; my held {_pendingRole} dropped (#{p} already has a role, nobody free to take it)";
                        _hasPending = false;
                    }
                    PocketRolesPlugin.Logger.LogInfo($"HostWish: {role} meant for #{p} {Core.Game.NameOf(p)} -> me{heldNote}");
                    return true;
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"HostWish.Intercept: {e}");
            }
            return false;
        }

        /// <summary>RoleAssignment.EndVanillaSelection: release a held role, restore the rate, tell the host, consume the wish.</summary>
        internal static void OnEnd()
        {
            if (!_active)
            {
                return;
            }
            try
            {
                if (_hasPending)
                {
                    var me = Find(_me);
                    PlayerControl taker = null;
                    if (Wish == Kind.Crewmate)
                    {
                        // vanilla is done: every player still without a role is a plain crewmate — one of them takes my impostor role
                        // (v0.5.2: a designated impostor first)
                        taker = Designate.Taker(255) ?? PickPartner();
                        if (taker != null)
                        {
                            _partner = taker.PlayerId;
                            _shownPartner = _partner;
                            _satisfied = true;
                            RoleAssignment.RestoredRedirected(_me, _partner);
                            Send(taker, _pendingRole, _pendingOverride);
                            Designate.MarkTaken(_partner, _me);
                            PocketRolesPlugin.Logger.LogInfo($"HostWish: my held {_pendingRole} -> #{_partner} {Core.Game.NameOf(_partner)} (I stay crew)");
                        }
                        else PocketRolesPlugin.Logger.LogWarning("HostWish: nobody can take my impostor role");
                    }
                    if (taker == null && me != null && !me.roleAssigned)
                    {
                        if (Wish == Kind.Impostor && !RoleAssignment.IsImpostorRole(_pendingRole)
                            && RoleAssignment.SelectImpostorsSeen < RoleAssignment.SelectTarget)
                        {
                            // v0.5.2: vanilla issued fewer impostor roles than the target (3 players: none) and handed me a crew special
                            // instead — stay roleless so FillImpostors (only players without a SetRole in an unregistered lobby) promotes
                            // me first; the crew special is dropped
                            _fillPending = true;
                            if (!Designate.FillPrefer.Contains(_me)) Designate.FillPrefer.Insert(0, _me);
                            PocketRolesPlugin.Logger.LogInfo($"HostWish: my held {_pendingRole} dropped — vanilla owes {RoleAssignment.SelectTarget - RoleAssignment.SelectImpostorsSeen} impostor role(s), the top-up promotes me");
                        }
                        else Send(me, _pendingRole, _pendingOverride);
                    }
                    _hasPending = false;
                }
                if (_rateSaved)
                {
                    try { GameOptionsManager.Instance?.CurrentGameOptions?.RoleOptions?.SetRoleRate(VanillaRole, _savedCount, _savedChance); } catch (Exception) { }
                    _rateSaved = false;
                }
                bool ok = _satisfied;
                if (!ok && Wish == Kind.Crewmate)
                {
                    var me = Find(_me);
                    ok = me != null && (me.Data.Role == null || !RoleAssignment.IsImpostorRole(me.Data.Role.Role));
                }
                string role = RoleText();
                if (_fillPending)
                {
                    Chat.Chat.Local(Chat.Chat.Title, string.Format(Lang.T("me.result.fill", "本体は自分を選ばなかったので、補充で {0} になります。", "Vanilla did not pick me; the top-up makes me {0}.", "原版没有选中我，由补充程序设为 {0}。"), role));
                }
                else if (ok)
                {
                    string swap = _shownPartner != 255 ? Lang.TF("me.result.swap", "（{0} と入れ替え）", " (swapped with {0})", Core.Game.NameOf(_shownPartner)) : "";
                    Chat.Chat.Local(Chat.Chat.Title, Lang.TF("me.result.ok", "今回の自分: {0}{1}", "This game, me: {0}{1}", role, swap));
                }
                else
                {
                    Chat.Chat.Local(Chat.Chat.Title, Lang.TF("me.result.no", "今回は本体が {0} を出さなかったので、おまかせになりました。", "Vanilla did not hand out {0} this time; you got a random role.", role));
                }
                PocketRolesPlugin.Logger.LogInfo($"HostWish: {(ok ? "fulfilled" : _fillPending ? "left to FillImpostors" : "NOT fulfilled")} ({Wish}{(Wish == Kind.Vanilla ? " " + VanillaRole : "")}, partner #{_partner})");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"HostWish.OnEnd: {e}");
            }
            finally
            {
                _active = false;
                Wish = Kind.None;   // consumed
            }
        }

        internal static void Send(PlayerControl to, RoleTypes role, bool canOverride)
        {
            Redirecting = true;
            try { to.RpcSetRole(role, canOverride); }
            finally { Redirecting = false; }
        }

        internal static PlayerControl Find(byte id)
        {
            if (id == 255) return null;
            foreach (var pc in Core.Game.AllPlayers())
                if (pc != null && pc.PlayerId == id) return pc;
            return null;
        }

        /// <summary>A random connected player other than me that has no role yet (and is not the Game Master).</summary>
        private static PlayerControl PickPartner()
        {
            var list = new List<PlayerControl>();
            foreach (var pc in Core.Game.AllPlayers())
            {
                if (pc == null || pc.Data == null || pc.Data.Disconnected) continue;
                if (pc.PlayerId == _me || pc.roleAssigned) continue;
                if (Core.Game.GameMasterActive && Core.Game.IsHost(pc.PlayerId)) continue;
                if (Designate.IsCrewDesignee(pc.PlayerId)) continue;   // v0.5.2
                list.Add(pc);
            }
            if (list.Count == 0) return null;
            return list[new System.Random().Next(list.Count)];
        }
    }
}
