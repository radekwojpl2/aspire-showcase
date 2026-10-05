using Aspire.Hosting.JavaScript;

static class WebExtensions
{
    /// <summary>
    /// The API. Only bff calls it, so it has no external endpoint: in Azure it's reachable only
    /// inside the Container Apps environment.
    /// </summary>
    public static IResourceBuilder<ProjectResource> AddWeb(this IDistributedApplicationBuilder builder) =>
        builder.AddProject<Projects.AspireShowcase_Api>("web")
            .WithHttpHealthCheck("/health")
            .WithoutConnectionProperties()
            // Link on the dashboard; Swagger UI is only mapped in Development.
            .WithUrl("/swagger", "Swagger");

    /// <summary>
    /// The backend for frontend: the only thing the browser talks to. It signs users in, keeps
    /// their tokens, and forwards /api to web with the user's access token. Once published it
    /// also serves the React app, so a single public Container App serves the UI and /api.
    /// </summary>
    /// <remarks>
    /// bff builds Logto's redirect URIs from the request. TLS ends at the Container Apps
    /// ingress, so it relies on X-Forwarded-Proto, which Aspire has ASP.NET Core trust there
    /// (ASPNETCORE_FORWARDEDHEADERS_ENABLED).
    /// </remarks>
    public static IResourceBuilder<ProjectResource> AddBff(
        this IDistributedApplicationBuilder builder, IResourceBuilder<ProjectResource> web) =>
        builder.AddProject<Projects.AspireShowcase_Bff>("bff")
            .WithHttpHealthCheck("/health")
            .WithExternalHttpEndpoints()
            .WithoutConnectionProperties()
            .WithReference(web)
            .WaitFor(web);

    /// <summary>
    /// Vite dev server with hot reload, used for local development only. It proxies /api, /bff
    /// and Logto's callbacks to bff (see vite.config.ts). On publish, the built React app is
    /// copied into bff's wwwroot instead.
    /// </summary>
    public static IResourceBuilder<ViteAppResource> AddFrontend(
        this IDistributedApplicationBuilder builder, IResourceBuilder<ProjectResource> bff)
    {
        var frontend = builder.AddViteApp("frontend", "../AspireShowcase.Web")
            // A fixed port, so the redirect URIs registered in Logto
            // (http://localhost:5173/signin-oidc) stay the same between runs.
            .WithEndpoint("http", endpoint => endpoint.Port = 5173)
            .WithReference(bff)
            .WaitFor(bff);

        bff.PublishWithContainerFiles(frontend, "wwwroot");
        return frontend;
    }

    // References only pass connection strings: the separate APP_DB_PASSWORD, APP_DB_URI...
    // variables would put the database password in the Container App itself, bypassing
    // Key Vault. This has to come before the WithReference calls to apply to them.
    static IResourceBuilder<ProjectResource> WithoutConnectionProperties(this IResourceBuilder<ProjectResource> project) =>
        project.WithReferenceEnvironment(
            ReferenceEnvironmentInjectionFlags.All & ~ReferenceEnvironmentInjectionFlags.ConnectionProperties);
}
