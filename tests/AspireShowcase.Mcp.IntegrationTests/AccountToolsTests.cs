using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace AspireShowcase.Mcp.IntegrationTests;

/// <summary>Who the user is signed in as, which web looks up from the user ID in their token.</summary>
public sealed class AccountToolsTests(McpFactory mcp) : IClassFixture<McpFactory>
{
    [Fact]
    public async Task The_user_is_told_who_they_are_signed_in_as_and_which_business_they_own()
    {
        mcp.Web.Answer(HttpMethod.Get, "/api/me", new
        {
            name = "Radek", email = "radek@example.com", business = new { name = "Anna Hair", slug = "anna-hair" },
        });
        await using var client = await mcp.ConnectAsync();

        var result = await client.CallToolAsync("whoami");

        Assert.NotEqual(true, result.IsError);
        var me = JsonDocument.Parse(Text(result)).RootElement;
        Assert.Equal("radek@example.com", me.GetProperty("email").GetString());
        Assert.Equal("anna-hair", me.GetProperty("business").GetProperty("slug").GetString());
    }

    static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
