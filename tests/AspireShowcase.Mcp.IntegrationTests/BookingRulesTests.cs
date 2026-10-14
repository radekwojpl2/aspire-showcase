using ModelContextProtocol.Protocol;

namespace AspireShowcase.Mcp.IntegrationTests;

/// <summary>
/// How booking works, as a resource: a document the user attaches for the model, rather than
/// data a tool looks up.
/// </summary>
public sealed class BookingRulesTests(McpFactory mcp) : IClassFixture<McpFactory>
{
    const string Uri = "guide://booking-rules";

    [Fact]
    public async Task The_booking_rules_are_listed_among_the_resources()
    {
        await using var client = await mcp.ConnectAsync();

        var resource = Assert.Single(await client.ListResourcesAsync(), resource => resource.Uri == Uri);

        Assert.Equal("How booking works", resource.Title);
        Assert.Equal("text/markdown", resource.MimeType);
    }

    [Fact]
    public async Task The_booking_rules_explain_the_cancellation_notice()
    {
        await using var client = await mcp.ConnectAsync();

        var resource = await client.ReadResourceAsync(Uri);

        var document = Assert.IsType<TextResourceContents>(Assert.Single(resource.Contents));
        Assert.Equal("text/markdown", document.MimeType);
        Assert.StartsWith("# How booking works", document.Text);
        Assert.Contains("cancellation notice", document.Text);
    }
}
