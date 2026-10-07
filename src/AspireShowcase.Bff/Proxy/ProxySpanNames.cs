using System.Diagnostics;
using OpenTelemetry.Instrumentation.AspNetCore;

/// <summary>
/// Names the spans of proxied requests after their path. Every /api request matches the one
/// proxy route, so its span would be called "PUT /api/{**rest}" whatever it was for.
/// </summary>
static class ProxySpanNames
{
    const string ProxyRoute = "/api/{**rest}";

    /// <remarks>
    /// The instrumentation names the span after the route when the request ends, then calls this,
    /// so the name set here stays. Application Insights names requests after the http.route tag,
    /// so that changes too. The http.server.request.duration metric reads the route from the
    /// endpoint instead, and keeps grouping by /api/{**rest}.
    /// </remarks>
    public static void AddProxySpanNames(this WebApplicationBuilder builder) =>
        builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
            options.EnrichWithHttpResponse = (activity, response) =>
            {
                // web's 404s are left alone: any path can get one, and each would be a new name.
                if (activity.GetTagItem("http.route") as string != ProxyRoute ||
                    response.StatusCode == StatusCodes.Status404NotFound)
                {
                    return;
                }

                var route = Template(response.HttpContext.Request.Path);
                activity.DisplayName = $"{response.HttpContext.Request.Method} {route}";
                activity.SetTag("http.route", route);
            });

    /// <summary>
    /// The path with its variable parts masked, so each kind of request gets one name: IDs (all
    /// GUIDs) become {id}, and a business's booking link, which is free text, {slug}.
    /// </summary>
    static string Template(PathString path)
    {
        var segments = path.Value!.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            if (Guid.TryParse(segments[i], out _))
            {
                segments[i] = "{id}";
            }
            else if (i >= 2 && segments[i - 2] == "public" && segments[i - 1] == "businesses")
            {
                segments[i] = "{slug}";
            }
        }
        return string.Join('/', segments);
    }
}
