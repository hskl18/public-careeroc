using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Contracts.Resumes;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Careeroc.Application.Services;

public sealed partial class ResumeIntelligenceService
{
    private async Task UpsertResumeEvaluationMemoryAsync(
        Guid userId,
        string resumeTitle,
        Guid resumeEvaluationId,
        ResumeExtraction extraction,
        ResumeEvaluationResult evaluation,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        const string kind = "resume";
        const string key = "resume.latest_evaluation";
        var value = JsonSerializer.Serialize(
            new ResumeEvaluationMemory(
                resumeTitle,
                evaluation.Score,
                extraction.Skills,
                extraction.ExperienceCount,
                extraction.ProjectCount,
                extraction.HasEducation,
                extraction.HasContact,
                extraction.HasLinks,
                evaluation.NextActions),
            JsonOptions);
        var summary = $"{resumeTitle}: resume score {evaluation.Score}/100.";
        var entry = await _dbContext.UserMemoryEntries
            .FirstOrDefaultAsync(
                candidate =>
                    candidate.UserId == userId &&
                    candidate.Kind == kind &&
                    candidate.Key == key,
                cancellationToken);

        if (entry is null)
        {
            _dbContext.UserMemoryEntries.Add(
                new UserMemoryEntry
                {
                    UserId = userId,
                    Kind = kind,
                    Key = key,
                    Value = value,
                    Summary = summary,
                    SourceType = "resume_evaluation",
                    SourceId = resumeEvaluationId,
                    Confidence = 0.9,
                    ConfirmationStatus = "confirmed",
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                });
            return;
        }

        entry.Value = value;
        entry.Summary = summary;
        entry.SourceType = "resume_evaluation";
        entry.SourceId = resumeEvaluationId;
        entry.Confidence = 0.9;
        entry.ConfirmationStatus = "confirmed";
        entry.UpdatedAtUtc = nowUtc;
    }

    private async Task<User> GetOrCreateUserAsync(string? userEmail, CancellationToken cancellationToken)
    {
        var resolvedUserEmail = ApplicationDefaults.NormalizeRequiredUserEmail(userEmail);
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.Email == resolvedUserEmail, cancellationToken);

        if (user is not null)
        {
            return user;
        }

        user = new User
        {
            Email = resolvedUserEmail,
            Name = ResolveUserName(resolvedUserEmail)
        };
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return user;
    }

    private async Task<ResumeEvaluationContext> LoadEvaluationContextAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var userContext = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.TargetRole,
                user.Location
            })
            .SingleAsync(cancellationToken);
        var mailboxSnapshot = await _resumeMailboxContextBuilder.BuildForUserAsync(userId, cancellationToken);
        var applicationStats = mailboxSnapshot.Applications
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                InterviewOrOffer = group.Count(application => application.Status == ApplicationStatus.Interviewing || application.Status == ApplicationStatus.Offer)
            })
            .FirstOrDefault();

        return new ResumeEvaluationContext(
            userContext.TargetRole,
            userContext.Location,
            applicationStats?.Total ?? 0,
            applicationStats?.InterviewOrOffer ?? 0);
    }

    private static string ResolveUserName(string userEmail)
    {
        var separatorIndex = userEmail.IndexOf('@');
        return separatorIndex > 0 ? userEmail[..separatorIndex] : ApplicationDefaults.DefaultUserName;
    }
}
