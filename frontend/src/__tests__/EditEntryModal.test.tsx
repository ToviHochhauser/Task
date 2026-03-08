import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import EditEntryModal from "../components/admin/EditEntryModal";
import api from "../services/api";
import toast from "react-hot-toast";
import type { TimeEntry } from "../types";

vi.mock("../services/api");

vi.mock("react-hot-toast", () => ({
  default: { success: vi.fn(), error: vi.fn() },
}));

const mockEntry: TimeEntry = {
  id: 42,
  clockIn: "2026-03-07T09:00:00",
  clockOut: "2026-03-07T17:00:00",
  durationMinutes: 480,
  notes: "Regular shift",
  isManuallyEdited: false,
};

describe("EditEntryModal", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders the modal title when entry is provided", () => {
    render(
      <EditEntryModal entry={mockEntry} onClose={vi.fn()} onSaved={vi.fn()} />
    );
    expect(screen.getByText("Edit Time Entry")).toBeInTheDocument();
  });

  it("does not render when entry is null", () => {
    render(
      <EditEntryModal entry={null} onClose={vi.fn()} onSaved={vi.fn()} />
    );
    expect(screen.queryByText("Edit Time Entry")).not.toBeInTheDocument();
  });

  it("pre-fills notes field from entry", () => {
    render(
      <EditEntryModal entry={mockEntry} onClose={vi.fn()} onSaved={vi.fn()} />
    );
    expect(screen.getByPlaceholderText(/reason for edit/i)).toHaveValue(
      "Regular shift"
    );
  });

  it("pre-fills clock-in datetime from entry", () => {
    render(
      <EditEntryModal entry={mockEntry} onClose={vi.fn()} onSaved={vi.fn()} />
    );
    // datetime-local value is ISO truncated to minutes
    expect(screen.getByDisplayValue("2026-03-07T09:00")).toBeInTheDocument();
  });

  it("pre-fills clock-out datetime from entry", () => {
    render(
      <EditEntryModal entry={mockEntry} onClose={vi.fn()} onSaved={vi.fn()} />
    );
    expect(screen.getByDisplayValue("2026-03-07T17:00")).toBeInTheDocument();
  });

  it("renders Save and Cancel buttons", () => {
    render(
      <EditEntryModal entry={mockEntry} onClose={vi.fn()} onSaved={vi.fn()} />
    );
    expect(screen.getByRole("button", { name: /save/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /cancel/i })).toBeInTheDocument();
  });

  it("calls api.put with entry id and form data on save", async () => {
    const user = userEvent.setup();
    const onSaved = vi.fn();
    vi.mocked(api.put).mockResolvedValueOnce({ data: {} });

    render(
      <EditEntryModal entry={mockEntry} onClose={vi.fn()} onSaved={onSaved} />
    );

    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() =>
      expect(api.put).toHaveBeenCalledWith(
        "/admin/attendance/42",
        expect.objectContaining({ notes: "Regular shift" })
      )
    );
    await waitFor(() => expect(onSaved).toHaveBeenCalledOnce());
  });

  it("shows success toast after save", async () => {
    const user = userEvent.setup();
    vi.mocked(api.put).mockResolvedValueOnce({ data: {} });

    render(
      <EditEntryModal entry={mockEntry} onClose={vi.fn()} onSaved={vi.fn()} />
    );

    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() =>
      expect(vi.mocked(toast.success)).toHaveBeenCalledWith(
        "Entry updated successfully."
      )
    );
  });

  it("shows error toast when api.put fails", async () => {
    const user = userEvent.setup();
    vi.mocked(api.put).mockRejectedValueOnce({
      response: { data: { error: "Entry not found." } },
    });

    render(
      <EditEntryModal entry={mockEntry} onClose={vi.fn()} onSaved={vi.fn()} />
    );

    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() =>
      expect(vi.mocked(toast.error)).toHaveBeenCalledWith("Entry not found.")
    );
  });

  it("shows validation error when clock-out is before clock-in", async () => {
    const user = userEvent.setup();
    const invalidEntry: TimeEntry = {
      ...mockEntry,
      clockIn: "2026-03-07T17:00:00",
      clockOut: "2026-03-07T09:00:00", // before clockIn
    };

    render(
      <EditEntryModal
        entry={invalidEntry}
        onClose={vi.fn()}
        onSaved={vi.fn()}
      />
    );

    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() =>
      expect(vi.mocked(toast.error)).toHaveBeenCalledWith(
        "Clock out time must be after clock in time."
      )
    );
    // api.put should NOT have been called
    expect(api.put).not.toHaveBeenCalled();
  });

  it("leaves clock-out empty when entry has no clock-out", () => {
    const openEntry: TimeEntry = {
      ...mockEntry,
      clockOut: null,
      durationMinutes: null,
    };

    render(
      <EditEntryModal entry={openEntry} onClose={vi.fn()} onSaved={vi.fn()} />
    );

    // Only clock-in should be pre-filled; clock-out should be empty
    expect(screen.getByDisplayValue("2026-03-07T09:00")).toBeInTheDocument();
    expect(screen.queryByDisplayValue("2026-03-07T17:00")).not.toBeInTheDocument();
  });
});
