# PocketRoles: Aegis の定義ファイル (aegis\definitions.txt) に署名します（偽の定義ファイルを読ませる攻撃を防ぐため。要望 2026-09-22）。
#   署名: RSA 3072 / SHA-256 / PKCS#1 v1.5。対象は「UTF-8 の BOM を除き、CRLF を LF にした」ファイルの中身です
#         （GitHub の raw は LF、Windows の作業コピーは CRLF なので、どちらでも同じ署名で通るようにしています）。
#   出力: aegis\definitions.txt.sig（1 行目 = 署名の base64、2 行目 = keyid=公開鍵の指紋の先頭 16 桁）
#   MOD（src\Net\AegisRules.cs）とトレイアプリ（aegis\Aegis.ps1・aegis\AegisBan.ps1）は、埋め込みの「信頼する公開鍵の一覧」
#   （TrustedKeys。「無効にした鍵」RevokedKeyIds は除く）のどれかで確かめられたファイルだけを使います。
# 使い方（Windows PowerShell 5.1。ふだんはデスクトップの「PocketRoles 署名の鍵」フォルダーの .cmd から）:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\sign-definitions.ps1 -Init      … 鍵を 1 回だけ作る（あれば上書きしません）
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\sign-definitions.ps1 -Protect   … 鍵にパスワードを付ける（鍵を守る.cmd）
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\sign-definitions.ps1 -Backup E:\ … 別のパスワードの控えを USB メモリなどに
#                                                    作る（鍵をUSBに控える.cmd。-Backup ask = USB メモリを選ぶ）
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\sign-definitions.ps1            … 署名して、埋め込みの公開鍵で確かめる
#                                                    （定義ファイルに署名する.cmd。どの鍵を使うかとパスワードを聞きます）
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\sign-definitions.ps1 -Verify    … 確かめるだけ（秘密鍵は使いません）
#   ... -SignRelease dist\SHA256SUMS.txt                   … リリースに付けるファイルの一覧（SHA256SUMS.txt）に署名する
#                                                    （build-release.ps1 が最後に自動で呼びます。鍵とパスワードは署名と同じように聞きます）
#   ... -VerifyRelease dist\SHA256SUMS.txt                 … その署名を確かめるだけ（秘密鍵は使いません）
#   ... -Unlock <番号> -UnlockOut <ファイル>                 … Aegis BAN 管理の本人確認（v0.5.5。BAN 管理が見える黒い画面で実行します。
#                                                    鍵とパスワードは署名と同じように聞きます。番号に署名した応答だけを書き、定義ファイルは変えません）
#   -KeyFolder <フォルダー>: パスワード付きの鍵を置く・探すフォルダー（既定: デスクトップの「PocketRoles 署名の鍵」）
#   definitions.txt を直したら必ず version= を上げてから署名し直し、definitions.txt と definitions.txt.sig を一緒にコミットして main に push します。
# 署名する前に確かめること（どれかに当たると署名しません）:
#   - version の行はちょうど 1 つで、「version=N」（N は 1 以上）の形だけ（空白・行末のコメント・大文字は不可。
#     MOD と新旧のトレイアプリが同じ行を同じ数として読むため）
#   - BOM を除き CRLF を LF にした後に CR（CR CR LF や単独の CR）や 2 つ目の BOM が残らない
#     （MOD とトレイアプリはこの形でキャッシュし、次の起動でもう一度同じ変換をしてから署名を確かめるため）
#   - 1 つの version には 1 つの中身だけ: 署名した版と中身（SHA-256）を台帳
#     %APPDATA%\PocketRoles\signing\signed-versions.txt に残し、署名済みの版を別の中身で署名し直すことや、
#     署名済みの最新より古い版で新しく署名することを断ります（同じ版の署名済みファイルが 2 つあると、
#     攻撃者が古い方を配って差し替えられるため）。秘密ではないので中身は普通のテキストです。
#     台帳のほかに、鍵の控えの横の台帳（-Backup が USB の控えの横にも置く）と、git の HEAD・main・origin/main に
#     コミットされた定義ファイルのうち .sig が信頼する鍵で確かめられるものも「署名済み」として数えます
#     （新しい PC で USB の控えから署名するときも、公開済みの版と違う中身で同じ版に署名しないため）。
#   - version= が MOD の下限（src\Net\AegisRules.cs の MinDefinitionsVersion）より古くない
#     （上限より新しいときは、次の MOD リリースの前に下限を上げるよう表示します。build-release.ps1 は一致を求めます）
#   - 使う鍵が、MOD と 2 つのトレイアプリの「信頼する公開鍵の一覧」に入っていて、「無効にした鍵」ではない
#   - v0.5.5: [rules] の行の終わりの条件（@mod<=0.5.5、@game>=2026.9.1）と [update] の minmod は、どの MOD でも同じに読める
#     形だけ（mod か game、<= >= == = < >、数字 3 つの版）。[rules] は 200 行、[update] は 50 行まで（MOD がその先を読まないため）。
#     minmod はこのリポジトリの版（csproj と PocketRolesPlugin.Version の低い方）より新しくできません。minmod を上げる時は
#     GitHub の最新リリースがそれ以上かを確かめ（つながらない時は署名しません）、下げる・消す時は画面で確認します。比べる元は
#     いつもこのリポジトリの aegis\definitions.txt（HEAD・main・origin/main のうち署名の合う最新）です（-File に関係なく）
# 秘密鍵:
#   -Init が作るのは %APPDATA%\PocketRoles\signing\definitions-private.xml（パスワードなし。この Windows ユーザーだけが読める
#   アクセス権）。-Protect がそれをパスワードで暗号化した「署名の鍵_PC用.prkey」を「PocketRoles 署名の鍵」フォルダーに作り、
#   読み戻して同じ鍵に戻ることを確かめてから、パスワードなしの方をランダムなバイトで上書きして消します。-Backup は、
#   PC 用とは別のパスワードで暗号化した「署名の鍵_USB控え.prkey」を USB メモリなどに作ります（同じパスワードは断ります）。
#   パスワード付きの鍵（テキスト）: PBKDF2-HMAC-SHA256（ランダムな 16 バイトの salt、600000 回）で 32 バイトの鍵を作り、
#   そこから AES-256 の鍵と MAC の鍵を HMAC-SHA256 で分け、鍵の XML を AES-256-CBC（ランダムな IV）で暗号化して、書式・各欄・
#   暗号文の全体に HMAC-SHA256 を付けます（encrypt-then-MAC。MAC を先に確かめるので、パスワード違いや書き換えは復号の前に
#   分かります）。パスワードは、このスクリプトを動かしている画面で本人が入力します（Read-Host -AsSecureString。画面に出さず、
#   ファイルにもログにも残しません）。決めるときは 2 回入力して確かめます。
#   鍵の場所の控え（秘密ではない。ランチャーが「この PC に署名の鍵がある」と分かるためにも使う）:
#   %APPDATA%\PocketRoles\signing\protected-keys.txt
#   リポジトリには入れません（.gitignore でも除外。鍵のフォルダーはリポジトリの中には作りません）。
#   このスクリプトは秘密鍵もパスワードも画面にもファイルにも出しません。
#   両方なくすと、新しい公開鍵を埋め込んだ MOD とトレイアプリを出すまで定義ファイルを更新できなくなります。
#   他人に渡ると偽の定義ファイルを作れてしまうので、漏れたら新しい鍵を作り、古い鍵の keyid を RevokedKeyIds に入れて出します。
# 複数の鍵（2026-09-22 要望「将来ほかの管理者の鍵も足せるように」）: .sig の keyid= で鍵を選びます。鍵を足すのも無効にするのも
#   リリースで行います（src\Net\AegisRules.cs・aegis\Aegis.ps1・aegis\AegisBan.ps1 の TrustedKeys / RevokedKeyIds を同じにする。
#   -Verify と build-release.ps1 は、3 つの一覧が同じか、各 keyid が鍵の本当の指紋か、無効にした鍵が残っていないかを確かめます）。
param(
    [switch]$Init,
    [switch]$Verify,
    [switch]$Protect,
    [string]$Backup = '',
    [switch]$Sign,
    [string]$File = '',
    [string]$KeyDir = '',
    [string]$KeyFolder = '',
    [string]$Unlock = '',
    [string]$UnlockOut = '',
    [switch]$UpdateHidden,        # v0.5.5: regenerate the hidden #h1 sections from the private list, then STOP (bump version= and sign afterward)
    [switch]$HashLegacy,          # v0.5.5 (F): once the required-update floor passes v0.5.5, absorb the remaining plain [tools]/[dlls]/[dllwords]/[ngwords]/[ngallow] lines into the private list and hash them (drops the plain lines), then STOP
    [string]$Sections = '',       # -HashLegacy only: the sections to convert, comma separated (既定: 5 つ全部)。ngwords,ngallow だけならフロアの確認はしません
    [switch]$BuiltinNg,           # v0.5.5: rewrite the MOD's built-in NG list (src\Chat\NgText.cs, hashed with its BuiltinSaltHex) from the private list, then STOP
    [string]$PrivateList = '',    # override the remembered private-list path (a plain file OUTSIDE the repository / AppData)
    [string]$SignRelease = '',    # v0.5.5: sign dist\SHA256SUMS.txt for a release (the ledger keeps one content per version)
    [string]$VerifyRelease = ''   # v0.5.5: verify a release's SHA256SUMS.txt against its .sig
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $File) { $File = Join-Path $root 'aegis\definitions.txt' }
if (-not $KeyDir) { $KeyDir = Join-Path $env:APPDATA 'PocketRoles\signing' }
$keyFolderName = 'PocketRoles 署名の鍵'
if (-not $KeyFolder) { $KeyFolder = Join-Path ([Environment]::GetFolderPath('Desktop')) $keyFolderName }
$doBackup = $PSBoundParameters.ContainsKey('Backup')
$doUnlock = $PSBoundParameters.ContainsKey('Unlock')
$doSignRelease = $PSBoundParameters.ContainsKey('SignRelease')
$doVerifyRelease = $PSBoundParameters.ContainsKey('VerifyRelease')
$privPath = Join-Path $KeyDir 'definitions-private.xml'
$pubPath = Join-Path $KeyDir 'definitions-public.xml'
$ledgerPath = Join-Path $KeyDir 'signed-versions.txt'
$relLedgerPath = Join-Path $KeyDir 'signed-releases.txt'
$locPath = Join-Path $KeyDir 'protected-keys.txt'
$pcKeyName = '署名の鍵_PC用.prkey'
$usbKeyName = '署名の鍵_USB控え.prkey'
$MinPasswordChars = 10
$sigPath = $File + '.sig'
$aegisPs1 = Join-Path $root 'aegis\Aegis.ps1'
$banPs1 = Join-Path $root 'aegis\AegisBan.ps1'
$rulesCs = Join-Path $root 'src\Net\AegisRules.cs'
$utf8 = New-Object Text.UTF8Encoding($false)
# the first lines of a .prkey file (not covered by the MAC: comment lines are skipped when it is read)
$keyFileHeader = "# PocketRoles の定義ファイル（aegis\definitions.txt）に署名する鍵です。パスワードで暗号化してあります。`r`n" +
    "# 人に渡したり、ネットやクラウドに上げたりしないでください。使い方は「PocketRoles 署名の鍵」フォルダーの 説明.txt にあります。`r`n"

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

public static class PrDefSign
{
    // the canonical form (the same as src\Net\DefinitionsSignature.cs and the Sig class of aegis\Aegis.ps1)
    public static byte[] Canonical(byte[] d)
    {
        int start = d.Length >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF ? 3 : 0;
        var o = new List<byte>(d.Length);
        for (int i = start; i < d.Length; i++)
        {
            if (d[i] == 0x0D && i + 1 < d.Length && d[i + 1] == 0x0A) continue;
            o.Add(d[i]);
        }
        return o.ToArray();
    }

    /// <summary>True when the canonical form still holds a CR (CR CR LF, a lone CR): it would not be canonical again.</summary>
    public static bool HasCr(byte[] c) { return Array.IndexOf(c, (byte)0x0D) >= 0; }

    /// <summary>True when the canonical form holds a UTF-8 BOM (U+FEFF) anywhere: a second leading BOM, or one inside.</summary>
    public static bool HasBom(byte[] c)
    {
        for (int i = 0; i + 2 < c.Length; i++) if (c[i] == 0xEF && c[i + 1] == 0xBB && c[i + 2] == 0xBF) return true;
        return false;
    }

    /// <summary>SHA-256 of the bytes, lower-case hex (the ledger of signed versions).</summary>
    public static string Sha256Hex(byte[] b)
    {
        using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
    }

    static byte[] Tlv(byte tag, byte[] body)
    {
        var o = new List<byte>();
        o.Add(tag);
        int n = body.Length;
        if (n < 0x80) o.Add((byte)n);
        else if (n < 0x100) { o.Add(0x81); o.Add((byte)n); }
        else { o.Add(0x82); o.Add((byte)(n >> 8)); o.Add((byte)(n & 0xFF)); }
        o.AddRange(body);
        return o.ToArray();
    }

    static byte[] Int(byte[] b)
    {
        int i = 0;
        while (i < b.Length - 1 && b[i] == 0) i++;
        var o = new List<byte>();
        if ((b[i] & 0x80) != 0) o.Add(0);
        for (int k = i; k < b.Length; k++) o.Add(b[k]);
        return Tlv(0x02, o.ToArray());
    }

    static byte[] Cat(params byte[][] parts)
    {
        var o = new List<byte>();
        foreach (var p in parts) o.AddRange(p);
        return o.ToArray();
    }

    /// <summary>SubjectPublicKeyInfo (DER) of the public part of a key.</summary>
    public static byte[] Spki(string keyXml)
    {
        using (var rsa = new RSACryptoServiceProvider())
        {
            rsa.PersistKeyInCsp = false;
            rsa.FromXmlString(keyXml);
            RSAParameters p = rsa.ExportParameters(false);
            byte[] rsaPub = Tlv(0x30, Cat(Int(p.Modulus), Int(p.Exponent)));
            byte[] algId = { 0x30, 0x0D, 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01, 0x05, 0x00 };
            byte[] bits = Tlv(0x03, Cat(new byte[] { 0 }, rsaPub));
            return Tlv(0x30, Cat(algId, bits));
        }
    }

    /// <summary>SHA-256 of the SubjectPublicKeyInfo, lower-case hex (the same as openssl's pubkey DER fingerprint).</summary>
    public static string Fingerprint(string keyXml)
    {
        using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Spki(keyXml))).Replace("-", "").ToLowerInvariant();
    }

    public static string PublicXml(string keyXml)
    {
        using (var rsa = new RSACryptoServiceProvider())
        {
            rsa.PersistKeyInCsp = false;
            rsa.FromXmlString(keyXml);
            return rsa.ToXmlString(false);
        }
    }

    public static int KeyBits(string keyXml)
    {
        using (var rsa = new RSACryptoServiceProvider()) { rsa.PersistKeyInCsp = false; rsa.FromXmlString(keyXml); return rsa.KeySize; }
    }

    public static string NewKey(int bits)
    {
        using (var rsa = new RSACryptoServiceProvider(bits))
        {
            rsa.PersistKeyInCsp = false;
            return rsa.ToXmlString(true);
        }
    }

    public static byte[] Sign(byte[] canonical, string privateXml)
    {
        using (var rsa = new RSACryptoServiceProvider())
        {
            rsa.PersistKeyInCsp = false;
            rsa.FromXmlString(privateXml);
            return rsa.SignData(canonical, "SHA256");
        }
    }

    public static bool Verify(byte[] canonical, byte[] sig, string publicXml)
    {
        using (var rsa = new RSACryptoServiceProvider())
        {
            rsa.PersistKeyInCsp = false;
            rsa.FromXmlString(publicXml);
            if (sig.Length != rsa.KeySize / 8) return false;
            return rsa.VerifyData(canonical, "SHA256", sig);
        }
    }

    // ---- the private key as UTF-8 XML bytes (the plain file, or what a .prkey decrypts to): its text lives only in here

    static string Text(byte[] keyXml)
    {
        int start = keyXml.Length >= 3 && keyXml[0] == 0xEF && keyXml[1] == 0xBB && keyXml[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(keyXml, start, keyXml.Length - start);
    }

    /// <summary>True when the bytes hold an RSA private key (not only a public one).</summary>
    public static bool IsPrivateKey(byte[] keyXml)
    {
        try
        {
            using (var rsa = new RSACryptoServiceProvider()) { rsa.PersistKeyInCsp = false; rsa.FromXmlString(Text(keyXml)); return !rsa.PublicOnly; }
        }
        catch (Exception) { return false; }   // not XML, not an RSA key
    }

    public static string PublicXmlOf(byte[] keyXml) { return PublicXml(Text(keyXml)); }
    public static int KeyBitsOf(byte[] keyXml) { return KeyBits(Text(keyXml)); }
    public static byte[] SignWith(byte[] canonical, byte[] keyXml) { return Sign(canonical, Text(keyXml)); }
}

/// <summary>
/// The password-protected copy of the private key (a .prkey text file of key=value lines; '#' lines are comments):
/// PBKDF2-HMAC-SHA256 (random 16-byte salt, 300000..20000000 iterations, 600000 when written) gives a 32-byte master key;
/// HMAC-SHA256 of it gives separate keys for AES-256 and for the MAC; the key's XML is encrypted with AES-256-CBC (PKCS#7,
/// random IV) and HMAC-SHA256 covers the format, every field and the ciphertext (encrypt-then-MAC: the MAC is checked in
/// constant time before anything is decrypted, so a wrong password or an edited file gives nothing).
/// .NET Framework 4.7.2+ APIs only (Windows PowerShell 5.1, C# 5).
/// </summary>
public static class PrKeyFile
{
    public const string Format = "PocketRoles.SigningKey.v1";
    public const string Kdf = "pbkdf2-hmac-sha256";
    public const string Cipher = "aes-256-cbc+hmac-sha256";
    public const int DefaultIterations = 600000, MinIterations = 300000, MaxIterations = 20000000;
    public const int MaxFileChars = 64 * 1024;

    /// <summary>The UTF-8 bytes of a password typed with Read-Host -AsSecureString, made without a managed string (the caller wipes them).</summary>
    public static byte[] Utf8Of(SecureString s)
    {
        if (s == null || s.Length == 0) return new byte[0];
        IntPtr p = IntPtr.Zero;
        char[] c = new char[s.Length];
        try
        {
            p = Marshal.SecureStringToGlobalAllocUnicode(s);
            Marshal.Copy(p, c, 0, c.Length);
            return Encoding.UTF8.GetBytes(c);
        }
        finally
        {
            Array.Clear(c, 0, c.Length);
            if (p != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(p);
        }
    }

    public static int CharCount(byte[] utf8) { return utf8 == null ? 0 : Encoding.UTF8.GetCharCount(utf8); }

    /// <summary>Equal bytes, in a time that does not depend on where they differ.</summary>
    public static bool Same(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        int d = 0;
        for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i];
        return d == 0;
    }

    public static void Wipe(byte[] b) { if (b != null) Array.Clear(b, 0, b.Length); }

    static byte[] RandomBytes(int n)
    {
        var b = new byte[n];
        using (var r = new RNGCryptoServiceProvider()) r.GetBytes(b);
        return b;
    }

    public static bool IsKeyId(string s)
    {
        if (s == null || s.Length != 16) return false;
        foreach (char ch in s) if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f'))) return false;
        return true;
    }

    public static bool IsCopyLabel(string s) { return s == "pc" || s == "usb"; }

    // password → master key (PBKDF2) → one key for AES and one for the MAC
    static void DeriveKeys(byte[] password, byte[] salt, int iterations, out byte[] encKey, out byte[] macKey)
    {
        byte[] master;
        using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256)) master = kdf.GetBytes(32);
        try
        {
            using (var h = new HMACSHA256(master))
            {
                encKey = h.ComputeHash(Encoding.ASCII.GetBytes(Format + " aes-256-cbc key"));
                macKey = h.ComputeHash(Encoding.ASCII.GetBytes(Format + " hmac-sha256 key"));
            }
        }
        finally { Wipe(master); }
    }

    // what the MAC covers: the format and every field, exactly as written, then the ciphertext
    static byte[] MacInput(string copy, string keyId, int iterations, string salt, string iv, string data)
    {
        return Encoding.UTF8.GetBytes(Format + "\n" + Kdf + "\n" + Cipher + "\n" + copy + "\n" + keyId + "\n"
            + iterations.ToString(CultureInfo.InvariantCulture) + "\n" + salt + "\n" + iv + "\n" + data);
    }

    static byte[] Mac(byte[] macKey, byte[] input)
    {
        using (var h = new HMACSHA256(macKey)) return h.ComputeHash(input);
    }

    static Aes NewAes(byte[] key, byte[] iv)
    {
        var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;
        return aes;
    }

    /// <summary>The key=value lines of a .prkey file (CRLF) for <paramref name="plain"/> (the key's XML bytes).</summary>
    public static string Encrypt(byte[] plain, byte[] password, string copy, string keyId, int iterations)
    {
        if (plain == null || plain.Length == 0) throw new ArgumentException("plain");
        if (password == null || password.Length == 0) throw new ArgumentException("password");
        if (iterations < MinIterations || iterations > MaxIterations) throw new ArgumentOutOfRangeException("iterations");
        if (!IsCopyLabel(copy) || !IsKeyId(keyId)) throw new ArgumentException("copy / keyId");
        byte[] salt = RandomBytes(16), iv = RandomBytes(16), encKey, macKey;
        DeriveKeys(password, salt, iterations, out encKey, out macKey);
        try
        {
            byte[] ct;
            using (var aes = NewAes(encKey, iv))
            using (var t = aes.CreateEncryptor()) ct = t.TransformFinalBlock(plain, 0, plain.Length);
            string s = Convert.ToBase64String(salt), v = Convert.ToBase64String(iv), d = Convert.ToBase64String(ct);
            string m = Convert.ToBase64String(Mac(macKey, MacInput(copy, keyId, iterations, s, v, d)));
            var sb = new StringBuilder();
            sb.Append("format=").Append(Format).Append("\r\n");
            sb.Append("copy=").Append(copy).Append("\r\n");
            sb.Append("keyid=").Append(keyId).Append("\r\n");
            sb.Append("kdf=").Append(Kdf).Append("\r\n");
            sb.Append("iterations=").Append(iterations.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            sb.Append("salt=").Append(s).Append("\r\n");
            sb.Append("cipher=").Append(Cipher).Append("\r\n");
            sb.Append("iv=").Append(v).Append("\r\n");
            sb.Append("data=").Append(d).Append("\r\n");
            sb.Append("mac=").Append(m).Append("\r\n");
            return sb.ToString();
        }
        finally { Wipe(encKey); Wipe(macKey); }
    }

    /// <summary>The fields of a .prkey file (no password needed; nothing secret); null when it is not one.</summary>
    public sealed class Info
    {
        public string Copy, KeyId, Salt, Iv, Data, Mac;
        public int Iterations;
    }

    public static Info Parse(string text)
    {
        if (text == null || text.Length > MaxFileChars) return null;
        var f = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimStart((char)0xFEFF).Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) return null;
            string k = line.Substring(0, eq), v = line.Substring(eq + 1);
            if (f.ContainsKey(k)) return null;
            f[k] = v;
        }
        string[] need = { "format", "copy", "keyid", "kdf", "iterations", "salt", "cipher", "iv", "data", "mac" };
        if (f.Count != need.Length) return null;
        foreach (var k in need) if (!f.ContainsKey(k)) return null;
        if (f["format"] != Format || f["kdf"] != Kdf || f["cipher"] != Cipher) return null;
        if (!IsCopyLabel(f["copy"]) || !IsKeyId(f["keyid"])) return null;
        int it;
        if (!int.TryParse(f["iterations"], NumberStyles.None, CultureInfo.InvariantCulture, out it) || it < MinIterations || it > MaxIterations) return null;
        try
        {
            if (Convert.FromBase64String(f["salt"]).Length != 16 || Convert.FromBase64String(f["iv"]).Length != 16) return null;
            if (Convert.FromBase64String(f["mac"]).Length != 32) return null;
            int n = Convert.FromBase64String(f["data"]).Length;
            if (n == 0 || n % 16 != 0) return null;
        }
        catch (FormatException) { return null; }
        return new Info { Copy = f["copy"], KeyId = f["keyid"], Iterations = it, Salt = f["salt"], Iv = f["iv"], Data = f["data"], Mac = f["mac"] };
    }

    /// <summary>The key's XML bytes (the caller wipes them); null for a wrong password, an edited file or not a .prkey file.</summary>
    public static byte[] Decrypt(string text, byte[] password)
    {
        var i = Parse(text);
        if (i == null || password == null || password.Length == 0) return null;
        byte[] encKey, macKey;
        DeriveKeys(password, Convert.FromBase64String(i.Salt), i.Iterations, out encKey, out macKey);
        try
        {
            if (!Same(Mac(macKey, MacInput(i.Copy, i.KeyId, i.Iterations, i.Salt, i.Iv, i.Data)), Convert.FromBase64String(i.Mac))) return null;
            byte[] ct = Convert.FromBase64String(i.Data);
            using (var aes = NewAes(encKey, Convert.FromBase64String(i.Iv)))
            using (var t = aes.CreateDecryptor()) return t.TransformFinalBlock(ct, 0, ct.Length);
        }
        catch (CryptographicException) { return null; }
        finally { Wipe(encKey); Wipe(macKey); }
    }
}
'@

# v0.5.5 hidden lists: the shared hashing (src\Net\AegisHash.cs) — the exact code the mod compiles and the tray embeds, so
# the entries this tool writes hash the same everywhere. Read from the repository (this tool always runs beside it). When it
# is missing (an old checkout) the hidden features are off, but signing a plain file still works.
$hashCs = Join-Path $root 'src\Net\AegisHash.cs'
$script:HasHash = $false
if (Test-Path -LiteralPath $hashCs) {
    try { Add-Type -TypeDefinition ([IO.File]::ReadAllText($hashCs, [Text.Encoding]::UTF8)) -Language CSharp -ErrorAction Stop; $script:HasHash = $true }
    catch { Write-Host ('  メモ: src\Net\AegisHash.cs を読み込めませんでした（隠しリストの機能は使えません）: ' + $_.Exception.Message) -ForegroundColor Yellow }
}

function Fail([string]$m) { Write-Host ('エラー: ' + $m) -ForegroundColor Red; exit 1 }

