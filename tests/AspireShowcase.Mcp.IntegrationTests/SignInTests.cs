using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace AspireShowcase.Mcp.IntegrationTests;

/// <summary>
/// Signing in to the MCP server as the MCP authorization spec has it, with Logto as the
/// authorization server: the only way in, and only as the app registered for MCP clients. The
/// server acts for the user on web with a token Logto exchanged for theirs.
/// </summary>
public sealed class SignInTests(McpFactory mcp) : IClassFixture<McpFactory>
{
    [Fact]
    public async Task A_client_without_a_token_is_told_where_to_sign_in()
    {
        var response = await mcp.CreateClient().PostAsync("/mcp", InitializeRequest());

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
        Assert.Equal(McpFactory.Issuer, metadata.GetProperty("authorization_servers")[0].GetString());
    }

    [Fact]
    public async Task A_client_in_a_browser_can_read_the_resource_metadata_from_its_own_origin()
    {
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/.well-known/oauth-protected-resource/mcp");
        preflight.Headers.Add("Origin", "http://localhost:6274");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "mcp-protocol-version");
        var read = new HttpRequestMessage(HttpMethod.Get, "/.well-known/oauth-protected-resource/mcp");
        read.Headers.Add("Origin", "http://localhost:6274");

        var preflightResponse = await mcp.CreateClient().SendAsync(preflight);
        var readResponse = await mcp.CreateClient().SendAsync(read);

        Assert.Equal(HttpStatusCode.NoContent, preflightResponse.StatusCode);
        Assert.Equal("*", Assert.Single(readResponse.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task The_mcp_endpoint_itself_is_not_open_to_other_origins()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = InitializeRequest() };
        request.Headers.Add("Origin", "http://localhost:6274");

        var response = await mcp.CreateClient().SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task A_token_issued_for_another_resource_is_refused()
    {
        var response = await SendAsync(McpFactory.AccessToken(audience: "https://api.aspire-showcase"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("bff-app")]
    [InlineData("https://some-mcp-client.example/client.json")] // A client of Logto's dynamic (CIMD) app.
    public async Task A_token_issued_to_another_application_is_refused(string clientId)
    {
        var response = await SendAsync(McpFactory.AccessToken(clientId: clientId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_of_the_app_of_the_mcp_clients_gets_in()
    {
        var response = await SendAsync(McpFactory.AccessToken());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void The_server_does_not_start_without_its_Logto_applications()
    {
        using var unconfigured = mcp.WithWebHostBuilder(builder => builder.UseSetting("Logto:ClientId", ""));

        var exception = Assert.Throws<InvalidOperationException>(() => unconfigured.CreateClient());

        Assert.Contains("logto-mcp-client-id", exception.Message);
    }

    [Fact]
    public async Task Web_gets_a_token_exchanged_for_the_one_of_the_user_not_the_token_itself()
    {
        mcp.Web.Answer(HttpMethod.Get, "/api/public/businesses/token-test", new
        {
            name = "Token test", slug = "token-test", timeZone = "UTC", cancellationNoticeHours = 0,
            services = Array.Empty<object>(),
        });
        var userToken = McpFactory.AccessToken(userId: "token-test-user");
        await using var client = await mcp.ConnectAsync(accessToken: userToken);

        await client.CallToolAsync("get_business", new Dictionary<string, object?> { ["slug"] = "token-test" });

        var (_, _, authorization) = Assert.Single(mcp.Web.Received, request => request.Request.EndsWith("/token-test"));
        Assert.Equal($"Bearer {McpFactory.ExchangedToken}", authorization);
        var exchange = Assert.Single(mcp.Logto.Exchanges, form => form["subject_token"] == userToken);
        Assert.Equal("urn:ietf:params:oauth:grant-type:token-exchange", exchange["grant_type"]);
        Assert.Equal("https://api.aspire-showcase", exchange["resource"]);
    }

    Task<HttpResponseMessage> SendAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = InitializeRequest() };
        request.Headers.Authorization = new("Bearer", accessToken);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return mcp.CreateClient().SendAsync(request);
    }

    static StringContent InitializeRequest() => new(
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""",
        System.Text.Encoding.UTF8, "application/json");
}
