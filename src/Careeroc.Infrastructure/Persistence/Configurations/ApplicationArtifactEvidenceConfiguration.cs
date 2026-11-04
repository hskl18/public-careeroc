using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class ApplicationArtifactEvidenceConfiguration : IEntityTypeConfiguration<ApplicationArtifactEvidence>
{
    public void Configure(EntityTypeBuilder<ApplicationArtifactEvidence> builder)
    {
        builder.ToTable("application_artifact_evidence");
        builder.HasKey(evidence => evidence.Id);
        builder.Property(evidence => evidence.ArtifactKind).HasMaxLength(80).IsRequired();
        builder.Property(evidence => evidence.FileName).HasMaxLength(240).IsRequired();
        builder.Property(evidence => evidence.ContentType).HasMaxLength(120).IsRequired();
        builder.Property(evidence => evidence.ContentHash).HasMaxLength(128).IsRequired();
        builder.Property(evidence => evidence.SourceLabel).HasMaxLength(500);
        builder.Property(evidence => evidence.SafeTitle).HasMaxLength(240).IsRequired();
        builder.Property(evidence => evidence.ExtractedFactType).HasMaxLength(80).IsRequired();
        builder.Property(evidence => evidence.ExtractedFactText).HasMaxLength(500).IsRequired();
        builder.Property(evidence => evidence.Confidence).HasPrecision(5, 4);
        builder.Property(evidence => evidence.SourceSnippet).HasMaxLength(500).IsRequired();
        builder.Property(evidence => evidence.ModelPath).HasMaxLength(160);
        builder.Property(evidence => evidence.Purpose).HasMaxLength(120).IsRequired();
        builder.Property(evidence => evidence.EvidenceSource).HasMaxLength(200).IsRequired();
        builder.Property(evidence => evidence.ReviewGateResult).HasMaxLength(80).IsRequired();
        builder.Property(evidence => evidence.FallbackPath).HasMaxLength(160).IsRequired();
        builder.Property(evidence => evidence.ReviewReason).HasMaxLength(280);
        builder.HasIndex(evidence => new { evidence.JobApplicationId, evidence.CreatedAtUtc });
        builder.HasIndex(evidence => new { evidence.UserId, evidence.CreatedAtUtc });

        builder
            .HasOne(evidence => evidence.User)
            .WithMany(user => user.ApplicationArtifactEvidence)
            .HasForeignKey(evidence => evidence.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(evidence => evidence.JobApplication)
            .WithMany(application => application.ArtifactEvidence)
            .HasForeignKey(evidence => evidence.JobApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
