/**
 * E2E — Admin dashboard flows
 *
 * Covers: employee list, creating employees, viewing attendance reports,
 * editing time entries, reopening entries, and toggling user status.
 *
 * beforeEach ensures the E2E employee has at least one completed entry
 * so report-related tests always have data to work with.
 */

import { test, expect } from "./fixtures/auth.fixture";
import {
  ensureHasCompletedEntry,
  getE2EEmployeeId,
  setEmployeeActive,
} from "./helpers/api-seed";

test.describe("Admin dashboard", () => {
  test.beforeEach(async () => {
    // Always start with a completed entry for e2e_employee
    await ensureHasCompletedEntry();
  });

  // ── Employee list ───────────────────────────────────────────────────────────

  test("admin sees the employee list panel with at least one employee", async ({
    adminPage,
  }) => {
    await adminPage.goto("/admin");

    // "Employees" panel heading is visible
    await expect(adminPage.getByText("Employees")).toBeVisible();

    // The seeded admin account is always in the list
    await expect(adminPage.getByText("System Administrator")).toBeVisible();
  });

  // ── Create employee ─────────────────────────────────────────────────────────

  test("admin can create a new employee via the form", async ({ adminPage }) => {
    await adminPage.goto("/admin");

    // Open the Add Employee form (desktop "Add" button)
    await adminPage.getByRole("button", { name: /^add$/i }).click();

    // Fill in valid credentials (password: 8+ chars, 1 uppercase, 1 digit)
    const uniqueSuffix = Date.now().toString().slice(-7);
    await adminPage.getByPlaceholder("Username").fill(`e2enew${uniqueSuffix}`);
    await adminPage
      .getByPlaceholder(/password/i)
      .fill("NewEmp1234");
    await adminPage.getByPlaceholder("Full Name").fill("New E2E User");

    await adminPage.getByRole("button", { name: /^create$/i }).click();

    // Success toast confirms creation
    await expect(
      adminPage.getByText("Employee created successfully.")
    ).toBeVisible({ timeout: 8_000 });
  });

  // ── View attendance report ──────────────────────────────────────────────────

  test("admin can click an employee to view their attendance report", async ({
    adminPage,
  }) => {
    await adminPage.goto("/admin");

    // Click on the E2E employee row to load their report
    await adminPage.getByText("E2E Test Employee").click();

    // Report heading appears in the right panel
    await expect(
      adminPage.getByRole("heading", { name: /attendance report/i })
    ).toBeVisible({ timeout: 8_000 });

    // Summary row shows Total Hours
    await expect(adminPage.getByText(/total hours/i)).toBeVisible();

    // At least one entry row exists in the table
    const rows = adminPage.locator("table tbody tr");
    await expect(rows.first()).toBeVisible({ timeout: 6_000 });
  });

  // ── Edit time entry ─────────────────────────────────────────────────────────

  test("admin can edit a time entry's notes and save", async ({ adminPage }) => {
    await adminPage.goto("/admin");
    await adminPage.getByText("E2E Test Employee").click();
    await expect(
      adminPage.getByRole("heading", { name: /attendance report/i })
    ).toBeVisible({ timeout: 8_000 });

    // Open the edit modal for the first entry in the table
    await adminPage.getByRole("button", { name: /^edit$/i }).first().click();

    // Confirm the modal opened
    await expect(adminPage.getByRole("dialog")).toBeVisible();
    await expect(adminPage.getByText("Edit Time Entry")).toBeVisible();

    // Update the notes field
    const notesInput = adminPage.locator(
      'input[placeholder="Reason for edit..."]'
    );
    await notesInput.fill("E2E automated test note");

    // Save
    await adminPage.getByRole("button", { name: /^save$/i }).click();

    // Success toast and modal closes
    await expect(
      adminPage.getByText("Entry updated successfully.")
    ).toBeVisible({ timeout: 8_000 });
    await expect(adminPage.getByRole("dialog")).not.toBeVisible();

    // The "Edited" badge should now appear on the updated row
    await expect(adminPage.getByText("Edited").first()).toBeVisible({
      timeout: 6_000,
    });
  });

  // ── Reopen entry ────────────────────────────────────────────────────────────

  test("admin can reopen a closed entry via the Reopen button", async ({
    adminPage,
  }) => {
    await adminPage.goto("/admin");
    await adminPage.getByText("E2E Test Employee").click();
    await expect(
      adminPage.getByRole("heading", { name: /attendance report/i })
    ).toBeVisible({ timeout: 8_000 });

    // Accept the window.confirm() dialog that the Reopen handler shows
    adminPage.once("dialog", (dialog) => dialog.accept());
    await adminPage.getByRole("button", { name: /^reopen$/i }).first().click();

    // Success toast confirms the entry was reopened
    await expect(adminPage.getByText(/entry reopened/i)).toBeVisible({
      timeout: 8_000,
    });

    // After reopen, that entry is now open (Clock Out becomes "—")
    // and the Reopen button for that entry disappears
    // (The row count may differ if the employee now has an open entry.)
    // We just confirm the toast appeared — the API response is the ground truth.
  });

  // ── Toggle user status ──────────────────────────────────────────────────────

  test("admin can deactivate an employee and then reactivate them", async ({
    adminPage,
  }) => {
    const empId = await getE2EEmployeeId();

    try {
      await adminPage.goto("/admin");

      // Power button with "Deactivate user" title is next to e2e_employee
      const deactivateBtn = adminPage
        .locator("li")
        .filter({ hasText: "E2E Test Employee" })
        .getByTitle("Deactivate user");
      await deactivateBtn.click();

      // "Inactive" label appears beneath the employee's name
      await expect(
        adminPage
          .locator("li")
          .filter({ hasText: "E2E Test Employee" })
          .getByText("Inactive")
      ).toBeVisible({ timeout: 8_000 });

      // Name renders with line-through styling (opacity 60%) — visual confirmation
      // that the employee is deactivated. We verify by checking the Activate button.
      const activateBtn = adminPage
        .locator("li")
        .filter({ hasText: "E2E Test Employee" })
        .getByTitle("Activate user");
      await expect(activateBtn).toBeVisible({ timeout: 6_000 });

      // Reactivate
      await activateBtn.click();

      // "Inactive" label disappears after reactivation
      await expect(
        adminPage
          .locator("li")
          .filter({ hasText: "E2E Test Employee" })
          .getByText("Inactive")
      ).not.toBeVisible({ timeout: 8_000 });
    } finally {
      // Safety net: always restore active state even if the test fails mid-way
      await setEmployeeActive(empId, true);
    }
  });
});
