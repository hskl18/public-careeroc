using System.Security.Cryptography;
using System.Text;
using Careeroc.Application.Abstractions;
using Careeroc.Contracts.Agents;
using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed record PersonalAccessTokenIdentity(string Email, string Name);

public sealed class PersonalAccessTokenService
{
    public const string TokenPrefix = "coc_";
    public const int MaxActiveTokensPerUser = 10;
    private const int DefaultLifetimeDays = 30;
    private const int MaxLifetimeDays = 365;
    private static readonly TimeSpan LastUsedWriteInterval = TimeSpan.FromMinutes(5);

    private readonly ICareerocDbContext _dbContext;
    private readonly IClock _clock;

    public PersonalAccessTokenService(ICareerocDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<CreatedPersonalAccessTokenDto> CreateAsync(
        string? userEmail,
        CreatePersonalAccessTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 80)
        {
            throw new ArgumentException("Token name must be between 1 and 80 characters.", nameof(request));
        }

        var lifetimeDays = request.ExpiresInDays ?? DefaultLifetimeDays;
        if (lifetimeDays is < 1 or > MaxLifetimeDays)
        {
            throw new ArgumentException($"Token lifetime must be between 1 and {MaxLifetimeDays} days.", nameof(request));
        }

        var user = await FindUserAsync(userEmail, cancellationToken)
            ?? throw new InvalidOperationException("Open the workspace once before creating an access token.");
        var now = _clock.UtcNow;
        var activeTokens = await _dbContext.PersonalAccessTokens.CountAsync(
            token => token.UserId == user.Id && token.RevokedAtUtc == null && token.ExpiresAtUtc > now,
            cancellationToken);
        if (activeTokens >= MaxActiveTokensPerUser)
        {
            throw new InvalidOperationException($"Revoke a token first; at most {MaxActiveTokensPerUser} can be active.");
        }

        var secret = TokenPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var token = new PersonalAccessToken
        {
            UserId = user.Id,
            Name = name,
            TokenPrefix = secret[..12],
            TokenHash = Hash(secret),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(lifetimeDays)
        };
        _dbContext.PersonalAccessTokens.Add(token);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new CreatedPersonalAccessTokenDto(secret, ToDto(token));
    }

    public async Task<IReadOnlyList<PersonalAccessTokenDto>> ListAsync(
        string? userEmail,
        CancellationToken cancellationToken = default)
    {
        var user = await FindUserAsync(userEmail, cancellationToken);
        if (user is null)
        {
            return [];
        }

        var tokens = await _dbContext.PersonalAccessTokens
            .AsNoTracking()
            .Where(token => token.UserId == user.Id)
            .OrderByDescending(token => token.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return tokens.Select(ToDto).ToList();
    }

    public async Task<bool> RevokeAsync(string? userEmail, Guid tokenId, CancellationToken cancellationToken = default)
    {
        var user = await FindUserAsync(userEmail, cancellationToken);
        var token = user is null
            ? null
            : await _dbContext.PersonalAccessTokens.FirstOrDefaultAsync(
                candidate => candidate.Id == tokenId && candidate.UserId == user.Id,
                cancellationToken);
        if (token is null)
        {
            return false;
        }

        token.RevokedAtUtc ??= _clock.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PersonalAccessTokenIdentity?> AuthenticateAsync(
        string? presentedToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedToken) ||
            !presentedToken.StartsWith(TokenPrefix, StringComparison.Ordinal) ||
            presentedToken.Length > 128)
        {
            return null;
        }

        var hash = Hash(presentedToken);
        var now = _clock.UtcNow;
        var token = await _dbContext.PersonalAccessTokens
            .Include(candidate => candidate.User)
            .FirstOrDefaultAsync(
                candidate => candidate.TokenHash == hash && candidate.RevokedAtUtc == null && candidate.ExpiresAtUtc > now,
                cancellationToken);
        if (token is null)
        {
            return null;
        }

        if (token.LastUsedAtUtc is null || now - token.LastUsedAtUtc.Value >= LastUsedWriteInterval)
        {
            token.LastUsedAtUtc = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new PersonalAccessTokenIdentity(token.User.Email, token.User.Name);
    }

    private async Task<User?> FindUserAsync(string? userEmail, CancellationToken cancellationToken)
    {
        var email = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        return await _dbContext.Users.FirstOrDefaultAsync(user => user.Email == email, cancellationToken);
    }

    // Tokens carry 256 random bits, so a fast hash is enough; a slow KDF would only add latency.
    private static string Hash(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static PersonalAccessTokenDto ToDto(PersonalAccessToken token)
    {
        return new PersonalAccessTokenDto(
            token.Id,
            token.Name,
            token.TokenPrefix,
            token.Scope,
            token.CreatedAtUtc,
            token.ExpiresAtUtc,
            token.LastUsedAtUtc,
            token.RevokedAtUtc);
    }
}
