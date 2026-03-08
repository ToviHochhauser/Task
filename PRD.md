# Product Requirements Document (PRD)
## TimeClock — Full-Stack Attendance Management System

| Field | Value |
|---|---|
| **Document Version** | 1.0 |
| **Status** | Final Draft |
| **Date** | March 8, 2026 |
| **Owner** | Product Management |
| **Stack** | ASP.NET Core 9 · React 19 · TypeScript · SQL Server |

---

## Table of Contents

1. [Product Vision & Executive Summary](#1-product-vision--executive-summary)
2. [Target Audience & User Personas](#2-target-audience--user-personas)
3. [Functional Requirements — User Stories](#3-functional-requirements--user-stories)
4. [Non-Functional Requirements](#4-non-functional-requirements)
5. [User Interface Requirements](#5-user-interface-requirements)
6. [Data Model Overview](#6-data-model-overview)
7. [Success Metrics & Acceptance Criteria](#7-success-metrics--acceptance-criteria)
8. [Out of Scope](#8-out-of-scope)
9. [Risks & Mitigations](#9-risks--mitigations)
10. [Glossary](#10-glossary)

---

## 1. Product Vision & Executive Summary

### 1.1 Problem Statement

Conventional attendance systems delegate timekeeping to the client — the employee's browser or device. This creates three critical failure modes:

| Failure Mode | Consequence |
|---|---|
| **Time Theft / Manipulation** | Employees can adjust local device clocks to inflate shift durations or backdate clock-ins. |
| **Timezone Drift** | Inconsistent local timezone settings produce timestamps incompatible across a distributed workforce. |
| **Clock Skew** | Devices that are out of sync produce timestamps seconds or minutes apart from the true time, compounding reporting inaccuracies. |

Together, these failures erode payroll integrity and create compliance exposure, particularly in regulated industries where accurate timekeeping is a legal requirement.

### 1.2 Solution

**TimeClock** solves the root cause by externalising timekeeping entirely. Every clock-in and clock-out timestamp is **generated server-side** using a live, authoritative Zurich time source (Europe/Zurich). The client never sends a timestamp — it sends only an intent ("clock in"). The server decides the time.

```
Employee clicks "Clock In"
        │
        ▼
POST /api/attendance/clock-in  ← no timestamp in request body
        │
        ▼
Server fetches authoritative time via fallback chain:
  1. timeapi.io (primary)
  2. worldtimeapi.org (secondary)
  3. Server-side TimeZoneInfo (always available)
        │
        ▼
TimeEntry { ClockIn = zurichNow } → persisted to SQL Server
```

The result is an attendance record that is:
- **Tamper-proof** — no client-supplied timestamp can alter it
- **Timezone-consistent** — all records share Europe/Zurich regardless of device locale
- **Auditable** — every admin correction is logged with full before/after values

### 1.3 Key Value Proposition

> "TimeClock is the single source of truth for when work began and ended — owned by the server, not the employee's device."

---

## 2. Target Audience & User Personas

### 2.1 Persona A — The Employee

| Attribute | Detail |
|---|---|
| **Name** | Sarah M. |
| **Role** | Retail / Service worker |
| **Technical Proficiency** | Low to Medium |
| **Devices** | Shared desktop terminal or personal smartphone |
| **Primary Goal** | Record shift start and end with minimal friction |
| **Pain Points** | Complicated interfaces, uncertain whether the clock-in registered, confusion about elapsed time when working across timezone boundaries |

**Needs from TimeClock:**
- A single, prominent button to clock in and clock out
- Immediate visual confirmation that the action was recorded
- A live elapsed timer showing how long the current shift has been running
- A clear read-only history of all past shifts

### 2.2 Persona B — The Administrator

| Attribute | Detail |
|---|---|
| **Name** | Marco F. |
| **Role** | HR Manager / Shift Supervisor |
| **Technical Proficiency** | Medium to High |
| **Devices** | Desktop workstation |
| **Primary Goal** | Maintain accurate attendance records and produce payroll-ready reports |
| **Pain Points** | Employees who forget to clock out, manual data entry errors, inability to prove a correction was legitimate, no way to block terminated employees immediately |

**Needs from TimeClock:**
- The ability to correct erroneous entries with a full audit trail of who changed what
- Per-employee reports with total hours and estimated pay, exportable to CSV for payroll
- Immediate control over which users can access the system (activate/deactivate)
- Visibility of anomalous shifts (unusually long durations) for review
- Automatic handling of shifts left open indefinitely (stale shift auto-close)

---

## 3. Functional Requirements — User Stories

### 3.1 Authentication

| ID | User Story | Acceptance Criteria | Priority |
|---|---|---|---|
| AUTH-01 | As an **employee**, I want to log in with my username and password so that I can access my time clock. | Valid credentials issue a 15-minute JWT access token and a 7-day refresh token. Invalid credentials return a 401 with a user-friendly message. | P0 |
| AUTH-02 | As an **employee**, I want my session to stay active across tabs without re-logging in, so that I am not interrupted mid-shift. | Refresh tokens rotate silently. A proactive refresh fires when the access token has < 2 minutes remaining. All open tabs receive the updated token via `localStorage` storage events. | P0 |
| AUTH-03 | As an **employee**, I want a clear notification when my session has expired, so that I am not silently logged out mid-action. | On access token expiry, a toast notification fires via a `auth:expired` DOM event before the user is redirected to the login page. | P1 |
| AUTH-04 | As a **system**, I need to protect the login endpoint from brute-force attacks. | A sliding-window rate limiter allows a maximum of **10 login attempts per IP+username combination per 5-minute window**. Excess attempts receive `429 Too Many Requests`. | P0 |
| AUTH-05 | As an **administrator**, I need to create new user accounts with defined roles. | The registration endpoint is gated by `[Authorize(Roles = "Admin")]`. Roles are validated against an allowlist (`Employee`, `Admin`). Usernames must be 3–50 alphanumeric characters; passwords must be at least 6 characters. | P0 |
| AUTH-06 | As a **system**, I need to ensure tokens are cryptographically secure and never contain a default fallback key. | The `Jwt:Key` must be explicitly configured via user-secrets or environment variables. Application startup fails with a clear `InvalidOperationException` if the key is absent. | P0 |

---

### 3.2 Time Tracking (Employee Clock)

| ID | User Story | Acceptance Criteria | Priority |
|---|---|---|---|
| TC-01 | As an **employee**, I want to clock in with a single button tap so that beginning my shift is as frictionless as possible. | `POST /api/attendance/clock-in` creates a `TimeEntry` with a server-generated Zurich timestamp. The button transitions to "Clock Out" state immediately upon server confirmation. | P0 |
| TC-02 | As an **employee**, I want to clock out and optionally add a note about my shift so that I can provide context to my supervisor. | `POST /api/attendance/clock-out` accepts an optional `notes` field (≤ 500 characters). Notes from clock-in and clock-out are concatenated with ` \| `. | P0 |
| TC-03 | As an **employee**, I want a live elapsed timer so that I always know how long my current shift has been running. | The timer is derived from a synced server-time delta, not `Date.now()` alone. It renders in `HH:MM:SS` format and updates every second without drift. | P1 |
| TC-04 | As an **employee**, I want a live Zurich clock display so that I know the authoritative time being used for my shifts. | The frontend syncs server time every 5 minutes and extrapolates locally between syncs using a delta reference. The displayed time is always consistent with server reality. | P1 |
| TC-05 | As an **employee**, I want a warning if the time sync fails so that I know my timestamps may not reflect the primary source. | An amber `AlertTriangle` notification is displayed when the server time sync call fails. The message reassures the user that timestamps are still recorded server-side. | P1 |
| TC-06 | As a **system**, I must reject a clock-out that would create a shift shorter than 1 minute. | If `(ClockOut − ClockIn) < 1 minute`, the service throws a validation error and returns `400 Bad Request` with a descriptive message. | P0 |
| TC-07 | As an **employee**, I want to view a paginated history of my past shifts so that I can verify my attendance record. | `GET /api/attendance/history` returns up to 50 entries per page, including clock-in, clock-out, duration (formatted), notes, and an `isManuallyEdited` flag. | P1 |
| TC-08 | As an **employee**, I want to see which of my entries have been edited by an admin so that I have full transparency. | Any entry with `IsManuallyEdited = true` displays an amber "Edited" pill badge. | P2 |
| TC-09 | As a **system**, I must prevent a user from having two open (unclosed) shifts simultaneously. | A filtered unique database index `(UserId) WHERE ClockOut IS NULL` enforces at most one open entry per user. Concurrent duplicate clock-ins receive `409 Conflict`. | P0 |
| TC-10 | As a **system**, I must auto-close shifts that have been open for more than 16 hours to prevent data anomalies. | On the next clock-in attempt, an open entry exceeding 16 hours is closed at `ClockIn + 16h`, flagged as `IsManuallyEdited`, and annotated with `[Auto-closed: exceeded 16h limit]`. | P1 |

---

### 3.3 Admin Tools

| ID | User Story | Acceptance Criteria | Priority |
|---|---|---|---|
| ADM-01 | As an **administrator**, I want to view a full employee roster so that I have an overview of all users and their status. | `GET /api/admin/users` returns all users with username, full name, role badge, and active/inactive status indicator. | P0 |
| ADM-02 | As an **administrator**, I want to deactivate a terminated employee immediately so that they cannot clock in or access the system. | `PUT /api/admin/users/{userId}/status` toggles `IsActive`. `IsActiveMiddleware` intercepts all subsequent authenticated requests from that user and returns `403 Forbidden`. | P0 |
| ADM-03 | As an **administrator**, I want to view a full attendance report for any employee, filterable by date range, so that I can prepare payroll. | `GET /api/admin/reports/{userId}` returns all entries with clock-in, clock-out, duration, notes, and a report summary including total hours and estimated pay. | P0 |
| ADM-04 | As an **administrator**, I want to export an employee's attendance report to CSV so that I can import it into payroll software. | CSV export is RFC 4180 compliant, UTF-8 BOM prefixed (for Excel compatibility), formula-injection protected, and includes a summary row with total hours, hourly rate, and estimated pay. | P1 |
| ADM-05 | As an **administrator**, I want to set an employee's hourly rate so that estimated pay is calculated automatically in reports. | `PUT /api/admin/users/{userId}/hourly-rate` persists a `decimal(8,2)` rate. Reports compute `EstimatedPay = TotalHours × HourlyRate`. Entries with no rate display "N/A". | P1 |
| ADM-06 | As an **administrator**, I want to edit a time entry to correct genuine errors so that the payroll record is accurate. | A Radix UI modal accepts new `ClockIn`, `ClockOut`, and an audit note. The backend validates `ClockOut > ClockIn` and `ClockIn ≤ now` before saving. | P0 |
| ADM-07 | As an **administrator**, I want every edit to a time entry to generate a full audit trail so that corrections are transparent and defensible. | Every field change (`ClockIn`, `ClockOut`, `Notes`, `DurationMinutes`) produces one `TimeEntryAuditLog` row with the old value, new value, the admin's user ID, and the UTC timestamp of the change. | P0 |
| ADM-08 | As an **administrator**, I want to view the full audit history for any time entry by clicking the "Edited" badge so that I can understand the correction history. | A Radix Dialog shows a table of all audit rows for the selected entry: timestamp, changed by, field name, old → new values. | P1 |
| ADM-09 | As an **administrator**, I want to reopen a closed time entry so that an employee can submit their own clock-out when the wrong time was recorded. | `POST /api/admin/attendance/{entryId}/reopen` nullifies `ClockOut` and `DurationMinutes`, auditing the change. The system validates that no other open entry exists for the user before reopening. | P1 |
| ADM-10 | As an **administrator**, I want shifts longer than 12 hours flagged visually so that I can identify potential data errors for review. | Entries with `DurationMinutes > 720` display an `AlertTriangle` icon in the admin report table. | P2 |
| ADM-11 | As an **administrator**, I want to check the offline queue status so that I know if any clock actions are pending persistence. | `GET /api/attendance/offline-queue-status` returns the count of pending offline entries. Accessible to Admin role only. | P2 |

---

## 4. Non-Functional Requirements

### 4.1 Reliability

| Requirement | Specification |
|---|---|
| **Zurich Time Fallback Chain** | The system must never return a `5xx` error to the user because an external time API is unavailable. The fallback order is: (1) `timeapi.io`, (2) `worldtimeapi.org`, (3) `TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, "Europe/Zurich")`. Each API call has a 3-second hard timeout. |
| **Time API Caching** | Successful API responses are cached for 10 minutes (as a UTC ↔ Zurich offset). Repeated calls within the cache window are served instantly without redundant HTTP requests. |
| **Offline Queue** | If the database is unreachable during a clock action, the request is persisted to `data/offline-queue.json` (guarded by `SemaphoreSlim` for thread safety). `OfflineSyncService` drains the queue into the database every 3 minutes, processing entries chronologically. |
| **Optimistic Concurrency** | `TimeEntry` rows carry a `RowVersion` rowversion token. Concurrent updates are detected by EF Core and return `409 Conflict`. |
| **Uptime Target** | Core clock-in/clock-out flow must be operational 99.9% of scheduled working hours. |

### 4.2 Security

| Requirement | Specification |
|---|---|
| **Password Storage** | All passwords are hashed using ASP.NET Core's `BCrypt` / `PasswordHasher`. Plaintext passwords are never stored or logged. |
| **JWT Configuration** | The `Jwt:Key` must be ≥ 32 characters. Application startup throws `InvalidOperationException` if it is absent. There is no fallback key in source code. |
| **Token Lifecycle** | Access tokens expire in 15 minutes. Refresh tokens expire in 7 days with automatic rotation on each use. Refresh token reuse (theft detection) triggers revocation of the entire token family for that user. |
| **IsActive Enforcement** | `IsActiveMiddleware` runs on every authenticated request. A deactivated user is blocked immediately at the middleware level, even if their JWT has not yet expired. |
| **Rate Limiting** | Login is rate-limited to 10 attempts per 5-minute sliding window, keyed by `{IP}:{username}`. Returns `429 Too Many Requests`. A cleanup timer purges expired rate-limit windows to prevent memory exhaustion. |
| **CORS** | CORS policy restricts allowed origins to the configured frontend origin. Wildcard origins are not permitted in production configuration. |
| **Role Validation** | The `Role` field is validated against a server-side allowlist at registration. Arbitrary role escalation via API is not possible. |
| **CSV Injection** | CSV cells beginning with `=`, `+`, `-`, or `@` are prefixed with a tab character to neutralise formula injection when opened in spreadsheet software. |
| **Global Exception Middleware** | All unhandled exceptions are intercepted by `ExceptionMiddleware` and mapped to structured JSON responses. Raw stack traces are never returned to the client. |
| **Input Validation** | Usernames: 3–50 alphanumeric/underscore characters. Passwords: minimum 6 characters. Full name: maximum 200 characters. Notes: maximum 500 characters. Date range queries: `from ≤ to` enforced server-side. |

### 4.3 Performance

| Requirement | Specification |
|---|---|
| **Database Indexing** | `TimeEntries(UserId, ClockIn)` composite index with covering columns for history and report queries. Filtered unique index `(UserId) WHERE ClockOut IS NULL` for open-entry lookups. `TimeEntryAuditLogs(TimeEntryId)` index for fast audit retrieval. `RefreshTokens(Token)` unique index for O(1) token validation. |
| **Pagination** | History and report endpoints are paginated (default: 50 entries per page). The system must never return an unbounded resultset to the client. |
| **React Rendering** | Clock page state is updated from the server response body (optimistic-free). The shared tick interval drives both the Zurich clock display and the elapsed timer from a single `setInterval`, avoiding redundant re-renders. |
| **API Response Time** | Clock-in/clock-out endpoints must respond in < 2 seconds under normal load (inclusive of time API lookup or cache hit). |

### 4.4 Maintainability & Testability

| Requirement | Specification |
|---|---|
| **ITimeService Abstraction** | The time source is abstracted behind `ITimeService`. All time-dependent business logic can be exercised in unit tests by injecting a mock time service without coupling to external APIs or real clocks. |
| **Global Exception Handling** | A single `ExceptionMiddleware` centralises exception-to-HTTP mapping, eliminating scattered try/catch blocks in controllers. |
| **EF Core Change Tracker Audit** | Audit log insertion is automated via an overridden `SaveChangesAsync`. No individual service method needs to manually trigger audit writes. |
| **Migration Strategy** | All schema changes are managed via EF Core migrations. `dotnet-ef` (v10) is the designated CLI tool. |

---

## 5. User Interface Requirements

### 5.1 Global Design Principles

| Principle | Requirement |
|---|---|
| **Aesthetic** | Clean, minimalist. High contrast for readability on shared terminal displays. No decorative elements that add visual noise. |
| **Theme** | Dark/Light mode with system-preference-aware default. Manual toggle persisted via `ThemeContext` across sessions. |
| **Icons** | Lucide React icon set used consistently throughout. Icons must be accompanied by labels or tooltips for accessibility. |
| **Notifications** | `react-hot-toast` provides transient success, error, and status messages. Toasts auto-dismiss and do not block UI interaction. |
| **Typography** | Monospace font for time values (`HH:MM:SS`) to prevent layout shift during timer updates. |

### 5.2 Login Page

| Element | Requirement |
|---|---|
| Username field | `minLength=3`, `maxLength=50`, `pattern` enforcing alphanumeric/underscore |
| Password field | `minLength=6`, masked input |
| Submit button | Disabled and shows spinner during in-flight request |
| Error display | Inline error message on invalid credentials; no disclosure of whether username or password is incorrect (security) |
| Rate limit feedback | Clear `429` error message advising the user to wait before retrying |

### 5.3 Employee Clock Page

| Element | Requirement |
|---|---|
| **Zurich Clock** | Large, prominent display of current Europe/Zurich time in `HH:MM:SS` format with date. Updates every second using a delta-from-sync approach. |
| **Sync Warning** | Amber `AlertTriangle` banner displayed when server time sync fails. Message: "Time sync unavailable — your clock-in/out will still be recorded server-side." |
| **Clock In / Out Button** | Single, full-width action button. Label changes between "Clock In" and "Clock Out" based on open-shift status. Disabled during in-flight request (debounced). |
| **Notes Field** | Optional text area below the action button. Maximum 500 characters with a visible character counter. Clears on successful clock action. |
| **Elapsed Timer** | Displayed only when a shift is open. Format: `HH:MM:SS`. Derived from synced server time, not `Date.now()`. |
| **Shift History Table** | Paginated (50 per page). Columns: Clock In · Clock Out · Duration · Notes · Status. "Edited" amber pill on `IsManuallyEdited` entries. |
| **Mobile Layout** | Desktop → standard table. Mobile (< 768 px) → compact card layout with identical data fields. |

### 5.4 Admin Dashboard

| Element | Requirement |
|---|---|
| **Employee Roster** | Table with username, full name, role badge (colour-coded), and active/inactive status indicator. Sortable by name. |
| **Create Employee Form** | Inline input row within the roster table. Fields: username, full name, password, role selector. Validates all constraints before submission. |
| **Activate/Deactivate Toggle** | Per-row power toggle. Toggle fires `PUT /api/admin/users/{userId}/status`. Visual state updates immediately on confirmation. Confirmation prompt required before deactivation. |
| **Per-Employee Report Panel** | Triggered by selecting an employee. Displays full paginated entry table plus a summary row (total hours, hourly rate, estimated pay). |
| **Hourly Rate Input** | Inline numeric input field in the employee roster or report header. Validates positive decimal input. Saves on blur or explicit submit. |
| **CSV Export Button** | Located in the report header. Triggers a blob download named `report-{employeeName}-{date}.csv`. Shows loading state during generation. |
| **Edit Entry Modal** | Radix UI Dialog. Fields: Clock In (datetime-local), Clock Out (datetime-local), Admin Note. Clock In/Out pickers display CET/CEST label. Future timestamps are disabled. Submits via `PUT /api/admin/attendance/{entryId}`. |
| **Reopen Entry Action** | Button in the edit modal or entry action menu. Confirmation prompt. Fires `POST /api/admin/attendance/{entryId}/reopen`. |
| **Audit Trail Modal** | Triggered by clicking the "Edited" badge. Radix UI Dialog. Table: Changed At · Changed By · Field · Old Value → New Value. |
| **Long Shift Indicator** | `AlertTriangle` icon (amber) on any entry with `DurationMinutes > 720`. Tooltip: "Shift exceeds 12 hours — review recommended." |

---

## 6. Data Model Overview

### 6.1 Core Entities

| Entity | Purpose | Key Fields |
|---|---|---|
| `Users` | System principals | `Id`, `Username`, `PasswordHash`, `Role`, `IsActive`, `HourlyRate` |
| `TimeEntries` | Shift records | `ClockIn`, `ClockOut` (Zurich-local), `DurationMinutes`, `Notes`, `IsManuallyEdited`, `RowVersion` |
| `TimeEntryAuditLogs` | Immutable edit history | `TimeEntryId`, `ChangedByUserId`, `ChangedAt` (UTC), `FieldName`, `OldValue`, `NewValue` |
| `RefreshTokens` | Session management | `Token`, `ExpiresAt`, `RevokedAt`, `ReplacedByToken` |

### 6.2 Critical Constraints

| Constraint | Mechanism |
|---|---|
| One open entry per user | Filtered unique index `(UserId) WHERE ClockOut IS NULL` |
| Concurrent update safety | `RowVersion` optimistic concurrency on `TimeEntries` |
| Referential integrity | `Users → TimeEntries`: Restrict delete. `TimeEntries → AuditLogs`: Cascade delete. `Users → RefreshTokens`: Cascade delete. |

> **Note:** `TimeEntry.ClockIn/ClockOut` are stored as Europe/Zurich local time without UTC offset. `Users.CreatedAt`, `AuditLogs.ChangedAt`, and all `RefreshToken` datetime fields are stored as UTC. Cross-table date comparisons must account for this convention. A future migration to `DateTimeOffset` is recommended to make timezone intent explicit in the schema.

---

## 7. Success Metrics & Acceptance Criteria

### 7.1 Technical Reliability

| Metric | Target | Measurement |
|---|---|---|
| Time sync uptime | ≥ 99.9% of clock actions served with a valid Zurich timestamp | Percentage of `clock-in`/`clock-out` requests that complete without a 5xx error, even when external time APIs are unavailable |
| Fallback chain coverage | 100% of clock actions return a timestamp | Zero 500 responses caused by time API unavailability |
| Offline queue drain rate | ≤ 3-minute backlog clearance | Time between a clock action hitting the queue and it being persisted to the database |
| Concurrent request safety | Zero duplicate open entries | `409 Conflict` returned on all race-condition clock-in attempts (DB-level enforcement validated by integration tests) |

### 7.2 Security & Access Control

| Metric | Target | Measurement |
|---|---|---|
| Unauthorized shift edits | Zero | All `PUT /api/admin/attendance/{entryId}` calls produce an audit log row; any edit without a corresponding log entry is a violation |
| Deactivated user access | < 1 request succeeds after deactivation | Time between `IsActive = false` and first rejected request ≤ 1 request (next middleware invocation) |
| Brute-force protection | Zero successful brute-force attacks | Rate limiter triggers at 10 attempts; monitored via 429-response rate in logs |
| Token security | Zero recoverable hardcoded secrets in source code | CI check: no fallback JWT key expression present in `Program.cs` or `AuthService.cs` |

### 7.3 Data Integrity

| Metric | Target | Measurement |
|---|---|---|
| Audit trail completeness | 100% of admin-edited fields have a corresponding log row | Query: `COUNT(Changes) = COUNT(AuditLogs)` for any edited entry |
| Sub-1-minute shift rejection | 100% rejected | Integration test: clock-out attempt within 59 seconds always returns `400` |
| Stale shift auto-close | 100% of shifts > 16h closed before new clock-in | Query: zero open entries with `ClockIn < NOW() - 16h` |
| CSV formula injection | Zero exploitable formulas in exported CSV | Automated test: cells starting with `=`, `+`, `-`, `@` are prefixed with `\t` |

### 7.4 User Experience

| Metric | Target | Measurement |
|---|---|---|
| Clock action response time | p95 ≤ 2 seconds end-to-end | Measured at client from button click to UI state change |
| Time sync warning display | 100% shown within 1 render cycle after sync failure | Frontend test: mock API failure → assert `AlertTriangle` is rendered |
| Session continuity | Zero unexpected logouts during an active shift | Proactive token refresh fires before expiry; no gap in authenticated state |
| Mobile usability | All core employee actions completable on 375 px viewport | Manual QA + Playwright viewport test for card layout |

---

## 8. Out of Scope

The following capabilities are explicitly excluded from this version and should not influence sprint planning:

| Item | Rationale |
|---|---|
| Native mobile applications (iOS/Android) | The responsive web UI satisfies mobile use cases. Native apps require separate build/distribution pipelines. |
| Multi-timezone support | The system is intentionally opinionated: Europe/Zurich is the single authoritative timezone. Multi-timezone is a future architectural decision requiring schema migration to `DateTimeOffset`. |
| Payroll system integration (direct API) | CSV export is the defined integration boundary. Direct ERP/payroll API connectors are a phase-2 consideration. |
| Biometric or hardware clock-in terminals | Out of scope for this web-based solution. |
| Shift scheduling / calendar | TimeClock records when work happened; it does not plan when work should happen. |
| Employee self-correction of time entries | Employees may not edit their own entries. Only admins may edit, with a mandatory audit trail. |
| Email / push notifications | Notifications are in-app (toast) only. |
| Multi-tenant / multi-company support | Single-tenant architecture. All users belong to the same organisation. |

---

## 9. Risks & Mitigations

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| Both external time APIs unreachable simultaneously | Low | High | Server-side `TimeZoneInfo` fallback always executes. System is never dependent on external API availability for its core guarantee. |
| JWT secret not configured in production | Medium | Critical | Application startup throws on missing key. Cannot deploy without explicit configuration. |
| Database unavailable during clock action | Low | High | Offline queue persists the action locally. Background sync drains within 3 minutes of database recovery. |
| Token theft via XSS | Low | High | Short-lived 15-minute access tokens limit the exploitation window. Deactivation immediately blocks further API access regardless of token state. |
| Admin accidentally deactivates themselves | Low | Medium | UI should warn when an admin attempts to deactivate their own account. Fallback: direct DB access to reactivate. |
| CSV formula injection exploited by attacker | Low | Medium | Server-side prefix sanitisation on all CSV cells before export. |
| Cross-table datetime comparisons producing wrong results | Medium | Medium | Known technical debt (UTC vs Zurich-local mixing). Mitigated by isolation: reports use only `TimeEntries` dates. Track as tech debt item for `DateTimeOffset` migration. |

---

## 10. Glossary

| Term | Definition |
|---|---|
| **Clock In** | The action of recording the start of a work shift. Generates a server-side Zurich timestamp. |
| **Clock Out** | The action of recording the end of a work shift. Validates minimum shift duration (≥ 1 minute). |
| **Zurich Time** | The authoritative timezone for all shift timestamps: Europe/Zurich (CET/CEST). |
| **Open Entry** | A `TimeEntry` record with `ClockIn` set and `ClockOut = NULL`, indicating an ongoing shift. |
| **Stale Shift** | An open entry whose `ClockIn` is more than 16 hours in the past. Auto-closed on next clock-in. |
| **Audit Log** | An immutable record of a field-level change to a `TimeEntry`, including who made the change and the before/after values. |
| **Refresh Token** | A long-lived (7-day) token used to obtain a new access token without re-authenticating. Rotated on each use. |
| **IsActive** | A `User` flag that, when `false`, causes `IsActiveMiddleware` to reject all authenticated requests from that user with `403 Forbidden`. |
| **Offline Queue** | A local JSON file (`data/offline-queue.json`) that buffers clock actions when the database is unreachable. Drained by `OfflineSyncService` every 3 minutes. |
| **Optimistic Concurrency** | A database-level mechanism using `RowVersion` to detect and reject concurrent writes to the same `TimeEntry` row, returning `409 Conflict`. |
| **JWT** | JSON Web Token — a signed, stateless bearer token used for authentication. Access tokens expire in 15 minutes. |
| **CET/CEST** | Central European Time (UTC+1) / Central European Summer Time (UTC+2) — the two offsets of the Europe/Zurich timezone, switching with DST. |
