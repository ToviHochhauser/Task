import { useState } from "react";
import api from "../../services/api";
import toast from "react-hot-toast";
import * as Dialog from "@radix-ui/react-dialog";
import { X, Loader2 } from "lucide-react";
import type { TimeEntry } from "../../types";
import { getApiError } from "../../utils/formatters";
import { INPUT_CLASSES } from "./types";

const maxDateTimeLocal = () => new Date().toISOString().substring(0, 16);
const toLocalInputValue = (dateStr: string) => dateStr.substring(0, 16);

interface Props {
  entry: TimeEntry | null;
  onClose: () => void;
  onSaved: () => void;
}

export default function EditEntryModal({ entry, onClose, onSaved }: Props) {
  const [editClockIn, setEditClockIn] = useState("");
  const [editClockOut, setEditClockOut] = useState("");
  const [editNotes, setEditNotes] = useState("");
  const [saving, setSaving] = useState(false);

  // Sync form state when a new entry is opened
  const openEntry = (open: boolean) => {
    if (!open) {
      onClose();
      return;
    }
  };

  // Initialize form when entry changes
  if (entry && editClockIn === "" && !saving) {
    setEditClockIn(toLocalInputValue(entry.clockIn));
    setEditClockOut(entry.clockOut ? toLocalInputValue(entry.clockOut) : "");
    setEditNotes(entry.notes || "");
  }

  const handleSave = async () => {
    if (!entry || saving) return;

    if (editClockIn && editClockOut && new Date(editClockOut) <= new Date(editClockIn)) {
      toast.error("Clock out time must be after clock in time.");
      return;
    }

    setSaving(true);
    try {
      await api.put(`/admin/attendance/${entry.id}`, {
        clockIn: editClockIn || null,
        clockOut: editClockOut || null,
        notes: editNotes,
      });
      toast.success("Entry updated successfully.");
      onClose();
      onSaved();
    } catch (err: unknown) {
      toast.error(getApiError(err, "Failed to update entry."));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Dialog.Root open={!!entry} onOpenChange={openEntry}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-[100] bg-black/40 backdrop-blur-[2px]" />
        <Dialog.Content className="fixed left-1/2 top-1/2 z-[101] w-[calc(100%-2rem)] max-w-[420px] -translate-x-1/2 -translate-y-1/2 rounded-2xl border border-border bg-card p-6 shadow-lg focus:outline-none dark:border-dark-border dark:bg-dark-card sm:p-8">
          <Dialog.Title className="mb-5 text-lg font-bold tracking-tight text-text dark:text-dark-text">
            Edit Time Entry
          </Dialog.Title>

          <div className="flex flex-col gap-4">
            <div>
              <label className="mb-1.5 block text-[0.8125rem] font-semibold text-text dark:text-dark-text">
                Clock In <span className="font-normal text-text-muted dark:text-dark-text-muted">(Zurich time CET/CEST)</span>
              </label>
              <input
                type="datetime-local"
                value={editClockIn}
                max={maxDateTimeLocal()}
                onChange={(e) => setEditClockIn(e.target.value)}
                className={INPUT_CLASSES}
              />
            </div>
            <div>
              <label className="mb-1.5 block text-[0.8125rem] font-semibold text-text dark:text-dark-text">
                Clock Out <span className="font-normal text-text-muted dark:text-dark-text-muted">(Zurich time CET/CEST)</span>
              </label>
              <input
                type="datetime-local"
                value={editClockOut}
                max={maxDateTimeLocal()}
                onChange={(e) => setEditClockOut(e.target.value)}
                className={INPUT_CLASSES}
              />
            </div>
            <div>
              <label className="mb-1.5 block text-[0.8125rem] font-semibold text-text dark:text-dark-text">
                Notes
              </label>
              <input
                type="text"
                value={editNotes}
                onChange={(e) => setEditNotes(e.target.value)}
                placeholder="Reason for edit..."
                maxLength={500}
                className={INPUT_CLASSES}
              />
            </div>
          </div>

          <div className="mt-6 flex gap-3">
            <button
              className="inline-flex items-center gap-2 rounded-lg bg-primary px-5 py-2.5 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-primary-dark disabled:cursor-not-allowed disabled:opacity-55"
              onClick={handleSave}
              disabled={saving}
            >
              {saving && <Loader2 className="h-4 w-4 animate-spin" />}
              {saving ? "Saving..." : "Save"}
            </button>
            <Dialog.Close asChild>
              <button className="rounded-lg border border-border bg-border-light px-5 py-2.5 text-sm font-medium text-text-muted transition-colors hover:bg-border hover:text-text dark:border-dark-border dark:bg-dark-border dark:text-dark-text-muted dark:hover:bg-dark-border/70 dark:hover:text-dark-text">
                Cancel
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
