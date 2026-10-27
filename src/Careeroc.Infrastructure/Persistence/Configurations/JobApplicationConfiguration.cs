using Careeroc.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Careeroc.Infrastructure.Persistence.Configurations;

public sealed class JobApplicationConfiguration : IEntityTypeConfiguration<JobApplication>
{
    public void Configure(EntityTypeBuilder<JobApplication> builder)
    {
        builder.ToTable("job_applications");
        builder.HasKey(application => application.Id);
        builder.Property(application => application.Company).HasMaxLength(200).IsRequired();
        builder.Property(application => application.Role).HasMaxLength(200).IsRequired();
        builder.Property(application => application.RoleCanonical).HasMaxLength(120);
        builder.Property(application => application.Source).HasMaxLength(100).IsRequired();
        builder.Property(application => application.SourceSystem).HasMaxLength(80);
        builder.Property(application => application.Location).HasMaxLength(160);
        builder.Property(application => application.SeniorityLevel).HasMaxLength(40);
        builder.HasIndex(application => new { application.UserId, application.Company, application.Role });
        builder.HasIndex(application => new { application.UserId, application.LastActivityAtUtc });
        builder.HasIndex(application => new { application.UserId, application.RoleCanonical });
        builder.HasIndex(application => new { application.UserId, application.SourceSystem });

        builder
            .HasOne(application => application.User)
            .WithMany(user => user.JobApplications)
            .HasForeignKey(application => application.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
