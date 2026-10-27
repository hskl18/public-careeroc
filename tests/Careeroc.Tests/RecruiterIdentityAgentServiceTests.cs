using Careeroc.Application.Models;
using Careeroc.Application.Services.Agents;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class RecruiterIdentityAgentServiceTests
{
    [Fact]
    public void Identify_ExtractsRecruiterIdentity_FromNamedMailbox()
    {
        var service = new RecruiterIdentityAgentService();
        var email = new InboxEmail
        {
            Sender = "Priya Shah <priya.shah@openai.com>",
            Subject = "Open to a quick intro chat?",
            ParsedBody = "Hi Alex, I am a Technical Recruiter at OpenAI and would love to connect."
        };

        var decision = service.Identify(
            email,
            new ParsedRecruitingEmail(
                EmailCategory.RecruiterOutreach,
                ApplicationStatus.RecruiterContact,
                "OpenAI",
                "Research Engineer",
                true,
                null,
                0.92m,
                "Detected recruiter outreach.",
                "Stub parser",
                null));

        Assert.Equal("Priya Shah", decision.ContactName);
        Assert.Equal("priya.shah@openai.com", decision.ContactEmail);
        Assert.Equal(RecruitingContactType.Recruiter, decision.ContactType);
        Assert.Contains("Recruiter", decision.RoleHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Identify_ExtractsCoordinatorIdentity_FromSchedulingMailbox()
    {
        var service = new RecruiterIdentityAgentService();
        var email = new InboxEmail
        {
            Sender = "coordinator@figma.com",
            Subject = "Interview confirmed",
            ParsedBody = "Your interview coordinator will send the final details shortly."
        };

        var decision = service.Identify(
            email,
            new ParsedRecruitingEmail(
                EmailCategory.InterviewConfirmation,
                ApplicationStatus.Interviewing,
                "Figma",
                "Product Engineer",
                true,
                null,
                0.88m,
                "Detected interview confirmation.",
                "Stub parser",
                null));

        Assert.Null(decision.ContactName);
        Assert.Equal("coordinator@figma.com", decision.ContactEmail);
        Assert.Equal(RecruitingContactType.Coordinator, decision.ContactType);
        Assert.Equal("Recruiting Coordinator", decision.RoleHint);
    }
}
