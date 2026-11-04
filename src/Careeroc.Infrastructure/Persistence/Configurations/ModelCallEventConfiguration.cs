using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class ModelCallEventConfiguration : IEntityTypeConfiguration<ModelCallEvent>
{
    public void Configure(EntityTypeBuilder<ModelCallEvent> builder)
    {
        builder.ToTable("model_call_events");
        builder.HasKey(modelCallEvent => modelCallEvent.Id);
        builder.Property(modelCallEvent => modelCallEvent.Provider).HasMaxLength(80).IsRequired();
        builder.Property(modelCallEvent => modelCallEvent.Model).HasMaxLength(160).IsRequired();
        builder.Property(modelCallEvent => modelCallEvent.Purpose).HasMaxLength(120).IsRequired();
        builder.Property(modelCallEvent => modelCallEvent.Outcome).HasMaxLength(40).IsRequired();
        builder.Property(modelCallEvent => modelCallEvent.UserEmail).HasMaxLength(320);
        builder.Property(modelCallEvent => modelCallEvent.RunId).HasMaxLength(120);
        builder.Property(modelCallEvent => modelCallEvent.CorrelationId).HasMaxLength(120);
        builder.Property(modelCallEvent => modelCallEvent.EstimatedCostUsd).HasColumnType("numeric(12,6)");
        builder.Property(modelCallEvent => modelCallEvent.ErrorType).HasMaxLength(160);
        builder.HasIndex(modelCallEvent => modelCallEvent.OccurredAtUtc);
        builder.HasIndex(modelCallEvent => modelCallEvent.UserEmail);
        builder.HasIndex(modelCallEvent => modelCallEvent.InboxAccountId);
        builder.HasIndex(modelCallEvent => modelCallEvent.CorrelationId);
        builder.HasIndex(modelCallEvent => new { modelCallEvent.Purpose, modelCallEvent.OccurredAtUtc });
        builder.HasIndex(modelCallEvent => new { modelCallEvent.UserEmail, modelCallEvent.OccurredAtUtc });
    }
}
