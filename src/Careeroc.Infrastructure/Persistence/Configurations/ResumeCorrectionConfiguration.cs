using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class ResumeCorrectionConfiguration : IEntityTypeConfiguration<ResumeCorrection>
{
    public void Configure(EntityTypeBuilder<ResumeCorrection> builder)
    {
        builder.ToTable("resume_corrections");
        builder.HasKey(correction => correction.Id);
        builder.Property(correction => correction.FieldPath).HasMaxLength(240).IsRequired();
        builder.Property(correction => correction.PreviousValue).HasMaxLength(2000);
        builder.Property(correction => correction.CorrectedValue).HasMaxLength(2000).IsRequired();
        builder.Property(correction => correction.Note).HasMaxLength(1000);
        builder.Property(correction => correction.CreatedAtUtc).IsRequired();
        builder
            .HasOne(correction => correction.ResumeVersion)
            .WithMany(version => version.Corrections)
            .HasForeignKey(correction => correction.ResumeVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(correction => new { correction.ResumeVersionId, correction.CreatedAtUtc });
    }
}
