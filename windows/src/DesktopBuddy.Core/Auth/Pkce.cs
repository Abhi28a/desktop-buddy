using System.Security.Cryptography;
using System.Text;

namespace DesktopBuddy.Core;

// Port of randomURLSafe / Data.base64URL and the S256 challenge in OAuth.swift (RFC 7636).

public static class Pkce
{
    /// Base64 URL-safe alphabet without padding.
    public static string Base64Url(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// `bytes` random bytes from the OS generator, base64url encoded (64 bytes give an 86-character verifier).
    public static string RandomUrlSafe(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    /// code_challenge = BASE64URL(SHA256(ASCII(verifier))).
    public static string Challenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string NewVerifier() => RandomUrlSafe(64);
    public static string NewState() => RandomUrlSafe(24);
}
