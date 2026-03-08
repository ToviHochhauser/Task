/**
 * E2E — Authentication flows
 *
 * Covers: login happy path, bad credentials, redirect guards,
 * and role-based access control (/admin is admin-only).
 */

import { test, expect } from "./fixtures/auth.fixture";
import { ensureE2EEmployee } from "./helpers/api-seed";

test.describe("Login page", () => {
  test("wrong credentials display an error message", async ({ page }) => {
    await page.goto("/login");
    await page.getByLabel(/username/i).fill("nobody");
    await page.getByLabel(/password/i).fill("WrongPass99");
    await page.getByRole("button", { name: /sign in/i }).click();

    // Error alert should appear and we should remain on /login
    await expect(page.getByRole("alert")).toBeVisible({ timeout: 8_000 });
    await expect(page).toHaveURL(/\/login/);
  });

  test("admin can log in and reach the admin dashboard", async ({ page }) => {
    await page.goto("/login");
    await page.getByLabel(/username/i).fill("admin");
    await page.getByLabel(/password/i).fill("admin123");
    await page.getByRole("button", { name: /sign in/i }).click();

    // Navigate away from /login confirms successful authentication
    await page.waitForURL((url) => !url.pathname.includes("/login"), {
      timeout: 10_000,
    });

    // Admin can access the admin dashboard
    await page.goto("/admin");
    await expect(
      page.getByRole("heading", { name: /admin dashboard/i })
    ).toBeVisible();
  });

  test("unauthenticated visit to / redirects to /login", async ({ page }) => {
    await page.goto("/");
    await expect(page).toHaveURL(/\/login/);
  });

  test("employee is redirected from /admin to the clock page", async ({ employeePage }) => {
    await ensureE2EEmployee();
    await employeePage.goto("/admin");

    // AdminRoute bounces non-admin users to /
    await expect(employeePage).not.toHaveURL(/\/admin/);
    await expect(
      employeePage.getByRole("heading", { name: /time clock/i })
    ).toBeVisible();
  });
});
