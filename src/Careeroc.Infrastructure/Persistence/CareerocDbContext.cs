using Careeroc.Application.Abstractions;
using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Infrastructure.Persistence;

public sealed class CareerocDbContext : DbContext, ICareerocDbContext
{
    private readonly IClock _clock;

    public CareerocDbContext(DbContextOptions<CareerocDbContext> options, IClock clock)
        : base(options)
    {
        _clock = clock;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<InboxAccount> InboxAccounts => Set<InboxAccount>();
    public DbSet<InboxEmail> InboxEmails => Set<InboxEmail>();
    public DbSet<RecruitingContact> RecruitingContacts => Set<RecruitingContact>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<ApplicationResumeFit> ApplicationResumeFits => Set<ApplicationResumeFit>();
    public DbSet<JobApplicationEvent> JobApplicationEvents => Set<JobApplicationEvent>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<EmailProcessingJob> EmailProcessingJobs => Set<EmailProcessingJob>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ModelCallEvent> ModelCallEvents => Set<ModelCallEvent>();
    public DbSet<UserMemoryEntry> UserMemoryEntries => Set<UserMemoryEntry>();
    public DbSet<ResumeDocument> ResumeDocuments => Set<ResumeDocument>();
    public DbSet<ResumeVersion> ResumeVersions => Set<ResumeVersion>();
    public DbSet<ResumeEvaluation> ResumeEvaluations => Set<ResumeEvaluation>();
    public DbSet<PersonalAccessToken> PersonalAccessTokens => Set<PersonalAccessToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CareerocDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampNewRows();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampNewRows();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Entities carry no wall-clock defaults: a new row's timestamps that a service left unset take
    // the clock's time here, so tests with a fixed clock see the same times as the code under test.
    private void StampNewRows()
    {
        var now = _clock.UtcNow;
        foreach (var entry in ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added))
        {
            foreach (var property in entry.Properties)
            {
                if (property.Metadata.ClrType == typeof(DateTimeOffset) &&
                    property.CurrentValue is DateTimeOffset value &&
                    value == default)
                {
                    property.CurrentValue = now;
                }
            }
        }
    }
}
