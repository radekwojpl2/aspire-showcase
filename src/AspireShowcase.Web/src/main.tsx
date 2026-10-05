import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import './index.css';
import App from './App.tsx';
import { loadSession, SessionContext } from './session.ts';
import { initTelemetry } from './telemetry.ts';

void initTelemetry();

// Who is signed in decides what every page shows, so it's known before the first render.
const session = await loadSession();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <SessionContext.Provider value={session}>
      <App />
    </SessionContext.Provider>
  </StrictMode>,
);
