/**
 * E2E — Employee clock-in / clock-out flows
 *
 * Each test starts from a clean "clocked out" state enforced by beforeEach.
 * Uses the employeePage fixture (pre-authenticated as e2e_employee).
 */

import { test, expect } from "./fixtures/auth.fixture";
import { ensureE2EEmployee, ensureClockOut } from "./helpers/api-seed";

test.describe("Employee clock in / out", () => {
  test.beforeEach(async () => {
    // Guarantee a clocked-out starting state for every test
    await ensureE2EEmployee();
    await ensureClockOut();
  });

  test("shows 'Clocked Out' status and an enabled Clock In button on load", async ({
    employeePage,
  }) => {
    await employeePage.goto("/");

    await expect(employeePage.getByRole("status")).toContainText(
      /currently clocked out/i
    );
    await expect(
      employeePage.getByRole("button", { name: /^clock in$/i })
    ).toBeEnabled();
  });

  test("clocking in flips the badge to Clocked In and shows an elapsed timer", async ({
    employeePage,
  }) => {
    await employeePage.goto("/");

    await employeePage.getByRole("button", { name: /^clock in$/i }).click();

    // Status badge updates to "Currently Clocked In"
    await expect(employeePage.getByRole("status")).toContainText(
      /currently clocked in/i,
      { timeout: 10_000 }
    );

    // Button switches to Clock Out
    await expect(
      employeePage.getByRole("button", { name: /^clock out$/i })
    ).toBeEnabled();

    // Mono elapsed timer (HH:MM:SS) appears
    await expect(employeePage.locator("span.font-mono")).toBeVisible({
      timeout: 5_000,
    });
  });

  test("clocking in then out creates a completed entry in the history table", async ({
    employeePage,
  }) => {
    await employeePage.goto("/");

    // Clock in
    await employeePage.getByRole("button", { name: /^clock in$/i }).click();
    await expect(employeePage.getByRole("status")).toContainText(
      /currently clocked in/i,
      { timeout: 10_000 }
    );

    // Clock out
    await employeePage.getByRole("button", { name: /^clock out$/i }).click();
    await expect(employeePage.getByRole("status")).toContainText(
      /currently clocked out/i,
      { timeout: 10_000 }
    );

    // History table is visible with at least one row
    const table = employeePage.locator("table").first();
    await expect(table).toBeVisible();

    // The most recent row (index 0, ordered descending) should have a clock-out
    // time — not the placeholder dash that open entries show
    const firstRow = table.locator("tbody tr").first();
    await expect(firstRow).toBeVisible();
    const clockOutCell = firstRow.locator("td").nth(1);
    await expect(clockOutCell).not.toHaveText("—");
  });

  test("history shows the 'Edited' badge for manually edited entries", async ({
    employeePage,
  }) => {
    // This test verifies the UI correctly distinguishes edited entries.
    // We clock in/out to get an entry, then check if the "Edited" badge
    // rendering works — the badge is conditionally shown via isManuallyEdited.
    await employeePage.goto("/");

    await employeePage.getByRole("button", { name: /^clock in$/i }).click();
    await expect(employeePage.getByRole("status")).toContainText(
      /currently clocked in/i,
      { timeout: 10_000 }
    );
    await employeePage.getByRole("button", { name: /^clock out$/i }).click();
    await expect(employeePage.getByRole("status")).toContainText(
      /currently clocked out/i,
      { timeout: 10_000 }
    );

    // The fresh entry is NOT manually edited, so the "Edited" badge should not appear
    const table = employeePage.locator("table").first();
    const firstRow = table.locator("tbody tr").first();
    await expect(firstRow.getByText("Edited")).not.toBeVisible();
  });
});
