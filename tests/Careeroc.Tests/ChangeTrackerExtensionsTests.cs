using Careeroc.Application.Abstractions;
using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Tests;

public sealed class ChangeTrackerExtensionsTests
{
    [Fact]
    public async Task DiscardPendingChangesUndoesUnsavedWorkButKeepsLoadedEntitiesTracked()
    {
        await using var dbContext = TestDbContextFactory.Create(Guid.NewGuid().ToString("N"));
        var user = new User { Email = "alex@example.com", Name = "Alex Rivera" };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        user.Name = "Changed before a failed save";
        var added = new User { Email = "jordan@example.com", Name = "Jordan Lee" };
        dbContext.Users.Add(added);

        dbContext.ChangeTracker.DiscardPendingChanges();

        Assert.Equal("Alex Rivera", user.Name);
        Assert.Equal(EntityState.Unchanged, dbContext.Entry(user).State);
        Assert.Equal(EntityState.Detached, dbContext.Entry(added).State);
        user.Name = "Saved later";
        await dbContext.SaveChangesAsync();
        Assert.Equal("Saved later", (await dbContext.Users.AsNoTracking().SingleAsync()).Name);
    }
}
