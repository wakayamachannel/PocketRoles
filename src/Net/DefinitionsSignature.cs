using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.5 (2026-09-22 request "定義ファイルに署名を付ける（偽の定義ファイルを読ませる攻撃を防ぐ）"): the detached signature of the
    /// Aegis definitions file. aegis/definitions.txt.sig, next to aegis/definitions.txt (GitHub raw: the same URL + ".sig"), holds
    /// the base64 RSA signature on one line, optionally followed by a "keyid=" line (the first 16 hex digits of the SHA-256 of
    /// the signing key's SubjectPublicKeyInfo). RSA 3072, SHA-256, PKCS#1 v1.5, over the CANONICAL form of the file: its UTF-8
    /// bytes with a leading BOM removed and every CRLF turned into LF (GitHub raw serves LF, a Windows working copy has CRLF:
    /// both verify with the same signature).
    ///
    /// Signed by the maintainer with tools/sign-definitions.ps1 (the private key never leaves that PC); the public key is
    /// compiled into the mod (AegisRules) and the tray app (aegis/Aegis.ps1, class Sig, the same rules in C# 5).
    /// Pure .NET, no Unity or BepInEx: safe on any thread, and tested on its own.
    ///
    /// Several keys (2026-09-22 request "将来ほかの管理者の鍵も足せるように"): the mod and the tray apps trust a LIST of public
    /// keys and refuse a list of revoked key ids, both changed only by a release (AegisRules.TrustedKeys / RevokedKeyIds,
    /// Sig.TrustedKeys / RevokedKeyIds of aegis/Aegis.ps1 and aegis/AegisBan.ps1; tools/sign-definitions.ps1 -Verify checks
    /// the lists are the same everywhere). A .sig naming a revoked id is refused; one naming a key checks with that key only;
    /// one without a keyid= line checks with each trusted key. A key is used only when its declared id is its own.
    /// </summary>
    internal static class DefinitionsSignature
    {
        internal enum Status { Ok, Missing, Invalid, Revoked }

        /// <summary>
        /// A trusted public key: <see cref="Id"/> (the first 16 hex digits of the SHA-256 of its SubjectPublicKeyInfo, as a
        /// .sig's keyid= line names it) and the RSA modulus / exponent in base64 (as in the key's XML form). Immutable; the
        /// check that the id is the key's own runs once, on first use (a mistyped id makes the key unusable, never another's).
        /// </summary>
        internal sealed class PublicKey
        {
            internal readonly string Id, ModulusB64, ExponentB64;
            private volatile int _idState;   // 0 = not checked yet, 1 = the id is this key's own, 2 = it is not

            internal PublicKey(string id, string modulusB64, string exponentB64)
            {
                Id = id ?? ""; ModulusB64 = modulusB64 ?? ""; ExponentB64 = exponentB64 ?? "";
            }

            internal bool IdMatches
            {
                get
                {
                    int s = _idState;
                    if (s == 0) _idState = s = Id.Length == 16 && string.Equals(KeyIdOf(ModulusB64, ExponentB64), Id, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                    return s == 1;
                }
            }
        }

        /// <summary>The key id of an RSA public key (see <see cref="PublicKey.Id"/>), lower-case; "" when it is not a key.</summary>
        internal static string KeyIdOf(string modulusB64, string exponentB64)
        {
            try
            {
                using (var rsa = RSA.Create())
                using (var sha = SHA256.Create())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(modulusB64), Exponent = Convert.FromBase64String(exponentB64) });
                    return Convert.ToHexString(sha.ComputeHash(rsa.ExportSubjectPublicKeyInfo()), 0, 8).ToLowerInvariant();
                }
            }
            catch (CryptographicException) { return ""; }
            catch (FormatException) { return ""; }
            catch (ArgumentException) { return ""; }
        }

        /// <summary>True when <paramref name="keyId"/> is in <paramref name="revoked"/> (any case).</summary>
        internal static bool IsRevoked(string keyId, IReadOnlyCollection<string> revoked)
        {
            if (string.IsNullOrEmpty(keyId) || revoked == null) return false;
            foreach (var r in revoked) if (string.Equals(r, keyId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Longest .sig text read (a 3072-bit signature is 512 base64 characters).</summary>
        internal const int MaxSigChars = 4096;

        /// <summary>
        /// The bytes that are signed: a leading UTF-8 BOM removed, CRLF → LF (a lone CR is kept). Both caches store these bytes
        /// and canonicalize them again at the next start, so tools/sign-definitions.ps1 refuses a file whose canonical form
        /// still holds a CR (CR CR LF, a lone CR) or another BOM: for every signed file Canonical(Canonical(x)) == Canonical(x).
        /// </summary>
        internal static byte[] Canonical(byte[] data)
        {
            if (data == null) return Array.Empty<byte>();
            int start = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            var o = new byte[data.Length - start];
            int n = 0;
            for (int i = start; i < data.Length; i++)
            {
                if (data[i] == 0x0D && i + 1 < data.Length && data[i + 1] == 0x0A) continue;
                o[n++] = data[i];
            }
            if (n != o.Length) Array.Resize(ref o, n);
            return o;
        }

        /// <summary>
        /// The .sig text: one base64 line (the signature) and an optional "keyid=" line; blank lines and '#' lines are skipped.
        /// False when there is no signature line, more than one, or it is not base64.
        /// </summary>
        internal static bool TryParseSig(string sigText, out byte[] sig, out string keyId)
        {
            sig = null; keyId = null;
            if (string.IsNullOrEmpty(sigText) || sigText.Length > MaxSigChars) return false;
            foreach (var raw in sigText.Split('\n'))
            {
                string line = raw.Trim().TrimStart('﻿');
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("keyid=", StringComparison.OrdinalIgnoreCase)) { keyId = line.Substring(6).Trim(); continue; }
                if (sig != null) { sig = null; return false; }
                try { sig = Convert.FromBase64String(line); }
                catch (FormatException) { sig = null; return false; }
            }
            return sig != null && sig.Length > 0;
        }

        /// <summary>
        /// <paramref name="canonical"/> (see <see cref="Canonical"/>) against the .sig text with the trusted keys. Missing: no
        /// .sig text at all. Revoked: the .sig names a key id in <paramref name="revoked"/>. Invalid: a malformed .sig, a key id
        /// that is not trusted, or a signature that matches no usable key (revoked keys and keys whose id is not their own are
        /// never used, even when listed in <paramref name="trusted"/>). Never throws.
        /// </summary>
        internal static Status Verify(byte[] canonical, string sigText, IReadOnlyList<PublicKey> trusted, IReadOnlyCollection<string> revoked)
        {
            if (string.IsNullOrWhiteSpace(sigText)) return Status.Missing;
            if (canonical == null || !TryParseSig(sigText, out var sig, out var id)) return Status.Invalid;
            if (IsRevoked(id, revoked)) return Status.Revoked;
            if (trusted == null) return Status.Invalid;
            foreach (var key in trusted)
            {
                if (key == null || IsRevoked(key.Id, revoked)) continue;
                if (!string.IsNullOrEmpty(id) && !string.Equals(id, key.Id, StringComparison.OrdinalIgnoreCase)) continue;
                if (!key.IdMatches) continue;
                if (VerifyWith(canonical, sig, key)) return Status.Ok;
            }
            return Status.Invalid;
        }

        private static bool VerifyWith(byte[] canonical, byte[] sig, PublicKey key)
        {
            try
            {
                using (var rsa = RSA.Create())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(key.ModulusB64), Exponent = Convert.FromBase64String(key.ExponentB64) });
                    if (sig.Length != (rsa.KeySize + 7) / 8) return false;
                    return rsa.VerifyData(canonical, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (CryptographicException) { return false; }
            catch (FormatException) { return false; }
            catch (ArgumentException) { return false; }
        }

        /// <summary>
        /// Rollback guard: a verified file of <paramref name="fetched"/> replaces the values in use only when it is NEWER
        /// (an equal version keeps the file in use: two signed files must never share a version — tools/sign-definitions.ps1
        /// keeps a ledger of the versions it signed and refuses other content under one of them), and never when it is below
        /// <paramref name="minVersion"/> (the version the mod was released with, AegisRules.MinDefinitionsVersion), even over
        /// the built-in values: an old signed file served again cannot bring back limits a later file fixed.
        /// </summary>
        internal static bool VersionAccepted(int fetched, int inUse, bool inUseIsBuiltin, int minVersion) =>
            fetched > 0 && fetched >= minVersion && (inUseIsBuiltin || fetched > inUse);

        /// <summary>A short English reason for a log line ("no signature" / "the signature does not match" / "signed with a revoked key").</summary>
        internal static string Describe(Status s) => s == Status.Ok ? "signature OK" : s == Status.Missing ? "no signature"
            : s == Status.Revoked ? "signed with a revoked key" : "the signature does not match";
    }
}
