// PocketRoles ReportFetcher — 作者用のツールです。報告専用メール（Gmail）から不具合報告・要望・質問・+host のメールを reports\ に
// 取り込み、質問への返事を 1 通「Gmail の下書き」に入れます。
//
// メールは送りません（わざとです）。このツールから（SMTP で）送ると、Gmail がこの PC（家）の IP アドレスをメールの
// Received ヘッダーに書き込み、受け取った人に見えてしまいます（チーターに家の回線を攻撃されるおそれがあります）。
// 下書きを作ったあと、作者がブラウザで Gmail を開き →「下書き」→ 内容を確かめて →「送信」を押します。
// ブラウザの Gmail（とスマホの Gmail アプリ）から送ったメールには、この PC の IP アドレスは入りません。
// Outlook・Thunderbird・Windows の「メール」・iPhone の「メール」などのメールソフトにも IMAP で下書きが出てきますが、
// そこから送ると SMTP で送ることになり、前の版と同じく IP アドレスが入ります。送るのはブラウザの Gmail からだけです。
//
// 使い方:
//   ReportFetcher.dll [report-mail.json]
//       取り込み: 受信トレイ → reports\bugs\ / requests\ / questions\ / aegis\（取り込んだメールは「Processed」フォルダへ移します）
//   ReportFetcher.dll --draft <下書き.txt> [--dry-run] [report-mail.json]
//       返事を 1 通、Gmail の「下書き」フォルダに入れます（送信はしません）。下書き.txt = ヘッダーの行（To: / Subject: /
//       In-Reply-To: / References: …）、空行、本文。うまくいくと下書き.txt の末尾に「Drafted: 日時」を足し、同じファイルから
//       2 回は作りません（前の版が書いた「Sent: 日時」の行があっても作りません）。
//       --dry-run: どこにもつながず、下書き.txt の隣に .eml を作って中身を表示するだけです。
//   ReportFetcher.dll --send …
//       前の版の書き方です。いまは --draft と同じ動きで、送信はしません（最初にそのことを表示します）。
//   ReportFetcher.dll --check-sent [report-mail.json]
//       前の版（--send）で送った返事に、この PC の IP アドレスが入ったかを数えます。読むだけで、メールは何も変えません。
//       Gmail の送信済みと sentFolder（あれば）のメールの Received と From のヘッダー（From は自分が送ったメールかを確かめる
//       ためだけ）と日付だけを読み、件数と日付だけを表示します（IP アドレス・ホスト名・メールアドレス・宛先・件名・ヘッダーの文は、
//       エラーの時も表示しません）。Received の行がない・読めないメールは「判定できない」と数え、「なかった」とは言いません。
//   ReportFetcher.dll --self-test
//       （使い方には出さない）Received ヘッダーの判定を、作りものの例でテストします。どこにもつなぎません。
// 終了コード: 0 = OK（--check-sent では「公開の IP アドレスは見つからなかった」）、1 = そのほかのエラー（--check-sent では
//             「全部は調べられなかった」も）、2 = 設定・引数の不足、3 = ログインの失敗、4 = 下書きの不備、
//             10 = --check-sent で公開の IP アドレスが入った返事が見つかった。
// パスワードは表示しません。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;

namespace PocketRoles.Tools
{
    /// <summary>
    /// report-mail.json. Unknown keys are ignored by System.Text.Json (the default JsonUnmappedMemberHandling.Skip), so an
    /// old file that still holds smtpHost / smtpPort / smtpUser loads unchanged (checked by --self-test). smtpHost / smtpUser
    /// are read only so that --check-sent can tell when the old --send used another account or server (never printed, never used to connect).
    /// </summary>
    internal sealed class MailConfig
    {
        public string imapHost { get; set; } = "imap.gmail.com";
        public int imapPort { get; set; } = 993;
        public string user { get; set; } = "";
        public string appPassword { get; set; } = "";
        public string processedFolder { get; set; } = "PocketRoles/Processed";
        /// <summary>IMAP folder where the old --send kept a copy of every reply; now only read by --check-sent (never created).</summary>
        public string sentFolder { get; set; } = "PocketRoles/Sent";
        public string outputDir { get; set; } = "reports";
        public string requestMarker { get; set; } = "+request";
        /// <summary>Plus-alias for questions ("+question" is always accepted as well).</summary>
        public string helpMarker { get; set; } = "+help";
        /// <summary>v0.5.5: plus-alias of the hosts and admins (Aegis: cheater reports with a report zip, shared-ban requests) → reports\aegis.</summary>
        public string hostMarker { get; set; } = "+host";
        public int maxMails { get; set; } = 200;
        /// <summary>Display name used in the From: header of reply drafts.</summary>
        public string fromName { get; set; } = "PocketRoles サポート";
        /// <summary>Senders (domain or address) whose mails are archived without import when they carry no attachment.</summary>
        public string[] skipSenders { get; set; } = new[] { "google.com", "accounts.google.com", "youtube.com", "mail-noreply.google.com", "no-reply@accounts.google.com" };
        /// <summary>Old --send only (null when absent): the SMTP server it sent through. Read by --check-sent, never printed.</summary>
        public string smtpHost { get; set; }
        /// <summary>Old --send only (null / empty = the same as user): the account it logged in to SMTP with. Read by --check-sent, never printed.</summary>
        public string smtpUser { get; set; }
    }

    internal sealed class DraftException : Exception
    {
        public DraftException(string message) : base(message) { }
    }

    internal sealed class Draft
    {
        public string To;
        public string Cc;
        public string Subject;
        public string InReplyTo;
        public string References;
        public string Faq;
        public string Body = "";
        /// <summary>Value of the trailing "Drafted: …" line appended after the Gmail draft was made (null = not yet).</summary>
        public string DraftedAt;
        /// <summary>Value of the trailing "Sent: …" line the old --send appended after sending (null = never sent).</summary>
        public string SentAt;
    }

    /// <summary>What the Received headers of one sent mail reveal about the machine that submitted it. Ordered by how bad it is (Public &gt; Unknown &gt; Lan &gt; None).</summary>
    internal enum ClientIp
    {
        /// <summary>No SMTP-submission hop with an IP literal (web / app send, Google-internal hops only, a malformed header).</summary>
        None = 0,
        /// <summary>Only LAN addresses (10/8, 172.16/12, 192.168/16, 127/8, 169.254/16, ::1, fe80::/10, fc00::/7).</summary>
        Lan = 1,
        /// <summary>Cannot tell: no Received header at all (a real sent mail always has one), or the check itself failed. Never counted as "none".</summary>
        Unknown = 2,
        /// <summary>At least one address outside those ranges (the home line's public address).</summary>
        Public = 3,
    }

    internal static class Program
    {
        private static readonly string[] QuestionKeywords =
        {
            "質問", "教えて", "わからない", "分からない", "導入", "遊び方",
            "how to", "install", "help",
            "怎么", "如何", "安装", "问题",
        };

        private static readonly HashSet<string> SavedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".log", ".txt", ".png", ".jpg", ".jpeg", ".cfg", ".json",
        };

        private static readonly JsonSerializerOptions ConfigJson = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        private static int Main(string[] args)
        {
            // When the output is captured by a script (bash / PowerShell) emit UTF-8 so Chinese and Japanese survive; keep the console code page otherwise.
            try { if (Console.IsOutputRedirected) Console.OutputEncoding = new UTF8Encoding(false); } catch { }

            string cfgArg = null, draftPath = null;
            bool dryRun = false, sendAlias = false, checkSent = false, selfTest = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--draft" || a == "--send")
                {
                    if (a == "--send") sendAlias = true;
                    if (i + 1 >= args.Length) { Console.WriteLine(a + " の後に下書きファイルを指定してください。"); PrintUsage(); return 2; }
                    if (draftPath != null) { Console.WriteLine("下書きファイルは 1 つだけ指定してください。"); PrintUsage(); return 2; }
                    draftPath = args[++i];
                }
                else if (a == "--dry-run") dryRun = true;
                else if (a == "--check-sent") checkSent = true;
                else if (a == "--self-test") selfTest = true;
                else if (a == "--help" || a == "-h" || a == "/?") { PrintUsage(); return 0; }
                else if (a.StartsWith("--", StringComparison.Ordinal)) { Console.WriteLine("不明なオプション: " + a); PrintUsage(); return 2; }
                else cfgArg = a;
            }
            if (sendAlias)
            {
                Console.WriteLine("【お知らせ】--send（このツールからの送信）は、わざと無くしました。");
                Console.WriteLine("  このツールから送ると、Gmail がこの PC（家）の IP アドレスをメールに書き込み、受け取った人に見えてしまうためです。");
                Console.WriteLine("  代わりに Gmail の「下書き」を作ります（--draft と同じ）。送信は、ブラウザの Gmail から自分で押してください。");
                Console.WriteLine("  （Outlook・Thunderbird・Windows の「メール」・iPhone の「メール」などのメールソフトから送ると、同じく IP アドレスが入ります）");
            }
            int modes = (draftPath != null ? 1 : 0) + (checkSent ? 1 : 0) + (selfTest ? 1 : 0);
            if (modes > 1) { Console.WriteLine("--draft / --check-sent / --self-test は、どれか 1 つだけ指定してください。"); PrintUsage(); return 2; }
            if (dryRun && draftPath == null) { Console.WriteLine("--dry-run は --draft <下書き> と一緒に指定してください。"); PrintUsage(); return 2; }
            if (selfTest) return SelfTest();

