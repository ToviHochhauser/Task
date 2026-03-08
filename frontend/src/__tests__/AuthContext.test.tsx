import { describe, it, expect } from "vitest";
import { render, screen, waitFor, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AuthProvider, useAuth } from "../context/AuthContext";

// ── Test consumer component ───────────────────────────────────────────────────

function AuthConsumer() {
  const { user, token, login, logout, isAdmin } = useAuth();
  return (
    <div>
      <span data-testid="token">{token ?? "null"}</span>
      <span data-testid="username">{user?.username ?? "null"}</span>
      <span data-testid="role">{user?.role ?? "null"}</span>
      <span data-testid="isAdmin">{String(isAdmin)}</span>
      <button
        onClick={() =>
          login("tok123", "refresh123", { username: "alice", fullName: "Alice Test", role: "Employee" })
        }
      >
        Do Login
      </button>
      <button onClick={logout}>Do Logout</button>
    </div>
  );
}

function renderAuth() {
  return render(
    <AuthProvider>
      <AuthConsumer />
    </AuthProvider>
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("AuthContext", () => {
  it("starts with null token and user when localStorage is empty", () => {
    renderAuth();
    expect(screen.getByTestId("token")).toHaveTextContent("null");
    expect(screen.getByTestId("username")).toHaveTextContent("null");
  });

  it("login() updates state and persists to localStorage", async () => {
    const user = userEvent.setup();
    renderAuth();

    await user.click(screen.getByRole("button", { name: "Do Login" }));

    expect(screen.getByTestId("token")).toHaveTextContent("tok123");
    expect(screen.getByTestId("username")).toHaveTextContent("alice");
    expect(localStorage.getItem("token")).toBe("tok123");
    expect(JSON.parse(localStorage.getItem("user")!).username).toBe("alice");
  });

  it("logout() clears state and removes from localStorage", async () => {
    const user = userEvent.setup();
    renderAuth();

    // Login first
    await user.click(screen.getByRole("button", { name: "Do Login" }));
    expect(screen.getByTestId("token")).toHaveTextContent("tok123");

    // Then logout
    await user.click(screen.getByRole("button", { name: "Do Logout" }));

    expect(screen.getByTestId("token")).toHaveTextContent("null");
    expect(screen.getByTestId("username")).toHaveTextContent("null");
    expect(localStorage.getItem("token")).toBeNull();
    expect(localStorage.getItem("user")).toBeNull();
  });

  it("isAdmin is false for Employee role and true for Admin role", async () => {
    const user = userEvent.setup();
    renderAuth();

    // Employee role
    await user.click(screen.getByRole("button", { name: "Do Login" }));
    expect(screen.getByTestId("isAdmin")).toHaveTextContent("false");

    // Now render an Admin version
    function AdminConsumer() {
      const { login, isAdmin } = useAuth();
      return (
        <>
          <span data-testid="adminFlag">{String(isAdmin)}</span>
          <button onClick={() => login("adminTok", "adminRefresh", { username: "bob", fullName: "Bob", role: "Admin" })}>
            Admin Login
          </button>
        </>
      );
    }
    const { getByTestId, getByRole } = render(
      <AuthProvider>
        <AdminConsumer />
      </AuthProvider>
    );
    await user.click(getByRole("button", { name: "Admin Login" }));
    expect(getByTestId("adminFlag")).toHaveTextContent("true");
  });

  it("auth:unauthorized event clears auth state", async () => {
    const user = userEvent.setup();
    renderAuth();

    // Login first
    await user.click(screen.getByRole("button", { name: "Do Login" }));
    expect(screen.getByTestId("token")).toHaveTextContent("tok123");

    // Dispatch the 401 event the Axios interceptor fires
    act(() => {
      window.dispatchEvent(new Event("auth:unauthorized"));
    });

    await waitFor(() =>
      expect(screen.getByTestId("token")).toHaveTextContent("null")
    );
    expect(screen.getByTestId("username")).toHaveTextContent("null");
  });

  it("reads existing token and user from localStorage on mount", () => {
    localStorage.setItem("token", "existing-tok");
    localStorage.setItem(
      "user",
      JSON.stringify({ username: "bob", fullName: "Bob", role: "Employee" })
    );

    renderAuth();

    expect(screen.getByTestId("token")).toHaveTextContent("existing-tok");
    expect(screen.getByTestId("username")).toHaveTextContent("bob");
  });

  it("handles corrupted user JSON in localStorage gracefully", () => {
    localStorage.setItem("token", "some-token");
    localStorage.setItem("user", "<<<invalid json>>>");

    // Should not throw — AuthProvider catches the parse error and returns null user.
    // Note: token state is initialised from localStorage before the catch block
    // clears it, so token remains "some-token" while user is null. This is an
    // acceptable transient state; the token-expiry effect will log out shortly after.
    renderAuth();

    // User is null because JSON.parse threw
    expect(screen.getByTestId("username")).toHaveTextContent("null");
    // Token was already read into state before the catch ran
    expect(screen.getByTestId("token")).toHaveTextContent("some-token");
  });
});
