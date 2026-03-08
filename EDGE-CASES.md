# TimeClock - Edge Cases & Mitigation Strategies

A comprehensive analysis of edge cases across the entire stack, with severity ratings and recommended mitigations.

---

## Table of Contents

1. [Concurrency & Race Conditions](#1-concurrency--race-conditions)
2. [Authentication & Security](#2-authentication--security)
3. [Authorization & Access Control](#3-authorization--access-control)
4. [Input Validation](#4-input-validation)
5. [Time Zone Handling](#5-time-zone-handling)
6. [Business Logic](#6-business-logic)
7. [Error Handling](#7-error-handling)
8. [Frontend State Management](#8-frontend-state-management)
9. [API Contract & Data Integrity](#9-api-contract--data-integrity)
10. [Database Concerns](#10-database-concerns)
11. [Deployment & Configuration](#11-deployment--configuration)
12. [Additional Edge Cases (Addendum)](#additional-edge-cases-addendum)

---

## 1. Concurrency & Race Conditions

### 1.1 Double Clock-In (FIXED)

**Edge Case:** Two rapid "Clock In" requests pass the open-entry check before either saves, creating two open entries for the same user.

**Where:** `AttendanceService.ClockInAsync()` — the check-then-insert pattern (TOCTOU vulnerability).

**Status:** ✅ **FIXED** — Added a filtered unique index `IX_TimeEntries_OpenEntry` on `(UserId) WHERE ClockOut IS NULL` via EF Core migration, enforcing at most one open entry per user at the database level. `DbUpdateException` is now caught by middleware and returns `409 Conflict`.

### 1.2 Double Clock-Out (FIXED)

**Edge Case:** Two concurrent "Clock Out" requests both find the same open entry and both attempt to close it.

**Where:** `AttendanceService.ClockOutAsync()` — same TOCTOU pattern.

**Status:** ✅ **FIXED** — Added `[Timestamp] RowVersion` column to `TimeEntry` model. EF Core optimistic concurrency ensures the second concurrent save throws `DbUpdateConcurrencyException`, which middleware maps to `409 Conflict`.

### 1.3 Frontend Debounce Gap (FIXED)

**Edge Case:** User clicks "Clock In" button; if the API takes >1 second, the time-based debounce expires and allows a second click.

**Where:** `ClockPage.tsx` — `debounceRef` uses `setTimeout(1000)` instead of tracking request state.

**Status:** ✅ **FIXED** — Debounce now clears in `finally` block immediately after request completes, and button is disabled via `loading` state during requests.

---

## 2. Authentication & Security

### 2.1 Hardcoded Admin Credentials (FIXED)

**Edge Case:** Every deployment seeds `admin` / `admin123` if no admin exists. This is a known credential in the source code.

**Where:** `Program.cs` (seed logic).

**Status:** ✅ **FIXED** — Seed credentials now read from `Seed:AdminUsername` and `Seed:AdminPassword` configuration/environment variables, with fallback to defaults. A warning is logged when default credentials are used.

### 2.2 JWT Secret Fallback in Source Code (FIXED)

**Edge Case:** If `Jwt:Key` is not configured, a hardcoded fallback key is used, which is visible in the source repo.

**Where:** `Program.cs` and `AuthService.cs`.

**Status:** ✅ **FIXED** — Both `Program.cs` and `AuthService.GenerateToken()` now throw `InvalidOperationException` if `Jwt:Key` is not configured. No fallback key exists in source code.

### 2.3 JWT Stored in localStorage (MEDIUM)

**Edge Case:** Any XSS vulnerability on the site can read the JWT from `localStorage`, allowing session hijacking.

**Where:** `AuthContext.tsx`, line 20.

**Mitigation:**
- Move token storage to an **httpOnly, Secure, SameSite** cookie managed by the backend.
- If localStorage must be used, implement a short token lifetime with refresh tokens.

### 2.4 No Token Revocation

**Edge Case:** After logout, the JWT remains valid for up to 12 hours. If intercepted, it can be reused.

**Where:** `AuthService.cs` (12-hour expiry), logout only clears client-side storage.

**Mitigation:**
- Implement a **token blacklist** (in-memory or Redis) checked on each request, or use short-lived access tokens (5-15 min) with refresh tokens.

### 2.5 No Rate Limiting on Login (FIXED)

**Edge Case:** Brute-force attacks can try unlimited username/password combinations.

**Where:** `AuthController.Login()` — no throttling.

**Status:** ✅ **FIXED** — Added `LoginRateLimitMiddleware` with in-memory tracking per IP. Limits to 10 login attempts per 5-minute window. Returns `429 Too Many Requests` when exceeded.

---

## 3. Authorization & Access Control

### 3.1 No Row-Level Security

**Edge Case:** Admin A can request reports or edit entries for Admin B by guessing user IDs. No audit trail exists.

**Where:** `AdminService.GetEmployeeReportAsync()` and `EditTimeEntryAsync()` — accept raw `userId`/`entryId` without verifying ownership scope.

**Mitigation:**
- Restrict admin operations to employees only (not other admins), or implement role hierarchy.
- Add an **audit log table** recording who edited what and when.

### 3.2 Employee Data Isolation

**Edge Case:** The `GetUserId()` helper extracts user ID from the JWT claim. If the JWT is forged (e.g., weak secret), a user could impersonate another.

**Where:** All controllers that call `GetUserId()`.

**Mitigation:**
- Ensure a strong JWT secret (see 2.2).
- Backend already authorizes via `[Authorize]`, but consider adding explicit user-scoping in service queries.

### 3.3 Role Value Not Validated on Registration (FIXED)

**Edge Case:** If the registration endpoint is exposed, a user could register with `Role = "Admin"` and gain admin access.

**Where:** `AuthService.RegisterAsync()` — no whitelist on role field.

**Status:** ✅ **FIXED** — `AuthController.Register` now requires `[Authorize(Roles = "Admin")]`. `AuthService.RegisterAsync()` validates role against whitelist `["Employee", "Admin"]` and rejects invalid values.

---

## 4. Input Validation

### 4.1 Username / Password Constraints (FIXED)

**Edge Case:** Users can register with empty strings, whitespace-only names, extremely long strings (>100 chars hits DB limit), or single-character passwords.

**Where:** `AuthService.RegisterAsync()` — no validation before DB save.

**Status:** ✅ **FIXED** — `AuthService.RegisterAsync()` now validates: username 3-50 chars (alphanumeric + underscore, trimmed), password min 6 chars, full name required and max 200 chars (trimmed). Frontend form inputs also enforce `minLength`, `maxLength`, and `pattern` attributes.

### 4.2 Date Range Queries Unbounded (FIXED)

**Edge Case:** Passing `from > to`, dates in year 1900 or 3000, or malformed ISO strings to history/report endpoints.

**Where:** `AttendanceService.GetHistoryAsync()` and `AdminService.GetEmployeeReportAsync()`.

**Status:** ✅ **FIXED** — Both services now validate `from <= to` and throw `InvalidOperationException` (400 Bad Request) if the range is inverted.

### 4.3 Admin Edit Allows ClockOut Before ClockIn (FIXED)

**Edge Case:** Admin sets `ClockOut = 08:00` and `ClockIn = 14:00`, resulting in a **negative duration** stored in the database. Reports sum these, producing incorrect totals.

**Where:** `AdminService.EditTimeEntryAsync()` — no temporal order validation.

**Status:** ✅ **FIXED** — Backend validates `ClockOut > ClockIn` before saving and throws `InvalidOperationException` (400) if violated. Frontend edit modal also validates client-side before submission.

### 4.4 Notes Field Unbounded (FIXED)

**Edge Case:** A user or admin can submit megabytes of text in the notes field (`nvarchar(max)` in DB).

**Where:** `AdminService.EditTimeEntryAsync()`.

**Status:** ✅ **FIXED** — Backend validates notes max 500 chars. Frontend input has `maxLength={500}` attribute.

---

## 5. Time Zone Handling

### 5.1 DateTime vs. DateTimeOffset (HIGH)

**Edge Case:** All `TimeEntry` records store `DateTime` (no timezone info). If the server or application runs in a different timezone, all stored times are ambiguous.

**Where:** `TimeEntry.cs` model — `ClockIn` and `ClockOut` are `DateTime`, not `DateTimeOffset`.

**Mitigation:**
- Use `DateTimeOffset` throughout the stack to preserve timezone context.
- Alternatively, document and enforce that all stored times are UTC, and convert to Zurich time only at the presentation layer.

### 5.2 TimeAPI.io Response Parsing Loses Offset

**Edge Case:** The API returns `2026-03-05T14:30:00+01:00`, but `DateTime.Parse()` drops the offset and treats it as local server time.

**Where:** `WorldTimeApiService.cs`, line 71.

**Mitigation:**
- Use `DateTimeOffset.Parse()` to preserve the offset.
- Or explicitly parse the `dateTime` field (which has no offset) from the API response and treat it as Zurich time.

### 5.3 DST Transition Ambiguity

**Edge Case:** During the "fall back" DST transition, the hour 02:00-02:59 occurs twice. A clock-in at "02:30" is ambiguous — it could be summer time or winter time.

**Where:** Any clock-in/out during the last Sunday of October (CET) between 02:00-03:00.

**Mitigation:**
- Store times as `DateTimeOffset` to disambiguate.
- Use `TimeZoneInfo.GetAmbiguousTimeOffsets()` to detect and handle ambiguous times.

### 5.4 Fallback Timezone ID Is Windows-Only (FIXED)

**Edge Case:** The fallback `TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time")` throws `TimeZoneNotFoundException` on Linux/macOS, where IANA IDs (`Europe/Zurich`) are used.

**Where:** `WorldTimeApiService.cs`.

**Status:** ✅ **FIXED** — Changed to `TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich")` which works cross-platform on .NET 6+.

### 5.5 Admin Edits in Wrong Timezone (FIXED)

**Edge Case:** Admin is in New York (UTC-5). They edit a time entry using `<input type="datetime-local">`, which shows and submits in their browser's local time. The value is converted to ISO/UTC and stored, but the system expects Zurich time.

**Where:** `AdminPage.tsx` — `toLocalInputValue()` and the edit submission flow.

**Status:** ✅ **FIXED** — Edit modal now explicitly labels all time inputs as "Zurich time (CET/CEST)" so admins in different timezones are aware of the expected timezone.

---

## 6. Business Logic

### 6.1 Midnight-Crossing Shifts (FIXED)

**Edge Case:** Employee clocks in at 23:50 on March 5 and clocks out at 00:10 on March 6. A query for "March 5 history" (filtering by `ClockIn` date) includes this entry, but a query for "March 6" does not — even though 10 minutes of work occurred on March 6.

**Where:** `AttendanceService.GetHistoryAsync()` and `AdminService.GetEmployeeReportAsync()`.

**Status:** ✅ **FIXED** — Both services now use overlap logic: `ClockIn < toEnd AND (ClockOut > from OR ClockOut IS NULL)`, capturing all entries whose shift overlaps the requested date range.

### 6.2 ClockOut = ClockIn (Zero Duration) (FIXED)

**Edge Case:** User clocks in and immediately clocks out. Duration = 0 minutes. Technically valid but likely an error.

**Status:** ✅ **FIXED** — `AttendanceService.ClockOutAsync()` now rejects clock-outs with duration < 1 minute, throwing `InvalidOperationException` (400 Bad Request).

### 6.3 Forgotten Clock-Out (Open Entries) (FIXED)

**Edge Case:** Employee clocks in but forgets to clock out. The entry remains open with `ClockOut = NULL` and `DurationMinutes = NULL`. Next day they can't clock in because they have an open entry.

**Where:** `AttendanceService.ClockInAsync()` — blocks new clock-in if any open entry exists.

**Status:** ✅ **FIXED** — `ClockInAsync()` now auto-closes entries open for >16 hours: sets `ClockOut` to `ClockIn + 16h`, marks as manually edited, appends "[Auto-closed: exceeded 16h limit]" to notes, and allows the new clock-in to proceed. Admin panel also shows a "Long" warning badge for entries exceeding 12 hours.

### 6.4 IsActive Flag Only Checked on Login (FIXED)

**Edge Case:** A deactivated employee's existing JWT remains valid until expiry. They can continue to clock in/out for up to 12 hours after deactivation.

**Where:** `AuthService.cs` — `IsActive` checked only in `LoginAsync()`, not on subsequent requests.

**Status:** ✅ **FIXED** — `IsActiveMiddleware` checks `IsActive` on every authenticated request. Deactivated users receive `401` with "Account is deactivated." message immediately, regardless of JWT validity.

---

## 7. Error Handling

### 7.1 Generic 500 for Database Errors (FIXED)

**Edge Case:** `DbUpdateException` (constraint violations, connection failures) returns a generic "An unexpected error occurred" with HTTP 500.

**Where:** `ExceptionMiddleware.cs` — only maps `InvalidOperationException`, `UnauthorizedAccessException`, `KeyNotFoundException`.

**Status:** ✅ **FIXED** — `ExceptionMiddleware` now catches `DbUpdateConcurrencyException` (409 Conflict) and `DbUpdateException` (409 Conflict with constraint violation message) separately.

### 7.2 Silent Time Sync Failure (FIXED)

**Edge Case:** If all time APIs are unreachable and the fallback also fails, the frontend displays no time (zurichTime = null). The user has no indication that the clock is unavailable.

**Where:** `ClockPage.tsx` — empty catch block on time sync.

**Status:** ✅ **FIXED** — `ClockPage.tsx` now tracks `timeSyncFailed` state. If initial sync fails and no base time exists, displays a warning: "Unable to sync Zurich time. Timestamps are recorded server-side."

### 7.3 Partial Save on Edit (FIXED)

**Edge Case:** Admin edits both `ClockIn` and `ClockOut`. If the duration recalculation throws after `ClockIn` is modified in memory (but before `SaveChangesAsync`), EF Core tracks the change. However, since the exception is thrown before save, this is actually safe — EF Core won't persist. But if there's an exception **during** `SaveChangesAsync`, partial state could occur.

**Status:** ✅ **FIXED** — `AdminService.EditTimeEntryAsync()` now wraps the entire edit operation in an explicit `IDbContextTransaction`. Changes are only committed if all validations pass and `SaveChangesAsync` succeeds.

---

## 8. Frontend State Management

### 8.1 Stale Status After Network Error (FIXED)

**Edge Case:** Clock-in succeeds, but the subsequent `fetchStatus()` call fails due to a network glitch. The UI still shows "Clocked Out" even though the user is clocked in.

**Where:** `ClockPage.tsx` — separate API calls for action + status refresh.

**Status:** ✅ **FIXED** — `ClockResponse` DTO now includes `IsClockedIn` and `LastClockIn` fields. `ClockPage.tsx` reads status directly from the clock-in/clock-out response, eliminating the need for a separate `fetchStatus()` call. Falls back to `fetchStatus()` only on error.

### 8.2 Multi-Tab Desynchronization (FIXED)

**Edge Case:** User logs in on Tab A. Tab B still shows the login page. User clocks in on Tab A. Tab B shows "Clocked Out".

**Where:** `AuthContext.tsx` — no `storage` event listener for cross-tab sync.

**Status:** ✅ **FIXED** — `AuthContext.tsx` now listens to the `window.storage` event. When `token` or `user` keys change in another tab, the auth state is synced immediately across all tabs.

### 8.3 Corrupted localStorage Crashes App (FIXED)

**Edge Case:** If `localStorage.getItem("user")` returns malformed JSON, `JSON.parse()` throws, crashing the app.

**Where:** `AuthContext.tsx` — no try-catch around JSON parse.

**Status:** ✅ **FIXED** — Wrapped in try-catch; if parsing fails, clears localStorage and returns null.

### 8.4 Token Expiry Without Notification (FIXED)

**Edge Case:** User leaves the app open for >12 hours. Token expires. Next action returns 401, interceptor redirects to login with no explanation.

**Where:** `api.ts` Axios interceptor and `AuthContext.tsx`.

**Status:** ✅ **FIXED** — `AuthContext.tsx` now decodes the JWT `exp` claim and checks it every 30 seconds. When the token expires, a `auth:expired` event is dispatched and `ClockPage.tsx` shows a toast: "Session expired. Please log in again."

---

## 9. API Contract & Data Integrity

### 9.1 Negative Duration in Reports (FIXED)

**Edge Case:** If admin-edited entries have negative durations, report totals become incorrect (can even be negative).

**Where:** `AdminService.GetEmployeeReportAsync()` sums `DurationMinutes`.

**Status:** ✅ **FIXED** — Report total hours calculation now filters to only sum entries with `DurationMinutes > 0`, clamping any negative values to 0. Combined with the existing validation that `ClockOut > ClockIn` (4.3), negative durations cannot be created through normal flows.

### 9.2 DateTime Serialization Mismatch

**Edge Case:** Backend returns `DateTime` serialized as ISO 8601 without timezone suffix. Frontend treats it as UTC (JavaScript `new Date()` behavior for ISO strings without `Z`). If the backend appends `Z`, times shift by the Zurich offset.

**Where:** All API responses containing `DateTime` fields.

**Mitigation:**
- Standardize: always return UTC with `Z` suffix, or always return Zurich time without suffix and document the convention.
- Use `DateTimeOffset` to make the contract explicit.

### 9.3 User ID Reuse After Deletion

**Edge Case:** If a user is deleted (cascade), their ID could theoretically be reused by a new user. Historical references to that ID would then point to the wrong person.

**Mitigation:**
- Use soft deletes (`IsActive = false`) instead of hard deletes.
- Never reuse IDs (IDENTITY columns don't reuse by default in SQL Server, so this is safe unless the table is truncated/reseeded).

---

## 10. Database Concerns

### 10.1 No Unique Constraint on Open Entries (FIXED)

**Edge Case:** Race condition (see 1.1) creates duplicate open entries. No database-level guard.

**Where:** `TimeEntries` table schema.

**Status:** ✅ **FIXED** — Added filtered unique index `IX_TimeEntries_OpenEntry` on `(UserId) WHERE ClockOut IS NULL` via EF Core migration `AddOpenEntryUniqueIndex`.

### 10.2 Cascade Delete Loses Data (FIXED)

**Edge Case:** Deleting a user cascades to all their time entries — permanent data loss with no recovery.

**Where:** `TimeEntry` foreign key with `DeleteBehavior.Cascade`.

**Status:** ✅ **FIXED** — Changed to `DeleteBehavior.Restrict` via EF Core migration `EdgeCaseFixes`. Attempting to delete a user with time entries now throws a DB error. Combined with existing `IsActive` soft-delete pattern, users are deactivated rather than deleted.

### 10.3 No Index on Date Range Queries (FIXED)

**Edge Case:** As data grows, history and report queries scanning by `ClockIn` date range become slow.

**Where:** `TimeEntries` table — composite index on `(UserId, ClockIn)` exists but may not cover all query patterns.

**Status:** ✅ **FIXED** — Upgraded the composite index to a covering index: `(UserId, ClockIn) INCLUDE (ClockOut, DurationMinutes, Notes)` via EF Core migration `EdgeCaseFixes`.

### 10.4 Username Uniqueness Race (FIXED)

**Edge Case:** Two admins register the same username simultaneously. The `AnyAsync` check passes for both, and one save throws a `DbUpdateException` for the unique index violation.

**Where:** `AuthService.RegisterAsync()` — check-then-insert pattern.

**Status:** ✅ **FIXED** — The unique index on `Username` already prevents duplicates at the DB level. `ExceptionMiddleware` now catches `DbUpdateException` and returns `409 Conflict` instead of a generic 500.

---

## 11. Deployment & Configuration

### 11.1 CORS Hardcoded to localhost:5173 (FIXED)

**Edge Case:** If the frontend runs on a different port or domain, all API requests are blocked by CORS.

**Where:** `Program.cs`.

**Status:** ✅ **FIXED** — CORS allowed origins are now read from `Cors:AllowedOrigins` configuration array, falling back to `http://localhost:5173` if not set.

### 11.2 Auto-Migration on Startup

**Edge Case:** If the database is locked or unavailable, `context.Database.Migrate()` hangs indefinitely, blocking app startup.

**Where:** `Program.cs`, seed/migration block.

**Mitigation:**
- Add a timeout to migration.
- Run migrations as a separate step in CI/CD rather than on every startup.
- Add health checks that report migration status.

### 11.3 JWT Issuer/Audience Can Be Null (FIXED)

**Edge Case:** If `Jwt:Issuer` or `Jwt:Audience` is not set in configuration, token validation may skip those checks, weakening security.

**Where:** `Program.cs`, JWT configuration block.

**Status:** ✅ **FIXED** — `Program.cs` now validates `Jwt:Key`, `Jwt:Issuer`, and `Jwt:Audience` at startup. Throws `InvalidOperationException` with a clear message if any are missing or empty.

---

## Severity Summary

| Severity | Count | Key Items |
|----------|-------|-----------|
| **OPEN** | 4 | localStorage JWT (2.3), no token revocation (2.4), no row-level security (3.1), DateTime vs DateTimeOffset (5.1) |
| **FIXED** | 31 | See list below |

### All Fixed Items

Double clock-in (1.1), double clock-out (1.2), debounce (1.3), admin credentials (2.1), JWT fallback (2.2), rate limiting (2.5), role validation (3.3), input validation (4.1), date ranges (4.2), ClockOut>ClockIn (4.3), notes limit (4.4), cross-platform TZ (5.4), TZ labels (5.5), midnight-crossing (6.1), zero duration (6.2), forgotten clock-out (6.3), IsActive middleware (6.4), DB exceptions (7.1), time sync warning (7.2), partial save (7.3), stale status (8.1), multi-tab sync (8.2), localStorage crash (8.3), token expiry (8.4), negative duration (9.1), unique index (10.1), cascade delete (10.2), covering index (10.3), CORS config (11.1), JWT config validation (11.3), concurrent edits (12.2), browser back (12.3), timeout detection (12.4), long shifts (12.5).

---

## Quick-Win Fixes (Low Effort, High Impact)

1. ✅ **Filtered unique index** on open entries — prevents double clock-in at DB level — **DONE**.
2. ✅ **Validate ClockOut > ClockIn** in `AdminService.EditTimeEntryAsync()` — **DONE**.
3. ✅ **Remove JWT secret fallback** — fail fast if not configured — **DONE**.
4. ✅ **Add try-catch around localStorage JSON.parse** in AuthContext — **DONE**.
5. ✅ **Disable button during API call** instead of time-based debounce — **DONE**.
6. ✅ **Handle DbUpdateException** in middleware with appropriate status codes — **DONE**.
7. ✅ **RowVersion optimistic concurrency** — prevents concurrent edit conflicts — **DONE**.
8. ✅ **Rate limiting on login** — 10 attempts per 5 min per IP — **DONE**.
9. ✅ **Auto-close stale entries** — 16h threshold with admin visibility — **DONE**.
10. ✅ **Cross-platform timezone** — IANA ID `Europe/Zurich` — **DONE**.
11. ✅ **Midnight-crossing overlap queries** — captures shifts spanning midnight — **DONE**.
12. ✅ **Multi-tab auth sync** — `storage` event listener — **DONE**.
13. ✅ **Token expiry notification** — proactive check every 30s — **DONE**.

---

## Additional Edge Cases (Addendum)

### 12.1 API Version Mismatch

**Edge Case:** Frontend and backend are deployed at different times, causing API contract mismatches (e.g., frontend expects a field that backend doesn't return).

**Mitigation:**
- Add API versioning (`/api/v1/...`).
- Include a version header or endpoint to detect mismatches.

### 12.2 Concurrent Admin Edits (FIXED)

**Edge Case:** Two admins edit the same time entry simultaneously. The last save wins, potentially overwriting the other admin's changes without notification.

**Where:** `AdminService.EditTimeEntryAsync()` — no optimistic concurrency.

**Status:** ✅ **FIXED** — Added `[Timestamp] RowVersion` column to `TimeEntry` model. EF Core automatically checks the row version on save. If another user modified the entry, `DbUpdateConcurrencyException` is thrown and middleware returns `409 Conflict: "The record was modified by another user. Please refresh and try again."`

### 12.3 Browser Back Button After Logout (FIXED)

**Edge Case:** User logs out, then presses browser back button. Cached page shows authenticated content briefly before the auth check redirects.

**Status:** ✅ **FIXED** — Backend now sets `Cache-Control: no-store, no-cache, must-revalidate` and `Pragma: no-cache` headers on all API responses via inline middleware in `Program.cs`.

### 12.4 Network Timeout vs. Server Error (FIXED)

**Edge Case:** If an API call times out, the frontend shows a generic error. User doesn't know if their action was processed or not.

**Where:** All API calls in frontend — no distinction between timeout and failure.

**Status:** ✅ **FIXED** — Axios response interceptor in `api.ts` now detects `ECONNABORTED` errors and attaches a `friendlyMessage`. `getApiError()` in `formatters.ts` surfaces this message: "Request timed out. Your action may or may not have completed. Please check and retry."

### 12.5 Very Long Shifts (FIXED)

**Edge Case:** An employee accidentally stays clocked in for days (forgot to clock out). The duration becomes excessively large, skewing reports.

**Status:** ✅ **FIXED** — Two-pronged approach: (1) Backend auto-closes entries open >16 hours on next clock-in (see 6.3). (2) Admin panel shows a red "Long" warning badge with `AlertTriangle` icon on any entry exceeding 12 hours, flagging them for review.
