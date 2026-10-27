using Careeroc.Application;
using Careeroc.Domain.Entities;
using Careeroc.Domain.Enums;
using Careeroc.Infrastructure.Email;

namespace Careeroc.Tests;

public sealed class HeuristicRecruitingEmailParserTests
{
    [Fact]
    public void Parse_ClassifiesAssessmentEmail_AndExtractsStructuredFields()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "recruiting@ramp.com",
            Subject = "Ramp Online Assessment for Software Engineer",
            ParsedBody = "Please complete the online assessment for the Software Engineer role by April 10, 2026."
        };

        var parsed = parser.Parse(email);

        Assert.Equal(EmailCategory.OaInvitation, parsed.Category);
        Assert.Equal(ApplicationStatus.OaPending, parsed.RecommendedStatus);
        Assert.Equal("Ramp", parsed.Company);
        Assert.Equal("Software Engineer", parsed.Role);
        Assert.True(parsed.ActionRequired);
        Assert.Equal(new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero), parsed.DueDateUtc);
    }

    [Fact]
    public void Parse_KeepsCompanyOnTheSubjectLine_WhenTheBodyOpensWithAGreeting()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "Harborline Talent <talent@harborline.example>",
            Subject = "Update on your application to Harborline",
            ParsedBody = "Hi Alex, after careful consideration we have decided not to move forward with your candidacy."
        };

        var parsed = parser.Parse(email);

        Assert.Equal("Harborline", parsed.Company);
        Assert.Equal(EmailCategory.Rejection, parsed.Category);
    }

    [Fact]
    public void Parse_DoesNotTreatAnAgencyAsTheEmployerOfAConfidentialClient()
    {
        var parsed = new HeuristicRecruitingEmailParser().Parse(new InboxEmail
        {
            Sender = "Riley Chen <riley@brightpathtalent.example>",
            Subject = "Backend Engineer opportunity with a confidential client",
            ParsedBody = "Hi Alex, I'm partnering with a confidential client on a Backend Engineer search. Would you be open to a quick call?"
        });

        Assert.Equal(EmailCategory.RecruiterOutreach, parsed.Category);
        Assert.Equal(ApplicationDefaults.UnknownCompany, parsed.Company);
    }

    [Fact]
    public void Parse_TreatsACodingAssessmentAsAnAssessmentInvitation()
    {
        var parsed = new HeuristicRecruitingEmailParser().Parse(new InboxEmail
        {
            Sender = "Pinecrest Bank Talent <talent@pinecrest.example>",
            Subject = "Next step for the Software Development Engineer position",
            ParsedBody = "Hi Alex, the next step is a coding assessment. Please complete it by November 2, 2026."
        });

        Assert.Equal(EmailCategory.OaInvitation, parsed.Category);
    }

    [Fact]
    public void Parse_TreatsARecruiterScreenAsAnInterviewInvitation()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "Diego Alvarez <diego.alvarez@harborline.example>",
            Subject = "Recruiter screen for Software Engineer I at Harborline",
            ParsedBody = "Hi Alex, I'd like to schedule a 30 minute recruiter screen. Please share a few times that work for you."
        };

        Assert.Equal(EmailCategory.InterviewInvitation, parser.Parse(email).Category);
    }

    [Fact]
    public void Parse_ExtractsAssessmentWindowDeadline_FromRelativeDuration()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "assessment@carrier.com",
            Subject = "Carrier assessment invitation",
            ParsedBody = "Please complete your assessment within 48 hours of receiving this email.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 24, 18, 30, 0, TimeSpan.Zero)
        };

        var parsed = parser.Parse(email);

        Assert.Equal(EmailCategory.OaInvitation, parsed.Category);
        Assert.Equal(new DateTimeOffset(2026, 4, 26, 18, 30, 0, TimeSpan.Zero), parsed.DueDateUtc);
    }

    [Fact]
    public void Parse_ExtractsNumericAssessmentDeadline()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "assessment@veeva.com",
            Subject = "Veeva assessment",
            ParsedBody = "Your assessment is due by 04/29/2026.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 24, 18, 0, 0, TimeSpan.Zero)
        };

        var parsed = parser.Parse(email);

        Assert.Equal(EmailCategory.OaInvitation, parsed.Category);
        Assert.Equal(new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero), parsed.DueDateUtc);
    }

    [Fact]
    public void Parse_ExtractsHackerrankAssessmentSponsor_FromTestTitle()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "HackerRank <no-reply@hackerrankmail.com>",
            Subject = "Bot Auto Software Intern Test - Infra",
            ParsedBody = """
                Powered by HackerRank

                Thank you for your interest in joining Bot Auto! We're excited to invite you to the next step in the process: the HackerRank Bot Auto Software Intern Test - Infra assessment.
                Timeline: Please complete the assessment within 7 calendar days, or as discussed with your recruiter.
                End Login Date/Time: 3 May 2026 09:00 AM PDT (America - Los Angeles)
                """,
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 24, 19, 19, 0, TimeSpan.Zero)
        };

        var parsed = parser.Parse(email);

        Assert.Equal(EmailCategory.OaInvitation, parsed.Category);
        Assert.Equal(ApplicationStatus.OaPending, parsed.RecommendedStatus);
        Assert.Equal("Bot Auto", parsed.Company);
        Assert.Equal("Software Intern", parsed.Role);
        Assert.True(parsed.ActionRequired);
        Assert.Equal(new DateTimeOffset(2026, 5, 3, 16, 0, 0, TimeSpan.Zero), parsed.DueDateUtc);
    }

    [Fact]
    public void Parse_ExtractsAssessmentWindowDeadline_FromCalendarDays()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "HackerRank <no-reply@hackerrankmail.com>",
            Subject = "Bot Auto Software Intern Test - Infra",
            ParsedBody = "Timeline: Please complete the assessment within 7 calendar days.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 24, 19, 19, 0, TimeSpan.Zero)
        };

        var parsed = parser.Parse(email);

        Assert.Equal(EmailCategory.OaInvitation, parsed.Category);
        Assert.Equal(new DateTimeOffset(2026, 5, 1, 19, 19, 0, TimeSpan.Zero), parsed.DueDateUtc);
    }

    [Fact]
    public void Parse_ClassifiesCandidateQuestionnaire_AsActionableAssessmentStep()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "Google Recruiting Program (no reply) <google-recruiting-noreply@google.com>",
            Subject = "Candidate questionnaire: Jordan Lee",
            ParsedBody = "Thank you for your interest in the Student Researcher Program at Google! Please complete the candidate questionnaire as soon as possible. Your profile will not be shared until this form is submitted.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 23, 7, 14, 0, TimeSpan.Zero)
        };

        var parsed = parser.Parse(email);

        Assert.Equal(EmailCategory.OaInvitation, parsed.Category);
        Assert.Equal(ApplicationStatus.OaPending, parsed.RecommendedStatus);
        Assert.Equal("Google", parsed.Company);
        Assert.Equal("Student Researcher Program", parsed.Role);
        Assert.True(parsed.ActionRequired);
        Assert.Null(parsed.DueDateUtc);
        Assert.Contains("questionnaire", parsed.WorkflowNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_ClassifiesRecruiterOutreach_FromCommonOutreachLanguage()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "maya@stripe.com",
            Subject = "Reaching out about a Senior Backend Engineer opportunity at Stripe",
            ParsedBody = "Hi Alex, I came across your background and thought you could be a strong fit for the Senior Backend Engineer role at Stripe. Would you be open to a quick intro chat this week?"
        };

        var parsed = parser.Parse(email);

        Assert.Equal(EmailCategory.RecruiterOutreach, parsed.Category);
        Assert.Equal(ApplicationStatus.RecruiterContact, parsed.RecommendedStatus);
        Assert.Equal("Stripe", parsed.Company);
        Assert.Equal("Senior Backend Engineer", parsed.Role);
        Assert.True(parsed.ActionRequired);
    }

    [Fact]
    public void Parse_ClassifiesInterviewSchedulingEmail_AndExtractsRole()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "coordinator@figma.com",
            Subject = "Please select a time for your Product Engineer interview at Figma",
            ParsedBody = "Hi Alex, we'd love to schedule an interview for Product Engineer at Figma next week. Please select a time that works for you."
        };

        var parsed = parser.Parse(email);

        Assert.Equal(EmailCategory.InterviewInvitation, parsed.Category);
        Assert.Equal(ApplicationStatus.Interviewing, parsed.RecommendedStatus);
        Assert.Equal("Figma", parsed.Company);
        Assert.Equal("Product Engineer", parsed.Role);
    }

    [Fact]
    public void Parse_ClassifiesZoomInterviewAvailabilityRequest_AndExtractsRole()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "Riley Park <RileyPark@paypal.com>",
            Subject = "PayPal Zoom Interview Availability Request",
            ParsedBody = """
                Hi Jordan,

                After sharing your resume with the team, they were impressed by your background and would like to invite you to interview via Zoom, to tell you a little more about the Data Operations Intern position and get to know you better.

                Please send over at least 3 dates with corresponding times and local time zone for interview this week/next week so that I can get you scheduled!
                """,
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 27, 22, 57, 0, TimeSpan.Zero)
        };

        var parsed = parser.Parse(email);

        Assert.Equal(EmailCategory.InterviewInvitation, parsed.Category);
        Assert.Equal(ApplicationStatus.Interviewing, parsed.RecommendedStatus);
        Assert.Equal("PayPal", parsed.Company);
        Assert.Equal("Data Operations Intern", parsed.Role);
        Assert.True(parsed.ActionRequired);
    }

    [Fact]
    public void Parse_ClassifiesInterviewConfirmationSubject_FromTikTokPortal()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "TikTok <noreply@careers.tiktok.com>",
            Subject = "ByteDance: Confirmation of your interview",
            ParsedBody = "Your interview has been scheduled. Please review the details in the candidate portal."
        };

        var parsed = parser.Parse(email);

        Assert.Equal(EmailCategory.InterviewConfirmation, parsed.Category);
        Assert.Equal(ApplicationStatus.Interviewing, parsed.RecommendedStatus);
        Assert.Equal("TikTok", parsed.Company);
        Assert.True(parsed.ActionRequired);
    }

    [Fact]
    public void Parse_ClassifiesInstitutionalOfferPaperwork_WithReadableFallbackRole()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var inboxAccount = new InboxAccount
        {
            EmailAddress = "jlee@example.edu"
        };
        var email = new InboxEmail
        {
            InboxAccount = inboxAccount,
            Sender = "\"Northgate Physics HR/AP\" <hr-ap-phys@example.edu>",
            Subject = "ACTION REQUIRED: Signature on Offer Letter and Hire Paperwork Via Onboarding Solutions",
            ParsedBody = "We have not received your signed Offer Letter or the hire documents sent via Onboarding Solutions. You must sign the Offer Letter by March 25th.",
            ReceivedAtUtc = new DateTimeOffset(2026, 3, 20, 18, 0, 0, TimeSpan.Zero)
        };

        var parsed = parser.Parse(email);

        Assert.Equal(EmailCategory.Offer, parsed.Category);
        Assert.Equal(ApplicationStatus.Offer, parsed.RecommendedStatus);
        Assert.Equal("Northgate Physics", parsed.Company);
        Assert.Equal("Offer Letter / Hire Paperwork", parsed.Role);
        Assert.True(parsed.ActionRequired);
        Assert.Equal(new DateTimeOffset(2026, 3, 25, 0, 0, 0, TimeSpan.Zero), parsed.DueDateUtc);
    }

    [Fact]
    public void Parse_DoesNotInferBareCampusCompany_FromSameInstitutionMailboxDomain()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var inboxAccount = new InboxAccount
        {
            EmailAddress = "jlee@example.edu"
        };
        var email = new InboxEmail
        {
            InboxAccount = inboxAccount,
            Sender = "\"Recruiting\" <recruiting@example.edu>",
            Subject = "Thank you for applying",
            ParsedBody = "Thank you for applying for the AI Intern role. We will review your materials soon."
        };

        var parsed = parser.Parse(email);

        Assert.Equal(EmailCategory.ApplicationReceived, parsed.Category);
        Assert.Equal(ApplicationDefaults.UnknownCompany, parsed.Company);
        Assert.Equal("AI Intern", parsed.Role);
    }

    [Fact]
    public void Parse_DoesNotTreatTemporalRejectionPhrase_AsCompany()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var inboxAccount = new InboxAccount
        {
            EmailAddress = "jlee@example.edu"
        };
        var email = new InboxEmail
        {
            InboxAccount = inboxAccount,
            Sender = "\"Recruiting\" <no-reply@example.edu>",
            Subject = "Application update",
            ParsedBody = "Thank you for your interest in the AI/ML Engineer role. Unfortunately, we will not be moving forward with your application at this time."
        };

        var parsed = parser.Parse(email);

        Assert.Equal(EmailCategory.Rejection, parsed.Category);
        Assert.Equal(ApplicationDefaults.UnknownCompany, parsed.Company);
    }

    [Fact]
    public void Parse_DoesNotPromotePersonalSenderNameIntoCompany()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "\"Okafor, Dana\" <dana.okafor@gmail.com>",
            Subject = "Re: availability",
            ParsedBody = "Thanks for your reply. We will get back to you soon."
        };

        var parsed = parser.Parse(email);

        Assert.Equal(ApplicationDefaults.UnknownCompany, parsed.Company);
        Assert.Equal(ApplicationDefaults.UnknownRole, parsed.Role);
    }

    [Theory]
    [InlineData("do-not-reply pinewood <pinewood@myworkday.com>", "Thanks for Applying to Pinewood", "Pinewood")]
    [InlineData("Corvane Systems Human Resources <corvane@myworkday.com>", "Thank you for applying! | R1000001 Software Developer Intern role", "Corvane Systems")]
    [InlineData("QVL People Operations <qvl@myworkday.com>", "Internship - Software Engineer at QVL: Thank you for applying", "QVL")]
    [InlineData("harbor@myworkday.com", "Brightline Compute: Thank you for applying to the Intern - Compiler with us!", "Brightline Compute")]
    [InlineData("brx@myworkday.com", "Thank you for applying to BRX!", "BRX")]
    public void Parse_CleansAtsSenderArtifacts_FromCompanyNames(
        string sender,
        string subject,
        string expectedCompany)
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = sender,
            Subject = subject,
            ParsedBody = subject
        };

        var parsed = parser.Parse(email);

        Assert.Equal(expectedCompany, parsed.Company);
    }

    [Fact]
    public void Parse_ExtractsRelativeTomorrowDeadline_FromReceivedDate()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "recruiting@veeva.com",
            Subject = "Please reply with your availability",
            ParsedBody = "Please reply with your availability by tomorrow morning.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 24, 18, 0, 0, TimeSpan.Zero)
        };

        var parsed = parser.Parse(email);

        Assert.Equal(new DateTimeOffset(2026, 4, 25, 0, 0, 0, TimeSpan.Zero), parsed.DueDateUtc);
    }

    [Fact]
    public void Parse_ExtractsWeekdayDeadline_FromReceivedDate()
    {
        var parser = new HeuristicRecruitingEmailParser();
        var email = new InboxEmail
        {
            Sender = "coordinator@figma.com",
            Subject = "Interview scheduling",
            ParsedBody = "Please share your availability by Friday afternoon.",
            ReceivedAtUtc = new DateTimeOffset(2026, 4, 21, 18, 0, 0, TimeSpan.Zero)
        };

        var parsed = parser.Parse(email);

        Assert.Equal(new DateTimeOffset(2026, 4, 24, 0, 0, 0, TimeSpan.Zero), parsed.DueDateUtc);
    }
}
