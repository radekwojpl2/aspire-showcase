import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Everything that isn't the React app goes to the "bff" resource: the API, sign-in, and the
// two paths Logto redirects back to. The Host header is kept (no changeOrigin), so bff builds
// those redirect URIs for http://localhost:5173, the address registered in Logto.
const bff = {
  target: process.env.BFF_HTTP,
  changeOrigin: false,
};

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': bff,
      '/bff': bff,
      '/signin-oidc': bff,
      '/signout-callback-oidc': bff,
    },
  },
});
