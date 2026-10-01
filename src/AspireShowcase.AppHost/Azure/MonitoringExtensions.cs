using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;

static class MonitoringExtensions
{
    /// <summary>
    /// Azure only: keeps logs, traces and metrics in Application Insights.
    /// Locally, telemetry goes to the Aspire dashboard as before, and nothing here
    /// is provisioned (run mode would otherwise try to create these in Azure).
    /// </summary>
    public static void AddAzureMonitoring(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<AzureContainerAppEnvironmentResource> acaEnv,
        params IResourceBuilder<ProjectResource>[] projects)
    {
        if (!builder.ExecutionContext.IsPublishMode)
        {
            return;
        }

        // One Log Analytics workspace shared by the Container Apps environment and
        // Application Insights; otherwise each creates its own.
        var logs = builder.AddAzureLogAnalyticsWorkspace("logs");
        acaEnv.WithAzureLogAnalyticsWorkspace(logs);

        var insights = builder.AddAzureApplicationInsights("insights")
            .WithLogAnalyticsWorkspace(logs);
        foreach (var project in projects)
        {
            project.WithReference(insights);
        }

        // Shared workbooks, listed under Workbooks in Application Insights: an overview with
        // requests, metrics, browser telemetry and logs, and one for troubleshooting the API
        // with its database and cache.
        AddWorkbook("dashboards", "overview", "Aspire showcase overview");
        AddWorkbook("api-dashboard", "api", "Aspire showcase API");

        // The key keeps the workbook the same Azure resource across deploys; the layout is
        // read from workbooks/{file}.workbook.json.
        void AddWorkbook(string name, string file, string displayName) =>
            builder.AddBicepTemplate(name, "workbooks/workbook.bicep")
                .WithParameter("appInsightsName", insights.Resource.NameOutputReference)
                .WithParameter("workbookKey", $"aspire-showcase-{file}")
                .WithParameter("displayName", displayName)
                .WithParameter("serializedData", File.ReadAllText(
                    Path.Combine(builder.AppHostDirectory, "workbooks", $"{file}.workbook.json")));
    }
}
