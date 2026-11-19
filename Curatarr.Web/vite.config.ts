import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Proxies /api/* to the ASP.NET Core backend during development, so the
// browser sees same-origin requests (no CORS setup needed) and the
// Identity cookie works normally. Adjust the target if Curatarr.Api
// prints a different port when you run it.
export default defineConfig({
  plugins: [react()],
  server: {
    host: true,
    allowedHosts: true,
    port: 5173,
    proxy: {
      "/api": {
        target: "http://localhost:5000",
        changeOrigin: true,
      },
      "/hubs": {
        target: "http://localhost:5000",
        changeOrigin: true,
        ws: true, // SignalR needs the WebSocket upgrade proxied too
      },
    },
  },
});
