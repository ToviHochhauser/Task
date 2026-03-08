import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  timeout: 30_000,
  expect: { timeout: 8_000 },

  // Run tests sequentially — clock-in/out flows are time-dependent and
  // parallel workers would race against each other.
  fullyParallel: false,
  workers: 1,

  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,

  reporter: [["html", { outputFolder: "playwright-report", open: "never" }], ["list"]],

  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:5173",
    trace: "on-first-retry",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
  },

  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],

  // Uncomment to auto-start servers before E2E runs (requires both servers idle):
  // webServer: [
  //   {
  //     command: "cd backend && dotnet run",
  //     url: "http://localhost:5000/api/auth/login",
  //     reuseExistingServer: true,
  //     timeout: 60_000,
  //   },
  //   {
  //     command: "cd frontend && npm run dev",
  //     url: "http://localhost:5173",
  //     reuseExistingServer: true,
  //     timeout: 30_000,
  //   },
  // ],
});
