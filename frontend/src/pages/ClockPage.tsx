import { useState, useEffect, useRef } from "react";
import api from "../services/api";
import toast from "react-hot-toast";
import { Clock, LogIn, LogOut, AlertTriangle, Timer, ChevronLeft, ChevronRight, Filter } from "lucide-react";
import clsx from "clsx";
import type { TimeEntry, PaginatedResponse } from "../types";
import { formatDate, formatDateOnly, formatTimeShort, formatDuration, getApiError } from "../utils/formatters";

export default function ClockPage() {
  const [isClockedIn, setIsClockedIn] = useState(false);
  const [lastClockIn, setLastClockIn] = useState<string | null>(null);
  const [history, setHistory] = useState<TimeEntry[]>([]);
  const [historyPage, setHistoryPage] = useState(1);
  const [historyTotalPages, setHistoryTotalPages] = useState(1);
  const [zurichTime, setZurichTime] = useState<Date | null>(null);
  const [loading, setLoading] = useState(false);
  const [initialLoading, setInitialLoading] = useState(true);
  const [timeSyncFailed, setTimeSyncFailed] = useState(false);
  const [elapsed, setElapsed] = useState<string | null>(null);
  const [clockNote, setClockNote] = useState("");
  const [historyFrom, setHistoryFrom] = useState("");
  const [historyTo, setHistoryTo] = useState("");
  const debounceRef = useRef(false);
  const zurichBaseRef = useRef<{ apiTime: Date; localTime: number } | null>(null);

  const fetchStatus = async () => {
    try {
      const { data } = await api.get("/attendance/status");
      setIsClockedIn(data.isClockedIn);
      setLastClockIn(data.lastClockIn);
    } catch {
      toast.error("Failed to fetch status.");
    }
  };

  const fetchHistory = async (page = 1, from?: string, to?: string) => {
    try {
      const params: Record<string, string | number> = { page };
      const f = from ?? historyFrom;
      const t = to ?? historyTo;
      if (f) params.from = f;
      if (t) params.to = t;
      const { data } = await api.get<PaginatedResponse<TimeEntry>>("/attendance/history", { params });
      setHistory(data.items);
      setHistoryPage(data.currentPage);
      setHistoryTotalPages(data.totalPages);
    } catch {
      toast.error("Failed to fetch history.");
    }
  };

  const syncTime = async () => {
    try {
      const { data } = await api.get("/time/current");
      const apiTime = new Date(data.datetime);
      zurichBaseRef.current = { apiTime, localTime: Date.now() };
      setZurichTime(apiTime);
      setTimeSyncFailed(false);
    } catch {
      if (!zurichBaseRef.current) setTimeSyncFailed(true);
    }
  };

  // Single tick drives both Zurich clock and elapsed timer so they change in sync
  const isClockedInRef = useRef(isClockedIn);
  const lastClockInRef = useRef(lastClockIn);
  isClockedInRef.current = isClockedIn;
  lastClockInRef.current = lastClockIn;

  useEffect(() => {
    Promise.all([fetchStatus(), fetchHistory(1), syncTime()]).finally(() => setInitialLoading(false));

    const syncInterval = setInterval(syncTime, 5 * 60 * 1000);

    const formatElapsed = (diffMs: number) => {
      const diff = Math.max(0, diffMs);
      const h = Math.floor(diff / 3600000);
      const m = Math.floor((diff % 3600000) / 60000);
      const s = Math.floor((diff % 60000) / 1000);
      return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
    };

    const tick = () => {
      const base = zurichBaseRef.current;
      if (base) {
        const nowMs = base.apiTime.getTime() + (Date.now() - base.localTime);
        setZurichTime(new Date(nowMs));

        if (isClockedInRef.current && lastClockInRef.current) {
          // Both lastClockIn and base.apiTime are parsed by new Date() from
          // TZ-less strings, so they share the same (browser-local) epoch basis.
          // The diff between them is correct regardless of browser timezone.
          setElapsed(formatElapsed(nowMs - new Date(lastClockInRef.current).getTime()));
        } else {
          setElapsed(null);
        }
      } else {
        // Don't show elapsed until Zurich time syncs — using Date.now()
        // against a Zurich-local timestamp produces a wrong offset if the
        // browser timezone differs from Europe/Zurich.
        setElapsed(null);
      }
    };

    tick();
    const tickInterval = setInterval(tick, 1000);

    return () => {
      clearInterval(syncInterval);
      clearInterval(tickInterval);
    };
  }, []);

  // Listen for token expiry events (8.4)
  useEffect(() => {
    const handleExpired = () => toast.error("Session expired. Please log in again.");
    window.addEventListener("auth:expired", handleExpired);
    return () => window.removeEventListener("auth:expired", handleExpired);
  }, []);

  const handleClock = async (action: "clock-in" | "clock-out") => {
    if (debounceRef.current) return;
    debounceRef.current = true;

    setLoading(true);

    try {
      const { data } = await api.post(`/attendance/${action}`, {
        notes: clockNote.trim() || null,
      });
      toast.success(data.message);
      setClockNote("");
      // Use status from response to avoid stale state (8.1)
      if (data.isClockedIn !== undefined) {
        setIsClockedIn(data.isClockedIn);
        setLastClockIn(data.lastClockIn ?? null);
      }
      // Use response timestamp to update time sync so elapsed timer appears instantly
      if (data.timestamp) {
        zurichBaseRef.current = { apiTime: new Date(data.timestamp), localTime: Date.now() };
        setZurichTime(new Date(data.timestamp));
        setTimeSyncFailed(false);
      }
      await fetchHistory(1);
    } catch (err: unknown) {
      toast.error(getApiError(err, "Operation failed."));
      // Fallback: re-fetch status if the action failed or response lacked status
      await fetchStatus();
    } finally {
      setLoading(false);
      debounceRef.current = false;
    }
  };

  return (
    <div className="flex flex-col gap-6">
      {/* Clock Card */}
      <div className="rounded-xl border border-border bg-card px-6 py-8 text-center shadow-sm dark:border-dark-border dark:bg-dark-card sm:px-8 sm:py-10">
        <h2 className="mb-1 text-xl font-bold tracking-tight text-text dark:text-dark-text sm:text-2xl">
          Time Clock
        </h2>

        {/* Zurich time */}
        {zurichTime ? (
          <div className="mb-5 flex items-center justify-center gap-1.5 text-sm tabular-nums text-text-muted dark:text-dark-text-muted">
            <Clock className="h-3.5 w-3.5" aria-hidden="true" />
            Zurich: <strong className="font-semibold">{formatDate(zurichTime)}</strong>
          </div>
        ) : timeSyncFailed ? (
          <div className="mb-5 flex items-center justify-center gap-1.5 text-sm text-amber-600 dark:text-amber-400">
            <AlertTriangle className="h-3.5 w-3.5" aria-hidden="true" />
            Unable to sync Zurich time. Timestamps are recorded server-side.
          </div>
        ) : null}

        {/* Status badge */}
        <div
          className={clsx(
            "mx-auto mb-2 inline-flex items-center gap-2 rounded-full px-5 py-2 text-[0.8125rem] font-semibold",
            isClockedIn
              ? "bg-success-light text-success dark:bg-green-950 dark:text-green-400"
              : "bg-border-light text-text-muted dark:bg-dark-border dark:text-dark-text-muted"
          )}
          role="status"
          aria-live="polite"
        >
          <span
            className={clsx(
              "h-2 w-2 shrink-0 rounded-full",
              isClockedIn
                ? "animate-pulse-dot bg-success"
                : "bg-text-muted opacity-60 dark:bg-dark-text-muted"
            )}
          />
          {isClockedIn ? "Currently Clocked In" : "Currently Clocked Out"}
        </div>

        {/* Elapsed timer */}
        {isClockedIn && elapsed && (
          <div className="mb-1 flex items-center justify-center gap-2">
            <Timer className="h-4 w-4 text-success" aria-hidden="true" />
            <span className="font-mono text-2xl font-bold tabular-nums text-success sm:text-3xl">
              {elapsed}
            </span>
          </div>
        )}

        {isClockedIn && lastClockIn && (
          <p className="mb-1 text-[0.8125rem] text-text-muted dark:text-dark-text-muted">
            Since: {formatDate(lastClockIn)}
          </p>
        )}

        {/* Optional note input */}
        <div className="mx-auto mt-5 w-full max-w-xs">
          <input
            type="text"
            value={clockNote}
            onChange={(e) => setClockNote(e.target.value)}
            maxLength={500}
            placeholder={isClockedIn ? "Add a note for clock-out..." : "Add a note for clock-in..."}
            className="w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-text outline-none placeholder:text-gray-400 focus:border-primary focus:ring-2 focus:ring-primary/15 dark:border-dark-border dark:bg-dark-bg dark:text-dark-text dark:placeholder:text-gray-500 dark:focus:border-primary"
            aria-label="Optional clock note"
            disabled={loading}
          />
          <p className="mt-1 text-[0.6875rem] text-text-muted dark:text-dark-text-muted">
            {isClockedIn
              ? "This note will be saved when you clock out."
              : "This note will be saved when you clock in."}
          </p>
        </div>

        {/* Clock button */}
        <div className="mt-4">
          {!isClockedIn ? (
            <button
              className="inline-flex items-center gap-2.5 rounded-full bg-primary-darker px-10 py-3.5 text-[1.0625rem] font-semibold tracking-wide text-white shadow-[0_4px_16px_rgba(19,78,74,0.3)] transition-all hover:-translate-y-0.5 hover:bg-primary-dark hover:shadow-[0_8px_24px_rgba(19,78,74,0.4)] active:translate-y-0 active:shadow-[0_2px_8px_rgba(19,78,74,0.25)] disabled:translate-y-0 disabled:cursor-not-allowed disabled:opacity-55 disabled:shadow-none"
              onClick={() => handleClock("clock-in")}
              disabled={loading}
            >
              <LogIn className="h-5 w-5" aria-hidden="true" />
              {loading ? "Processing..." : "Clock In"}
            </button>
          ) : (
            <button
              className="inline-flex items-center gap-2.5 rounded-full bg-danger px-10 py-3.5 text-[1.0625rem] font-semibold tracking-wide text-white shadow-[0_4px_16px_rgba(220,38,38,0.25)] transition-all hover:-translate-y-0.5 hover:bg-danger-hover hover:shadow-[0_8px_24px_rgba(220,38,38,0.35)] active:translate-y-0 active:shadow-[0_2px_8px_rgba(220,38,38,0.2)] disabled:translate-y-0 disabled:cursor-not-allowed disabled:opacity-55 disabled:shadow-none"
              onClick={() => handleClock("clock-out")}
              disabled={loading}
            >
              <LogOut className="h-5 w-5" aria-hidden="true" />
              {loading ? "Processing..." : "Clock Out"}
            </button>
          )}
        </div>
      </div>

      {/* Shift History */}
      <div className="rounded-xl border border-border bg-card p-5 shadow-sm sm:p-6 dark:border-dark-border dark:bg-dark-card">
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <h3 className="text-[0.9375rem] font-bold tracking-tight text-text dark:text-dark-text">
            Recent Shifts
          </h3>
          <div className="flex flex-wrap items-center gap-2">
            <Filter className="h-3.5 w-3.5 text-text-muted dark:text-dark-text-muted" aria-hidden="true" />
            <input
              type="date"
              value={historyFrom}
              onChange={(e) => { setHistoryFrom(e.target.value); fetchHistory(1, e.target.value, historyTo); }}
              className="rounded-lg border border-border bg-white px-2 py-1 text-xs text-text outline-none focus:border-primary dark:border-dark-border dark:bg-dark-bg dark:text-dark-text"
              aria-label="From date"
            />
            <span className="text-xs text-text-muted dark:text-dark-text-muted">to</span>
            <input
              type="date"
              value={historyTo}
              onChange={(e) => { setHistoryTo(e.target.value); fetchHistory(1, historyFrom, e.target.value); }}
              className="rounded-lg border border-border bg-white px-2 py-1 text-xs text-text outline-none focus:border-primary dark:border-dark-border dark:bg-dark-bg dark:text-dark-text"
              aria-label="To date"
            />
            {(historyFrom || historyTo) && (
              <button
                onClick={() => { setHistoryFrom(""); setHistoryTo(""); fetchHistory(1, "", ""); }}
                className="rounded-md px-2 py-1 text-xs font-medium text-text-muted transition-colors hover:bg-border-light hover:text-text dark:text-dark-text-muted dark:hover:bg-dark-border dark:hover:text-dark-text"
              >
                Clear
              </button>
            )}
          </div>
        </div>
        {initialLoading ? (
          <div className="flex flex-col gap-2">
            {[1, 2, 3].map((i) => (
              <div key={i} className="h-10 animate-pulse rounded-lg bg-border-light dark:bg-dark-border" />
            ))}
          </div>
        ) : history.length === 0 ? (
          <p className="py-8 text-center text-sm text-text-muted dark:text-dark-text-muted">
            No shifts recorded yet.
          </p>
        ) : (
          <>
            {/* Desktop table */}
            <div className="hidden sm:block">
              <table className="w-full border-collapse text-sm">
                <thead>
                  <tr>
                    <th className="border-b-2 border-border px-3 py-2 text-left text-[0.7rem] font-semibold uppercase tracking-wider text-text-muted dark:border-dark-border dark:text-dark-text-muted">
                      Clock In
                    </th>
                    <th className="border-b-2 border-border px-3 py-2 text-left text-[0.7rem] font-semibold uppercase tracking-wider text-text-muted dark:border-dark-border dark:text-dark-text-muted">
                      Clock Out
                    </th>
                    <th className="border-b-2 border-border px-3 py-2 text-left text-[0.7rem] font-semibold uppercase tracking-wider text-text-muted dark:border-dark-border dark:text-dark-text-muted">
                      Duration
                    </th>
                    <th className="border-b-2 border-border px-3 py-2 text-left text-[0.7rem] font-semibold uppercase tracking-wider text-text-muted dark:border-dark-border dark:text-dark-text-muted">
                      Notes
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {history.map((entry) => (
                    <tr
                      key={entry.id}
                      className={clsx(
                        "transition-colors",
                        entry.isManuallyEdited
                          ? "bg-amber-50 hover:bg-amber-100 dark:bg-amber-950/30 dark:hover:bg-amber-950/50"
                          : "hover:bg-primary-xlight dark:hover:bg-dark-border/50"
                      )}
                    >
                      <td className="border-b border-border-light px-3 py-3 dark:border-dark-border">
                        {formatDate(entry.clockIn)}
                      </td>
                      <td className="border-b border-border-light px-3 py-3 dark:border-dark-border">
                        {entry.clockOut ? formatDate(entry.clockOut) : "—"}
                      </td>
                      <td className="border-b border-border-light px-3 py-3 font-medium tabular-nums dark:border-dark-border">
                        {formatDuration(entry.durationMinutes)}
                      </td>
                      <td className="border-b border-border-light px-3 py-3 dark:border-dark-border">
                        {entry.notes || ""}
                        {entry.isManuallyEdited && (
                          <span className="ml-2 inline-block rounded-full bg-warning-light px-2 py-0.5 text-[0.625rem] font-bold uppercase tracking-wide text-warning-dark dark:bg-amber-900/50 dark:text-amber-300">
                            Edited
                          </span>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Mobile card view */}
            <div className="flex flex-col gap-3 sm:hidden">
              {history.map((entry) => (
                <div
                  key={entry.id}
                  className={clsx(
                    "rounded-lg border p-4",
                    entry.isManuallyEdited
                      ? "border-amber-200 bg-amber-50 dark:border-amber-800 dark:bg-amber-950/30"
                      : "border-border-light bg-border-light/30 dark:border-dark-border dark:bg-dark-border/30"
                  )}
                >
                  <div className="mb-2 flex items-center justify-between">
                    <span className="text-xs font-semibold uppercase text-text-muted dark:text-dark-text-muted">
                      {formatDateOnly(entry.clockIn)}
                    </span>
                    <div className="flex items-center gap-2">
                      {entry.isManuallyEdited && (
                        <span className="rounded-full bg-warning-light px-2 py-0.5 text-[0.625rem] font-bold uppercase text-warning-dark dark:bg-amber-900/50 dark:text-amber-300">
                          Edited
                        </span>
                      )}
                      <span className="font-medium tabular-nums text-text dark:text-dark-text">
                        {formatDuration(entry.durationMinutes)}
                      </span>
                    </div>
                  </div>
                  <div className="flex gap-4 text-[0.8125rem] text-text-muted dark:text-dark-text-muted">
                    <div>
                      <span className="text-xs font-medium uppercase">In:</span>{" "}
                      {formatTimeShort(entry.clockIn)}
                    </div>
                    <div>
                      <span className="text-xs font-medium uppercase">Out:</span>{" "}
                      {entry.clockOut
                        ? formatTimeShort(entry.clockOut)
                        : "—"}
                    </div>
                  </div>
                  {entry.notes && (
                    <p className="mt-2 text-xs text-text-muted dark:text-dark-text-muted">
                      {entry.notes}
                    </p>
                  )}
                </div>
              ))}
            </div>

            {/* Pagination controls (Fix #7) */}
            {historyTotalPages > 1 && (
              <div className="mt-4 flex items-center justify-between text-sm text-text-muted dark:text-dark-text-muted">
                <button
                  onClick={() => fetchHistory(historyPage - 1)}
                  disabled={historyPage <= 1}
                  className="inline-flex items-center gap-1 rounded-md border border-border px-3 py-1.5 text-xs font-medium transition-colors hover:bg-border-light disabled:cursor-not-allowed disabled:opacity-40 dark:border-dark-border dark:hover:bg-dark-border"
                >
                  <ChevronLeft className="h-3.5 w-3.5" /> Prev
                </button>
                <span className="text-xs">Page {historyPage} of {historyTotalPages}</span>
                <button
                  onClick={() => fetchHistory(historyPage + 1)}
                  disabled={historyPage >= historyTotalPages}
                  className="inline-flex items-center gap-1 rounded-md border border-border px-3 py-1.5 text-xs font-medium transition-colors hover:bg-border-light disabled:cursor-not-allowed disabled:opacity-40 dark:border-dark-border dark:hover:bg-dark-border"
                >
                  Next <ChevronRight className="h-3.5 w-3.5" />
                </button>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
