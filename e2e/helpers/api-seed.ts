/**
 * E2E test-data seeder.
 *
 * Creates the backend resources that E2E tests need before they run.
 * Calls the live backend REST API using the default admin credentials.
 *
 * Designed to be idempotent — running it twice does not create duplicates.
 */

const API_BASE = process.env.E2E_API_URL ?? "http://localhost:5000/api";

// ── Types ────────────────────────────────────────────────────────────────────

interface AuthResponse {
  token: string;
  username: string;
  fullName: string;
  role: string;
}

interface ApiErrorBody {
  error?: string;
}

// ── Auth helpers ──────────────────────────────────────────────────────────────

async function getAdminToken(): Promise<string> {
  const resp = await fetch(`${API_BASE}/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username: "admin", password: "admin123" }),
  });

  if (!resp.ok) {
    throw new Error(
      `Admin login failed (${resp.status}). Is the backend running at ${API_BASE}?`
    );
  }

  const data = (await resp.json()) as AuthResponse;
  return data.token;
}

/**
 * Logs in as the E2E employee and returns their JWT.
 * Calls ensureE2EEmployee() first so the account always exists.
 */
export async function getEmployeeToken(): Promise<string> {
  await ensureE2EEmployee();
  const resp = await fetch(`${API_BASE}/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username: "e2e_employee", password: "Employee123!" }),
  });
  if (!resp.ok) throw new Error(`e2e_employee login failed (${resp.status})`);
  return ((await resp.json()) as AuthResponse).token;
}

// ── Seed functions ───────────────────────────────────────────────────────────

/**
 * Creates the E2E test employee (username: e2e_employee) via the Admin API.
 * Safe to call multiple times — "username already exists" is treated as success.
 */
export async function ensureE2EEmployee(): Promise<void> {
  const token = await getAdminToken();

  const resp = await fetch(`${API_BASE}/admin/employees`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({
      username: "e2e_employee",
      password: "Employee123!",
      fullName: "E2E Test Employee",
      role: "Employee",
    }),
  });

  if (!resp.ok) {
    const body = (await resp.json()) as ApiErrorBody;
    // "Username already exists" means the employee is already there — that is fine.
    if (!body.error?.toLowerCase().includes("already exists")) {
      throw new Error(`Failed to create E2E employee: ${body.error ?? resp.status}`);
    }
  }
}

/**
 * Clocks in the E2E employee via the API.
 * Useful for specs that need to start in a clocked-in state.
 * "Already clocked in" is treated as success.
 */
export async function clockInE2EEmployee(): Promise<unknown> {
  const token = await getEmployeeToken();

  const resp = await fetch(`${API_BASE}/attendance/clock-in`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
  });

  if (!resp.ok) {
    const body = (await resp.json()) as ApiErrorBody;
    // "Already clocked in" is acceptable — employee is in the right state.
    if (!body.error?.toLowerCase().includes("already clocked in")) {
      throw new Error(`Clock-in failed: ${body.error ?? resp.status}`);
    }
  }

  return resp.ok ? resp.json() : null;
}

/**
 * Ensures the E2E employee is clocked OUT.
 * Checks attendance status first; clocks out only if currently clocked in.
 */
export async function ensureClockOut(): Promise<void> {
  const token = await getEmployeeToken();

  const statusResp = await fetch(`${API_BASE}/attendance/status`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!statusResp.ok)
    throw new Error(`Failed to check attendance status (${statusResp.status})`);

  const { isClockedIn } = (await statusResp.json()) as { isClockedIn: boolean };
  if (!isClockedIn) return;

  const resp = await fetch(`${API_BASE}/attendance/clock-out`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!resp.ok) throw new Error(`Clock-out failed (${resp.status})`);
}

/**
 * Ensures the E2E employee has at least one completed (closed) time entry.
 * Clocks in then immediately clocks out via the API.
 */
export async function ensureHasCompletedEntry(): Promise<void> {
  await ensureE2EEmployee();
  await ensureClockOut();       // start from a known clocked-out state
  await clockInE2EEmployee();   // create an open entry
  await ensureClockOut();       // close it
}

/**
 * Returns the database ID of the E2E employee from the admin employee list.
 */
export async function getE2EEmployeeId(): Promise<number> {
  const token = await getAdminToken();
  const resp = await fetch(`${API_BASE}/admin/employees`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!resp.ok) throw new Error(`Failed to list employees (${resp.status})`);

  const employees = (await resp.json()) as Array<{ id: number; username: string }>;
  const emp = employees.find((e) => e.username === "e2e_employee");
  if (!emp) throw new Error("e2e_employee not found in employee list");
  return emp.id;
}

/**
 * Activates or deactivates a user by ID via the admin API.
 * Used to set up and clean up the deactivation edge-case test.
 */
export async function setEmployeeActive(userId: number, isActive: boolean): Promise<void> {
  const token = await getAdminToken();
  const resp = await fetch(`${API_BASE}/admin/users/${userId}/status`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: JSON.stringify({ isActive }),
  });
  if (!resp.ok) {
    const body = (await resp.json().catch(() => ({}))) as ApiErrorBody;
    throw new Error(
      `setEmployeeActive(${userId}, ${isActive}) failed: ${body.error ?? resp.status}`
    );
  }
}

/**
 * Returns the ID of the most recent time entry for the E2E employee.
 * Requires at least one entry to exist (call ensureHasCompletedEntry first).
 */
export async function getLatestEntryId(): Promise<number> {
  const adminToken = await getAdminToken();
  const empId = await getE2EEmployeeId();

  const resp = await fetch(`${API_BASE}/admin/reports/${empId}?pageSize=1`, {
    headers: { Authorization: `Bearer ${adminToken}` },
  });
  if (!resp.ok) throw new Error(`Failed to get report (${resp.status})`);

  const data = (await resp.json()) as { entries: Array<{ id: number }> };
  if (!data.entries.length) throw new Error("No entries found for e2e_employee");
  return data.entries[0].id;
}
