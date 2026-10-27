using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
        builder.ToTable("reminders");
        builder.HasKey(reminder => reminder.Id);
        builder.Property(reminder => reminder.Title).HasMaxLength(250).IsRequired();
        builder.HasIndex(reminder => new { reminder.Status, reminder.DueAtUtc });

        builder
            .HasOne(reminder => reminder.JobApplication)
            .WithMany(application => application.Reminders)
            .HasForeignKey(reminder => reminder.JobApplicationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
