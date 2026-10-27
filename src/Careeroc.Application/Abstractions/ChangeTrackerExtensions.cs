using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Careeroc.Application.Abstractions;

public static class ChangeTrackerExtensions
{
    // Undoes unsaved changes after a failed save, so the next save in the same context does not
    // retry them, while entities loaded earlier stay tracked (unlike ChangeTracker.Clear()).
    public static void DiscardPendingChanges(this ChangeTracker changeTracker)
    {
        foreach (var entry in changeTracker.Entries().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.State = EntityState.Detached;
                    break;
                case EntityState.Modified:
                    entry.CurrentValues.SetValues(entry.OriginalValues);
                    entry.State = EntityState.Unchanged;
                    break;
                case EntityState.Deleted:
                    entry.State = EntityState.Unchanged;
                    break;
            }
        }
    }
}
