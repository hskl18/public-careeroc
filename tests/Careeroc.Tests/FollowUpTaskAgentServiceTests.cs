using Careeroc.Application.Models;
using Careeroc.Application.Services.Agents;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class FollowUpTaskAgentServiceTests
{
    [Fact]
    public void CreatePlan_SuppressesLowSignalGenericActionRequiredUpdates()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 20, 0, 0, TimeSpan.Zero));
        var service = new FollowUpTaskAgentService(clock);
        var application = new JobApplication
        {
            Company = "Legrand North America",
            Role = "Applied Data Analytics Intern"
        };
        var parsed = new ParsedRecruitingEmail(
            EmailCategory.GenericUpdate,
            ApplicationStatus.Applied,
            application.Company,
            application.Role,
            ActionRequired: true,
            DueDateUtc: clock.UtcNow.AddDays(2),
            ClassificationConfidence: 0.71m,
            WorkflowNote: "Portal update did not contain a concrete user task.",
            ParsingSource: "test",
            ParsingModel: null);

        var plan = service.CreatePlan(application, parsed, parsed.DueDateUtc);

        Assert.False(plan.ShouldCreate);
        Assert.Contains("Suppressed low-signal", plan.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatePlan_KeepsRecruiterReplyActions()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 20, 0, 0, TimeSpan.Zero));
        var service = new FollowUpTaskAgentService(clock);
        var application = new JobApplication
        {
            Company = "Legrand North America",
            Role = "Applied Data Analytics Intern"
        };
        var parsed = new ParsedRecruitingEmail(
            EmailCategory.RecruiterOutreach,
            ApplicationStatus.RecruiterContact,
            application.Company,
            application.Role,
            ActionRequired: true,
            DueDateUtc: clock.UtcNow.AddDays(2),
            ClassificationConfidence: 0.89m,
            WorkflowNote: "Recruiter requested a reply.",
            ParsingSource: "test",
            ParsingModel: null);

        var plan = service.CreatePlan(application, parsed, parsed.DueDateUtc);

        Assert.True(plan.ShouldCreate);
        Assert.Equal(ReminderType.FollowUp, plan.Type);
        Assert.Equal("Reply to recruiter at Legrand North America", plan.Title);
        Assert.Equal("create_reminder", plan.ToolName);
        Assert.Contains("reply to recruiter", plan.ToolArgumentsJson!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreatePlan_LabelsCandidateQuestionnaireTasks()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 20, 0, 0, TimeSpan.Zero));
        var service = new FollowUpTaskAgentService(clock);
        var application = new JobApplication
        {
            Company = "Google",
            Role = "Student Researcher Program"
        };
        var parsed = new ParsedRecruitingEmail(
            EmailCategory.OaInvitation,
            ApplicationStatus.OaPending,
            application.Company,
            application.Role,
            ActionRequired: true,
            DueDateUtc: null,
            ClassificationConfidence: 0.90m,
            WorkflowNote: "Detected a candidate questionnaire or form request.",
            ParsingSource: "test",
            ParsingModel: null);

        var plan = service.CreatePlan(application, parsed, parsed.DueDateUtc);

        Assert.True(plan.ShouldCreate);
        Assert.Equal(ReminderType.AssessmentDeadline, plan.Type);
        Assert.Equal("Complete candidate questionnaire for Google", plan.Title);
    }

    [Fact]
    public void CreateToolCall_UsesSkipReminderTool_ForNonActionableUpdates()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 4, 20, 20, 0, 0, TimeSpan.Zero));
        var service = new FollowUpTaskAgentService(clock);
        var application = new JobApplication
        {
            Company = "Figma",
            Role = "Software Engineer"
        };
        var parsed = new ParsedRecruitingEmail(
            EmailCategory.GenericUpdate,
            ApplicationStatus.Applied,
            application.Company,
            application.Role,
            ActionRequired: false,
            DueDateUtc: null,
            ClassificationConfidence: 0.80m,
            WorkflowNote: "FYI only.",
            ParsingSource: "test",
            ParsingModel: null);

        var toolCall = service.CreateToolCall(application, parsed, parsed.DueDateUtc);

        Assert.Equal("skip_reminder", toolCall.Name);
        Assert.False(toolCall.Arguments.ShouldCreate);
    }
}
