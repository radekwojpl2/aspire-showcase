using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace AspireShowcase.Bff.IntegrationTests;

/// <summary>
/// What bff's traces show: spans named after the request rather than the proxy's route, and no
/// database spans of their own for queries outside a request, such as the migrations at startup.
/// </summary>
public sealed class TracingTests(BffFactory bff) : IClassFixture<BffFactory>
{
    [Theory]
    [InlineData("PUT", "/api/businesses/mine/staff/0199b1c4-7a3e-7f00-8a1b-2c3d4e5f6a7b", "/api/businesses/mine/staff/{id}")]
    [InlineData("POST", "/api/me/bookings/0199b1c4-7a3e-7f00-8a1b-2c3d4e5f6a7b/cancel", "/api/me/bookings/{id}/cancel")]
    [InlineData("GET", "/api/public/businesses/anna-hair/slots?date=2026-10-08", "/api/public/businesses/{slug}/slots")]
    [InlineData("GET", "/api/businesses/mine/bookings", "/api/businesses/mine/bookings")]
    public async Task A_proxied_request_is_named_after_its_path_with_ids_masked(string method, string url, string route)
    {
        var response = await SendAsync(new HttpMethod(method), url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var span = await bff.Spans.WaitForAsync(span =>
            span.Kind == ActivityKind.Server && span.GetTagItem("url.path") as string == url.Split('?')[0]);
        Assert.Equal($"{method} {route}", span.DisplayName);
        Assert.Equal(route, span.GetTagItem("http.route"));
    }

    [Fact]
    public async Task Queries_for_a_request_are_traced_under_it()
    {
        var (cookie, _) = await bff.SignInAsync("token", DateTimeOffset.UtcNow.AddHours(1));
        var path = $"/api/tracing-test/{Guid.NewGuid():N}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);

        await SendAsync(request);

        // Reading the session from bff-db.
        var requestSpan = await bff.Spans.WaitForAsync(span =>
            span.Kind == ActivityKind.Server && span.GetTagItem("url.path") as string == path);
        Assert.Contains(bff.Spans, span => span.Source.Name == "Npgsql" && span.TraceId == requestSpan.TraceId);
    }

    [Fact]
    public async Task Queries_outside_a_request_are_not_traced()
    {
        Assert.Null(Activity.Current);
        await bff.Services.GetRequiredService<ITicketStore>().RetrieveAsync("no-such-session");

        // Neither this query nor the migrations bff ran when it started.
        Assert.DoesNotContain(bff.Spans, span => span.Source.Name == "Npgsql" && string.IsNullOrEmpty(span.ParentId));
    }

    Task<HttpResponseMessage> SendAsync(HttpMethod method, string url) => SendAsync(new HttpRequestMessage(method, url));

    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        request.Headers.Add("X-CSRF", "1");
        return bff.CreateClientWithoutCookies().SendAsync(request);
    }
}
