using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class ResumeDocumentConfiguration : IEntityTypeConfiguration<ResumeDocument>
{
    public void Configure(EntityTypeBuilder<ResumeDocument> builder)
    {
        builder.ToTable("resume_documents");
        builder.HasKey(document => document.Id);
        builder.Property(document => document.Title).HasMaxLength(240).IsRequired();
        builder.Property(document => document.SourceKind).HasMaxLength(40).IsRequired();
        builder.Property(document => document.SourceFileName).HasMaxLength(240);
        builder.Property(document => document.SourceText).HasColumnType("text").IsRequired();
        builder.Property(document => document.SourceLinksJson).HasColumnType("jsonb");
        builder.Property(document => document.CreatedAtUtc).IsRequired();
        builder.Property(document => document.UpdatedAtUtc).IsRequired();
        builder
            .HasOne(document => document.User)
            .WithMany(user => user.ResumeDocuments)
            .HasForeignKey(document => document.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(document => new { document.UserId, document.UpdatedAtUtc });
    }
}