            try
            {
                string cfgPath = cfgArg ?? FindConfig();
                if (cfgPath == null || !File.Exists(cfgPath))
                {
                    Console.WriteLine("設定ファイル report-mail.json が見つかりません。");
                    return 2;
                }
                MailConfig cfg;
                try { cfg = JsonSerializer.Deserialize<MailConfig>(File.ReadAllText(cfgPath, Encoding.UTF8), ConfigJson); }
                catch (JsonException e)
                {
                    // never echo the parser's message: it may quote a piece of the file (the file holds the app password)
                    Console.WriteLine("report-mail.json の書き方がまちがっています" + (e.LineNumber.HasValue ? "（" + (e.LineNumber.Value + 1) + " 行目あたり）" : "") + "。");
                    return 2;
                }
                if (cfg == null || string.IsNullOrWhiteSpace(cfg.user))
                {
                    Console.WriteLine("report-mail.json の user を設定してください。");
                    return 2;
                }
                // A dry run never authenticates, so it does not need the password.
                if (!dryRun && (string.IsNullOrWhiteSpace(cfg.appPassword) || cfg.appPassword.Contains("ここに")))
                {
                    Console.WriteLine("report-mail.json の user / appPassword を設定してください（アプリ パスワードは 16 文字、スペース不要）。");
                    return 2;
                }
                string password = (cfg.appPassword ?? "").Replace(" ", "");
                string baseDir = Path.GetDirectoryName(Path.GetFullPath(cfgPath));

                if (checkSent) return CheckSent(cfg, password);
                if (draftPath != null) return MakeDraft(cfg, password, draftPath, dryRun);
                return Fetch(cfg, password, baseDir);
            }
            catch (AuthenticationException)
            {
                Console.WriteLine("ログインに失敗しました。report-mail.json のアドレスとアプリ パスワードを確認してください（2 段階認証がオンで、アプリ パスワードを使う必要があります）。");
                return 3;
            }
            catch (DraftException e)
            {
                Console.WriteLine("下書きエラー: " + e.Message);
                return 4;
            }
            catch (Exception e)
            {
                // --check-sent never prints an exception text (it could quote a host name or an address)
                Console.WriteLine(checkSent ? "エラーが起きました（" + e.GetType().Name + "）。" : "エラー: " + e.Message);
                return 1;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("使い方:");
            Console.WriteLine("  ReportFetcher.dll [report-mail.json]");
            Console.WriteLine("      メールを取り込む → reports\\bugs, requests, questions, aegis（+host の報告 zip）");
            Console.WriteLine("  ReportFetcher.dll --draft <下書き.txt> [--dry-run] [report-mail.json]");
            Console.WriteLine("      返事を 1 通、Gmail の「下書き」に入れる（送信はしません）。");
            Console.WriteLine("      そのあと、ブラウザで Gmail を開き →「下書き」→ 内容を確かめて →「送信」を押します。");
            Console.WriteLine("      --dry-run: どこにもつながず、下書きの隣に .eml を作って表示するだけ");
            Console.WriteLine("  ReportFetcher.dll --check-sent [report-mail.json]");
            Console.WriteLine("      前の版（--send）で送った返事に、この PC の IP アドレスが入ったかを数える（読むだけ。件数と日付だけ表示）");
            Console.WriteLine("  --send は前の版の書き方です。いまは --draft と同じで、送信はしません。");
            Console.WriteLine("  （このツールから送ると、この PC の IP アドレスが相手に見えてしまうので、送信はブラウザの Gmail からします）");
            Console.WriteLine("  送信はブラウザの Gmail か、スマホの Gmail アプリからだけ。Outlook・Thunderbird・Windows の「メール」・");
            Console.WriteLine("  iPhone の「メール」などのメールソフトからは送らないでください（家の IP アドレスが入るため）。");
        }

        // ------------------------------------------------------------------ fetch ------------------------------------------------------------------

        private static int Fetch(MailConfig cfg, string password, string baseDir)
        {
            string outDir = Path.IsPathRooted(cfg.outputDir) ? cfg.outputDir : Path.Combine(baseDir, cfg.outputDir);
            Directory.CreateDirectory(Path.Combine(outDir, "bugs"));
            Directory.CreateDirectory(Path.Combine(outDir, "requests"));
            Directory.CreateDirectory(Path.Combine(outDir, "questions"));
            Directory.CreateDirectory(Path.Combine(outDir, "aegis"));

            using var client = new ImapClient();
            client.Connect(cfg.imapHost, cfg.imapPort, SecureSocketOptions.SslOnConnect);
            client.Authenticate(cfg.user, password);
            var inbox = client.Inbox;
            inbox.Open(FolderAccess.ReadWrite);

            IMailFolder processed = null;
            try
            {
                var personal = client.GetFolder(client.PersonalNamespaces[0]);
                processed = GetOrCreateFolder(personal, cfg.processedFolder);
            }
            catch (Exception e)
            {
                Console.WriteLine("処理済みフォルダを準備できませんでした（メールはそのまま残します）: " + e.Message);
            }

            var uids = inbox.Search(SearchQuery.All).ToList();
            if (uids.Count > cfg.maxMails) uids = uids.Skip(uids.Count - cfg.maxMails).ToList();
            int bugs = 0, requests = 0, questions = 0, aegis = 0, skipped = 0;
            var toMove = new List<UniqueId>();
            foreach (var uid in uids)
            {
                MimeMessage msg;
                try { msg = inbox.GetMessage(uid); }
                catch (Exception e) { Console.WriteLine($"  [skip] uid {uid}: {e.Message}"); continue; }

                string from = msg.From.Mailboxes.FirstOrDefault()?.Address ?? "unknown";
                var files = EnumerateFiles(msg).ToList();
                // System mails (Google security notices, YouTube, etc.) without attachments are not reports: archive them silently.
                if (files.Count == 0 && IsSystemSender(from, cfg.skipSenders))
                {
                    toMove.Add(uid);
                    skipped++;
                    continue;
                }

                string body = msg.TextBody ?? HtmlToText(msg.HtmlBody) ?? "";
                string kind = Classify(msg, cfg, body, files);
                DateTimeOffset date = (msg.Date == DateTimeOffset.MinValue ? DateTimeOffset.Now : msg.Date).ToLocalTime();
                string stamp = date.ToString(kind == "questions" ? "yyyyMMdd-HHmmss" : "yyyyMMdd-HHmm");
                string dir = Path.Combine(outDir, kind, stamp + "-" + ShortHash(from + "|" + msg.MessageId));
                Directory.CreateDirectory(dir);

                int saved = 0;
                var savedNames = new List<string>();
                foreach (var (name, entity) in files)
                {
                    string ext = Path.GetExtension(name);
                    if (!SavedExtensions.Contains(ext)) continue;
                    string path = Path.Combine(dir, name);
                    using (var fs = File.Create(path))
                    {
                        if (entity is MimePart part) part.Content.DecodeTo(fs);
                        else if (entity is MessagePart mp) mp.Message.WriteTo(fs);
                    }
                    saved++;
                    savedNames.Add(name);
                }

                File.WriteAllText(Path.Combine(dir, "mail.txt"),
                    kind == "questions" ? FormatQuestionMail(msg, from, date, kind, body, savedNames) : FormatReportMail(msg, from, date, kind, body),
                    new UTF8Encoding(true));
                if (kind == "questions") WriteReplySkeleton(dir, msg, from);

                Console.WriteLine($"  [{kind}] {stamp} {MaskAddress(from)} \"{msg.Subject}\" files={saved}");
                if (kind == "requests") requests++; else if (kind == "questions") questions++; else if (kind == "aegis") aegis++; else bugs++;
                toMove.Add(uid);
            }

            if (processed != null && toMove.Count > 0)
            {
                inbox.MoveTo(toMove, processed);
            }
            else if (toMove.Count > 0)
            {
                inbox.AddFlags(toMove, MessageFlags.Seen, true);
            }
            client.Disconnect(true);
            Console.WriteLine($"取り込み完了: 不具合 {bugs} 件 / 要望 {requests} 件 / 質問 {questions} 件 / Aegis（ホスト・管理人）{aegis} 件 / システム通知など {skipped} 件は処理済みへ → {outDir}");
            return 0;
        }

