using Careeroc.Application;

namespace Careeroc.Tests;

public sealed class RecruitingEmailRelevanceTests
{
    [Fact]
    public void IsLikelyRecruiting_KeepsSuspiciousJobOffersSoTheScamCheckerSeesThem()
    {
        Assert.True(
            RecruitingEmailRelevance.IsLikelyRecruiting(
                "Hiring Desk <hiring.desk.offers@freemail.example>",
                "Job offer: Remote Data Entry Specialist, $55/hr, start today",
                "You have been selected for a remote Data Entry Specialist position. Reply with your bank account details to start."));
    }

    [Fact]
    public void IsLikelyRecruiting_KeepsAgencyOutreachForAConfidentialClient()
    {
        Assert.True(
            RecruitingEmailRelevance.IsLikelyRecruiting(
                "Riley Chen <riley@brightpathtalent.example>",
                "Backend Engineer opportunity with a confidential client",
                "I'm partnering with a confidential client on a Backend Engineer search. Would you be open to a quick call?"));
    }

    [Theory]
    [InlineData("Parcel Post <orders@parcelpost.example>", "Your order has shipped", "Your order is on the way.")]
    [InlineData("Systems Weekly <newsletter@systemsweekly.example>", "Systems Weekly: consensus protocols", "This week: Raft log compaction. Unsubscribe any time.")]
    public void IsLikelyRecruiting_StillFiltersEverydayNoise(string sender, string subject, string body)
    {
        Assert.False(RecruitingEmailRelevance.IsLikelyRecruiting(sender, subject, body));
    }
}
