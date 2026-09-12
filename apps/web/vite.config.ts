import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // 5200 for the web app and 5201 for the API. Both stay on http locally,
    // and that is deliberate: browsers treat http and https as different
    // sites, so mixing schemes here would break the auth cookie in a way
    // that looks like an auth bug rather than a scheme mismatch.
    port: 5200,

    // Fail loudly if 5200 is taken instead of silently moving to 5201, which
    // is the API's port and would produce a very confusing afternoon.
    strictPort: true,
  },
})
