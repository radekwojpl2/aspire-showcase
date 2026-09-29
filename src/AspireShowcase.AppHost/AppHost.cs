var builder = DistributedApplication.CreateBuilder(args);

// Deployment target for `aspire deploy`: an Azure Container Apps environment
// (with its own container registry and Log Analytics workspace).
builder.AddAzureContainerAppEnvironment("aca-env");

var api = builder.AddProject<Projects.AspireShowcase_Api>("api")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var web = builder.AddViteApp("web", "../AspireShowcase.Web")
    .WithReference(api)
    .WaitFor(api);

// On publish, the built React app is copied into the API container's wwwroot,
// so a single Container App serves both the UI and /api.
api.PublishWithContainerFiles(web, "wwwroot");

builder.Build().Run();
