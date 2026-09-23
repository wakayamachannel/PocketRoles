using System;
using System.Collections.Generic;
using System.Text;
using AmongUs.GameOptions;
using PocketRoles.Core;
using PocketRoles.Net;

namespace PocketRoles.Game
{
    /// <summary>
    /// "Next game, &lt;player&gt; is impostor / crew" (v0.5.2, 2026-09-15 request「いんぽを指定するのは？人に」): the host names
    /// OTHER players for the next game from the lobby chat (/next &lt;name|#id&gt; impostor | crew | auto) or the ホスト page
    /// button. Same mechanism as <see cref="HostWish"/> (the host's own wish, unchanged from v0.5.1): inside vanilla's
    /// SelectRoles the RpcSetRole prefix hands every role vanilla is about to send to <see cref="Intercept"/> — after
    /// HostWish.Intercept declined it — which re-addresses impostor roles to the designated players and impostor roles
    /// away from the players designated as crew. Only vanilla's own broadcast messages change recipients, every player
    /// still receives exactly one first SetRole (2026.8.18 clients apply only the first), and the impostor count vanilla
    /// decided is never changed, so an unregistered lobby stays legal. A designated impostor vanilla never picked is
    /// still first in line when RoleAssignment.FillImpostors tops the team up (3 players: vanilla issues no impostor).
    /// Consumed by the game it applied to; cleared when the host leaves the lobby. Every notice is host-local.
    /// </summary>
    public static class Designate
    {
        public enum Side { Impostor, Crewmate }

        internal sealed class Entry
        {
            public byte Id;
            public string Name;               // as designated: display and the identity fallback
            public string Puid, FriendCode;   // identity (a player id is reused by the next joiner in a public lobby)
            public Side Side;
            public int Order;                 // designation order = priority when there are more designees than slots
            // per selection
            public bool Satisfied;
            public byte Partner = 255;        // impostor designee: the player whose impostor role it took; crew designee: who got its impostor role
        }

        internal static readonly List<Entry> List = new List<Entry>();
        private static int _seq;

        // per selection
        private static bool _active, _hostHasWish, _hostImpWish;
        private struct Held { public RoleTypes Role; public bool Co; public byte From; }
        /// <summary>Impostor roles taken from crew designees, waiting for a holder (a designated impostor, a crew special's recipient, or anybody at OnEnd).</summary>
        private static readonly List<Held> _held = new List<Held>();
        /// <summary>Per-game snapshots read by RoleAssignment.FillImpostors, which runs after <see cref="OnEnd"/> consumed the list.</summary>
        internal static readonly List<byte> FillPrefer = new List<byte>();
        internal static readonly HashSet<byte> FillAvoid = new HashSet<byte>();

        public static bool IsSet => List.Count > 0;

        // ------------------------------------------------------------------ text

        private static string SideText(Side s)
        {
            return s == Side.Impostor
                ? Lang.T("me.name.impostor", "インポスター", "Impostor", "伪装者")
                : Lang.T("me.name.crew", "クルー", "Crewmate", "船员");
        }

        private static string Shown(Entry e)
        {
            string n = Lang.StripTags(e.Name ?? "").Trim();
            return n.Length == 0 ? "#" + e.Id : n;
        }

        private static Entry Of(byte id)
        {
            for (int i = 0; i < List.Count; i++) if (List[i].Id == id) return List[i];
            return null;
        }

        private static int CountImpostorEntries()
        {
            int n = 0;
            for (int i = 0; i < List.Count; i++) if (List[i].Side == Side.Impostor) n++;
            return n;
        }

        /// <summary>Impostor slots left for designees at the current player count (vanilla's clamp minus the host's own impostor wish); -1 unknown.</summary>
        private static int ImpostorSlots()
        {
            int connected = 0;
            foreach (var pc in Core.Game.AllPlayers())
                if (pc != null && pc.Data != null && !pc.Data.Disconnected) connected++;
            int slots = RoleAssignment.ImpostorSlots(connected);
            if (slots < 0) return -1;
            if (HostWishesImpostor()) slots--;
            return Math.Max(0, slots);
        }

        private static bool HostWishesImpostor()
        {
            return HostWish.Wish == HostWish.Kind.Impostor
                   || (HostWish.Wish == HostWish.Kind.Vanilla && RoleAssignment.IsImpostorRole(HostWish.VanillaRole));
        }

