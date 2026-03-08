# Implementation Plan: 4 Priority Features

## Build Order

```
#12 Notes → #3 CSV Export → #10 Audit Trails → #17 Refresh Tokens
```

Each feature is independent, but this order goes from smallest to largest risk. Audit trails (#10) should come after notes (#12) so it captures note changes automatically. Refresh tokens (#17) goes last because it changes the auth flow across the entire stack.

---

## Feature #12: Notes at Clock-In/Out

**Scope: ~1–2 hours | No migration needed**

The `Notes` column already exists on `TimeEntries`. We just need to let employees pass notes through.

### Backend

| File | Change |
|------|--------|
| `DTOs/AttendanceDtos.cs` | Add `record ClockRequest(string? Notes);` |
| `Services/AttendanceService.cs` | Add `string? notes = null` param to `ClockInAsync` and `ClockOutAsync`. Set `entry.Notes = notes?.Trim()` (validate ≤500 chars). On clock-out, if both clock-in and clock-out have notes, concatenate with ` | `. |
| `Controllers/AttendanceController.cs` | Accept `[FromBody] ClockRequest? request` on both endpoints. Pass `request?.Notes` to service. |

### Frontend

| File | Change |
|------|--------|
| `pages/ClockPage.tsx` | Add `clockNote` state + text input below the clock button. Pass `{ notes: clockNote }` as POST body. Clear after success. |

### Edge Cases
- Empty/whitespace-only notes → treat as null (trim + nullify)
- Notes > 500 chars → throw `InvalidOperationException` in service
- Sending no body or `{}` must still work (backwards compatible)
- Auto-close logic already appends to Notes — make sure employee notes don't conflict

---

## Feature #3: CSV Export + Hourly Rate

**Scope: ~4–6 hours | 1 migration**

### Migration: `AddHourlyRateToUser`
```
Users + HourlyRate decimal(8,2) NULL
```

### Backend

| File | Change |
|------|--------|
| `Models/User.cs` | Add `decimal? HourlyRate { get; set; }` |
| `Data/AppDbContext.cs` | Configure `HasColumnType("decimal(8,2)")` |
| `DTOs/AttendanceDtos.cs` | Add `HourlyRate` to `EmployeeDto`. Add `HourlyRate` + `EstimatedPay` to `EmployeeReportDto`. Add `record UpdateHourlyRateRequest(decimal HourlyRate);` |
| `Services/AdminService.cs` | Map `HourlyRate` in employee list + report DTOs. Compute `EstimatedPay = totalHours × rate`. Add `GetEmployeeReportCsvAsync(int userId, DateTime? from, DateTime? to)` — fetches ALL entries (no pagination), builds RFC 4180 CSV with BOM (`\uFEFF`). |
| `Controllers/AdminController.cs` | Accept `[FromQuery] string? format` on `GetReport`. When `format == "csv"` → return `File(bytes, "text/csv", "report-{name}.csv")`. Add `PUT /api/admin/users/{userId}/hourly-rate`. |

### Frontend

| File | Change |
|------|--------|
| `pages/AdminPage.tsx` | Add "Export CSV" button in report header. Fetch with `responseType: 'blob'`, trigger download via `URL.createObjectURL`. Show `Est. Pay: CHF X.XX` next to total hours. Add hourly rate input in employee management. |

### Edge Cases
- **CSV injection**: prefix cells starting with `=`, `+`, `-`, `@` with a tab character
- **Commas/newlines in notes**: proper RFC 4180 quoting (wrap in `"`, escape `"` as `""`)
- **Null hourly rate**: show "N/A" for estimated pay, not 0
- **UTF-8 BOM**: prepend `\uFEFF` so Excel reads it correctly

---

## Feature #10: Advanced Audit Trails

**Scope: ~1 day | 1 migration**

### Migration: `AddTimeEntryAuditLog`
```sql
TimeEntryAuditLogs (
    Id              int PK IDENTITY,
    TimeEntryId     int FK → TimeEntries(Id) CASCADE,
    ChangedByUserId int FK → Users(Id) RESTRICT,
    ChangedAt       datetime2 NOT NULL,
    FieldName       nvarchar(50) NOT NULL,
    OldValue        nvarchar(500) NULL,
    NewValue        nvarchar(500) NULL
)
INDEX IX_TimeEntryAuditLogs_TimeEntryId ON (TimeEntryId)
```

### Backend

| File | Change |
|------|--------|
| `Models/TimeEntryAuditLog.cs` | **NEW** — Entity with nav props to `TimeEntry` and `User` |
| `Data/AppDbContext.cs` | Add `DbSet<TimeEntryAuditLog>`. Entity config (FKs, indexes, column lengths). Override `SaveChangesAsync`: iterate `ChangeTracker.Entries<TimeEntry>()` where `State == Modified`, compare `OriginalValue` vs `CurrentValue` for `ClockIn`, `ClockOut`, `Notes`, `DurationMinutes`. Insert audit rows. Get `ChangedByUserId` from `IHttpContextAccessor`. |
| `Program.cs` | Add `builder.Services.AddHttpContextAccessor();` |
| `DTOs/AttendanceDtos.cs` | Add `record AuditLogDto(int Id, string ChangedByUserName, DateTime ChangedAt, string FieldName, string? OldValue, string? NewValue);` |
| `Services/AdminService.cs` | Add `GetAuditLogsAsync(int entryId)` — query audit logs joined with Users. |
| `Controllers/AdminController.cs` | Add `GET /api/admin/attendance/{entryId}/audit` |

### Frontend

| File | Change |
|------|--------|
| `pages/AdminPage.tsx` | Make "Edited" badge clickable → `onClick` fetches audit logs → show in Radix Dialog modal (reuse existing modal pattern). Table: Timestamp, Changed By, Field, Old → New. |

### Edge Cases
- **No HTTP context** (seed, auto-close, tests): skip audit logging gracefully
- **Multiple fields in one save**: one audit row per changed field, same `ChangedAt` — correct
- **Reopen entry**: audits `ClockOut: <time> → null` and `DurationMinutes: <value> → null`
- **DurationMinutes**: audit it — it's a visible outcome, even though derived
- **InMemory provider in tests**: `ChangeTracker` works identically

---

## Feature #17: Refresh Token Auth

**Scope: ~1–2 days | 1 migration**

### Migration: `AddRefreshTokens`
```sql
RefreshTokens (
    Id              int PK IDENTITY,
    UserId          int FK → Users(Id) CASCADE,
    Token           nvarchar(128) NOT NULL UNIQUE,
    ExpiresAt       datetime2 NOT NULL,
    CreatedAt       datetime2 NOT NULL,
    RevokedAt       datetime2 NULL,
    ReplacedByToken nvarchar(128) NULL
)
INDEX IX_RefreshTokens_UserId ON (UserId)
UNIQUE INDEX IX_RefreshTokens_Token ON (Token)
```

### Backend

| File | Change |
|------|--------|
| `Models/RefreshToken.cs` | **NEW** — Entity with computed `IsRevoked`, `IsExpired`, `IsActive` props. Nav prop to `User`. |
| `Models/User.cs` | Add nav: `ICollection<RefreshToken> RefreshTokens` |
| `Data/AppDbContext.cs` | Add `DbSet<RefreshToken>`. Entity config (unique index on Token, FKs). |
| `DTOs/AuthDtos.cs` | Update `AuthResponse` → add `RefreshToken` field. Add `record RefreshRequest(string RefreshToken);`. Add `record RefreshResponse(string Token, string RefreshToken);`. |
| `Services/AuthService.cs` | Access token expiry: `12h → 15min`. Add `GenerateRefreshToken()` (64-byte `RandomNumberGenerator` → base64). Update `LoginAsync` + `RegisterAsync` to create + persist refresh token (7-day expiry). Add `RefreshAsync(string refreshToken)`: validate → revoke old → issue new pair (rotation). Add `RevokeRefreshTokenAsync(string token)`. |
| `Controllers/AuthController.cs` | Add `[AllowAnonymous] POST /api/auth/refresh`. Add `POST /api/auth/logout`. |

### Frontend

| File | Change |
|------|--------|
| `services/api.ts` | Rewrite 401 interceptor: attempt refresh before clearing auth. Use `isRefreshing` flag + request queue to prevent concurrent refreshes. Use a **separate** axios instance for the refresh call to avoid interceptor recursion. |
| `context/AuthContext.tsx` | Store `refreshToken` in localStorage. `logout()` calls `POST /api/auth/logout`. Proactive timer: attempt refresh instead of logout when access token nears expiry. Multi-tab sync for `refreshToken` key. |
| `pages/LoginPage.tsx` | Store `data.refreshToken` from login response. |

### Edge Cases
- **Token theft detection**: reused revoked token → revoke ALL tokens for that user
- **Concurrent 401s**: interceptor queue prevents multiple refresh calls
- **Multi-tab**: `storage` event syncs new tokens across tabs
- **Refresh endpoint is `[AllowAnonymous]`**: called when access token is expired
- **User deactivated between refreshes**: `RefreshAsync` must check `user.IsActive`
- **Old 12h tokens after deploy**: users get logged out once (acceptable one-time pain)
- **Token cleanup**: delete expired tokens > 30 days (add to startup)

---

## Files Touched (Summary)

| File | #12 | #3 | #10 | #17 |
|------|-----|----|-----|-----|
| `Models/User.cs` | | x | | x |
| `Models/TimeEntryAuditLog.cs` | | | **NEW** | |
| `Models/RefreshToken.cs` | | | | **NEW** |
| `Data/AppDbContext.cs` | | x | x | x |
| `DTOs/AttendanceDtos.cs` | x | x | x | |
| `DTOs/AuthDtos.cs` | | | | x |
| `Services/AttendanceService.cs` | x | | | |
| `Services/AdminService.cs` | | x | x | |
| `Services/AuthService.cs` | | | | x |
| `Controllers/AttendanceController.cs` | x | | | |
| `Controllers/AdminController.cs` | | x | x | |
| `Controllers/AuthController.cs` | | | | x |
| `Program.cs` | | | x | |
| `frontend/src/pages/ClockPage.tsx` | x | | | |
| `frontend/src/pages/AdminPage.tsx` | | x | x | |
| `frontend/src/pages/LoginPage.tsx` | | | | x |
| `frontend/src/services/api.ts` | | | | x |
| `frontend/src/context/AuthContext.tsx` | | | | x |

**Total: 3 migrations, 2 new model files, ~16 files modified**
