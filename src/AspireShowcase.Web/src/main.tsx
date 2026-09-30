import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { LogtoProvider } from '@logto/react';
import './index.css';
import App from './App.tsx';
import { callbackPath, SignInCallback } from './auth.tsx';
import { loadClientConfig } from './config.ts';
import { initTelemetry } from './telemetry.ts';

void initTelemetry();

// Sign-in is only shown once a Logto application is configured (the logto-app-id parameter).
const { logtoEndpoint, logtoAppId, logtoApiResource } = await loadClientConfig();
const root = createRoot(document.getElementById('root')!);

if (logtoEndpoint && logtoAppId) {
  // Listing the API resource at sign-in lets the app get access tokens for the API later.
  const resources = logtoApiResource ? [logtoApiResource] : [];
  root.render(
    <StrictMode>
      <LogtoProvider config={{ endpoint: logtoEndpoint, appId: logtoAppId, resources }}>
        {window.location.pathname === callbackPath ? (
          <SignInCallback />
        ) : (
          <App signInEnabled apiResource={logtoApiResource ?? undefined} />
        )}
      </LogtoProvider>
    </StrictMode>,
  );
} else {
  root.render(
    <StrictMode>
      <App signInEnabled={false} />
    </StrictMode>,
  );
}
