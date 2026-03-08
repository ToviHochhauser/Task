export interface TimeEntry {
  id: number;
  clockIn: string;
  clockOut: string | null;
  durationMinutes: number | null;
  notes: string | null;
  isManuallyEdited: boolean;
}

// Fix #7: Paginated response wrapper
export interface PaginatedResponse<T> {
  items: T[];
  currentPage: number;
  totalPages: number;
  totalCount: number;
  hasNextPage: boolean;
}
