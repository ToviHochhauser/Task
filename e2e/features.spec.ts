import { test, expect } from "./fixtures/auth.fixture";
import {
  ensureE2EEmployee,
  ensureClockOut,
  getEmployeeToken,
  getAdminToken,
  getE2EEmployeeId,
  ensureHasCompletedEntry,
  getLatestEntryId,
} from "./helpers/api-seed";

const API = process.env.E2E_API_URL ?? "http://localhost:5000/api";

// ──────────────────────────────────────────────────────────────────────────────
// Feature #12 — Notes at Clock-In/Out
// ──────────────────────────────────────────────────────────────────────────────

test.describe("Feature #12 — Notes at Clock-In/Out", () => {
  test.beforeEach(async () => {
    await ensureE2EEmployee();
    await ensureClockOut();
  });

  test("clock-in with note saves note on entry (API)", async () => {
    const token = await getEmployeeToken();

    // Clock in with a note
    const clockInRes = await fetch(`${API}/attendance/clock-in`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({ notes: "E2E morning shift" }),
    });
    expect(clockInRes.status).toBe(200);

    // Check history
    const historyRes = await fetch(`${API}/attendance/history?pageSize=1`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const history = await historyRes.json();
    expect(history.items[0].notes).toBe("E2E morning shift");

    // Clean up
    await ensureClockOut();
  });

  test("clock-out with note appends to clock-in note (API)", async () => {
    const token = await getEmployeeToken();

    // Clock in with a note
    await fetch(`${API}/attendance/clock-in`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({ notes: "Started" }),
    });

    // Clock out with a note
    const clockOutRes = await fetch(`${API}/attendance/clock-out`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({ notes: "Done" }),
    });
    expect(clockOutRes.status).toBe(200);

    // Check history — notes should be "Started | Done"
    const historyRes = await fetch(`${API}/attendance/history?pageSize=1`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    const history = await historyRes.json();
    expect(history.items[0].notes).toBe("Started | Done");
  });

  test("note input field is visible on clock page (UI)", async ({ employeePage }) => {
    const noteInput = employeePage.getByLabel(/optional clock note/i);
    await expect(noteInput).toBeVisible();
    await expect(noteInput).toHaveAttribute("maxLength", "500");
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// Feature #3 — CSV Export + Hourly Rate
// ──────────────────────────────────────────────────────────────────────────────

test.describe("Feature #3 — CSV Export + Hourly Rate", () => {
  test("CSV export returns valid CSV with UTF-8 BOM (API)", async () => {
    await ensureE2EEmployee();
    await ensureHasCompletedEntry();

    const adminToken = await getAdminToken();
    const empId = await getE2EEmployeeId();

    const res = await fetch(`${API}/admin/reports/${empId}?format=csv`, {
      headers: { Authorization: `Bearer ${adminToken}` },
    });

    expect(res.status).toBe(200);
    expect(res.headers.get("content-type")).toContain("text/csv");

    const text = await res.text();
    // Check UTF-8 BOM (first character)
    expect(text.charCodeAt(0)).toBe(0xFEFF);
    // Check header row
    expect(text).toContain("Date,Clock In,Clock Out,Duration (h),Notes,Edited");
    // Check summary row
    expect(text).toContain("Total Hours");
  });

  test("set hourly rate and verify in report (API)", async () => {
    const adminToken = await getAdminToken();
    const empId = await getE2EEmployeeId();

    // Set hourly rate
    const rateRes = await fetch(`${API}/admin/users/${empId}/hourly-rate`, {
      method: "PUT",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${adminToken}`,
      },
      body: JSON.stringify({ hourlyRate: 42.5 }),
    });
    expect(rateRes.status).toBe(200);

    // Verify in report
    const reportRes = await fetch(`${API}/admin/reports/${empId}`, {
      headers: { Authorization: `Bearer ${adminToken}` },
    });
    const report = await reportRes.json();
    expect(report.hourlyRate).toBe(42.5);
    expect(report.estimatedPay).toBeDefined();
  });

  test("negative hourly rate rejected (API)", async () => {
    const adminToken = await getAdminToken();
    const empId = await getE2EEmployeeId();

    const res = await fetch(`${API}/admin/users/${empId}/hourly-rate`, {
      method: "PUT",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${adminToken}`,
      },
      body: JSON.stringify({ hourlyRate: -10 }),
    });
    expect(res.status).toBe(400);
  });

  test("CSV export button visible on admin page (UI)", async ({ adminPage }) => {
    // Navigate to admin page — already logged in as admin
    const empList = adminPage.locator("text=admin");
    await expect(empList.first()).toBeVisible();

    // Click an employee to view report
    await empList.first().click();

    // Export CSV button should appear
    await expect(adminPage.getByText(/export csv/i)).toBeVisible({ timeout: 8000 });
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// Feature #10 — Audit Trails
// ──────────────────────────────────────────────────────────────────────────────

test.describe("Feature #10 — Audit Trails", () => {
  test("editing entry creates audit log (API)", async () => {
    await ensureE2EEmployee();
    await ensureHasCompletedEntry();

    const adminToken = await getAdminToken();
    const empId = await getE2EEmployeeId();
    const entryId = await getLatestEntryId();

    // Edit the entry — add notes
    const editRes = await fetch(`${API}/admin/attendance/${entryId}`, {
      method: "PUT",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${adminToken}`,
      },
      body: JSON.stringify({ notes: "Audit test correction" }),
    });
    expect(editRes.status).toBe(200);

    // Get audit logs
    const auditRes = await fetch(`${API}/admin/attendance/${entryId}/audit`, {
      headers: { Authorization: `Bearer ${adminToken}` },
    });
    expect(auditRes.status).toBe(200);

    const logs = await auditRes.json();
    expect(logs.length).toBeGreaterThan(0);

    // Should have a Notes field change
    const notesLog = logs.find((l: { fieldName: string }) => l.fieldName === "Notes");
    expect(notesLog).toBeDefined();
    expect(notesLog.newValue).toBe("Audit test correction");
  });

  test("audit endpoint returns 404 for nonexistent entry", async () => {
    const adminToken = await getAdminToken();

    const res = await fetch(`${API}/admin/attendance/999999/audit`, {
      headers: { Authorization: `Bearer ${adminToken}` },
    });
    expect(res.status).toBe(404);
  });
});

// ──────────────────────────────────────────────────────────────────────────────
// Feature #17 — Refresh Token Auth
// ──────────────────────────────────────────────────────────────────────────────

test.describe("Feature #17 — Refresh Token Auth", () => {
  test("login returns refresh token, refresh rotates it (API)", async () => {
    // Login
    const loginRes = await fetch(`${API}/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username: "admin", password: "admin123" }),
    });
    expect(loginRes.status).toBe(200);
    const loginBody = await loginRes.json();
    expect(loginBody.refreshToken).toBeDefined();
    expect(loginBody.refreshToken.length).toBeGreaterThan(10);

    // Refresh
    const refreshRes = await fetch(`${API}/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: loginBody.refreshToken }),
    });
    expect(refreshRes.status).toBe(200);
    const refreshBody = await refreshRes.json();
    expect(refreshBody.token).toBeDefined();
    expect(refreshBody.refreshToken).toBeDefined();
    // New refresh token should differ from old
    expect(refreshBody.refreshToken).not.toBe(loginBody.refreshToken);
  });

  test("logout revokes refresh token (API)", async () => {
    // Login
    const loginRes = await fetch(`${API}/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username: "admin", password: "admin123" }),
    });
    const loginBody = await loginRes.json();

    // Logout
    const logoutRes = await fetch(`${API}/auth/logout`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: loginBody.refreshToken }),
    });
    expect(logoutRes.status).toBe(200);

    // Try to use revoked token — should fail
    const refreshRes = await fetch(`${API}/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: loginBody.refreshToken }),
    });
    expect(refreshRes.status).toBe(401);
  });

  test("reusing revoked token triggers theft detection (API)", async () => {
    // Login
    const loginRes = await fetch(`${API}/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username: "admin", password: "admin123" }),
    });
    const loginBody = await loginRes.json();
    const firstRefreshToken = loginBody.refreshToken;

    // Rotate: get new token
    const refreshRes = await fetch(`${API}/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: firstRefreshToken }),
    });
    expect(refreshRes.status).toBe(200);
    const refreshBody = await refreshRes.json();
    const secondRefreshToken = refreshBody.refreshToken;

    // Attacker reuses first (now revoked) token → theft detection
    const attackRes = await fetch(`${API}/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: firstRefreshToken }),
    });
    expect(attackRes.status).toBe(401);
    const attackBody = await attackRes.json();
    expect(attackBody.error).toContain("revoked");

    // Even the second (valid) token should now be revoked
    const secondRes = await fetch(`${API}/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: secondRefreshToken }),
    });
    expect(secondRes.status).toBe(401);
  });

  test("invalid refresh token returns 401 (API)", async () => {
    const res = await fetch(`${API}/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: "completely-invalid-token" }),
    });
    expect(res.status).toBe(401);
  });
});
