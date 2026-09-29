var builder = DistributedApplication.CreateBuilder(args);

// Deployment target for `aspire deploy`: an Azure Container Apps environment
// (with its own container registry and Log Analytics workspace).
builder.AddAzureContainerAppEnvironment("aca-env");

// The ASP.NET Core project serves both /api and, once published, the React UI,
// so it is deployed as the "web" Container App.
var web = builder.AddProject<Projects.AspireShowcase_Api>("web")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// Vite dev server with hot reload, used for local development only.
var frontend = builder.AddViteApp("frontend", "../AspireShowcase.Web")
    .WithReference(web)
    .WaitFor(web);

// On publish, the built React app is copied into the web container's wwwroot,
// so a single Container App serves both the UI and /api.
web.PublishWithContainerFiles(frontend, "wwwroot");

builder.Build().Run();
