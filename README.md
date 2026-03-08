# TimeClock — Full-Stack Attendance Management System

A production-grade employee time-tracking application built with **ASP.NET Core 9** and **React 19**. Designed around a single core guarantee: every timestamp is recorded by the server using an authoritative Zurich time source — client clocks are never trusted for shift data.

---

## Table of Contents

- [Overview](#overview)
- [Core Features](#core-features)
- [Technical Highlights](#technical-highlights)
- [Architecture](#architecture)
- [API Reference](#api-reference)
- [Database Schema](#database-schema)
- [Security](#security)
- [Testing](#testing)
- [Docker — Quick Start for Reviewers](#docker--quick-start-for-reviewers)
- [Getting Started](#getting-started)
- [Tech Stack](#tech-stack)

---

## Overview

TimeClock solves a fundamental problem in workforce management: **who controls the clock?** Most simple attendance tools record the client's local time, which can be manipulated, skewed by timezone mismatches, or simply wrong. This system externalizes timekeeping entirely — every clock-in and clock-out is timestamped server-side using a live external time API (Europe/Zurich), with an automatic fallback chain to ensure reliability.

The result is a system where:
- Employees cannot alter their own timestamps
- All times are consistent regardless of the client's device timezone or locale
- Admins have full audit and correction capabilities with complete edit history visibility

---

## Core Features

### Employee — Time Clock

| Feature | Detail |
|---|---|
| **Clock In / Clock Out** | Single-action toggle with server-side Zurich timestamp; optional notes on each action |
| **Notes at Clock-In/Out** | Optional free-text note (≤500 chars) on each clock-in/out; clock-out notes are appended to clock-in notes with ` \| ` separator |
| **Live Shift Timer** | Real-time elapsed timer (HH:MM:SS), derived from synced server time — not `Date.now()` |
| **Zurich Clock Display** | Live clock synced from the server every 5 minutes; extrapolated locally between syncs |
| **Time Sync Warning** | Amber `AlertTriangle` notification shown when the server time sync fails, with a clear message that timestamps are still recorded server-side |
| **Recent Shifts Table** | Paginated shift history (50 per page), with full clock-in, clock-out, duration, and notes |
| **Responsive Layout** | Full desktop table; switches to compact card layout on mobile |
| **Manually Edited Badge** | Amber "Edited" pill displayed on any entry modified by an admin |
| **Session Expiry Toast** | Automatic notification on JWT expiry via a custom `auth:expired` DOM event |
| **Refresh Token Auth** | 15-min access tokens + 7-day refresh tokens with automatic rotation; proactive silent refresh when < 2 min remain; multi-tab session sync |

### Admin — Dashboard

| Feature | Detail |
|---|---|
| **Employee Roster** | Full employee list with role badges and active/inactive status indicators |
| **Create Employee** | Inline form to register new employees with username, full name, password, and role (`Employee` / `Admin`) |
| **Activate / Deactivate Users** | Per-user power toggle; deactivated users are immediately blocked at the middleware level |
| **Per-Employee Report** | Full paginated attendance report with total hours, hourly rate, and estimated pay |
| **CSV Export** | Download employee report as RFC 4180 CSV with UTF-8 BOM, formula-injection protection, and summary rows (total hours, hourly rate, estimated pay) |
| **Hourly Rate Management** | Set per-employee hourly rate; estimated pay auto-calculated in reports and CSV exports |
| **Edit Time Entry** | Radix UI modal to correct clock-in, clock-out, and add an audit note (Zurich time, CET/CEST labelled) |
| **Reopen Closed Entry** | Nullify an entry's clock-out, allowing the employee to re-submit their own clock-out |
| **Long Shift Indicator** | Shifts exceeding 12 hours are flagged with an `AlertTriangle` icon for admin review |
| **Audit Trail** | Full edit history for every time entry — tracks who changed which field, when, with old/new values; clickable "Edited" badge opens audit modal |
| **Stale Shift Auto-Close** | Shifts open for more than 16 hours are automatically closed at the next clock-in attempt |

### UI / UX

- **Dark / Light Mode** — system-aware default with manual toggle persisted via `ThemeContext`
- **Toast Notifications** — `react-hot-toast` for success, error, and status messages
- **Debounced Clock Actions** — prevents double-submissions on rapid button clicks
- **Optimistic State Updates** — clock status is updated from the server response body, not assumed locally
- **Lucide React Icons** — consistent, accessible icon set throughout the interface

### Offline Resilience

| Feature | Detail |
|---|---|
| **Offline Queue** | When the database is unreachable during clock-in/out, the action is persisted to a local JSON file (`data/offline-queue.json`) via `OfflineQueueService` |
| **Background Sync** | `OfflineSyncService` (a `BackgroundService`) drains the offline queue into the database every 3 minutes, processing entries chronologically |
| **Thread Safety** | Queue file access is guarded by a `SemaphoreSlim` to prevent concurrent corruption |
| **Admin Visibility** | Admins can check the queue status via `GET /api/attendance/offline-queue-status` |

---

## Technical Highlights

### Server-Side Timestamp Authority

The most critical design decision in the system is that **the server always owns the timestamp**.

```
Client clicks "Clock In"
        │
        ▼
POST /api/attendance/clock-in  (no timestamp in request body)
        │
        ▼
AttendanceService.ClockInAsync()
        │
        ├─► GET https://timeapi.io/api/time/current/zone?timeZone=Europe/Zurich  (primary)
        ├─► GET https://worldtimeapi.org/api/timezone/Europe/Zurich               (fallback)
        └─► TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, "Europe/Zurich")    (last resort)
        │
        ▼
TimeEntry { ClockIn = zurichNow } → saved to SQL Server
```

The `ITimeService` interface abstracts the time source. In tests, it is trivially replaced with a mock, making time-sensitive business logic fully testable without depending on external APIs.

### Zurich Time Fallback Chain

```csharp
// WorldTimeApiService.cs
private static readonly string[] TimeApiUrls =
[
    "https://timeapi.io/api/time/current/zone?timeZone=Europe/Zurich",
    "https://worldtimeapi.org/api/timezone/Europe/Zurich"
];

// If both APIs fail → server-side conversion (always available)
var zurichZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich");
return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zurichZone);
```

Each API has a 3-second timeout. All failures are logged as warnings. The fallback chain ensures the system never returns a 500 to the user simply because an external API is unreachable.

Successful API responses are cached for **10 minutes** (as a UTC ↔ Zurich offset), so subsequent time lookups within the cache window are instant and avoid redundant HTTP calls.

### Frontend Time Synchronisation

The frontend displays a live Zurich clock, but it avoids drift from server reality using a delta-based approach:

```ts
// ClockPage.tsx — a single tick drives both the clock display and the elapsed timer
const tick = () => {
  const base = zurichBaseRef.current; // { apiTime: Date, localTime: number }
  if (base) {
    const nowMs = base.apiTime.getTime() + (Date.now() - base.localTime);
    setZurichTime(new Date(nowMs));
    // Elapsed time is anchored to the same synced origin — never to Date.now() alone
    setElapsed(formatElapsed(nowMs - new Date(lastClockIn).getTime()));
  }
};
```

The browser's local clock is only used as a *delta* between syncs, never as an absolute time reference. This prevents incorrect elapsed times when the browser's timezone differs from Europe/Zurich.

### Stale Shift Auto-Close

```csharp
// AttendanceService.cs
const double MaxShiftHours = 16;

if (openEntry != null)
{
    var openHours = (zurichNow - openEntry.ClockIn).TotalHours;
    if (openHours > MaxShiftHours)
    {
        openEntry.ClockOut = openEntry.ClockIn.AddHours(MaxShiftHours);
        openEntry.Notes += " [Auto-closed: exceeded 16h limit]";
        openEntry.IsManuallyEdited = true;
        // ...
    }
}
```

Shifts open for more than 16 hours are automatically closed when a new clock-in is attempted. The entry is flagged as `IsManuallyEdited` and a note is appended, making it visible to admins in the audit trail.

### Minimum Shift Enforcement

Clock-out is rejected if the resulting shift would be shorter than 1 minute:

```csharp
var durationMinutes = (zurichTime - openEntry.ClockIn).TotalMinutes;
if (durationMinutes < 1)
    throw new InvalidOperationException(
        "Shift duration must be at least 1 minute. Clock-out rejected.");
```

### Global Exception Middleware

All exceptions are intercepted by `ExceptionMiddleware` and mapped to consistent HTTP status codes and JSON error bodies — no raw stack traces ever reach the client:

```csharp
var (statusCode, message) = ex switch
{
    UnauthorizedAccessException  => (401, ex.Message),
    InvalidOperationException    => (400, ex.Message),
    KeyNotFoundException         => (404, ex.Message),
    DbUpdateConcurrencyException => (409, "Record was modified by another user."),
    DbUpdateException            => (409, "Database constraint violation."),
    _                            => (500, "An unexpected error occurred.")
};
```

### Login Rate Limiting

`LoginRateLimitMiddleware` enforces a sliding window of **10 attempts per composite key per 5-minute window** using a lock-free `ConcurrentDictionary`. The rate limit key combines the client IP and username (`{IP}:{username}`), so brute-force attempts against a single account are throttled even from different IPs (within a single origin IP). Expired windows are cleaned up by a periodic timer.

### IsActive Middleware

After JWT validation, a custom `IsActiveMiddleware` checks whether the authenticated user's `IsActive` flag is `true`. Deactivated accounts are immediately blocked with `401 Unauthorized` on every subsequent request. Results are cached in `IMemoryCache` (30-second TTL) to avoid a database round-trip on every API call.

---

## Architecture

```
┌────────────────────────────────────────────────────┐
│                    Frontend                        │
│  React 19 · TypeScript · Vite 7 · Tailwind CSS 4  │
│  AuthContext · ThemeContext · Axios JWT Interceptor│
│  Pages: Login · ClockPage · AdminPage              │
└────────────────────────┬───────────────────────────┘
                         │ HTTP/JSON (JWT Bearer)
                         ▼
┌────────────────────────────────────────────────────┐
│                    Backend                         │
│         ASP.NET Core 9 Web API (:5000)             │
│                                                    │
│  Middleware Pipeline:                              │
│  ExceptionMiddleware → RateLimitMiddleware →       │
│  CORS → Cache-Control → Auth → IsActiveMiddleware  │
│  → Authorization → Controllers                     │
│                                                    │
│  Controllers  →  Services  →  EF Core DbContext    │
│                     │                              │
│              ITimeService (WorldTimeAPI + fallback) │
└───────────────────────┬────────────────────────────┘
                        │
           ┌────────────▼────────────┐
           │   SQL Server (local)    │
           │   Database: TimeClockDb │
           │   Tables: Users         │
           │     TimeEntries         │
           │     RefreshTokens       │
           │     TimeEntryAuditLogs  │
           └─────────────────────────┘
```

### Directory Structure

```
timeclock/
├── backend/                    # ASP.NET Core 9 Web API
│   ├── Controllers/            # AuthController, AttendanceController, AdminController, TimeController
│   ├── Services/               # AttendanceService, AuthService, AdminService, WorldTimeApiService,
│   │                           #   OfflineQueueService, OfflineSyncService
│   ├── Models/                 # User, TimeEntry, RefreshToken, TimeEntryAuditLog, OfflineEntry
│   ├── DTOs/                   # Request/Response records
│   ├── Data/                   # AppDbContext (EF Core)
│   ├── Middleware/             # ExceptionMiddleware, RateLimitMiddleware, IsActiveMiddleware
│   ├── Migrations/             # EF Core migration history
│   └── Program.cs              # DI composition root, middleware pipeline
│
├── frontend/                   # React 19 + TypeScript + Vite 7
│   └── src/
│       ├── pages/              # LoginPage, ClockPage, AdminPage
│       ├── components/         # Navbar, ErrorBoundary
│       ├── context/            # AuthContext, ThemeContext
│       ├── services/           # Axios API client + JWT interceptor
│       └── utils/              # formatters (date, duration, error)
│
├── backend.Tests/              # xUnit + Moq + FluentAssertions
├── e2e/                        # Playwright end-to-end test suite
├── start.cmd                   # Starts both servers
└── install.cmd                 # One-click dependency setup
```

---

## API Reference

All endpoints except `/api/auth/login` require a `Bearer` JWT token in the `Authorization` header.

### Auth

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| `POST` | `/api/auth/login` | None | Authenticate and receive access + refresh tokens |
| `POST` | `/api/auth/register` | Admin | Register a new user |
| `POST` | `/api/auth/refresh` | None | Exchange refresh token for new access + refresh token pair (rotation) |
| `POST` | `/api/auth/logout` | None | Revoke a refresh token (server-side logout) |

### Attendance (Employee)

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| `GET` | `/api/attendance/status` | Employee | Returns whether the user is currently clocked in |
| `POST` | `/api/attendance/clock-in` | Employee | Clock in with optional notes (Zurich timestamp, server-recorded) |
| `POST` | `/api/attendance/clock-out` | Employee | Clock out with optional notes (Zurich timestamp, server-recorded) |
| `GET` | `/api/attendance/history` | Employee | Paginated shift history (`?from&to&page&pageSize`) |
| `GET` | `/api/attendance/offline-queue-status` | Admin | Check pending offline queue entries |

### Admin

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| `GET` | `/api/admin/employees` | Admin | List all employees |
| `POST` | `/api/admin/employees` | Admin | Create a new employee |
| `PUT` | `/api/admin/users/{id}/status` | Admin | Activate or deactivate a user account |
| `GET` | `/api/admin/reports/{userId}` | Admin | Per-employee report (`?format=csv` for CSV export) |
| `PUT` | `/api/admin/users/{id}/hourly-rate` | Admin | Set hourly rate for an employee |
| `PUT` | `/api/admin/attendance/{entryId}` | Admin | Edit a time entry (clock-in, clock-out, notes) |
| `POST` | `/api/admin/attendance/{entryId}/reopen` | Admin | Reopen a closed entry for re-clock-out |
| `GET` | `/api/admin/attendance/{entryId}/audit` | Admin | Get full audit log history for a time entry |

### Time

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| `GET` | `/api/time/current` | Employee | Current Zurich time (used by frontend clock sync) |

---

## Database Schema

### Users

| Column | Type | Constraints |
|---|---|---|
| `Id` | `int` | PK, Identity |
| `Username` | `nvarchar(100)` | NOT NULL, UNIQUE |
| `PasswordHash` | `nvarchar(max)` | NOT NULL (BCrypt) |
| `FullName` | `nvarchar(200)` | NOT NULL |
| `Role` | `nvarchar(20)` | `Admin` or `Employee` |
| `CreatedAt` | `datetime2` | NOT NULL |
| `IsActive` | `bit` | NOT NULL, default `true` |
| `HourlyRate` | `decimal(8,2)` | nullable |

### TimeEntries

| Column | Type | Constraints |
|---|---|---|
| `Id` | `int` | PK, Identity |
| `UserId` | `int` | FK → `Users.Id`, NOT NULL |
| `ClockIn` | `datetime2` | NOT NULL (Zurich local time) |
| `ClockOut` | `datetime2` | nullable |
| `DurationMinutes` | `float` | nullable |
| `Notes` | `nvarchar(max)` | nullable |
| `IsManuallyEdited` | `bit` | NOT NULL, default `false` |
| `RowVersion` | `rowversion` | Optimistic concurrency token |

**Indexes:** `Users.Username` (unique) · `TimeEntries(UserId, ClockIn)` (composite) · unique partial index on open entries (`UserId` where `ClockOut IS NULL`) · `RefreshTokens.Token` (unique) · `TimeEntryAuditLogs.TimeEntryId`

**Relationships:** One `User` → many `TimeEntries` (restrict delete) · One `User` → many `RefreshTokens` (cascade delete) · One `TimeEntry` → many `TimeEntryAuditLogs` (cascade delete)

### RefreshTokens

| Column | Type | Constraints |
|---|---|---|
| `Id` | `int` | PK, Identity |
| `UserId` | `int` | FK → `Users.Id`, CASCADE |
| `Token` | `nvarchar(128)` | NOT NULL, UNIQUE |
| `ExpiresAt` | `datetime2` | NOT NULL |
| `CreatedAt` | `datetime2` | NOT NULL |
| `RevokedAt` | `datetime2` | nullable |
| `ReplacedByToken` | `nvarchar(128)` | nullable (token rotation chain) |

### TimeEntryAuditLogs

| Column | Type | Constraints |
|---|---|---|
| `Id` | `int` | PK, Identity |
| `TimeEntryId` | `int` | FK → `TimeEntries.Id`, CASCADE |
| `ChangedByUserId` | `int` | FK → `Users.Id`, RESTRICT |
| `ChangedAt` | `datetime2` | NOT NULL |
| `FieldName` | `nvarchar(50)` | NOT NULL |
| `OldValue` | `nvarchar(500)` | nullable |
| `NewValue` | `nvarchar(500)` | nullable |

---

## Security

| Concern | Implementation |
|---|---|
| **Authentication** | JWT Bearer (HS256) with 15-min access tokens + 7-day refresh tokens; automatic token rotation with theft detection |
| **Refresh token security** | Token rotation on each refresh; reuse of revoked tokens triggers mass-revocation of all user sessions |
| **Password storage** | BCrypt hashing via `BCrypt.Net-Next` |
| **Rate limiting** | 10 login attempts per `{IP}:{username}` composite key per 5-minute sliding window |
| **Active user check** | `IsActiveMiddleware` re-validates `IsActive` after JWT auth; result cached in `IMemoryCache` |
| **Timestamp integrity** | Clients send no timestamp in clock-in/out payloads; server records exclusively from `ITimeService` |
| **Concurrency** | `RowVersion` optimistic concurrency token on `TimeEntries` |
| **CORS** | Restricted to configured origins; defaults to `http://localhost:5173` |
| **Production hardening** | HTTPS redirect + HSTS enforced in non-development environments |
| **Cache headers** | `Cache-Control: no-store, no-cache, must-revalidate` on all responses |
| **Error leakage** | `ExceptionMiddleware` ensures no stack traces or internal details reach the client |
| **Audit trail** | All time entry modifications tracked with field-level change history (who, what, when, old/new values) |
| **CSV injection protection** | Formula-injection characters (`=`, `+`, `-`, `@`) prefixed with tab in CSV exports |

---

## Testing

The project ships with three test layers:

### Backend — Unit & Integration Tests (`backend.Tests/`)

- **Framework:** xUnit + Moq + FluentAssertions
- **In-memory database** via `Microsoft.EntityFrameworkCore.InMemory` for service-layer isolation
- **Integration tests** via `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`)
- Reusable helpers: `DbContextFactory`, `JwtTokenFactory`, `TimeServiceMockFactory`, `TimeClockWebAppFactory`

```bash
cd backend.Tests && dotnet test
```

### Frontend — Unit Tests (`frontend/`)

- **Framework:** Vitest 4 + `@testing-library/react` + `@testing-library/user-event` + jsdom
- API layer mocked via `src/services/__mocks__/api.ts`

```bash
cd frontend && npm test             # single run
cd frontend && npm run test:watch   # watch mode
cd frontend && npm run test:coverage
```

### End-to-End Tests (`e2e/`)

- **Framework:** Playwright 1.58 (Chromium)
- Full user journeys: login, clock-in/out, admin edit flows, role-based access

```bash
npm run test:e2e            # headless
npm run test:e2e:ui         # Playwright UI mode
npm run test:e2e:debug      # step-through debugger
```

---

## Docker — Quick Start for Reviewers

The easiest way to run the project without installing .NET, Node.js, or SQL Server locally. You only need **Docker Desktop**.

```bash
docker compose up --build
```

That's it. Docker will:
1. Pull a SQL Server 2022 Linux image and start it
2. Build and launch the ASP.NET Core API (auto-applies EF Core migrations + seeds admin user)
3. Build the React frontend and serve it via nginx

| Service | URL |
|---|---|
| Frontend | http://localhost:5173 |
| Backend API | http://localhost:5000 |
| SQL Server | `localhost,1433` (SA / `TimeClock_Dev123!`) |

**Default admin credentials:** `admin` / `admin123`

To stop and remove containers:
```bash
docker compose down
```
To also delete the database volume:
```bash
docker compose down -v
```

---

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js 20+](https://nodejs.org/)
- SQL Server (local instance with Windows Authentication, or update the connection string)

### One-Click Setup

```cmd
install.cmd
```

Installs frontend dependencies and applies EF Core database migrations.

### Running the Application

```cmd
start.cmd
```

Or start each server manually:

```bash
# Terminal 1 — Backend API
cd backend && dotnet run
# Listening on http://localhost:5000

# Terminal 2 — Frontend Dev Server
cd frontend && npm run dev
# Listening on http://localhost:5173
```

Open **http://localhost:5173** in your browser.

**Default admin credentials:** `admin` / `admin123`

> Before deploying to production, override the seed credentials using the `Seed:AdminUsername` and `Seed:AdminPassword` environment variables (or `appsettings.Production.json`). The application will log a warning on startup if the default password is still in use.

---

## Tech Stack

### Backend

| Technology | Version | Role |
|---|---|---|
| ASP.NET Core | 9.0 | Web API framework |
| Entity Framework Core | 9.0 | ORM + migrations |
| SQL Server | 2017+ | Relational database |
| `JwtBearer` | 9.0 | JWT authentication middleware |
| `BCrypt.Net-Next` | 4.1.0 | Password hashing |
| `Microsoft.AspNetCore.OpenApi` | 9.0 | OpenAPI spec (development) |

### Frontend

| Technology | Version | Role |
|---|---|---|
| React | 19.2 | UI framework |
| TypeScript | 5.9 | Static typing |
| Vite | 7.3 | Build tool and dev server |
| Tailwind CSS | 4.2 | Utility-first styling |
| Axios | 1.13 | HTTP client with JWT interceptor |
| React Router DOM | 7.13 | Client-side routing |
| `@radix-ui/react-dialog` | 1.1 | Accessible modal primitives |
| `@radix-ui/react-dropdown-menu` | 2.1 | Accessible dropdown primitives |
| `@radix-ui/react-alert-dialog` | 1.1 | Accessible confirm dialog primitives |
| `react-hot-toast` | 2.6 | Toast notifications |
| `lucide-react` | 0.577 | Icon library |
| `clsx` | 2.1 | Conditional class name utility |

### Testing

| Technology | Role |
|---|---|
| xUnit | Backend test runner |
| Moq | Service and dependency mocking |
| FluentAssertions | Readable assertion syntax |
| Vitest 4 | Frontend test runner |
| `@testing-library/react` | Component testing utilities |
| Playwright 1.58 | End-to-end browser automation |
