import { describe, it, expect } from "vitest";
import { formatDate, formatDateOnly, formatTimeShort, formatDuration, getApiError } from "../utils/formatters";

// ── formatDuration ────────────────────────────────────────────────────────────

describe("formatDuration", () => {
  it("returns 'In progress' for null", () => {
    expect(formatDuration(null)).toBe("In progress");
  });

  it("formats exact hours with zero minutes", () => {
    expect(formatDuration(120)).toBe("2h 0m");
  });

  it("formats hours and minutes", () => {
    expect(formatDuration(90)).toBe("1h 30m");
  });

  it("formats less than one hour", () => {
    expect(formatDuration(45)).toBe("0h 45m");
  });

  it("formats zero minutes", () => {
    expect(formatDuration(0)).toBe("0h 0m");
  });

  it("rounds fractional minutes", () => {
    // 1.6 → h=0, m=round(1.6)=2
    expect(formatDuration(1.6)).toBe("0h 2m");
  });
});

// ── formatDate ────────────────────────────────────────────────────────────────

describe("formatDate", () => {
  it("returns a non-empty string for a valid ISO string", () => {
    const result = formatDate("2026-03-07T10:00:00");
    expect(result).toBeTruthy();
    expect(typeof result).toBe("string");
  });

  it("accepts a Date object", () => {
    const result = formatDate(new Date(2026, 2, 7, 10, 0, 0));
    expect(typeof result).toBe("string");
    expect(result.length).toBeGreaterThan(0);
  });
});

// ── getApiError ───────────────────────────────────────────────────────────────

describe("getApiError", () => {
  it("returns friendlyMessage when present (timeout takes priority)", () => {
    const err = { friendlyMessage: "Request timed out. Please try again." };
    expect(getApiError(err)).toBe("Request timed out. Please try again.");
  });

  it("returns response.data.error when present", () => {
    const err = { response: { data: { error: "Invalid credentials." } } };
    expect(getApiError(err)).toBe("Invalid credentials.");
  });

  it("friendlyMessage overrides response.data.error", () => {
    const err = {
      friendlyMessage: "Connection timed out",
      response: { data: { error: "Server error" } },
    };
    expect(getApiError(err)).toBe("Connection timed out");
  });

  it("returns the custom fallback when no structured error is present", () => {
    expect(getApiError({}, "Something went wrong")).toBe("Something went wrong");
  });

  it("returns the default fallback for null error", () => {
    expect(getApiError(null)).toBe("An error occurred.");
  });
});

// ── formatDateOnly ────────────────────────────────────────────────────────────

describe("formatDateOnly", () => {
  it("formats ISO datetime string to DD.MM.YYYY", () => {
    expect(formatDateOnly("2026-03-08T09:30:00")).toBe("08.03.2026");
  });

  it("handles date-only string (no time part)", () => {
    expect(formatDateOnly("2026-01-15")).toBe("15.01.2026");
  });

  it("pads single-digit days and months", () => {
    expect(formatDateOnly("2026-03-01T00:00:00")).toBe("01.03.2026");
  });

  it("returns raw input for invalid format", () => {
    const result = formatDateOnly("not-a-date");
    expect(typeof result).toBe("string");
    expect(result.length).toBeGreaterThan(0);
  });
});

// ── formatTimeShort ───────────────────────────────────────────────────────────

describe("formatTimeShort", () => {
  it("formats ISO datetime string to HH:MM", () => {
    expect(formatTimeShort("2026-03-08T09:30:00")).toBe("09:30");
  });

  it("handles midnight", () => {
    expect(formatTimeShort("2026-03-08T00:00:00")).toBe("00:00");
  });

  it("handles time with fractional seconds", () => {
    expect(formatTimeShort("2026-03-08T14:05:00.123")).toBe("14:05");
  });

  it("returns input unchanged when no T separator", () => {
    expect(formatTimeShort("2026-03-08")).toBe("2026-03-08");
  });
});