        /// <summary>Status line for /next (the host's own wish line comes from HostWish.Describe).</summary>
        public static string Describe()
        {
            if (List.Count == 0)
                return Lang.T("next.state.none",
                    "次の試合の指名: なし（/next <名前|#番号> impostor | crew で指定、/next reset で全部解除）",
                    "Next game, others: none (/next <name|#id> impostor | crew designates; /next reset clears all)");
            var sb = new StringBuilder();
            foreach (var e in List)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(Shown(e)).Append(" (#").Append(e.Id).Append(")=").Append(SideText(e.Side));
            }
            int slots = ImpostorSlots();
            return Lang.TF("next.state",
                "次の試合の指名: {0}（インポスター枠 {1} 人。解除は /next <名前> auto）",
                "Next game, others: {0} (impostor slots {1}; /next <name> auto clears one)",
                sb.ToString(), slots < 0 ? "?" : slots.ToString());
        }

        /// <summary>"/next &lt;name&gt;": that player's designation.</summary>
        public static string DescribeOne(byte id)
        {
            var e = Of(id);
            string name = Lang.StripTags(Core.Game.NameOf(id) ?? "").Trim();
            if (e == null) return Lang.TF("next.notlisted", "{0} は指名されていません。", "{0} is not designated.", name.Length == 0 ? "#" + id : name);
            return Lang.TF("next.one", "{0} (#{1}): {2} に指名中", "{0} (#{1}): designated {2}", Shown(e), e.Id, SideText(e.Side));
        }

        /// <summary>Short state for the top-left ping display, next to HostWish.Tag().</summary>
        public static string Tag()
        {
            if (List.Count == 0) return "";
            var imps = new StringBuilder();
            var crews = new StringBuilder();
            int ni = 0, nc = 0;
            foreach (var e in List)
            {
                var sb = e.Side == Side.Impostor ? imps : crews;
                int n = e.Side == Side.Impostor ? ++ni : ++nc;
                if (n > 3) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(Shown(e));
            }
            if (ni > 3) imps.Append("+").Append(ni - 3);
            if (nc > 3) crews.Append("+").Append(nc - 3);
            var t = new StringBuilder();
            if (imps.Length > 0) t.Append(Lang.T("next.tag.imp", "次:インポ=", "next: imp=", "下局:伪装者=")).Append(imps);
            if (crews.Length > 0) { if (t.Length > 0) t.Append(' '); t.Append(Lang.T("next.tag.crew", "次:クルー=", "next: crew=", "下局:船员=")).Append(crews); }
            return t.ToString();
        }

        /// <summary>ホスト page button: "次のインポ: なし" / "次のインポ: 太郎" (+n when more entries exist).</summary>
        public static string ButtonLabel()
        {
            string who = null;
            foreach (var e in List) if (e.Side == Side.Impostor) { who = Shown(e); break; }
            string label = Lang.T("ui.host.nextimp", "次のインポ", "Next imp", "下局伪装者") + ": "
                           + (who ?? Lang.T("next.none", "なし", "none", "无"));
            int others = List.Count - (who != null ? 1 : 0);
            if (others > 0) label += " (+" + others + ")";
            return label;
        }

        // ------------------------------------------------------------------ setting (host only, any lobby mode)

