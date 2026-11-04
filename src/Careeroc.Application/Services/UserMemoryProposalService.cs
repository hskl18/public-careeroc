using Careeroc.Application.Abstractions;
using Careeroc.Contracts.Profile;
using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Careeroc.Application.Services;

public sealed class UserMemoryProposalService
{
    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public UserMemoryProposalService(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<IReadOnlyList<UserMemoryEntryDto>> GetPendingProposalsAsync(
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var user = await GetExistingUserAsync(userEmail, cancellationToken);
        if (user is null)
        {
            return [];
        }

        var nowUtc = _clock.UtcNow;
        return await _dbContext.UserMemoryEntries
            .AsNoTracking()
            .Where(entry =>
                entry.UserId == user.Id &&
                entry.ConfirmationStatus == "pending" &&
                (!entry.ExpiresAtUtc.HasValue || entry.ExpiresAtUtc > nowUtc))
            .OrderByDescending(entry => entry.UpdatedAtUtc)
            .ThenBy(entry => entry.Kind)
            .Take(40)
            .Select(entry => MapMemory(entry))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserMemoryEntryDto>> GetProposalsForApplicationAsync(
        Guid jobApplicationId,
        string? userEmail,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var user = await GetExistingUserAsync(userEmail, cancellationToken);
        if (user is null)
        {
            return [];
        }

        var inboxEmailIds = await _dbContext.InboxEmails
            .AsNoTracking()
            .Where(email =>
                email.JobApplicationId == jobApplicationId &&
                email.InboxAccount.UserId == user.Id)
            .Select(email => email.Id)
            .Take(250)
            .ToListAsync(cancellationToken);

        var sourceIds = inboxEmailIds
            .Append(jobApplicationId)
            .ToHashSet();
        var nowUtc = _clock.UtcNow;

        return await _dbContext.UserMemoryEntries
            .AsNoTracking()
            .Where(entry =>
                entry.UserId == user.Id &&
                entry.ConfirmationStatus == "pending" &&
                entry.SourceId.HasValue &&
                sourceIds.Contains(entry.SourceId.Value) &&
                (!entry.ExpiresAtUtc.HasValue || entry.ExpiresAtUtc > nowUtc))
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenByDescending(entry => entry.UpdatedAtUtc)
            .Take(Math.Clamp(limit, 1, 20))
            .Select(entry => MapMemory(entry))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserMemoryEntryDto>> GetProposalsForResumeAsync(
        Guid resumeDocumentId,
        string? userEmail,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var user = await GetExistingUserAsync(userEmail, cancellationToken);
        if (user is null)
        {
            return [];
        }

        var resumeVersionIds = await _dbContext.ResumeVersions
            .AsNoTracking()
            .Where(version =>
                version.ResumeDocumentId == resumeDocumentId &&
                version.ResumeDocument.UserId == user.Id)
            .Select(version => version.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        var sourceIds = resumeVersionIds
            .Append(resumeDocumentId)
            .ToHashSet();
        var nowUtc = _clock.UtcNow;

        return await _dbContext.UserMemoryEntries
            .AsNoTracking()
            .Where(entry =>
                entry.UserId == user.Id &&
                entry.ConfirmationStatus == "pending" &&
                entry.SourceId.HasValue &&
                sourceIds.Contains(entry.SourceId.Value) &&
                (!entry.ExpiresAtUtc.HasValue || entry.ExpiresAtUtc > nowUtc))
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenByDescending(entry => entry.UpdatedAtUtc)
            .Take(Math.Clamp(limit, 1, 20))
            .Select(entry => MapMemory(entry))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserMemoryEntryDto>> GetConfirmedMemoryAsync(
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var user = await GetExistingUserAsync(userEmail, cancellationToken);
        if (user is null)
        {
            return [];
        }

        var nowUtc = _clock.UtcNow;
        return await _dbContext.UserMemoryEntries
            .AsNoTracking()
            .Where(entry =>
                entry.UserId == user.Id &&
                entry.ConfirmationStatus == "confirmed" &&
                (!entry.ExpiresAtUtc.HasValue || entry.ExpiresAtUtc > nowUtc))
            .OrderBy(entry => entry.Kind)
            .ThenBy(entry => entry.Key)
            .ThenByDescending(entry => entry.UpdatedAtUtc)
            .Take(200)
            .Select(entry => MapMemory(entry))
            .ToListAsync(cancellationToken);
    }

    public async Task<UserMemoryEntryDto> ProposeMemoryAsync(
        ProposeUserMemoryRequest request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var user = await GetOrCreateUserAsync(userEmail, cancellationToken);
        var nowUtc = _clock.UtcNow;
        var normalized = NormalizeProposal(request, nowUtc);
        var proposal = await _dbContext.UserMemoryEntries
            .Where(entry =>
                entry.UserId == user.Id &&
                entry.Kind == normalized.Kind &&
                entry.Key == normalized.Key &&
                entry.ConfirmationStatus == "pending" &&
                entry.SourceType == normalized.SourceType &&
                entry.SourceId == normalized.SourceId)
            .OrderByDescending(entry => entry.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (proposal is null)
        {
            proposal = new UserMemoryEntry
            {
                UserId = user.Id,
                Kind = normalized.Kind,
                Key = normalized.Key,
                CreatedAtUtc = nowUtc,
                ConfirmationStatus = "pending"
            };
            _dbContext.UserMemoryEntries.Add(proposal);
        }

        proposal.Value = normalized.Value;
        proposal.Summary = normalized.Summary;
        proposal.SourceType = normalized.SourceType;
        proposal.SourceId = normalized.SourceId;
        proposal.Confidence = normalized.Confidence;
        proposal.ExpiresAtUtc = normalized.ExpiresAtUtc;
        proposal.UpdatedAtUtc = nowUtc;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapMemory(proposal);
    }

    public async Task<UserMemoryEntryDto?> UpdateConfirmedMemoryAsync(
        Guid memoryEntryId,
        UpdateUserMemoryRequest request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var user = await GetOrCreateUserAsync(userEmail, cancellationToken);
        var nowUtc = _clock.UtcNow;
        var normalized = NormalizeMemoryUpdate(request, nowUtc);
        var memory = await _dbContext.UserMemoryEntries
            .FirstOrDefaultAsync(
                entry =>
                    entry.Id == memoryEntryId &&
                    entry.UserId == user.Id &&
                    entry.ConfirmationStatus == "confirmed" &&
                    (!entry.ExpiresAtUtc.HasValue || entry.ExpiresAtUtc > nowUtc),
                cancellationToken);

        if (memory is null)
        {
            return null;
        }

        memory.Value = normalized.Value;
        memory.Summary = normalized.Summary;
        memory.Confidence = normalized.Confidence;
        memory.ExpiresAtUtc = normalized.ExpiresAtUtc;
        memory.SourceType = "user";
        memory.UpdatedAtUtc = nowUtc;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapMemory(memory);
    }

    public async Task<bool> DeleteMemoryAsync(
        Guid memoryEntryId,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var user = await GetOrCreateUserAsync(userEmail, cancellationToken);
        var memory = await _dbContext.UserMemoryEntries
            .FirstOrDefaultAsync(
                entry =>
                    entry.Id == memoryEntryId &&
                    entry.UserId == user.Id,
                cancellationToken);

        if (memory is null)
        {
            return false;
        }

        _dbContext.UserMemoryEntries.Remove(memory);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<UserMemoryEntryDto?> ResolveProposalAsync(
        Guid memoryEntryId,
        ResolveUserMemoryProposalRequest request,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var user = await GetOrCreateUserAsync(userEmail, cancellationToken);
        var action = NormalizeResolutionAction(request.Action);
        var nowUtc = _clock.UtcNow;
        var proposal = await _dbContext.UserMemoryEntries
            .FirstOrDefaultAsync(
                entry =>
                    entry.Id == memoryEntryId &&
                    entry.UserId == user.Id &&
                    entry.ConfirmationStatus == "pending",
                cancellationToken);

        if (proposal is null)
        {
            return null;
        }

        if (proposal.ExpiresAtUtc.HasValue && proposal.ExpiresAtUtc <= nowUtc)
        {
            throw new InvalidOperationException("Expired memory proposals cannot be resolved.");
        }

        if (action == "confirm")
        {
            var existingConfirmed = await _dbContext.UserMemoryEntries
                .FirstOrDefaultAsync(
                    entry =>
                        entry.UserId == user.Id &&
                        entry.Id != proposal.Id &&
                        entry.Kind == proposal.Kind &&
                        entry.Key == proposal.Key &&
                        entry.ConfirmationStatus == "confirmed" &&
                        (!entry.ExpiresAtUtc.HasValue || entry.ExpiresAtUtc > nowUtc),
                    cancellationToken);

            if (existingConfirmed is not null)
            {
                existingConfirmed.Value = proposal.Value;
                existingConfirmed.Summary = proposal.Summary;
                existingConfirmed.SourceType = proposal.SourceType;
                existingConfirmed.SourceId = proposal.SourceId;
                existingConfirmed.Confidence = proposal.Confidence;
                existingConfirmed.ExpiresAtUtc = proposal.ExpiresAtUtc;
                existingConfirmed.UpdatedAtUtc = nowUtc;
                proposal.ConfirmationStatus = "applied";
                proposal.UpdatedAtUtc = nowUtc;

                await _dbContext.SaveChangesAsync(cancellationToken);
                return MapMemory(existingConfirmed);
            }
        }

        proposal.ConfirmationStatus = action == "confirm" ? "confirmed" : "rejected";
        proposal.UpdatedAtUtc = nowUtc;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapMemory(proposal);
    }

    private async Task<User> GetOrCreateUserAsync(string? userEmail, CancellationToken cancellationToken)
    {
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.Email == resolvedUserEmail, cancellationToken);

        if (user is not null)
        {
            return user;
        }

        user = new User
        {
            Email = resolvedUserEmail,
            Name = ResolveUserName(resolvedUserEmail),
            TimeZoneId = ApplicationDefaults.DefaultTimeZoneId
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    private async Task<User?> GetExistingUserAsync(string? userEmail, CancellationToken cancellationToken)
    {
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        return await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Email == resolvedUserEmail, cancellationToken);
    }

    private static NormalizedMemoryProposal NormalizeProposal(ProposeUserMemoryRequest request, DateTimeOffset nowUtc)
    {
        var kind = NormalizeToken(request.Kind, 80, "Memory kind");
        var key = NormalizeToken(request.Key, 160, "Memory key");
        var value = NormalizeMemoryValue(request.Value);
        var summary = NormalizeOptionalText(request.Summary, 1000, "Memory summary");
        var sourceType = NormalizeToken(request.SourceType, 80, "Memory source type");
        var confidence = Math.Clamp(request.Confidence ?? 0.5, 0.0, 1.0);
        var expiresAtUtc = request.ExpiresAtUtc;

        if (expiresAtUtc.HasValue && expiresAtUtc <= nowUtc)
        {
            throw new InvalidOperationException("Memory proposal expiration must be in the future.");
        }

        return new NormalizedMemoryProposal(
            kind,
            key,
            value,
            summary,
            sourceType,
            request.SourceId,
            confidence,
            expiresAtUtc);
    }

    private static NormalizedMemoryUpdate NormalizeMemoryUpdate(UpdateUserMemoryRequest request, DateTimeOffset nowUtc)
    {
        var value = NormalizeMemoryValue(request.Value);
        var summary = NormalizeOptionalText(request.Summary, 1000, "Memory summary");
        var confidence = Math.Clamp(request.Confidence ?? 1.0, 0.0, 1.0);
        var expiresAtUtc = request.ExpiresAtUtc;

        if (expiresAtUtc.HasValue && expiresAtUtc <= nowUtc)
        {
            throw new InvalidOperationException("Memory expiration must be in the future.");
        }

        return new NormalizedMemoryUpdate(value, summary, confidence, expiresAtUtc);
    }

    private static string NormalizeToken(string? value, int maxLength, string label)
    {
        var normalized = NormalizeRequiredText(value, maxLength, label);
        if (!normalized.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.' or ':'))
        {
            throw new InvalidOperationException($"{label} may only contain letters, numbers, '.', ':', '_' or '-'.");
        }

        return normalized;
    }

    private static string NormalizeResolutionAction(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "confirm" or "confirmed" or "approve" or "approved" => "confirm",
            "reject" or "rejected" or "dismiss" or "dismissed" => "reject",
            _ => throw new InvalidOperationException("Memory proposal action must be confirm or reject.")
        };
    }

    private static string NormalizeMemoryValue(string? value)
    {
        var normalized = NormalizeRequiredText(value, 2000, "Memory value");
        if (!LooksLikeJson(normalized))
        {
            return normalized;
        }

        try
        {
            using var document = JsonDocument.Parse(normalized);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Memory value looks like JSON but is not valid JSON.",
                exception);
        }
    }

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static string NormalizeRequiredText(string? value, int maxLength, string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{label} is required.");
        }

        if (normalized.Length > maxLength)
        {
            throw new InvalidOperationException($"{label} must be {maxLength} characters or fewer.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength, string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length > maxLength)
        {
            throw new InvalidOperationException($"{label} must be {maxLength} characters or fewer.");
        }

        return normalized;
    }

    private static UserMemoryEntryDto MapMemory(UserMemoryEntry entry)
    {
        return new UserMemoryEntryDto(
            entry.Id,
            entry.Kind,
            entry.Key,
            entry.Value,
            entry.Summary,
            entry.SourceType,
            entry.SourceId,
            entry.Confidence,
            entry.ConfirmationStatus,
            entry.UpdatedAtUtc,
            entry.ExpiresAtUtc);
    }

    private static string ResolveUserName(string userEmail)
    {
        var separatorIndex = userEmail.IndexOf('@');
        return separatorIndex > 0 ? userEmail[..separatorIndex] : ApplicationDefaults.DefaultUserName;
    }

    private sealed record NormalizedMemoryProposal(
        string Kind,
        string Key,
        string Value,
        string? Summary,
        string SourceType,
        Guid? SourceId,
        double Confidence,
        DateTimeOffset? ExpiresAtUtc);

    private sealed record NormalizedMemoryUpdate(
        string Value,
        string? Summary,
        double Confidence,
        DateTimeOffset? ExpiresAtUtc);
}
