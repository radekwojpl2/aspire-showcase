using Microsoft.AspNetCore.Hosting;

namespace AspireShowcase.Bff.IntegrationTests;

/// <summary>The Content-Security-Policy on what bff serves, error responses included.</summary>
public sealed class ContentSecurityPolicyTests(BffFactory bff) : IClassFixture<BffFactory>
{
    [Theory]
    [InlineData("/bff/user")]
    [InlineData("/bff/no-such-endpoint")]
    public async Task Responses_allow_only_this_site(string path)
    {
        var response = await bff.CreateClient().GetAsync(path);

        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
    }

    [Fact]
    public async Task With_Application_Insights_the_browser_may_send_it_telemetry()
    {
        using var withInsights = bff.WithWebHostBuilder(builder => builder.UseSetting(
            "APPLICATIONINSIGHTS_CONNECTION_STRING",
            "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://westeurope-5.in.applicationinsights.azure.com/"));

        var response = await withInsights.CreateClient().GetAsync("/bff/user");

        Assert.Contains(
            "connect-src 'self' https://westeurope-5.in.applicationinsights.azure.com https://js.monitor.azure.com",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }
}
