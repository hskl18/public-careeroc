using Careeroc.Domain.Entities;
using Careeroc.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Email).HasMaxLength(320).IsRequired();
        builder.Property(user => user.Name).HasMaxLength(200).IsRequired();
        builder.Property(user => user.Role).HasMaxLength(40).HasDefaultValue(UserRoleNames.User).IsRequired();
        builder.Property(user => user.TimeZoneId).HasMaxLength(80).IsRequired();
        builder.Property(user => user.ProfileIcon).HasMaxLength(80).HasDefaultValue("inbox-blue").IsRequired();
        builder.Property(user => user.Location).HasMaxLength(160);
        builder.Property(user => user.TargetRole).HasMaxLength(200);
        builder.Property(user => user.WelcomeDismissedAtUtc);
        builder.HasIndex(user => user.Email).IsUnique();
    }
}
