# TimeClock - Additional Edge Cases

Supplementary edge cases discovered beyond the initial [EDGE-CASES.md](EDGE-CASES.md) audit. These represent functional gaps, performance concerns, and security hardening opportunities.

> **Overlap note:** Items marked with *(extends #X.Y)* share the same root cause as an existing EDGE-CASES.md entry but expose a distinct impact vector or gap not covered by the original fix.

---

## HIGH — Functional Gaps

### 1. No Endpoint to Deactivate/Activate Users

**Edge Case:** The `IsActive` field exists on the `User` model, and `IsActiveMiddleware` checks it on every authenticated request. However, there is no API endpoint to toggle a user's status. The admin dashboard has no way to deactivate an employee, making the entire deactivation feature dead code.

**Where:** [AdminController.cs](backend/Controllers/AdminController.cs) — missing PATCH/PUT endpoint to update user status

**Impact:** High
- A terminated employee cannot be prevented from accessing the system. Their JWT remains valid until 12-hour expiry.
- `IsActiveMiddleware` fires immediately after deactivation, but admins have no way to trigger it.

**Recommendation:**
- Add `[HttpPut("users/{userId}/status")]` endpoint requiring Admin role.
- Accept a DTO with `IsActive: bool`.
- Return updated user status or 404 if not found.
- Frontend should add a toggle button in the employee list for activation/deactivation.

---

## MEDIUM — Functional & Data Integrity Gaps

### 2. Admin Edit Allows Future Timestamps

**Edge Case:** `EditTimeEntryAsync` validates that `ClockOut > ClockIn` but does not reject future dates. An admin can set a clock-in to `2027-03-07 10:00` and clock-out to `2027-03-08 14:00`. Reports will then sum "future hours" as if they were worked.

**Where:** [AdminService.cs](backend/Services/AdminService.cs#L86-L90)

**Impact:** Medium
- Inflated report totals when admin sets arbitrary future dates.
- Ambiguous intent: past-fixing (correcting yesterday's entry) is valid, but future-setting is not.

**Recommendation:**
- Validate: `ClockIn <= now AND ClockOut <= now` in backend before saving.
- Document: "Admins may only edit past or present times, not future times."
- Frontend edit modal should disable future date selection.

---

### 3. Admin Edit Can't Reopen an Entry (Clear ClockOut)

**Edge Case:** When editing an entry, the code only sets `ClockOut` if `request.ClockOut.HasValue`. There is no mechanism to **unset** a closed entry's `ClockOut` field back to `NULL`. If an admin accidentally closed a shift that should remain open, it cannot be undone through the UI.

**Where:** [AdminService.cs](backend/Services/AdminService.cs#L80)

```csharp
if (request.ClockOut.HasValue) entry.ClockOut = request.ClockOut.Value;
// No way to clear ClockOut if it's already set
```

**Impact:** Medium
- Incorrectly closed entries are permanently locked and can only be corrected by direct database manipulation.

**Recommendation:**
- Option A: Accept `ClockOut: null` in the request body to explicitly clear it.
- Option B: Add a dedicated `[HttpPost("attendance/{entryId}/reopen")]` endpoint.
- Validate: cannot reopen if a later entry exists for the same user (would create overlap with the unique open-entry index).

---

### 4. Admin Edit Has No Max Shift Duration Guard

**Edge Case:** The backend auto-closes entries open >16 hours on next clock-in (EDGE-CASES #6.3). However, when an admin manually edits an entry, there is no corresponding upper-bound check. An admin can create a 200-hour shift, severely skewing report totals.

**Where:** [AdminService.cs](backend/Services/AdminService.cs)

**Impact:** Medium
- Report accuracy compromised by accidental or malicious entries inflating total hours.
- Inconsistency: auto-close enforces a 16h ceiling on natural shifts, but manual edits bypass it entirely.

**Recommendation:**
- Add validation in `EditTimeEntryAsync`:
  ```csharp
  var durationHours = (entry.ClockOut.Value - entry.ClockIn).TotalHours;
  if (durationHours > 24)  // or your chosen sensible max
      throw new InvalidOperationException("Shift duration cannot exceed 24 hours.");
  ```
- Frontend should warn admins when setting duration > 12 hours (consistent with the existing "Long" badge threshold).

---

### 5. ClockIn Calls Time API Twice

**Edge Case:** `ClockInAsync` calls `GetZurichTimeAsync()` twice:
1. Once for stale-entry detection
2. Again for the new clock-in timestamp

If the time API has 2–3 second latency, the stale-entry check and the new entry timestamp are seconds apart, creating fractional drift.

**Where:** [AttendanceService.cs](backend/Services/AttendanceService.cs#L31), [line 52](backend/Services/AttendanceService.cs#L52)

**Impact:** Low-Medium
- Practically insignificant for business logic, but wasteful and creates subtle inconsistency.
- Doubles external API calls per clock-in.

**Recommendation:**
- Fetch time once at the start: `var zurichNow = await _timeService.GetZurichTimeAsync();`
- Reuse for both stale-entry check and new entry creation.

---

### 6. Mixed Timezone Conventions in Database *(extends EDGE-CASES #5.1, #9.2)*

**Edge Case:** The database mixes two timezone conventions:
- `User.CreatedAt` defaults to `DateTime.UtcNow` (UTC)
- `TimeEntry.ClockIn` and `ClockOut` store Zurich local time (no offset, implicit timezone)

Any cross-table query joining `Users` and `TimeEntries` by date is ambiguous. A report asking "all shifts after user creation date" would compare a UTC timestamp against a Zurich-local timestamp, producing wrong results (off by 1–2 hours depending on DST).

**Where:** [User.cs](backend/Models/User.cs#L11) vs [AttendanceService.cs](backend/Services/AttendanceService.cs#L56)

> **Relationship to existing issues:** EDGE-CASES #5.1 identifies that `TimeEntry` uses `DateTime` without timezone context (marked **OPEN**, HIGH). This item exposes the additional cross-table inconsistency caused by mixing UTC and local time *within the same database*. EDGE-CASES #9.2 covers the serialization contract mismatch. All three share the same root fix: standardize on `DateTimeOffset`.

**Impact:** Medium
- Cross-table date comparisons are unreliable today; any new reporting query joining these tables will silently produce wrong results.

**Recommendation:**
- Standardize all `DateTime` columns to UTC and document it explicitly.
- Consider migrating to `DateTimeOffset` to make timezone intent explicit in the schema.
- This is a prerequisite for fixing #5.1 and #9.2 completely.

---

### 7. No Pagination Metadata

**Edge Case:** History and report endpoints accept `page` and `pageSize` parameters but return only a flat list of entries — no `totalCount`, `totalPages`, or `hasNextPage`. The frontend cannot determine if more pages exist.

**Where:** [AttendanceService.cs](backend/Services/AttendanceService.cs#L97-L130), [AdminService.cs](backend/Services/AdminService.cs#L28-L68)

**Endpoints affected:**
- `GET /attendance/history?page=1&pageSize=50` → returns array, no metadata
- `GET /admin/reports/{userId}?page=1&pageSize=50` → returns `EmployeeReportDto` with only `entries: []`

**Impact:** Medium
- Frontend must guess whether more pages exist (hacky: assume page is full if result count == pageSize).
- Cannot display "Page 3 of 17" or disable the "Next" button on the last page.

**Recommendation:**
- Create a wrapper DTO:
  ```csharp
  public record PaginatedResponse<T>(
      List<T> Items,
      int CurrentPage,
      int TotalPages,
      int TotalCount,
      bool HasNextPage
  );
  ```
- Update both endpoints to return `PaginatedResponse<TimeEntryDto>`.
- Backend derives `TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)`.

---

### 8. IsActiveMiddleware Queries DB on Every Authenticated Request *(extends EDGE-CASES #6.4)*

**Edge Case:** `IsActiveMiddleware` executes a database query on **every** authenticated API request:

```csharp
var isActive = await db.Users.AnyAsync(u => u.Id == userId && u.IsActive);
```

**Where:** [IsActiveMiddleware.cs](backend/Middleware/IsActiveMiddleware.cs#L23)

> **Relationship to existing issues:** EDGE-CASES #6.4 correctly fixed the *correctness* gap (deactivated users were not blocked mid-session). This item addresses the *performance* cost that fix introduced — a full DB round-trip on every request.

**Impact:** Medium
- Under standard usage this doubles DB round-trips per request.
- At scale (many concurrent users) this becomes a bottleneck with no benefit over a short-lived cache.

**Recommendation:**
- Option A: Add `IsActive` as a custom JWT claim — zero DB cost, but requires token re-issue on status change (acceptable given 12h expiry and immediate enforcement through middleware on deactivation).
- Option B: In-memory cache with a short TTL (e.g., 30 seconds), keyed by `userId`, invalidated explicitly on deactivation.

---

## LOW — Security Hardening & Minor Issues

### 9. No HTTPS Enforcement

**Edge Case:** The backend listens on HTTP only (`http://localhost:5000`). There is no `UseHttpsRedirection()` middleware, no HSTS headers, and no certificate configuration. JWTs are transmitted in plaintext, vulnerable to MITM interception.

**Where:** [Program.cs](backend/Program.cs)

**Impact:** Low (development environment — critical in production)

**Recommendation:**
- Add `app.UseHttpsRedirection();` and `app.UseHsts();` (outside Development environment).
- Configure an SSL certificate in `appsettings.json` for production.
- For dev: `dotnet dev-certs https --trust` generates a trusted local certificate.

---

### 10. JsonDocument Not Disposed

**Edge Case:** `WorldTimeApiService.TryFetchFromApi()` creates a `JsonDocument` via `JsonDocument.Parse(json)` but never disposes it. `JsonDocument` implements `IDisposable` and pools managed memory. Under repeated calls (time sync on every clock-in), pooled allocations accumulate.

**Where:** [WorldTimeApiService.cs](backend/Services/WorldTimeApiService.cs#L57)

```csharp
var json = await response.Content.ReadAsStringAsync();
var doc = JsonDocument.Parse(json);  // ← Never disposed
```

**Impact:** Low
- Memory leak accumulates over days of operation.
- Negligible in short-lived dev environments.

**Recommendation:**
```csharp
using var doc = JsonDocument.Parse(json);
// doc automatically disposed at end of block
```

---

### 11. Password Minimum Too Weak *(extends EDGE-CASES #4.1)*

**Edge Case:** Password validation requires a minimum of 6 characters (set when fixing EDGE-CASES #4.1). OWASP recommends at least 8 characters with complexity. Passwords like `"password"` or `"123456"` are accepted.

**Where:** [AuthService.cs](backend/Services/AuthService.cs#L44)

```csharp
if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
    throw new InvalidOperationException("Password must be at least 6 characters.");
```

> **Relationship to existing issues:** EDGE-CASES #4.1 fixed the validation crash (empty/whitespace passwords). The minimum threshold of 6 remains below OWASP recommendations, especially given the rate limiter allows ~2,880 guesses per IP per day.

**Impact:** Low
- Weak passwords raise brute-force risk in a credential-stuffing scenario.

**Recommendation:**
- Increase minimum to 8: `request.Password.Length < 8`
- Optionally enforce complexity:
  ```csharp
  var hasUpper = request.Password.Any(char.IsUpper);
  var hasDigit = request.Password.Any(char.IsDigit);
  if (!hasUpper || !hasDigit)
      throw new InvalidOperationException("Password must contain at least one uppercase letter and one digit.");
  ```
- Frontend should display a password strength meter.

---

### 12. Seed Logic Re-Triggers If Admin Role Changes

**Edge Case:** The seed condition checks `!db.Users.Any(u => u.Role == "Admin")`. If the only admin's role is changed to `"Employee"` (by a compromised account or direct DB access), the next app restart seeds a new default-credential admin (`admin` / `admin123`), bypassing the env-var credential fix from EDGE-CASES #2.1.

**Where:** [Program.cs](backend/Program.cs#L96)

```csharp
if (!db.Users.Any(u => u.Role == "Admin"))  // ← Triggers whenever no Admin role exists
{
    // Seeds default admin ...
}
```

**Impact:** Low
- Requires either deliberate role downgrade or a compromised database — unlikely in practice.
- However, it silently undermines the security fix in EDGE-CASES #2.1.

**Recommendation:**
- Seed by username, not by role:
  ```csharp
  if (!db.Users.Any(u => u.Username == seedUsername))
  {
      // Seed only if this specific account doesn't exist
  }
  ```
- Log successful seeding to an audit table for detectability.

---

## Summary Table

| # | Category | Severity | Area | Fix Complexity |
|---|----------|----------|------|----------------|
| 1 | Functional | **HIGH** | API missing deactivate endpoint | Medium |
| 2 | Functional | **MEDIUM** | Future timestamps allowed in admin edit | Low |
| 3 | Functional | **MEDIUM** | Can't reopen a closed entry | Medium |
| 4 | Functional | **MEDIUM** | No max shift duration guard in admin edit | Low |
| 5 | Performance | **MEDIUM** | Double time API call in ClockIn | Low |
| 6 | Data Integrity | **MEDIUM** | Mixed UTC/local timezone conventions in DB | High |
| 7 | UX | **MEDIUM** | No pagination metadata | Low |
| 8 | Performance | **MEDIUM** | DB query on every authenticated request | Medium |
| 9 | Security | **LOW** | No HTTPS enforcement (dev-only issue) | High |
| 10 | Memory | **LOW** | JsonDocument not disposed | Low |
| 11 | Security | **LOW** | Password minimum below OWASP threshold | Low |
| 12 | Security | **LOW** | Seed re-triggers if admin role is changed | Low |

---

## Quick-Win Fixes (Low Effort, High Value)

- **#10:** Wrap `JsonDocument.Parse()` in `using` — 2 min
- **#5:** Cache time API result at start of `ClockInAsync` — 5 min
- **#2:** Add `ClockIn <= now AND ClockOut <= now` validation — 5 min
- **#4:** Add max shift duration (24h) check in `EditTimeEntryAsync` — 5 min
- **#11:** Increase password min to 8 chars, add complexity check — 10 min
- **#12:** Change seed condition to check by username — 5 min

---

## Medium-Effort Fixes

- **#1:** Add `PUT /users/{id}/status` endpoint + UI toggle button — 30 min
- **#3:** Support reopening entries (separate endpoint or nullable ClockOut) — 20 min
- **#7:** Add `PaginatedResponse<T>` wrapper DTO to both endpoints — 15 min
- **#8:** Add in-memory cache (30s TTL) to `IsActiveMiddleware` — 20 min

---

## High-Effort / Strategic Fixes

- **#6:** Audit and standardize all timestamps to UTC across DB and code — 1–2 hours (prerequisite for fully resolving EDGE-CASES #5.1 and #9.2)
- **#9:** HTTPS setup (certificate, IIS/Kestrel config) — 1+ hour (production only)
