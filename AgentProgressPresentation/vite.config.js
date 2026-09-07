import { defineConfig } from "vite";

// The Vite entry lives in app/ so that AgentProgressPresentation/index.html can stay
// a script-free redirect stub. GitHub Pages serves this folder directly, and any
// <script src> in the stub would be fetched by the browser's preload scanner before
// the redirect takes effect, producing a cancelled request on every visit.
export default defineConfig({
  root: "app",
  base: "./",
  build: {
    outDir: "../dist",
    emptyOutDir: true,
    target: "es2020",
    sourcemap: false,
  },
});
