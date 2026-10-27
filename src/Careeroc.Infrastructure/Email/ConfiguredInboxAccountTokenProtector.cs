using System.Security.Cryptography;
using System.Text;
using Careeroc.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Careeroc.Infrastructure.Email;

public sealed class ConfiguredInboxAccountTokenProtector : IInboxAccountTokenProtector
{
    private const string ProtectedPrefix = "enc:v1:";
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly byte[]? _key;

    public ConfiguredInboxAccountTokenProtector(IConfiguration configuration)
    {
        var secret =
            FirstNonEmpty(
                configuration["Security:TokenProtectionKey"],
                configuration["CAREEROC_TOKEN_PROTECTION_KEY"]);
        _key = string.IsNullOrWhiteSpace(secret)
            ? null
            : SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public string? Protect(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || IsProtected(token))
        {
            return token;
        }

        if (_key is null)
        {
            return token;
        }

        var plaintext = Encoding.UTF8.GetBytes(token);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(_key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var payload = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, payload, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSizeBytes + TagSizeBytes, ciphertext.Length);

        return $"{ProtectedPrefix}{Convert.ToBase64String(payload)}";
    }

    public string? Unprotect(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !IsProtected(token))
        {
            return token;
        }

        if (_key is null)
        {
            throw new InvalidOperationException("Inbox token protection key is not configured.");
        }

        var payload = Convert.FromBase64String(token[ProtectedPrefix.Length..]);
        if (payload.Length <= NonceSizeBytes + TagSizeBytes)
        {
            throw new InvalidOperationException("Protected inbox token payload is invalid.");
        }

        var nonce = payload[..NonceSizeBytes];
        var tag = payload[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
        var ciphertext = payload[(NonceSizeBytes + TagSizeBytes)..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, TagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    public static bool IsProtected(string token)
    {
        return token.StartsWith(ProtectedPrefix, StringComparison.Ordinal);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
