using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class ResumeVersionConfiguration : IEntityTypeConfiguration<ResumeVersion>
{
    public void Configure(EntityTypeBuilder<ResumeVersion> builder)
    {
        builder.ToTable("resume_versions");
        builder.HasKey(version => version.Id);
        builder.Property(version => version.Summary).HasMaxLength(1000).IsRequired();
        builder.Property(version => version.SkillsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(version => version.ProjectHighlightsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(version => version.StructuredDataJson).HasColumnType("jsonb").IsRequired();
        builder.Property(version => version.ExtractionMetadataJson).HasColumnType("jsonb").IsRequired();
        builder.Property(version => version.EvaluationRunId).HasMaxLength(120);
        builder.Property(version => version.CreatedAtUtc).IsRequired();
        builder
            .HasOne(version => version.ResumeDocument)
            .WithMany(document => document.Versions)
            .HasForeignKey(version => version.ResumeDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(version => new { version.ResumeDocumentId, version.CreatedAtUtc });
    }
}
