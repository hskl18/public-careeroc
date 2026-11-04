using System.Text.Json;
using Careeroc.Application.Abstractions;
using Careeroc.Application.Models;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Application.Services.Agents;

public sealed class FollowUpTaskAgentService
{
    private readonly IClock _clock;

    public FollowUpTaskAgentService(IClock clock)
    {
        _clock = clock;
    }

    public FollowUpTaskPlan CreatePlan(
        JobApplication application,
        ParsedRecruitingEmail parsed,
        DateTimeOffset? normalizedDueDateUtc)
    {
        var toolCall = CreateToolCall(application, parsed, normalizedDueDateUtc);
        return ToPlan(toolCall);
    }

    public FollowUpTaskToolCall CreateToolCall(
        JobApplication application,
        ParsedRecruitingEmail parsed,
        DateTimeOffset? normalizedDueDateUtc)
    {
        if (!parsed.ActionRequired)
        {
            return new FollowUpTaskToolCall(
                "skip_reminder",
                new FollowUpTaskToolArguments(
                    false,
                    ReminderType.FollowUp,
                    string.Empty,
                    normalizedDueDateUtc ?? _clock.UtcNow.AddDays(2)),
                "No human follow-up is required for this workflow update.");
        }

        if (!IsActionableCategory(parsed.Category))
        {
            return new FollowUpTaskToolCall(
                "skip_reminder",
                new FollowUpTaskToolArguments(
                    false,
                    ReminderType.FollowUp,
                    string.Empty,
                    normalizedDueDateUtc ?? _clock.UtcNow.AddDays(2)),
                $"Suppressed low-signal follow-up task for {parsed.Category}.");
        }

        var reminderType = parsed.Category switch
        {
            EmailCategory.OaInvitation => ReminderType.AssessmentDeadline,
            EmailCategory.InterviewInvitation or EmailCategory.InterviewConfirmation => ReminderType.InterviewPreparation,
            _ => ReminderType.FollowUp
        };

        var dueAtUtc = normalizedDueDateUtc ?? _clock.UtcNow.AddDays(2);
        var title = parsed.Category switch
        {
            EmailCategory.OaInvitation when IsQuestionnaireOrFormTask(parsed) => $"Complete candidate questionnaire for {application.Company}",
            EmailCategory.OaInvitation => $"Complete assessment for {application.Company}",
            EmailCategory.InterviewInvitation => $"Prepare for interview with {application.Company}",
            EmailCategory.InterviewConfirmation => $"Confirm interview details for {application.Company}",
            EmailCategory.RecruiterOutreach => $"Reply to recruiter at {application.Company}",
            EmailCategory.Offer => $"Review the offer from {application.Company}",
            _ => $"Review next step for {application.Company}"
        };

        return new FollowUpTaskToolCall(
            "create_reminder",
            new FollowUpTaskToolArguments(
                true,
                reminderType,
                title,
                dueAtUtc),
            $"Planned a follow-up task from the {parsed.Category} workflow state.");
    }

    private static FollowUpTaskPlan ToPlan(FollowUpTaskToolCall toolCall)
    {
        return new FollowUpTaskPlan(
            toolCall.Arguments.ShouldCreate,
            toolCall.Arguments.Type,
            toolCall.Arguments.Title,
            toolCall.Arguments.DueAtUtc,
            toolCall.Reason,
            "Follow-Up Task Agent",
            toolCall.Name,
            JsonSerializer.Serialize(toolCall.Arguments, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static bool IsActionableCategory(EmailCategory category)
    {
        return category is
            EmailCategory.RecruiterOutreach or
            EmailCategory.OaInvitation or
            EmailCategory.InterviewInvitation or
            EmailCategory.InterviewConfirmation or
            EmailCategory.Offer;
    }

    private static bool IsQuestionnaireOrFormTask(ParsedRecruitingEmail parsed)
    {
        return parsed.WorkflowNote.Contains("questionnaire", StringComparison.OrdinalIgnoreCase) ||
               parsed.WorkflowNote.Contains("form request", StringComparison.OrdinalIgnoreCase);
    }
}
