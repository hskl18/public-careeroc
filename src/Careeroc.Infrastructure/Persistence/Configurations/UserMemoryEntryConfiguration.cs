using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class UserMemoryEntryConfiguration : IEntityTypeConfiguration<UserMemoryEntry>
{
    public void Configure(EntityTypeBuilder<UserMemoryEntry> builder)
    {
        builder.ToTable("user_memory_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Kind).HasMaxLength(80).IsRequired();
        builder.Property(entry => entry.Key).HasMaxLength(160).IsRequired();
        builder.Property(entry => entry.Value).HasMaxLength(UserMemoryEntry.MaxStoredValueLength).IsRequired();
        builder.Property(entry => entry.Summary).HasMaxLength(UserMemoryEntry.MaxSummaryLength);
        builder.Property(entry => entry.SourceType).HasMaxLength(80).IsRequired();
        builder.Property(entry => entry.ConfirmationStatus).HasMaxLength(40).IsRequired();
        builder.Property(entry => entry.Confidence).IsRequired();
        builder.Property(entry => entry.CreatedAtUtc).IsRequired();
        builder.Property(entry => entry.UpdatedAtUtc).IsRequired();
        // Postgres's xmin as a row version: two requests that both read a pending proposal cannot
        // both resolve it, so a double-clicked approval creates its reminders once.
        builder.Property<uint>("RowVersion").HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        builder
            .HasOne(entry => entry.User)
            .WithMany(user => user.MemoryEntries)
            .HasForeignKey(entry => entry.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(entry => new { entry.UserId, entry.Kind });
        builder.HasIndex(entry => new { entry.UserId, entry.ConfirmationStatus });
        builder.HasIndex(entry => new { entry.UserId, entry.Key });
        builder.HasIndex(entry => new
        {
            entry.UserId,
            entry.ConfirmationStatus,
            entry.ExpiresAtUtc,
            entry.UpdatedAtUtc
        }).HasDatabaseName("ix_user_memory_entries_user_status_exp_updated");
        builder.HasIndex(entry => new
        {
            entry.UserId,
            entry.Kind,
            entry.Key,
            entry.ConfirmationStatus
        }).HasDatabaseName("ix_user_memory_entries_user_kind_key_status");
    }
}
