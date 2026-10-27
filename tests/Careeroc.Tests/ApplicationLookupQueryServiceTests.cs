using Careeroc.Application;
using Careeroc.Application.Services;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class ApplicationLookupQueryServiceTests
{
    private static readonly TestClock Clock = new(new DateTimeOffset(2026, 5, 14, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task SearchAsync_ReturnsRecentUserScopedMatches()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };
        var otherUser = new User
        {
            Email = "other@example.com",
            Name = "Other"
        };

        dbContext.Users.AddRange(user, otherUser);
        dbContext.JobApplications.AddRange(
            new JobApplication
            {
                UserId = user.Id,
                User = user,
                Company = "OpenAI",
                Role = "Platform Engineer",
                Status = ApplicationStatus.Interviewing,
                Source = "Email",
                Location = "San Francisco",
                LastActivityAtUtc = new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero)
            },
            new JobApplication
            {
                UserId = user.Id,
                User = user,
                Company = "Anthropic",
                Role = "Machine Learning Engineer",
                RoleCanonical = "ML Engineer",
                Status = ApplicationStatus.Applied,
                Source = "Manual",
                LastActivityAtUtc = new DateTimeOffset(2026, 4, 21, 9, 0, 0, TimeSpan.Zero)
            },
            new JobApplication
            {
                UserId = otherUser.Id,
                User = otherUser,
                Company = "OpenAI",
                Role = "Backend Engineer",
                Status = ApplicationStatus.Offer,
                Source = "Email",
                LastActivityAtUtc = new DateTimeOffset(2026, 4, 22, 9, 0, 0, TimeSpan.Zero)
            });
        await dbContext.SaveChangesAsync();

        var service = new ApplicationLookupQueryService(dbContext, Clock);
        var response = await service.SearchAsync(ApplicationDefaults.DefaultUserEmail, "engineer");

        Assert.Equal(2, response.Items.Count);
        Assert.Equal("Anthropic", response.Items[0].Company);
        Assert.Equal("OpenAI", response.Items[1].Company);
        Assert.All(response.Items, item => Assert.NotEqual(ApplicationStatus.Offer.ToString(), item.Status));
    }

    [Fact]
    public async Task SearchAsync_ClampsLimit()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        await using var dbContext = TestDbContextFactory.Create(databaseName);
        var user = new User
        {
            Email = ApplicationDefaults.DefaultUserEmail,
            Name = ApplicationDefaults.DefaultUserName
        };

        dbContext.Users.Add(user);
        for (var index = 0; index < 30; index++)
        {
            dbContext.JobApplications.Add(
                new JobApplication
                {
                    UserId = user.Id,
                    User = user,
                    Company = $"Company {index:00}",
                    Role = "Software Engineer",
                    Status = ApplicationStatus.Applied,
                    Source = "Email",
                    LastActivityAtUtc = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero).AddDays(index)
                });
        }

        await dbContext.SaveChangesAsync();

        var service = new ApplicationLookupQueryService(dbContext, Clock);
        var response = await service.SearchAsync(ApplicationDefaults.DefaultUserEmail, "software", 100);

        Assert.Equal(25, response.Items.Count);
        Assert.Equal("Company 29", response.Items[0].Company);
    }
}
