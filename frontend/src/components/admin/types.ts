import type { TimeEntry } from "../../types";

export interface Employee {
  id: number;
  username: string;
  fullName: string;
  role: string;
  isActive: boolean;
  hourlyRate?: number | null;
}

export interface EmployeeReport {
  userId: number;
  fullName: string;
  entries: TimeEntry[];
  totalHours: number;
  currentPage: number;
  totalPages: number;
  totalCount: number;
  hasNextPage: boolean;
  hourlyRate?: number | null;
  estimatedPay?: number | null;
}

export interface AuditLog {
  id: number;
  changedByUserName: string;
  changedAt: string;
  fieldName: string;
  oldValue: string | null;
  newValue: string | null;
}

export const INPUT_CLASSES =
  "w-full rounded-lg border-[1.5px] border-border bg-white px-3 py-2 text-sm text-text outline-none transition-all placeholder:text-gray-400 focus:border-primary focus:ring-2 focus:ring-primary/15 dark:border-dark-border dark:bg-dark-bg dark:text-dark-text dark:placeholder:text-gray-500 dark:focus:border-primary dark:focus:ring-primary/25";
