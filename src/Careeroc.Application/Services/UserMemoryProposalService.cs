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
        var user = await UserAccounts.GetOrCreateAsync(_dbContext, userEmail, null, cancellationToken);
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

    public async Task<bool> DeleteMemoryAsync(
        Guid memoryEntryId,
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var user = await UserAccounts.GetOrCreateAsync(_dbContext, userEmail, null, cancellationToken);
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
        var user = await UserAccounts.GetOrCreateAsync(_dbContext, userEmail, null, cancellationToken);
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
            throw new UserFacingException("Expired memory proposals cannot be resolved.");
        }

        // A newer review overwrites the same pending entry, so a stale page could approve unseen content.
        if (request.ExpectedUpdatedAtUtc is { } expected &&
            (proposal.UpdatedAtUtc - expected).Duration() > TimeSpan.FromMilliseconds(1))
        {
            throw new UserFacingException("This proposal changed after you opened it. Reload and decide again.");
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

                await SaveResolutionAsync(cancellationToken);
                return MapMemory(existingConfirmed);
            }
        }

        proposal.ConfirmationStatus = action == "confirm" ? "confirmed" : "rejected";
        proposal.UpdatedAtUtc = nowUtc;

        await SaveResolutionAsync(cancellationToken);
        return MapMemory(proposal);
    }

    // Two requests that both read the pending proposal (a double click) cannot both resolve it:
    // UserMemoryEntry has a row version, so the second save conflicts and creates nothing.
    private async Task SaveResolutionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new UserFacingException("This proposal was decided in another request. Reload to see the result.", exception);
        }
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
            throw new UserFacingException("Memory proposal expiration must be in the future.");
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

    private static string NormalizeToken(string? value, int maxLength, string label)
    {
        var normalized = NormalizeRequiredText(value, maxLength, label);
        if (!normalized.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.' or ':'))
        {
            throw new UserFacingException($"{label} may only contain letters, numbers, '.', ':', '_' or '-'.");
        }

        return normalized;
    }

    private static string NormalizeResolutionAction(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "confirm" or "confirmed" or "approve" or "approved" => "confirm",
            "reject" or "rejected" or "dismiss" or "dismissed" => "reject",
            _ => throw new UserFacingException("Memory proposal action must be confirm or reject.")
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
            throw new UserFacingException(
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
            throw new UserFacingException($"{label} is required.");
        }

        if (normalized.Length > maxLength)
        {
            throw new UserFacingException($"{label} must be {maxLength} characters or fewer.");
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
            throw new UserFacingException($"{label} must be {maxLength} characters or fewer.");
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


    private sealed record NormalizedMemoryProposal(
        string Kind,
        string Key,
        string Value,
        string? Summary,
        string SourceType,
        Guid? SourceId,
        double Confidence,
        DateTimeOffset? ExpiresAtUtc);

}
