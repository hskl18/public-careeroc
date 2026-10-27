using Careeroc.Api.Security;

namespace Careeroc.Tests;

public sealed class ProductionUrlPolicyTests
{
    [Theory]
    [InlineData("https://api.careeroc.com", false, true)]
    [InlineData("http://localhost:5114", false, true)]
    [InlineData("http://api.careeroc.com", false, false)]
    [InlineData("http://api:8080", false, false)]
    [InlineData("http://api:8080", true, true)]
    [InlineData("http://careeroc-api.railway.internal:8080", true, true)]
    [InlineData("http://api.careeroc.svc.cluster.local", true, true)]
    [InlineData("http://127.0.0.1:8080", true, true)]
    [InlineData("http://10.0.0.5:8080", true, false)]
    [InlineData("http://api.careeroc.com", true, false)]
    public void IsAllowed_SeparatesPublicAndPrivateNetworkUrls(string value, bool isInternal, bool expected)
    {
        Assert.Equal(expected, ProductionUrlPolicy.IsAllowed(new Uri(value), isInternal));
    }
}
