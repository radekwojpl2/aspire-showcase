using System.Diagnostics;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace AspireShowcase.Bff.IntegrationTests;

/// <summary>
/// What bff's traces show: no database spans of their own for queries outside a request, such as
/// the migrations at startup.
/// </summary>
public sealed class TracingTests(BffFactory bff) : IClassFixture<BffFactory>
{
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

    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        request.Headers.Add("X-CSRF", "1");
        return bff.CreateClientWithoutCookies().SendAsync(request);
    }
}
