using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

static class ApiProxy
{
    /// <summary>The header every /api request has to carry; see <see cref="UseCsrfHeaderCheck"/>.</summary>
    public const string CsrfHeader = "X-CSRF";

    /// <summary>
    /// Forwards /api/* to web with YARP, swapping the session cookie for the user's access
    /// token. "web" is its resource name in the AppHost, resolved by service discovery.
    /// </summary>
    public static void AddApiProxy(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<AccessTokens>();

        builder.Services.AddReverseProxy()
            .LoadFromMemory(
                [new RouteConfig { RouteId = "api", ClusterId = "web", Match = new RouteMatch { Path = "/api/{**rest}" } }],
                [new ClusterConfig
                {
                    ClusterId = "web",
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        ["web"] = new() { Address = "https+http://web" },
                    },
                }])
            .AddTransforms(transforms =>
            {
                // web only accepts access tokens; the session cookie is bff's business.
                transforms.AddRequestHeaderRemove("Cookie");
                transforms.AddRequestTransform(async context =>
                {
                    var tokens = context.HttpContext.RequestServices.GetRequiredService<AccessTokens>();
                    if (await tokens.GetAsync(context.HttpContext) is { } token)
                    {
                        context.ProxyRequest.Headers.Authorization = new("Bearer", token);
                    }
                });
            })
            .AddServiceDiscoveryDestinationResolver();
    }

    /// <summary>
    /// Refuses /api requests without the X-CSRF header. Browsers send the session cookie with
    /// any request to this site; a custom header can only be set by the app's own scripts
    /// (another site would need a CORS preflight, which bff never allows).
    /// </summary>
    public static void UseCsrfHeaderCheck(this WebApplication app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && context.Request.Headers[CsrfHeader] != "1")
            {
                await Results.Problem(
                    title: $"Requests to /api need the {CsrfHeader}: 1 header.",
                    statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
                return;
            }

            await next(context);
        });
}
