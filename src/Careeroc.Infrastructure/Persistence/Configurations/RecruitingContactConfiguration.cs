using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class RecruitingContactConfiguration : IEntityTypeConfiguration<RecruitingContact>
{
    public void Configure(EntityTypeBuilder<RecruitingContact> builder)
    {
        builder.ToTable("recruiting_contacts");
        builder.HasKey(contact => contact.Id);
        builder.Property(contact => contact.Email).HasMaxLength(320).IsRequired();
        builder.Property(contact => contact.DisplayName).HasMaxLength(200);
        builder.Property(contact => contact.CompanyHint).HasMaxLength(200);
        builder.Property(contact => contact.RoleHint).HasMaxLength(200);
        builder.Property(contact => contact.Confidence).HasColumnType("numeric(4,2)");
        builder.HasIndex(contact => new { contact.UserId, contact.Email }).IsUnique();
        builder.HasIndex(contact => new { contact.UserId, contact.LastSeenAtUtc });

        builder
            .HasOne(contact => contact.User)
            .WithMany(user => user.RecruitingContacts)
            .HasForeignKey(contact => contact.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
