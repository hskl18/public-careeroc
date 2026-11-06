using Careeroc.Application;
using Careeroc.Domain.Enums;

namespace Careeroc.Tests;

public sealed class RequestEnumTests
{
    [Theory]
    [InlineData("Oa Pending")]
    [InlineData("oa_pending")]
    [InlineData("OA-PENDING")]
    public void AcceptsANameWrittenAnyWay(string value)
    {
        Assert.Equal(ApplicationStatus.OaPending, RequestEnum.Parse<ApplicationStatus>(value));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("42")]
    [InlineData("Applied,Rejected")]
    [InlineData("")]
    [InlineData(null)]
    public void RefusesNumbersCombinationsAndNothing(string? value)
    {
        Assert.Throws<UserFacingException>(() => RequestEnum.Parse<ApplicationStatus>(value));
    }
}
