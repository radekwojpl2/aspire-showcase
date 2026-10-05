import { apiFetch } from './api.ts';

export type ClientConfig = {
  applicationInsightsConnectionString?: string | null;
};

let config: Promise<ClientConfig> | undefined;

// Runtime settings from the API, fetched once. They differ locally and in Azure, so they
// can't be baked into the build. Anything that fails to load is treated as not set.
export function loadClientConfig(): Promise<ClientConfig> {
  config ??= apiFetch('/api/config')
    .then((response) => (response.ok ? response.json() : {}))
    .catch(() => ({}));
  return config;
}
