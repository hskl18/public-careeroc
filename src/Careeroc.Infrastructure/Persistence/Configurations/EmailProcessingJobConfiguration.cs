using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class EmailProcessingJobConfiguration : IEntityTypeConfiguration<EmailProcessingJob>
{
    public void Configure(EntityTypeBuilder<EmailProcessingJob> builder)
    {
        builder.ToTable("email_processing_jobs");
        builder.HasKey(job => job.Id);
        builder.Property(job => job.RunId).HasMaxLength(120);
        builder.Property(job => job.CorrelationId).HasMaxLength(120);
        builder.Property(job => job.LastError).HasColumnType("text");
        builder.Property(job => job.DeadLetterReason).HasMaxLength(280);
        builder.HasIndex(job => job.InboxEmailId).IsUnique();
        builder.HasIndex(job => new { job.Status, job.AvailableAtUtc });
        builder.HasIndex(job => new { job.Status, job.DeadLetteredAtUtc });
        builder.HasIndex(job => job.RunId);
        builder.HasIndex(job => job.CorrelationId);

        builder
            .HasOne(job => job.InboxEmail)
            .WithMany()
            .HasForeignKey(job => job.InboxEmailId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
