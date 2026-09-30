// Shared Application Insights workbook: requests, metrics, browser and logs in one place.
// The layout lives in overview.workbook.json; edit it there (or in the portal, then
// export it with Advanced Editor > Gallery Template), because deploys overwrite it.

param appInsightsName string

@description('Workbook JSON (Notebook/1.0). __APPINSIGHTS_ID__ is replaced with the Application Insights resource ID.')
param serializedData string

resource insights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource workbook 'Microsoft.Insights/workbooks@2023-06-01' = {
  // Workbook names must be GUIDs; a deterministic one keeps redeploys idempotent.
  name: guid(resourceGroup().id, 'aspire-showcase-overview')
  location: resourceGroup().location
  kind: 'shared'
  properties: {
    displayName: 'Aspire showcase overview'
    category: 'workbook'
    sourceId: insights.id
    serializedData: replace(serializedData, '__APPINSIGHTS_ID__', insights.id)
  }
}

output workbookId string = workbook.id
