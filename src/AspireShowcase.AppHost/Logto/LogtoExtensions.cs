static class LogtoExtensions
{
    // Identifier of the API resource the API's access tokens are issued for (their audience).
    const string ApiResource = "https://api.aspire-showcase";

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
    /// Lets bff sign users in with Logto, as the "Traditional web" application created for it in
    /// the Logto console, and get access tokens for the API resource.
    /// </summary>
    /// <remarks>
    /// The ID and secret only exist once that application has been created, so they're read
    /// from parameters that are empty until then; bff then runs without sign-in.
    /// </remarks>
    public static IResourceBuilder<ProjectResource> WithLogtoSignIn(
        this IResourceBuilder<ProjectResource> bff, IResourceBuilder<ContainerResource> logto)
    {
        var builder = bff.ApplicationBuilder;
        var appId = builder.AddOptionalParameter(
            "logto-app-id", "ID of the Logto application bff signs users in with.");
        var appSecret = builder.AddOptionalParameter(
            "logto-app-secret", "Secret of the Logto application bff signs users in with.", secret: true);

        return bff
            .WithEnvironment("Logto__Endpoint", builder.PublicEndpoint(logto))
            .WithEnvironment("Logto__AppId", appId)
            .WithEnvironment("Logto__AppSecret", appSecret)
            .WithEnvironment("Logto__ApiResource", ApiResource);
    }

    /// <summary>
    /// The API accepts access tokens issued for the API resource, and gives owners their role
    /// through Logto's Management API, as the machine-to-machine application created for it.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithLogtoApi(
        this IResourceBuilder<ProjectResource> web, IResourceBuilder<ContainerResource> logto)
    {
        var builder = web.ApplicationBuilder;
        var m2mAppId = builder.AddOptionalParameter(
            "logto-m2m-app-id", "ID of the Logto machine-to-machine application web calls the Management API as.");
        var m2mAppSecret = builder.AddOptionalParameter(
            "logto-m2m-app-secret", "Secret of the Logto machine-to-machine application.", secret: true);

        return web
            .WithEnvironment("Logto__Endpoint", builder.PublicEndpoint(logto))
            .WithEnvironment("Logto__ApiResource", ApiResource)
            .WithEnvironment("Logto__M2mAppId", m2mAppId)
            .WithEnvironment("Logto__M2mAppSecret", m2mAppSecret);
    }

    static IResourceBuilder<ContainerResource> AddLogtoContainer(
        this IDistributedApplicationBuilder builder, string name, int port, LogtoDatabase db) =>
        builder.AddContainer(name, "svhd/logto", "1.44")
            // Locally on a fixed port, the same as inside the container. Logto's URL is the issuer
            // of every token it hands out; with a new port on each run, tokens from the last run
            // would be refused (401 from web, a failed sign-out) until they expired.
            .WithHttpEndpoint(port: builder.ExecutionContext.IsRunMode ? port : null, targetPort: port, name: "http")
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