        /// <summary>Designates <paramref name="pc"/> (never the host: HostWish is the host's own path). Replaces an earlier designation of the same player, keeping its priority.</summary>
        public static bool Set(PlayerControl pc, Side side, out string message)
        {
            message = "";
            if (!Core.Game.IsHostActive)
            {
                message = Lang.T("cmd.modoff", "MODは現在オフです（バニラの試合）。", "The mod is currently off (vanilla game).");
                return false;
            }
            if (pc == null || pc.Data == null) return false;
            if (pc.AmOwner)
            {
                message = Lang.T("next.self", "自分の役は /next impostor | crew | auto で指定します。", "Your own role: /next impostor | crew | auto.");
                return false;
            }
            byte id = pc.PlayerId;
            var e = Of(id);
            if (e == null)
            {
                e = new Entry { Id = id, Order = _seq++ };
                List.Add(e);
            }
            e.Side = side;
            e.Name = Core.Game.NameOf(id) ?? "";
            string puid = null, fc = null;
            try { var info = Core.Game.Info(id); if (info != null) { puid = info.Puid; fc = info.FriendCode; } } catch (Exception) { }
            e.Puid = puid ?? "";
            e.FriendCode = fc ?? "";
            string name = Shown(e);
            message = Lang.TF("next.set", "次の試合: {0} (#{1}) = {2}（1 試合だけ。解除は /next #{1} auto）", "Next game: {0} (#{1}) = {2} (one game; /next #{1} auto clears)", name, id, SideText(side));
            if (side == Side.Impostor)
            {
                int slots = ImpostorSlots();
                if (slots >= 0 && CountImpostorEntries() > slots)
                    message += Lang.TF("next.cap", " 今の人数だとインポスター枠は {0} 人です（自分の指定も数えます）。先に指名した人が優先されます。", " Impostor slots at the current player count: {0} (your own wish counts); earlier designations win.", slots);
            }
            if (!Registration.CompatMode && Core.Game.ForcedRoles.ContainsKey(id))
                message += Lang.T("next.assignwins", " /assign の指定があるので、そちらが優先です。", " An /assign entry for this player takes precedence.");
            if (Core.Game.InProgress) message += Lang.T("cmd.set.next", " — 次の試合から適用", " - applies from the next game");
            PocketRolesPlugin.Logger.LogInfo($"Designate: set #{id} {name} -> {side} (order {e.Order}, {List.Count} designated)");
            return true;
        }

        /// <summary>"/next &lt;name&gt; auto".</summary>
        public static string Remove(byte id)
        {
            var e = Of(id);
            string name = Lang.StripTags(Core.Game.NameOf(id) ?? "").Trim();
            if (name.Length == 0) name = "#" + id;
            if (e == null) return Lang.TF("next.notlisted", "{0} は指名されていません。", "{0} is not designated.", name);
            List.Remove(e);
            PocketRolesPlugin.Logger.LogInfo($"Designate: removed #{id} {name}");
            return Lang.TF("next.removed", "{0} の指名を解除しました。", "Designation of {0} cleared.", name);
        }

        /// <summary>"/next reset" (the caller also clears HostWish).</summary>
        public static void Reset()
        {
            if (List.Count > 0) PocketRolesPlugin.Logger.LogInfo($"Designate: reset ({List.Count} designation(s) cleared)");
            List.Clear();
        }

