using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace AspireShowcase.Bff.IntegrationTests;

/// <summary>
/// What bff sends web for a session, and what it does with one that has ended: signs the browser
/// out and answers 401 with X-Session-Ended, which sends the React app to sign in again.
/// </summary>
public sealed class SessionTests(BffFactory bff) : IClassFixture<BffFactory>
{
    [Fact]
    public async Task A_signed_in_request_is_passed_to_web_with_the_access_token()
    {
        var (cookie, _) = await bff.SignInAsync("token-1", DateTimeOffset.UtcNow.AddHours(1));
        var path = NewApiPath();

        var response = await SendAsync(path, cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Bearer token-1", bff.Web.AuthorizationOf(path));
    }

    [Fact]
    public async Task A_session_whose_token_cannot_be_refreshed_is_ended()
    {
        var (cookie, sessionId) = await bff.SignInAsync("expired", DateTimeOffset.UtcNow.AddMinutes(-5));
        var path = NewApiPath();

        var response = await SendAsync(path, cookie);

        AssertSessionEnded(response);
        Assert.False(bff.Web.Received(path));
        Assert.Null(await bff.Services.GetRequiredService<ITicketStore>().RetrieveAsync(sessionId));
    }

    [Fact]
    public async Task A_session_cookie_bff_cannot_read_is_ended()
    {
        // As after bff-db was reset, or its data protection keys lost.
        var path = NewApiPath();

        var response = await SendAsync(path, "bff-session=not-a-session");

        AssertSessionEnded(response);
        Assert.False(bff.Web.Received(path));
    }

    [Fact]
    public async Task A_request_without_a_session_is_passed_to_web_without_a_token()
    {
        var path = NewApiPath();

        var response = await SendAsync(path, cookie: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(bff.Web.AuthorizationOf(path));
    }

    Task<HttpResponseMessage> SendAsync(string path, string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-CSRF", "1");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }
        return bff.CreateClientWithoutCookies().SendAsync(request);
    }

    static void AssertSessionEnded(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Session-Ended")));
        // The browser is told to delete the cookie.
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith("bff-session=;"));
    }

    static string NewApiPath() => $"/api/session-test/{Guid.NewGuid():N}";
}
