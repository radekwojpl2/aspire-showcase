/// <summary>
/// The Content-Security-Policy on everything bff serves, the React app's pages included. With
/// the tokens kept out of the browser, injected script is the main threat left: it couldn't
/// steal a token, but it could call /api as the user, X-CSRF header and all. The app's scripts
/// and styles are all files Vite built, so nothing inline or from other sites is allowed.
/// </summary>
/// <remarks>
/// Locally the pages come from the Vite dev server, which needs inline scripts for hot reload,
/// so the policy only applies to the app as it's published.
/// </remarks>
static class ContentSecurityPolicy
{
    public static void UseContentSecurityPolicy(this WebApplication app)
    {
        var logto = app.Services.GetRequiredService<LogtoSettings>();
        var policy = string.Join("; ",
            "default-src 'self'",
            $"connect-src {string.Join(' ', ["'self'", .. ApplicationInsightsOrigins(app.Configuration)])}",
            "object-src 'none'",
            "base-uri 'self'",
            // No other site may frame the app (clickjacking).
            "frame-ancestors 'none'",
            // The sign-out form posts to bff, which redirects to Logto's sign-out page; browsers
            // apply form-action to that redirect too.
            $"form-action {string.Join(' ', ["'self'", .. Origin(logto.IsConfigured ? logto.Endpoint : null)])}");

        // Set when the response starts, so error responses that cleared the headers get it too.
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.ContentSecurityPolicy = policy;
                return Task.CompletedTask;
            });
            return next(context);
        });
    }

    /// <summary>
    /// In Azure the React app sends telemetry to Application Insights (see telemetry.ts): to the
    /// ingestion endpoint in the connection string, which bff gets too, and its SDK fetches its
    /// settings from Microsoft's CDN.
    /// </summary>
    static IEnumerable<string> ApplicationInsightsOrigins(IConfiguration configuration)
    {
        if (configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] is not { Length: > 0 } connectionString)
        {
            return [];
        }

        var ingestion = connectionString.Split(';')
            .Select(part => part.Split('=', 2))
            .FirstOrDefault(pair => pair.Length == 2 && pair[0].Trim().Equals("IngestionEndpoint", StringComparison.OrdinalIgnoreCase))?[1]
            // What the SDK uses when the connection string doesn't name one.
            ?? "https://dc.services.visualstudio.com";
        return [.. Origin(ingestion), "https://js.monitor.azure.com"];
    }

    static IEnumerable<string> Origin(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? [uri.GetLeftPart(UriPartial.Authority)] : [];
}
