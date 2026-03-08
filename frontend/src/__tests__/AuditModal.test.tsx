import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import AuditModal from "../components/admin/AuditModal";
import type { AuditLog } from "../components/admin/types";

const mockLogs: AuditLog[] = [
  {
    id: 1,
    changedByUserName: "Admin User",
    changedAt: "2026-03-07T12:00:00",
    fieldName: "Notes",
    oldValue: null,
    newValue: "Updated shift",
  },
  {
    id: 2,
    changedByUserName: "Admin User",
    changedAt: "2026-03-07T13:00:00",
    fieldName: "ClockIn",
    oldValue: "2026-03-07T08:00:00",
    newValue: "2026-03-07T09:00:00",
  },
];

describe("AuditModal", () => {
  it("does not render when entryId is null", () => {
    render(
      <AuditModal
        entryId={null}
        auditLogs={[]}
        loading={false}
        onClose={vi.fn()}
      />
    );
    expect(screen.queryByText("Edit History")).not.toBeInTheDocument();
  });

  it("renders modal title when entryId is provided", () => {
    render(
      <AuditModal
        entryId={1}
        auditLogs={mockLogs}
        loading={false}
        onClose={vi.fn()}
      />
    );
    expect(screen.getByText("Edit History")).toBeInTheDocument();
  });

  it("shows loading state without table when loading is true", () => {
    render(
      <AuditModal
        entryId={1}
        auditLogs={[]}
        loading={true}
        onClose={vi.fn()}
      />
    );
    // Title still shows
    expect(screen.getByText("Edit History")).toBeInTheDocument();
    // Table headers should NOT appear during loading
    expect(screen.queryByText("When")).not.toBeInTheDocument();
    expect(screen.queryByText("Field")).not.toBeInTheDocument();
  });

  it("shows empty state when auditLogs is empty and not loading", () => {
    render(
      <AuditModal
        entryId={1}
        auditLogs={[]}
        loading={false}
        onClose={vi.fn()}
      />
    );
    expect(
      screen.getByText(/no audit records found/i)
    ).toBeInTheDocument();
  });

  it("renders table headers when logs are present", () => {
    render(
      <AuditModal
        entryId={1}
        auditLogs={mockLogs}
        loading={false}
        onClose={vi.fn()}
      />
    );
    expect(screen.getByText("When")).toBeInTheDocument();
    expect(screen.getByText("By")).toBeInTheDocument();
    expect(screen.getByText("Field")).toBeInTheDocument();
    expect(screen.getByText("Old Value")).toBeInTheDocument();
    expect(screen.getByText("New Value")).toBeInTheDocument();
  });

  it("renders each audit log row with field name and new value", () => {
    render(
      <AuditModal
        entryId={1}
        auditLogs={mockLogs}
        loading={false}
        onClose={vi.fn()}
      />
    );
    expect(screen.getByText("Notes")).toBeInTheDocument();
    expect(screen.getByText("Updated shift")).toBeInTheDocument();
    expect(screen.getByText("ClockIn")).toBeInTheDocument();
    expect(screen.getByText("Admin User")).toBeInTheDocument();
  });

  it("renders 'null' placeholder for null old values", () => {
    render(
      <AuditModal
        entryId={1}
        auditLogs={mockLogs}
        loading={false}
        onClose={vi.fn()}
      />
    );
    // First log has oldValue: null — should show italic "null" placeholder
    const nullSpans = screen.getAllByText("null");
    expect(nullSpans.length).toBeGreaterThan(0);
  });

  it("calls onClose when the Close button is clicked", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();

    render(
      <AuditModal
        entryId={1}
        auditLogs={mockLogs}
        loading={false}
        onClose={onClose}
      />
    );

    await user.click(screen.getByRole("button", { name: /^close$/i }));

    expect(onClose).toHaveBeenCalledOnce();
  });
});
