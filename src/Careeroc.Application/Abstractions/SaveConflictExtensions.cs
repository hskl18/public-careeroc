using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Abstractions;

public static class SaveConflictExtensions
{
    // InboxAccount and UserMemoryEntry carry row versions, so a save can lose a race with another
    // request or a sync; the person is told to retry instead of getting a 500.
    public static async Task SaveOrAskToRetryAsync(
        this ICareerocDbContext dbContext,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new UserFacingException(message, exception);
        }
    }
}
