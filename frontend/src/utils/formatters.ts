// Timestamps from the backend are Zurich-local with no timezone suffix.
// We must NOT pass them through new Date() + toLocaleString() because that
// reinterprets them in the browser's local timezone, which may differ from Zurich.
// Instead, parse the ISO string directly and format manually.
export function formatDate(dateStr: string | Date) {
  const s = typeof dateStr === "string" ? dateStr : dateStr.toISOString();
  // Expected format: "2026-03-08T09:30:00" or "2026-03-08T09:30:00.123"
  const [datePart, timePart] = s.split("T");
  if (!datePart || !timePart) return s;
  const [year, month, day] = datePart.split("-");
  if (!year || !month || !day) return s;
  const [h, m, sec] = timePart.split(":");
  if (!h || !m) return s;
  const sClean = sec?.split(".")[0] ?? "00"; // strip fractional seconds
  return `${day}.${month}.${year}, ${h}:${m}:${sClean}`;
}

// Date-only: "08.03.2026"
export function formatDateOnly(dateStr: string) {
  const datePart = dateStr.split("T")[0];
  if (!datePart) return dateStr;
  const [year, month, day] = datePart.split("-");
  if (!year || !month || !day) return dateStr;
  return `${day}.${month}.${year}`;
}

// Time-only: "09:30"
export function formatTimeShort(dateStr: string) {
  const timePart = dateStr.split("T")[1];
  if (!timePart) return dateStr;
  const [h, m] = timePart.split(":");
  return `${h}:${m}`;
}

export function formatDuration(minutes: number | null) {
  if (minutes == null) return "In progress";
  const h = Math.floor(minutes / 60);
  const m = Math.round(minutes % 60);
  return `${h}h ${m}m`;
}

export function getApiError(err: unknown, fallback = "An error occurred."): string {
  const error = err as { response?: { data?: { error?: string } }; friendlyMessage?: string };
  // Surface timeout-specific message (12.4)
  if (error?.friendlyMessage) return error.friendlyMessage;
  return error?.response?.data?.error || fallback;
}
