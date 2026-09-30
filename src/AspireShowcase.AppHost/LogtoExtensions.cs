static class LogtoExtensions
{
    /// <summary>
    /// Logto serves sign-in on port 3001 and its admin console on port 3002. A Container App
    /// has a single HTTP ingress port, so each port gets its own container, both running the
    /// same image against the same database. The first one creates the database schema.
    /// </summary>
    public static IResourceBuilder<ContainerResource> AddLogto(
        this IDistributedApplicationBuilder builder, LogtoDatabase db)
    {
        var logto = builder.AddLogtoContainer("logto", 3001, db)
            .WithEntrypoint("sh")
            .WithArgs("-c", "npm run cli db seed -- --swe && npm start")
            // /api/status answers 204 No Content when Logto is up.
            .WithHttpHealthCheck("/api/status", statusCode: 204);

        var logtoAdmin = builder.AddLogtoContainer("logto-admin", 3002, db)
            .WaitFor(logto);

        // Logto runs with NODE_ENV=production, which refuses CORS from an admin console whose
        // hostname is "localhost", so the console can't call the Management API. Locally it's
        // served on 127.0.0.1 instead, which the check lets through.
        var adminEndpoint = builder.ExecutionContext.IsRunMode
            ? ReferenceExpression.Create($"http://127.0.0.1:{builder.PublicEndpoint(logtoAdmin).Property(EndpointProperty.Port)}")
            : ReferenceExpression.Create($"{builder.PublicEndpoint(logtoAdmin)}");
        logtoAdmin.WithUrl(ReferenceExpression.Create($"{adminEndpoint}/console"), "Admin console");

        // Both need both public URLs: ENDPOINT for sign-in, ADMIN_ENDPOINT for the console.
        foreach (var instance in new[] { logto, logtoAdmin })
        {
            instance
                .WithEnvironment("ENDPOINT", builder.PublicEndpoint(logto))
                .WithEnvironment("ADMIN_ENDPOINT", adminEndpoint);
        }

        return logto;
    }

    /// <summary>
    /// Lets the React app sign in with Logto: /api/config passes it Logto's public URL and the
    /// ID of the "Single page app" application created for it in the Logto console.
    /// </summary>
    /// <remarks>
    /// The ID only exists once that application has been created, so it's read from the
    /// logto-app-id parameter and is empty until then; the React app then hides sign-in.
    /// </remarks>
    public static IResourceBuilder<ProjectResource> WithLogto(
        this IResourceBuilder<ProjectResource> web, IResourceBuilder<ContainerResource> logto)
    {
        var builder = web.ApplicationBuilder;
        var appId = builder.AddParameter(
                "logto-app-id",
                () => builder.Configuration["Parameters:logto-app-id"] ?? "")
            .WithDescription("ID of the Logto application the React app signs in with.");

        return web
            .WithEnvironment("Logto__Endpoint", builder.PublicEndpoint(logto))
            .WithEnvironment("Logto__AppId", appId);
    }

    static IResourceBuilder<ContainerResource> AddLogtoContainer(
        this IDistributedApplicationBuilder builder, string name, int port, LogtoDatabase db) =>
        builder.AddContainer(name, "svhd/logto", "1.44")
            .WithHttpEndpoint(targetPort: port, name: "http")
            .WithExternalHttpEndpoints()
            // TLS ends at the Container Apps ingress; trust its X-Forwarded-* headers.
            .WithEnvironment("TRUST_PROXY_HEADER", "1")
            .WithLogtoDatabase(db);

    // Browsers open these, so locally they must be localhost addresses; a container would
    // otherwise get addresses on the container network (http://logto.dev.internal:3001).
    static EndpointReference PublicEndpoint(
        this IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource> resource) =>
        builder.ExecutionContext.IsRunMode
            ? resource.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork)
            : resource.GetEndpoint("http");
}
