import * as Dialog from "@radix-ui/react-dialog";
import { X, Loader2 } from "lucide-react";
import type { AuditLog } from "./types";

interface Props {
  entryId: number | null;
  auditLogs: AuditLog[];
  loading: boolean;
  onClose: () => void;
}

export default function AuditModal({ entryId, auditLogs, loading, onClose }: Props) {
  return (
    <Dialog.Root open={entryId !== null} onOpenChange={(open) => !open && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-[100] bg-black/40 backdrop-blur-[2px]" />
        <Dialog.Content className="fixed left-1/2 top-1/2 z-[101] w-[calc(100%-2rem)] max-w-[560px] -translate-x-1/2 -translate-y-1/2 rounded-2xl border border-border bg-card p-6 shadow-lg focus:outline-none dark:border-dark-border dark:bg-dark-card sm:p-8">
          <Dialog.Title className="mb-5 text-lg font-bold tracking-tight text-text dark:text-dark-text">
            Edit History
          </Dialog.Title>

          {loading ? (
            <div className="flex justify-center py-8">
              <Loader2 className="h-6 w-6 animate-spin text-primary" />
            </div>
          ) : auditLogs.length === 0 ? (
            <p className="py-6 text-center text-sm text-text-muted dark:text-dark-text-muted">
              No audit records found for this entry.
            </p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full border-collapse text-xs">
                <thead>
                  <tr>
                    {["When", "By", "Field", "Old Value", "New Value"].map((h) => (
                      <th key={h} className="border-b-2 border-border px-2 py-1.5 text-left text-[0.65rem] font-semibold uppercase tracking-wider text-text-muted dark:border-dark-border dark:text-dark-text-muted">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {auditLogs.map((log) => (
                    <tr key={log.id} className="hover:bg-primary-xlight dark:hover:bg-dark-border/50">
                      <td className="border-b border-border-light px-2 py-2 tabular-nums dark:border-dark-border">
                        {new Date(log.changedAt).toLocaleString("en-CH")}
                      </td>
                      <td className="border-b border-border-light px-2 py-2 dark:border-dark-border">{log.changedByUserName}</td>
                      <td className="border-b border-border-light px-2 py-2 font-mono dark:border-dark-border">{log.fieldName}</td>
                      <td className="border-b border-border-light px-2 py-2 text-danger dark:border-dark-border dark:text-red-400">
                        {log.oldValue ?? <span className="italic text-text-muted">null</span>}
                      </td>
                      <td className="border-b border-border-light px-2 py-2 text-success dark:border-dark-border">
                        {log.newValue ?? <span className="italic text-text-muted">null</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <div className="mt-6 flex justify-end">
            <Dialog.Close asChild>
              <button className="rounded-lg border border-border bg-border-light px-5 py-2.5 text-sm font-medium text-text-muted transition-colors hover:bg-border hover:text-text dark:border-dark-border dark:bg-dark-border dark:text-dark-text-muted dark:hover:bg-dark-border/70 dark:hover:text-dark-text">
                Close
              </button>
            </Dialog.Close>
          </div>

          <Dialog.Close asChild>
            <button
              className="absolute right-4 top-4 rounded-md p-1 text-text-muted transition-colors hover:bg-border-light hover:text-text dark:text-dark-text-muted dark:hover:bg-dark-border dark:hover:text-dark-text"
              aria-label="Close"
            >
              <X className="h-4 w-4" />
            </button>
          </Dialog.Close>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
