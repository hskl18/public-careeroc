using Careeroc.Application;
using Careeroc.Application.Services;
using Careeroc.Domain.Entities;

namespace Careeroc.Tests;

public sealed class UserAgentContextSnapshotServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_RendersConfirmedMemoryAndSoulMarkdown()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName,
            Location = "Portland",
            TargetRole = "Software engineering intern"
        };
        dbContext.Users.Add(user);
        var nowUtc = new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero);
        dbContext.UserMemoryEntries.AddRange(
            new UserMemoryEntry
            {
                User = user,
                UserId = user.Id,
                Kind = "resume",
                Key = "resume.latest_evaluation",
                Value = "{\"score\":91,\"skills\":[\"TypeScript\",\"C#\"]}",
                Summary = "Latest resume is strong.",
                SourceType = "resume_evaluation",
                Confidence = 0.9,
                ConfirmationStatus = "confirmed",
                UpdatedAtUtc = new DateTimeOffset(2026, 4, 25, 12, 0, 0, TimeSpan.Zero)
            },
            new UserMemoryEntry
            {
                User = user,
                UserId = user.Id,
                Kind = "preference",
                Key = "target_stack",
                Value = "full-stack TypeScript",
                SourceType = "user",
                ConfirmationStatus = "pending",
                UpdatedAtUtc = new DateTimeOffset(2026, 4, 25, 13, 0, 0, TimeSpan.Zero)
            },
            new UserMemoryEntry
            {
                User = user,
                UserId = user.Id,
                Kind = "expired",
                Key = "old_fact",
                Value = "ignore me",
                SourceType = "user",
                ConfirmationStatus = "confirmed",
                ExpiresAtUtc = nowUtc.AddDays(-1)
            });
        dbContext.UserSoulProfiles.Add(
            new UserSoulProfile
            {
                User = user,
                UserId = user.Id,
                NarrativeTone = "direct",
                CareerNarrative = "Builds agentic career tools.",
                CommunicationPreferencesJson = "{\"detail\":\"concise\"}"
            });
        await dbContext.SaveChangesAsync();

        var service = new UserAgentContextSnapshotService(dbContext, new TestClock(nowUtc));
        var snapshot = await service.GetSnapshotAsync(ApplicationDefaults.DefaultUserEmail);

        Assert.Equal(1, snapshot.MemoryEntryCount);
        Assert.Contains("# memory.md", snapshot.MemoryMarkdown, StringComparison.Ordinal);
        Assert.Contains("resume.latest_evaluation", snapshot.MemoryMarkdown, StringComparison.Ordinal);
        Assert.Contains("Latest resume is strong.", snapshot.MemoryMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("target_stack", snapshot.MemoryMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("old_fact", snapshot.MemoryMarkdown, StringComparison.Ordinal);
        Assert.Contains("# soul.md", snapshot.SoulMarkdown, StringComparison.Ordinal);
        Assert.Contains("Narrative tone: direct", snapshot.SoulMarkdown, StringComparison.Ordinal);
        Assert.Contains("Builds agentic career tools.", snapshot.SoulMarkdown, StringComparison.Ordinal);
        Assert.Contains("detail", snapshot.SoulMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSnapshotAsync_ReturnsDefaultSnapshotsForMissingUser()
    {
        var databaseName = Guid.NewGuid().ToString("N");

        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var service = new UserAgentContextSnapshotService(dbContext, new TestClock(new DateTimeOffset(2026, 4, 26, 12, 0, 0, TimeSpan.Zero)));

        var snapshot = await service.GetSnapshotAsync("missing@example.com");

        Assert.Equal(0, snapshot.MemoryEntryCount);
        Assert.Contains("No confirmed long-term memory", snapshot.MemoryMarkdown, StringComparison.Ordinal);
        Assert.Contains("missing@example.com", snapshot.SoulMarkdown, StringComparison.Ordinal);
        Assert.Contains("Prefer concrete facts", snapshot.SoulMarkdown, StringComparison.Ordinal);
    }
}