function Test-InsideRepo([string]$path) {
    $full = [IO.Path]::GetFullPath($path).TrimEnd('\') + '\'
    $r = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    return $full.StartsWith($r, [StringComparison]::OrdinalIgnoreCase)
}

# ---------------------------------------------------------------- keyboard (the only two places input is read)

# a line typed in this console
function Read-Line([string]$prompt) {
    try { return [string](Read-Host -Prompt $prompt) }
    catch { Fail ('この画面では入力を受け付けられません。「' + $keyFolderName + '」フォルダーの .cmd から実行してください（' + $_.Exception.Message + '）') }
}

# a password typed in this console, never shown: its UTF-8 bytes (the caller wipes them with [PrKeyFile]::Wipe)
function Read-Secret([string]$prompt) {
    $s = $null
    try { $s = Read-Host -Prompt $prompt -AsSecureString }
    catch { Fail ('この画面ではパスワードを聞けません。「' + $keyFolderName + '」フォルダーの .cmd から実行してください（' + $_.Exception.Message + '）') }
    try { return ,([PrKeyFile]::Utf8Of($s)) } finally { if ($s) { $s.Dispose() } }
}

# a new password, typed twice; $Reject (optional) returns why a candidate may not be used ('' = fine)
function Read-NewPassword([string]$what, [scriptblock]$Reject) {
    Write-Host ''
    Write-Host ('新しいパスワードを決めます（' + $what + '）。' + $MinPasswordChars + ' 文字以上で、入力した文字は画面に出ません。') -ForegroundColor Cyan
    Write-Host '  忘れると誰にも戻せません（この鍵は使えなくなります）。紙に書いて、鍵とは別の場所にしまってください。'
    for ($try = 1; $try -le 3; $try++) {
        $first = Read-Secret '新しいパスワード'
        if ([PrKeyFile]::CharCount($first) -lt $MinPasswordChars) {
            [PrKeyFile]::Wipe($first)
            Write-Host ('  短すぎます。' + $MinPasswordChars + ' 文字以上にしてください。') -ForegroundColor Yellow
            continue
        }
        if ($Reject) {
            Write-Host '  確かめています…'
            $why = [string](& $Reject $first)
            if ($why) { [PrKeyFile]::Wipe($first); Write-Host ('  ' + $why) -ForegroundColor Yellow; continue }
        }
        $second = Read-Secret 'もう一度（確認のため）'
        $same = [PrKeyFile]::Same($first, $second)
        [PrKeyFile]::Wipe($second)
        if ($same) { return ,$first }
        [PrKeyFile]::Wipe($first)
        Write-Host '  2 回の入力が違いました。もう一度最初から入力してください。' -ForegroundColor Yellow
    }
    Fail 'パスワードを決められませんでした（何も変えていません）'
}

# y / N
function Read-Yes([string]$prompt) { return ((Read-Line ($prompt + ' (y/N)')).Trim() -match '^[yYｙＹ]') }

# ---------------------------------------------------------------- definitions checks

# version= as the clients see it (src\Net\AegisRules.cs Parse, aegis\Aegis.ps1 ParseText): every line whose key before '='
# is "version" in any case, outside '#' comments and '[' section lines. Version: the N of the first one when it is written
# exactly "version=N" (N = 1..999999999), else 0; Count: how many such lines; Bad: the first one not in that form.
function Get-DefinitionsVersion([byte[]]$canonical) {
    $n = 0; $count = 0; $bad = ''
    foreach ($line in ($utf8.GetString($canonical) -split "`n")) {
        $t = $line.Trim().TrimStart([char]0xFEFF).Trim()
        if (-not $t -or $t.StartsWith('#') -or $t.StartsWith('[')) { continue }
        $eq = $t.IndexOf('=')
        if ($eq -le 0 -or $t.Substring(0, $eq).Trim() -ne 'version') { continue }   # -ne ignores case, as the mod does
        $count++
        if (($t -cmatch '^version=([0-9]{1,9})$') -and ([int]$Matches[1] -gt 0)) { if ($count -eq 1) { $n = [int]$Matches[1] } }
        elseif (-not $bad) { $bad = $(if ($t.Length -gt 60) { $t.Substring(0, 60) + '…' } else { $t }) }
    }
    return [pscustomobject]@{ Version = $n; Count = $count; Bad = $bad }
}

# why the file cannot be signed ('' = fine): its canonical form must be canonical again (both caches store it and
# canonicalize it at the next start), and it needs exactly one version=N line that every mod and tray app reads alike
function Get-ContentProblem([byte[]]$canonical) {
    if ([PrDefSign]::HasCr($canonical)) { return '改行以外の CR（CR CR LF や単独の CR）が残っています。改行を CRLF か LF にそろえてください（そのままだとキャッシュに入れた後で署名が合わなくなります）' }
    if ([PrDefSign]::HasBom($canonical)) { return 'BOM（U+FEFF）が先頭以外にもあります（BOM が 2 つなど）。取り除いてください' }
    $v = Get-DefinitionsVersion $canonical
    if ($v.Count -eq 0) { return 'version=N（N は 1 以上）の行がありません' }
    if ($v.Count -gt 1) { return ('version の行が ' + $v.Count + ' 行あります。1 行にしてください（MOD とトレイアプリで読む行が違ってしまいます）') }
    if ($v.Bad) { return ('version の行は「version=N」（N は 1 以上）の形だけにしてください。空白・行末のコメント・大文字があると MOD やトレイアプリが読めません: ' + $v.Bad) }
    $hp = Get-HiddenProblem $canonical
    if ($hp) { return $hp }
    $pp = Get-PlainInHiddenProblem $canonical
    if ($pp) { return $pp }
    return ''
}

# v0.5.5 (G): the 26 already-public plain [tools] / [dlls] / [dllwords] lines (SHA-256 of the lower-case trimmed line). These
# are the ONLY plain lines allowed in the hidden cheat sections; a new cheat name typed in plain is refused, so it goes to the
# private list and is hashed with -UpdateHidden instead of being published. Public names, so their hashes leak nothing.
$script:LegacyCheatSha = @(
    '00f697dbd4d41943e683ed6b2729e60d3583439075d04f70b2308d3c316efa94','0e903e872cc147724f8dff1f87fd0e7ccea33c9051f233266c81891c4c49ce26',
    '1c219611271f13107baf63014f3cf3cd1d2dc8b0ac9f2851dadd3a40fa69c647','398991009da1d251792eb353a0b7b185bc83e71e12e489e73228b554fc6cebc5',
    '3a1b45d4778cc8a6e07420119952efa34e7ffd44ffe01d3726d501824d5f51b9','3d815dc9d3be971433362255d28a835a4a35adb3e575f96e122919e08ca7341f',
    '3df8dad2cca093dff4198eddcce67f78d8fc7ed7ac7433d5ee1c744972eb8261','426a12747ecdd77a009a243994de56575dd471aff9a64ae37017e8d51185914d',
    '4c7d0a2c0effdb4850e1a003c33ee2d9f6690f15503a518fbb29eab6f3781845','512078a39aae57ef2abfb376b82ed2ae02b2e2dd804fa5f732870ba25b6e2e39',
    '648d31d8900f5786fca1f40921b52cb11a1e613699b2557396c33d585d0dea57','82afbc2608378f25146eb22c793953c6d627a99f42f22c17fe1d96bd554b3a08',
    '8e5f29957efba43cc63bf801455eae4cdb19c8919d62ee906534b14f5de6b508','9c99cf23a7ed5160f9b6d2a16b59158bae83bf90702f251dbe2fc17bbb78576a',
    'a6271d9c581f4876f790b2cb1e566bc334ea43d6309ec146effcd15e9eb87a2b','c9539593b0514d0d73b35042a38264b195cca6f2eca0cc0a633811f5678b5d1b',
    'cf00be48e58724f788a5b4dd96960ecbc61000e398e50a1f30fb1f42709acb00','d508058f7eba5f70d1daf7de8c2d5fc35b39d9cbbcbf00762bf55b77dbac517a',
    'e34a4a370b979c8f3b7f11969d4400a54c5c9b2bb15d5ef0eaaf3b1c11fc11f4','e4c456927fa446fca368723b7c85e1be430e3281e373cbe990d5a1145740c729',
    'e6f2ad814692e3f553d63a5535bfef46c030a680e6c3e79ee850dfcf5ae7798a','efc4f22160446b975bab9b294e0847540ac2653fcc18c312e2f59ca7a76559c0',
    'f43b29e32a62e1d179182c36ef427d0bcbaf95a815e03401abcd02e1a8260361','f572c4e2b017381c77d9d1eb30035322c2d1a03c8be88291c878f4242c66f0b9',
    'f6f7eaad8f68dec658c87c36fcf78abaaef21ff9a87406b38fe83aa9dff4eb27','f8694a76af009cc221e61cdb083f6e800cb1b0c35bb4f338ebb6c87ed035937f'
)

# v0.5.5 (G): the NG plain lines allowed as legacy are those of the newest committed, signature-valid definitions file (the
# same git-blob source Get-BaselineMinMod uses); normalized like the mod's StripComment. $null when no verified baseline is in
# git (then the NG guard can only warn, since embedding NG-word hashes in this public tool would itself leak them).
function Get-BaselineNgSet {
    $rel = 'aegis/definitions.txt'
    $keys = @()
    foreach ($l in @(Get-TrustLists)) { foreach ($k in $l.Keys) { if (-not ($l.Revoked -contains $k.Id.ToLowerInvariant())) { $keys += $k.Xml } } }
    foreach ($rev in @('HEAD', 'main', 'origin/main')) {
        $d = Get-GitBlob $rev $rel; $s = Get-GitBlob $rev ($rel + '.sig')
        if ($null -eq $d -or $null -eq $s) { continue }
        $sig = Read-SigText ($utf8.GetString($s)); if (-not $sig) { continue }
        $canon = [PrDefSign]::Canonical($d); $ok = $false
        foreach ($x in $keys) { try { if ([PrDefSign]::Verify($canon, $sig.Sig, $x)) { $ok = $true; break } } catch { } }
        if (-not $ok) { continue }
        $set = New-Object Collections.Generic.HashSet[string]
        foreach ($sect in @('ngwords', 'ngallow')) {
            foreach ($ln in (Get-SectionLines $canon $sect)) { [void]$set.Add((($ln.Text -replace '#.*$', '').Trim())) }
        }
        return $set
    }
    return $null
}

# v0.5.5 (G): refuse a NEW plain (non-#) line in a hidden section. tools/dlls/dllwords: only the 26 public legacy lines are
# allowed. ngwords/ngallow: only lines already in the newest signed committed file (warn if none is verifiable). New cheat
# names / NG words must go in the private list and be hashed with -UpdateHidden. Messages give the line number only, no text.
function Get-PlainInHiddenProblem([byte[]]$canonical) {
    foreach ($sect in @('tools', 'dlls', 'dllwords')) {
        foreach ($ln in (Get-SectionLines $canonical $sect)) {
            $t = (($ln.Text -replace '#.*$', '').Trim()).ToLowerInvariant()
            if ($t.Length -eq 0) { continue }
            $sha = [PrDefSign]::Sha256Hex($utf8.GetBytes($t))
            if ($script:LegacyCheatSha -notcontains $sha) {
                return ([string]$ln.No + ' 行目: [' + $sect + '] に新しい平文の行があります。チート名は私的リストに書いて -UpdateHidden でハッシュにしてください（読める形では公開しません）')
            }
        }
    }
    $ngBase = Get-BaselineNgSet
    if ($null -eq $ngBase) {
        foreach ($sect in @('ngwords', 'ngallow')) {
            if ((Get-SectionLines $canonical $sect).Count -gt 0) { Write-Host ('  注意: [' + $sect + '] の平文の行は、署名の合うコミット済みファイルがないため「前からある行か」を確かめられませんでした（新しい NG 語が平文で残っていないか自分で確かめてください）') -ForegroundColor Yellow; break }
        }
        return ''
    }
    foreach ($sect in @('ngwords', 'ngallow')) {
        foreach ($ln in (Get-SectionLines $canonical $sect)) {
            $t = ($ln.Text -replace '#.*$', '').Trim()
            if ($t.Length -eq 0) { continue }
            if (-not $ngBase.Contains($t)) {
                return ([string]$ln.No + ' 行目: [' + $sect + '] に新しい平文の行があります。NG 語は私的リストに書いて -UpdateHidden でハッシュにしてください（読める形では公開しません）')
            }
        }
    }
    return ''
}

# v0.5.5 hidden lists: why the hashed lines (#h1) or the salt (hashsalt=) cannot be signed as they are ('' = fine). A #h1 line
# a client cannot read would be silently lost (treated as a comment), so the tool refuses one: outside a hashed section, in a
# form no released parser accepts, or with a missing / doubled / bad-form salt. The exact same rules the mod / tray parse by.
function Get-HiddenProblem([byte[]]$canonical) {
    $hiddenSections = @('tools', 'dlls', 'dllwords', 'ngwords', 'ngallow')
    $wire = '^#h1 [a-z]+=[0-9a-f]{20}([0-9a-f]{12})?( [a-z]+=[0-9A-Za-z._-]+)*$'
    $sect = ''; $sawSection = $false; $saltCount = 0; $saltBad = ''; $saltAfterSection = $false; $anyHidden = $false; $lineNo = 0
    # v0.5.5 (review): the mod caps distinct window lengths; the signing tool must refuse a file above the cap, or entries would
    # be silently dropped at match time. 24 for the NG union ([ngwords] ng + [ngallow] al), 16 for toolp and for dllw.
    $ngLen = New-Object Collections.Generic.HashSet[int]; $toolpLen = New-Object Collections.Generic.HashSet[int]; $dllwLen = New-Object Collections.Generic.HashSet[int]
    foreach ($raw in ($utf8.GetString($canonical) -split "`n")) {
        $lineNo++
        $t = $raw.Trim().TrimStart([char]0xFEFF).Trim()
        if ($t.Length -eq 0) { continue }
        if ($t -eq '#h1' -or $t.StartsWith('#h1 ') -or $t.StartsWith("#h1`t")) {
            $anyHidden = $true
            if ($hiddenSections -notcontains $sect) { return ([string]$lineNo + ' 行目: #h1 の隠しエントリは [tools] / [dlls] / [dllwords] / [ngwords] / [ngallow] の中だけに書けます（' + $(if ($sect) { '[' + $sect + ']' } else { '節の外' }) + ' にあります）') }
            if ($t -cnotmatch $wire) { return ([string]$lineNo + ' 行目: #h1 の書式が正しくありません（クライアントはこの行をコメントとして無視し、エントリが消えてしまいます）: ' + $(if ($t.Length -gt 60) { $t.Substring(0, 60) + '…' } else { $t })) }
            # the same strict parse the clients use: reject anything a released parser would drop (wrong key order, wrong kind for the section)
            if ($script:HasHash) {
                $hl = [PocketRoles.Net.AegisHash]::ParseHidden($t)
                if ($null -eq $hl) { return ([string]$lineNo + ' 行目: クライアントが読めない #h1 です（キーの順序や種類が違い、コメント扱いでエントリが消えます）: ' + $(if ($t.Length -gt 60) { $t.Substring(0, 60) + '…' } else { $t })) }
                $okKind = switch ($sect) { 'tools' { @('tool', 'toolp', 'sha', 'vi', 'signer') } 'dlls' { @('dll', 'sha', 'vi', 'signer') } 'dllwords' { @('dllw') } 'ngwords' { @('ng') } 'ngallow' { @('al') } default { @() } }
                if ($okKind -notcontains $hl.Kind) { return ([string]$lineNo + ' 行目: [' + $sect + '] に置けない種類の #h1 です（' + $hl.Kind + '）') }
                if ($hl.Kind -eq 'ng' -or $hl.Kind -eq 'al') { [void]$ngLen.Add([int]$hl.N) }
                elseif ($hl.Kind -eq 'toolp') { [void]$toolpLen.Add([int]$hl.N) }
                elseif ($hl.Kind -eq 'dllw') { [void]$dllwLen.Add([int]$hl.N) }
            }
            continue
        }
        if ($t.StartsWith('#')) { continue }
        if ($t.StartsWith('[')) { $sect = $(if ($t.EndsWith(']')) { $t.Substring(1, $t.Length - 2).Trim().ToLowerInvariant() } else { '' }); $sawSection = $true; continue }
        $eq = $t.IndexOf('=')
        if ($eq -gt 0 -and $t.Substring(0, $eq).Trim().ToLowerInvariant() -eq 'hashsalt') {
            $saltCount++
            if ($sawSection) { $saltAfterSection = $true }
            $val = $t.Substring($eq + 1).Trim()
            if (($val -cnotmatch '^[0-9a-f]{32,128}$') -or (($val.Length % 2) -ne 0)) { $saltBad = $val }
        }
    }
    if ($saltCount -gt 1) { return 'hashsalt= の行は 1 つだけにしてください（MOD とトレイアプリは最初の 1 つだけを読みます）' }
    if ($saltAfterSection) { return 'hashsalt= は最初の [ ] より前に書いてください（節の中の hashsalt= は読まれません）' }
    if ($saltBad -ne '') { return ('hashsalt= は 32〜128 桁の小文字 16 進数（偶数桁）にしてください: ' + $saltBad) }
    if ($anyHidden -and $saltCount -eq 0) { return '#h1 の隠しエントリがありますが hashsalt= の行がありません（クライアントは #h1 を全部無視します）' }
    if ($ngLen.Count -gt 24) { return ('[ngwords] と [ngallow] の長さ（n=）の種類が ' + $ngLen.Count + ' 通りあります。MOD は 24 通りまでしか照合できません（超えた分は当たらず、許可語が落ちて誤検知になります）。語の長さの種類を 24 以下にしてください') }
    if ($toolpLen.Count -gt 16) { return ('[tools] の名前の先頭（toolp）の長さの種類が ' + $toolpLen.Count + ' 通りあります。16 通り以下にしてください') }
    if ($dllwLen.Count -gt 16) { return ('[dllwords] の長さの種類が ' + $dllwLen.Count + ' 通りあります。16 通り以下にしてください') }
    return ''
}

# v0.5.5 オーナーの決定 2026-09-23「A」: 証拠の記録は 90 日残るようになった（AegisPrivacyCore.EvidenceKeepDays）。
# [erase] の説明が「60 日以上」「30 日で消える」のままだと、61〜90 日ぶん起動していなかったホストの PC に消去の依頼が届かず、
# プライバシーポリシー（第5条・3.10「90 日以上」）と食いちがう。署名する前に、この説明を直してもらう（署名した後は直せない）。
function Get-EraseCommentProblem([byte[]]$canonical) {
    $t = $utf8.GetString($canonical)
    if ($t.IndexOf('[erase]') -lt 0) { return '' }
    foreach ($old in @('at least 60 days', '60 日', '60 天')) {
        if ($t.IndexOf($old) -ge 0) {
            return ('[erase] の説明がまだ「' + $old + '」のままです。証拠の記録は 90 日残るようになった（v0.5.5 オーナーの決定 2026-09-23「A」）ので、' +
                '「Keep a request listed for at least 90 days」に直し、同じ段落の「the 30-day deletion」を「the 90-day deletion of evidence records (logs still go at 30 days)」に直してから署名してください' +
                '（プライバシーポリシー 第5条・3.10 は「90 日以上」と書いています）')
        }
    }
    return ''
}

# AegisRules.MinDefinitionsVersion: the oldest definitions version the mod accepts (0 = not found)
function Get-ModFloor {
    if (-not (Test-Path -LiteralPath $rulesCs)) { return 0 }
    $m = [regex]::Match([IO.File]::ReadAllText($rulesCs, [Text.Encoding]::UTF8), 'const int MinDefinitionsVersion\s*=\s*(\d+)\s*;')
    if ($m.Success) { return [int]$m.Groups[1].Value }
    return 0
}

# False when the mod would refuse this version; a note when the floor should be raised before the next mod release
function Test-Floor([int]$ver) {
    $floor = Get-ModFloor
    if ($floor -le 0) { Write-Host '  メモ: src\Net\AegisRules.cs に MinDefinitionsVersion が見つかりません' -ForegroundColor Yellow; return $true }
    if ($ver -lt $floor) {
        Write-Host ('version=' + $ver + ' は MOD が受け付ける最低の版（src\Net\AegisRules.cs の MinDefinitionsVersion = ' + $floor + '）より古いので、MOD はこのファイルを使いません') -ForegroundColor Red
        return $false
    }
    if ($ver -gt $floor -and -not $script:floorNoted) { $script:floorNoted = $true; Write-Host ('  メモ: 次の MOD リリースの前に src\Net\AegisRules.cs の MinDefinitionsVersion を ' + $ver + ' に上げてください（古い署名済みファイルの使い回しを防ぐ下限。build-release.ps1 が一致を確かめます）') -ForegroundColor Yellow }
    return $true
}

# ---------------------------------------------------------------- v0.5.5 version-scoped lines and the required update
# (2026-09-22 owner decisions 「版ごとに書ける仕組み」「アップデート必須」). A [rules] line may end with "@mod<=0.5.5, game>=2026.9.1";
# [update] holds "minmod = 0.5.6 [@...]" (below it the mod refuses to create rooms). The MOD reads conditions loosely and
# fails safe (src\Net\RuleScope.cs); this tool signs only the plain form (mod|game, <= >= == = < >, 3 numbers), so every
# released parser reads every line alike. The comparisons below follow tests\aegis-scope-vectors.txt (the scratch tests run
# these functions on it). The checker (rc-erase-check) dot-sources this file for the same functions.

$script:FirstScopedMod = '0.5.5'   # the first PocketRoles that reads @conditions and [update]
$script:MaxRulesLines = 200         # AegisRules.MaxRuleLines: lines past it are not read
$script:MaxUpdateLines = 50         # AegisRules.MaxUpdateLines

# "0.5.6", "v0.5.6", "0.5.6-beta" (a pre-release, below 0.5.6), "0.5.5+abc" (the part from '+' dropped) → @{ P; Pre } or $null
function ConvertTo-DefVersion([string]$s) {
    if (-not $s) { return $null }
    $s = $s.Trim()
    $plus = $s.IndexOf('+'); if ($plus -ge 0) { $s = $s.Substring(0, $plus) }
    $pre = $false
    $dash = $s.IndexOf('-'); if ($dash -ge 0) { $pre = $true; $s = $s.Substring(0, $dash) }
    $m = [regex]::Match($s, '^[vV]?([0-9]{1,9}(\.[0-9]{1,9}){0,3})$')
    if (-not $m.Success) { return $null }
    $parts = @($m.Groups[1].Value -split '\.')
    $p = @(0, 0, 0, 0)
    for ($i = 0; $i -lt $parts.Count; $i++) { $p[$i] = [int]$parts[$i] }
    return [pscustomobject]@{ P = $p; Pre = $pre }
}

# the game's version: its leading numbers ("2026.8.18s" → 2026.8.18); $null for "?" or none
function ConvertTo-DefGame([string]$s) {
    if (-not $s) { return $null }
    $m = [regex]::Match($s.Trim(), '^[0-9]{1,9}(\.[0-9]{1,9}){1,3}')
    if (-not $m.Success) { return $null }
    return ConvertTo-DefVersion $m.Value
}

function Compare-DefVersion($a, $b) {
    for ($i = 0; $i -lt 4; $i++) { if ($a.P[$i] -ne $b.P[$i]) { if ($a.P[$i] -lt $b.P[$i]) { return -1 } else { return 1 } } }
    if ($a.Pre -ne $b.Pre) { if ($a.Pre) { return -1 } else { return 1 } }
    return 0
}

function Format-DefVersion($v) {
    if (-not $v) { return '' }
    $n = if ($v.P[3] -ne 0) { 4 } else { 3 }
    return ((@($v.P[0..($n - 1)]) -join '.') + $(if ($v.Pre) { '-pre' } else { '' }))
}

# 'T' / 'F' / 'U' as the mod judges a condition text (after '@'; $null = no condition = T): any F → F, any U → U, else T
function Test-DefCond($cond, $mod, $game) {
    if ($null -eq $cond) { return 'T' }
    $unknown = $false
    foreach ($a in $cond.Split(',')) {
        $m = [regex]::Match($a, '^\s*([A-Za-z][A-Za-z0-9_.-]*)\s*(<=|>=|==|=|<|>)\s*[vV]?([0-9]{1,9}(?:\.[0-9]{1,9}){0,3})\s*$')
        if (-not $m.Success) { $unknown = $true; continue }
        $s = $m.Groups[1].Value.ToLowerInvariant()
        $own = if ($s -eq 'mod') { $mod } elseif ($s -eq 'game') { $game } else { $null }
        if (-not $own) { $unknown = $true; continue }
        $c = Compare-DefVersion $own (ConvertTo-DefVersion $m.Groups[3].Value)
        $op = $m.Groups[2].Value
        $r = if ($op -eq '<') { $c -lt 0 } elseif ($op -eq '<=') { $c -le 0 } elseif ($op -eq '>') { $c -gt 0 } elseif ($op -eq '>=') { $c -ge 0 } else { $c -eq 0 }
        if (-not $r) { return 'F' }
    }
    if ($unknown) { return 'U' } else { return 'T' }
}

# the mod ignores a minmod that is clearly wrong: more than one major version ahead of the build, or a part above 999
function Test-DefClearlyWrong($floor, $own) {
    for ($i = 0; $i -lt 4; $i++) { if ($floor.P[$i] -gt 999) { return $true } }
    return ($floor.P[0] -gt $own.P[0] + 1)
}

# the floor for one build as the mod works it out: @{ State = none|ok|below|ignored; Floor; Test }
function Get-DefFloor($mins, $mod, $game, $sim) {
    $real = $null; $ign = $null
    foreach ($l in @($mins)) {
        if (-not $l -or (Test-DefCond $l.Cond $mod $game) -ne 'T') { continue }
        if (Test-DefClearlyWrong $l.Ver $mod) { if (-not $ign -or (Compare-DefVersion $l.Ver $ign) -gt 0) { $ign = $l.Ver } }
        elseif (-not $real -or (Compare-DefVersion $l.Ver $real) -gt 0) { $real = $l.Ver }
    }
    $floor = $real; $test = $false
    if ($sim) {
        if (Test-DefClearlyWrong $sim $mod) { if (-not $ign -or (Compare-DefVersion $sim $ign) -gt 0) { $ign = $sim; $test = $true } }
        elseif (-not $floor -or (Compare-DefVersion $sim $floor) -gt 0) { $floor = $sim; $test = $true }
    }
    if ($floor) { return @{ State = $(if ((Compare-DefVersion $mod $floor) -ge 0) { 'ok' } else { 'below' }); Floor = $floor; Test = $test } }
    if ($ign) { return @{ State = 'ignored'; Floor = $null; Test = $test } }
    return @{ State = 'none'; Floor = $null; Test = $false }
}

# the lines of one section of the canonical text: @{ No = line number; Text = the trimmed line } (no comments, no blanks)
# List[psobject], never List[object]: on Windows 11 の PowerShell 5.1（5.1.26100）は @($list) が List[object] のとき
# 「Argument types do not match」で落ちます（$ErrorActionPreference = 'Continue' だと黙って 0 件になります）。
function Get-SectionLines([byte[]]$canonical, [string]$name) {
    $out = New-Object Collections.Generic.List[psobject]
    $sec = ''; $no = 0
    foreach ($raw in ($utf8.GetString($canonical) -split "`n")) {
        $no++
        $t = $raw.Trim().TrimStart([char]0xFEFF).Trim()
        if (-not $t -or $t.StartsWith('#')) { continue }
        if ($t.StartsWith('[')) { $sec = $(if ($t.EndsWith(']')) { $t.Substring(1, $t.Length - 2).Trim().ToLowerInvariant() } else { '' }); continue }
        if ($t.IndexOf('=') -gt 0 -and $t.Substring(0, $t.IndexOf('=')).Trim() -eq 'version') { continue }   # the mod takes the first as version=
        if ($sec -eq $name) { $out.Add([pscustomobject]@{ No = $no; Text = $t }) }
    }
    return ,$out
}

# a [rules] line as the mod splits it: '=' first, then the '#' comment of the value, then '@' → @{ Key; Value; Cond } ($null: no '=')
function Split-RulesLine([string]$t) {
    $eq = $t.IndexOf('=')
    if ($eq -le 0) { return $null }
    $val = $t.Substring($eq + 1)
    $h = $val.IndexOf('#'); if ($h -ge 0) { $val = $val.Substring(0, $h) }
    $cond = $null
    $at = $val.IndexOf('@'); if ($at -ge 0) { $cond = $val.Substring($at + 1); $val = $val.Substring(0, $at) }
    return [pscustomobject]@{ Key = $t.Substring(0, $eq).Trim().ToLowerInvariant(); Value = $val.Trim(); Cond = $cond }
}

# why a condition text may not be signed ('' = fine): "mod|game <op> x.y.z", separated by ',', nothing else
function Get-CondProblem([string]$cond) {
    if ($cond.Contains('@')) { return '「@」が 2 つあります' }
    foreach ($a in $cond.Split(',')) {
        $x = $a.Trim()
        if (-not $x) { return '空の条件があります（「,」の前後を確かめてください）' }
        $m = [regex]::Match($x, '^(mod|game)\s*(<=|>=|==|=|<|>)\s*v?([0-9]{1,9}\.[0-9]{1,9}\.[0-9]{1,9})$')
        if (-not $m.Success) { return ('読めない条件「' + $x + '」（mod か game、<= >= == = < > のどれか、数字 3 つの版。例: mod<=0.5.5、game>=2026.9.1）') }
        if ($m.Groups[1].Value -eq 'game' -and [int]($m.Groups[3].Value.Split('.')[0]) -lt 2020) { return ('game には Among Us の版（2026.8.18 など）を書いてください: ' + $x) }
    }
    return ''
}

# $false when no PocketRoles v0.5.5 or later can meet the mod conditions (e.g. @mod<0.5.5: v0.5.4 and older read no conditions)
function Test-CondReachable([string]$cond) {
    $first = ConvertTo-DefVersion $script:FirstScopedMod
    foreach ($a in $cond.Split(',')) {
        $m = [regex]::Match($a.Trim(), '^mod\s*(<=|>=|==|=|<|>)\s*v?([0-9.]+)$')
        if (-not $m.Success) { continue }
        $v = ConvertTo-DefVersion $m.Groups[2].Value
        if (-not $v) { continue }
        $c = Compare-DefVersion $v $first; $op = $m.Groups[1].Value
        if (($op -eq '<' -and $c -le 0) -or ($op -eq '<=' -and $c -lt 0) -or (($op -eq '==' -or $op -eq '=') -and $c -lt 0)) { return $false }
    }
    return $true
}

# [rules]: @{ Problems (refused); Warnings }
function Get-ScopeProblems([byte[]]$canonical) {
    $p = @(); $w = @()
    $lines = Get-SectionLines $canonical 'rules'
    if ($lines.Count -gt $script:MaxRulesLines) { $p += ('[rules] の行が ' + $lines.Count + ' 行あります。MOD は ' + $script:MaxRulesLines + ' 行までしか読まない（その先の行は使われず、ゆるめる行が抜けると厳しくなる）ので、' + $script:MaxRulesLines + ' 行以下にしてください') }
    foreach ($l in $lines) {
        if ($l.Text.IndexOf('@') -lt 0) { continue }
        $d = Split-RulesLine $l.Text
        if (-not $d -or $null -eq $d.Cond) { continue }   # an '@' only in the comment
        $why = Get-CondProblem $d.Cond
        if ($why) { $p += ('[rules] ' + $l.No + ' 行目: ' + $why); continue }
        if (-not (Test-CondReachable $d.Cond)) { $w += ('[rules] ' + $l.No + ' 行目: v0.5.5 以降のどの版にも当てはまりません（v0.5.4 以前は条件を読まないので、この行はどこでも使われません）') }
    }
    return @{ Problems = $p; Warnings = $w }
}

# [update]: @{ Problems; Warnings; MinMods = @{ No; Text; Ver; Cond } }
function Get-UpdateProblems([byte[]]$canonical) {
    $p = @(); $w = @(); $mins = @()
    $lines = Get-SectionLines $canonical 'update'
    if ($lines.Count -gt $script:MaxUpdateLines) { $p += ('[update] の行が ' + $lines.Count + ' 行あります。MOD は ' + $script:MaxUpdateLines + ' 行までしか読まないので、それ以下にしてください') }
    $first = ConvertTo-DefVersion $script:FirstScopedMod
    foreach ($l in $lines) {
        $t = $l.Text
        $h = $t.IndexOf('#'); if ($h -ge 0) { $t = $t.Substring(0, $h) }
        $eq = $t.IndexOf('=')
        if ($eq -le 0 -or $t.Substring(0, $eq).Trim() -cne 'minmod') { $p += ('[update] ' + $l.No + ' 行目: この節に書けるのは「minmod = 0.5.6」の形だけです'); continue }
        $val = $t.Substring($eq + 1); $cond = $null
        $at = $val.IndexOf('@'); if ($at -ge 0) { $cond = $val.Substring($at + 1); $val = $val.Substring(0, $at) }
        $val = $val.Trim()
        if ($val -notmatch '^[0-9]{1,9}\.[0-9]{1,9}\.[0-9]{1,9}$') { $p += ('[update] ' + $l.No + ' 行目: minmod は数字 3 つの版にしてください（例: 0.5.6）: ' + $val); continue }
        if ($null -ne $cond) { $why = Get-CondProblem $cond; if ($why) { $p += ('[update] ' + $l.No + ' 行目: ' + $why); continue } }
        $v = ConvertTo-DefVersion $val
        if ((Compare-DefVersion $v $first) -lt 0) { $w += ('[update] ' + $l.No + ' 行目: minmod ' + $val + ' は v' + $script:FirstScopedMod + ' より前なので効果がありません（v0.5.4 以前はこの節を読みません）') }
        $mins += [pscustomobject]@{ No = $l.No; Text = $val; Ver = $v; Cond = $(if ($null -ne $cond) { $cond.Trim() } else { $null }) }
    }
    return @{ Problems = $p; Warnings = $w; MinMods = $mins }
}

function Get-MaxMinMod($mins) {
    $best = $null
    foreach ($m in @($mins)) { if ($m -and (-not $best -or (Compare-DefVersion $m.Ver $best) -gt 0)) { $best = $m.Ver } }
    return $best
}

# this repository's PocketRoles version: the lower of the csproj <Version> and PocketRolesPlugin.Version ($null: no source here)
function Get-RepoModVersion {
    $vs = @()
    $csproj = Join-Path $root 'PocketRoles.csproj'
    if (Test-Path -LiteralPath $csproj) { $m = [regex]::Match([IO.File]::ReadAllText($csproj, [Text.Encoding]::UTF8), '<Version>([^<]+)</Version>'); if ($m.Success) { $vs += $m.Groups[1].Value.Trim() } }
    $plugin = Join-Path $root 'src\PocketRolesPlugin.cs'
    if (Test-Path -LiteralPath $plugin) { $m = [regex]::Match([IO.File]::ReadAllText($plugin, [Text.Encoding]::UTF8), 'const string Version\s*=\s*"([^"]+)"'); if ($m.Success) { $vs += $m.Groups[1].Value.Trim() } }
    $best = $null
    foreach ($s in $vs) { $v = ConvertTo-DefVersion $s; if ($v -and (-not $best -or (Compare-DefVersion $v $best) -lt 0)) { $best = $v } }
    return $best
}

# the newest release published on GitHub (tag_name), $null when it cannot be read (only called when a minmod is raised)
function Get-PublishedModVersion {
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $wc = New-Object Net.WebClient
        $wc.Headers['User-Agent'] = 'PocketRoles-sign-definitions (+https://github.com/wakayamachannel/PocketRoles)'
        $wc.Headers['Accept'] = 'application/vnd.github+json'
        $wc.Encoding = [Text.Encoding]::UTF8
        try { $json = $wc.DownloadString('https://api.github.com/repos/wakayamachannel/PocketRoles/releases/latest') } finally { $wc.Dispose() }
        return ConvertTo-DefVersion ([string](ConvertFrom-Json $json).tag_name)
    } catch { return $null }
}

