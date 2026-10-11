using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AspireShowcase.Mcp.IntegrationTests;

/// <summary>
/// Signing in to the MCP server as the MCP authorization spec has it, with Logto as the
/// authorization server, and acting for the user on web with a token Logto exchanged for theirs.
/// </summary>
public sealed class SignInTests(SignInMcpFactory mcp) : IClassFixture<SignInMcpFactory>
{
    [Fact]
    public async Task A_client_without_a_token_is_told_where_to_sign_in()
    {
        var response = await mcp.CreateClient().PostAsync("/mcp", JsonContent());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Contains("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\"", challenge.Parameter);
    }

    [Fact]
    public async Task The_resource_metadata_names_Logto_as_the_authorization_server()
    {
        var metadata = await mcp.CreateClient().GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource/mcp");

        Assert.Equal("http://localhost/mcp", metadata.GetProperty("resource").GetString());
        Assert.Equal(SignInMcpFactory.Issuer, metadata.GetProperty("authorization_servers")[0].GetString());
    }

    [Fact]
    public async Task A_token_issued_for_another_resource_is_refused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent() };
        request.Headers.Authorization = new("Bearer", SignInMcpFactory.AccessToken(audience: "https://api.aspire-showcase"));

        var response = await mcp.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_public_tools_need_no_sign_in_at_their_own_endpoint()
    {
        await using var client = await mcp.ConnectAsync(path: "/mcp/public");

        var tools = await client.ListToolsAsync();

        Assert.Contains(tools, tool => tool.Name == "get_business");
    }

    [Fact]
    public async Task Web_gets_a_token_exchanged_for_the_one_of_the_user_not_the_token_itself()
    {
        mcp.Web.Answer(HttpMethod.Get, "/api/public/businesses/token-test", new
        {
            name = "Token test", slug = "token-test", timeZone = "UTC", cancellationNoticeHours = 0,
            services = Array.Empty<object>(),
        });
        var userToken = SignInMcpFactory.AccessToken();
        await using var client = await mcp.ConnectAsync(accessToken: userToken);

        await client.CallToolAsync("get_business", new Dictionary<string, object?> { ["slug"] = "token-test" });

        var (_, _, authorization) = Assert.Single(mcp.Web.Received, request => request.Request.EndsWith("/token-test"));
        Assert.Equal($"Bearer {SignInMcpFactory.ExchangedToken}", authorization);
        var exchange = Assert.Single(mcp.Logto.Exchanges, form => form["subject_token"] == userToken);
        Assert.Equal("urn:ietf:params:oauth:grant-type:token-exchange", exchange["grant_type"]);
        Assert.Equal("https://api.aspire-showcase", exchange["resource"]);
    }

    // An MCP initialize request; its content doesn't matter for a refusal.
    static StringContent JsonContent() => new(
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""",
        System.Text.Encoding.UTF8, "application/json");
}
