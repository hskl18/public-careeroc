using Careeroc.Application.Services;

namespace Careeroc.Tests;

public sealed class UserAccountsTests
{
    [Fact]
    public async Task GetOrCreateAsyncCreatesTheUserOnceAndThenReturnsIt()
    {
        var databaseName = $"user-accounts-{Guid.NewGuid():N}";
        await using var dbContext = TestDbContextFactory.Create(databaseName);

        var created = await UserAccounts.GetOrCreateAsync(dbContext, " Student@Example.com ", "Student User", CancellationToken.None);
        var again = await UserAccounts.GetOrCreateAsync(dbContext, "student@example.com", "Another Name", CancellationToken.None);

        Assert.Equal(created.Id, again.Id);
        Assert.Equal("student@example.com", again.Email);
        Assert.Equal("Student User", again.Name);
        Assert.Single(dbContext.Users);
    }

    [Theory]
    [InlineData("  Ada Lovelace  ", "ada@example.com", "Ada Lovelace")]
    [InlineData(null, "ada@example.com", "ada")]
    [InlineData("\u0000\u0007", "ada@example.com", "ada")]
    [InlineData("Ada\u0000 Lovelace", "ada@example.com", "Ada Lovelace")]
    public void DisplayNameKeepsWhatTheNameColumnCanHold(string? name, string email, string expected)
    {
        Assert.Equal(expected, UserAccounts.DisplayName(name, email));
    }

    [Fact]
    public void DisplayNameIsCutToTheColumnLength()
    {
        Assert.Equal(200, UserAccounts.DisplayName(new string('a', 500), "ada@example.com").Length);
    }
}
