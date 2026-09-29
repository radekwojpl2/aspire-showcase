var builder = DistributedApplication.CreateBuilder(args);

// Deployment target for `aspire deploy`: an Azure Container Apps environment
// (with its own container registry and Log Analytics workspace).
builder.AddAzureContainerAppEnvironment("aca-env");

// Secret parameter. Locally it is read from the AppHost's user secrets
// (Parameters:db-connection-string); in CI from the Parameters__db-connection-string env var.
var dbConnectionString = builder.AddParameter("db-connection-string", secret: true);

var api = builder.AddProject<Projects.AspireShowcase_Api>("api")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithEnvironment("ConnectionStrings__db", dbConnectionString);

var web = builder.AddViteApp("web", "../AspireShowcase.Web")
    .WithReference(api)
    .WaitFor(api);

// On publish, the built React app is copied into the API container's wwwroot,
// so a single Container App serves both the UI and /api.
api.PublishWithContainerFiles(web, "wwwroot");

builder.Build().Run();
