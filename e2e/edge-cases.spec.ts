/**
 * E2E — API-level edge case validations (ADDITIONAL-EDGE-CASES.md)
 *
 * These tests call the backend REST API directly with fetch() to verify
 * backend validation rules that are independent of the frontend UI.
 *
 * Fix references map to ADDITIONAL-EDGE-CASES.md item numbers.
 */

import { test, expect } from "./fixtures/auth.fixture";
import {
  ensureE2EEmployee,
  ensureHasCompletedEntry,
  getLatestEntryId,
  getE2EEmployeeId,
  getEmployeeToken,
  setEmployeeActive,
} from "./helpers/api-seed";

const API_BASE = process.env.E2E_API_URL ?? "http://localhost:5000/api";

// ── Shared admin token helper (local to this file) ───────────────────────────

async function adminToken(): Promise<string> {
  const resp = await fetch(`${API_BASE}/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username: "admin", password: "admin123" }),
  });
  return ((await resp.json()) as { token: string }).token;
}

async function createEmployee(
  token: string,
  password: string
): Promise<Response> {
  return fetch(`${API_BASE}/admin/employees`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({
      username: `edge_pw_${Date.now()}`,
      password,
      fullName: "Edge Case Test User",
      role: "Employee",
    }),
  });
}

// ── Fix #11: Password complexity validation ───────────────────────────────────

test.describe("Fix #11 — Password complexity (API)", () => {
  let token: string;

  test.beforeAll(async () => {
    token = await adminToken();
  });

  test("password shorter than 8 characters is rejected with 400", async () => {
    const resp = await createEmployee(token, "Short1");
    expect(resp.status).toBe(400);
    const body = (await resp.json()) as { error: string };
    expect(body.error).toMatch(/8 characters/i);
  });

  test("password without an uppercase letter is rejected with 400", async () => {
    const resp = await createEmployee(token, "nouppercase1");
    expect(resp.status).toBe(400);
    const body = (await resp.json()) as { error: string };
    expect(body.error).toMatch(/uppercase/i);
  });

  test("password without a digit is rejected with 400", async () => {
    const resp = await createEmployee(token, "NoDigitsHere");
    expect(resp.status).toBe(400);
    const body = (await resp.json()) as { error: string };
    expect(body.error).toMatch(/digit/i);
  });

  test("valid password (8+ chars, uppercase, digit) is accepted", async () => {
    const resp = await createEmployee(token, "ValidPass1");
    // 201 Created or 200 OK
    expect(resp.status).toBeLessThan(300);
  });
});

// ── Fix #1 + #8: Deactivated user is blocked by IsActiveMiddleware ────────────

test.describe("Fix #1 + #8 — Deactivated user blocked by middleware (API)", () => {
  test("deactivated employee's JWT returns 401 on every authenticated request", async () => {
    await ensureE2EEmployee();
    const empId = await getE2EEmployeeId();
    const token = await getEmployeeToken();

    try {
      // Confirm the token works BEFORE deactivation
      const beforeResp = await fetch(`${API_BASE}/attendance/status`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect(beforeResp.status).toBe(200);

      // Deactivate the employee via admin API
      await setEmployeeActive(empId, false);

      // Same JWT is now rejected by IsActiveMiddleware (30 s TTL, but freshly set)
      const afterResp = await fetch(`${API_BASE}/attendance/status`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      expect(afterResp.status).toBe(401);

      const body = (await afterResp.json()) as { error: string };
      expect(body.error).toMatch(/deactivated/i);
    } finally {
      // Always restore active status so subsequent tests are not affected
      await setEmployeeActive(empId, true);
    }
  });
});

// ── Fix #2: Future timestamps rejected in admin edit ─────────────────────────

test.describe("Fix #2 — Future timestamps rejected in admin edit (API)", () => {
  test("PUT /admin/attendance/:id rejects a future clock-in timestamp", async () => {
    await ensureHasCompletedEntry();
    const entryId = await getLatestEntryId();
    const token = await adminToken();

    // Build a timestamp one year in the future
    const future = new Date();
    future.setFullYear(future.getFullYear() + 1);
    const futureStr = future.toISOString().slice(0, 16); // "YYYY-MM-DDTHH:mm"

    const resp = await fetch(`${API_BASE}/admin/attendance/${entryId}`, {
      method: "PUT",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({
        clockIn: futureStr,
        clockOut: null,
        notes: "Attempting future clock-in",
      }),
    });

    expect(resp.status).toBe(400);
    const body = (await resp.json()) as { error: string };
    expect(body.error).toMatch(/future/i);
  });

  test("PUT /admin/attendance/:id rejects a future clock-out timestamp", async () => {
    await ensureHasCompletedEntry();
    const entryId = await getLatestEntryId();
    const token = await adminToken();

    // Valid (past) clock-in, future clock-out
    const validClockIn = new Date();
    validClockIn.setDate(validClockIn.getDate() - 1);

    const futureClockOut = new Date();
    futureClockOut.setFullYear(futureClockOut.getFullYear() + 1);

    const resp = await fetch(`${API_BASE}/admin/attendance/${entryId}`, {
      method: "PUT",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({
        clockIn: validClockIn.toISOString().slice(0, 16),
        clockOut: futureClockOut.toISOString().slice(0, 16),
        notes: "Attempting future clock-out",
      }),
    });

    expect(resp.status).toBe(400);
    const body = (await resp.json()) as { error: string };
    expect(body.error).toMatch(/future/i);
  });
});

// ── Fix #4: Max shift duration enforced in admin edit ────────────────────────

test.describe("Fix #4 — Max 24-hour shift duration enforced (API)", () => {
  test("PUT /admin/attendance/:id rejects a shift longer than 24 hours", async () => {
    await ensureHasCompletedEntry();
    const entryId = await getLatestEntryId();
    const token = await adminToken();

    // Clock in 2 days ago, clock out 25 hours later (still in the past)
    const clockIn = new Date();
    clockIn.setDate(clockIn.getDate() - 2);

    const clockOut = new Date(clockIn.getTime() + 25 * 60 * 60 * 1000); // +25 h

    const resp = await fetch(`${API_BASE}/admin/attendance/${entryId}`, {
      method: "PUT",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({
        clockIn: clockIn.toISOString().slice(0, 16),
        clockOut: clockOut.toISOString().slice(0, 16),
        notes: "Testing 25-hour shift rejection",
      }),
    });

    expect(resp.status).toBe(400);
    const body = (await resp.json()) as { error: string };
    expect(body.error).toMatch(/24 hours/i);
  });

  test("PUT /admin/attendance/:id accepts a valid shift within 24 hours", async () => {
    await ensureHasCompletedEntry();
    const entryId = await getLatestEntryId();
    const token = await adminToken();

    // 8-hour shift, 2 days ago — well within limits
    const clockIn = new Date();
    clockIn.setDate(clockIn.getDate() - 2);
    clockIn.setHours(8, 0, 0, 0);

    const clockOut = new Date(clockIn.getTime() + 8 * 60 * 60 * 1000);

    const resp = await fetch(`${API_BASE}/admin/attendance/${entryId}`, {
      method: "PUT",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({
        clockIn: clockIn.toISOString().slice(0, 16),
        clockOut: clockOut.toISOString().slice(0, 16),
        notes: "Valid 8-hour shift",
      }),
    });

    expect(resp.status).toBe(200);
  });
});

// ── Fix #3: Reopen entry via API ──────────────────────────────────────────────

test.describe("Fix #3 — Reopen closed entry (API)", () => {
  test("POST /admin/attendance/:id/reopen clears the clock-out timestamp", async () => {
    await ensureHasCompletedEntry();
    const entryId = await getLatestEntryId();
    const token = await adminToken();

    const resp = await fetch(
      `${API_BASE}/admin/attendance/${entryId}/reopen`,
      {
        method: "POST",
        headers: { Authorization: `Bearer ${token}` },
      }
    );

    expect(resp.status).toBe(200);

    const entry = (await resp.json()) as {
      clockOut: string | null;
      isManuallyEdited: boolean;
    };
    expect(entry.clockOut).toBeNull();
    expect(entry.isManuallyEdited).toBe(true);
  });
});
