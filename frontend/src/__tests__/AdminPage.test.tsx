import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { BrowserRouter } from "react-router-dom";
import AdminPage from "../pages/AdminPage";
import api from "../services/api";
import { AuthProvider } from "../context/AuthContext";

vi.mock("../services/api");

vi.mock("react-hot-toast", () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

// ── Mock data ──────────────────────────────────────────────────────────────────

const mockEmployees = [
  {
    id: 1,
    username: "admin",
    fullName: "Admin User",
    role: "Admin",
    isActive: true,
    createdAt: "2026-01-01T00:00:00",
    hourlyRate: null,
  },
  {
    id: 2,
    username: "emp1",
    fullName: "Employee One",
    role: "Employee",
    isActive: true,
    createdAt: "2026-02-01T00:00:00",
    hourlyRate: 45.0,
  },
];

const mockReport = {
  userId: 2,
  fullName: "Employee One",
  entries: [
    {
      id: 10,
      clockIn: "2026-03-07T09:00:00",
      clockOut: "2026-03-07T17:00:00",
      durationMinutes: 480,
      notes: "Regular shift",
      isManuallyEdited: true,
    },
  ],
  totalHours: 8.0,
  currentPage: 1,
  totalPages: 1,
  totalCount: 1,
  hasNextPage: false,
  hourlyRate: 45.0,
  estimatedPay: 360.0,
};

const mockAuditLogs = [
  {
    id: 1,
    changedByUserName: "Admin User",
    changedAt: "2026-03-07T12:00:00",
    fieldName: "Notes",
    oldValue: null,
    newValue: "Regular shift",
  },
];

function setupApiMocks() {
  vi.mocked(api.get).mockImplementation((url: unknown) => {
    if (url === "/admin/employees")
      return Promise.resolve({ data: mockEmployees });

    if (typeof url === "string" && url.startsWith("/admin/reports/"))
      return Promise.resolve({ data: mockReport });

    if (typeof url === "string" && url.includes("/audit"))
      return Promise.resolve({ data: mockAuditLogs });

    return Promise.resolve({ data: {} });
  });
}

function renderAdmin() {
  // Set up auth state so AdminPage thinks we're logged in as admin
  localStorage.setItem("token", "admin-jwt-token");
  localStorage.setItem(
    "user",
    JSON.stringify({ username: "admin", fullName: "Admin User", role: "Admin" })
  );

  return render(
    <BrowserRouter>
      <AuthProvider>
        <AdminPage />
      </AuthProvider>
    </BrowserRouter>
  );
}

beforeEach(() => {
  setupApiMocks();
});

// ── Tests ───────────────────────────────────────────────────────────────────

describe("AdminPage — Feature Tests (#3, #10)", () => {
  // ── #3: Hourly Rate ────────────────────────────────────────────────

  it("displays hourly rate for employees in the report", async () => {
    renderAdmin();

    // Wait for employee list to load
    await waitFor(() =>
      expect(screen.getByText("Employee One")).toBeInTheDocument()
    );

    // Click on employee to view report
    await userEvent.click(screen.getByText("Employee One"));

    // Should show estimated pay
    await waitFor(() =>
      expect(screen.getByText(/estimated pay/i)).toBeInTheDocument()
    );
  });

  it("displays total hours in report", async () => {
    renderAdmin();

    await waitFor(() =>
      expect(screen.getByText("Employee One")).toBeInTheDocument()
    );

    await userEvent.click(screen.getByText("Employee One"));

    await waitFor(() =>
      expect(screen.getByText(/total hours/i)).toBeInTheDocument()
    );
  });

  it("renders CSV export button for employee report", async () => {
    renderAdmin();

    await waitFor(() =>
      expect(screen.getByText("Employee One")).toBeInTheDocument()
    );

    await userEvent.click(screen.getByText("Employee One"));

    await waitFor(() =>
      expect(screen.getByText(/export csv/i)).toBeInTheDocument()
    );
  });

  // ── #10: Audit Trail ───────────────────────────────────────────────

  it("shows 'Edited' badge for manually edited entries", async () => {
    renderAdmin();

    await waitFor(() =>
      expect(screen.getByText("Employee One")).toBeInTheDocument()
    );

    await userEvent.click(screen.getByText("Employee One"));

    await waitFor(() =>
      expect(screen.getAllByText(/edited/i).length).toBeGreaterThan(0)
    );
  });
});
