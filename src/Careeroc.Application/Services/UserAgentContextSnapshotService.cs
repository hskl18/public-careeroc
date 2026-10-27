using System.Text;
using System.Text.Json;
using Careeroc.Application.Abstractions;
using Careeroc.Contracts.Profile;
using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed class UserAgentContextSnapshotService : IUserAgentContextSnapshotProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public UserAgentContextSnapshotService(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<UserAgentContextSnapshotDto> GetSnapshotAsync(
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(candidate => candidate.SoulProfile)
            .FirstOrDefaultAsync(candidate => candidate.Email == resolvedUserEmail, cancellationToken);

        if (user is null)
        {
            var nowUtc = _clock.UtcNow;
            return new UserAgentContextSnapshotDto(
                nowUtc,
                RenderEmptyMemoryMarkdown(nowUtc),
                RenderDefaultSoulMarkdown(resolvedUserEmail, null, null, null, nowUtc),
                0,
                null,
                null);
        }

        var generatedAtUtc = _clock.UtcNow;
        var memoryEntries = await LoadConfirmedMemoryAsync(user.Id, generatedAtUtc, cancellationToken);
        var memoryMarkdown = RenderMemoryMarkdown(user, memoryEntries, generatedAtUtc);
        var soulMarkdown = RenderSoulMarkdown(user, user.SoulProfile, generatedAtUtc);

        return new UserAgentContextSnapshotDto(
            generatedAtUtc,
            memoryMarkdown,
            soulMarkdown,
            memoryEntries.Count,
            memoryEntries.Count == 0 ? null : memoryEntries.Max(entry => (DateTimeOffset?)entry.UpdatedAtUtc),
            user.SoulProfile?.UpdatedAtUtc);
    }

    private async Task<IReadOnlyList<UserMemoryEntry>> LoadConfirmedMemoryAsync(
        Guid userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        return await _dbContext.UserMemoryEntries
            .AsNoTracking()
            .Where(entry =>
                entry.UserId == userId &&
                entry.ConfirmationStatus == "confirmed" &&
                (!entry.ExpiresAtUtc.HasValue || entry.ExpiresAtUtc > nowUtc))
            .OrderBy(entry => entry.Kind)
            .ThenBy(entry => entry.Key)
            .ThenByDescending(entry => entry.UpdatedAtUtc)
            .Take(40)
            .ToListAsync(cancellationToken);
    }

    private static string RenderMemoryMarkdown(
        User user,
        IReadOnlyList<UserMemoryEntry> memoryEntries,
        DateTimeOffset generatedAtUtc)
    {
        if (memoryEntries.Count == 0)
        {
            return RenderEmptyMemoryMarkdown(generatedAtUtc);
        }

        var builder = new StringBuilder();
        builder.AppendLine("# memory.md");
        builder.AppendLine();
        builder.AppendLine($"Generated: {generatedAtUtc:O}");
        builder.AppendLine($"User: {SanitizeInline(user.Email)}");
        builder.AppendLine();
        builder.AppendLine("Confirmed career memory only. Ignore anything not listed here.");

        foreach (var group in memoryEntries.GroupBy(entry => entry.Kind, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine();
            builder.AppendLine($"## {SanitizeHeading(group.Key)}");

            foreach (var entry in group)
            {
                var value = NormalizeMemoryValue(entry);
                var summary = string.IsNullOrWhiteSpace(entry.Summary)
                    ? string.Empty
                    : $" — {SanitizeInline(entry.Summary)}";
                builder.Append("- ");
                builder.Append(SanitizeInline(entry.Key));
                builder.Append(": ");
                builder.Append(SanitizeInline(value));
                builder.Append(summary);
                builder.Append($" (source: {SanitizeInline(entry.SourceType)}, confidence: {entry.Confidence:0.##})");
                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string RenderSoulMarkdown(
        User user,
        UserSoulProfile? soulProfile,
        DateTimeOffset generatedAtUtc)
    {
        if (soulProfile is null)
        {
            return RenderDefaultSoulMarkdown(user.Email, user.Name, user.Location, user.TargetRole, generatedAtUtc);
        }

        var rendered = soulProfile.RenderedSoulMarkdown?.Trim();
        if (!string.IsNullOrWhiteSpace(rendered))
        {
            return rendered;
        }

        var builder = new StringBuilder();
        builder.AppendLine("# soul.md");
        builder.AppendLine();
        builder.AppendLine($"Generated: {generatedAtUtc:O}");
        builder.AppendLine($"User: {SanitizeInline(user.Email)}");
        builder.AppendLine();
        builder.AppendLine("## Voice");
        builder.AppendLine($"- Narrative tone: {SanitizeInline(soulProfile.NarrativeTone)}");

        if (!string.IsNullOrWhiteSpace(soulProfile.CareerNarrative))
        {
            builder.AppendLine();
            builder.AppendLine("## Career Narrative");
            builder.AppendLine(SanitizeBlock(soulProfile.CareerNarrative));
        }

        var preferences = RenderCommunicationPreferences(soulProfile.CommunicationPreferencesJson);
        if (!string.IsNullOrWhiteSpace(preferences))
        {
            builder.AppendLine();
            builder.AppendLine("## Communication Preferences");
            builder.Append(preferences);
        }

        return builder.ToString().TrimEnd();
    }

    private static string RenderDefaultSoulMarkdown(
        string email,
        string? name,
        string? location,
        string? targetRole,
        DateTimeOffset generatedAtUtc)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# soul.md");
        builder.AppendLine();
        builder.AppendLine($"Generated: {generatedAtUtc:O}");
        builder.AppendLine($"User: {SanitizeInline(email)}");
        builder.AppendLine();
        builder.AppendLine("## Voice");
        builder.AppendLine("- Narrative tone: clear, direct, evidence-backed");
        builder.AppendLine("- Prefer concrete facts over invented personalization.");
        builder.AppendLine();
        builder.AppendLine("## Profile");
        builder.AppendLine($"- Name: {SanitizeInline(string.IsNullOrWhiteSpace(name) ? "Unknown" : name)}");
        builder.AppendLine($"- Target role: {SanitizeInline(string.IsNullOrWhiteSpace(targetRole) ? "Not set" : targetRole)}");
        builder.AppendLine($"- Location: {SanitizeInline(string.IsNullOrWhiteSpace(location) ? "Not set" : location)}");
        return builder.ToString().TrimEnd();
    }

    private static string RenderEmptyMemoryMarkdown(DateTimeOffset generatedAtUtc)
    {
        return $"""
            # memory.md

            Generated: {generatedAtUtc:O}

            No confirmed long-term memory is available yet.
            """;
    }

    private static string NormalizeMemoryValue(UserMemoryEntry entry)
    {
        var value = entry.Value.Trim();
        if (value.Length == 0)
        {
            return "(empty)";
        }

        if (value.StartsWith('{') || value.StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(value);
                return JsonSerializer.Serialize(document.RootElement, JsonOptions);
            }
            catch (JsonException)
            {
                return Truncate(value, 900);
            }
        }

        return Truncate(value, 900);
    }

    private static string RenderCommunicationPreferences(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return $"- {SanitizeInline(JsonSerializer.Serialize(document.RootElement, JsonOptions))}";
            }

            var builder = new StringBuilder();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                builder.Append("- ");
                builder.Append(SanitizeInline(property.Name));
                builder.Append(": ");
                builder.AppendLine(SanitizeInline(JsonSerializer.Serialize(property.Value, JsonOptions)));
            }

            return builder.ToString().TrimEnd();
        }
        catch (JsonException)
        {
            return $"- {SanitizeInline(Truncate(json, 900))}";
        }
    }

    private static string SanitizeHeading(string value)
    {
        var normalized = SanitizeInline(value);
        return string.IsNullOrWhiteSpace(normalized) ? "memory" : normalized;
    }

    private static string SanitizeInline(string value)
    {
        return Truncate(value.ReplaceLineEndings(" ").Trim(), 900);
    }

    private static string SanitizeBlock(string value)
    {
        return Truncate(value.Trim(), 2000);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
