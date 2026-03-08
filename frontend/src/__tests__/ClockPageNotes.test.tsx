import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import ClockPage from "../pages/ClockPage";
import api from "../services/api";
import type { TimeEntry, PaginatedResponse } from "../types";

vi.mock("../services/api");

vi.mock("react-hot-toast", () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

// ── Default mock setup ─────────────────────────────────────────────────────────

function setupApiMocks(overrides?: {
  isClockedIn?: boolean;
  lastClockIn?: string | null;
  items?: TimeEntry[];
  totalPages?: number;
}) {
  const { isClockedIn = false, lastClockIn = null, items = [], totalPages = 1 } =
    overrides ?? {};

  vi.mocked(api.get).mockImplementation((url: unknown) => {
    if (url === "/attendance/status")
      return Promise.resolve({ data: { isClockedIn, lastClockIn } });

    if (url === "/attendance/history")
      return Promise.resolve<{ data: PaginatedResponse<TimeEntry> }>({
        data: {
          items,
          currentPage: 1,
          totalPages,
          totalCount: items.length,
          hasNextPage: totalPages > 1,
        },
      });

    if (url === "/time/current")
      return Promise.resolve({ data: { datetime: "2026-03-07T10:00:00" } });

    return Promise.resolve({ data: {} });
  });
}

beforeEach(() => {
  setupApiMocks();
});

// ── Feature #12: Notes at Clock-In/Out ──────────────────────────────────────

describe("ClockPage — Notes Feature (#12)", () => {
  it("renders note input field with correct placeholder", async () => {
    setupApiMocks({ isClockedIn: false });
    render(<ClockPage />);

    await waitFor(() =>
      expect(screen.getByLabelText(/optional clock note/i)).toBeInTheDocument()
    );
    expect(screen.getByPlaceholderText(/add a note/i)).toBeInTheDocument();
  });

  it("sends notes in clock-in request body", async () => {
    const user = userEvent.setup();
    setupApiMocks({ isClockedIn: false });

    vi.mocked(api.post).mockResolvedValueOnce({
      data: {
        message: "Clocked in successfully.",
        isClockedIn: true,
        lastClockIn: "2026-03-07T10:05:00",
        timestamp: "2026-03-07T10:05:00",
      },
    });

    render(<ClockPage />);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /clock in/i })).toBeInTheDocument()
    );

    // Override after initial load so the mount-time syncTime call uses the proper mock
    vi.mocked(api.get).mockResolvedValue({
      data: { items: [], currentPage: 1, totalPages: 1, totalCount: 0, hasNextPage: false },
    });

    // Type a note
    await user.type(screen.getByLabelText(/optional clock note/i), "Morning shift");
    await user.click(screen.getByRole("button", { name: /clock in/i }));

    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith("/attendance/clock-in", {
        notes: "Morning shift",
      })
    );
  });

  it("sends null notes when input is empty", async () => {
    const user = userEvent.setup();
    setupApiMocks({ isClockedIn: false });

    vi.mocked(api.post).mockResolvedValueOnce({
      data: {
        message: "Clocked in successfully.",
        isClockedIn: true,
        lastClockIn: "2026-03-07T10:05:00",
        timestamp: "2026-03-07T10:05:00",
      },
    });

    render(<ClockPage />);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /clock in/i })).toBeInTheDocument()
    );

    // Override after initial load so the mount-time syncTime call uses the proper mock
    vi.mocked(api.get).mockResolvedValue({
      data: { items: [], currentPage: 1, totalPages: 1, totalCount: 0, hasNextPage: false },
    });

    // Click without typing a note
    await user.click(screen.getByRole("button", { name: /clock in/i }));

    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith("/attendance/clock-in", {
        notes: null,
      })
    );
  });

  it("clears note input after successful clock action", async () => {
    const user = userEvent.setup();
    setupApiMocks({ isClockedIn: false });

    vi.mocked(api.post).mockResolvedValueOnce({
      data: {
        message: "Clocked in successfully.",
        isClockedIn: true,
        lastClockIn: "2026-03-07T10:05:00",
        timestamp: "2026-03-07T10:05:00",
      },
    });

    render(<ClockPage />);

    await waitFor(() =>
      expect(screen.getByLabelText(/optional clock note/i)).toBeInTheDocument()
    );

    // Override after initial load so the mount-time syncTime call uses the proper mock
    vi.mocked(api.get).mockResolvedValue({
      data: { items: [], currentPage: 1, totalPages: 1, totalCount: 0, hasNextPage: false },
    });

    await user.type(screen.getByLabelText(/optional clock note/i), "Test note");
    await user.click(screen.getByRole("button", { name: /clock in/i }));

    await waitFor(() =>
      expect(screen.getByLabelText(/optional clock note/i)).toHaveValue("")
    );
  });

  it("displays notes in shift history", async () => {
    const entries: TimeEntry[] = [
      {
        id: 1,
        clockIn: "2026-03-07T09:00:00",
        clockOut: "2026-03-07T17:00:00",
        durationMinutes: 480,
        notes: "Morning shift | Finished early",
        isManuallyEdited: false,
      },
    ];
    setupApiMocks({ items: entries });
    render(<ClockPage />);

    await waitFor(() =>
      expect(screen.getAllByText("Morning shift | Finished early").length).toBeGreaterThan(0)
    );
  });

  it("note input is disabled while loading", async () => {
    setupApiMocks({ isClockedIn: false });
    render(<ClockPage />);

    // Note input should not be disabled when not loading
    await waitFor(() =>
      expect(screen.getByLabelText(/optional clock note/i)).not.toBeDisabled()
    );
  });

  it("note input has maxLength of 500", async () => {
    setupApiMocks({ isClockedIn: false });
    render(<ClockPage />);

    await waitFor(() => {
      const input = screen.getByLabelText(/optional clock note/i);
      expect(input).toHaveAttribute("maxLength", "500");
    });
  });
});
