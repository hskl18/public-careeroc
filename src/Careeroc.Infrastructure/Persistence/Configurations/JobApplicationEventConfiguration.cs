using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class JobApplicationEventConfiguration : IEntityTypeConfiguration<JobApplicationEvent>
{
    public void Configure(EntityTypeBuilder<JobApplicationEvent> builder)
    {
        builder.ToTable("job_application_events");
        builder.HasKey(@event => @event.Id);
        builder.Property(@event => @event.EventType).HasMaxLength(100).IsRequired();
        builder.Property(@event => @event.ExtractedDataJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(@event => new { @event.JobApplicationId, @event.EventTimeUtc });

        builder
            .HasOne(@event => @event.JobApplication)
            .WithMany(application => application.Events)
            .HasForeignKey(@event => @event.JobApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(@event => @event.InboxEmail)
            .WithMany(email => email.Events)
            .HasForeignKey(@event => @event.InboxEmailId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