        /// <summary>ホスト page button: おまかせ → the connected players in id order (one impostor designee at a time) → おまかせ. Crew designations made in chat are kept.</summary>
        public static string CycleFirst()
        {
            var players = new List<PlayerControl>();
            foreach (var pc in Core.Game.AllPlayers())
            {
                if (pc == null || pc.Data == null || pc.Data.Disconnected || pc.AmOwner) continue;
                var ce = Of(pc.PlayerId);
                if (ce != null && ce.Side == Side.Crewmate) continue;   // chat-made crew designations are kept
                players.Add(pc);
            }
            players.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId));
            byte current = 255;
            foreach (var e in List) if (e.Side == Side.Impostor) { current = e.Id; break; }
            PlayerControl next = null;
            foreach (var pc in players)
            {
                if (current == 255 || pc.PlayerId > current) { next = pc; break; }
            }
            List.RemoveAll(e => e.Side == Side.Impostor);
            if (next == null)
            {
                PocketRolesPlugin.Logger.LogInfo("Designate: host button -> none");
                return Lang.T("next.cycle.none", "次のインポ: なし（おまかせ）", "Next impostor: none (random)");
            }
            Set(next, Side.Impostor, out var msg);
            return msg;
        }

        /// <summary>Leaving the lobby (GameState.ResetForNewLobby): the designations belonged to it.</summary>
        public static void Clear()
        {
            if (List.Count > 0) PocketRolesPlugin.Logger.LogInfo("Designate: cleared (left the lobby)");
            List.Clear();
            _seq = 0;
            FillPrefer.Clear();
            FillAvoid.Clear();
            _active = false;
        }

        // ------------------------------------------------------------------ inside vanilla's SelectRoles

        private static bool Free(PlayerControl pc)
        {
            if (pc == null || pc.Data == null || pc.Data.Disconnected || pc.roleAssigned) return false;
            if (Core.Game.GameMasterActive && Core.Game.IsHost(pc.PlayerId)) return false;
            return true;
        }

        /// <summary>
        /// A designated impostor that still has no role: <paramref name="prefer"/> when it is one, else the first by
        /// designation order (never <paramref name="exclude"/>). Used by HostWish too (the host's held impostor role).
        /// </summary>
        internal static PlayerControl Taker(byte prefer, byte exclude = 255)
        {
            if (!_active) return null;
            Entry first = null;
            foreach (var e in List)
            {
                if (e.Side != Side.Impostor || e.Satisfied || e.Id == exclude) continue;
                var pc = HostWish.Find(e.Id);
                if (!Free(pc)) continue;
                if (e.Id == prefer) return pc;
                if (first == null) first = e;
            }
            return first == null ? null : HostWish.Find(first.Id);
        }

        /// <summary>A designee received an impostor role through a swap (from <paramref name="partner"/>, 255 = nobody in particular).</summary>
        internal static void MarkTaken(byte id, byte partner)
        {
            var e = Of(id);
            if (e == null) return;
            e.Satisfied = true;
            e.Partner = partner;
        }

        internal static bool IsCrewDesignee(byte id)
        {
            if (!_active) return false;
            var e = Of(id);
            return e != null && e.Side == Side.Crewmate;
        }

        /// <summary>RoleAssignment.BeginVanillaSelection, right after HostWish.OnBegin (not for haison games): validate the list, trim it to the impostor slots, arm.</summary>
        internal static void OnBegin()
        {
            _active = false;
            _held.Clear();
            FillPrefer.Clear();
            FillAvoid.Clear();
            try
            {
                foreach (var e in List) { e.Satisfied = false; e.Partner = 255; }
                if (List.Count == 0) return;
                _hostHasWish = HostWish.IsSet;      // after HostWish.OnBegin (GM mode / VanillaRoles-off may have dropped it)
                _hostImpWish = _hostHasWish && HostWishesImpostor();
                // players who left (or whose id now belongs to a later joiner)
                for (int i = List.Count - 1; i >= 0; i--)
                {
                    var e = List[i];
                    var pc = Core.Game.Player(e.Id);
                    bool ok = pc != null && pc.Data != null && !pc.Data.Disconnected;
                    if (ok)
                    {
                        string puid = null, fc = null;
                        try { var info = Core.Game.Info(e.Id); if (info != null) { puid = info.Puid; fc = info.FriendCode; } } catch (Exception) { }
                        if (!string.IsNullOrEmpty(e.Puid) && !string.IsNullOrEmpty(puid)) ok = e.Puid == puid;
                        else if (!string.IsNullOrEmpty(e.FriendCode) && !string.IsNullOrEmpty(fc)) ok = e.FriendCode == fc;
                        else ok = (Core.Game.NameOf(e.Id) ?? "") == (e.Name ?? "");
                    }
                    if (ok) continue;
                    PocketRolesPlugin.Logger.LogInfo($"Designate: dropped #{e.Id} {Shown(e)} (left, or the id belongs to somebody else now)");
                    Chat.Chat.Local(Chat.Chat.Title, Lang.TF("next.left", "指名していた {0} は退室したので、指名を解除しました。", "{0} left; the designation was dropped.", Shown(e)));
                    List.RemoveAt(i);
                }
                // more designated impostors than slots: the earliest designations win
                int slots = Math.Max(0, RoleAssignment.SelectTarget - (_hostImpWish ? 1 : 0));
                for (int i = List.Count - 1; i >= 0 && CountImpostorEntries() > slots; i--)
                {
                    var e = List[i];
                    if (e.Side != Side.Impostor) continue;
                    PocketRolesPlugin.Logger.LogInfo($"Designate: #{e.Id} {Shown(e)} skipped this game ({CountImpostorEntries()} impostor designees, {slots} slot(s))");
                    Chat.Chat.Local(Chat.Chat.Title, Lang.TF("next.trimmed", "インポスター枠が {0} 人なので、{1} の指名は今回見送りです。", "Only {0} impostor slot(s): {1} is skipped this game.", slots, Shown(e)));
                    List.RemoveAt(i);
                }
                foreach (var e in List)
                {
                    if (e.Side == Side.Impostor) FillPrefer.Add(e.Id); else FillAvoid.Add(e.Id);
                }
                _active = List.Count > 0;
                if (_active)
                {
                    var sb = new StringBuilder();
                    foreach (var e in List) { if (sb.Length > 0) sb.Append(", "); sb.Append('#').Append(e.Id).Append(' ').Append(Shown(e)).Append('=').Append(e.Side); }
                    PocketRolesPlugin.Logger.LogInfo($"Designate: armed — {sb} (target {RoleAssignment.SelectTarget}, host wish {(_hostHasWish ? HostWish.Wish.ToString() : "none")})");
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Designate.OnBegin: {e}");
                _active = false;
            }
        }

        private static void Redirect(PlayerControl to, RoleTypes role, bool co, byte from, string why)
        {
            HostWish.Send(to, role, co);
            if (RoleAssignment.IsImpostorRole(role)) RoleAssignment.RestoredRedirected(from, to.PlayerId);   // the 'promoted …' notice follows impostor roles only
            PocketRolesPlugin.Logger.LogInfo($"Designate: {role} meant for #{from} {Core.Game.NameOf(from)} -> #{to.PlayerId} {Core.Game.NameOf(to.PlayerId)} ({why})");
        }

        /// <summary>
        /// Vanilla's own RpcSetRole during SelectRoles, after TakeImpostorDefault and after HostWish.Intercept declined
        /// it. Returns true when the send was replaced (or dropped) here — the prefix then returns false.
        /// </summary>
        internal static bool Intercept(PlayerControl pc, RoleTypes role, bool co)
        {
            if (!_active || HostWish.Redirecting || pc == null || pc.Data == null) return false;
            try
            {
                byte p = pc.PlayerId;
                bool imp = RoleAssignment.IsImpostorRole(role);
                if (Core.Game.GameMasterActive && Core.Game.IsHost(p)) return false;   // the Game Master never plays
                var e = Of(p);
                if (e != null && e.Side == Side.Impostor)
                {
                    if (imp && !e.Satisfied)
                    {
                        e.Satisfied = true;
                        PocketRolesPlugin.Logger.LogInfo($"Designate: vanilla gave #{p} {Core.Game.NameOf(p)} {role} itself");
                        return false;
                    }
                    if (imp)
                    {
                        // already served by a swap (its first SetRole): this impostor role must go to somebody else
                        var d2 = Taker(255, p);
                        // the chain moves on: the player p took its role from is d2's partner now (d2's later crew role goes there)
                        if (d2 != null) { Redirect(d2, role, co, p, "second impostor role of a served designee"); MarkTaken(d2.PlayerId, e.Partner); e.Partner = 255; return true; }
                        var x = HostWish.Find(e.Partner);
                        if (Free(x) && !IsCrewDesignee(x.PlayerId)) { Redirect(x, role, co, p, "back to the player it was taken from"); e.Partner = 255; return true; }
                        // the partner is a crew designee (or has a role already): hold it in the free partner's name — the crew-pass swap
                        // gives that partner a free player's crew special and the free player this role; OnEnd releases it otherwise
                        byte src = Free(x) ? x.PlayerId : p;
                        if (src != p) e.Partner = 255;
                        _held.Add(new Held { Role = role, Co = co, From = src });
                        PocketRolesPlugin.Logger.LogInfo($"Designate: holding {role} (second impostor role of served designee #{p}, owner #{src})");
                        return true;
                    }
                    if (e.Satisfied)
                    {
                        // the crew pass visits the designee we made impostor: its crew role goes to the player it took the impostor role from
                        var x = HostWish.Find(e.Partner);
                        if (Free(x)) Redirect(x, role, co, p, "crew role of a served designee");
                        else PocketRolesPlugin.Logger.LogInfo($"Designate: later {role} for served designee #{p} dropped (partner already has a role)");
                        return true;
                    }
                    // a crew role for a designee vanilla never picked: the impostor pass is over, nothing left to take. When vanilla
                    // still owes impostor roles (3 players: it issues none), the designee stays roleless so that FillImpostors — which
                    // in an unregistered lobby can only promote players without a SetRole — promotes it first; the crew special is dropped.
                    if (RoleAssignment.SelectImpostorsSeen < RoleAssignment.SelectTarget && Free(pc))
                    {
                        PocketRolesPlugin.Logger.LogInfo($"Designate: {role} for #{p} {Core.Game.NameOf(p)} withheld (vanilla owes {RoleAssignment.SelectTarget - RoleAssignment.SelectImpostorsSeen} impostor role(s): the top-up promotes the designee)");
                        return true;
                    }
                    return false;
                }
                if (e != null)
                {
                    // designated crew
                    if (!imp) { e.Satisfied = true; return false; }
                    var d = Taker(255, p);
                    if (d != null)
                    {
                        Redirect(d, role, co, p, "impostor role of a crew designee");
                        MarkTaken(d.PlayerId, p);
                        e.Satisfied = true;   // p is vanilla's impostor pick: the crew pass never visits it, AssignPlainRoles gives it Crewmate
                        e.Partner = d.PlayerId;
                        return true;
                    }
                    _held.Add(new Held { Role = role, Co = co, From = p });
                    PocketRolesPlugin.Logger.LogInfo($"Designate: holding {role} taken from crew designee #{p} {Core.Game.NameOf(p)}");
                    return true;
                }
                // a player without designation (or the host)
                if (_hostHasWish && Core.Game.IsHost(p)) return false;   // HostWish alone decides the host's roles
                if (imp)
                {
                    var d = Taker(255, p);
                    if (d == null) return false;
                    Redirect(d, role, co, p, "free impostor pick");
                    MarkTaken(d.PlayerId, p);
                    return true;   // p receives nothing now: AssignPlainRoles sends it Crewmate
                }
                if (_held.Count > 0 && Free(pc))
                {
                    // a crew role for a free player while an impostor role taken from a crew designee waits: swap — the crew
                    // designee gets this crew role (its first SetRole), this player gets the impostor role (its first SetRole)
                    var h = _held[0];
                    _held.RemoveAt(0);
                    var c = HostWish.Find(h.From);
                    if (Free(c)) HostWish.Send(c, role, co);
                    else PocketRolesPlugin.Logger.LogInfo($"Designate: {role} meant for #{p} dropped (crew designee #{h.From} already has a role)");
                    HostWish.Send(pc, h.Role, h.Co);
                    RoleAssignment.RestoredRedirected(h.From, p);
                    var ce = Of(h.From);
                    if (ce != null) { ce.Satisfied = true; ce.Partner = p; }
                    PocketRolesPlugin.Logger.LogInfo($"Designate: held {h.Role} (from crew designee #{h.From}) -> #{p} {Core.Game.NameOf(p)}; its {role} -> #{h.From}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                PocketRolesPlugin.Logger.LogError($"Designate.Intercept: {ex}");
            }
            return false;
        }

        /// <summary>HostWish (vanilla crew-role wish): my held impostor role when nobody designated can take it right now — a free non-designee gets it in the crew pass or at OnEnd.</summary>
        internal static void Hold(RoleTypes role, bool co, byte from)
        {
            if (!_active)
            {
                var f = HostWish.Find(from);
                if (Free(f)) HostWish.Send(f, role, co);
                else PocketRolesPlugin.Logger.LogWarning($"Designate.Hold: not armed and #{from} already has a role — {role} dropped");
                return;
            }
            _held.Add(new Held { Role = role, Co = co, From = from });
            PocketRolesPlugin.Logger.LogInfo($"Designate: holding {role} for HostWish (taken from #{from} {Core.Game.NameOf(from)})");
        }

        /// <summary>The still-free player a served designee took its role from (HostWish hands a held crew role there), else null.</summary>
        internal static PlayerControl PartnerOf(byte id)
        {
            var e = Of(id);
            if (e == null || e.Partner == 255) return null;
            var x = HostWish.Find(e.Partner);
            return Free(x) ? x : null;
        }

        /// <summary>Anybody without a role yet (designees included; not the Game Master, not the host with an own wish).</summary>
        private static PlayerControl AnyFree()
        {
            foreach (var pc in Core.Game.AllPlayers())
            {
                if (!Free(pc)) continue;
                if (_hostHasWish && Core.Game.IsHost(pc.PlayerId)) continue;
                return pc;
            }
            return null;
        }

        /// <summary>A random connected player without a role that is not designated, not the host with an own wish and not the Game Master.</summary>
        private static PlayerControl PickAnybody()
        {
            var list = new List<PlayerControl>();
            foreach (var pc in Core.Game.AllPlayers())
            {
                if (!Free(pc)) continue;
                if (Of(pc.PlayerId) != null) continue;
                if (_hostHasWish && Core.Game.IsHost(pc.PlayerId)) continue;
                list.Add(pc);
            }
            if (list.Count == 0) return null;
            return list[new System.Random().Next(list.Count)];
        }

        /// <summary>RoleAssignment.EndVanillaSelection, after HostWish.OnEnd (whose crew-wish handoff asks <see cref="Taker"/>): release held roles, tell the host, consume.</summary>
        internal static void OnEnd()
        {
            if (!_active) return;
            try
            {
                foreach (var h in _held)
                {
                    // vanilla is done, so anybody still without a role is safe to address (no later SetRole can follow)
                    var taker = Taker(255, h.From) ?? PickAnybody();
                    var from = HostWish.Find(h.From);
                    if (taker == null && Free(from)) taker = from;   // the crew designee keeps vanilla's choice (count preserved, designation unfulfilled)
                    if (taker == null) taker = AnyFree();            // last resort: the impostor role must not vanish
                    if (taker == null)
                    {
                        PocketRolesPlugin.Logger.LogWarning($"Designate: nobody can take the {h.Role} taken from #{h.From}; FillImpostors tops the team up");
                        continue;
                    }
                    bool back = taker.PlayerId == h.From;
                    Redirect(taker, h.Role, h.Co, h.From, back ? "nobody else could take it: back to its owner" : "released at the end of the selection");
                    var ce = Of(h.From);
                    if (!back)
                    {
                        MarkTaken(taker.PlayerId, h.From);
                        if (ce != null) { ce.Satisfied = true; ce.Partner = taker.PlayerId; }
                    }
                    else if (ce != null) { ce.Satisfied = false; ce.Partner = 255; }
                }
                _held.Clear();
                FillPrefer.Clear();
                // impostor roles vanilla still owes (3 players: it issues none): FillImpostors tops the team up, designees first
                int shortBy = Math.Max(0, RoleAssignment.SelectTarget - RoleAssignment.SelectImpostorsSeen);
                foreach (var e in List)
                {
                    var pc = HostWish.Find(e.Id);
                    var cur = pc != null && pc.Data != null && pc.Data.Role != null ? pc.Data.Role.Role : RoleTypes.Crewmate;
                    bool hasRole = pc != null && pc.roleAssigned;
                    bool ok;
                    string side = SideText(e.Side);
                    if (e.Side == Side.Impostor)
                    {
                        ok = e.Satisfied || (hasRole && RoleAssignment.IsImpostorRole(cur));
                        if (!ok && pc != null && !hasRole)
                        {
                            FillPrefer.Add(e.Id);   // FillImpostors' first picks when the team is short
                            if (FillPrefer.Count <= shortBy)
                            {
                                Chat.Chat.Local(Chat.Chat.Title, Lang.TF("next.result.fill", "本体は {0} を選ばなかったので、補充で {1} にします。", "Vanilla did not pick {0}; the top-up makes them {1}.", Shown(e), side));
                                PocketRolesPlugin.Logger.LogInfo($"Designate: #{e.Id} {Shown(e)} left to FillImpostors ({shortBy} short)");
                                continue;
                            }
                        }
                    }
                    else ok = !hasRole || !RoleAssignment.IsImpostorRole(cur);
                    if (ok)
                    {
                        string swap = e.Partner != 255 ? Lang.TF("me.result.swap", "（{0} と入れ替え）", " (swapped with {0})", Core.Game.NameOf(e.Partner)) : "";
                        Chat.Chat.Local(Chat.Chat.Title, Lang.TF("next.result.ok", "今回: {0} = {1}{2}", "This game: {0} = {1}{2}", Shown(e), side, swap));
                    }
                    else
                    {
                        Chat.Chat.Local(Chat.Chat.Title, Lang.TF("next.result.no", "今回は本体の配役の都合で {0} を {1} にできませんでした。", "Vanilla's picks left no way to make {0} {1} this game.", Shown(e), side));
                    }
                    PocketRolesPlugin.Logger.LogInfo($"Designate: result #{e.Id} {Shown(e)} {e.Side} {(ok ? "OK" : "NOT fulfilled")} (partner #{e.Partner}, role now {cur}, roleAssigned {hasRole})");
                }
            }
            catch (Exception ex)
            {
                PocketRolesPlugin.Logger.LogError($"Designate.OnEnd: {ex}");
            }
            finally
            {
                _active = false;
                _held.Clear();
                List.Clear();   // consumed
            }
        }
    }
}
