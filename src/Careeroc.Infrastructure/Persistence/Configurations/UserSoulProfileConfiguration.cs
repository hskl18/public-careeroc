using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class UserSoulProfileConfiguration : IEntityTypeConfiguration<UserSoulProfile>
{
    public void Configure(EntityTypeBuilder<UserSoulProfile> builder)
    {
        builder.ToTable("user_soul_profiles");
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.NarrativeTone).HasMaxLength(240).IsRequired();
        builder.Property(profile => profile.CareerNarrative).HasMaxLength(2000);
        builder.Property(profile => profile.CommunicationPreferencesJson).HasColumnType("jsonb");
        builder.Property(profile => profile.RenderedSoulMarkdown).HasColumnType("text").IsRequired();
        builder.Property(profile => profile.CreatedAtUtc).IsRequired();
        builder.Property(profile => profile.UpdatedAtUtc).IsRequired();
        builder
            .HasOne(profile => profile.User)
            .WithOne(user => user.SoulProfile)
            .HasForeignKey<UserSoulProfile>(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(profile => profile.UserId).IsUnique();
    }
}
