using Careeroc.Application.Abstractions;
using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Infrastructure.Persistence;

public sealed class CareerocDbContext : DbContext, ICareerocDbContext
{
    public CareerocDbContext(DbContextOptions<CareerocDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<InboxAccount> InboxAccounts => Set<InboxAccount>();
    public DbSet<InboxEmail> InboxEmails => Set<InboxEmail>();
    public DbSet<RecruitingContact> RecruitingContacts => Set<RecruitingContact>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<ApplicationResumeFit> ApplicationResumeFits => Set<ApplicationResumeFit>();
    public DbSet<ApplicationArtifactEvidence> ApplicationArtifactEvidence => Set<ApplicationArtifactEvidence>();
    public DbSet<JobApplicationEvent> JobApplicationEvents => Set<JobApplicationEvent>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<EmailProcessingJob> EmailProcessingJobs => Set<EmailProcessingJob>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ModelCallEvent> ModelCallEvents => Set<ModelCallEvent>();
    public DbSet<UserMemoryEntry> UserMemoryEntries => Set<UserMemoryEntry>();
    public DbSet<UserSoulProfile> UserSoulProfiles => Set<UserSoulProfile>();
    public DbSet<ResumeDocument> ResumeDocuments => Set<ResumeDocument>();
    public DbSet<ResumeVersion> ResumeVersions => Set<ResumeVersion>();
    public DbSet<ResumeEvaluation> ResumeEvaluations => Set<ResumeEvaluation>();
    public DbSet<ResumeCorrection> ResumeCorrections => Set<ResumeCorrection>();
    public DbSet<PersonalAccessToken> PersonalAccessTokens => Set<PersonalAccessToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CareerocDbContext).Assembly);
    }
}
