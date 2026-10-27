using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Careeroc.Application.Abstractions;

public interface ICareerocDbContext
{
    DbSet<User> Users { get; }
    DbSet<InboxAccount> InboxAccounts { get; }
    DbSet<InboxEmail> InboxEmails { get; }
    DbSet<RecruitingContact> RecruitingContacts { get; }
    DbSet<JobApplication> JobApplications { get; }
    DbSet<ApplicationResumeFit> ApplicationResumeFits { get; }
    DbSet<ApplicationArtifactEvidence> ApplicationArtifactEvidence { get; }
    DbSet<JobApplicationEvent> JobApplicationEvents { get; }
    DbSet<Reminder> Reminders { get; }
    DbSet<EmailProcessingJob> EmailProcessingJobs { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<ModelCallEvent> ModelCallEvents { get; }
    DbSet<UserMemoryEntry> UserMemoryEntries { get; }
    DbSet<UserSoulProfile> UserSoulProfiles { get; }
    DbSet<ResumeDocument> ResumeDocuments { get; }
    DbSet<ResumeVersion> ResumeVersions { get; }
    DbSet<ResumeEvaluation> ResumeEvaluations { get; }
    DbSet<ResumeCorrection> ResumeCorrections { get; }
    DbSet<PersonalAccessToken> PersonalAccessTokens { get; }
    ChangeTracker ChangeTracker { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
