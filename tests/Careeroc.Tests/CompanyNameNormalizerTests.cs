using Careeroc.Application;

namespace Careeroc.Tests;

public sealed class CompanyNameNormalizerTests
{
    [Theory]
    [InlineData("Https")]
    [InlineData("http")]
    [InlineData("https://docs.google.com/forms/d/e/example/viewform")]
    [InlineData("docs.google.com")]
    [InlineData("www.greenhouse.io")]
    public void TryNormalize_RejectsUrlLikeCompanyNames(string rawCompany)
    {
        Assert.False(CompanyNameNormalizer.TryNormalize(rawCompany, out _));
        Assert.False(CompanyNameNormalizer.IsUsableCompanyName(rawCompany));
    }

    [Theory]
    [InlineData("Okafor, Dana")]
    [InlineData("O'Neil, Dana")]
    public void TryNormalize_RejectsCommaSeparatedPersonNames(string rawCompany)
    {
        Assert.False(CompanyNameNormalizer.TryNormalize(rawCompany, out _));
        Assert.False(CompanyNameNormalizer.IsUsableCompanyName(rawCompany));
        Assert.True(CompanyNameNormalizer.IsLikelyPersonName(rawCompany));
    }

    [Theory]
    [InlineData("GEHA", "GEHA")]
    [InlineData("Carrier Global Corporation", "Carrier Global Corporation")]
    [InlineData("Bosch", "Bosch")]
    [InlineData("C3.ai", "C3.ai")]
    [InlineData("Kestrel AI Scheduling", "Kestrel AI")]
    [InlineData("Harborline Talent", "Harborline")]
    [InlineData("harborline", "Harborline")]
    [InlineData("NORTHWIND LABS", "Northwind Labs")]
    public void TryNormalize_KeepsRealCompanyNames(string rawCompany, string expectedCompany)
    {
        Assert.True(CompanyNameNormalizer.TryNormalize(rawCompany, out var company));
        Assert.Equal(expectedCompany, company);
        Assert.True(CompanyNameNormalizer.IsUsableCompanyName(rawCompany));
    }
}
