using Careeroc.Application.Abstractions;
using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public static class UserAccounts
{
    // Where signed-in users get their row (demo workspaces create theirs). A first page load sends several requests at once,
    // and each may insert the row; the losers read the winner's row instead of failing on the
    // unique email index.
    public static async Task<User> GetOrCreateAsync(
        ICareerocDbContext dbContext,
        string? userEmail,
        string? userName,
        CancellationToken cancellationToken)
    {
        var email = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var user = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
        if (user is not null)
        {
            return user;
        }

        user = new User { Email = email, Name = DisplayName(userName, email) };
        dbContext.Users.Add(user);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return user;
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Entries<User>().Single(entry => entry.Entity == user).State = EntityState.Detached;
            var existing = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return existing;
        }
    }

    // The name arrives from the sign-in provider through a header, so it is cleaned to what the
    // users.name column (200 characters, no control characters) can hold.
    public static string DisplayName(string? userName, string email)
    {
        var cleaned = new string((userName ?? string.Empty).Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (cleaned.Length > 0)
        {
            return cleaned.Length > 200 ? cleaned[..200] : cleaned;
        }

        var separatorIndex = email.IndexOf('@');
        return separatorIndex > 0 ? email[..separatorIndex] : ApplicationDefaults.DefaultUserName;
    }
}
