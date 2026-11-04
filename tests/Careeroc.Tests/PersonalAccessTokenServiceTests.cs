using Careeroc.Application.Services;
using Careeroc.Contracts.Agents;
using Careeroc.Domain.Entities;

namespace Careeroc.Tests;

public sealed class PersonalAccessTokenServiceTests
{
    [Fact]
    public async Task CreateAsync_ReturnsTheSecretOnceAndStoresOnlyItsHash()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        await using var dbContext = await CreateContextWithUserAsync();
        var service = new PersonalAccessTokenService(dbContext, clock);

        var created = await service.CreateAsync("alex@example.com", new CreatePersonalAccessTokenRequest("Claude Desktop"));

        Assert.StartsWith("coc_", created.Token, StringComparison.Ordinal);
        var stored = Assert.Single(dbContext.PersonalAccessTokens);
        Assert.NotEqual(created.Token, stored.TokenHash);
        Assert.DoesNotContain(created.Token, stored.TokenHash, StringComparison.Ordinal);
        Assert.Equal(created.Token[..12], stored.TokenPrefix);
        Assert.Equal(clock.UtcNow.AddDays(30), stored.ExpiresAtUtc);
    }

    [Fact]
    public async Task AuthenticateAsync_AcceptsOnlyLiveTokens()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        await using var dbContext = await CreateContextWithUserAsync();
        var service = new PersonalAccessTokenService(dbContext, clock);
        var live = await service.CreateAsync("alex@example.com", new CreatePersonalAccessTokenRequest("Live"));
        var revoked = await service.CreateAsync("alex@example.com", new CreatePersonalAccessTokenRequest("Revoked"));
        var shortLived = await service.CreateAsync("alex@example.com", new CreatePersonalAccessTokenRequest("Short", 1));
        await service.RevokeAsync("alex@example.com", revoked.Details.Id);
        var twoDaysLater = new PersonalAccessTokenService(dbContext, new TestClock(clock.UtcNow.AddDays(2)));

        Assert.Equal("alex@example.com", (await twoDaysLater.AuthenticateAsync(live.Token))?.Email);
        Assert.Null(await twoDaysLater.AuthenticateAsync(revoked.Token));
        Assert.Null(await twoDaysLater.AuthenticateAsync(shortLived.Token));
        Assert.Null(await twoDaysLater.AuthenticateAsync(live.Token[..^1] + "x"));
        Assert.Null(await twoDaysLater.AuthenticateAsync("not-a-careeroc-token"));
    }

    [Fact]
    public async Task RevokeAsync_CannotTouchAnotherUsersToken()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        await using var dbContext = await CreateContextWithUserAsync();
        dbContext.Users.Add(new User { Email = "other@example.com", Name = "Other" });
        await dbContext.SaveChangesAsync();
        var service = new PersonalAccessTokenService(dbContext, clock);
        var created = await service.CreateAsync("alex@example.com", new CreatePersonalAccessTokenRequest("Mine"));

        Assert.False(await service.RevokeAsync("other@example.com", created.Details.Id));
        Assert.NotNull(await service.AuthenticateAsync(created.Token));
    }

    [Theory]
    [InlineData("", 30)]
    [InlineData("Token", 0)]
    [InlineData("Token", 366)]
    public async Task CreateAsync_RejectsInvalidRequests(string name, int lifetimeDays)
    {
        var clock = new TestClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        await using var dbContext = await CreateContextWithUserAsync();
        var service = new PersonalAccessTokenService(dbContext, clock);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync("alex@example.com", new CreatePersonalAccessTokenRequest(name, lifetimeDays)));
    }

    private static async Task<Careeroc.Infrastructure.Persistence.CareerocDbContext> CreateContextWithUserAsync()
    {
        var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        dbContext.Users.Add(new User { Email = "alex@example.com", Name = "Alex Rivera" });
        await dbContext.SaveChangesAsync();
        return dbContext;
    }
}
