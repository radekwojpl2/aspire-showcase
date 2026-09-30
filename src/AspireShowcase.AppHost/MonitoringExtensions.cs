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
        IResourceBuilder<ProjectResource> web)
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
        web.WithReference(insights);

        // Shared workbook with requests, metrics, browser telemetry and logs,
        // listed under Workbooks in Application Insights.
        builder.AddBicepTemplate("dashboards", "workbooks/overview.bicep")
            .WithParameter("appInsightsName", insights.Resource.NameOutputReference)
            .WithParameter("serializedData", File.ReadAllText(
                Path.Combine(builder.AppHostDirectory, "workbooks", "overview.workbook.json")));
    }
}
