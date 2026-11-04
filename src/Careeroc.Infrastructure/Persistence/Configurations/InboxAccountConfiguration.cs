using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class InboxAccountConfiguration : IEntityTypeConfiguration<InboxAccount>
{
    public void Configure(EntityTypeBuilder<InboxAccount> builder)
    {
        builder.ToTable("inbox_accounts");
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Provider).HasMaxLength(50).IsRequired();
        builder.Property(account => account.EmailAddress).HasMaxLength(320).IsRequired();
        builder.Property(account => account.OAuthMetadataJson)
            .HasColumnName("o_auth_metadata_json")
            .HasColumnType("jsonb");
        builder.Property(account => account.AccessTokenEncrypted).HasColumnType("text");
        builder.Property(account => account.RefreshTokenEncrypted).HasColumnType("text");
        builder.HasIndex(account => new { account.UserId, account.EmailAddress }).IsUnique();

        builder
            .HasOne(account => account.User)
            .WithMany(user => user.InboxAccounts)
            .HasForeignKey(account => account.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
