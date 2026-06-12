import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/api": "http://127.0.0.1:8766",
    },
  },
  build: {
    // dist~ — trailing ~ keeps Unity from importing Vite output as assets.
    outDir: "dist~",
    emptyOutDir: true,
  },
});
