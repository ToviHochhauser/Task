import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import Navbar from "../components/Navbar";
import { AuthProvider } from "../context/AuthContext";
import { ThemeProvider } from "../context/ThemeContext";

vi.mock("../services/api");

// Mock refreshApi used by AuthContext logout
vi.mock("../services/api", async () => {
  const actual = await vi.importActual<typeof import("../services/api")>("../services/api");
  return {
    ...actual,
    default: { post: vi.fn(), get: vi.fn(), put: vi.fn(), delete: vi.fn() },
    refreshApi: { post: vi.fn() },
  };
});

function renderNavbar(
  role: "Admin" | "Employee" = "Employee",
  initialPath = "/"
) {
  localStorage.setItem("token", "test-jwt-token");
  localStorage.setItem(
    "user",
    JSON.stringify({ username: "testuser", fullName: "Test User", role })
  );

  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <AuthProvider>
        <ThemeProvider>
          <Navbar />
        </ThemeProvider>
      </AuthProvider>
    </MemoryRouter>
  );
}

describe("Navbar", () => {
  it("renders the TimeClock brand link", () => {
    renderNavbar();
    expect(screen.getByRole("link", { name: /timeclock/i })).toBeInTheDocument();
  });

  it("renders user initials from fullName", () => {
    renderNavbar();
    // "Test User" → initials "TU"
    expect(screen.getByText("TU")).toBeInTheDocument();
  });

  it("renders the user full name and role", () => {
    renderNavbar();
    expect(screen.getByText("Test User")).toBeInTheDocument();
    expect(screen.getByText("Employee")).toBeInTheDocument();
  });

  it("renders Admin link for admin users", () => {
    renderNavbar("Admin");
    // The desktop link — there may be a mobile duplicate, so use getAllBy
    const adminLinks = screen.getAllByRole("link", { name: /admin/i });
    expect(adminLinks.length).toBeGreaterThan(0);
  });

  it("does not render Admin link for Employee users", () => {
    renderNavbar("Employee");
    // Only the "Clock In/Out" link should be present, no admin link
    expect(screen.queryByRole("link", { name: /^admin$/i })).not.toBeInTheDocument();
  });

  it("renders the Sign out button", () => {
    renderNavbar();
    expect(screen.getByRole("button", { name: /sign out/i })).toBeInTheDocument();
  });

  it("clicking Sign out clears auth state from localStorage", async () => {
    const user = userEvent.setup();
    renderNavbar();

    await user.click(screen.getByRole("button", { name: /sign out/i }));

    await waitFor(() => {
      expect(localStorage.getItem("token")).toBeNull();
      expect(localStorage.getItem("user")).toBeNull();
    });
  });

  it("renders theme toggle button with accessibility label", () => {
    renderNavbar();
    // Should have a button that says "Switch to dark mode" or "Switch to light mode"
    expect(screen.getByRole("button", { name: /switch to/i })).toBeInTheDocument();
  });

  it("returns null (renders nothing) when user is not logged in", () => {
    localStorage.removeItem("token");
    localStorage.removeItem("user");

    const { container } = render(
      <MemoryRouter>
        <AuthProvider>
          <ThemeProvider>
            <Navbar />
          </ThemeProvider>
        </AuthProvider>
      </MemoryRouter>
    );

    // Navbar returns null when user is null — nothing rendered
    expect(container.firstChild).toBeNull();
  });

  it("renders 'Clock In/Out' nav link", () => {
    renderNavbar();
    expect(screen.getByRole("link", { name: /clock in\/out/i })).toBeInTheDocument();
  });
});
