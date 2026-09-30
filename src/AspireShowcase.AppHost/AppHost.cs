var builder = DistributedApplication.CreateBuilder(args);

// Deployment target for `aspire deploy`: an Azure Container Apps environment
// (with its own container registry).
var acaEnv = builder.AddAzureContainerAppEnvironment("aca-env");

// The ASP.NET Core project serves both /api and, once published, the React UI,
// so it is deployed as the "web" Container App.
var web = builder.AddProject<Projects.AspireShowcase_Api>("web")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    // Link on the dashboard; Swagger UI is only mapped in Development.
    .WithUrl("/swagger", "Swagger");

// Azure only: keeps logs, traces and metrics in Application Insights.
// Locally, telemetry goes to the Aspire dashboard as before, and nothing here
// is provisioned (run mode would otherwise try to create these in Azure).
if (builder.ExecutionContext.IsPublishMode)
{
    // One Log Analytics workspace shared by the Container Apps environment and
    // Application Insights; otherwise each creates its own.
    var logs = builder.AddAzureLogAnalyticsWorkspace("logs");
    acaEnv.WithAzureLogAnalyticsWorkspace(logs);

    var insights = builder.AddAzureApplicationInsights("insights")
        .WithLogAnalyticsWorkspace(logs);
    web.WithReference(insights);

    // Shared workbook with requests, metrics, browser telemetry and logs,
    // listed under Workbooks in Application Insights.
    builder.AddBicepTemplate("dashboards", "workbooks/overview.bicep")
        .WithParameter("appInsightsName", insights.Resource.NameOutputReference)
        .WithParameter("serializedData", File.ReadAllText(
            Path.Combine(builder.AppHostDirectory, "workbooks", "overview.workbook.json")));
}

// Vite dev server with hot reload, used for local development only.
var frontend = builder.AddViteApp("frontend", "../AspireShowcase.Web")
    .WithReference(web)
    .WaitFor(web);

// On publish, the built React app is copied into the web container's wwwroot,
// so a single Container App serves both the UI and /api.
web.PublishWithContainerFiles(frontend, "wwwroot");

builder.Build().Run();