        /// <summary>bugs / requests / questions / aegis. Aliases win (+host: the hosts' and admins' Aegis mail, its report zip for the ban console); then request keywords in the subject; then question keywords (only without zip/log).</summary>
        private static string Classify(MimeMessage msg, MailConfig cfg, string body, List<(string name, MimeEntity entity)> files)
        {
            if (AddressedTo(msg, cfg.hostMarker)) return "aegis";
            if (AddressedTo(msg, cfg.helpMarker) || AddressedTo(msg, "+question")) return "questions";
            if (AddressedTo(msg, cfg.requestMarker)) return "requests";
            string subject = msg.Subject ?? "";
            if (HasRequestKeyword(subject)) return "requests";
            bool hasReportFile = files.Any(f =>
            {
                string ext = Path.GetExtension(f.name);
                return ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) || ext.Equals(".log", StringComparison.OrdinalIgnoreCase);
            });
            if (!hasReportFile && (HasQuestionKeyword(subject) || HasQuestionKeyword(body))) return "questions";
            return "bugs";
        }

        private static bool AddressedTo(MimeMessage msg, string marker)
        {
            if (string.IsNullOrWhiteSpace(marker)) return false;
            foreach (var m in msg.To.Mailboxes.Concat(msg.Cc.Mailboxes))
                if (m.Address != null && m.Address.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // Gmail may rewrite the plus address; fall back to the delivery headers
            foreach (var h in msg.Headers)
                if ((h.Field == "Delivered-To" || h.Field == "X-Original-To" || h.Field == "Envelope-To") && h.Value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool HasRequestKeyword(string subject)
        {
            string s = subject ?? "";
            return s.Contains("要望") || s.IndexOf("request", StringComparison.OrdinalIgnoreCase) >= 0 || s.Contains("功能") || s.Contains("建议");
        }

        private static bool HasQuestionKeyword(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var k in QuestionKeywords)
                if (text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>Existing mail.txt layout for bugs / requests (lower-case keys), plus the ids needed to reply in-thread.</summary>
        private static string FormatReportMail(MimeMessage msg, string from, DateTimeOffset date, string kind, string body)
        {
            var sb = new StringBuilder();
            sb.AppendLine("date: " + date.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("from: " + from + (string.IsNullOrEmpty(msg.From.Mailboxes.FirstOrDefault()?.Name) ? "" : " (" + msg.From.Mailboxes.First().Name + ")"));
            sb.AppendLine("to: " + string.Join(", ", msg.To.Mailboxes.Select(m => m.Address)));
            sb.AppendLine("subject: " + msg.Subject);
            sb.AppendLine("message-id: " + Angle(msg.MessageId));
            if (!string.IsNullOrEmpty(msg.InReplyTo)) sb.AppendLine("in-reply-to: " + Angle(msg.InReplyTo));
            sb.AppendLine("kind: " + kind);
            sb.AppendLine();
            sb.AppendLine(body);
            return sb.ToString();
        }

        /// <summary>mail.txt for questions: RFC-style header names so the values can be copied into a reply draft.</summary>
        private static string FormatQuestionMail(MimeMessage msg, string from, DateTimeOffset date, string kind, string body, List<string> savedNames)
        {
            var sb = new StringBuilder();
            string fromName = msg.From.Mailboxes.FirstOrDefault()?.Name;
            sb.AppendLine("From: " + (string.IsNullOrEmpty(fromName) ? from : fromName + " <" + from + ">"));
            sb.AppendLine("To: " + string.Join(", ", msg.To.Mailboxes.Select(m => m.Address)));
            if (msg.Cc.Mailboxes.Any()) sb.AppendLine("Cc: " + string.Join(", ", msg.Cc.Mailboxes.Select(m => m.Address)));
            sb.AppendLine("Date: " + date.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            sb.AppendLine("Subject: " + msg.Subject);
            sb.AppendLine("Message-Id: " + Angle(msg.MessageId));
            sb.AppendLine("In-Reply-To: " + (string.IsNullOrEmpty(msg.InReplyTo) ? "" : Angle(msg.InReplyTo)));
            if (msg.References.Count > 0) sb.AppendLine("References: " + string.Join(" ", msg.References.Select(Angle)));
            sb.AppendLine("Kind: " + kind);
            if (savedNames.Count > 0) sb.AppendLine("Attachments: " + string.Join(", ", savedNames));
            sb.AppendLine();
            sb.AppendLine(body);
            return sb.ToString();
        }

        /// <summary>Creates reply.txt with the threading headers filled in and an EMPTY body (an empty body never becomes a draft).</summary>
        private static void WriteReplySkeleton(string dir, MimeMessage msg, string from)
        {
            string path = Path.Combine(dir, "reply.txt");
            if (File.Exists(path)) return;
            string subject = (msg.Subject ?? "").Trim();
            if (!Regex.IsMatch(subject, @"^\s*(re|返信|回复|回覆)\s*:", RegexOptions.IgnoreCase)) subject = "Re: " + subject;
            var refs = msg.References.Select(Angle).ToList();
            if (!string.IsNullOrEmpty(msg.MessageId)) refs.Add(Angle(msg.MessageId));
            var sb = new StringBuilder();
            sb.AppendLine("To: " + from);
            sb.AppendLine("Subject: " + subject);
            sb.AppendLine("In-Reply-To: " + (string.IsNullOrEmpty(msg.MessageId) ? "" : Angle(msg.MessageId)));
            sb.AppendLine("References: " + string.Join(" ", refs));
            sb.AppendLine("FAQ: ");
            sb.AppendLine();
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>Attachments plus inline parts that carry a file name (screenshots pasted into Gmail are inline).</summary>
        private static IEnumerable<(string name, MimeEntity entity)> EnumerateFiles(MimeMessage msg)
        {
            var seen = new HashSet<MimeEntity>();
            int n = 0;
            foreach (var att in msg.Attachments)
            {
                seen.Add(att);
                string name = (att as MimePart)?.FileName ?? (att as MessagePart)?.ContentDisposition?.FileName ?? ("attachment" + n);
                n++;
                yield return (SafeName(name), att);
            }
            foreach (var part in msg.BodyParts.OfType<MimePart>())
            {
                if (seen.Contains(part) || string.IsNullOrEmpty(part.FileName)) continue;
                if (!part.ContentType.IsMimeType("image", "*") && !part.IsAttachment) continue;
                seen.Add(part);
                yield return (SafeName(part.FileName), part);
            }
        }

        // ------------------------------------------------------------------ draft ------------------------------------------------------------------

        /// <summary>
        /// Puts ONE reply into the Gmail Drafts folder over IMAP (APPEND with \Draft \Seen). Nothing is sent: the owner opens
        /// Gmail in the browser, checks the draft and presses Send there (a web send carries no client IP). --dry-run only
        /// writes and prints the .eml next to the draft, without connecting.
        /// </summary>
        private static int MakeDraft(MailConfig cfg, string password, string draftPath, bool dryRun)
        {
            string full = Path.GetFullPath(draftPath);
            if (!File.Exists(full)) throw new DraftException("下書きファイルがありません: " + full);
            var draft = ParseDraft(full);
            if (string.IsNullOrWhiteSpace(draft.To)) throw new DraftException("To: がありません（Gmail の下書きは作りません）。");
            if (string.IsNullOrWhiteSpace(draft.Body)) throw new DraftException("本文が空です（Gmail の下書きは作りません）。ヘッダーの後に空行を 1 行入れ、その下に本文を書いてください。");
            if (!dryRun) RefuseIfDone(draft);

            var msg = BuildMessage(cfg, draft);
            var rcpts = msg.To.Mailboxes.Select(m => m.Address).Concat(msg.Cc.Mailboxes.Select(m => m.Address)).ToList();
            if (rcpts.Count == 0) throw new DraftException("To: にメールアドレスが含まれていません: " + draft.To);

            if (dryRun)
            {
                string emlPath = Path.Combine(Path.GetDirectoryName(full), Path.GetFileNameWithoutExtension(full) + ".eml");
                // 998 = RFC 5322 line limit; with the default 78 MimeKit would switch the preview to quoted-printable and make it unreadable.
                msg.Prepare(EncodingConstraint.EightBit, 998);
                using (var fs = File.Create(emlPath)) msg.WriteTo(fs);
                Console.WriteLine("[dry-run] Gmail にはつながず、下書きも作りません（もちろん送信もしません）。組み立てたメール → " + emlPath);
                Console.WriteLine("[dry-run] To: " + string.Join(", ", rcpts) + " / Subject: " + msg.Subject + " / 本文 " + draft.Body.Length + " 文字" + (draft.Faq != null ? " / FAQ: " + draft.Faq : ""));
                if (draft.DraftedAt != null) Console.WriteLine("[dry-run] 注意: この下書きは Gmail の下書きにもう入れてあります（Drafted: " + draft.DraftedAt + "）。");
                if (draft.SentAt != null) Console.WriteLine("[dry-run] 注意: この下書きは前の版のツールで送信済みです（Sent: " + draft.SentAt + "）。");
                Console.WriteLine("----- " + Path.GetFileName(emlPath) + " -----");
                Console.WriteLine(File.ReadAllText(emlPath, Encoding.UTF8));
                Console.WriteLine("----- end -----");
                return 0;
            }

            using (var client = new ImapClient())
            {
                client.Connect(cfg.imapHost, cfg.imapPort, SecureSocketOptions.SslOnConnect);
                client.Authenticate(cfg.user, password);
                var drafts = FindSpecialFolder(client, SpecialFolder.Drafts, FolderAttributes.Drafts);
                if (drafts == null)
                {
                    try { client.Disconnect(true); } catch { }
                    Console.WriteLine("Gmail の「下書き」フォルダが見つかりませんでした（フォルダの名前で当てずっぽうに探すことはしません）。何も作っていません。");
                    Console.WriteLine("ブラウザの Gmail の 設定 →「ラベル」→「下書き」の「IMAP で表示」にチェックが入っているか確かめてください。");
                    return 1;
                }
                // 7-bit (base64 / quoted-printable) is the safest form for IMAP APPEND; Gmail shows it as normal text.
                msg.Prepare(EncodingConstraint.SevenBit);
                try
                {
                    drafts.Append(new AppendRequest(msg, MessageFlags.Draft | MessageFlags.Seen));
                }
                catch (Exception e)
                {
                    // The connection can drop after Gmail stored the draft but before its answer arrived: never suggest a blind retry.
                    try { client.Disconnect(true); } catch { }
                    Console.WriteLine("Gmail の「下書き」に入れる途中でエラーが起きました（" + e.GetType().Name + "）: " + e.Message);
                    Console.WriteLine("下書きが入っていることもあります。もう一度実行する前に、ブラウザの Gmail の「下書き」を見てください（入っていたら、もう一度は実行しないでください）。");
                    return 1;
                }
                // Mark the draft file right after the APPEND returned (before LOGOUT, which may fail on its own) so the
                // same file cannot become a second Gmail draft.
                MarkDrafted(full);
                try { client.Disconnect(true); } catch { }
            }

            Console.WriteLine("Gmail の「下書き」に入れました。まだ何も送っていません。（宛先 " + string.Join(", ", rcpts.Select(MaskAddress)) + "）");
            Console.WriteLine("このあと: ブラウザで Gmail を開き →「下書き」→ 内容を確かめて →「送信」を押してください。");
            Console.WriteLine("（このツールからは送りません。ここから送ると、この PC の IP アドレスが相手に見えてしまうためです）");
            Console.WriteLine("送信はブラウザの Gmail か、スマホの Gmail アプリからだけにしてください。Outlook・Thunderbird・Windows の「メール」・");
            Console.WriteLine("iPhone の「メール」などのメールソフトにもこの下書きが出ますが、そこから送ると家の IP アドレスが入ります。");
            Console.WriteLine("初めての時だけ: ブラウザの Gmail の 設定 →「アカウントとインポート」→「名前」が「" + (string.IsNullOrWhiteSpace(cfg.fromName) ? "PocketRoles サポート" : cfg.fromName) + "」になっているか確かめてください");
            Console.WriteLine("（ブラウザから送ると、相手にはここの名前が見えます。本名などになっていたら「情報を編集」で直します）。");
            return 0;
        }

        /// <summary>Appends "Drafted: &lt;now&gt;" to the draft file (the same file then cannot become a second Gmail draft). Never throws.</summary>
        private static void MarkDrafted(string full)
        {
            try
            {
                string existing = File.ReadAllText(full, Encoding.UTF8);
                string nl = existing.Contains("\r\n") ? "\r\n" : "\n";
                string tail = (existing.EndsWith("\n") ? "" : nl) + nl + "Drafted: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + nl;
                File.AppendAllText(full, tail, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Console.WriteLine("  下書きファイルへの Drafted: の追記に失敗しました（Gmail の下書きはできています。同じ返事を 2 回作らないよう気をつけてください）: " + e.Message);
            }
        }

        /// <summary>A draft file that already became a Gmail draft ("Drafted:") or was sent by the old tool ("Sent:") is refused.</summary>
        private static void RefuseIfDone(Draft d)
        {
            if (d.DraftedAt != null)
                throw new DraftException("この下書きは Gmail の下書きにもう入れてあります（Drafted: " + d.DraftedAt + "）。作り直すときは、Gmail の「下書き」にある古いほうを消してから、このファイルの末尾の Drafted: の行を消して、もう一度実行してください。");
            if (d.SentAt != null)
                throw new DraftException("この下書きは前の版のツールで送信済みです（Sent: " + d.SentAt + "）。同じ返事をもう一度作るときは、相手に 2 通届いてよいか確かめてから、末尾の Sent: の行を消して、もう一度実行してください。");
        }

        /// <summary>Header block (until the first blank line), then the body. "#" lines in the header block are comments. Trailing "Drafted: …" / "Sent: …" lines mark a finished draft.</summary>
        private static Draft ParseDraft(string path) => ParseDraftText(File.ReadAllText(path, Encoding.UTF8));

        private static Draft ParseDraftText(string text)
        {
            var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var d = new Draft();
            int i = 0;
            for (; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.Trim().Length == 0) { i++; break; }
                if (l.StartsWith("#", StringComparison.Ordinal)) continue;
                int c = l.IndexOf(':');
                if (c <= 0) throw new DraftException($"{i + 1} 行目がヘッダーではありません（To: / Subject: … の後に空行を 1 行入れてから本文を書いてください）: {l}");
                string name = l.Substring(0, c).Trim().ToLowerInvariant();
                string val = l.Substring(c + 1).Trim();
                switch (name)
                {
                    case "to": d.To = val; break;
                    case "cc": d.Cc = val; break;
                    case "subject": d.Subject = val; break;
                    case "in-reply-to": d.InReplyTo = val; break;
                    case "references": d.References = val; break;
                    case "faq": d.Faq = val.Length == 0 ? null : val; break;   // memo for the reviewer, never put into the mail
                    case "drafted": d.DraftedAt = val; break;
                    case "sent": d.SentAt = val; break;
                    default: Console.WriteLine("  [warn] 不明なヘッダーを無視します: " + name); break;
                }
            }
            var body = lines.Skip(i).ToList();
            TrimBlankTail(body);
            while (body.Count > 0)
            {
                string last = body[body.Count - 1];
                if (last.StartsWith("Drafted: ", StringComparison.Ordinal)) d.DraftedAt ??= last.Substring(9).Trim();
                else if (last.StartsWith("Sent: ", StringComparison.Ordinal)) d.SentAt ??= last.Substring(6).Trim();
                else break;
                body.RemoveAt(body.Count - 1);
                TrimBlankTail(body);
            }
            d.Body = string.Join("\n", body);
            return d;
        }

        private static void TrimBlankTail(List<string> lines)
        {
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        }

        private static MimeMessage BuildMessage(MailConfig cfg, Draft d)
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(cfg.fromName ?? "", cfg.user));
            msg.To.AddRange(ParseAddresses("To", d.To));
            if (!string.IsNullOrWhiteSpace(d.Cc)) msg.Cc.AddRange(ParseAddresses("Cc", d.Cc));
            msg.Subject = d.Subject ?? "";
            msg.Date = DateTimeOffset.Now;
            int at = cfg.user.IndexOf('@');
            msg.MessageId = MimeUtils.GenerateMessageId(at > 0 ? cfg.user.Substring(at + 1) : "pocketroles.local");
            if (!string.IsNullOrWhiteSpace(d.InReplyTo)) msg.InReplyTo = d.InReplyTo.Trim().Trim('<', '>');
            if (!string.IsNullOrWhiteSpace(d.References))
                foreach (var r in d.References.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    msg.References.Add(r.Trim('<', '>'));
            var body = new TextPart("plain");
            body.SetText(Encoding.UTF8, d.Body);
            msg.Body = body;
            return msg;
        }

        private static InternetAddressList ParseAddresses(string header, string value)
        {
            InternetAddressList list;
            if (!InternetAddressList.TryParse(value, out list) || !list.Mailboxes.Any(m => !string.IsNullOrEmpty(m.Address) && m.Address.Contains("@")))
                throw new DraftException(header + ": のメールアドレスが読み取れません: " + value);
            return list;
        }

        // ------------------------------------------------------------------ check-sent ------------------------------------------------------------------

        private sealed class SentScan
        {
            public int Checked, Public, LanOnly, Unknown, None;
            public DateTimeOffset? FirstPublic, LastPublic, FirstLan, LastLan, FirstUnknown, LastUnknown;
        }

        /// <summary>
        /// Why the old --send's copies may not all be in this mailbox: its smtpUser was another account, or its smtpHost was
        /// not Gmail (Gmail keeps the submitted copy — the one with the Received header — only in the account that sent it).
        /// Null = nothing to worry about (keys absent, empty, the same address in another case, or a Gmail host).
        /// The values are compared only, never printed.
        /// </summary>
        internal static string OldSendElsewhere(MailConfig cfg)
        {
            if (cfg == null) return null;
            string su = (cfg.smtpUser ?? "").Trim();
            if (su.Length > 0 && !string.Equals(su, (cfg.user ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                return "前の版の設定（report-mail.json の smtpUser）が user とちがうアカウントです。前の版はそのアカウントで送っていたので、その控えはこのメールボックスにはありません。";
            string sh = (cfg.smtpHost ?? "").Trim().TrimEnd('.').ToLowerInvariant();
            if (sh.Length > 0 && !(IsHostUnder(sh, "gmail.com") || IsHostUnder(sh, "google.com") || IsHostUnder(sh, "googlemail.com")))
                return "前の版の設定（report-mail.json の smtpHost）が Gmail ではないサーバーです。前の版はそこから送っていたので、Gmail の送信済みだけでは分かりません。";
            return null;
        }

        /// <summary>
        /// READ-ONLY: counts the replies the old --send submitted over SMTP whose Received headers carry this PC's address.
        /// Folders are opened read-only (EXAMINE) and only the Received / From header fields and the internal date are fetched
        /// (BODY.PEEK, so nothing is marked as read). Prints counts and dates only — never an address, host, recipient,
        /// subject or header text, not even in an error (only the exception type name). 10 = at least one public address.
        /// Fails closed: a mail in Gmail's Sent folder that cannot be judged (no Received header, unreadable headers, an error)
        /// makes the result "not everything could be checked" (1), never "none found" (0).
        /// </summary>
        private static int CheckSent(MailConfig cfg, string password)
        {
            Console.WriteLine("前の版（--send）で送った返事に、この PC の IP アドレスが入っているかを調べます（読むだけです。メールは何も変えません）。");
            bool incomplete = false;
            string elsewhere = OldSendElsewhere(cfg);
            if (elsewhere != null)
            {
                Console.WriteLine(elsewhere);
                Console.WriteLine("  このメールボックスの分だけ調べます（全部は調べられません）。");
                incomplete = true;
            }
            using var client = new ImapClient();
            try
            {
                client.Connect(cfg.imapHost, cfg.imapPort, SecureSocketOptions.SslOnConnect);
                client.Authenticate(cfg.user, password);
            }
            catch (AuthenticationException) { throw; }
            catch (Exception e)
            {
                Console.WriteLine("Gmail につなげませんでした（" + e.GetType().Name + "）。");
                return 1;
            }

            var folders = new List<(string label, IMailFolder folder)>();
            IMailFolder sent = null;
            try { sent = FindSpecialFolder(client, SpecialFolder.Sent, FolderAttributes.Sent); }
            catch (Exception e) { Console.WriteLine("送信済みフォルダを探せませんでした（" + e.GetType().Name + "）。"); }
            if (sent != null) folders.Add(("Gmail の送信済み", sent));
            else
            {
                Console.WriteLine("Gmail の送信済みフォルダが見つかりませんでした（フォルダの名前で当てずっぽうに探すことはしません）。");
                Console.WriteLine("  ブラウザの Gmail の 設定 →「ラベル」→「送信済み」の「IMAP で表示」にチェックが入っているか確かめてください。");
                incomplete = true;
            }

            if (!string.IsNullOrWhiteSpace(cfg.sentFolder))
            {
                IMailFolder mine = null;
                try { mine = FindFolder(client.GetFolder(client.PersonalNamespaces[0]), cfg.sentFolder); }
                catch (Exception e) { Console.WriteLine(cfg.sentFolder + " を探せませんでした（" + e.GetType().Name + "）。"); }
                if (mine == null) Console.WriteLine(cfg.sentFolder + " はありません（前の版で返事を送っていなければ、ないのがふつうです）。");
                else if (sent == null || !string.Equals(mine.FullName, sent.FullName, StringComparison.Ordinal)) folders.Add((cfg.sentFolder + "（前の版のツールの控え）", mine));
            }

            int totalPublic = 0, totalLan = 0;
            foreach (var (label, folder) in folders)
            {
                Console.WriteLine("[" + label + "]");
                SentScan r;
                try { r = ScanSentFolder(folder, cfg.user); }
                catch (AuthenticationException) { throw; }
                catch (Exception e)
                {
                    Console.WriteLine("  調べられませんでした（" + e.GetType().Name + "）。");
                    incomplete = true;
                    continue;
                }
                bool isGmailSent = ReferenceEquals(folder, sent);
                Console.WriteLine("  調べたメール（自分が送ったもの）: " + r.Checked + " 通");
                Console.WriteLine("  公開の IP アドレス（家の回線の番号）が入っている: " + r.Public + " 通");
                Console.WriteLine("  LAN の IP アドレス（家の中だけの番号）だけが入っている: " + r.LanOnly + " 通");
                Console.WriteLine("  判定できない（Received の行がない・読めない）: " + r.Unknown + " 通");
                Console.WriteLine("  入っていない: " + r.None + " 通");
                if (r.Public > 0) Console.WriteLine("  公開の IP アドレスが入っているメールの日付: 最初 " + Day(r.FirstPublic) + " / 最後 " + Day(r.LastPublic));
                if (r.LanOnly > 0) Console.WriteLine("  LAN の IP アドレスだけのメールの日付: 最初 " + Day(r.FirstLan) + " / 最後 " + Day(r.LastLan));
                if (r.Unknown > 0) Console.WriteLine("  判定できないメールの日付: 最初 " + Day(r.FirstUnknown) + " / 最後 " + Day(r.LastUnknown));
                if (isGmailSent && r.Unknown > 0)
                {
                    // a mail really sent from this account always carries at least one Received line: never read "no line" as "no address"
                    Console.WriteLine("  Gmail の送信済みにあるのに Received の行がない・読めないメールは、入っているかどうか分かりません。");
                    Console.WriteLine("  ブラウザの Gmail でその日付のメールを開き →「︙」→「メッセージのソースを表示」で、Received: の行を自分で見てください。");
                    incomplete = true;
                }
                if (!isGmailSent && r.Checked > 0)
                    Console.WriteLine("  （ここは前の版のツールが送ったあとに、Gmail を通る前の形で保存した控えです。Received の行がないことが多いですが、" +
                                      "Gmail が送信済みと同じメールとしてまとめている時は、送信済みと同じ数が出ることもあります。見るべきは Gmail の送信済みのほうです）");
                totalPublic += r.Public;
                totalLan += r.LanOnly;
            }
            try { client.Disconnect(true); } catch { }

            if (totalPublic > 0)
            {
                Console.WriteLine("結果: 公開の IP アドレス（家の回線の番号）が入った返事がありました。受け取った人は、メールの詳しい情報からその番号を見られます。");
                Console.WriteLine("      いまの版は Gmail の下書きを作るだけなので、これから送る返事には入りません" +
                                  "（送信はブラウザの Gmail からだけ。メールソフトから送ると入ります）。");
                if (incomplete) Console.WriteLine("      （ほかに、調べられなかったものもあります。上の表示を見てください）");
                return 10;
            }
            if (incomplete)
            {
                Console.WriteLine("結果: 全部は調べられませんでした。調べられた分には、公開の IP アドレスが入った返事はありませんでした" +
                                  "（「なかった」とは言えません。上の表示を見てください）。");
                return 1;
            }
            Console.WriteLine(totalLan > 0
                ? "結果: 公開の IP アドレスが入った返事はありませんでした（LAN の番号だけのものはありました。家の外からは使えない番号です）。"
                : "結果: 公開の IP アドレスが入った返事はありませんでした。");
            return 0;
        }

        private static string Day(DateTimeOffset? d) => d.HasValue ? d.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "（不明）";

        private static SentScan ScanSentFolder(IMailFolder folder, string user)
        {
            var r = new SentScan();
            folder.Open(FolderAccess.ReadOnly);
            var uids = folder.Search(SearchQuery.FromContains(user));
            if (uids.Count == 0) return r;
            var request = new FetchRequest(MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate, new[] { HeaderId.Received, HeaderId.From });
            for (int i = 0; i < uids.Count; i += 500)
            {
                var chunk = uids.Skip(i).Take(500).ToList();
                foreach (var s in folder.Fetch(chunk, request))
                {
                    var when = s.InternalDate;
                    ClientIp kind;
                    if (s.Headers == null) kind = ClientIp.Unknown;   // SEARCH FROM matched it but the headers did not come: cannot tell
                    else if (!IsFrom(s.Headers[HeaderId.From], user)) continue;   // SEARCH FROM is a substring match
                    else kind = ClassifyMessage(s.Headers.Where(h => h.Id == HeaderId.Received).Select(h => h.Value));
                    r.Checked++;
                    if (kind == ClientIp.Public) { r.Public++; Span(ref r.FirstPublic, ref r.LastPublic, when); }
                    else if (kind == ClientIp.Unknown) { r.Unknown++; Span(ref r.FirstUnknown, ref r.LastUnknown, when); }
                    else if (kind == ClientIp.Lan) { r.LanOnly++; Span(ref r.FirstLan, ref r.LastLan, when); }
                    else r.None++;
                }
            }
            return r;
        }

        private static void Span(ref DateTimeOffset? first, ref DateTimeOffset? last, DateTimeOffset? when)
        {
            if (!when.HasValue) return;
            if (!first.HasValue || when.Value < first.Value) first = when;
            if (!last.HasValue || when.Value > last.Value) last = when;
        }

        /// <summary>True when one of the addresses in the From value is exactly <paramref name="user"/> (case-insensitive).</summary>
        private static bool IsFrom(string fromValue, string user)
        {
            if (string.IsNullOrEmpty(fromValue) || string.IsNullOrEmpty(user)) return false;
            foreach (Match m in Regex.Matches(fromValue, @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+"))
                if (string.Equals(m.Value.TrimEnd('.'), user.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ------------------------------------------------------------------ Received classifier ------------------------------------------------------------------

        private static readonly HashSet<string> ReceivedKeywords = new HashSet<string>(StringComparer.Ordinal) { "from", "by", "via", "with", "id", "for" };

        /// <summary>
        /// The worst result over all Received headers of one message (Public &gt; Unknown &gt; Lan &gt; None). No Received header at
        /// all, or an error while reading them, is Unknown (a mail really sent from the account always has one: never "none").
        /// </summary>
        internal static ClientIp ClassifyMessage(IEnumerable<string> receivedValues)
        {
            if (receivedValues == null) return ClientIp.Unknown;
            var worst = ClientIp.None;
            int count = 0;
            try
            {
                foreach (var v in receivedValues)
                {
                    count++;
                    var k = ClassifyReceived(v);
                    if (k > worst) worst = k;
                }
            }
            catch (Exception)
            {
                return worst == ClientIp.Public ? ClientIp.Public : ClientIp.Unknown;
            }
            return count == 0 ? ClientIp.Unknown : worst;
        }

        /// <summary>
        /// One Received header. It "has the client IP" when its from-clause holds an IP literal ([a.b.c.d] or [IPv6:…]), its
        /// by-clause host is a Google mail host (ends with google.com; Gmail's SMTP submission host is smtp.gmail.com, so
        /// gmail.com / googlemail.com count too), its with-clause is SMTP-based (ESMTPSA, ESMTPS, ESMTP, SMTP, SMTPS … — not
        /// HTTP / HTTPREST, which is the web and the app), and neither the from-host nor its reverse DNS name is a *.google.com
        /// host (Google-internal hops). The address is then Lan or Public (see <see cref="IsLan"/>). Never throws: an
        /// unexpected error is Unknown (malformed input that parses without an error stays None).
        /// </summary>
        internal static ClientIp ClassifyReceived(string value)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value)) return ClientIp.None;
                if (value.Length > 8192) value = value.Substring(0, 8192);
                string s = Regex.Replace(value, @"\r\n|\r|\n", " ").Replace('\t', ' ');   // unfold
                var clauses = SplitReceived(s);
                if (!clauses.TryGetValue("from", out var from) || !clauses.TryGetValue("by", out var by) || !clauses.TryGetValue("with", out var with)) return ClientIp.None;
                string byHost = FirstWord(by).TrimEnd('.').ToLowerInvariant();
                if (!(IsHostUnder(byHost, "google.com") || IsHostUnder(byHost, "gmail.com") || IsHostUnder(byHost, "googlemail.com"))) return ClientIp.None;
                if (!Regex.IsMatch(FirstWord(with), @"^(UTF8)?E?SMTP", RegexOptions.IgnoreCase)) return ClientIp.None;
                foreach (Match m in Regex.Matches(from, @"[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)+\.?"))
                    if (IsHostUnder(m.Value.TrimEnd('.').ToLowerInvariant(), "google.com")) return ClientIp.None;
                var result = ClientIp.None;
                foreach (Match m in Regex.Matches(from, @"\[(?:IPv6:)?([0-9A-Fa-f:.]+)\]", RegexOptions.IgnoreCase))
                {
                    if (!TryParseIp(m.Groups[1].Value, out var ip)) continue;
                    var k = IsLan(ip) ? ClientIp.Lan : ClientIp.Public;
                    if (k > result) result = k;
                }
                return result;
            }
            catch (Exception)
            {
                return ClientIp.Unknown;
            }
        }

        private static bool IsHostUnder(string host, string domain) => host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);

        /// <summary>
        /// Splits an unfolded Received value into its clauses (from / by / via / with / id / for → the words after the keyword,
        /// comments included; the first occurrence of each keyword wins). Comments "( … )" (nested, with \-escapes) and
        /// "[ … ]" literals are single words; a top-level ";" ends the clauses (the date follows). Unbalanced input is tolerated.
        /// </summary>
        private static Dictionary<string, string> SplitReceived(string s)
        {
            var words = new List<string>();
            var sb = new StringBuilder();
            void Flush() { if (sb.Length > 0) { words.Add(sb.ToString()); sb.Clear(); } }
            int depth = 0;
            bool bracket = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (depth > 0)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < s.Length) { sb.Append(s[++i]); continue; }
                    if (c == '(') depth++;
                    else if (c == ')' && --depth == 0) Flush();
                    continue;
                }
                if (bracket)
                {
                    sb.Append(c);
                    if (c == ']') { bracket = false; }
                    continue;
                }
                if (c == '(') { Flush(); depth = 1; sb.Append(c); continue; }
                if (c == '[') { bracket = true; sb.Append(c); continue; }
                if (c == ';') break;
                if (char.IsWhiteSpace(c)) { Flush(); continue; }
                sb.Append(c);
            }
            Flush();

            var map = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
            StringBuilder current = null;
            foreach (var w in words)
            {
                string lw = w.ToLowerInvariant();
                if (ReceivedKeywords.Contains(lw))
                {
                    current = map.ContainsKey(lw) ? null : (map[lw] = new StringBuilder());
                    continue;
                }
                current?.Append(w).Append(' ');
            }
            return map.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        }

        /// <summary>The first word of a clause that is not a comment.</summary>
        private static string FirstWord(string clause)
        {
            foreach (var w in (clause ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (!w.StartsWith("(", StringComparison.Ordinal)) return w;
            return "";
        }

        private static bool TryParseIp(string text, out IPAddress ip)
        {
            ip = null;
            if (string.IsNullOrEmpty(text)) return false;
            if (Regex.IsMatch(text, @"^\d{1,3}(\.\d{1,3}){3}$"))
                return IPAddress.TryParse(text, out ip) && ip.AddressFamily == AddressFamily.InterNetwork;
            if (text.IndexOf(':') >= 0)
                return IPAddress.TryParse(text, out ip) && ip.AddressFamily == AddressFamily.InterNetworkV6;
            return false;
        }

        /// <summary>LAN-only: 10/8, 172.16/12, 192.168/16, 127/8, 169.254/16, ::1, fe80::/10, fc00::/7 (IPv4-mapped IPv6 as IPv4). Everything else (100.64/10 too) is public.</summary>
        internal static bool IsLan(IPAddress ip)
        {
            if (ip == null) return false;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            var b = ip.GetAddressBytes();
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return b[0] == 10 || (b[0] == 172 && (b[1] & 0xF0) == 16) || (b[0] == 192 && b[1] == 168) || b[0] == 127 || (b[0] == 169 && b[1] == 254);
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                return IPAddress.IPv6Loopback.Equals(ip) || (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) || (b[0] & 0xFE) == 0xFC;
            return false;
        }

        // ------------------------------------------------------------------ self-test ------------------------------------------------------------------

        /// <summary>Hidden --self-test: the Received classifier, the LAN ranges, the draft markers and an old config, on built-in synthetic data. No network.</summary>
        private static int SelfTest()
        {
            int n = 0, fail = 0;
            void Check(string name, Func<bool> test)
            {
                n++;
                bool ok;
                try { ok = test(); } catch (Exception e) { ok = false; name += "（例外 " + e.GetType().Name + "）"; }
                if (!ok) fail++;
                Console.WriteLine((ok ? "PASS" : "FAIL") + "  " + n.ToString("00") + ". " + name);
            }
            void Expect(string name, string header, ClientIp expect)
            {
                Check(name + " → " + expect, () => ClassifyReceived(header) == expect);
            }

            // documentation-range addresses only (192.0.2.x, 198.51.100.x, 203.0.113.x, 2001:db8::), plus the LAN ranges
            Expect("SMTP 送信（ESMTPSA）・公開 IPv4",
                "from [192.168.1.23] (client.example.net. [203.0.113.45])\r\n        by smtp.gmail.com with ESMTPSA id 5b1f17b1804b1-46e1b2c3d4esm1234567f8f.12.2026.09.20.10.11.12\r\n        for <someone@example.com>\r\n        (version=TLS1_3 cipher=TLS_AES_256_GCM_SHA384 bits=256/256);\r\n        Sat, 20 Sep 2026 10:11:12 -0700 (PDT)",
                ClientIp.Public);
            Expect("SMTP 送信（ESMTPSA）・公開 IPv6",
                "from [IPv6:2001:db8:85a3::8a2e:370:7334] (v6.client.example.net. [2001:db8:85a3::8a2e:370:7334])\r\n        by smtp.gmail.com with ESMTPSA id d9443c01a7336-29a1b2c3d4esm1234567b3a.10.2026.09.20.10.11.12\r\n        for <someone@example.com>\r\n        (version=TLS1_3 cipher=TLS_AES_256_GCM_SHA384 bits=256/256);\r\n        Sat, 20 Sep 2026 10:11:12 -0700 (PDT)",
                ClientIp.Public);
            Expect("SMTP 送信・192.168.x.x だけ",
                "from [192.168.0.10] (unknown [192.168.0.10])\r\n        by smtp.gmail.com with ESMTPSA id 2adb3069b0e04-5a1b2c3d4e5sm123456e87.3.2026.09.20.10.11.12;\r\n        Sat, 20 Sep 2026 10:11:12 -0700 (PDT)",
                ClientIp.Lan);
            Expect("ブラウザの Gmail から送信（with HTTP）",
                "by 2002:a05:6a10:8c0f:b0:5a1:2b3c:4d5e with HTTP; Sat, 20 Sep 2026 10:11:12 -0700 (PDT)",
                ClientIp.None);
            Expect("スマホの Gmail アプリから送信（with HTTPREST）",
                "from 2002:a05:6a10:8c0f::1 by 2002:a05:6a10:8c0f:b0:5a1:2b3c:4d5e with HTTPREST; Sat, 20 Sep 2026 10:11:12 -0700 (PDT)",
                ClientIp.None);
            Expect("google.com の中の中継（mail-sor-f41.google.com → mx.google.com with SMTPS）",
                "from mail-sor-f41.google.com (mail-sor-f41.google.com. [198.51.100.41])\r\n        by mx.google.com with SMTPS id a640c23a62f3a-b2c3d4e5f6sor1234567866b.12.2026.09.20.10.11.12\r\n        for <someone@example.com>\r\n        (Google Transport Security);\r\n        Sat, 20 Sep 2026 10:11:12 -0700 (PDT)",
                ClientIp.None);
            Expect("Google ではないサーバー（by mail.example.org）",
                "from client.example.net (client.example.net. [203.0.113.60]) by mail.example.org with ESMTPSA id 12345; Sat, 20 Sep 2026 10:11:12 +0900",
                ClientIp.None);
            Expect("with HTTP は SMTP ではない（from に IP があっても）",
                "from [203.0.113.9] by smtp.gmail.com with HTTP; Sat, 20 Sep 2026 10:11:12 -0700 (PDT)",
                ClientIp.None);
            Expect("大文字・小文字がまざっている",
                "FROM [192.0.2.44] (Client.Example.NET. [192.0.2.44]) BY SMTP.GMAIL.COM WITH esmtpsa ID x1; Sat, 20 Sep 2026 10:11:12 -0700",
                ClientIp.Public);
            Expect("折り返したヘッダー（タブ・改行だらけ、LAN と公開の両方）",
                "from [10.0.0.8]\r\n\t(client.example.net.\r\n\t [198.51.100.23])\r\n        by\r\n\tsmtp.gmail.com\r\n\twith\r\n\tESMTPSA id abc;\r\n\tSat, 20 Sep 2026 10:11:12 -0700 (PDT)",
                ClientIp.Public);
            Expect("IPv4 を埋め込んだ IPv6（::ffff:192.168.x.x）は LAN",
                "from [IPv6:::ffff:192.168.1.5] by smtp.gmail.com with ESMTPSA id x; Sat, 20 Sep 2026 10:11:12 -0700",
                ClientIp.Lan);
            Expect("日付（; の後）の中の [ ] は数えない",
                "from [192.168.3.3] by smtp.gmail.com with ESMTPSA id x; Sat, 20 Sep 2026 [203.0.113.7]",
                ClientIp.Lan);
            Expect("こわれたヘッダー: null", null, ClientIp.None);
            Expect("こわれたヘッダー: 空", "", ClientIp.None);
            Expect("こわれたヘッダー: from だけ", "from", ClientIp.None);
            Expect("こわれたヘッダー: 閉じないかっこ", "from ((((( [192.0.2.1 by smtp.gmail.com with ESMTPSA", ClientIp.None);
            Expect("こわれたヘッダー: 閉じない [", "from [192.0.2.1 by smtp.gmail.com with ESMTPSA id x", ClientIp.None);
            Expect("こわれたヘッダー: ありえない IP（999.1.2.3・300.2.3.4・zzzz::1）",
                "from [999.1.2.3] (bad.example.net [300.2.3.4]) ([IPv6:zzzz::1]) by smtp.gmail.com with ESMTPSA id x; Sat, 20 Sep 2026",
                ClientIp.None);
            Expect("こわれたヘッダー: 制御文字・全角空白", "from \u0000\uFFFF [\u3000] by \t\t with ; ; ;", ClientIp.None);
            Expect("こわれたヘッダー: とても長い（かっこ 20000 個）", "from " + new string('(', 20000) + " by smtp.gmail.com with ESMTPSA", ClientIp.None);
            Expect("こわれたヘッダー: 余分な ) と \\ だらけ", "from ))) \\\\\\ [192.0.2.9] (\\)) by smtp.gmail.com with ESMTPSA", ClientIp.Public);
            Expect("こわれたヘッダー: 閉じないかっこが by までのみこむ", "from [192.0.2.9] ((\\)) by smtp.gmail.com with ESMTPSA", ClientIp.None);

            Check("1 通の中でいちばん悪い結果（HTTP・Google の中継・LAN → LAN）", () => ClassifyMessage(new[]
            {
                "by 2002:a05:6a10:8c0f:b0:5a1:2b3c:4d5e with HTTP; Sat, 20 Sep 2026 10:11:12 -0700 (PDT)",
                "from mail-sor-f41.google.com (mail-sor-f41.google.com. [198.51.100.41]) by mx.google.com with SMTPS id x; Sat, 20 Sep 2026",
                "from [192.168.0.10] (unknown [192.168.0.10]) by smtp.gmail.com with ESMTPSA id y; Sat, 20 Sep 2026",
            }) == ClientIp.Lan);
            Check("1 通の中でいちばん悪い結果（LAN と公開 → 公開）", () => ClassifyMessage(new[]
            {
                "from [192.168.0.10] (unknown [192.168.0.10]) by smtp.gmail.com with ESMTPSA id y; Sat, 20 Sep 2026",
                "from [192.0.2.10] (client.example.net. [192.0.2.10]) by smtp.gmail.com with ESMTPSA id z; Sat, 20 Sep 2026",
                null,
            }) == ClientIp.Public);
            Check("Received の行が 1 つもないメールは「判定できない」（「入っていない」にしない）", () =>
                ClassifyMessage(new string[0]) == ClientIp.Unknown && ClassifyMessage(null) == ClientIp.Unknown);
            IEnumerable<string> ThenFail(string first) { yield return first; throw new InvalidOperationException("self-test"); }
            Check("Received を読む途中のエラーは「判定できない」（LAN の後でも。先に公開が見つかっていれば公開）", () =>
                ClassifyMessage(ThenFail("from [192.168.0.10] by smtp.gmail.com with ESMTPSA id y; Sat, 20 Sep 2026")) == ClientIp.Unknown
                && ClassifyMessage(ThenFail("by 2002:a05:6a10:8c0f:b0:5a1:2b3c:4d5e with HTTP; Sat, 20 Sep 2026")) == ClientIp.Unknown
                && ClassifyMessage(ThenFail("from [192.0.2.10] by smtp.gmail.com with ESMTPSA id z; Sat, 20 Sep 2026")) == ClientIp.Public);

            var lan = new[] { "10.1.2.3", "172.16.0.1", "172.31.255.255", "192.168.0.1", "127.0.0.1", "169.254.1.1", "::1", "fe80::1", "febf::1", "fc00::1", "fd12:3456::1", "::ffff:10.0.0.1" };
            var pub = new[] { "172.15.255.255", "172.32.0.1", "192.169.0.1", "100.64.0.1", "192.0.2.1", "198.51.100.1", "203.0.113.1", "fec0::1", "2001:db8::1" };
            Check("LAN の範囲（10/8・172.16/12・192.168/16・127/8・169.254/16・::1・fe80::/10・fc00::/7）", () => lan.All(a => IsLan(IPAddress.Parse(a))));
            Check("LAN ではない番号（100.64/10 もふくむ）は公開", () => pub.All(a => !IsLan(IPAddress.Parse(a))));

            Check("From が自分のアドレスだけ一致（よく似た別アドレスは別）", () =>
                IsFrom("PocketRoles サポート <support@example.com>", "support@example.com")
                && IsFrom("SUPPORT@EXAMPLE.COM", "support@example.com")
                && !IsFrom("x <xsupport@example.com>", "support@example.com")
                && !IsFrom(null, "support@example.com"));

            Check("下書き: 末尾の Drafted: 行を読み、本文から外す・2 回目は断る", () =>
            {
                var d = ParseDraftText("To: someone@example.com\r\nSubject: Re: test\r\n\r\n本文です。\r\n\r\nDrafted: 2026-09-22 10:00:00\r\n");
                if (d.DraftedAt != "2026-09-22 10:00:00" || d.Body != "本文です。" || d.SentAt != null) return false;
                try { RefuseIfDone(d); return false; } catch (DraftException) { return true; }
            });
            Check("下書き: 前の版の Sent: 行でも断る", () =>
            {
                var d = ParseDraftText("To: someone@example.com\n\n本文\n\nSent: 2026-09-01 09:00:00\n");
                if (d.SentAt == null || d.Body != "本文") return false;
                try { RefuseIfDone(d); return false; } catch (DraftException) { return true; }
            });
            Check("下書き: 印のない下書きは通す", () =>
            {
                var d = ParseDraftText("To: someone@example.com\nFAQ: Q01\n# memo\n\n本文\n");
                RefuseIfDone(d);
                return d.DraftedAt == null && d.SentAt == null && d.Faq == "Q01" && d.Body == "本文";
            });
            Check("前の版の report-mail.json（smtpHost / smtpPort / smtpUser つき）もそのまま読める", () =>
            {
                const string old = "{\n  // old file\n  \"user\": \"support@example.com\",\n  \"appPassword\": \"\",\n  \"smtpHost\": \"smtp.gmail.com\",\n  \"smtpPort\": 587,\n  \"smtpUser\": \"\",\n  \"sentFolder\": \"PocketRoles/Sent\",\n}";
                var c = JsonSerializer.Deserialize<MailConfig>(old, ConfigJson);
                return c != null && c.user == "support@example.com" && c.sentFolder == "PocketRoles/Sent" && c.imapHost == "imap.gmail.com"
                    && c.smtpHost == "smtp.gmail.com" && c.smtpUser == "" && OldSendElsewhere(c) == null;
            });
            Check("前の版の smtpUser が別のアカウント → 全部は調べられない（大文字ちがいの同じアドレス・空・なしは OK）", () =>
                OldSendElsewhere(new MailConfig { user = "support@example.com", smtpUser = "other@example.com" }) != null
                && OldSendElsewhere(new MailConfig { user = "support@example.com", smtpUser = "Support@Example.com" }) == null
                && OldSendElsewhere(new MailConfig { user = "support@example.com", smtpUser = "" }) == null
                && OldSendElsewhere(new MailConfig { user = "support@example.com" }) == null);
            Check("前の版の smtpHost が Gmail ではない → 全部は調べられない（smtp.gmail.com・googlemail.com・google.com の下は OK）", () =>
                OldSendElsewhere(new MailConfig { user = "support@example.com", smtpHost = "smtp.example.org" }) != null
                && OldSendElsewhere(new MailConfig { user = "support@example.com", smtpHost = "gmail.com.example.org" }) != null
                && OldSendElsewhere(new MailConfig { user = "support@example.com", smtpHost = "SMTP.Gmail.com." }) == null
                && OldSendElsewhere(new MailConfig { user = "support@example.com", smtpHost = "smtp.googlemail.com" }) == null
                && OldSendElsewhere(new MailConfig { user = "support@example.com", smtpHost = "smtp-relay.google.com" }) == null);

            Console.WriteLine(fail == 0 ? "self-test: すべて PASS（" + n + " 件）" : "self-test: FAIL " + fail + " 件 / " + n + " 件");
            return fail == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ helpers ------------------------------------------------------------------

        private static string FindConfig()
        {
            string exeDir = AppContext.BaseDirectory;
            var candidates = new List<string> { Path.Combine(exeDir, "report-mail.json") };
            // walk up from tools\ReportFetcher\bin\<Config>\<tfm>\ to the project root (depth varies with the target framework)
            var dir = new DirectoryInfo(exeDir);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                candidates.Add(Path.Combine(dir.FullName, "report-mail.json"));
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "report-mail.json"));
            foreach (var c in candidates) if (File.Exists(c)) return Path.GetFullPath(c);
            return null;
        }

        /// <summary>
        /// A special-use folder (Drafts / Sent): GetFolder(SpecialFolder) when the server has SPECIAL-USE or XLIST, otherwise
        /// (or when that finds nothing) the folder whose LIST attributes carry <paramref name="attribute"/>. Never guesses a
        /// (localized) folder name; null when there is none.
        /// </summary>
        private static IMailFolder FindSpecialFolder(ImapClient client, SpecialFolder kind, FolderAttributes attribute)
        {
            if ((client.Capabilities & (ImapCapabilities.SpecialUse | ImapCapabilities.XList)) != 0)
            {
                var f = client.GetFolder(kind);
                if (f != null) return f;
            }
            foreach (var ns in client.PersonalNamespaces)
                foreach (var f in client.GetFolders(ns, StatusItems.None, false))
                    if ((f.Attributes & attribute) != 0) return f;
            return null;
        }

        /// <summary>An existing folder by path (never created); null when a segment is missing.</summary>
        private static IMailFolder FindFolder(IMailFolder root, string path)
        {
            IMailFolder current = root;
            foreach (var seg in path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                IMailFolder next = null;
                try { next = current.GetSubfolders(false).FirstOrDefault(f => string.Equals(f.Name, seg, StringComparison.OrdinalIgnoreCase)); } catch { }
                if (next == null) return null;
                current = next;
            }
            return ReferenceEquals(current, root) ? null : current;
        }

        private static IMailFolder GetOrCreateFolder(IMailFolder root, string path)
        {
            IMailFolder current = root;
            foreach (var seg in path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                IMailFolder next = null;
                try { next = current.GetSubfolders(false).FirstOrDefault(f => string.Equals(f.Name, seg, StringComparison.OrdinalIgnoreCase)); } catch { }
                if (next == null) next = current.Create(seg, true);
                current = next;
            }
            return current;
        }

        private static bool IsSystemSender(string from, string[] skip)
        {
            if (string.IsNullOrEmpty(from)) return false;
            string f = from.ToLowerInvariant();
            foreach (var s in skip ?? new string[0])
            {
                string p = (s ?? "").Trim().ToLowerInvariant();
                if (p.Length == 0) continue;
                if (p.StartsWith("@") ? f.EndsWith(p) : (f == p || f.EndsWith("@" + p) || f.EndsWith("." + p))) return true;
            }
            return false;
        }

        private static string Angle(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            id = id.Trim();
            return id.StartsWith("<") ? id : "<" + id + ">";
        }

        private static string ShortHash(string s)
        {
            using var sha = SHA256.Create();
            var h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            return BitConverter.ToString(h, 0, 4).Replace("-", "").ToLowerInvariant();
        }

        private static string MaskAddress(string a)
        {
            int at = a.IndexOf('@');
            if (at <= 1) return "***";
            return a.Substring(0, 1) + "***" + a.Substring(at);
        }

        private static string SafeName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Length > 120 ? name.Substring(0, 120) : name;
        }

        /// <summary>Plain text from an HTML-only mail: drops style/script, keeps line breaks of br/p/div/li, decodes entities.</summary>
        private static string HtmlToText(string html)
        {
            if (html == null) return null;
            string s = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<!--.*?-->", "", RegexOptions.Singleline);
            s = Regex.Replace(s, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"</\s*(p|div|li|tr|h[1-6]|blockquote|pre|table|ul|ol)\s*>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<\s*li[^>]*>", "- ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<[^>]+>", "");
            s = WebUtility.HtmlDecode(s);
            s = s.Replace("\r\n", "\n").Replace('\r', '\n');
            s = Regex.Replace(s, @"[ \t ]+\n", "\n");
            s = Regex.Replace(s, @"\n{3,}", "\n\n");
            return s.Trim();
        }
    }
}
