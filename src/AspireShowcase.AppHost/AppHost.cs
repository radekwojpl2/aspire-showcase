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

builder.Build().Run();
