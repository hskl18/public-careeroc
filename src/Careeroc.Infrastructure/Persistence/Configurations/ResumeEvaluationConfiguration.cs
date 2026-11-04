using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class ResumeEvaluationConfiguration : IEntityTypeConfiguration<ResumeEvaluation>
{
    public void Configure(EntityTypeBuilder<ResumeEvaluation> builder)
    {
        builder.ToTable("resume_evaluations");
        builder.HasKey(evaluation => evaluation.Id);
        builder.Property(evaluation => evaluation.Headline).HasMaxLength(500).IsRequired();
        builder.Property(evaluation => evaluation.Verdict).HasMaxLength(1000).IsRequired();
        builder.Property(evaluation => evaluation.StrengthsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(evaluation => evaluation.GapsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(evaluation => evaluation.NextActionsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(evaluation => evaluation.SectionReviewsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(evaluation => evaluation.EvidenceJson).HasColumnType("jsonb").IsRequired();
        builder.Property(evaluation => evaluation.CreatedAtUtc).IsRequired();
        builder
            .HasOne(evaluation => evaluation.ResumeVersion)
            .WithOne(version => version.Evaluation)
            .HasForeignKey<ResumeEvaluation>(evaluation => evaluation.ResumeVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(evaluation => evaluation.ResumeVersionId).IsUnique();
    }
}
