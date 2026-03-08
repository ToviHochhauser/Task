import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AuthProvider, useAuth } from "../context/AuthContext";
import api from "../services/api";

vi.mock("../services/api");

// ── Test consumer component for refresh token features ──────────────────────

function RefreshAuthConsumer() {
  const { user, token, login, logout, isAdmin } = useAuth();
  return (
    <div>
      <span data-testid="token">{token ?? "null"}</span>
      <span data-testid="username">{user?.username ?? "null"}</span>
      <span data-testid="isAdmin">{String(isAdmin)}</span>
      <button
        onClick={() =>
          login("access-tok", "refresh-tok-123", {
            username: "alice",
            fullName: "Alice Test",
            role: "Employee",
          })
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
      <RefreshAuthConsumer />
    </AuthProvider>
  );
}

beforeEach(() => {
  vi.mocked(api.post).mockReset();
});

// ── Feature #17: Refresh Token Auth ─────────────────────────────────────────

describe("AuthContext — Refresh Token Features (#17)", () => {
  it("login() stores refreshToken in localStorage", async () => {
    const user = userEvent.setup();
    renderAuth();

    await user.click(screen.getByRole("button", { name: "Do Login" }));

    expect(localStorage.getItem("refreshToken")).toBe("refresh-tok-123");
    expect(localStorage.getItem("token")).toBe("access-tok");
  });

  it("logout() removes refreshToken from localStorage", async () => {
    const user = userEvent.setup();
    renderAuth();

    await user.click(screen.getByRole("button", { name: "Do Login" }));
    expect(localStorage.getItem("refreshToken")).toBe("refresh-tok-123");

    // Mock the logout API call (fire-and-forget)
    vi.mocked(api.post).mockResolvedValue({ data: { message: "Logged out." } });

    await user.click(screen.getByRole("button", { name: "Do Logout" }));

    expect(localStorage.getItem("refreshToken")).toBeNull();
    expect(localStorage.getItem("token")).toBeNull();
    expect(localStorage.getItem("user")).toBeNull();
  });

  it("logout() calls /auth/logout with refreshToken", async () => {
    const user = userEvent.setup();
    renderAuth();

    vi.mocked(api.post).mockResolvedValue({ data: { message: "Logged out." } });

    await user.click(screen.getByRole("button", { name: "Do Login" }));
    await user.click(screen.getByRole("button", { name: "Do Logout" }));

    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith("/auth/logout", {
        refreshToken: "refresh-tok-123",
      })
    );
  });

  it("logout() does not throw if server logout fails", async () => {
    const user = userEvent.setup();
    renderAuth();

    vi.mocked(api.post).mockRejectedValue(new Error("Network error"));

    await user.click(screen.getByRole("button", { name: "Do Login" }));

    // Should not throw
    await user.click(screen.getByRole("button", { name: "Do Logout" }));

    expect(screen.getByTestId("token")).toHaveTextContent("null");
  });

  it("auth:unauthorized event clears all auth storage including refreshToken", async () => {
    const user = userEvent.setup();
    renderAuth();

    await user.click(screen.getByRole("button", { name: "Do Login" }));
    expect(screen.getByTestId("token")).toHaveTextContent("access-tok");

    act(() => {
      window.dispatchEvent(new Event("auth:unauthorized"));
    });

    await waitFor(() =>
      expect(screen.getByTestId("token")).toHaveTextContent("null")
    );
  });

  it("multi-tab sync: removing refreshToken in another tab mirrors logout", async () => {
    const user = userEvent.setup();
    renderAuth();

    await user.click(screen.getByRole("button", { name: "Do Login" }));
    expect(screen.getByTestId("token")).toHaveTextContent("access-tok");

    // Simulate another tab removing refreshToken
    act(() => {
      window.dispatchEvent(
        new StorageEvent("storage", {
          key: "refreshToken",
          newValue: null,
          oldValue: "refresh-tok-123",
        })
      );
    });

    await waitFor(() =>
      expect(screen.getByTestId("token")).toHaveTextContent("null")
    );
  });

  it("multi-tab sync: updating token in another tab syncs state", async () => {
    const user = userEvent.setup();
    renderAuth();

    await user.click(screen.getByRole("button", { name: "Do Login" }));

    // Simulate another tab updating the token (from a refresh)
    act(() => {
      window.dispatchEvent(
        new StorageEvent("storage", {
          key: "token",
          newValue: "new-access-tok-from-refresh",
          oldValue: "access-tok",
        })
      );
    });

    await waitFor(() =>
      expect(screen.getByTestId("token")).toHaveTextContent(
        "new-access-tok-from-refresh"
      )
    );
  });
});
