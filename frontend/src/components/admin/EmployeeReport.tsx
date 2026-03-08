import { useState, useEffect } from "react";
import api from "../../services/api";
import toast from "react-hot-toast";
import * as AlertDialog from "@radix-ui/react-alert-dialog";
import { Pencil, ChevronLeft, ChevronRight, Users, Loader2, AlertTriangle, RotateCcw, Download, Filter } from "lucide-react";
import clsx from "clsx";
import type { TimeEntry } from "../../types";
import { formatDate, formatDateOnly, formatTimeShort, formatDuration, getApiError } from "../../utils/formatters";
import type { EmployeeReport as EmployeeReportType } from "./types";
import { INPUT_CLASSES } from "./types";

interface Props {
  report: EmployeeReportType | null;
  loadingReport: boolean;
  selectedEmployee: number | null;
  reportPage: number;
  dateFrom: string;
  dateTo: string;
  onDateFromChange: (v: string) => void;
  onDateToChange: (v: string) => void;
  onFetchReport: (userId: number, page: number) => void;
  onEditEntry: (entry: TimeEntry) => void;
  onOpenAudit: (entryId: number) => void;
}

const isLongShift = (entry: TimeEntry) =>
  entry.durationMinutes != null && entry.durationMinutes > 720;

export default function EmployeeReport({
  report,
  loadingReport,
  selectedEmployee,
  reportPage,
  dateFrom,
  dateTo,
  onDateFromChange,
  onDateToChange,
  onFetchReport,
  onEditEntry,
  onOpenAudit,
}: Props) {
  const [reopeningEntry, setReopeningEntry] = useState<number | null>(null);
  const [exportingCsv, setExportingCsv] = useState(false);
  const [confirmReopenEntry, setConfirmReopenEntry] = useState<TimeEntry | null>(null);

  // Re-fetch report when date filters change
  useEffect(() => {
    if (selectedEmployee) onFetchReport(selectedEmployee, 1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dateFrom, dateTo]);

  const handleReopen = async (entry: TimeEntry) => {
    setReopeningEntry(entry.id);
    setConfirmReopenEntry(null);
    try {
      await api.post(`/admin/attendance/${entry.id}/reopen`);
      toast.success("Entry reopened. Employee can now clock out.");
      if (selectedEmployee) onFetchReport(selectedEmployee, reportPage);
    } catch (err: unknown) {
      toast.error(getApiError(err, "Failed to reopen entry."));
    } finally {
      setReopeningEntry(null);
    }
  };

  const handleExportCsv = async () => {
    if (!selectedEmployee || exportingCsv) return;
    setExportingCsv(true);
    try {
      const params: Record<string, string> = { format: "csv" };
      if (dateFrom) params.from = dateFrom;
      if (dateTo) params.to = dateTo;
      const response = await api.get(`/admin/reports/${selectedEmployee}`, {
        params,
        responseType: "blob",
      });
      const url = URL.createObjectURL(new Blob([response.data], { type: "text/csv" }));
      const link = document.createElement("a");
      const disposition = response.headers["content-disposition"] ?? "";
      const match = disposition.match(/filename="?([^";\n]+)"?/);
      link.href = url;
      link.download = match?.[1] ?? "report.csv";
      link.click();
      URL.revokeObjectURL(url);
    } catch {
      toast.error("Failed to export CSV.");
    } finally {
      setExportingCsv(false);
    }
  };

  return (
    <div className="rounded-xl border border-border bg-card p-5 shadow-sm sm:p-6 dark:border-dark-border dark:bg-dark-card">
      {loadingReport ? (
        <div className="flex flex-col items-center justify-center py-12">
          <Loader2 className="h-6 w-6 animate-spin text-primary" />
          <p className="mt-2 text-sm text-text-muted dark:text-dark-text-muted">Loading report...</p>
        </div>
      ) : report ? (
        <>
          <h3 className="mb-1 text-[0.9375rem] font-bold text-text dark:text-dark-text">
            {report.fullName} — Attendance Report
          </h3>
          {/* Date range filter */}
          <div className="mb-4 flex flex-wrap items-end gap-3">
            <div className="flex items-center gap-1.5 text-xs font-medium text-text-muted dark:text-dark-text-muted">
              <Filter className="h-3.5 w-3.5" />
              Filter:
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <input
                type="date"
                value={dateFrom}
                onChange={(e) => onDateFromChange(e.target.value)}
                className={INPUT_CLASSES + " !w-auto !py-1.5 text-xs"}
                aria-label="From date"
              />
              <span className="text-xs text-text-muted dark:text-dark-text-muted">to</span>
              <input
                type="date"
                value={dateTo}
                onChange={(e) => onDateToChange(e.target.value)}
                className={INPUT_CLASSES + " !w-auto !py-1.5 text-xs"}
                aria-label="To date"
              />
              {(dateFrom || dateTo) && (
                <button
                  onClick={() => { onDateFromChange(""); onDateToChange(""); }}
                  className="rounded-md px-2 py-1 text-xs font-medium text-text-muted transition-colors hover:bg-border-light hover:text-text dark:text-dark-text-muted dark:hover:bg-dark-border dark:hover:text-dark-text"
                >
                  Clear
                </button>
              )}
            </div>
          </div>
          <div className="mb-5 flex flex-wrap items-center justify-between gap-2">
            <div className="flex flex-wrap items-center gap-4 text-sm text-text-muted dark:text-dark-text-muted">
              <span>
                Total Hours:{" "}
                <strong className="text-base text-primary">{report.totalHours.toFixed(1)}h</strong>
              </span>
              {report.estimatedPay != null && (
                <span>
                  Estimated Pay:{" "}
                  <strong className="text-base text-success">{report.estimatedPay.toFixed(2)}</strong>
                </span>
              )}
              {report.totalCount > 0 && (
                <span className="text-xs">
                  {report.totalCount} {report.totalCount === 1 ? "entry" : "entries"}
                </span>
              )}
            </div>
            <button
              onClick={handleExportCsv}
              disabled={exportingCsv}
              className="inline-flex items-center gap-1.5 rounded-md border border-border bg-border-light px-3 py-1.5 text-xs font-medium text-text-muted transition-colors hover:border-primary hover:text-primary disabled:cursor-not-allowed disabled:opacity-50 dark:border-dark-border dark:bg-dark-border dark:text-dark-text-muted dark:hover:border-primary dark:hover:text-primary-light"
            >
              {exportingCsv ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Download className="h-3.5 w-3.5" />}
              Export CSV
            </button>
          </div>

          {/* Desktop table */}
          <div className="hidden sm:block">
            <table className="w-full border-collapse text-sm">
              <thead>
                <tr>
                  {["Clock In", "Clock Out", "Duration", "Notes", "Actions"].map((h) => (
                    <th
                      key={h}
                      className="border-b-2 border-border px-3 py-2 text-left text-[0.7rem] font-semibold uppercase tracking-wider text-text-muted dark:border-dark-border dark:text-dark-text-muted"
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {report.entries.map((entry) => (
                  <tr
                    key={entry.id}
                    className={clsx(
                      "transition-colors",
                      entry.isManuallyEdited
                        ? "bg-amber-50 hover:bg-amber-100 dark:bg-amber-950/30 dark:hover:bg-amber-950/50"
                        : "hover:bg-primary-xlight dark:hover:bg-dark-border/50"
                    )}
                  >
                    <td className="border-b border-border-light px-3 py-3 dark:border-dark-border">{formatDate(entry.clockIn)}</td>
                    <td className="border-b border-border-light px-3 py-3 dark:border-dark-border">{entry.clockOut ? formatDate(entry.clockOut) : "—"}</td>
                    <td className="border-b border-border-light px-3 py-3 font-medium tabular-nums dark:border-dark-border">
                      {formatDuration(entry.durationMinutes)}
                      {isLongShift(entry) && (
                        <span className="ml-2 inline-flex items-center gap-0.5 rounded-full bg-red-100 px-2 py-0.5 text-[0.625rem] font-bold uppercase tracking-wide text-red-700 dark:bg-red-900/50 dark:text-red-300" title="Shift exceeds 12 hours — review recommended">
                          <AlertTriangle className="h-2.5 w-2.5" />Long
                        </span>
                      )}
                    </td>
                    <td className="border-b border-border-light px-3 py-3 dark:border-dark-border">
                      {entry.notes || ""}
                      {entry.isManuallyEdited && (
                        <button
                          onClick={() => onOpenAudit(entry.id)}
                          title="View edit history"
                          className="ml-2 inline-block rounded-full bg-warning-light px-2 py-0.5 text-[0.625rem] font-bold uppercase tracking-wide text-warning-dark hover:bg-amber-200 dark:bg-amber-900/50 dark:text-amber-300 dark:hover:bg-amber-800/60"
                        >
                          Edited
                        </button>
                      )}
                    </td>
                    <td className="border-b border-border-light px-3 py-3 dark:border-dark-border">
                      <div className="flex items-center gap-1.5">
                        <button
                          className="inline-flex items-center gap-1 rounded-md border border-primary/25 bg-primary-xlight px-2.5 py-1 text-xs font-semibold text-primary-dark transition-colors hover:border-primary hover:bg-primary/10 dark:border-primary/40 dark:bg-primary/10 dark:text-primary-light dark:hover:bg-primary/20"
                          onClick={() => onEditEntry(entry)}
                        >
                          <Pencil className="h-3 w-3" aria-hidden="true" />
                          Edit
                        </button>
                        {entry.clockOut && (
                          <button
                            className="inline-flex items-center gap-1 rounded-md border border-amber-300 bg-amber-50 px-2.5 py-1 text-xs font-semibold text-amber-700 transition-colors hover:border-amber-400 hover:bg-amber-100 disabled:cursor-not-allowed disabled:opacity-50 dark:border-amber-700 dark:bg-amber-950/30 dark:text-amber-300 dark:hover:bg-amber-950/50"
                            onClick={() => setConfirmReopenEntry(entry)}
                            disabled={reopeningEntry === entry.id}
                          >
                            {reopeningEntry === entry.id
                              ? <Loader2 className="h-3 w-3 animate-spin" />
                              : <RotateCcw className="h-3 w-3" aria-hidden="true" />
                            }
                            Reopen
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Mobile card view */}
          <div className="flex flex-col gap-3 sm:hidden">
            {report.entries.map((entry) => (
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
                      <button
                        onClick={() => onOpenAudit(entry.id)}
                        title="View edit history"
                        className="rounded-full bg-warning-light px-2 py-0.5 text-[0.625rem] font-bold uppercase text-warning-dark hover:bg-amber-200 dark:bg-amber-900/50 dark:text-amber-300 dark:hover:bg-amber-800/60"
                      >
                        Edited
                      </button>
                    )}
                    <span className="font-medium tabular-nums">
                      {formatDuration(entry.durationMinutes)}
                      {isLongShift(entry) && (
                        <span title="Long shift"><AlertTriangle className="ml-1 inline h-3 w-3 text-red-500" /></span>
                      )}
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
                    {entry.clockOut ? formatTimeShort(entry.clockOut) : "—"}
                  </div>
                </div>
                {entry.notes && (
                  <p className="mt-1.5 text-xs text-text-muted dark:text-dark-text-muted">{entry.notes}</p>
                )}
                <div className="mt-3 flex gap-2">
                  <button
                    className="inline-flex items-center gap-1 rounded-md border border-primary/25 bg-primary-xlight px-2.5 py-1 text-xs font-semibold text-primary-dark transition-colors hover:border-primary hover:bg-primary/10 dark:border-primary/40 dark:bg-primary/10 dark:text-primary-light dark:hover:bg-primary/20"
                    onClick={() => onEditEntry(entry)}
                  >
                    <Pencil className="h-3 w-3" aria-hidden="true" />
                    Edit
                  </button>
                  {entry.clockOut && (
                    <button
                      className="inline-flex items-center gap-1 rounded-md border border-amber-300 bg-amber-50 px-2.5 py-1 text-xs font-semibold text-amber-700 transition-colors hover:border-amber-400 hover:bg-amber-100 disabled:cursor-not-allowed disabled:opacity-50 dark:border-amber-700 dark:bg-amber-950/30 dark:text-amber-300 dark:hover:bg-amber-950/50"
                      onClick={() => setConfirmReopenEntry(entry)}
                      disabled={reopeningEntry === entry.id}
                    >
                      {reopeningEntry === entry.id
                        ? <Loader2 className="h-3 w-3 animate-spin" />
                        : <RotateCcw className="h-3 w-3" aria-hidden="true" />
                      }
                      Reopen
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>

          {/* Pagination */}
          {report.totalPages > 1 && (
            <div className="mt-4 flex items-center justify-between text-sm text-text-muted dark:text-dark-text-muted">
              <button
                onClick={() => onFetchReport(report.userId, reportPage - 1)}
                disabled={reportPage <= 1}
                className="inline-flex items-center gap-1 rounded-md border border-border px-3 py-1.5 text-xs font-medium transition-colors hover:bg-border-light disabled:cursor-not-allowed disabled:opacity-40 dark:border-dark-border dark:hover:bg-dark-border"
              >
                <ChevronLeft className="h-3.5 w-3.5" /> Prev
              </button>
              <span className="text-xs">Page {report.currentPage} of {report.totalPages}</span>
              <button
                onClick={() => onFetchReport(report.userId, reportPage + 1)}
                disabled={!report.hasNextPage}
                className="inline-flex items-center gap-1 rounded-md border border-border px-3 py-1.5 text-xs font-medium transition-colors hover:bg-border-light disabled:cursor-not-allowed disabled:opacity-40 dark:border-dark-border dark:hover:bg-dark-border"
              >
                Next <ChevronRight className="h-3.5 w-3.5" />
              </button>
            </div>
          )}
        </>
      ) : (
        <div className="flex flex-col items-center justify-center py-12 text-center">
          <Users className="mb-3 h-10 w-10 text-text-muted/40 dark:text-dark-text-muted/40" />
          <p className="text-sm text-text-muted dark:text-dark-text-muted">
            Select an employee to view their report.
          </p>
        </div>
      )}

      {/* Reopen confirmation dialog */}
      <AlertDialog.Root
        open={confirmReopenEntry !== null}
        onOpenChange={(open) => !open && setConfirmReopenEntry(null)}
      >
        <AlertDialog.Portal>
          <AlertDialog.Overlay className="fixed inset-0 z-[100] bg-black/40 backdrop-blur-[2px]" />
          <AlertDialog.Content className="fixed left-1/2 top-1/2 z-[101] w-[calc(100%-2rem)] max-w-[400px] -translate-x-1/2 -translate-y-1/2 rounded-2xl border border-border bg-card p-6 shadow-lg focus:outline-none dark:border-dark-border dark:bg-dark-card">
            <AlertDialog.Title className="mb-2 text-base font-bold text-text dark:text-dark-text">
              Reopen Time Entry?
            </AlertDialog.Title>
            <AlertDialog.Description className="mb-5 text-sm text-text-muted dark:text-dark-text-muted">
              {confirmReopenEntry && (
                <>Entry from {formatDate(confirmReopenEntry.clockIn)} will be reopened. The employee will need to clock out again.</>
              )}
            </AlertDialog.Description>
            <div className="flex justify-end gap-3">
              <AlertDialog.Cancel asChild>
                <button className="rounded-lg border border-border bg-border-light px-4 py-2 text-sm font-medium text-text-muted transition-colors hover:bg-border hover:text-text dark:border-dark-border dark:bg-dark-border dark:text-dark-text-muted dark:hover:bg-dark-border/70">
                  Cancel
                </button>
              </AlertDialog.Cancel>
              <AlertDialog.Action asChild>
                <button
                  className="rounded-lg bg-amber-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-amber-700"
                  onClick={() => confirmReopenEntry && handleReopen(confirmReopenEntry)}
                >
                  Reopen
                </button>
              </AlertDialog.Action>
            </div>
          </AlertDialog.Content>
        </AlertDialog.Portal>
      </AlertDialog.Root>
    </div>
  );
}