# v0.5.5 (E, review #13): the newest PUBLISHED release from /releases/latest — which is, by definition, neither a draft nor a
# pre-release — with its tag, page URL, publish time and assets, so a raised minmod can be checked against the very build the
# launcher installs. Ok=$false with Error in { offline | ratelimit | http | parse } tells the offline case apart from a rate
# limit (403/429) or an unreadable answer, so the refusal can say what to do.
function Get-PublishedRelease {
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $wc = New-Object Net.WebClient
        $wc.Headers['User-Agent'] = 'PocketRoles-sign-definitions (+https://github.com/wakayamachannel/PocketRoles)'
        $wc.Headers['Accept'] = 'application/vnd.github+json'
        $wc.Encoding = [Text.Encoding]::UTF8
        try { $json = $wc.DownloadString('https://api.github.com/repos/wakayamachannel/PocketRoles/releases/latest') } finally { $wc.Dispose() }
        $o = ConvertFrom-Json $json
        $ver = ConvertTo-DefVersion ([string]$o.tag_name)
        if (-not $ver) { return @{ Ok = $false; Error = 'parse' } }
        return @{ Ok = $true; Version = $ver; Tag = [string]$o.tag_name; HtmlUrl = [string]$o.html_url; PublishedAt = [string]$o.published_at; Assets = @($o.assets) }
    } catch [Net.WebException] {
        $resp = $_.Exception.Response; $status = 0
        if ($resp) { try { $status = [int]$resp.StatusCode } catch { } }
        if ($status -eq 403 -or $status -eq 429) { return @{ Ok = $false; Error = 'ratelimit'; Status = $status } }
        if ($status -gt 0) { return @{ Ok = $false; Error = 'http'; Status = $status } }
        return @{ Ok = $false; Error = 'offline' }
    } catch { return @{ Ok = $false; Error = 'parse' } }
}

# the local time of an ISO 8601 UTC stamp (published_at) as a short string; '' when it cannot be read
function Format-PublishedAt([string]$iso) {
    if (-not $iso) { return '' }
    $d = [DateTime]::MinValue
    if ([DateTime]::TryParse($iso, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$d)) {
        return $d.ToLocalTime().ToString('yyyy-MM-dd HH:mm')
    }
    return $iso
}

# the highest minmod of the newest committed aegis\definitions.txt whose .sig verifies (HEAD, main, origin/main) — always
# this repository's own file, whatever -File points to (the BAN console signs a working copy outside it). $null: none found.
function Get-BaselineMinMod {
    $rel = 'aegis/definitions.txt'
    $keys = @()
    foreach ($l in @(Get-TrustLists)) { foreach ($k in $l.Keys) { if (-not ($l.Revoked -contains $k.Id.ToLowerInvariant())) { $keys += $k.Xml } } }
    $best = $null
    foreach ($rev in @('HEAD', 'main', 'origin/main')) {
        $d = Get-GitBlob $rev $rel
        $s = Get-GitBlob $rev ($rel + '.sig')
        if ($null -eq $d -or $null -eq $s) { continue }
        $sig = Read-SigText ($utf8.GetString($s))
        if (-not $sig) { continue }
        $canon = [PrDefSign]::Canonical($d)
        $ok = $false
        foreach ($x in $keys) { try { if ([PrDefSign]::Verify($canon, $sig.Sig, $x)) { $ok = $true; break } } catch { } }
        if (-not $ok) { continue }
        $v = (Get-DefinitionsVersion $canon).Version
        if ($best -and $v -le $best.Version) { continue }
        $best = @{ Version = $v; Rev = $rev; Max = (Get-MaxMinMod (Get-UpdateProblems $canon).MinMods) }
    }
    return $best
}

# the minmod lines against this repository's version (no network): $true = fine
function Test-MinModRepo($up) {
    $max = Get-MaxMinMod $up.MinMods
    if (-not $max) { return $true }
    $repo = Get-RepoModVersion
    if (-not $repo) { Write-Host '  メモ: このフォルダーに MOD のソースがないので、minmod をリポジトリの版と比べていません' -ForegroundColor Yellow; return $true }
    if ((Compare-DefVersion $max $repo) -gt 0) {
        Write-Host ('minmod ' + (Format-DefVersion $max) + ' は、このリポジトリの PocketRoles v' + (Format-DefVersion $repo) + ' より新しいので署名しません（書きまちがいなら直してください。先にその版を出す時は、リポジトリの版を上げてから）') -ForegroundColor Red
        return $false
    }
    return $true
}

# raising, lowering or removing the floor, against the newest verified committed file: a raise needs the published release
# to be at least every minmod (asks GitHub; offline = refused); a lowering or a removal is shown and must be confirmed;
# unchanged = no network (the BAN console's offline signing keeps working). $true = may be signed.
function Test-MinModChange($up) {
    $max = Get-MaxMinMod $up.MinMods
    $base = Get-BaselineMinMod
    $baseMax = if ($base) { $base.Max } else { $null }
    $raised = [bool]($max -and (-not $baseMax -or (Compare-DefVersion $max $baseMax) -gt 0))
    if ($raised) {
        $rel = Get-PublishedRelease
        if (-not $rel.Ok) {
            $why = switch ($rel.Error) {
                'ratelimit' { 'GitHub の API 制限（' + $rel.Status + '）に当たりました。少し待ってから、もう一度実行してください' }
                'http' { 'GitHub の最新リリースを読めませんでした（HTTP ' + $rel.Status + '）。もう一度実行してください' }
                'parse' { 'GitHub の最新リリースの中身を読めませんでした（tag_name が読めない・リリースがまだない）' }
                default { 'インターネットにつながらないので、minmod を上げる署名はしません（つないでから、もう一度実行してください）' }
            }
            Write-Host $why -ForegroundColor Red; return $false
        }
        $pub = $rel.Version
        foreach ($m in @($up.MinMods)) {
            if ((Compare-DefVersion $m.Ver $pub) -gt 0) {
                Write-Host ('[update] ' + $m.No + ' 行目: minmod ' + $m.Text + ' は GitHub の最新リリース ' + $rel.Tag + '（v' + (Format-DefVersion $pub) + '）より新しいので署名しません（先にリリースを公開してください。公開前に上げると、アップデートできないまま部屋を作れなくなります）') -ForegroundColor Red
                return $false
            }
        }
        # review #13: the launcher installs the LATEST release's first "PocketRoles-<ver>.zip" (Setup を除く) asset. Require it
        # to exist, be fully uploaded and be the first match, so both the API path and the redirect fallback fetch the same file.
        $tagVer = $rel.Tag -replace '^[vV]', ''
        $expected = 'PocketRoles-' + $tagVer + '.zip'
        $firstMatch = $null
        foreach ($a in $rel.Assets) { if (([string]$a.name) -match '^PocketRoles-[\w.\-]+\.zip$' -and ([string]$a.name) -notmatch 'Setup') { $firstMatch = $a; break } }
        if (-not $firstMatch) { Write-Host ('GitHub の最新リリース ' + $rel.Tag + ' に、ランチャーが落とす mod 本体の zip（' + $expected + '）が添付されていません。先にリリースに zip を添付してください') -ForegroundColor Red; return $false }
        if (([string]$firstMatch.name) -cne $expected) { Write-Host ('GitHub の最新リリース ' + $rel.Tag + ' の mod 本体の資産名が ' + $firstMatch.name + ' で、ランチャーが探す ' + $expected + ' と違います（build-release.ps1 が作る名前に合わせてください）') -ForegroundColor Red; return $false }
        if (([string]$firstMatch.state) -and ([string]$firstMatch.state) -ne 'uploaded') { Write-Host ('GitHub の最新リリースの資産 ' + $firstMatch.name + ' はまだアップロード中です（state=' + $firstMatch.state + '）。アップロードが終わってから実行してください') -ForegroundColor Red; return $false }
        if ([long]$firstMatch.size -le 0) { Write-Host ('GitHub の最新リリースの資産 ' + $firstMatch.name + ' の大きさが 0 です') -ForegroundColor Red; return $false }
        $script:PublishedModVersion = $pub
        $when = Format-PublishedAt $rel.PublishedAt
        Write-Host ('  minmod を上げます: ' + $(if ($baseMax) { 'v' + (Format-DefVersion $baseMax) } else { 'なし' }) + ' → v' + (Format-DefVersion $max) + '（確かめた元: ' + $(if ($base) { 'git ' + $base.Rev + ' の v' + $base.Version } else { '署名の合うコミット済みのファイルなし' }) + '）') -ForegroundColor Cyan
        Write-Host ('  GitHub の最新リリース: ' + $rel.Tag + '（v' + (Format-DefVersion $pub) + '）' + $(if ($rel.HtmlUrl) { ' ' + $rel.HtmlUrl } else { '' }) + $(if ($when) { '（公開: ' + $when + '）' } else { '' })) -ForegroundColor Cyan
        Write-Host ('  ランチャーが落とす資産: ' + $firstMatch.name + '（' + [math]::Round([long]$firstMatch.size / 1KB) + ' KB）') -ForegroundColor Cyan
        return $true
    }
    if ($baseMax -and (-not $max -or (Compare-DefVersion $max $baseMax) -lt 0)) {
        Write-Host ('minmod を' + $(if ($max) { ' v' + (Format-DefVersion $baseMax) + ' から v' + (Format-DefVersion $max) + ' に下げます' } else { '消します（今は v' + (Format-DefVersion $baseMax) + '）' }) + '。コミット済みの git ' + $base.Rev + ' の v' + $base.Version + ' と比べています（BAN 管理で作ったファイルなら、[update] が作業中のものと違っていないか確かめてください）') -ForegroundColor Yellow
        if (-not (Read-Yes 'このまま署名しますか')) { Write-Host '署名をやめました' -ForegroundColor Yellow; return $false }
    }
    return $true
}

# the mod's tables from its source (numbers, directions, built-in levels); $null when the source is not next to this tool
function Get-ModTables {
    if ($script:ModTables) { return $script:ModTables }
    $rc = Join-Path $root 'src\Net\AegisRules.cs'; $cd = Join-Path $root 'src\Net\CheatDetector.cs'
    if (-not (Test-Path -LiteralPath $rc) -or -not (Test-Path -LiteralPath $cd)) { return $null }
    $a = [IO.File]::ReadAllText($rc, [Text.Encoding]::UTF8); $c = [IO.File]::ReadAllText($cd, [Text.Encoding]::UTF8)
    $inv = [Globalization.CultureInfo]::InvariantCulture
    $defs = [ordered]@{}
    foreach ($m in [regex]::Matches($a, 'new Def\("([a-z.]+)",\s*([0-9.]+)f,\s*([0-9.]+)f,\s*([0-9.]+)f(,\s*true)?\)')) {
        $defs[$m.Groups[1].Value] = [pscustomobject]@{ Def = [double]::Parse($m.Groups[2].Value, $inv); Min = [double]::Parse($m.Groups[3].Value, $inv); Max = [double]::Parse($m.Groups[4].Value, $inv); Int = $m.Groups[5].Success }
    }
    $kNames = @(([regex]::Replace([regex]::Match($a, 'internal enum K\s*\{([^}]*)\}').Groups[1].Value, '//[^\n]*', '')) -split '[,\s]+' | Where-Object { $_ })
    $keys = @($defs.Keys)
    $relax = @{}
    $body = [regex]::Match($a, '(?s)private static int RelaxOf\(K k\)(.*?)default:').Groups[1].Value
    foreach ($m in [regex]::Matches($body, '((?:case K\.\w+:\s*)+)return ([+-]1);')) {
        foreach ($k in [regex]::Matches($m.Groups[1].Value, 'case K\.(\w+):')) { $i = [array]::IndexOf($kNames, $k.Groups[1].Value); if ($i -ge 0 -and $i -lt $keys.Count) { $relax[$keys[$i]] = [int]$m.Groups[2].Value } }
    }
    $rules = @(([regex]::Replace([regex]::Match($c, 'internal enum Rule\s*\{([^}]*)\}').Groups[1].Value, '//[^\n]*', '')) -split '[,\s]+' | Where-Object { $_ })
    $levels = @{}
    foreach ($r in $rules) { $levels[$r.ToLowerInvariant()] = 'notice' }
    $lvBody = [regex]::Match($c, '(?s)static Level LevelOf\(Rule r\)(.*?)default:').Groups[1].Value
    foreach ($m in [regex]::Matches($lvBody, '((?:case Rule\.\w+:\s*)+)return Level\.(\w+);')) {
        foreach ($k in [regex]::Matches($m.Groups[1].Value, 'case Rule\.(\w+):')) { $levels[$k.Groups[1].Value.ToLowerInvariant()] = $m.Groups[2].Value.ToLowerInvariant() }
    }
    [void]$levels.Remove('ngword')   # no level.NgWord in the file
    if ($defs.Count -eq 0 -or $levels.Count -eq 0 -or $relax.Count -eq 0) { return $null }
    $script:ModTables = @{ Defs = $defs; Relax = $relax; Levels = $levels }
    return $script:ModTables
}

# [rules] resolved for one build / game version as the mod does it (AegisRules.Resolve): @{ Values = key → number;
# Levels = rule (lower case) → level }, or $null without the mod's source
function Resolve-DefRules([byte[]]$canonical, $mod, $game) {
    $tb = Get-ModTables
    if (-not $tb) { return $null }
    $inv = [Globalization.CultureInfo]::InvariantCulture
    $rank = @{ off = 0; notice = 1; repeat = 2; certain = 3 }
    $vals = @{}; foreach ($k in $tb.Defs.Keys) { $vals[$k] = $tb.Defs[$k].Def }
    $lv = @{}; foreach ($k in $tb.Levels.Keys) { $lv[$k] = $tb.Levels[$k] }
    $n = 0
    foreach ($l in (Get-SectionLines $canonical 'rules')) {
        if (++$n -gt $script:MaxRulesLines) { break }
        $d = Split-RulesLine $l.Text
        if (-not $d) { continue }
        $t = Test-DefCond $d.Cond $mod $game
        if ($t -eq 'F') { continue }
        if ($d.Key.StartsWith('level.')) {
            $rn = $d.Key.Substring(6).Trim()
            $want = $d.Value.ToLowerInvariant()
            if (-not $tb.Levels.ContainsKey($rn) -or -not $rank.ContainsKey($want)) { continue }
            if ($rank[$want] -gt $rank[$tb.Levels[$rn]]) { continue }   # stricter than built in: never
            if ($t -eq 'U' -and $rank[$want] -ge $rank[$lv[$rn]]) { continue }
            $lv[$rn] = $want
        } else {
            if (-not $tb.Defs.Contains($d.Key)) { continue }
            $def = $tb.Defs[$d.Key]
            $f = 0.0
            if (-not [double]::TryParse($d.Value, [Globalization.NumberStyles]::Float, $inv, [ref]$f) -or [double]::IsNaN($f) -or [double]::IsInfinity($f)) { continue }
            if ($def.Int) { $f = [Math]::Round($f) }
            $f = [Math]::Max($def.Min, [Math]::Min($def.Max, $f))
            if ($t -eq 'U') { $s = $tb.Relax[$d.Key]; if (-not $s -or -not ($s * ($f - $vals[$d.Key]) -gt 0)) { continue } }
            $vals[$d.Key] = $f
        }
    }
    if ($vals['speed.notice'] -gt $vals['speed.kick']) { $vals['speed.notice'] = $vals['speed.kick'] }
    return @{ Values = $vals; Levels = $lv }
}

# the Among Us version this repository's mod is made for ($null without the source)
function Get-RepoGameVersion {
    $plugin = Join-Path $root 'src\PocketRolesPlugin.cs'
    if (-not (Test-Path -LiteralPath $plugin)) { return $null }
    $m = [regex]::Match([IO.File]::ReadAllText($plugin, [Text.Encoding]::UTF8), 'SupportedGameVersion\s*=\s*"([^"]+)"')
    if ($m.Success) { return $m.Groups[1].Value } else { return $null }
}

# what hosts get from this file: who can no longer create rooms, and each version-scoped key per version
function Write-ScopeSummary([byte[]]$canonical, $up, [string[]]$more = @()) {
    $pub = $script:PublishedModVersion
    $pubText = if ($pub) { '（最新のリリース v' + (Format-DefVersion $pub) + '）' } else { '' }
    foreach ($m in @($up.MinMods)) {
        $when = if ($m.Cond) { '「@' + $m.Cond + '」の時、' } else { '' }
        Write-Host ('  この定義ファイルを受け取ると: ' + $when + 'v' + $m.Text + ' より前の PocketRoles（v0.5.5 以降）は部屋を作れなくなります' + $pubText)
    }
    $keys = @()
    foreach ($l in (Get-SectionLines $canonical 'rules')) { $d = Split-RulesLine $l.Text; if ($d -and $null -ne $d.Cond -and $keys -notcontains $d.Key) { $keys += $d.Key } }
    if ($keys.Count -eq 0) { return }
    $gameText = Get-RepoGameVersion
    $game = ConvertTo-DefGame $gameText
    $vers = @($script:FirstScopedMod)
    $repo = Get-RepoModVersion; if ($repo) { $vers += (Format-DefVersion $repo) }
    if ($pub) { $vers += (Format-DefVersion $pub) }
    $vers += $more
    $vers = @($vers | Where-Object { $_ } | Select-Object -Unique)
    $res = @{}
    foreach ($v in $vers) { $res[$v] = Resolve-DefRules $canonical (ConvertTo-DefVersion $v) $game }
    if (-not $res[$vers[0]]) { Write-Host '  メモ: MOD のソースがないので、版ごとの値は表示しません'; return }
    Write-Host ('  版ごとの行（Among Us ' + $gameText + ' の時）:')
    foreach ($k in $keys) {
        $cells = @()
        foreach ($v in $vers) {
            $r = $res[$v]
            $x = if ($k.StartsWith('level.')) { $r.Levels[$k.Substring(6).Trim()] } else { $r.Values[$k] }
            $cells += ('v' + $v + ' = ' + $x)
        }
        Write-Host ('    ' + $k + ': ' + ($cells -join ' / '))
    }
}

