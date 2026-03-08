import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import ClockPage from "../pages/ClockPage";
import api from "../services/api";
import toast from "react-hot-toast";
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

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("ClockPage", () => {
  it("shows 'Currently Clocked Out' status on initial load when not clocked in", async () => {
    setupApiMocks({ isClockedIn: false });
    render(<ClockPage />);

    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent("Currently Clocked Out")
    );
  });

  it("shows 'Currently Clocked In' status when employee is clocked in", async () => {
    setupApiMocks({ isClockedIn: true, lastClockIn: "2026-03-07T09:00:00" });
    render(<ClockPage />);

    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent("Currently Clocked In")
    );
  });

  it("Clock In button calls api.post and updates status", async () => {
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

    // Wait for initial load to complete before overriding the mock, so that
    // the initial syncTime("/time/current") call uses the proper setupApiMocks
    // implementation and doesn't receive invalid data.
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /clock in/i })).toBeInTheDocument()
    );

    // After clock-in, fetchHistory is called — mock it returning empty
    vi.mocked(api.get).mockResolvedValue({ data: { items: [], currentPage: 1, totalPages: 1, totalCount: 0, hasNextPage: false } });

    await user.click(screen.getByRole("button", { name: /clock in/i }));

    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith("/attendance/clock-in", { notes: null })
    );

    // Status updates from response
    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent("Currently Clocked In")
    );
  });

  it("shows 'No shifts recorded yet' when history is empty", async () => {
    setupApiMocks({ items: [] });
    render(<ClockPage />);

    await waitFor(() =>
      expect(screen.getByText(/no shifts recorded yet/i)).toBeInTheDocument()
    );
  });

  it("renders shift history entries", async () => {
    const entries: TimeEntry[] = [
      {
        id: 1,
        clockIn: "2026-03-07T09:00:00",
        clockOut: "2026-03-07T17:00:00",
        durationMinutes: 480,
        notes: null,
        isManuallyEdited: false,
      },
    ];
    setupApiMocks({ items: entries });
    render(<ClockPage />);

    // "8h 0m" appears in both desktop table and mobile card view
    await waitFor(() => expect(screen.getAllByText("8h 0m").length).toBeGreaterThan(0));
  });

  it("shows pagination controls when history spans multiple pages", async () => {
    const entries: TimeEntry[] = Array.from({ length: 10 }, (_, i) => ({
      id: i + 1,
      clockIn: `2026-03-0${i + 1}T09:00:00`,
      clockOut: `2026-03-0${i + 1}T17:00:00`,
      durationMinutes: 480,
      notes: null,
      isManuallyEdited: false,
    }));

    setupApiMocks({ items: entries, totalPages: 3 });
    render(<ClockPage />);

    await waitFor(() =>
      expect(screen.getByText(/page 1 of 3/i)).toBeInTheDocument()
    );

    expect(screen.getByRole("button", { name: /prev/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /next/i })).toBeInTheDocument();
  });

  it("Clock Out button calls api.post and updates status to Clocked Out", async () => {
    const user = userEvent.setup();
    setupApiMocks({ isClockedIn: true, lastClockIn: "2026-03-07T09:00:00" });

    vi.mocked(api.post).mockResolvedValueOnce({
      data: {
        message: "Clocked out successfully.",
        isClockedIn: false,
        lastClockIn: null,
        timestamp: "2026-03-07T17:00:00",
      },
    });

    render(<ClockPage />);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /clock out/i })).toBeInTheDocument()
    );

    // After clock-out, history is re-fetched — return empty to keep test simple
    vi.mocked(api.get).mockResolvedValue({
      data: { items: [], currentPage: 1, totalPages: 1, totalCount: 0, hasNextPage: false },
    });

    await user.click(screen.getByRole("button", { name: /clock out/i }));

    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith("/attendance/clock-out", { notes: null })
    );

    await waitFor(() =>
      expect(screen.getByRole("status")).toHaveTextContent("Currently Clocked Out")
    );
  });

  it("shows error toast when clock-in API fails", async () => {
    const user = userEvent.setup();
    setupApiMocks({ isClockedIn: false });

    vi.mocked(api.post).mockRejectedValueOnce({
      response: { data: { error: "Already clocked in." } },
    });

    render(<ClockPage />);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /clock in/i })).toBeInTheDocument()
    );

    await user.click(screen.getByRole("button", { name: /clock in/i }));

    await waitFor(() =>
      expect(vi.mocked(toast.error)).toHaveBeenCalledWith(
        expect.stringContaining("Already clocked in")
      )
    );
  });

  it("shows error toast when clock-out API fails", async () => {
    const user = userEvent.setup();
    setupApiMocks({ isClockedIn: true, lastClockIn: "2026-03-07T09:00:00" });

    vi.mocked(api.post).mockRejectedValueOnce({
      response: { data: { error: "Not clocked in." } },
    });

    render(<ClockPage />);

    await waitFor(() =>
      expect(screen.getByRole("button", { name: /clock out/i })).toBeInTheDocument()
    );

    await user.click(screen.getByRole("button", { name: /clock out/i }));

    await waitFor(() =>
      expect(vi.mocked(toast.error)).toHaveBeenCalledWith(
        expect.stringContaining("Not clocked in")
      )
    );
  });
});
