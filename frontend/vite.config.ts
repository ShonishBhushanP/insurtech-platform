import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Dev server on 5173. The app talks to the API Gateway (APIM stand-in) at VITE_API_BASE.
export default defineConfig({
  plugins: [react()],
  server: { port: 5173 },
});
