// PocketRoles ReportFetcher — downloads bug-report / feature-request / question mails from the dedicated mailbox into reports\,
// and sends ONE approved reply from the same mailbox (--send).
// Usage:
//   ReportFetcher.dll [path\to\report-mail.json]
//       fetch: INBOX → reports\bugs\ / reports\requests\ / reports\questions\ (mails are moved to the "Processed" folder afterwards)
//   ReportFetcher.dll --send <draft.txt> [--dry-run] [path\to\report-mail.json]
//       send one reply. Draft = header lines (To: / Subject: / In-Reply-To: / References: …), a blank line, then the body.
//       --dry-run builds <draft>.eml next to the draft and prints it WITHOUT connecting to any server.
// Never prints the password.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;

namespace PocketRoles.Tools
{
    internal sealed class MailConfig
    {
        public string imapHost { get; set; } = "imap.gmail.com";
        public int imapPort { get; set; } = 993;
        public string user { get; set; } = "";
        public string appPassword { get; set; } = "";
        public string processedFolder { get; set; } = "PocketRoles/Processed";
        /// <summary>IMAP folder that receives a copy of every reply sent with --send (created when missing).</summary>
        public string sentFolder { get; set; } = "PocketRoles/Sent";
        public string outputDir { get; set; } = "reports";
        public string requestMarker { get; set; } = "+request";
        /// <summary>Plus-alias for questions ("+question" is always accepted as well).</summary>
        public string helpMarker { get; set; } = "+help";
        public int maxMails { get; set; } = 200;
        /// <summary>SMTP for --send. Same credentials as IMAP unless smtpUser is set.</summary>
        public string smtpHost { get; set; } = "smtp.gmail.com";
        public int smtpPort { get; set; } = 587;
        public string smtpUser { get; set; } = "";
        /// <summary>Display name used in the From: header of replies.</summary>
        public string fromName { get; set; } = "PocketRoles サポート";
        /// <summary>Senders (domain or address) whose mails are archived without import when they carry no attachment.</summary>
        public string[] skipSenders { get; set; } = new[] { "google.com", "accounts.google.com", "youtube.com", "mail-noreply.google.com", "no-reply@accounts.google.com" };
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
        /// <summary>Value of the trailing "Sent: …" line appended after a successful send (null = not sent yet).</summary>
        public string SentAt;
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

        private static int Main(string[] args)
        {
            string cfgArg = null, draftPath = null;
            bool dryRun = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--send")
                {
                    if (i + 1 >= args.Length) { Console.WriteLine("--send の後に下書きファイルを指定してください。"); PrintUsage(); return 2; }
                    draftPath = args[++i];
                }
                else if (a == "--dry-run") dryRun = true;
                else if (a == "--help" || a == "-h" || a == "/?") { PrintUsage(); return 0; }
                else if (a.StartsWith("--", StringComparison.Ordinal)) { Console.WriteLine("不明なオプション: " + a); PrintUsage(); return 2; }
                else cfgArg = a;
            }
            if (dryRun && draftPath == null) { Console.WriteLine("--dry-run は --send <下書き> と一緒に指定してください。"); PrintUsage(); return 2; }

            // When the output is captured by a script (bash / PowerShell) emit UTF-8 so Chinese and Japanese survive; keep the console code page otherwise.
            try { if (Console.IsOutputRedirected) Console.OutputEncoding = new UTF8Encoding(false); } catch { }

