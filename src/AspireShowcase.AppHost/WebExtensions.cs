using Aspire.Hosting.JavaScript;

static class WebExtensions
{
    /// <summary>
    /// The ASP.NET Core project serves both /api and, once published, the React UI,
    /// so it is deployed as the "web" Container App.
    /// </summary>
    public static IResourceBuilder<ProjectResource> AddWeb(this IDistributedApplicationBuilder builder) =>
        builder.AddProject<Projects.AspireShowcase_Api>("web")
            .WithHttpHealthCheck("/health")
            .WithExternalHttpEndpoints()
            // References only pass connection strings: the separate APP_DB_PASSWORD, APP_DB_URI...
            // variables would put the database password in the Container App itself, bypassing
            // Key Vault. This has to come before the WithReference calls to apply to them.
            .WithReferenceEnvironment(
                ReferenceEnvironmentInjectionFlags.All & ~ReferenceEnvironmentInjectionFlags.ConnectionProperties)
            // Link on the dashboard; Swagger UI is only mapped in Development.
            .WithUrl("/swagger", "Swagger");

    /// <summary>
    /// Vite dev server with hot reload, used for local development only. On publish, the built
    /// React app is copied into the web container's wwwroot, so a single Container App serves
    /// both the UI and /api.
    /// </summary>
    public static IResourceBuilder<ViteAppResource> AddFrontend(
        this IDistributedApplicationBuilder builder, IResourceBuilder<ProjectResource> web)
    {
        var frontend = builder.AddViteApp("frontend", "../AspireShowcase.Web")
            // A fixed port, so the sign-in redirect URI registered in Logto
            // (http://localhost:5173/callback) stays the same between runs.
            .WithEndpoint("http", endpoint => endpoint.Port = 5173)
            .WithReference(web)
            .WaitFor(web);

        web.PublishWithContainerFiles(frontend, "wwwroot");
        return frontend;
    }
}
