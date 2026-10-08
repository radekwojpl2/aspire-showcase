using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace AspireShowcase.Bff.IntegrationTests;

/// <summary>
/// The X-CSRF header check on /api: the session cookie goes with any request a browser makes to
/// bff, but only the app's own scripts can add the header.
/// </summary>
public sealed class CsrfTests(BffFactory bff) : IClassFixture<BffFactory>
{
    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public async Task An_api_request_without_the_header_is_refused_before_reaching_web(string? header)
    {
        var path = NewApiPath();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (header is not null)
        {
            request.Headers.Add("X-CSRF", header);
        }

        var response = await bff.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Requests to /api need the X-CSRF: 1 header.",
            (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
        Assert.False(bff.Web.Received(path));
    }

    [Fact]
    public async Task An_api_request_with_the_header_is_passed_to_web()
    {
        var path = NewApiPath();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF", "1");

        var response = await bff.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(bff.Web.Received(path));
    }

    [Fact]
    public async Task The_logo_of_a_business_can_be_read_without_the_header_for_an_img_tag()
    {
        var path = $"/api/public/businesses/test-{Guid.NewGuid():N}/logo";

        var response = await bff.CreateClient().GetAsync($"{path}?v=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(bff.Web.Received(path));
    }

    [Fact]
    public async Task Changing_the_logo_still_needs_the_header()
    {
        var response = await bff.CreateClient().PutAsync("/api/businesses/mine/logo", new ByteArrayContent([1]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Requests_outside_api_do_not_need_the_header()
    {
        var response = await bff.CreateClient().GetAsync("/bff/user");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Each test its own path, so they can tell their requests apart in the shared stub.
    static string NewApiPath() => $"/api/csrf-test/{Guid.NewGuid():N}";
}
