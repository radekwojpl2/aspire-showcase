using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

static class ApiProxy
{
    /// <summary>The header every /api request has to carry; see <see cref="UseCsrfHeaderCheck"/>.</summary>
    public const string CsrfHeader = "X-CSRF";

    /// <summary>
    /// On a 401 from bff itself, as opposed to one from web: the session has ended and the user
    /// has to sign in again. See <see cref="UseAccessTokens"/>.
    /// </summary>
    public const string SessionEndedHeader = "X-Session-Ended";

    static readonly object AccessTokenKey = new();

    /// <summary>
    /// Forwards /api/* to web with YARP, swapping the session cookie for the user's access
    /// token. "web" is its resource name in the AppHost, resolved by service discovery.
    /// </summary>
    public static void AddApiProxy(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<AccessTokens>();
        builder.Services.AddSingleton<SessionTelemetry>();

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
                transforms.AddRequestTransform(context =>
                {
                    if (context.HttpContext.Items[AccessTokenKey] is string token)
                    {
                        context.ProxyRequest.Headers.Authorization = new("Bearer", token);
                    }
                    return ValueTask.CompletedTask;
                });
            })
            .AddServiceDiscoveryDestinationResolver();
    }

    /// <summary>
    /// Refuses /api requests without the X-CSRF header. Browsers send the session cookie with
    /// any request to this site; a custom header can only be set by the app's own scripts
    /// (another site would need a CORS preflight, which bff never allows).
    /// </summary>
    /// <remarks>
    /// Except for reading a business's logo (V1-6): the booking page shows it with an img tag,
    /// which can't add a header, and reading a public image changes nothing for anyone.
    /// </remarks>
    public static void UseCsrfHeaderCheck(this WebApplication app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") && context.Request.Headers[CsrfHeader] != "1" &&
                !IsLogo(context.Request))
            {
                await Results.Problem(
                    title: $"Requests to /api need the {CsrfHeader}: 1 header.",
                    statusCode: StatusCodes.Status400BadRequest).ExecuteAsync(context);
                return;
            }

            await next(context);
        });

    // GET /api/public/businesses/{slug}/logo, and HEAD.
    static bool IsLogo(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) &&
        request.Path.Value?.Split('/') is ["", "api", "public", "businesses", { Length: > 0 }, "logo"];

    /// <summary>
    /// Gets the user's access token for /api requests, which the proxy then sends to web. A
    /// request with a session cookie but no token belongs to a session that has ended: its
    /// refresh token was rejected, or it's gone from bff-db. Passed on without a token, it would
    /// get a 401 from web while /bff/user still said the user is signed in. So bff signs the
    /// browser out and answers 401 itself, which tells the app to sign in again.
    /// </summary>
    public static void UseAccessTokens(this WebApplication app) =>
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                await next(context);
                return;
            }

            var token = await context.RequestServices.GetRequiredService<AccessTokens>().GetAsync(context);
            if (token is null && context.Request.Cookies.ContainsKey(LogtoAuthentication.SessionCookie))
            {
                // UseAuthentication read the session already: if it found one, its token is what failed.
                context.RequestServices.GetRequiredService<SessionTelemetry>().Ended(
                    context.User.Identity?.IsAuthenticated == true ? "refresh_failed" : "session_not_found");
                // Deletes the cookie, and the session if it's still in bff-db.
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                context.Response.Headers[SessionEndedHeader] = "1";
                await Results.Problem(
                    title: "Your session has ended. Sign in again.",
                    statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(context);
                return;
            }

            context.Items[AccessTokenKey] = token;
            await next(context);
        });
}
