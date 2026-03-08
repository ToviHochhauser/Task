import { defineConfig, mergeConfig } from "vitest/config";
import viteConfig from "./vite.config";

export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      // jsdom gives us browser-like APIs (localStorage, window, etc.)
      environment: "jsdom",

      // Run global setup before every test file
      setupFiles: ["./src/test-setup.ts"],

      // Do NOT use Vitest globals — import { describe, it, expect, vi } explicitly.
      globals: false,

      // Coverage via V8 (no Babel needed)
      coverage: {
        provider: "v8",
        reporter: ["text", "html", "lcov"],
        include: ["src/**"],
        exclude: [
          "src/main.tsx",
          "src/test-setup.ts",
          "src/**/__mocks__/**",
          "src/**/*.d.ts",
        ],
      },
    },
  })
);
