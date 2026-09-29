import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Proxy API calls to the "web" resource (the ASP.NET Core project)
      '/api': {
        target: process.env.WEB_HTTPS || process.env.WEB_HTTP,
        changeOrigin: true
      }
    }
  }
});
