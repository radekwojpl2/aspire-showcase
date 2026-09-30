import { loadClientConfig } from './config.ts';

// Sends page views, browser exceptions and fetch calls to Application Insights.
// The connection string comes from the API at runtime and is only set in Azure,
// so locally this does nothing.
export async function initTelemetry(): Promise<void> {
  const connectionString = (await loadClientConfig()).applicationInsightsConnectionString;
  if (!connectionString) return;

  // Loaded on demand so the SDK isn't in the main bundle when it isn't used.
  const { ApplicationInsights } = await import('@microsoft/applicationinsights-web');
  const appInsights = new ApplicationInsights({
    config: {
      connectionString,
      // Adds W3C trace headers to fetch calls, so browser requests
      // and API requests show up as one end-to-end transaction.
      enableCorsCorrelation: true,
      enableRequestHeaderTracking: true,
      enableResponseHeaderTracking: true,
    },
  });
  appInsights.loadAppInsights();
  appInsights.trackPageView();
}
