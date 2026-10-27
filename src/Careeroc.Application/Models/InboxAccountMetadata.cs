using System.Text.Json;
using System.Text.Json.Nodes;
using Careeroc.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Careeroc.Application.Models;

public sealed class InboxAccountMetadata
{
    public bool IsValid { get; set; } = true;
    public string? InvalidReason { get; set; }
    public string SetupMode { get; set; } = "manual";
    public string ConnectionStatus { get; set; } = "Configured";
    public string NextStep { get; set; } = "Connection details are stored locally.";
    public string? OAuthState { get; set; }
    public string? OAuthCodeVerifier { get; set; }
    public string? OAuthReturnUrl { get; set; }
    public DateTimeOffset? OAuthStateExpiresAtUtc { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }
    public string? GmailHistoryId { get; set; }
    public int? GmailMessagesTotal { get; set; }
    public DateTimeOffset? GmailProfileObservedAtUtc { get; set; }
    public DateTimeOffset? ConnectedAtUtc { get; set; }
    public int? BackfillMonths { get; set; }
    public bool BackfillAllHistory { get; set; }
    public DateTimeOffset? InitialBackfillCompletedAtUtc { get; set; }
    public DateTimeOffset? SentReplyBackfillCompletedAtUtc { get; set; }
}

public static class InboxAccountMetadataJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static InboxAccountMetadata Read(
        InboxAccount account,
        Func<InboxAccount, InboxAccountMetadata> createFallback,
        ILogger? logger = null)
    {
        if (!string.IsNullOrWhiteSpace(account.OAuthMetadataJson))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<InboxAccountMetadata>(account.OAuthMetadataJson, JsonOptions);
                if (metadata is not null)
                {
                    metadata.IsValid = true;
                    metadata.InvalidReason = null;
                    HydrateTokenColumns(account, metadata);
                    return metadata;
                }
            }
            catch (JsonException exception)
            {
                logger?.LogWarning(
                    exception,
                    "Inbox account metadata JSON could not be parsed for inbox account {InboxAccountId}.",
                    account.Id);

                var fallback = createFallback(account);
                fallback.IsValid = false;
                fallback.InvalidReason = "json_parse";
                fallback.SetupMode = "error";
                fallback.ConnectionStatus = "Metadata invalid";
                fallback.NextStep = "Reconnect this inbox. Stored connection metadata could not be parsed.";
                fallback.AccessToken = null;
                fallback.RefreshToken = null;
                fallback.AccessTokenExpiresAtUtc = null;
                return fallback;
            }
        }

        var fallbackMetadata = createFallback(account);
        fallbackMetadata.IsValid = true;
        fallbackMetadata.InvalidReason = null;
        HydrateTokenColumns(account, fallbackMetadata);
        return fallbackMetadata;
    }

    public static void Write(InboxAccount account, InboxAccountMetadata metadata)
    {
        account.AccessTokenEncrypted = metadata.AccessToken;
        account.RefreshTokenEncrypted = metadata.RefreshToken;

        var metadataJson = JsonSerializer.SerializeToNode(metadata, JsonOptions)?.AsObject() ?? [];
        metadataJson.Remove("accessToken");
        metadataJson.Remove("refreshToken");
        account.OAuthMetadataJson = metadataJson.ToJsonString(JsonOptions);
    }

    private static void HydrateTokenColumns(InboxAccount account, InboxAccountMetadata metadata)
    {
        metadata.AccessToken = FirstNonEmpty(account.AccessTokenEncrypted, metadata.AccessToken);
        metadata.RefreshToken = FirstNonEmpty(account.RefreshTokenEncrypted, metadata.RefreshToken);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
