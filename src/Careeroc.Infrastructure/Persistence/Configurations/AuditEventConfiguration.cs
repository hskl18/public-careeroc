using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.HasKey(auditEvent => auditEvent.Id);
        builder.Property(auditEvent => auditEvent.EventType).HasMaxLength(160).IsRequired();
        builder.Property(auditEvent => auditEvent.Outcome).HasMaxLength(40).IsRequired();
        builder.Property(auditEvent => auditEvent.UserEmail).HasMaxLength(320);
        builder.Property(auditEvent => auditEvent.TargetEmail).HasMaxLength(320);
        builder.Property(auditEvent => auditEvent.ResourceType).HasMaxLength(80);
        builder.Property(auditEvent => auditEvent.Method).HasMaxLength(12);
        builder.Property(auditEvent => auditEvent.Path).HasMaxLength(500);
        builder.Property(auditEvent => auditEvent.RemoteIp).HasMaxLength(80);
        builder.Property(auditEvent => auditEvent.Detail).HasMaxLength(1000);
        builder.HasIndex(auditEvent => auditEvent.OccurredAtUtc);
        builder.HasIndex(auditEvent => auditEvent.UserEmail);
        builder.HasIndex(auditEvent => new { auditEvent.EventType, auditEvent.OccurredAtUtc });
    }
}
