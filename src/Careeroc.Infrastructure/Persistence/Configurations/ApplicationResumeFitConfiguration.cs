using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class ApplicationResumeFitConfiguration : IEntityTypeConfiguration<ApplicationResumeFit>
{
    public void Configure(EntityTypeBuilder<ApplicationResumeFit> builder)
    {
        builder.ToTable("application_resume_fits");
        builder.HasKey(fit => fit.Id);
        builder.Property(fit => fit.MatchScore).IsRequired();
        builder.Property(fit => fit.StrengthsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(fit => fit.GapsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(fit => fit.Rationale).HasMaxLength(1000).IsRequired();
        builder.Property(fit => fit.CreatedAtUtc).IsRequired();
        builder.Property(fit => fit.UpdatedAtUtc).IsRequired();
        builder.HasIndex(fit => new { fit.JobApplicationId, fit.ResumeVersionId }).IsUnique();
        builder.HasIndex(fit => new { fit.UserId, fit.UpdatedAtUtc });

        builder
            .HasOne(fit => fit.User)
            .WithMany()
            .HasForeignKey(fit => fit.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(fit => fit.JobApplication)
            .WithMany()
            .HasForeignKey(fit => fit.JobApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(fit => fit.ResumeVersion)
            .WithMany()
            .HasForeignKey(fit => fit.ResumeVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