            try
            {
                string cfgPath = cfgArg ?? FindConfig();
                if (cfgPath == null || !File.Exists(cfgPath))
                {
                    Console.WriteLine("設定ファイル report-mail.json が見つかりません。");
                    return 2;
                }
                var cfg = JsonSerializer.Deserialize<MailConfig>(File.ReadAllText(cfgPath, Encoding.UTF8), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
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

                if (draftPath != null) return SendReply(cfg, password, draftPath, dryRun);
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
                Console.WriteLine("エラー: " + e.Message);
                return 1;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("使い方:");
            Console.WriteLine("  ReportFetcher.dll [report-mail.json]                         メールを取り込む → reports\\bugs, requests, questions");
            Console.WriteLine("  ReportFetcher.dll --send <下書き.txt> [--dry-run] [report-mail.json]  返信を 1 通送る（--dry-run は .eml を作るだけで接続しない）");
        }

        // ------------------------------------------------------------------ fetch ------------------------------------------------------------------

        private static int Fetch(MailConfig cfg, string password, string baseDir)
        {
            string outDir = Path.IsPathRooted(cfg.outputDir) ? cfg.outputDir : Path.Combine(baseDir, cfg.outputDir);
            Directory.CreateDirectory(Path.Combine(outDir, "bugs"));
            Directory.CreateDirectory(Path.Combine(outDir, "requests"));
            Directory.CreateDirectory(Path.Combine(outDir, "questions"));

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
            int bugs = 0, requests = 0, questions = 0, skipped = 0;
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
                if (kind == "requests") requests++; else if (kind == "questions") questions++; else bugs++;
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
            Console.WriteLine($"取り込み完了: 不具合 {bugs} 件 / 要望 {requests} 件 / 質問 {questions} 件 / システム通知など {skipped} 件は処理済みへ → {outDir}");
            return 0;
        }

        /// <summary>bugs / requests / questions. Aliases win; then request keywords in the subject; then question keywords (only without zip/log).</summary>
        private static string Classify(MimeMessage msg, MailConfig cfg, string body, List<(string name, MimeEntity entity)> files)
        {
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

        /// <summary>Creates reply.txt with the threading headers filled in and an EMPTY body (an empty body can never be sent by mistake).</summary>
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

        // ------------------------------------------------------------------ send ------------------------------------------------------------------

        private static int SendReply(MailConfig cfg, string password, string draftPath, bool dryRun)
        {
            string full = Path.GetFullPath(draftPath);
            if (!File.Exists(full)) throw new DraftException("下書きファイルがありません: " + full);
            var draft = ParseDraft(full);
            if (string.IsNullOrWhiteSpace(draft.To)) throw new DraftException("To: がありません（送信しません）。");
            if (string.IsNullOrWhiteSpace(draft.Body)) throw new DraftException("本文が空です（送信しません）。ヘッダーの後に空行を 1 行入れ、その下に本文を書いてください。");
            if (draft.SentAt != null && !dryRun) throw new DraftException("この下書きは送信済みです（Sent: " + draft.SentAt + "）。もう一度送るには末尾の Sent: 行を削除してください。");

            var msg = BuildMessage(cfg, draft);
            string toText = string.Join(", ", msg.To.Mailboxes.Select(m => m.Address).Concat(msg.Cc.Mailboxes.Select(m => m.Address)));
            if (toText.Length == 0) throw new DraftException("To: にメールアドレスが含まれていません: " + draft.To);

            if (dryRun)
            {
                string emlPath = Path.Combine(Path.GetDirectoryName(full), Path.GetFileNameWithoutExtension(full) + ".eml");
                // 998 = RFC 5322 line limit; with the default 78 MimeKit would switch the preview to quoted-printable and make it unreadable.
                msg.Prepare(EncodingConstraint.EightBit, 998);
                using (var fs = File.Create(emlPath)) msg.WriteTo(fs);
                Console.WriteLine("[dry-run] 送信も接続もしません。組み立てたメール → " + emlPath);
                Console.WriteLine("[dry-run] To: " + toText + " / Subject: " + msg.Subject + " / 本文 " + draft.Body.Length + " 文字" + (draft.Faq != null ? " / FAQ: " + draft.Faq : ""));
                if (draft.SentAt != null) Console.WriteLine("[dry-run] 注意: この下書きは送信済みです（Sent: " + draft.SentAt + "）。");
                Console.WriteLine("----- " + Path.GetFileName(emlPath) + " -----");
                Console.WriteLine(File.ReadAllText(emlPath, Encoding.UTF8));
                Console.WriteLine("----- end -----");
                return 0;
            }

            using (var smtp = new SmtpClient())
            {
                smtp.Connect(cfg.smtpHost, cfg.smtpPort, SecureSocketOptions.StartTls);
                smtp.Authenticate(string.IsNullOrWhiteSpace(cfg.smtpUser) ? cfg.user : cfg.smtpUser, password);
                smtp.Send(msg);
                smtp.Disconnect(true);
            }

            // Mark the draft so the same file cannot be sent twice.
            try
            {
                string existing = File.ReadAllText(full, Encoding.UTF8);
                string nl = existing.Contains("\r\n") ? "\r\n" : "\n";
                string tail = (existing.EndsWith("\n") ? "" : nl) + nl + "Sent: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + nl;
                File.AppendAllText(full, tail, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Console.WriteLine("  下書きへの Sent: 追記に失敗しました（メールは送信済みです）: " + e.Message);
            }

            // Keep a copy in our own IMAP folder (Gmail also stores it in [Gmail]/Sent Mail by itself).
            try
            {
                using var imap = new ImapClient();
                imap.Connect(cfg.imapHost, cfg.imapPort, SecureSocketOptions.SslOnConnect);
                imap.Authenticate(cfg.user, password);
                var personal = imap.GetFolder(imap.PersonalNamespaces[0]);
                var sent = GetOrCreateFolder(personal, cfg.sentFolder);
                sent.Append(msg, MessageFlags.Seen);
                imap.Disconnect(true);
                Console.WriteLine("  控えを " + cfg.sentFolder + " に保存しました。");
            }
            catch (Exception e)
            {
                Console.WriteLine("  控えの保存に失敗しました（Gmail の送信済みには自動で残ります）: " + e.Message);
            }

            Console.WriteLine("送信完了 → " + toText);
            return 0;
        }

        /// <summary>Header block (until the first blank line), then the body. "#" lines in the header block are comments. A trailing "Sent: …" line marks an already sent draft.</summary>
        private static Draft ParseDraft(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
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
                    case "faq": d.Faq = val.Length == 0 ? null : val; break;   // memo for the reviewer, never sent
                    case "sent": d.SentAt = val; break;
                    default: Console.WriteLine("  [warn] 不明なヘッダーを無視します: " + name); break;
                }
            }
            var body = lines.Skip(i).ToList();
            while (body.Count > 0 && body[body.Count - 1].Trim().Length == 0) body.RemoveAt(body.Count - 1);
            while (body.Count > 0 && body[body.Count - 1].StartsWith("Sent: ", StringComparison.Ordinal))
            {
                d.SentAt = body[body.Count - 1].Substring(6).Trim();
                body.RemoveAt(body.Count - 1);
                while (body.Count > 0 && body[body.Count - 1].Trim().Length == 0) body.RemoveAt(body.Count - 1);
            }
            d.Body = string.Join("\n", body);
            return d;
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
