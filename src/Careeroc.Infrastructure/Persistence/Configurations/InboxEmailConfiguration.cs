using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class InboxEmailConfiguration : IEntityTypeConfiguration<InboxEmail>
{
    public void Configure(EntityTypeBuilder<InboxEmail> builder)
    {
        builder.ToTable("inbox_emails");
        builder.HasKey(email => email.Id);
        builder.Property(email => email.ExternalMessageId).HasMaxLength(200).IsRequired();
        builder.Property(email => email.ThreadId).HasMaxLength(200).IsRequired();
        builder.Property(email => email.Sender).HasMaxLength(320).IsRequired();
        builder.Property(email => email.Subject).HasMaxLength(500).IsRequired();
        builder.Property(email => email.RawBody).HasColumnType("text").IsRequired();
        builder.Property(email => email.ParsedBody).HasColumnType("text").IsRequired();
        builder.Property(email => email.IsOutbound).HasDefaultValue(false);
        builder.Property(email => email.ContactName).HasMaxLength(200);
        builder.Property(email => email.ContactEmail).HasMaxLength(320);
        builder.Property(email => email.ContactRoleHint).HasMaxLength(200);
        builder.Property(email => email.ExtractedCompany).HasMaxLength(200);
        builder.Property(email => email.ExtractedRole).HasMaxLength(200);
        builder.Property(email => email.ClassificationConfidence).HasColumnType("numeric(4,2)");
        builder.Property(email => email.MatchingConfidence).HasColumnType("numeric(4,2)");
        builder.Property(email => email.ReviewReason).HasColumnType("text");
        builder.Property(email => email.ProcessingNotes).HasColumnType("text");
        builder.Property(email => email.ProcessingTraceJson).HasColumnType("jsonb");
        builder.HasIndex(email => new { email.RequiresManualReview, email.ReceivedAtUtc });
        builder.HasIndex(email => new { email.InboxAccountId, email.ExternalMessageId }).IsUnique();
        builder.HasIndex(email => new { email.InboxAccountId, email.ReceivedAtUtc });
        builder.HasIndex(email => new { email.InboxAccountId, email.ThreadId, email.ReceivedAtUtc });
        builder.HasIndex(email => new { email.JobApplicationId, email.ReceivedAtUtc });
        builder.HasIndex(email => new { email.RecruitingContactId, email.ReceivedAtUtc });

        builder
            .HasOne(email => email.InboxAccount)
            .WithMany(account => account.Emails)
            .HasForeignKey(email => email.InboxAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(email => email.JobApplication)
            .WithMany(application => application.Emails)
            .HasForeignKey(email => email.JobApplicationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(email => email.RecruitingContact)
            .WithMany(contact => contact.Emails)
            .HasForeignKey(email => email.RecruitingContactId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
