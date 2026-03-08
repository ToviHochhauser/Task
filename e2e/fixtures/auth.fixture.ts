/**
 * Playwright auth fixtures.
 *
 * Provides pre-authenticated `Page` objects for Admin and Employee roles,
 * so individual E2E specs do not need to repeat login boilerplate.
 *
 * Usage in a spec:
 *   import { test, expect } from "../fixtures/auth.fixture";
 *
 *   test("admin sees employee list", async ({ adminPage }) => {
 *     await adminPage.goto("/admin");
 *     await expect(adminPage.getByRole("heading", { name: /admin/i })).toBeVisible();
 *   });
 */

import { test as base, type Page } from "@playwright/test";
import { ensureE2EEmployee } from "../helpers/api-seed";

// ── Types ────────────────────────────────────────────────────────────────────

type AuthFixtures = {
  /** A Page already logged in as the default admin (admin / admin123). */
  adminPage: Page;
  /** A Page already logged in as the seeded E2E employee. */
  employeePage: Page;
};

// ── Login helper ─────────────────────────────────────────────────────────────

async function loginAs(page: Page, username: string, password: string): Promise<void> {
  await page.goto("/login");

  await page.getByLabel(/username/i).fill(username);
  await page.getByLabel(/password/i).fill(password);
  await page.getByRole("button", { name: /sign in|login/i }).click();

  // Wait until navigation away from /login confirms success.
  await page.waitForURL((url) => !url.pathname.includes("/login"), {
    timeout: 10_000,
  });
}

// ── Fixtures ─────────────────────────────────────────────────────────────────

export const test = base.extend<AuthFixtures>({
  adminPage: async ({ page }, use) => {
    await loginAs(page, "admin", "admin123");
    await use(page);
  },

  employeePage: async ({ browser }, use) => {
    // Ensure the E2E employee exists in the backend before logging in.
    await ensureE2EEmployee();

    const context = await browser.newContext();
    const page = await context.newPage();
    await loginAs(page, "e2e_employee", "Employee123!");
    await use(page);
    await context.close();
  },
});

export { expect } from "@playwright/test";