# every v0.5.5 check of a file before signing (no key is opened before these): $true = may be signed
function Test-SignScope([byte[]]$canonical) {
    $sc = Get-ScopeProblems $canonical
    $up = Get-UpdateProblems $canonical
    foreach ($w in @($sc.Warnings) + @($up.Warnings)) { if ($w) { Write-Host ('  メモ: ' + $w) -ForegroundColor Yellow } }
    $probs = @(@($sc.Problems) + @($up.Problems) | Where-Object { $_ })
    if ($probs.Count -gt 0) { foreach ($p in $probs) { Write-Host $p -ForegroundColor Red }; return $false }
    if (-not (Test-MinModRepo $up)) { return $false }
    if (-not (Test-MinModChange $up)) { return $false }
    Write-ScopeSummary $canonical $up
    return $true
}

# a ledger file of signed versions (not secret) into $map: version -> the SHA-256s of the signed canonical bytes (@())
function Read-LedgerFile([string]$path, $map) {
    if (-not $path -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { return }
    foreach ($line in [IO.File]::ReadAllLines($path, [Text.Encoding]::UTF8)) {
        $t = $line.Trim()
        if (-not $t -or $t.StartsWith('#')) { continue }
        $p = @($t -split '\s+')
        if ($p.Count -ge 2 -and $p[0] -match '^[0-9]{1,9}$' -and $p[1] -match '^[0-9a-fA-F]{64}$') { Add-Signed $map ([int]$p[0]) $p[1].ToLowerInvariant() }
    }
}

function Add-Signed($map, [int]$ver, [string]$hash) {
    if (-not $map.ContainsKey($ver)) { $map[$ver] = @() }
    if ($map[$ver] -notcontains $hash) { $map[$ver] = @($map[$ver]) + $hash }
}

# this PC's ledger (next to the key; the one Invoke-Sign writes)
function Read-Ledger {
    $map = @{}
    Read-LedgerFile $ledgerPath $map
    return $map
}

# a file of a git revision of this repository as its exact bytes ($null: no git, no such revision or file)
function Get-GitBlob([string]$rev, [string]$rel) {
    try {
        $psi = New-Object Diagnostics.ProcessStartInfo
        $psi.FileName = 'git'
        $psi.Arguments = '-C "' + $root + '" cat-file blob "' + $rev + ':' + $rel + '"'
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true
        $p = [Diagnostics.Process]::Start($psi)
        $err = $p.StandardError.ReadToEndAsync()
        $ms = New-Object IO.MemoryStream
        $p.StandardOutput.BaseStream.CopyTo($ms)
        $p.WaitForExit()
        [void]$err.Result
        if ($p.ExitCode -ne 0) { return $null }
        return ,$ms.ToArray()
    } catch { return $null }
}

# every version known as signed, beyond this PC's ledger ("one version = one content" must hold when that ledger is gone,
# e.g. signing on a new PC with the USB copy): the ledgers next to the reachable key copies (-Backup puts one beside the
# USB copy), and the definitions.txt + .sig committed at HEAD, main and origin/main when the .sig verifies with a trusted
# key (a committed signature may already be on GitHub). Returns @{ Map = version -> hashes; From = version -> where from }.
function Get-SignedVersions($ledger, $copies) {
    $map = @{}; $from = @{}
    $note = {
        param([int]$v, [string]$where)
        if (-not $from.ContainsKey($v)) { $from[$v] = @() }
        if ($from[$v] -notcontains $where) { $from[$v] = @($from[$v]) + $where }
    }
    foreach ($v in @($ledger.Keys)) { foreach ($h in @($ledger[$v])) { Add-Signed $map $v $h }; & $note $v ('台帳 ' + $ledgerPath) }
    $dirs = @{}
    foreach ($c in @($copies)) {
        if ($c.Plain) { continue }
        $d = Split-Path -Parent $c.Path
        if (-not $d -or $dirs.ContainsKey($d.ToLowerInvariant())) { continue }
        $dirs[$d.ToLowerInvariant()] = $true
        $f = Join-Path $d 'signed-versions.txt'
        if ([IO.Path]::GetFullPath($f) -eq [IO.Path]::GetFullPath($ledgerPath)) { continue }
        $m = @{}
        try { Read-LedgerFile $f $m } catch { Write-Host ('  メモ: 台帳を読めませんでした: ' + $f) -ForegroundColor Yellow }
        foreach ($v in @($m.Keys)) { foreach ($h in @($m[$v])) { Add-Signed $map $v $h }; & $note $v ('台帳 ' + $f) }
    }
    if (Test-InsideRepo $File) {
        $rel = [IO.Path]::GetFullPath($File).Substring([IO.Path]::GetFullPath($root).TrimEnd('\').Length + 1).Replace('\', '/')
        $keys = @()
        foreach ($l in @(Get-TrustLists)) { foreach ($k in $l.Keys) { if (-not ($l.Revoked -contains $k.Id.ToLowerInvariant())) { $keys += $k.Xml } } }
        foreach ($rev in @('HEAD', 'main', 'origin/main')) {
            $d = Get-GitBlob $rev $rel
            $s = Get-GitBlob $rev ($rel + '.sig')
            if ($null -eq $d -or $null -eq $s) { continue }
            $sig = Read-SigText ($utf8.GetString($s))
            if (-not $sig) { continue }
            $canon = [PrDefSign]::Canonical($d)
            $ok = $false
            foreach ($x in $keys) { try { if ([PrDefSign]::Verify($canon, $sig.Sig, $x)) { $ok = $true; break } } catch { } }
            if (-not $ok) { continue }
            $v = (Get-DefinitionsVersion $canon).Version
            if ($v -le 0) { continue }
            Add-Signed $map $v ([PrDefSign]::Sha256Hex($canon))
            & $note $v ('git ' + $rev)
        }
    }
    return @{ Map = $map; From = $from }
}

# $from's entries missing from $to are added to $to (created as a copy when missing); the other lines of $to stay
function Merge-Ledger([string]$from, [string]$to) {
    if (-not (Test-Path -LiteralPath $to)) { [IO.File]::Copy($from, $to); return }
    $have = @{}
    Read-LedgerFile $to $have
    $add = ''
    foreach ($line in [IO.File]::ReadAllLines($from, [Text.Encoding]::UTF8)) {
        $t = $line.Trim()
        if (-not $t -or $t.StartsWith('#')) { continue }
        $p = @($t -split '\s+')
        if ($p.Count -lt 2 -or $p[0] -notmatch '^[0-9]{1,9}$' -or $p[1] -notmatch '^[0-9a-fA-F]{64}$') { continue }
        $v = [int]$p[0]; $h = $p[1].ToLowerInvariant()
        if ($have.ContainsKey($v) -and ($have[$v] -contains $h)) { continue }
        $add += $t + "`r`n"
    }
    if ($add) { [IO.File]::AppendAllText($to, $add, $utf8) }
}

# ---------------------------------------------------------------- the trusted keys compiled into the clients

# the text without its whole-line // comments (an example in a comment is never read as a key)
function Remove-LineComments([string]$t) {
    return ((($t -split "`n") | Where-Object { -not $_.TrimStart().StartsWith('//') }) -join "`n")
}

# TrustedKeys (Id, Xml) and RevokedKeyIds of the mod and both tray apps; Problem when a list cannot be read
function Get-TrustLists {
    $out = @()
    foreach ($c in @(@{ Name = 'src\Net\AegisRules.cs'; Path = $rulesCs; Cs = $true },
                     @{ Name = 'aegis\Aegis.ps1'; Path = $aegisPs1; Cs = $false },
                     @{ Name = 'aegis\AegisBan.ps1'; Path = $banPs1; Cs = $false })) {
        $keys = @(); $revoked = @(); $problem = ''
        if (-not (Test-Path -LiteralPath $c.Path)) { $problem = 'ファイルがありません' }
        else {
            $t = [IO.File]::ReadAllText($c.Path, [Text.Encoding]::UTF8)
            $tm = [regex]::Matches($t, 'TrustedKeys\s*=\s*\{(.*?)\};', 'Singleline')
            $rm = [regex]::Matches($t, 'RevokedKeyIds\s*=\s*\{(.*?)\};', 'Singleline')
            if ($tm.Count -ne 1) { $problem = 'TrustedKeys（信頼する鍵の一覧）が 1 つだけ見つかりません' }
            elseif ($rm.Count -ne 1) { $problem = 'RevokedKeyIds（無効にした鍵の一覧）が 1 つだけ見つかりません' }
            else {
                $body = Remove-LineComments $tm[0].Groups[1].Value
                if ($c.Cs) {
                    foreach ($m in [regex]::Matches($body, 'new\s+DefinitionsSignature\.PublicKey\(\s*"([^"]*)"\s*,\s*"([^"]*)"\s*,\s*"([^"]*)"\s*\)')) {
                        $keys += [pscustomobject]@{ Id = $m.Groups[1].Value; Xml = '<RSAKeyValue><Modulus>' + $m.Groups[2].Value + '</Modulus><Exponent>' + $m.Groups[3].Value + '</Exponent></RSAKeyValue>' }
                    }
                } else {
                    foreach ($m in [regex]::Matches($body, 'new\s+Key\(\s*"([^"]*)"\s*,\s*"(<RSAKeyValue>.*?</RSAKeyValue>)"\s*\)')) {
                        $keys += [pscustomobject]@{ Id = $m.Groups[1].Value; Xml = $m.Groups[2].Value }
                    }
                }
                if ([regex]::Matches($body, '\bnew\b').Count -ne $keys.Count) { $problem = 'TrustedKeys に読めない行があります' }
                foreach ($m in [regex]::Matches((Remove-LineComments $rm[0].Groups[1].Value), '"([^"]*)"')) { $revoked += $m.Groups[1].Value.ToLowerInvariant() }
            }
        }
        $out += [pscustomobject]@{ Name = $c.Name; Keys = $keys; Revoked = $revoked; Problem = $problem }
    }
    return $out
}

# every list readable, every id its key's own, no revoked key still trusted, the same lists in all three; prints what is wrong
function Test-TrustLists($lists) {
    $ok = $true; $ref = $null; $refName = ''
    foreach ($l in $lists) {
        if ($l.Problem) { Write-Host ('  ' + $l.Name + ': ' + $l.Problem) -ForegroundColor Red; $ok = $false; continue }
        if ($l.Keys.Count -eq 0) { Write-Host ('  ' + $l.Name + ': 信頼する鍵が 1 つもありません') -ForegroundColor Red; $ok = $false; continue }
        foreach ($r in $l.Revoked) { if ($r -notmatch '^[0-9a-f]{16}$') { Write-Host ('  ' + $l.Name + ': RevokedKeyIds の「' + $r + '」は keyid（16 桁の 16 進数）ではありません') -ForegroundColor Red; $ok = $false } }
        $fps = @()
        foreach ($k in $l.Keys) {
            $fp = ''
            try { $fp = [PrDefSign]::Fingerprint($k.Xml) } catch { }
            if (-not $fp) { Write-Host ('  ' + $l.Name + ': 読めない公開鍵があります（keyid=' + $k.Id + '）') -ForegroundColor Red; $ok = $false; continue }
            $id = $fp.Substring(0, 16)
            if ($id -ne $k.Id.ToLowerInvariant()) { Write-Host ('  ' + $l.Name + ': keyid=' + $k.Id + ' が公開鍵と合いません（この公開鍵の keyid は ' + $id + '）') -ForegroundColor Red; $ok = $false }
            if ($l.Revoked -contains $id) { Write-Host ('  ' + $l.Name + ': 無効にした鍵 ' + $id + ' が信頼する鍵の一覧に残っています') -ForegroundColor Red; $ok = $false }
            if ([PrDefSign]::KeyBits($k.Xml) -lt 3072) { Write-Host ('  ' + $l.Name + ': 鍵 ' + $id + ' は 3072 ビットより短いです') -ForegroundColor Red; $ok = $false }
            $fps += $fp
        }
        $sum = (@($fps | Sort-Object -Unique) -join ',') + '|' + (@($l.Revoked | Sort-Object -Unique) -join ',')
        if ($null -eq $ref) { $ref = $sum; $refName = $l.Name }
        elseif ($sum -ne $ref) { Write-Host ('  ' + $l.Name + ' と ' + $refName + ' で、信頼する鍵か無効にした鍵の一覧が違います（同じにしてください）') -ForegroundColor Red; $ok = $false }
    }
    return $ok
}

# why the key with this fingerprint may not sign ('' = it may): it must be trusted by the mod and both tray apps, and
# revoked in none of them
function Get-KeyTrustProblem([string]$fp) {
    $lists = @(Get-TrustLists)
    if (-not (Test-TrustLists $lists)) { return '信頼する鍵の一覧に問題があります（上の表示）。直してから署名してください' }
    $id = $fp.Substring(0, 16)
    foreach ($l in $lists) {
        if ($l.Revoked -contains $id) { return ('この鍵（keyid=' + $id + '）は無効にしてあります（' + $l.Name + ' の RevokedKeyIds）') }
        $found = $false
        foreach ($k in $l.Keys) { if ([PrDefSign]::Fingerprint($k.Xml) -eq $fp) { $found = $true } }
        if (-not $found) { return ('この鍵（keyid=' + $id + '）は ' + $l.Name + ' の信頼する鍵の一覧に入っていません（新しい鍵なら、公開鍵を MOD とトレイアプリの TrustedKeys に足して出してから署名します）') }
    }
    return ''
}

# reads the .sig (base64 line + optional keyid= line); $null when missing or malformed
function Read-Sig([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return Read-SigText ([IO.File]::ReadAllText($path, [Text.Encoding]::UTF8))
}

# the same for the text of a .sig (a committed one read from git)
function Read-SigText([string]$text) {
    $sig = $null; $kid = ''
    foreach ($line in ($text -split "`n")) {
        $t = $line.Trim().TrimStart([char]0xFEFF)
        if (-not $t -or $t.StartsWith('#')) { continue }
        if ($t -like 'keyid=*') { $kid = $t.Substring(6).Trim(); continue }
        if ($sig) { return $null }
        try { $sig = [Convert]::FromBase64String($t) } catch { return $null }
    }
    if (-not $sig) { return $null }
    return [pscustomobject]@{ Sig = $sig; KeyId = $kid }
}

# checks the file against the .sig as the mod and each tray app do (their trusted keys, minus revoked ones);
# prints the result, returns $true when all pass
function Test-Signature {
    if (-not (Test-Path -LiteralPath $File)) { Write-Host ('定義ファイルがありません: ' + $File) -ForegroundColor Red; return $false }
    $canon = [PrDefSign]::Canonical([IO.File]::ReadAllBytes($File))
    $problem = Get-ContentProblem $canon
    if ($problem) { Write-Host ('定義ファイルの形式: ' + $problem) -ForegroundColor Red; return $false }
    # v0.5.5: the version conditions and [update] in the form every released mod reads alike; minmod not above this repository
    $scope = @(@((Get-ScopeProblems $canon).Problems) + @((Get-UpdateProblems $canon).Problems) | Where-Object { $_ })
    if ($scope.Count -gt 0) { foreach ($p in $scope) { Write-Host ('定義ファイルの形式: ' + $p) -ForegroundColor Red }; return $false }
    if (-not (Test-MinModRepo (Get-UpdateProblems $canon))) { return $false }
    $s = Read-Sig $sigPath
    if (-not $s) { Write-Host ('署名ファイルがない、または形式が違います: ' + $sigPath) -ForegroundColor Red; return $false }
    $lists = @(Get-TrustLists)
    $ok = Test-TrustLists $lists
    $sid = $s.KeyId.ToLowerInvariant()
    foreach ($l in $lists) {
        if ($l.Problem -or $l.Keys.Count -eq 0) { continue }   # already printed
        if ($sid -and ($l.Revoked -contains $sid)) { Write-Host ('  ' + $l.Name + ': 無効にした鍵（keyid=' + $s.KeyId + '）で署名されています') -ForegroundColor Red; $ok = $false; continue }
        $cands = @($l.Keys | Where-Object { -not ($l.Revoked -contains $_.Id.ToLowerInvariant()) -and (-not $sid -or $_.Id.ToLowerInvariant() -eq $sid) })
        if ($cands.Count -eq 0) { Write-Host ('  ' + $l.Name + ': 信頼する鍵の一覧にない鍵（keyid=' + $s.KeyId + '）で署名されています') -ForegroundColor Red; $ok = $false; continue }
        $hit = $null
        foreach ($k in $cands) { if ([PrDefSign]::Verify($canon, $s.Sig, $k.Xml)) { $hit = $k; break } }
        if ($hit) { Write-Host ('  ' + $l.Name + ': 署名 OK（鍵 ' + $hit.Id.ToLowerInvariant() + '）') }
        else { Write-Host ('  ' + $l.Name + ': 署名が合いません（署名のあとで定義ファイルが変わった可能性）') -ForegroundColor Red; $ok = $false }
    }
    $ver = (Get-DefinitionsVersion $canon).Version
    if (-not (Test-Floor $ver)) { $ok = $false }
    if ($ok) { Write-Host ('検証 OK: ' + $File + '（version=' + $ver + '）') -ForegroundColor Green }
    return $ok
}

# ---------------------------------------------------------------- the key copies

# the folder of the password-protected PC copy (created when missing; never inside the repository)
function Get-KeyFolder {
    $f = [IO.Path]::GetFullPath($KeyFolder)
    if (Test-InsideRepo $f) { Fail ('鍵のフォルダーはリポジトリの中には作りません: ' + $f) }
    if (-not (Test-Path -LiteralPath $f)) { [void][IO.Directory]::CreateDirectory($f) }
    return $f
}

# this Windows user only (no inherited entries); ignored where the file system has no access lists (a FAT32 / exFAT stick)
function Set-OwnerOnly([string]$path) {
    try {
        $me = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $fsec = New-Object Security.AccessControl.FileSecurity
        $fsec.SetAccessRuleProtection($true, $false)
        $fsec.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($me, [Security.AccessControl.FileSystemRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow)))
        [IO.File]::SetAccessControl($path, $fsec)
    } catch { }
}

# the plain key file (-Init) as bytes; the caller wipes them
function Read-PlainKey {
    $b = [IO.File]::ReadAllBytes($privPath)
    if (-not [PrDefSign]::IsPrivateKey($b)) { [PrKeyFile]::Wipe($b); Fail ('秘密鍵として読めません: ' + $privPath) }
    return ,$b
}

# removable drives that are ready (USB sticks, SD cards): their root folders
function Get-RemovableRoots {
    $r = @()
    try { foreach ($d in [IO.DriveInfo]::GetDrives()) { try { if ($d.DriveType -eq [IO.DriveType]::Removable -and $d.IsReady) { $r += $d } } catch { } } } catch { }
    return $r
}

# where the protected copies were written (not secret): copy, keyid, path, UTC time
function Read-Locations {
    $r = @()
    if (Test-Path -LiteralPath $locPath) {
        foreach ($line in [IO.File]::ReadAllLines($locPath, [Text.Encoding]::UTF8)) {
            $p = @($line.Split("`t"))
            if ($line.StartsWith('#') -or $p.Count -lt 3) { continue }
            $r += [pscustomobject]@{ Copy = $p[0]; KeyId = $p[1]; Path = $p[2] }
        }
    }
    return $r
}

function Save-Location([string]$copy, [string]$keyId, [string]$path) {
    if (-not (Test-Path -LiteralPath $KeyDir)) { [void][IO.Directory]::CreateDirectory($KeyDir) }
    $sb = New-Object Text.StringBuilder
    [void]$sb.Append("# PocketRoles: where tools\sign-definitions.ps1 wrote the password-protected signing key copies (not secret).`r`n")
    foreach ($e in @(Read-Locations)) { if ($e.Path -ne $path) { [void]$sb.Append($e.Copy + "`t" + $e.KeyId + "`t" + $e.Path + "`r`n") } }
    [void]$sb.Append($copy + "`t" + $keyId + "`t" + $path + "`t" + [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture) + "`r`n")
    [IO.File]::WriteAllText($locPath, $sb.ToString(), $utf8)
}

function Get-CopyName($info) { if ($info.Copy -eq 'usb') { return 'USB の控え' } else { return 'PC 用' } }

# every key copy this PC can reach: the plain key (-Init), the protected copies written before, the key folder, and the
# "PocketRoles 署名の鍵" folder (or the root) of each USB stick. Unreadable .prkey files are skipped with a note.
function Get-KeyCopies {
    $list = @()
    $seen = @{}
    if (Test-Path -LiteralPath $privPath) {
        $list += [pscustomobject]@{ Path = $privPath; Plain = $true; Copy = ''; KeyId = ''; Label = ('パスワードなしの鍵（まだ守っていません）: ' + $privPath) }
    }
    $paths = @()
    foreach ($e in @(Read-Locations)) { $paths += $e.Path }
    foreach ($dir in @($KeyFolder)) {
        if (Test-Path -LiteralPath $dir) { foreach ($f in @(Get-ChildItem -LiteralPath $dir -Filter '*.prkey' -File -ErrorAction SilentlyContinue)) { $paths += $f.FullName } }
    }
    foreach ($d in @(Get-RemovableRoots)) {
        foreach ($dir in @($d.RootDirectory.FullName, (Join-Path $d.RootDirectory.FullName $keyFolderName))) {
            if (Test-Path -LiteralPath $dir) { foreach ($f in @(Get-ChildItem -LiteralPath $dir -Filter '*.prkey' -File -ErrorAction SilentlyContinue)) { $paths += $f.FullName } }
        }
    }
    foreach ($p in $paths) {
        if (-not $p) { continue }
        try { $full = [IO.Path]::GetFullPath($p) } catch { continue }
        if ($seen.ContainsKey($full.ToLowerInvariant())) { continue }
        $seen[$full.ToLowerInvariant()] = $true
        if (-not $full.EndsWith('.prkey', [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
        $info = $null
        try { if ((Get-Item -LiteralPath $full).Length -le [PrKeyFile]::MaxFileChars) { $info = [PrKeyFile]::Parse([IO.File]::ReadAllText($full, [Text.Encoding]::UTF8)) } } catch { }
        if (-not $info) { Write-Host ('  メモ: 鍵のファイルとして読めないので飛ばします: ' + $full) -ForegroundColor Yellow; continue }
        $list += [pscustomobject]@{ Path = $full; Plain = $false; Copy = $info.Copy; KeyId = $info.KeyId; Label = ((Get-CopyName $info) + '（keyid ' + $info.KeyId + '）: ' + $full) }
    }
    return $list
}

# which copy to use: the only one, or the number the user types
function Select-KeyCopy($copies) {
    $copies = @($copies)
    if ($copies.Count -eq 0) { Fail '使える鍵が見つかりません' }
    if ($copies.Count -eq 1) { Write-Host ('使う鍵: ' + $copies[0].Label); return $copies[0] }
    Write-Host 'どの鍵を使いますか:'
    for ($i = 0; $i -lt $copies.Count; $i++) { Write-Host ('  [' + ($i + 1) + '] ' + $copies[$i].Label) }
    for ($try = 1; $try -le 3; $try++) {
        $a = (Read-Line '番号（Enter だけなら 1）').Trim()
        if (-not $a) { return $copies[0] }
        $n = 0
        if ([int]::TryParse($a, [ref]$n) -and $n -ge 1 -and $n -le $copies.Count) { return $copies[$n - 1] }
        Write-Host ('  1〜' + $copies.Count + ' の番号で答えてください') -ForegroundColor Yellow
    }
    Fail '鍵を選べませんでした（何も変えていません）'
}

# the private key of a copy: Key = its XML bytes, Password = the bytes typed for it ($null for the plain key).
# The caller wipes both. Three tries; a wrong password or an edited file is refused before anything is decrypted.
function Unlock-KeyCopy($c) {
    if ($c.Plain) { return [pscustomobject]@{ Key = (Read-PlainKey); Password = $null } }
    $text = [IO.File]::ReadAllText($c.Path, [Text.Encoding]::UTF8)
    $info = [PrKeyFile]::Parse($text)
    if (-not $info) { Fail ('鍵のファイルとして読めません: ' + $c.Path) }
    for ($try = 1; $try -le 3; $try++) {
        $pw = Read-Secret ('パスワード（' + (Get-CopyName $info) + '）')
        Write-Host '  確かめています…'
        $key = [PrKeyFile]::Decrypt($text, $pw)
        if ($null -ne $key) {
            $ok = [PrDefSign]::IsPrivateKey($key) -and ([PrDefSign]::Fingerprint([PrDefSign]::PublicXmlOf($key)).Substring(0, 16) -eq $info.KeyId)
            if (-not $ok) { [PrKeyFile]::Wipe($key); [PrKeyFile]::Wipe($pw); Fail ('鍵のファイルの中身が keyid と合いません（壊れています）: ' + $c.Path) }
            return [pscustomobject]@{ Key = $key; Password = $pw }
        }
        [PrKeyFile]::Wipe($pw)
        Write-Host '  パスワードが違います（またはファイルが書き換えられています）' -ForegroundColor Yellow
    }
    Fail 'パスワードが 3 回合いませんでした（何も変えていません）'
}

# writes the protected copy: into <dest>.tmp, read back and decrypted with the same password to the same bytes, then
# renamed to <dest> (an existing <dest> is replaced only after that check). Nothing plain is ever written.
function Write-KeyFile([byte[]]$Key, [byte[]]$Password, [string]$Dest, [string]$Copy, [string]$KeyId) {
    Write-Host '  暗号化しています…'
    $bytes = $utf8.GetBytes($keyFileHeader + [PrKeyFile]::Encrypt($Key, $Password, $Copy, $KeyId, [PrKeyFile]::DefaultIterations))
    $tmp = $Dest + '.tmp'
    if (Test-Path -LiteralPath $tmp) { [IO.File]::Delete($tmp) }
    [IO.File]::WriteAllBytes($tmp, $bytes)
    Set-OwnerOnly $tmp
    Write-Host '  読み戻して、同じ鍵に戻るか確かめています…'
    $back = [PrKeyFile]::Decrypt([IO.File]::ReadAllText($tmp, [Text.Encoding]::UTF8), $Password)
    $same = ($null -ne $back) -and [PrKeyFile]::Same($back, $Key)
    [PrKeyFile]::Wipe($back)
    if (-not $same) { try { [IO.File]::Delete($tmp) } catch { }; Fail ('書いた鍵のファイルを読み戻せませんでした（元の鍵はそのままです）: ' + $Dest) }
    if (Test-Path -LiteralPath $Dest) { [IO.File]::Delete($Dest) }
    [IO.File]::Move($tmp, $Dest)
    if (-not [PrKeyFile]::Same([IO.File]::ReadAllBytes($Dest), $bytes)) { Fail ('書いた鍵のファイルが読み戻した内容と違います: ' + $Dest) }
}

# the plain key file: overwritten with random bytes, then deleted (only after the protected copy was checked)
function Remove-PlainKey {
    $len = [int](Get-Item -LiteralPath $privPath).Length
    $junk = New-Object byte[] ([Math]::Max(1, $len))
    $rng = New-Object Security.Cryptography.RNGCryptoServiceProvider
    try { $rng.GetBytes($junk) } finally { $rng.Dispose() }
    $fs = New-Object IO.FileStream($privPath, [IO.FileMode]::Open, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $fs.Write($junk, 0, $junk.Length); $fs.Flush($true) } finally { $fs.Dispose() }
    [IO.File]::Delete($privPath)
    if (Test-Path -LiteralPath $privPath) { Fail ('パスワードなしの鍵を消せませんでした: ' + $privPath) }
}

# the backup folder: the given one, or a USB stick picked from a list ('ask'); a drive root gets a "PocketRoles 署名の鍵" folder
function Resolve-BackupFolder([string]$v) {
    $p = $v.Trim().Trim('"')
    if (-not $p -or $p -eq 'ask') {
        $p = ''
        for ($round = 1; $round -le 5 -and -not $p; $round++) {
            $drives = @(Get-RemovableRoots)
            Write-Host ''
            if ($drives.Count -eq 0) { Write-Host 'USB メモリが見つかりません。挿してから Enter を押してください（控えを置くフォルダーを入力しても構いません）。' -ForegroundColor Yellow }
            else {
                Write-Host '控えを置く USB メモリ:'
                for ($i = 0; $i -lt $drives.Count; $i++) {
                    $d = $drives[$i]
                    $label = ''; try { $label = $d.VolumeLabel } catch { }
                    $free = ''; try { $free = ('空き ' + [Math]::Round($d.AvailableFreeSpace / 1GB, 1) + ' GB') } catch { }
                    Write-Host ('  [' + ($i + 1) + '] ' + $d.RootDirectory.FullName + $(if ($label) { '（' + $label + '）' } else { '' }) + '  ' + $free)
                }
            }
            $a = (Read-Line $(if ($drives.Count -gt 0) { '番号（またはフォルダー）' } else { 'Enter（またはフォルダー）' })).Trim().Trim('"')
            $n = 0
            if (-not $a) { if ($drives.Count -eq 1) { $p = $drives[0].RootDirectory.FullName } }
            elseif ([int]::TryParse($a, [ref]$n)) { if ($n -ge 1 -and $n -le $drives.Count) { $p = $drives[$n - 1].RootDirectory.FullName } else { Write-Host '  その番号はありません' -ForegroundColor Yellow } }
            elseif ($a -match '^[A-Za-z]:' -or $a.StartsWith('\\')) { $p = $a }
            else { Write-Host '  番号か、E:\ のようなフォルダーで答えてください' -ForegroundColor Yellow }
        }
        if (-not $p) { Fail '控えを置く場所が決まりませんでした（何も変えていません）' }
    }
    if ($p -match '^[A-Za-z]:$') { $p += '\' }
    $full = [IO.Path]::GetFullPath($p)
    if ($full -eq [IO.Path]::GetPathRoot($full)) { $full = Join-Path $full $keyFolderName }
    if (Test-InsideRepo $full) { Fail ('控えはリポジトリの中には作りません: ' + $full) }
    if (-not (Test-Path -LiteralPath $full)) {
        try { [void][IO.Directory]::CreateDirectory($full) } catch { Fail ('フォルダーを作れません: ' + $full + '（' + $_.Exception.Message + '）') }
    }
    return $full
}

# ---------------------------------------------------------------- -Init: the key pair, once
function Invoke-Init {
    if (Test-InsideRepo $KeyDir) { Fail ('鍵はリポジトリの中には作りません: ' + $KeyDir) }
    if (Test-Path -LiteralPath $privPath) { Fail ('秘密鍵はもうあります（上書きしません）: ' + $privPath) }
    $prot = @(Get-KeyCopies | Where-Object { -not $_.Plain })
    if ($prot.Count -gt 0) {
        foreach ($c in $prot) { Write-Host ('  ' + $c.Label) }
        Fail 'パスワード付きの鍵がもうあります（上の表示）。鍵をなくした・漏れたので新しい鍵に替えるときは、古い鍵のファイルを別の場所へ移してから -Init を実行してください'
    }
    $me = [Security.Principal.WindowsIdentity]::GetCurrent().User
    if (-not (Test-Path -LiteralPath $KeyDir)) { [void][IO.Directory]::CreateDirectory($KeyDir) }
    # the folder: this Windows user only (no inherited entries)
    $ds = New-Object Security.AccessControl.DirectorySecurity
    $ds.SetAccessRuleProtection($true, $false)
    $inh = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    $ds.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($me, [Security.AccessControl.FileSystemRights]::FullControl, $inh, [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow)))
    [IO.Directory]::SetAccessControl($KeyDir, $ds)

    $priv = [PrDefSign]::NewKey(3072)
    # the file is created with its access list already set (this user only), and never over an existing one
    $fsec = New-Object Security.AccessControl.FileSecurity
    $fsec.SetAccessRuleProtection($true, $false)
    $fsec.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($me, [Security.AccessControl.FileSystemRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow)))
    $stream = New-Object IO.FileStream($privPath, [IO.FileMode]::CreateNew, [Security.AccessControl.FileSystemRights]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::None, $fsec)
    try { $b = $utf8.GetBytes($priv); $stream.Write($b, 0, $b.Length) } finally { $stream.Dispose() }

    # read back and check the pair works (nothing of it is printed)
    $back = [IO.File]::ReadAllText($privPath, [Text.Encoding]::UTF8)
    $probe = $utf8.GetBytes('PocketRoles definitions signing key check')
    $pub = [PrDefSign]::PublicXml($back)
    if (-not [PrDefSign]::Verify($probe, [PrDefSign]::Sign($probe, $back), $pub)) { Fail '作った鍵で署名と検証ができませんでした' }
    $back = $null; $priv = $null
    [IO.File]::WriteAllText($pubPath, $pub, $utf8)
    $fp = [PrDefSign]::Fingerprint($pub)
    $acl = @((Get-Acl -LiteralPath $privPath).Access | ForEach-Object { [string]$_.IdentityReference }) -join ', '
    Write-Host '鍵を作りました（RSA 3072）。' -ForegroundColor Green
    Write-Host ('  秘密鍵: ' + $privPath)
    Write-Host ('          アクセス権: ' + $acl + '（中身は表示しません。まだパスワードなしです）')
    Write-Host ('  公開鍵: ' + $pubPath)
    Write-Host ('  公開鍵の指紋（SubjectPublicKeyInfo の SHA-256）: ' + $fp)
    Write-Host ('  keyid: ' + $fp.Substring(0, 16))
    Write-Host '  公開鍵（埋め込み用）:'
    Write-Host ('  ' + $pub)
    Write-Host '次に: 公開鍵を src\Net\AegisRules.cs・aegis\Aegis.ps1・aegis\AegisBan.ps1 の TrustedKeys に足し（keyid も）、'
    Write-Host '      -Protect（鍵を守る.cmd）でパスワードを付けて、-Backup（鍵をUSBに控える.cmd）で控えを取ります。'
    exit 0
}

# ---------------------------------------------------------------- -Protect: the plain key → the password-protected PC copy
function Invoke-Protect {
    $folder = Get-KeyFolder
    $dest = Join-Path $folder $pcKeyName
    Write-Host '定義ファイルの署名の鍵にパスワードを付けます。' -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $privPath)) {
        if (Test-Path -LiteralPath $dest) { Write-Host ('鍵はもうパスワードで守られています: ' + $dest) -ForegroundColor Green; exit 0 }
        Fail ('パスワードなしの鍵がありません: ' + $privPath)
    }
    $key = Read-PlainKey
    if ([PrDefSign]::KeyBitsOf($key) -lt 3072) { [PrKeyFile]::Wipe($key); Fail '秘密鍵が 3072 ビットより短いので使いません' }
    $fp = [PrDefSign]::Fingerprint([PrDefSign]::PublicXmlOf($key))
    $kid = $fp.Substring(0, 16)
    Write-Host ('  鍵: keyid ' + $kid + '（' + $privPath + '）')
    Write-Host ('  パスワード付きの鍵を置く場所: ' + $dest)
    $lists = @(Get-TrustLists)
    $untrusted = @($lists | Where-Object { -not $_.Problem -and -not (@($_.Keys | Where-Object { $_.Id.ToLowerInvariant() -eq $kid }).Count) })
    if ($untrusted.Count -gt 0) { Write-Host ('  メモ: この鍵は ' + (($untrusted | ForEach-Object { $_.Name }) -join '・') + ' の信頼する鍵の一覧にまだありません') -ForegroundColor Yellow }
    if (Test-Path -LiteralPath $dest) {
        # an earlier run wrote the protected copy but did not get to remove the plain key: check it holds this key, then finish
        $info = $null
        try { $info = [PrKeyFile]::Parse([IO.File]::ReadAllText($dest, [Text.Encoding]::UTF8)) } catch { }
        if (-not $info -or $info.KeyId -ne $kid) { [PrKeyFile]::Wipe($key); Fail ('別の鍵のファイルがもうあります（上書きしません）: ' + $dest) }
        Write-Host 'パスワード付きの鍵はもうあります（前回の続き）。そのパスワードで同じ鍵に戻るか確かめてから、パスワードなしの鍵を消します。'
        $u = Unlock-KeyCopy ([pscustomobject]@{ Path = $dest; Plain = $false; Copy = $info.Copy; KeyId = $info.KeyId; Label = $dest })
        $same = [PrKeyFile]::Same($u.Key, $key)
        [PrKeyFile]::Wipe($u.Key); [PrKeyFile]::Wipe($u.Password)
        if (-not $same) { [PrKeyFile]::Wipe($key); Fail ('パスワード付きの鍵の中身が、パスワードなしの鍵と違います（どちらも消していません）: ' + $dest) }
    } else {
        $pw = Read-NewPassword 'PC 用の鍵' $null
        try { Write-KeyFile -Key $key -Password $pw -Dest $dest -Copy 'pc' -KeyId $kid } finally { [PrKeyFile]::Wipe($pw) }
    }
    [PrKeyFile]::Wipe($key)
    Save-Location 'pc' $kid $dest
    Remove-PlainKey
    Write-Host ''
    Write-Host '鍵をパスワードで守りました。' -ForegroundColor Green
    Write-Host ('  パスワード付きの鍵: ' + $dest)
    Write-Host ('  パスワードなしの鍵は上書きして消しました: ' + $privPath)
    Write-Host '次に: USB メモリを挿して「鍵をUSBに控える.cmd」で、別のパスワードの控えを作ってください（PC が壊れたときのため）。'
    exit 0
}

# ---------------------------------------------------------------- -Backup: another copy with its own password
function Invoke-Backup {
    $target = Resolve-BackupFolder $Backup
    $dest = Join-Path $target $usbKeyName
    Write-Host ''
    Write-Host ('署名の鍵の控えを作ります: ' + $dest) -ForegroundColor Cyan
    $tRoot = [IO.Path]::GetPathRoot($target).ToLowerInvariant()
    $near = @([IO.Path]::GetPathRoot($env:SystemRoot).ToLowerInvariant(), [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($KeyFolder)).ToLowerInvariant())
    if ($near -contains $tRoot) {
        Write-Host '  この場所は PC 用の鍵と同じドライブです。控えは USB メモリなど、PC とは別の物に置いてください（PC が壊れると両方なくなります）。' -ForegroundColor Yellow
        if (-not (Read-Yes '  それでもここに作りますか')) { Fail 'やめました（何も変えていません）' }
    }
    $destFull = [IO.Path]::GetFullPath($dest)
    if (Test-Path -LiteralPath $dest) {
        $old = $null
        try { $old = [PrKeyFile]::Parse([IO.File]::ReadAllText($dest, [Text.Encoding]::UTF8)) } catch { }
        Write-Host ('  ここにはもう控えがあります' + $(if ($old) { '（keyid ' + $old.KeyId + '）' } else { '' }) + ': ' + $dest) -ForegroundColor Yellow
        if (-not (Read-Yes '  新しいパスワードの控えで置き換えますか')) { Fail 'やめました（何も変えていません）' }
    }
    $copies = @(Get-KeyCopies | Where-Object { $_.Path -ne $destFull })
    if ($copies.Count -eq 0) { Fail '控えの元になる鍵が見つかりません（パスワード付きの鍵のフォルダーか USB メモリを確かめてください）' }
    Write-Host '控えの元にする鍵を開きます。'
    $src = Select-KeyCopy $copies
    $u = Unlock-KeyCopy $src
    $kid = [PrDefSign]::Fingerprint([PrDefSign]::PublicXmlOf($u.Key)).Substring(0, 16)
    # the new password must open no other copy of this key (each copy has its own password)
    $srcPw = $u.Password
    $others = @(Get-KeyCopies | Where-Object { -not $_.Plain -and $_.KeyId -eq $kid -and $_.Path -ne $destFull -and $_.Path -ne $src.Path } | ForEach-Object { $_.Path })
    $reject = {
        param([byte[]]$cand)
        if ($null -ne $srcPw -and [PrKeyFile]::Same($cand, $srcPw)) { return '控えの元の鍵と同じパスワードです。控えには別のパスワードを付けてください' }
        foreach ($o in $others) {
            $x = $null
            try { $x = [PrKeyFile]::Decrypt([IO.File]::ReadAllText($o, [Text.Encoding]::UTF8), $cand) } catch { }
            if ($null -ne $x) { [PrKeyFile]::Wipe($x); return ('ほかの鍵のファイル（' + $o + '）と同じパスワードです。別のパスワードにしてください') }
        }
        return ''
    }.GetNewClosure()
    $pw = Read-NewPassword 'USB の控え用。PC 用とは別のパスワード' $reject
    try { Write-KeyFile -Key $u.Key -Password $pw -Dest $dest -Copy 'usb' -KeyId $kid }
    finally { [PrKeyFile]::Wipe($pw); [PrKeyFile]::Wipe($u.Key); [PrKeyFile]::Wipe($u.Password) }
    Save-Location 'usb' $kid $destFull
    # the ledger of signed versions goes along: "one version = one content" must hold when this PC is gone
    if (Test-Path -LiteralPath $ledgerPath) {
        $toLedger = Join-Path $target 'signed-versions.txt'
        try { Merge-Ledger $ledgerPath $toLedger; Write-Host ('  署名した版の台帳も控えました: ' + $toLedger) }
        catch { Write-Host ('  メモ: 署名した版の台帳を控えられませんでした: ' + $_.Exception.Message) -ForegroundColor Yellow }
    }
    # the same for the releases ledger ("one release = one content")
    if (Test-Path -LiteralPath $relLedgerPath) {
        $toRel = Join-Path $target 'signed-releases.txt'
        try { Merge-ReleaseLedger $relLedgerPath $toRel; Write-Host ('  署名したリリースの台帳も控えました: ' + $toRel) }
        catch { Write-Host ('  メモ: 署名したリリースの台帳を控えられませんでした: ' + $_.Exception.Message) -ForegroundColor Yellow }
    }
    $readme = Join-Path ([IO.Path]::GetFullPath($KeyFolder)) '説明.txt'
    $readmeTo = Join-Path $target '説明.txt'
    if ((Test-Path -LiteralPath $readme) -and -not (Test-Path -LiteralPath $readmeTo)) { try { [IO.File]::Copy($readme, $readmeTo) } catch { } }
    Write-Host ''
    Write-Host '控えを作りました。' -ForegroundColor Green
    Write-Host ('  ' + $dest)
    Write-Host '  USB メモリは PC とは別の場所（引き出しなど）にしまってください。パスワードも PC 用とは別に控えてください。'
    exit 0
}

# ---------------------------------------------------------------- v0.5.5 hidden lists: -UpdateHidden (regenerate #h1 blocks)
# The plain names / words live ONLY on the author's PC, outside the repository, in a normal folder (e.g. %USERPROFILE%\
# PocketRoles-private). -UpdateHidden reads that list, hashes it with the file's salt (made once, then kept fixed) and rewrites
# the #h1 blocks of [tools] / [dlls] / [dllwords] / [ngwords] / [ngallow], keeping the human comments and the legacy plain
# lines. [ngwords] / [tools] / [dlls] / [dllwords] are sorted by hash (the order reveals nothing); [ngallow] KEEPS its order
# (an allow phrase blanks over the earlier ones). It DOES NOT sign: the author reviews, bumps version= and signs afterward.
# The BAN console's -File signing never regenerates (review #12). Private-list format (plain, one per line, # = comment):
#   [tools] name / name*   [dlls] name.dll   [dllwords] word   [ngwords] word / <word / word> / <word>   [ngallow] phrase

$script:privateListRemember = Join-Path $KeyDir 'private-list-path.txt'

# the private-list path: the -PrivateList override, else the remembered one, else asked. Refuses the repository, AppData and
# synced folders (OneDrive / Dropbox / Google Drive), then remembers it. Never printed to a committed place.
function Get-PrivateListPath {
    $p = ''
    if ($PrivateList) { $p = $PrivateList }
    elseif (Test-Path -LiteralPath $script:privateListRemember) { $p = ([IO.File]::ReadAllText($script:privateListRemember, [Text.Encoding]::UTF8)).Trim() }
    if (-not $p -or -not (Test-Path -LiteralPath $p -PathType Leaf)) {
        Write-Host 'チート名・NG 語の元になる私的リスト（この PC だけに置く平文。リポジトリや AppData ではなく、' -ForegroundColor Cyan
        Write-Host '同期されない普通のフォルダ、例: %USERPROFILE%\PocketRoles-private\aegis-source.txt）のパスを入れてください。' -ForegroundColor Cyan
        Write-Host '  節: [tools] [dlls] [dllwords] [ngwords] [ngallow]（[ngwords] は <語 / 語> / <語> も書けます）' -ForegroundColor DarkGray
        $p = (Read-Line '私的リストのファイル').Trim().Trim('"')
    }
    if (-not $p) { Fail '私的リストのパスがありません' }
    $full = [IO.Path]::GetFullPath($p)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { Fail ('私的リストが見つかりません: ' + $full) }
    if (Test-InsideRepo $full) { Fail ('私的リストはリポジトリの中には置けません（コミットされてしまいます）: ' + $full) }
    # not inside ANY git work tree (another checkout, another worktree): a `git add -A` there would commit the private list
    $dir = Split-Path -Parent $full
    $probe = $dir
    while ($probe -and $probe.Length -gt 3) {
        if ((Test-Path -LiteralPath (Join-Path $probe '.git'))) { Fail ('私的リストが git の作業フォルダーの中にあります（' + $probe + '）。git の管理外の普通のフォルダーに置いてください') }
        $up = Split-Path -Parent $probe
        if ($up -eq $probe) { break }
        $probe = $up
    }
    # PowerShell 5.1: git が 0 以外で終わって 2>$null で消すと、代入そのものが起きません（$inTree が $null のままになる）。
    # 私的リストは git の外に置くのが正しい置き方なので、ここは必ず通ります。先に '' を入れておきます。
    $inTree = ''
    $eap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $inTree = [string](& git -C $dir rev-parse --is-inside-work-tree 2>$null) } catch { $inTree = '' } finally { $ErrorActionPreference = $eap }
    if ($null -ne $inTree -and $inTree.Trim() -eq 'true') { Fail ('私的リストが git の管理下のフォルダーにあります（' + $dir + '）。git の管理外の普通のフォルダーに置いてください') }
    $appdata = [IO.Path]::GetFullPath($env:APPDATA); $localapp = [IO.Path]::GetFullPath($env:LOCALAPPDATA)
    if ($full.StartsWith($appdata, [StringComparison]::OrdinalIgnoreCase) -or ($localapp -and $full.StartsWith($localapp, [StringComparison]::OrdinalIgnoreCase))) { Fail ('私的リストは AppData の中には置けません（Claude などのアプリから見えなくなることがあります）: ' + $full) }
    foreach ($en in @('OneDrive', 'OneDriveConsumer', 'OneDriveCommercial')) {
        $d = [Environment]::GetEnvironmentVariable($en)
        if ($d) { $df = [IO.Path]::GetFullPath($d); if ($full.StartsWith($df, [StringComparison]::OrdinalIgnoreCase)) { Fail ('私的リストが同期フォルダ（' + $en + '）の中にあります。同期されない普通のフォルダ（例: %USERPROFILE%\PocketRoles-private）に置いてください') } }
    }
    foreach ($needle in @('\OneDrive', '\Dropbox', '\Google Drive', '\GoogleDrive')) { if ($full.IndexOf($needle, [StringComparison]::OrdinalIgnoreCase) -ge 0) { Fail ('私的リストが同期フォルダらしい場所にあります（' + $needle.Trim('\') + '）。同期されない普通のフォルダに置いてください') } }
    if (-not (Test-Path -LiteralPath $KeyDir)) { [void][IO.Directory]::CreateDirectory($KeyDir) }
    [IO.File]::WriteAllText($script:privateListRemember, $full, $utf8)
    return $full
}

# the private list. UTF-8 only (Notepad's "ANSI" is refused so nothing is mangled). Sections: [tools] [dlls] [dllwords]
# [ngwords] [ngallow], and the renamed-tool sub-sections [tools-sha] (a 64-hex content SHA-256, optionally followed by the
# file's size in bytes, or — v0.5.5 review 9/23 — the path of the file on this PC, which the tool hashes itself and whose
# size it checks), [tools-vi] (field:value or field:value*) [tools-signer] (signer name). Each entry keeps its line number
# so a note names the line, never the text.
# An inline '#' comment is stripped the way the mod's NgText.StripComment does, so it never becomes part of a hashed word.
function Read-PrivateList([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $strict = New-Object Text.UTF8Encoding($false, $true)
    $text = $null
    try { $text = $strict.GetString($bytes) } catch { Fail ('私的リストの文字コードが UTF-8 ではありません（メモ帳の「ANSI」で保存されているかもしれません）。UTF-8 で保存し直してください: ' + $path) }
    # List[psobject]: @($list) が List[object] だと「Argument types do not match」で落ちる PowerShell 5.1 があります（上の Get-SectionLines のメモ）
    $r = @{ Tools = New-Object Collections.Generic.List[psobject]; Dlls = New-Object Collections.Generic.List[psobject]; DllWords = New-Object Collections.Generic.List[psobject]; Ng = New-Object Collections.Generic.List[psobject]; Al = New-Object Collections.Generic.List[psobject]; Sha = New-Object Collections.Generic.List[psobject]; Vi = New-Object Collections.Generic.List[psobject]; Signer = New-Object Collections.Generic.List[psobject]; Other = New-Object Collections.Generic.List[string] }
    $sect = ''; $lineNo = 0
    foreach ($raw in ($text -split "`n")) {
        $lineNo++
        $line = $raw.Trim().TrimStart([char]0xFEFF).Trim()
        if ($line.Length -eq 0 -or $line.StartsWith('#')) { continue }
        # a lone surrogate (an unpaired high/low half) is unreadable; a valid pair (emoji etc.) is fine
        $bad = $false
        for ($i = 0; $i -lt $line.Length; $i++) { $c = $line[$i]; if ([char]::IsHighSurrogate($c)) { if ($i + 1 -ge $line.Length -or -not [char]::IsLowSurrogate($line[$i + 1])) { $bad = $true; break } $i++ } elseif ([char]::IsLowSurrogate($c)) { $bad = $true; break } }
        if ($bad -or $line.Contains([char]0xFFFD)) { Fail ($lineNo.ToString() + ' 行目: 読めない文字（U+FFFD か単独のサロゲート）があります') }
        if ($line.StartsWith('[')) { $sect = $(if ($line.EndsWith(']')) { $line.Substring(1, $line.Length - 2).Trim().ToLowerInvariant() } else { '' }); continue }
        # strip an inline comment (first '#'), like the mod; a line that becomes empty is skipped
        $t = ($line -replace '#.*$', '').Trim()
        if ($t.Length -eq 0) { continue }
        $item = [pscustomobject]@{ T = $t; No = $lineNo }
        switch ($sect) {
            'tools' { $r.Tools.Add($item) }
            'dlls' { $r.Dlls.Add($item) }
            'dllwords' { $r.DllWords.Add($item) }
            'ngwords' { $r.Ng.Add($item) }
            'ngallow' { $r.Al.Add($item) }
            'tools-sha' { $r.Sha.Add($item) }
            'tools-vi' { $r.Vi.Add($item) }
            'tools-signer' { $r.Signer.Add($item) }
            default { $r.Other.Add($sect + ': (line ' + $lineNo + ')') }
        }
    }
    return $r
}

function Remove-KnownExt([string]$n) {
    foreach ($e in @('.exe', '.dll')) { if ($n.ToLowerInvariant().EndsWith($e)) { return $n.Substring(0, $n.Length - $e.Length) } }
    return $n
}

# a private-list item is normally { T = text; No = line } but a caller / test may pass a bare string; either becomes { T; No }
function ConvertTo-PrivEntry($e) { if ($e -is [string]) { return [pscustomobject]@{ T = $e; No = 0 } } return $e }

# a private [tools] list -> sorted #h1 tool / toolp lines (invented values only in tests; real ones stay in the private list).
# tool= (whole name) carries no n= (an exact match does not need the length); only toolp= (a prefix) does.
function New-ToolHidden($entries, [byte[]]$salt) {
    $pairs = @()
    foreach ($e in @($entries)) {
        $e = ConvertTo-PrivEntry $e
        $e0 = $e.T
        $star = $e0.EndsWith('*')
        $base = if ($star) { $e0.Substring(0, $e0.Length - 1) } else { $e0 }
        $stripped = Remove-KnownExt $base
        if ($stripped -ne $base) { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [tools]: 拡張子を外しました（名前だけで判定します）') -ForegroundColor Yellow }
        $key = [PocketRoles.Net.AegisHash]::NormalizeToolKey($stripped)
        if ($key.Length -lt 5) { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [tools]: 5 文字未満なので入れません') -ForegroundColor Yellow; continue }
        if ([PocketRoles.Net.AegisHash]::HasUnstableCase($key)) { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [tools]: 小文字にできない特殊な文字があるので入れません（照合がずれます）') -ForegroundColor Yellow; continue }
        $h = [PocketRoles.Net.AegisHash]::Hash($salt, $key, 10)
        $line = if ($star) { '#h1 toolp=' + $h + ' n=' + $key.Length } else { '#h1 tool=' + $h }
        $pairs += [pscustomobject]@{ H = $h; Line = $line }
    }
    return @($pairs | Sort-Object H | Select-Object -ExpandProperty Line -Unique)
}

function New-DllHidden($entries, [byte[]]$salt) {
    $pairs = @()
    foreach ($e in @($entries)) {
        $e = ConvertTo-PrivEntry $e
        $n = $e.T.ToLowerInvariant()
        if (-not $n.EndsWith('.dll')) { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [dlls]: .dll で終わっていません') -ForegroundColor Yellow }
        $h = [PocketRoles.Net.AegisHash]::Hash($salt, $n, 10)
        $pairs += [pscustomobject]@{ H = $h; Line = ('#h1 dll=' + $h) }
    }
    return @($pairs | Sort-Object H | Select-Object -ExpandProperty Line -Unique)
}

function New-WordHidden($entries, [byte[]]$salt) {
    $pairs = @()
    foreach ($e in @($entries)) {
        $e = ConvertTo-PrivEntry $e
        $w = $e.T.ToLowerInvariant()
        if ($w.Length -lt 4) { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [dllwords]: 4 文字未満なので入れません') -ForegroundColor Yellow; continue }
        $h = [PocketRoles.Net.AegisHash]::Hash($salt, $w, 10)
        $pairs += [pscustomobject]@{ H = $h; Line = ('#h1 dllw=' + $h + ' n=' + $w.Length) }
    }
    return @($pairs | Sort-Object H | Select-Object -ExpandProperty Line -Unique)
}

# the clients never content-hash a file larger than this (aegis\Aegis.ps1 ProcScan.MaxHashFileBytes and the mod's
# SelfScan MaxStrongBytesPerScan): a sha entry made from such a file would never match
$script:MaxHashFileBytes = 64MB

# a private [tools-sha] list -> sorted #h1 sha lines. Each entry is a 64-hex SHA-256 of the cheat tool's file content
# (optionally followed by the file's size in bytes), or the path of that file on this PC (then the tool hashes it itself;
# the path is never written to the definitions file). v0.5.5 review 9/23: warn when the file is over the clients' size cap.
function New-ShaHidden($entries, [byte[]]$salt) {
    $pairs = @()
    foreach ($e in @($entries)) {
        $e = ConvertTo-PrivEntry $e
        $t = $e.T.Trim()
        $x = ''; $size = -1L
        $m = [regex]::Match($t, '^([0-9a-fA-F]{64})(?:\s+(\d+))?$')
        if ($m.Success) {
            $x = $m.Groups[1].Value.ToLowerInvariant()
            if ($m.Groups[2].Success) { $size = [long]$m.Groups[2].Value }
        } else {
            $isFile = $false
            try { $isFile = Test-Path -LiteralPath $t -PathType Leaf } catch { $isFile = $false }
            if ($isFile) {
                try {
                    $fi = Get-Item -LiteralPath $t
                    $size = $fi.Length
                    $x = (Get-FileHash -LiteralPath $t -Algorithm SHA256).Hash.ToLowerInvariant()
                    Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [tools-sha]: この PC のファイルから指紋を作りました（場所は定義ファイルに書きません）') -ForegroundColor DarkGray
                } catch { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [tools-sha]: ファイルを読めませんでした') -ForegroundColor Yellow; continue }
            }
        }
        if ($x -cnotmatch '^[0-9a-f]{64}$') { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [tools-sha]: 64 桁の 16 進数（ファイル内容の SHA-256）・「指紋 大きさ」・この PC のファイルの場所、のどれでもないので入れません') -ForegroundColor Yellow; continue }
        if ($size -gt $script:MaxHashFileBytes) {
            Write-Host ('  警告: 私的リスト ' + $e.No + ' 行目 [tools-sha]: 元のファイルが ' + [int]($script:MaxHashFileBytes / 1MB) + ' MB より大きいので、この指紋は当たりません（大きいファイルは中身を読みません）。バージョン情報か署名者で書いてください') -ForegroundColor Yellow
        }
        $h = [PocketRoles.Net.AegisHash]::Hash($salt, $x, 16)
        $pairs += [pscustomobject]@{ H = $h; Line = ('#h1 sha=' + $h) }
    }
    return @($pairs | Sort-Object H | Select-Object -ExpandProperty Line -Unique)
}

# a private [tools-vi] list -> sorted #h1 vi lines. Each entry is "field:value" or "field:value*" (field = o/i/p/d/c or the
# full word). The value is normalized like a tool name; a '*' makes it a prefix of that length.
function New-ViHidden($entries, [byte[]]$salt) {
    $pairs = @()
    foreach ($e in @($entries)) {
        $e = ConvertTo-PrivEntry $e
        $t = $e.T
        $colon = $t.IndexOf(':')
        if ($colon -le 0) { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [tools-vi]: 「項目:値」の形にしてください（項目 = o/i/p/d/c）') -ForegroundColor Yellow; continue }
        $f = [PocketRoles.Net.AegisHash]::ViFieldChar($t.Substring(0, $colon))
        if (-not $f) { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [tools-vi]: 項目は o/i/p/d/c（または originalfilename / internalname / productname / filedescription / companyname）にしてください') -ForegroundColor Yellow; continue }
        $val = $t.Substring($colon + 1).Trim()
        $star = $val.EndsWith('*'); if ($star) { $val = $val.Substring(0, $val.Length - 1) }
        $key = [PocketRoles.Net.AegisHash]::NormalizeToolKey($val)
        if ($key.Length -lt 5) { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [tools-vi]: 値が 5 文字未満なので入れません') -ForegroundColor Yellow; continue }
        if ([PocketRoles.Net.AegisHash]::HasUnstableCase($key)) { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [tools-vi]: 小文字にできない特殊な文字があるので入れません') -ForegroundColor Yellow; continue }
        $h = [PocketRoles.Net.AegisHash]::Hash($salt, $key, 10)
        $line = if ($star) { '#h1 vi=' + $h + ' f=' + $f + ' n=' + $key.Length } else { '#h1 vi=' + $h + ' f=' + $f }
        $pairs += [pscustomobject]@{ H = ($f + $h); Line = $line }
    }
    return @($pairs | Sort-Object H | Select-Object -ExpandProperty Line -Unique)
}

# a private [tools-signer] list -> sorted #h1 signer lines. Each entry is the Authenticode signer's friendly name.
function New-SignerHidden($entries, [byte[]]$salt) {
    $pairs = @()
    foreach ($e in @($entries)) {
        $e = ConvertTo-PrivEntry $e
        $key = [PocketRoles.Net.AegisHash]::NormalizeToolKey($e.T)
        if ($key.Length -lt 5) { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [tools-signer]: 5 文字未満なので入れません') -ForegroundColor Yellow; continue }
        if ([PocketRoles.Net.AegisHash]::HasUnstableCase($key)) { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [tools-signer]: 小文字にできない特殊な文字があるので入れません') -ForegroundColor Yellow; continue }
        $h = [PocketRoles.Net.AegisHash]::Hash($salt, $key, 10)
        $pairs += [pscustomobject]@{ H = $h; Line = ('#h1 signer=' + $h) }
    }
    return @($pairs | Sort-Object H | Select-Object -ExpandProperty Line -Unique)
}

# a private [ngwords] list -> #h1 ng lines, sorted by hash so the order reveals nothing (marks / ascii come from the shared
# NgWordLine, which normalizes exactly like the mod). A word normalized too short or past MaxNgLen is skipped with a note.
function New-NgHidden($entries, [byte[]]$salt) {
    $seen = @{}; $out = @()
    foreach ($e in @($entries)) {
        $e = ConvertTo-PrivEntry $e
        $line = [PocketRoles.Net.AegisHash]::NgWordLine($salt, $e.T)
        if (-not $line) { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [ngwords]: 入れませんでした（短すぎる・長すぎる・記号だけ・特殊な大文字。中身は出しません）') -ForegroundColor Yellow; continue }
        if (-not $seen.ContainsKey($line)) { $seen[$line] = $true; $out += $line }
    }
    return @($out | Sort-Object)
}

# a private [ngallow] list -> #h1 al lines. ORDER IS KEPT (unlike the others): an allow phrase blanks over the blanks the
# earlier ones left, so re-ordering could change matching. Dedup keeps the first occurrence.
function New-AlHidden($entries, [byte[]]$salt) {
    $seen = @{}; $out = @()
    foreach ($e in @($entries)) {
        $e = ConvertTo-PrivEntry $e
        $line = [PocketRoles.Net.AegisHash]::NgAllowLine($salt, $e.T)
        if (-not $line) { Write-Host ('  メモ: 私的リスト ' + $e.No + ' 行目 [ngallow]: 入れませんでした（短すぎる・長すぎる・特殊な大文字。中身は出しません）') -ForegroundColor Yellow; continue }
        if (-not $seen.ContainsKey($line)) { $seen[$line] = $true; $out += $line }
    }
    return @($out)
}

# the existing salt (before the first section), or a fresh 32-byte one. @{ Hex; Bytes; Made }
function Get-OrMakeSalt([byte[]]$canonical) {
    $sawSection = $false; $hex = ''
    foreach ($raw in ($utf8.GetString($canonical) -split "`n")) {
        $t = $raw.Trim().TrimStart([char]0xFEFF).Trim()
        if ($t.Length -eq 0) { continue }
        if ($t.StartsWith('[')) { $sawSection = $true; continue }
        if ($sawSection -or $t.StartsWith('#')) { continue }
        $eq = $t.IndexOf('=')
        if ($eq -gt 0 -and $t.Substring(0, $eq).Trim().ToLowerInvariant() -eq 'hashsalt') { $hex = $t.Substring($eq + 1).Trim(); break }
    }
    if ($hex -and ($hex -cmatch '^[0-9a-f]{32,128}$') -and (($hex.Length % 2) -eq 0)) {
        return @{ Hex = $hex; Bytes = [PocketRoles.Net.AegisHash]::ParseSalt($hex); Made = $false }
    }
    $b = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($b) } finally { $rng.Dispose() }
    $sb = New-Object Text.StringBuilder
    foreach ($x in $b) { [void]$sb.Append($x.ToString('x2')) }
    $newHex = $sb.ToString()
    return @{ Hex = $newHex; Bytes = [PocketRoles.Net.AegisHash]::ParseSalt($newHex); Made = $true }
}

# rewrite the file text: drop the old #h1 lines of [tools]/[dlls]/[dllwords], append the new sorted blocks at each section's
# end, insert hashsalt= after version= when it was just made. Comments and legacy plain lines are kept in place. Returns text.
function Update-HiddenInText([string]$text, $salt, $gen, $dropPlain = $null) {
    $lines = ($text -replace "`r", '') -split "`n"
    $out = New-Object Collections.Generic.List[string]
    $sect = ''; $pending = $null; $saltDone = -not $salt.Made
    $script:HiddenEmitted = New-Object Collections.Generic.HashSet[string]   # which $gen sections were written (missing-header guard)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $raw = $lines[$i]
        # the last split element after a trailing newline is empty: don't emit a bare blank tail
        if ($i -eq $lines.Count - 1 -and $raw -eq '') { continue }
        $t = $raw.Trim().TrimStart([char]0xFEFF).Trim()
        if ($t.StartsWith('[')) {
            if ($pending) { foreach ($g in $pending) { [void]$out.Add($g) }; $pending = $null }
            [void]$out.Add($raw)
            $sect = $(if ($t.EndsWith(']')) { $t.Substring(1, $t.Length - 2).Trim().ToLowerInvariant() } else { '' })
            if ($gen.ContainsKey($sect)) { $pending = @($gen[$sect]); [void]$script:HiddenEmitted.Add($sect) }
            continue
        }
        if ($gen.ContainsKey($sect) -and ($t -eq '#h1' -or $t.StartsWith('#h1 ') -or $t.StartsWith("#h1`t"))) { continue }   # drop old #h1
        # -HashLegacy: drop the plain entry lines of the named sections (a comment '#' line and a blank line are kept)
        if ($null -ne $dropPlain -and $dropPlain.Contains($sect) -and $t.Length -gt 0 -and -not $t.StartsWith('#')) { continue }
        [void]$out.Add($raw)
        if (-not $saltDone) {
            $eq = $t.IndexOf('=')
            if ($eq -gt 0 -and $t.Substring(0, $eq).Trim().ToLowerInvariant() -eq 'version') { [void]$out.Add('hashsalt=' + $salt.Hex); $saltDone = $true }
        }
    }
    if ($pending) { foreach ($g in $pending) { [void]$out.Add($g) } }
    return (($out -join "`r`n") + "`r`n")
}

# count the #h1 lines already in one section of the canonical text (Get-SectionLines drops every '#' line, so it cannot; the
# review-#12 "entries drop from X to Y" guard needs the real count). Tracks sections exactly as Update-HiddenInText does.
function Count-HiddenLines([byte[]]$canonical, [string]$name) {
    $sec = ''; $c = 0
    foreach ($raw in ($utf8.GetString($canonical) -split "`n")) {
        $t = $raw.Trim().TrimStart([char]0xFEFF).Trim()
        if ($t.StartsWith('[')) { $sec = $(if ($t.EndsWith(']')) { $t.Substring(1, $t.Length - 2).Trim().ToLowerInvariant() } else { '' }); continue }
        if ($sec -eq $name -and ($t -eq '#h1' -or $t.StartsWith('#h1 ') -or $t.StartsWith("#h1`t"))) { $c++ }
    }
    return $c
}

# True when $tok occurs in $hay with a non-letter/non-digit (or the ends of the text) on both sides. Used for tokens shorter
# than 4 characters, which otherwise hit inside ordinary words and variable names on almost every file.
function Test-BoundedHit([string]$hay, [string]$tok) {
    if ([string]::IsNullOrEmpty($hay) -or [string]::IsNullOrEmpty($tok)) { return $false }
    $i = $hay.IndexOf($tok, [StringComparison]::Ordinal)
    while ($i -ge 0) {
        $left = ($i -eq 0) -or (-not [char]::IsLetterOrDigit($hay[$i - 1]))
        $end = $i + $tok.Length
        $right = ($end -ge $hay.Length) -or (-not [char]::IsLetterOrDigit($hay[$end]))
        if ($left -and $right) { return $true }
        if ($i + 1 -ge $hay.Length) { break }
        $i = $hay.IndexOf($tok, $i + 1, [StringComparison]::Ordinal)
    }
    return $false
}

# warn (author screen only, never the text) when a private entry appears in plain in a tracked / staged / new-not-ignored repo
# file. In-process so nothing is written to %TEMP% (no `git grep -f`). Prints only counts, the file's line numbers and the
# private list's line numbers — never a token. Legacy public names are excluded (they are meant to be in the tree).
#
# v0.5.5 second review 9/23 — the old version reported 134 of 164 tracked files (82 %) and was therefore useless:
#   * it folded the WHOLE file with NgCompact, which drops every separator, so a document became one long string and a
#     2-character token matched across word and line ends. Folding is now done line by line (the lines are rejoined with
#     a newline, which NgCompact would have eaten), so nothing matches across two lines.
#   * a token shorter than 4 characters now counts only when it stands on its own (Test-BoundedHit), and only in the
#     plain lower-case text: in the folded text there are no boundaries left to test.
#   * a real copy of the list hits DOZENS of different entries; a coincidence hits one or two. The count of DIFFERENT
#     entries per file is printed so the two can be told apart, with the line numbers of both sides.
function Test-PrivateLeak($entries) {
    $legacyTool = @('cheatengine', 'artmoney', 'wemod', 'extremeinjector', 'xenos', 'xenos64', 'ghinjector', 'squalr', 'speedhack', 'gameguardian', 'sickomenu', 'amongusmenu', 'reclass')
    $legacyDll = @('version.dll', 'dxgi.dll', 'd3d11.dll', 'dinput8.dll', 'winmm.dll', 'dsound.dll', 'xinput1_3.dll', 'xinput1_4.dll', 'xinput9_1_0.dll', 'opengl32.dll')
    $legacyWord = @('menu', 'cheat', 'inject')
    # a token: K = what to look for, No = its line in the private list, Fold = also search the folded text, Short = < 4 chars
    $toks = New-Object Collections.Generic.List[psobject]
    function Add-Tok($text, $no, $fold, $allow) {
        if ([string]::IsNullOrEmpty($text)) { return }
        $toks.Add([pscustomobject]@{ K = $text; No = $no; Fold = [bool]$fold; Allow = [bool]$allow; Short = ($text.Length -lt 4) })
    }
    foreach ($e in @($entries.Tools)) { $b = (Remove-KnownExt ($e.T.TrimEnd('*'))).ToLowerInvariant(); if ($b.Length -ge 3 -and $legacyTool -notcontains $b) { Add-Tok $b $e.No $false $false } }
    foreach ($e in @($entries.Dlls)) { $b = $e.T.ToLowerInvariant(); if ($b.Length -ge 5 -and $legacyDll -notcontains $b) { Add-Tok $b $e.No $false $false } }
    foreach ($e in @($entries.DllWords)) { $b = $e.T.ToLowerInvariant(); if ($b.Length -ge 4 -and $legacyWord -notcontains $b) { Add-Tok $b $e.No $false $false } }
    foreach ($e in @($entries.Signer)) { $b = $e.T.ToLowerInvariant(); if ($b.Length -ge 5) { Add-Tok $b $e.No $false $false } }
    # NG / allow words: fold like the mod, keep length >= 2 (all of them), and search the folded line text too.
    # [ngallow] holds ORDINARY words on purpose (that is why they are allowed), so one of them in a repository file means
    # nothing: they still count towards "how many different entries does this file hold" (a copy of the allow list is a leak
    # too), but on their own they never raise a warning.
    foreach ($e in @($entries.Ng)) { $b = [PocketRoles.Net.AegisHash]::NgCompact($e.T.Trim('<', '>', [char]0xFF1C, [char]0xFF1E)); if ($b.Length -ge 2) { Add-Tok $b $e.No $true $false } }
    foreach ($e in @($entries.Al)) { $b = [PocketRoles.Net.AegisHash]::NgCompact($e.T.Trim('<', '>', [char]0xFF1C, [char]0xFF1E)); if ($b.Length -ge 2) { Add-Tok $b $e.No $true $true } }
    if ($toks.Count -eq 0) { return }
    $short = @($toks | Where-Object { $_.Short }).Count
    $files = @()
    $eap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try {
        $files += @(& git -C $root ls-files 2>$null)
        $files += @(& git -C $root ls-files --others --exclude-standard 2>$null)
    } finally { $ErrorActionPreference = $eap }
    $files = @($files | Where-Object { $_ } | Sort-Object -Unique)
    $skipExt = @('.png', '.ico', '.jpg', '.jpeg', '.gif', '.zip', '.mp4', '.dll', '.pdf', '.woff', '.woff2', '.ttf', '.eot', '.wav', '.mp3')
    $scanned = 0; $flagged = 0; $worst = 0
    $weak = New-Object Collections.Generic.List[psobject]
    foreach ($rel in $files) {
        $full = Join-Path $root $rel
        try {
            if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
            if ($skipExt -contains ([IO.Path]::GetExtension($rel).ToLowerInvariant())) { continue }
            $fi = Get-Item -LiteralPath $full
            if ($fi.Length -gt 16MB) { continue }
            $bytes = [IO.File]::ReadAllBytes($full)
            $isBin = $false; foreach ($bb in $bytes) { if ($bb -eq 0) { $isBin = $true; break } }
            if ($isBin) { continue }
            $lines = ([Text.Encoding]::UTF8.GetString($bytes)) -split "`r?`n"
        } catch { continue }
        $scanned++
        $lower = New-Object string[] $lines.Count
        $fold = New-Object string[] $lines.Count
        for ($i = 0; $i -lt $lines.Count; $i++) { $lower[$i] = $lines[$i].ToLowerInvariant(); $fold[$i] = [PocketRoles.Net.AegisHash]::NgCompact($lines[$i]) }
        # one joined string per form, newline-separated: a token can never match across two lines
        $lowerAll = [string]::Join("`n", $lower)
        $foldAll = [string]::Join("`n", $fold)
        $hitNos = New-Object Collections.Generic.List[int]     # the private list's line numbers
        $hitAt = New-Object Collections.Generic.List[int]      # this file's line numbers
        $longHit = $false                                      # an entry of 4+ characters, not from [ngallow], standing on its own
        foreach ($t in $toks) {
            $any = $false
            if ($t.Short) { $any = Test-BoundedHit $lowerAll $t.K }
            else { $any = $lowerAll.Contains($t.K) -or ($t.Fold -and $foldAll.Contains($t.K)) }
            if (-not $any) { continue }
            $hitNos.Add([int]$t.No)
            # a warning is raised only for an entry that STANDS ON ITS OWN in the plain text: inside a longer word, or only
            # after folding (which drops the separators inside a line), it is the shape of an ordinary sentence, not of a copy
            if (-not $t.Short -and -not $t.Allow -and (Test-BoundedHit $lowerAll $t.K)) { $longHit = $true }
            for ($i = 0; $i -lt $lines.Count; $i++) {
                $here = if ($t.Short) { Test-BoundedHit $lower[$i] $t.K } else { $lower[$i].Contains($t.K) -or ($t.Fold -and $fold[$i].Contains($t.K)) }
                if ($here) { if (-not $hitAt.Contains($i + 1)) { $hitAt.Add($i + 1) }; break }
            }
        }
        if ($hitNos.Count -eq 0) { continue }
        if ($hitNos.Count -gt $worst) { $worst = $hitNos.Count }
        # a copy of the list hits DOZENS of entries, and an entry of 4+ characters is never an accident; everything else is
        # one or two short tokens inside ordinary prose or a variable name, which is listed apart so it cannot drown the rest
        if (-not $longHit -and $hitNos.Count -lt 5) {
            $weak.Add([pscustomobject]@{ Rel = $rel; N = $hitNos.Count; At = @($hitAt | Sort-Object | Select-Object -First 3); No = @($hitNos | Sort-Object -Unique | Select-Object -First 3) })
            continue
        }
        $flagged++
        $colour = if ($hitNos.Count -ge 5) { 'Red' } else { 'Yellow' }
        $someAt = @($hitAt | Sort-Object | Select-Object -First 8)
        $someNo = @($hitNos | Sort-Object -Unique | Select-Object -First 8)
        Write-Host ('  警告: ' + $rel + ': 私的リストの ' + $hitNos.Count + ' 語が平文で当たりました（ファイルの行 ' + ($someAt -join ',') +
            $(if ($hitAt.Count -gt 8) { ' …' } else { '' }) + ' / 一覧の行 ' + ($someNo -join ',') + $(if ($hitNos.Count -gt 8) { ' …' } else { '' }) + '）') -ForegroundColor $colour
    }
    Write-Host ('  漏えい確認: ' + $toks.Count + ' 語（うち 4 文字未満 ' + $short + ' 語は語の区切りがある時だけ）を ' + $scanned + ' ファイルで確認しました') -ForegroundColor DarkGray
    if ($flagged -eq 0) { Write-Host '  要確認の当たりはありません（1 語で立っている 4 文字以上の語 0、5 語以上のファイル 0）' -ForegroundColor DarkGray }
    elseif ($worst -ge 5) { Write-Host ('  1 ファイルで最大 ' + $worst + ' 語が当たりました。5 語以上は一覧のコピーの疑いが濃いので、そのファイルを確かめてください') -ForegroundColor Red }
    else { Write-Host '  4 文字以上の語が 1 語で立っているファイルがあります。上の行番号で確かめてください' -ForegroundColor Yellow }
    if ($weak.Count -gt 0) {
        Write-Host ('  参考（短い語か [ngallow] のふつうの語が ' + $weak.Count + ' ファイルで 1〜4 語。文や変数名の中で、一覧のコピーではありません）:') -ForegroundColor DarkGray
        foreach ($w in @($weak | Sort-Object -Property @{ Expression = 'N'; Descending = $true } | Select-Object -First 10)) {
            Write-Host ('    ' + $w.Rel + ' — ' + $w.N + ' 語（ファイルの行 ' + ($w.At -join ',') + ' / 一覧の行 ' + ($w.No -join ',') + '）') -ForegroundColor DarkGray
        }
        if ($weak.Count -gt 10) { Write-Host ('    …ほか ' + ($weak.Count - 10) + ' ファイル') -ForegroundColor DarkGray }
    }
}

# v0.5.5 review 9/23: a [tools-signer] / [tools-vi] value that matches a program INSTALLED ON THIS PC would flag an ordinary
# app on every host (the clients match the signer name and the version-info fields of a running app's exe). Compares the
# private list's values with the exes under Program Files, Program Files (x86) and %LOCALAPPDATA%\Programs and warns.
# Prints only counts and the private list's line number — never a name, a value or a path. Read-only; capped in files and
# seconds so it cannot run long. $Roots / the caps are parameters so the headless test can point it at a scratch folder.
function Test-HiddenFalsePositive($entries, [string[]]$Roots = $null, [int]$MaxFiles = 20000, [int]$MaxSeconds = 120) {
    $signers = @()
    foreach ($e in @($entries.Signer)) {
        $k = [PocketRoles.Net.AegisHash]::NormalizeToolKey($e.T)
        if ($k.Length -ge 5) { $signers += [pscustomobject]@{ No = $e.No; Key = $k } }
    }
    $vis = @()
    foreach ($e in @($entries.Vi)) {
        $t = $e.T; $colon = $t.IndexOf(':')
        if ($colon -le 0) { continue }
        $f = [PocketRoles.Net.AegisHash]::ViFieldChar($t.Substring(0, $colon))
        if (-not $f) { continue }
        $val = $t.Substring($colon + 1).Trim()
        $star = $val.EndsWith('*'); if ($star) { $val = $val.Substring(0, $val.Length - 1) }
        $k = [PocketRoles.Net.AegisHash]::NormalizeToolKey($val)
        if ($k.Length -ge 5) { $vis += [pscustomobject]@{ No = $e.No; F = $f; Key = $k; Star = $star } }
    }
    if (($signers.Count + $vis.Count) -eq 0) { return }
    if (-not $Roots) {
        $Roots = @([Environment]::GetFolderPath('ProgramFiles'), [Environment]::GetFolderPath('ProgramFilesX86'), $env:ProgramW6432,
                   $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Programs' } else { '' }))
    }
    $Roots = @($Roots | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) } | Sort-Object -Unique)
    if ($Roots.Count -eq 0) { return }
    $hitLines = New-Object Collections.Generic.HashSet[int]
    $sw = [Diagnostics.Stopwatch]::StartNew(); $seen = 0; $stopped = $false
    foreach ($root in $Roots) {
        if ($stopped) { break }
        foreach ($f in (Get-ChildItem -LiteralPath $root -Filter *.exe -Recurse -File -Force -ErrorAction SilentlyContinue)) {
            if ($seen -ge $MaxFiles -or $sw.Elapsed.TotalSeconds -ge $MaxSeconds) { $stopped = $true; break }
            $seen++
            if ($vis.Count -gt 0) {
                try {
                    $vi = [Diagnostics.FileVersionInfo]::GetVersionInfo($f.FullName)
                    $fields = @{ 'o' = $vi.OriginalFilename; 'i' = $vi.InternalName; 'p' = $vi.ProductName; 'd' = $vi.FileDescription; 'c' = $vi.CompanyName }
                    foreach ($v in $vis) {
                        $val = [PocketRoles.Net.AegisHash]::NormalizeToolKey([string]$fields[[string]$v.F])
                        if ($val.Length -eq 0) { continue }
                        if ($v.Star) { if ($val.Length -ge $v.Key.Length -and $val.Substring(0, $v.Key.Length) -ceq $v.Key) { [void]$hitLines.Add($v.No) } }
                        elseif ($val -ceq $v.Key) { [void]$hitLines.Add($v.No) }
                    }
                } catch { }
            }
            if ($signers.Count -gt 0) {
                try {
                    $c = [Security.Cryptography.X509Certificates.X509Certificate]::CreateFromSignedFile($f.FullName)
                    $c2 = New-Object Security.Cryptography.X509Certificates.X509Certificate2 $c
                    $sn = [PocketRoles.Net.AegisHash]::NormalizeToolKey($c2.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false))
                    if ($sn.Length -gt 0) { foreach ($s in $signers) { if ($sn -ceq $s.Key) { [void]$hitLines.Add($s.No) } } }
                } catch { }
            }
        }
    }
    Write-Host ('  この PC に入っているアプリと照らしました: exe ' + $seen + ' 個・' + [int]$sw.Elapsed.TotalSeconds + ' 秒' + $(if ($stopped) { '（上限で打ち切り）' } else { '' })) -ForegroundColor DarkGray
    if ($hitLines.Count -eq 0) { Write-Host '  [tools-signer] / [tools-vi] と同じ署名者・製品情報のアプリは、この PC にはありませんでした' -ForegroundColor DarkGray; return }
    foreach ($no in ($hitLines | Sort-Object)) {
        Write-Host ('  警告: 私的リスト ' + $no + ' 行目 の値は、この PC に入っているふつうのアプリと同じです（このまま公開すると、そのアプリを使っているホストで誤検知します）') -ForegroundColor Yellow
    }
    Write-Host ('  → 心当たりがなければ、その行を消すか、中身の指紋（[tools-sha]）で書いてください（件数と行番号だけを出しています）') -ForegroundColor Yellow
}

function Invoke-UpdateHidden {
    if (-not $script:HasHash) { Fail 'src\Net\AegisHash.cs を読み込めないので隠しリストを作れません（リポジトリの中で実行してください）' }
    if (-not (Test-Path -LiteralPath $File)) { Fail ('定義ファイルがありません: ' + $File) }
    $listPath = Get-PrivateListPath
    $entries = Read-PrivateList $listPath
    Write-Host ('私的リストを読みました: ' + $listPath + '（tools ' + $entries.Tools.Count + '、dlls ' + $entries.Dlls.Count + '、dllwords ' + $entries.DllWords.Count + '、ngwords ' + $entries.Ng.Count + '、ngallow ' + $entries.Al.Count + '、sha ' + $entries.Sha.Count + '、vi ' + $entries.Vi.Count + '、signer ' + $entries.Signer.Count + '、その他の節 ' + $entries.Other.Count + '）') -ForegroundColor Cyan
    $canon = [PrDefSign]::Canonical([IO.File]::ReadAllBytes($File))
    $salt = Get-OrMakeSalt $canon
    if ($salt.Made) { Write-Host '  hashsalt= がなかったので新しく作りました（この版から固定します。作り直すと全部のエントリを作り直す必要があります）' -ForegroundColor Yellow }
    # the renamed-tool kinds (sha / vi / signer) are written into [tools] alongside the tool / toolp lines (the mod reads them from [tools] or [dlls])
    $toolBlock = @(New-ToolHidden $entries.Tools $salt.Bytes) + @(New-ShaHidden $entries.Sha $salt.Bytes) + @(New-ViHidden $entries.Vi $salt.Bytes) + @(New-SignerHidden $entries.Signer $salt.Bytes)
    $gen = @{
        tools    = $toolBlock
        dlls     = New-DllHidden $entries.Dlls $salt.Bytes
        dllwords = New-WordHidden $entries.DllWords $salt.Bytes
        ngwords  = New-NgHidden $entries.Ng $salt.Bytes
        ngallow  = New-AlHidden $entries.Al $salt.Bytes
    }
    # review #12: refuse to drop existing #h1 lines without the author seeing the counts (Count-HiddenLines counts '#h1' lines,
    # which Get-SectionLines cannot — it drops every '#' line, so the old count was always 0)
    $before = @{}
    foreach ($sect in @('tools', 'dlls', 'dllwords', 'ngwords', 'ngallow')) { $before[$sect] = Count-HiddenLines $canon $sect }
    Write-Host ('  作った隠しエントリ: tools ' + $gen.tools.Count + '（前 ' + $before['tools'] + '）、dlls ' + $gen.dlls.Count + '（前 ' + $before['dlls'] + '）、dllwords ' + $gen.dllwords.Count + '（前 ' + $before['dllwords'] + '）、ngwords ' + $gen.ngwords.Count + '（前 ' + $before['ngwords'] + '）、ngallow ' + $gen.ngallow.Count + '（前 ' + $before['ngallow'] + '）')
    foreach ($sect in @('tools', 'dlls', 'dllwords', 'ngwords', 'ngallow')) {
        if ($gen[$sect].Count -lt $before[$sect]) {
            if (-not (Read-Yes ('  [' + $sect + '] の隠しエントリが ' + $before[$sect] + ' 個から ' + $gen[$sect].Count + ' 個に減ります。このまま作り直しますか'))) { Fail 'やめました（何も変えていません）' }
        }
    }
    $newText = Update-HiddenInText ($utf8.GetString($canon)) $salt $gen
    # missing header: a section with generated lines whose [header] is not in the file would silently lose those lines
    foreach ($sect in @('tools', 'dlls', 'dllwords', 'ngwords', 'ngallow')) {
        if ($gen[$sect].Count -gt 0 -and -not $script:HiddenEmitted.Contains($sect)) { Fail ('[' + $sect + '] の節が定義ファイルにないので、作った隠しエントリを書けません（先に定義ファイルに [' + $sect + '] の節を作ってください。[ngwords] は MOD の組み込み一覧を置き換えます）') }
    }
    # the result must pass the hidden-format check before it is written
    $probl = Get-HiddenProblem ([PrDefSign]::Canonical($utf8.GetBytes($newText)))
    if ($probl) { Fail ('作り直した定義ファイルの形式に問題があります（書き込んでいません）: ' + $probl) }
    [IO.File]::WriteAllText($File, $newText, $utf8)
    Write-Host ('隠しリストを書き直しました: ' + $File) -ForegroundColor Green
    Test-PrivateLeak $entries
    Test-HiddenFalsePositive $entries   # v0.5.5 review: 署名者・バージョン情報が、この PC のふつうのアプリと同じでないか
    Write-Host '次に: version= を上げてから、定義ファイルに署名する.cmd で署名してください（このコマンドは署名しません）。' -ForegroundColor Cyan
    exit 0
}

# v0.5.5 (F): the one-time transition. Once the committed, signed [update] minmod is at least v0.5.5 (so no v0.5.4 client, which
# treats a [tools] with no plain line as invalid, still runs), absorb the remaining plain [tools]/[dlls]/[dllwords]/[ngwords]/
# [ngallow] lines into the private list, hash them and DROP the plain lines. Never signs (bump version= and sign afterward).
# -Sections ngwords,ngallow converts only those two: they are v0.5.5 sections no older client reads, so they can be hashed
# before the floor is raised (v0.5.5 release decision 2026-09-23: no plain NG word may stay in the repository).
function Invoke-HashLegacy {
    if (-not $script:HasHash) { Fail 'src\Net\AegisHash.cs を読み込めないので実行できません（リポジトリの中で実行してください）' }
    if (-not (Test-Path -LiteralPath $File)) { Fail ('定義ファイルがありません: ' + $File) }
    $all = @('tools', 'dlls', 'dllwords', 'ngwords', 'ngallow')
    # -Sections: どの節を平文からハッシュに変えるか（既定は 5 つ全部）
    $want = $all
    if ($Sections.Trim().Length -gt 0) {
        $want = @($Sections -split '[,;\s]+' | ForEach-Object { $_.Trim().Trim('[', ']').ToLowerInvariant() } | Where-Object { $_ })
        foreach ($s in $want) { if ($all -notcontains $s) { Fail ('-Sections に書ける節は ' + ($all -join '・') + ' だけです: ' + $s) } }
        $want = @($want | Sort-Object -Unique)
        if ($want.Count -eq 0) { Fail '-Sections が空です' }
    }
    # NG の 2 節だけなら、必要な版のフロアは要りません（[ngwords]/[ngallow] は v0.5.5 からの節で、v0.5.4 は読まないため）。
    # チートの名前の節（tools/dlls/dllwords）が 1 つでも入っていれば、これまでどおりフロアを確かめます。
    $ngOnly = @($want | Where-Object { $_ -ne 'ngwords' -and $_ -ne 'ngallow' }).Count -eq 0
    if ($ngOnly) {
        Write-Host '  [ngwords] / [ngallow] だけの変換なので、必要な版のフロアは確かめません（この 2 つの節は v0.5.5 からで、v0.5.4 のクライアントは読みません）' -ForegroundColor DarkGray
    } else {
        $floor = ConvertTo-DefVersion $script:FirstScopedMod
        $base = Get-BaselineMinMod
        if (-not $base -or -not $base.Max -or (Compare-DefVersion $base.Max $floor) -lt 0) {
            Fail ('平文の一括変換は、コミット済みの署名付き定義ファイルの [update] minmod が v' + $script:FirstScopedMod + ' 以上になってからにしてください（今は ' + $(if ($base -and $base.Max) { 'v' + (Format-DefVersion $base.Max) } else { 'なし' }) + '）。v0.5.4 のトレイは平文のない [tools] を無効と見なすため、それまでは平文を残します。NG ワードだけなら -Sections ngwords,ngallow で先に変えられます')
        }
    }
    $listPath = Get-PrivateListPath
    $canon = [PrDefSign]::Canonical([IO.File]::ReadAllBytes($File))
    # the current plain lines of the chosen sections (inline '#' stripped), to absorb into the private list
    $absorb = @{}; $absorbTotal = 0
    foreach ($sect in $want) {
        $absorb[$sect] = New-Object Collections.Generic.List[string]
        foreach ($ln in (Get-SectionLines $canon $sect)) {
            $t = ($ln.Text -replace '#.*$', '').Trim()
            if ($t.Length -gt 0) { [void]$absorb[$sect].Add($t); $absorbTotal++ }
        }
    }
    if ($absorbTotal -eq 0) { Fail ('定義ファイルに平文の ' + (($want | ForEach-Object { '[' + $_ + ']' }) -join '/') + ' の行がありません（変換するものがありません）') }
    # dedupe against what the private list already holds (by exact text), then append a block per section
    $existing = Read-PrivateList $listPath
    $have = @{ tools = @(); dlls = @(); dllwords = @(); ngwords = @(); ngallow = @() }
    foreach ($e in @($existing.Tools)) { $have.tools += $e.T.ToLowerInvariant() }
    foreach ($e in @($existing.Dlls)) { $have.dlls += $e.T.ToLowerInvariant() }
    foreach ($e in @($existing.DllWords)) { $have.dllwords += $e.T.ToLowerInvariant() }
    foreach ($e in @($existing.Ng)) { $have.ngwords += $e.T.ToLowerInvariant() }
    foreach ($e in @($existing.Al)) { $have.ngallow += $e.T.ToLowerInvariant() }
    $blocks = New-Object Collections.Generic.List[string]
    [void]$blocks.Add('')
    [void]$blocks.Add('# --- absorbed from definitions.txt by -HashLegacy on ' + ([DateTime]::UtcNow.ToString('yyyy-MM-dd')) + ' (UTC) ---')
    $appended = 0
    foreach ($sect in $want) {
        $add = @($absorb[$sect] | Where-Object { $have[$sect] -notcontains $_.ToLowerInvariant() })
        if ($add.Count -eq 0) { continue }
        [void]$blocks.Add('[' + $sect + ']')
        foreach ($a in $add) { [void]$blocks.Add($a); $appended++ }
    }
    if ($appended -gt 0) {
        $cur = [IO.File]::ReadAllText($listPath, $utf8)
        if ($cur.Length -gt 0 -and -not $cur.EndsWith("`n")) { $cur += "`r`n" }
        [IO.File]::WriteAllText($listPath, $cur + (($blocks -join "`r`n") + "`r`n"), $utf8)
        Write-Host ('  私的リストに定義ファイルの平文 ' + $appended + ' 行を取り込みました（中身は出しません）: ' + $listPath) -ForegroundColor Cyan
    } else {
        Write-Host '  定義ファイルの平文は、すべて私的リストに入っていました' -ForegroundColor DarkGray
    }
    # now hash everything from the (augmented) private list and DROP the plain lines from those sections
    $entries = Read-PrivateList $listPath
    $salt = Get-OrMakeSalt $canon
    if ($salt.Made) { Write-Host '  hashsalt= がなかったので新しく作りました' -ForegroundColor Yellow }
    $toolBlock = @(New-ToolHidden $entries.Tools $salt.Bytes) + @(New-ShaHidden $entries.Sha $salt.Bytes) + @(New-ViHidden $entries.Vi $salt.Bytes) + @(New-SignerHidden $entries.Signer $salt.Bytes)
    $genAll = @{ tools = $toolBlock; dlls = New-DllHidden $entries.Dlls $salt.Bytes; dllwords = New-WordHidden $entries.DllWords $salt.Bytes; ngwords = New-NgHidden $entries.Ng $salt.Bytes; ngallow = New-AlHidden $entries.Al $salt.Bytes }
    # 選んだ節だけを作り直します（ほかの節の平文も #h1 もそのまま残ります）
    $gen = @{}; $drop = New-Object Collections.Generic.HashSet[string]
    foreach ($s in $want) { $gen[$s] = $genAll[$s]; [void]$drop.Add($s) }
    $newText = Update-HiddenInText ($utf8.GetString($canon)) $salt $gen $drop
    foreach ($sect in $want) {
        if ($gen[$sect].Count -gt 0 -and -not $script:HiddenEmitted.Contains($sect)) { Fail ('[' + $sect + '] の節が定義ファイルにないので書けません') }
    }
    Write-Host ('  作った隠しエントリ: ' + (($want | ForEach-Object { '[' + $_ + '] ' + $gen[$_].Count + ' 個' }) -join '、')) -ForegroundColor Cyan
    $probl = Get-ContentProblem ([PrDefSign]::Canonical($utf8.GetBytes($newText)))
    if ($probl) { Fail ('作り直した定義ファイルに問題があります（書き込んでいません）: ' + $probl) }
    [IO.File]::WriteAllText($File, $newText, $utf8)
    Write-Host (($want | ForEach-Object { '[' + $_ + ']' }) -join '/') -NoNewline -ForegroundColor Green
    Write-Host (' の平文をハッシュにして、平文の行を消しました: ' + $File) -ForegroundColor Green
    Test-PrivateLeak $entries
    Write-Host '次に: version= を上げてから、定義ファイルに署名する.cmd で署名してください（このコマンドは署名しません）。' -ForegroundColor Cyan
    exit 0
}

# ---------------------------------------------------------------- v0.5.5: -BuiltinNg (the mod's built-in NG list)
# 2026-09-23 の決定: MOD に組み込みの NG ワードの一覧も、DLL の中に読める形で残しません。src\Chat\NgText.cs の
# BUILTIN-NG-BEGIN / END の間の 2 つの配列を、私的リストの [ngwords] / [ngallow] から作り直します（ハッシュだけ。
# 塩は同じファイルの BuiltinSaltHex）。定義ファイルには触りません。行数だけを出し、語は出しません。
function Invoke-BuiltinNg {
    if (-not $script:HasHash) { Fail 'src\Net\AegisHash.cs を読み込めないので実行できません（リポジトリの中で実行してください）' }
    $cs = Join-Path $root 'src\Chat\NgText.cs'
    if (-not (Test-Path -LiteralPath $cs)) { Fail ('ファイルがありません: ' + $cs) }
    $listPath = Get-PrivateListPath
    $entries = Read-PrivateList $listPath
    $bytes = [IO.File]::ReadAllBytes($cs)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { Fail ('src\Chat\NgText.cs に BOM があります（BOM なしの UTF-8 にしてください）') }
    $text = $utf8.GetString($bytes)
    $nl = $(if ($text.Contains("`r`n")) { "`r`n" } else { "`n" })
    $ms = [regex]::Match($text, 'BuiltinSaltHex\s*=\s*"([0-9a-fA-F]{32,128})"')
    if (-not $ms.Success) { Fail 'src\Chat\NgText.cs に BuiltinSaltHex が見つかりません' }
    $salt = [PocketRoles.Net.AegisHash]::ParseSalt($ms.Groups[1].Value.ToLowerInvariant())
    if ($null -eq $salt) { Fail 'BuiltinSaltHex が 32〜128 桁の偶数桁の 16 進数ではありません' }
    $ng = @(New-NgHidden $entries.Ng $salt)
    $al = @(New-AlHidden $entries.Al $salt)
    if ($ng.Count -eq 0 -and $al.Count -eq 0) { Fail '私的リストの [ngwords] / [ngallow] が空です' }
    $beginRe = '(?s)(//\s*BUILTIN-NG-BEGIN[^\r\n]*\r?\n).*?(\s*//\s*BUILTIN-NG-END)'
    $mb = [regex]::Match($text, $beginRe)
    if (-not $mb.Success) { Fail 'src\Chat\NgText.cs に BUILTIN-NG-BEGIN / BUILTIN-NG-END の目印がありません' }
    $beforeNg = ([regex]::Matches($mb.Value, '"#h1 ng=')).Count
    $beforeAl = ([regex]::Matches($mb.Value, '"#h1 al=')).Count
    Write-Host ('  作った組み込みの隠しエントリ: ngwords ' + $ng.Count + '（前 ' + $beforeNg + '）、ngallow ' + $al.Count + '（前 ' + $beforeAl + '）') -ForegroundColor Cyan
    if ($ng.Count -lt $beforeNg -or $al.Count -lt $beforeAl) {
        if (-not (Read-Yes '  組み込みのエントリが減ります。このまま作り直しますか')) { Fail 'やめました（何も変えていません）' }
    }
    $sb = New-Object Text.StringBuilder
    [void]$sb.Append($mb.Groups[1].Value)
    [void]$sb.Append('        internal static readonly string[] BuiltinWordLines =' + $nl + '        {' + $nl)
    foreach ($l in $ng) { [void]$sb.Append('            "' + $l + '",' + $nl) }
    [void]$sb.Append('        };' + $nl + $nl)
    [void]$sb.Append('        internal static readonly string[] BuiltinAllowLines =' + $nl + '        {' + $nl)
    foreach ($l in $al) { [void]$sb.Append('            "' + $l + '",' + $nl) }
    [void]$sb.Append('        };' + $nl + '        // BUILTIN-NG-END')
    $newText = $text.Substring(0, $mb.Index) + $sb.ToString() + $text.Substring($mb.Index + $mb.Length)
    # 生成した行が読めない形でないか（クライアントと同じ規則で確かめる）
    foreach ($l in (@($ng) + @($al))) {
        if ($l -cnotmatch '^#h1 [a-z]+=[0-9a-f]{20}( [a-z]+=[0-9A-Za-z._-]+)*$') { Fail ('作った行の形が正しくありません（書き込んでいません）: ' + $l.Substring(0, [Math]::Min(16, $l.Length)) + '…') }
        if ($null -eq [PocketRoles.Net.AegisHash]::ParseHidden($l)) { Fail '作った行をクライアントが読めません（書き込んでいません）' }
    }
    [IO.File]::WriteAllText($cs, $newText, $utf8)
    Write-Host ('MOD の組み込み NG 一覧を作り直しました（平文は書いていません）: ' + $cs) -ForegroundColor Green
    Test-PrivateLeak $entries
    Write-Host '次に: dotnet build でビルドし直してください（定義ファイルには触っていません）。' -ForegroundColor Cyan
    exit 0
}

# ---------------------------------------------------------------- default: sign, then check
function Invoke-Sign {
    if (-not (Test-Path -LiteralPath $File)) { Fail ('定義ファイルがありません: ' + $File) }
    # the file first (the private key is opened only once the file may be signed)
    $raw = [IO.File]::ReadAllBytes($File)
    if ($raw.Length -gt 64 * 1024) { Fail '定義ファイルが 64 KB を超えています（MOD とトレイアプリが読みません）' }
    $canon = [PrDefSign]::Canonical($raw)
    $problem = Get-ContentProblem $canon
    if ($problem) { Fail $problem }
    # 署名する時だけの確認（-Verify では見ません。すでに署名したファイルはもう直せないため）
    $eraseProblem = Get-EraseCommentProblem $canon
    if ($eraseProblem) { Fail $eraseProblem }
    $ver = (Get-DefinitionsVersion $canon).Version
    if (-not (Test-Floor $ver)) { Fail 'version= を上げてから署名してください' }
    # (G) before signing: if a private list is remembered, warn (never a hard stop) when any private entry appears in plain in a
    # tracked repo file. Read only, no prompt (the leak check must never block an offline BAN-console signing).
    if ((Test-Path -LiteralPath $script:privateListRemember) -and $script:HasHash) {
        $lp = ''
        try { $lp = ([IO.File]::ReadAllText($script:privateListRemember, [Text.Encoding]::UTF8)).Trim() } catch { }
        if ($lp -and (Test-Path -LiteralPath $lp -PathType Leaf)) {
            try { Write-Host '  署名前の確認: 私的リストの語が平文で入っていないか調べます…' -ForegroundColor DarkGray; Test-PrivateLeak (Read-PrivateList $lp) } catch { Write-Host ('  メモ: 私的リストの漏えい確認をとばしました（' + $_.Exception.Message + '）') -ForegroundColor Yellow }
        }
    }
    # v0.5.5: version-scoped [rules] lines and [update] minmod (a raised minmod asks GitHub for the newest release)
    if (-not (Test-SignScope $canon)) { Fail '上の行を直してから署名してください（署名していません）' }
    # one version = one content, forever: this PC's ledger, the ledgers beside the key copies (a USB copy's) and the
    # signed versions committed in git (a new PC with the USB copy has no ledger of its own)
    $hash = [PrDefSign]::Sha256Hex($canon)
    $ledger = Read-Ledger
    $copies = @(Get-KeyCopies)   # listed only (no key is opened before the checks)
    $known = Get-SignedVersions $ledger $copies
    $signed = $known.Map
    $maxSigned = 0
    foreach ($kv in $signed.Keys) { if ($kv -gt $maxSigned) { $maxSigned = $kv } }
    $where = { param([int]$v) if ($known.From.ContainsKey($v)) { return (@($known.From[$v]) -join '、') } else { return '' } }
    if ($signed.ContainsKey($ver) -and -not (@($signed[$ver]) -contains $hash)) {
        Fail ('version=' + $ver + ' はもう別の中身で署名しています（記録: ' + (& $where $ver) + '）。同じ版の署名済みファイルが 2 つあると古い方で差し替えられてしまうので、version= を ' + ($maxSigned + 1) + ' 以上に上げてから署名してください（その版の .sig をまだ一度もコミットも push もしていないときに限り、台帳のその行を消せば同じ版で署名し直せます）')
    }
    if (-not $signed.ContainsKey($ver) -and $ver -lt $maxSigned) {
        Fail ('version=' + $ver + ' は署名済みの最新 v' + $maxSigned + ' より古い版です。version= を ' + ($maxSigned + 1) + ' 以上にしてください（記録: ' + (& $where $maxSigned) + '）')
    }
    Write-Host ('定義ファイルに署名します: ' + $File + '（version=' + $ver + '）') -ForegroundColor Cyan
    if ($copies.Count -eq 0) { Fail ('署名の鍵が見つかりません（' + $KeyFolder + '、USB メモリの「' + $keyFolderName + '」、' + $privPath + ' を探しました）。USB の控えを使うときは挿してから実行してください') }
    $plain = @($copies | Where-Object { $_.Plain })
    if ($plain.Count -gt 0) {
        $c = $plain[0]
        Write-Host ('  メモ: 鍵がパスワードで守られていません（' + $privPath + '）。「' + $keyFolderName + '」フォルダーの 鍵を守る.cmd で守ってください') -ForegroundColor Yellow
    } else { $c = Select-KeyCopy $copies }
    $u = Unlock-KeyCopy $c
    [PrKeyFile]::Wipe($u.Password)
    $key = $u.Key
    if ([PrDefSign]::KeyBitsOf($key) -lt 3072) { [PrKeyFile]::Wipe($key); Fail '秘密鍵が 3072 ビットより短いので使いません' }
    $fp = [PrDefSign]::Fingerprint([PrDefSign]::PublicXmlOf($key))
    $why = Get-KeyTrustProblem $fp
    if ($why) { [PrKeyFile]::Wipe($key); Fail $why }
    $sig = [PrDefSign]::SignWith($canon, $key)
    [PrKeyFile]::Wipe($key)
    # the ledger entry before the .sig: a signature never exists without its entry
    $line = [string]$ver + "`t" + $hash + "`t" + [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture) + "`r`n"
    if (-not ($ledger.ContainsKey($ver) -and (@($ledger[$ver]) -contains $hash))) {
        $entry = ''
        if (-not (Test-Path -LiteralPath $ledgerPath)) {
            if (-not (Test-Path -LiteralPath $KeyDir)) { [void][IO.Directory]::CreateDirectory($KeyDir) }
            $entry = "# PocketRoles definitions: version, SHA-256 of the signed bytes (BOM removed, CRLF -> LF), signed at (UTC). One version = one content.`r`n"
        }
        $entry += $line
        try { [IO.File]::AppendAllText($ledgerPath, $entry, $utf8) } catch { Fail ('署名した版を台帳に書けませんでした（.sig は書いていません）: ' + $ledgerPath + ' — ' + $_.Exception.Message) }
    }
    # the ledgers beside the other key copies (the USB copy's, when it is plugged in) get the entry too
    foreach ($cc in @($copies)) {
        if ($cc.Plain) { continue }
        $f = Join-Path (Split-Path -Parent $cc.Path) 'signed-versions.txt'
        if ([IO.Path]::GetFullPath($f) -eq [IO.Path]::GetFullPath($ledgerPath) -or -not (Test-Path -LiteralPath $f)) { continue }
        try { Merge-Ledger $ledgerPath $f } catch { Write-Host ('  メモ: 鍵の控えの横の台帳に書けませんでした: ' + $f) -ForegroundColor Yellow }
    }
    $old = if (Test-Path -LiteralPath $sigPath) { [IO.File]::ReadAllText($sigPath, [Text.Encoding]::UTF8) } else { '' }
    $new = [Convert]::ToBase64String($sig) + "`r`n" + 'keyid=' + $fp.Substring(0, 16) + "`r`n"
    [IO.File]::WriteAllText($sigPath, $new, $utf8)
    if ($old -eq $new) { Write-Host ('署名は前と同じです（定義ファイルは変わっていません）: ' + $sigPath) }
    else { Write-Host ('署名しました: ' + $sigPath + '（version=' + $ver + '）。definitions.txt と一緒にコミットして main に push してください') -ForegroundColor Green }
    if (Test-Signature) { exit 0 } else { exit 1 }
}

# ---------------------------------------------------------------- -Unlock: the Aegis BAN console's owner check
# (v0.5.5; 2026-09-22 「管理画面に入るときのスキャン手薄すぎない？」. aegis\AegisBan.ps1 UnlockProof reads the answer.)
# The console makes a random number (32 hex digits) and runs this in a visible console; the owner picks the key copy and
# types its password here, exactly as for signing (nothing is shown, nothing secret is written). Signed with the
# definitions key (trusted by the mod and both tray apps, never a revoked one; RSA / SHA-256 / PKCS#1 v1.5):
#   "PocketRoles.Aegis.Unlock.v1|<number>|<UTC time>|<machine>|<user>"
# machine / user: the PC's and the Windows user's names plus 16 hex digits of a SHA-256 of the MachineGuid / the user's SID
# (the console's Ident builds the same strings, so an answer made on another PC or for another user does not open it).
# Written to -UnlockOut (must be "response-<number>.txt" in an existing folder outside the repository): the fields and the
# signature only. No definitions file, ledger or key file is changed.

# 16 hex digits of the SHA-256 of a text (UTF-8)
function Get-H16([string]$s) { return ([PrDefSign]::Sha256Hex($utf8.GetBytes($s))).Substring(0, 16) }

function Get-UnlockMachine {
    $guid = ''
    try {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
        try {
            $k = $base.OpenSubKey('SOFTWARE\Microsoft\Cryptography')
            if ($k) { try { $guid = [string]$k.GetValue('MachineGuid') } finally { $k.Dispose() } }
        } finally { $base.Dispose() }
    } catch { $guid = '' }
    return [Environment]::MachineName + '/' + (Get-H16 ('PocketRoles.Aegis.Machine|' + $guid.Trim().ToLowerInvariant()))
}

function Get-UnlockUser {
    $sid = ''
    try { $w = [Security.Principal.WindowsIdentity]::GetCurrent(); try { if ($w.User) { $sid = $w.User.Value } } finally { $w.Dispose() } } catch { $sid = '' }
    return [Environment]::UserName + '/' + (Get-H16 ('PocketRoles.Aegis.User|' + $sid))
}

function Invoke-Unlock {
    $nonce = [string]$Unlock
    if ($nonce -cnotmatch '^[0-9a-f]{32}$') { Fail '番号（-Unlock）は 16 進数 32 桁で指定してください（BAN 管理が作ります）' }
    if (-not $UnlockOut) { Fail '-UnlockOut（応答を書くファイル）がありません' }
    $out = [IO.Path]::GetFullPath($UnlockOut)
    if ([IO.Path]::GetFileName($out) -cne ('response-' + $nonce + '.txt')) { Fail ('応答のファイル名が違います（response-<番号>.txt）: ' + $out) }
    $dir = Split-Path -Parent $out
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { Fail ('応答を書くフォルダーがありません: ' + $dir) }
    if (Test-InsideRepo $out) { Fail ('応答はリポジトリの中には書きません: ' + $out) }
    Write-Host 'Aegis BAN 管理の本人確認: 署名の鍵で番号に署名します（定義ファイルは変えません）。' -ForegroundColor Cyan
    Write-Host ('  番号: ' + $nonce)
    $copies = @(Get-KeyCopies)
    if ($copies.Count -eq 0) { Fail ('署名の鍵が見つかりません（' + $KeyFolder + '、USB メモリの「' + $keyFolderName + '」、' + $privPath + ' を探しました）。USB の控えを使うときは挿してから実行してください') }
    $plain = @($copies | Where-Object { $_.Plain })
    if ($plain.Count -gt 0) {
        $c = $plain[0]
        Write-Host ('  メモ: 鍵がパスワードで守られていません（' + $privPath + '）。「' + $keyFolderName + '」フォルダーの 鍵を守る.cmd で守ってください') -ForegroundColor Yellow
    } else { $c = Select-KeyCopy $copies }
    $u = Unlock-KeyCopy $c
    [PrKeyFile]::Wipe($u.Password)
    $key = $u.Key
    if ([PrDefSign]::KeyBitsOf($key) -lt 3072) { [PrKeyFile]::Wipe($key); Fail '秘密鍵が 3072 ビットより短いので使いません' }
    $pub = [PrDefSign]::PublicXmlOf($key)
    $fp = [PrDefSign]::Fingerprint($pub)
    $why = Get-KeyTrustProblem $fp
    if ($why) { [PrKeyFile]::Wipe($key); Fail $why }
    $at = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
    $machine = Get-UnlockMachine
    $user = Get-UnlockUser
    $msg = 'PocketRoles.Aegis.Unlock.v1|' + $nonce + '|' + $at + '|' + $machine + '|' + $user
    $data = $utf8.GetBytes($msg)
    $sig = [PrDefSign]::SignWith($data, $key)
    [PrKeyFile]::Wipe($key)
    if (-not [PrDefSign]::Verify($data, $sig, $pub)) { Fail '署名を確かめられませんでした（応答は書いていません）' }
    $kid = $fp.Substring(0, 16)
    $text = "# PocketRoles Aegis BAN console: proof that the owner holds the signing key (not secret; used once)`r`n" +
        'format=PocketRoles.Aegis.Unlock.v1' + "`r`n" + 'nonce=' + $nonce + "`r`n" + 'at=' + $at + "`r`n" + 'machine=' + $machine + "`r`n" +
        'user=' + $user + "`r`n" + 'keyid=' + $kid + "`r`n" + 'sig=' + [Convert]::ToBase64String($sig) + "`r`n"
    [IO.File]::WriteAllText($out, $text, $utf8)
    Write-Host ('本人確認の応答を書きました（鍵 ' + $kid + '。パスワードも鍵も書いていません）: ' + $out) -ForegroundColor Green
    exit 0
}

# ---------------------------------------------------------------- -SignRelease / -VerifyRelease: dist\SHA256SUMS.txt
# (v0.5.5, 2026-09-23) The release's own files. dist\SHA256SUMS.txt lists the SHA-256 of every file attached to a GitHub
# release. GitHub computes its own digest for each asset and the sums file sits in the release too, but both live where
# the zip lives: whoever can replace the zip can replace them with it. A signature made with this key does not, because
# the key never leaves this PC and the Client carries only the public half. Same algorithm, same canonical bytes and the
# same .sig shape as the definitions file (RSA 3072 / SHA-256 / PKCS#1 v1.5; BOM removed, CRLF -> LF).
# The two kinds of file can never be swapped for one another:
#   - a sums file has no "version=N" line, so the mod and both tray apps refuse it as a definitions file
#     (Get-ContentProblem, and -Sign / -Verify refuse it here for the same reason);
#   - a definitions file has no "# format=PocketRoles.Release.Sums.v1" line, so this mode refuses it.
# A ledger, like the definitions one: release= alone is NOT enough. It only says the tag matches; it does not stop two
# signed sums files existing for the same tag. Running build-release.ps1 twice for the same <Version> gives different zip
# bytes (a zip carries timestamps), so a second, equally valid signed set can be made, and re-uploading to the same tag is
# real practice (PocketRolesLauncher.ps1: "a re-published zip with the same version number still gets installed"). Whoever
# kept the first published set (SHA256SUMS.txt + .sig + the old zips) could then put the older build back and pass the
# signature, the format check and release=. KeyDir\signed-releases.txt holds "one release = one content" the same way
# signed-versions.txt does for the definitions file (support\RELEASE-SIGNING.md 4).
$ReleaseSumsFormat = 'PocketRoles.Release.Sums.v1'
$ReleaseSumsName = 'SHA256SUMS.txt'

# the parts of a release sums file. Format / Release: the values of the "# format=" / "# release=" lines; Files: the
# "<64 hex><space><space><name>" lines as @{ Hash; Name }; Problem: why it is not a release sums file ('' = it is one).
# Read exactly as support\CLIENT-SUMS-VERIFY-SPEC.md 1 B 2 tells the Client to read it: no per-line Trim (a hash line with
# a stray space is refused here too) and exactly one "# " (a "##format=" line is an ordinary comment, so the format line
# is then missing and the file is refused). If this side were the looser of the two, a hand-edited list could be signed
# here and then refused by every Client.
function Read-ReleaseSums([byte[]]$canonical) {
    $fmt = ''; $rel = ''; $files = @(); $problem = ''; $nfmt = 0; $nrel = 0; $names = @{}
    foreach ($line in ($utf8.GetString($canonical) -split "`n")) {
        $t = $line.TrimStart([char]0xFEFF)
        if (-not $t) { continue }
        if ($t.StartsWith('#')) {
            if ($t -cmatch '^# format=(.+)$') { $nfmt++; if ($nfmt -eq 1) { $fmt = $Matches[1] } }
            elseif ($t -cmatch '^# release=(.+)$') { $nrel++; if ($nrel -eq 1) { $rel = $Matches[1] } }
            continue
        }
        if ($t -cmatch '^([0-9a-f]{64})  ([^\\/:*?"<>|]+)$') {
            $nm = $Matches[2]
            if ($names.ContainsKey($nm.ToLowerInvariant())) { $problem = ('同じファイル名が 2 回あります: ' + $nm); break }
            $names[$nm.ToLowerInvariant()] = $true
            $files += [pscustomobject]@{ Hash = $Matches[1]; Name = $nm }
            continue
        }
        $short = $(if ($t.Length -gt 60) { $t.Substring(0, 60) + '…' } else { $t })
        $problem = ('読めない行があります（「64 桁の小文字の 16 進数」＋半角スペース 2 つ＋ファイル名 だけにしてください）: ' + $short)
        break
    }
    if (-not $problem) {
        if ($nfmt -ne 1) { $problem = ('「# format=' + $ReleaseSumsFormat + '」の行が ' + $nfmt + ' 行あります。1 行にしてください') }
        elseif ($fmt -ne $ReleaseSumsFormat) { $problem = ('このファイルの format が違います（' + $fmt + '）。リリース用は「' + $ReleaseSumsFormat + '」です') }
        elseif ($nrel -ne 1) { $problem = ('「# release=<版>」の行が ' + $nrel + ' 行あります。1 行にしてください') }
        elseif ($rel -notmatch '^[0-9]+(\.[0-9]+){1,3}$') { $problem = ('「# release=」の版の書き方が違います（例 0.5.5）: ' + $rel) }
        elseif (@($files).Count -eq 0) { $problem = 'ハッシュの行が 1 行もありません' }
    }
    return [pscustomobject]@{ Format = $fmt; Release = $rel; Files = @($files); Problem = $problem }
}

# ---- the ledger of signed releases (not secret): release -> the SHA-256s of the signed canonical bytes ----

function Add-SignedRelease($map, [string]$rel, [string]$hash) {
    if (-not $map.ContainsKey($rel)) { $map[$rel] = @() }
    if ($map[$rel] -notcontains $hash) { $map[$rel] = @($map[$rel]) + $hash }
}

function Read-ReleaseLedgerFile([string]$path, $map) {
    if (-not $path -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { return }
    foreach ($line in [IO.File]::ReadAllLines($path, [Text.Encoding]::UTF8)) {
        $t = $line.Trim()
        if (-not $t -or $t.StartsWith('#')) { continue }
        $p = @($t -split '\s+')
        if ($p.Count -ge 2 -and $p[0] -match '^[0-9]+(\.[0-9]+){1,3}$' -and $p[1] -match '^[0-9a-fA-F]{64}$') { Add-SignedRelease $map $p[0] $p[1].ToLowerInvariant() }
    }
}

# this PC's ledger plus the ledgers beside the reachable key copies (a USB copy's), so that "one release = one content"
# still holds when this PC's ledger is gone (signing on a new PC with the USB copy)
function Read-ReleaseLedger($copies) {
    $map = @{}
    Read-ReleaseLedgerFile $relLedgerPath $map
    foreach ($c in @($copies)) {
        if ($c.Plain) { continue }
        $f = Join-Path (Split-Path -Parent $c.Path) 'signed-releases.txt'
        if ([IO.Path]::GetFullPath($f) -eq [IO.Path]::GetFullPath($relLedgerPath)) { continue }
        Read-ReleaseLedgerFile $f $map
    }
    return $map
}

function Merge-ReleaseLedger([string]$from, [string]$to) {
    if (-not (Test-Path -LiteralPath $to)) { [IO.File]::Copy($from, $to); return }
    $have = @{}
    Read-ReleaseLedgerFile $to $have
    $add = ''
    foreach ($line in [IO.File]::ReadAllLines($from, [Text.Encoding]::UTF8)) {
        $t = $line.Trim()
        if (-not $t -or $t.StartsWith('#')) { continue }
        $p = @($t -split '\s+')
        if ($p.Count -lt 2 -or $p[0] -notmatch '^[0-9]+(\.[0-9]+){1,3}$' -or $p[1] -notmatch '^[0-9a-fA-F]{64}$') { continue }
        if ($have.ContainsKey($p[0]) -and ($have[$p[0]] -contains $p[1].ToLowerInvariant())) { continue }
        $add += $t + "`r`n"
    }
    if ($add) { [IO.File]::AppendAllText($to, $add, $utf8) }
}

# why this file may not be signed / trusted as a release sums file ('' = it may). $checkFiles: also re-read every file it
# lists (they must lie next to it) and compare, so a sums file left over from an older build can never be signed.
function Get-ReleaseSumsProblem([string]$path, [byte[]]$canonical, [bool]$checkFiles) {
    if ([PrDefSign]::HasCr($canonical)) { return '改行以外の CR（CR CR LF や単独の CR）が残っています。改行を CRLF か LF にそろえてください' }
    if ([PrDefSign]::HasBom($canonical)) { return 'BOM（U+FEFF）が先頭以外にもあります。取り除いてください' }
    if ((Split-Path -Leaf $path) -ne $ReleaseSumsName) { return ('リリース用に署名できるのは「' + $ReleaseSumsName + '」という名前のファイルだけです: ' + (Split-Path -Leaf $path)) }
    $s = Read-ReleaseSums $canonical
    if ($s.Problem) { return $s.Problem }
    if ((Get-DefinitionsVersion $canonical).Count -gt 0) { return 'このファイルには version= の行があります。定義ファイルは -Sign で署名してください（リリース用の署名と混ざらないようにしています）' }
    if (-not $checkFiles) { return '' }
    $dir = Split-Path -Parent ([IO.Path]::GetFullPath($path))
    foreach ($f in @($s.Files)) {
        $p = Join-Path $dir $f.Name
        if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { return ('この一覧に書いてあるファイルが横にありません: ' + $p) }
        $h = (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($h -ne $f.Hash) { return ('「' + $f.Name + '」の中身が一覧と違います（作り直したあとの古い一覧かもしれません）。build-release.ps1 をもう一度実行してから署名してください') }
    }
    return ''
}

# checks a release sums file against its .sig with the same trusted keys as the definitions file; prints the result
function Test-ReleaseSignature([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { Write-Host ('ファイルがありません: ' + $full) -ForegroundColor Red; return $false }
    $canon = [PrDefSign]::Canonical([IO.File]::ReadAllBytes($full))
    $problem = Get-ReleaseSumsProblem $full $canon $true
    if ($problem) { Write-Host ($ReleaseSumsName + ' の形式: ' + $problem) -ForegroundColor Red; return $false }
    $s = Read-Sig ($full + '.sig')
    if (-not $s) { Write-Host ('署名ファイルがない、または形式が違います: ' + $full + '.sig') -ForegroundColor Red; return $false }
    $lists = @(Get-TrustLists)
    $ok = Test-TrustLists $lists
    $sid = $s.KeyId.ToLowerInvariant()
    foreach ($l in $lists) {
        if ($l.Problem -or $l.Keys.Count -eq 0) { continue }   # already printed
        if ($sid -and ($l.Revoked -contains $sid)) { Write-Host ('  ' + $l.Name + ': 無効にした鍵（keyid=' + $s.KeyId + '）で署名されています') -ForegroundColor Red; $ok = $false; continue }
        $cands = @($l.Keys | Where-Object { -not ($l.Revoked -contains $_.Id.ToLowerInvariant()) -and (-not $sid -or $_.Id.ToLowerInvariant() -eq $sid) })
        if ($cands.Count -eq 0) { Write-Host ('  ' + $l.Name + ': 信頼する鍵の一覧にない鍵（keyid=' + $s.KeyId + '）で署名されています') -ForegroundColor Red; $ok = $false; continue }
        $hit = $null
        foreach ($k in $cands) { if ([PrDefSign]::Verify($canon, $s.Sig, $k.Xml)) { $hit = $k; break } }
        if ($hit) { Write-Host ('  ' + $l.Name + ': 署名 OK（鍵 ' + $hit.Id.ToLowerInvariant() + '）') }
        else { Write-Host ('  ' + $l.Name + ': 署名が合いません（署名のあとで一覧が変わった可能性）') -ForegroundColor Red; $ok = $false }
    }
    if ($ok) {
        $r = Read-ReleaseSums $canon
        Write-Host ('検証 OK: ' + $full + '（release=' + $r.Release + '、ファイル ' + @($r.Files).Count + " 個）") -ForegroundColor Green
    }
    return $ok
}

function Invoke-SignRelease([string]$path) {
    $full = ''
    try { $full = [IO.Path]::GetFullPath($path) } catch { Fail ('ファイルの場所が読めません: ' + $path) }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { Fail ('ファイルがありません: ' + $full) }
    $raw = [IO.File]::ReadAllBytes($full)
    if ($raw.Length -gt 64 * 1024) { Fail ($ReleaseSumsName + ' が 64 KB を超えています') }
    $canon = [PrDefSign]::Canonical($raw)
    $problem = Get-ReleaseSumsProblem $full $canon $true
    if ($problem) { Fail $problem }
    $sums = Read-ReleaseSums $canon
    # one release = one content, forever (the note above $ReleaseSumsFormat). Checked before the private key is opened.
    $hash = [PrDefSign]::Sha256Hex($canon)
    $copies = @(Get-KeyCopies)   # listed only (no key is opened before the checks)
    $relLedger = Read-ReleaseLedger $copies
    if ($relLedger.ContainsKey($sums.Release) -and -not (@($relLedger[$sums.Release]) -contains $hash)) {
        Fail ('release=' + $sums.Release + ' はもう別の中身で署名しています（台帳: ' + $relLedgerPath + '）。同じ版の署名済みの一覧が 2 つあると、先に公開した組（一覧・署名・古い zip）を置き直すだけで古いビルドを配れてしまいます。版を上げてからビルドし直してください（その版をまだ 1 度も公開していないときに限り、台帳のその行を消せば同じ版で署名し直せます）')
    }
    Write-Host ('リリースのファイルの一覧に署名します: ' + $full + '（release=' + $sums.Release + '）') -ForegroundColor Cyan
    foreach ($f in @($sums.Files)) { Write-Host ('    ' + $f.Hash + '  ' + $f.Name) }
    if ($copies.Count -eq 0) { Fail ('署名の鍵が見つかりません（' + $KeyFolder + '、USB メモリの「' + $keyFolderName + '」、' + $privPath + ' を探しました）。USB の控えを使うときは挿してから実行してください') }
    $plain = @($copies | Where-Object { $_.Plain })
    if ($plain.Count -gt 0) {
        $c = $plain[0]
        Write-Host ('  メモ: 鍵がパスワードで守られていません（' + $privPath + '）。「' + $keyFolderName + '」フォルダーの 鍵を守る.cmd で守ってください') -ForegroundColor Yellow
    } else { $c = Select-KeyCopy $copies }
    $u = Unlock-KeyCopy $c
    [PrKeyFile]::Wipe($u.Password)
    $key = $u.Key
    if ([PrDefSign]::KeyBitsOf($key) -lt 3072) { [PrKeyFile]::Wipe($key); Fail '秘密鍵が 3072 ビットより短いので使いません' }
    $fp = [PrDefSign]::Fingerprint([PrDefSign]::PublicXmlOf($key))
    $why = Get-KeyTrustProblem $fp
    if ($why) { [PrKeyFile]::Wipe($key); Fail $why }
    $sig = [PrDefSign]::SignWith($canon, $key)
    [PrKeyFile]::Wipe($key)
    $kid = $fp.Substring(0, 16)
    # the ledger entry before the .sig: a signature never exists without its entry
    if (-not ($relLedger.ContainsKey($sums.Release) -and (@($relLedger[$sums.Release]) -contains $hash))) {
        $entry = ''
        if (-not (Test-Path -LiteralPath $relLedgerPath)) {
            if (-not (Test-Path -LiteralPath $KeyDir)) { [void][IO.Directory]::CreateDirectory($KeyDir) }
            $entry = "# PocketRoles releases: release, SHA-256 of the signed bytes (BOM removed, CRLF -> LF), signed at (UTC). One release = one content.`r`n"
        }
        $entry += [string]$sums.Release + "`t" + $hash + "`t" + [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture) + "`r`n"
        try { [IO.File]::AppendAllText($relLedgerPath, $entry, $utf8) } catch { Fail ('署名した版を台帳に書けませんでした（.sig は書いていません）: ' + $relLedgerPath + ' — ' + $_.Exception.Message) }
    }
    # the ledgers beside the other key copies (the USB copy's, when it is plugged in) get the entry too
    foreach ($cc in @($copies)) {
        if ($cc.Plain) { continue }
        $f = Join-Path (Split-Path -Parent $cc.Path) 'signed-releases.txt'
        if ([IO.Path]::GetFullPath($f) -eq [IO.Path]::GetFullPath($relLedgerPath) -or -not (Test-Path -LiteralPath $f)) { continue }
        try { Merge-ReleaseLedger $relLedgerPath $f } catch { Write-Host ('  メモ: 鍵の控えの横の台帳に書けませんでした: ' + $f) -ForegroundColor Yellow }
    }
    [IO.File]::WriteAllText(($full + '.sig'), ([Convert]::ToBase64String($sig) + "`r`n" + 'keyid=' + $kid + "`r`n"), $utf8)
    Write-Host ('署名しました: ' + $full + '.sig（鍵 ' + $kid + '）。リリースには zip と一緒に ' + $ReleaseSumsName + ' と ' + $ReleaseSumsName + '.sig の両方を付けてください') -ForegroundColor Green
    if (Test-ReleaseSignature $full) { exit 0 } else { exit 1 }
}

function Invoke-Main {
    # v0.5.5 merge 9/23: one list with all 11 modes (hidden-defs added -UpdateHidden / -HashLegacy / -BuiltinNg, release-sums added -SignRelease / -VerifyRelease)
    $modes = @(@($Init.IsPresent, $Verify.IsPresent, $Protect.IsPresent, $doBackup, $Sign.IsPresent, $doUnlock, $UpdateHidden.IsPresent, $HashLegacy.IsPresent, $BuiltinNg.IsPresent, $doSignRelease, $doVerifyRelease) | Where-Object { $_ }).Count
    if ($modes -gt 1) { Fail '-Init・-Verify・-Protect・-Backup・-Sign・-Unlock・-UpdateHidden・-HashLegacy・-BuiltinNg・-SignRelease・-VerifyRelease はどれか 1 つだけにしてください' }
    if ($Sections.Trim().Length -gt 0 -and -not $HashLegacy) { Fail '-Sections は -HashLegacy と一緒にだけ使えます' }
    if ($Init) { Invoke-Init }
    if ($Verify) { if (Test-Signature) { exit 0 } else { exit 1 } }
    if ($Protect) { Invoke-Protect }
    if ($doBackup) { Invoke-Backup }
    if ($doUnlock) { Invoke-Unlock }
    if ($UpdateHidden) { Invoke-UpdateHidden }
    if ($HashLegacy) { Invoke-HashLegacy }
    if ($BuiltinNg) { Invoke-BuiltinNg }
    if ($doVerifyRelease) { if (Test-ReleaseSignature $VerifyRelease) { exit 0 } else { exit 1 } }
    if ($doSignRelease) { Invoke-SignRelease $SignRelease }
    Invoke-Sign
}

# dot-sourced (the scratch tests replace Read-Line / Read-Secret, then call Invoke-Main): only the definitions above
if ($MyInvocation.InvocationName -ne '.') { Invoke-Main }
