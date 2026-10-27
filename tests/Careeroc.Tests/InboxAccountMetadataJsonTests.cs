using System.Text.Json;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Careeroc.Tests;

public sealed class InboxAccountMetadataJsonTests
{
    [Fact]
    public void Read_UsesTokenColumnsAheadOfLegacyJsonTokenFields()
    {
        var account = new InboxAccount
        {
            AccessTokenEncrypted = "column-access-token",
            RefreshTokenEncrypted = "column-refresh-token",
            OAuthMetadataJson =
                """
                {
                  "connectionStatus": "Connected",
                  "accessToken": "legacy-json-access-token",
                  "refreshToken": "legacy-json-refresh-token"
                }
                """
        };

        var metadata = InboxAccountMetadataJson.Read(account, _ => new InboxAccountMetadata());

        Assert.Equal("Connected", metadata.ConnectionStatus);
        Assert.Equal("column-access-token", metadata.AccessToken);
        Assert.Equal("column-refresh-token", metadata.RefreshToken);
    }

    [Fact]
    public void Write_StoresTokensInColumnsAndRemovesThemFromJson()
    {
        var account = new InboxAccount();
        var metadata = new InboxAccountMetadata
        {
            ConnectionStatus = "Connected",
            AccessToken = "protected-access-token",
            RefreshToken = "protected-refresh-token",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };

        InboxAccountMetadataJson.Write(account, metadata);

        Assert.Equal("protected-access-token", account.AccessTokenEncrypted);
        Assert.Equal("protected-refresh-token", account.RefreshTokenEncrypted);
        using var document = JsonDocument.Parse(account.OAuthMetadataJson!);
        Assert.False(document.RootElement.TryGetProperty("accessToken", out _));
        Assert.False(document.RootElement.TryGetProperty("refreshToken", out _));

        var readBack = InboxAccountMetadataJson.Read(account, _ => new InboxAccountMetadata());
        Assert.Equal("protected-access-token", readBack.AccessToken);
        Assert.Equal("protected-refresh-token", readBack.RefreshToken);
        Assert.Equal("Connected", readBack.ConnectionStatus);
    }

    [Fact]
    public void Read_ReturnsInvalidMetadataStatus_WhenJsonCannotBeParsed()
    {
        var account = new InboxAccount
        {
            OAuthMetadataJson = "{not-json"
        };

        var logger = new CapturingLogger();

        var metadata = InboxAccountMetadataJson.Read(
            account,
            _ => new InboxAccountMetadata
            {
                SetupMode = "manual",
                ConnectionStatus = "Configured",
                AccessToken = "access-token",
                RefreshToken = "refresh-token",
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
            },
            logger);

        Assert.False(metadata.IsValid);
        Assert.Equal("json_parse", metadata.InvalidReason);
        Assert.Equal("error", metadata.SetupMode);
        Assert.Equal("Metadata invalid", metadata.ConnectionStatus);
        Assert.Contains("Reconnect", metadata.NextStep, StringComparison.OrdinalIgnoreCase);
        Assert.Null(metadata.AccessToken);
        Assert.Null(metadata.RefreshToken);
        Assert.Null(metadata.AccessTokenExpiresAtUtc);
        Assert.Contains(logger.Messages, message => message.Contains(account.Id.ToString(), StringComparison.Ordinal));
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
